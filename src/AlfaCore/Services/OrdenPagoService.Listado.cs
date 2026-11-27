using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Listado de pagos a proveedores (como Cobranzas): pendientes de todos los proveedores, tarjetas y saldo final.
public sealed partial class OrdenPagoService
{
    /// <summary>
    /// Réplica de dbo.CO_CPTES_IMPAGOS (comprobantes de proveedores con saldo) que calcula las aplicaciones solo de cuentas
    /// de proveedores (TipoVista PR) y, con @proveedor, solo de ese proveedor. La vista del legacy suma las aplicaciones de
    /// toda la base (VE_SALDOAPLICACION, también clientes) y en LANUEVA tarda ~7 s; esta ~1,7 s con el mismo resultado
    /// (OrdenPagoImpagosSqlTests la compara con la vista). Las vistas del legacy no se tocan.
    /// </summary>
    internal const string ImpagosProveedoresSql = """
        SELECT a.CUENTA,a.FECHA,a.TC,a.SUCURSAL,a.NUMERO,a.LETRA,a.[DEBE-HABER],a.IMPORTE,a.MONEDA,a.VENCIMIENTO,a.CABNOMBRE,
            a.IMPORTE-ISNULL(ap.IMPORTE,0) SALDO,a.Estado,a.UNEGOCIO
        FROM dbo.MV_ASIENTOS a JOIN dbo.MA_CUENTAS c ON a.CUENTA=c.CODIGO
        LEFT JOIN (
            SELECT ap.CUENTA,ap.TCO_ORIGEN TC,ap.SUCURSAL_ORIGEN SUCURSAL,ap.NUMERO_ORIGEN NUMERO,ap.LETRA_ORIGEN LETRA,
                SUM(ap.IMPORTE) IMPORTE,a2.IMPORTE TOTAL,a2.FECHA,a2.IdVendedor
            FROM dbo.MV_APLICACION ap JOIN dbo.MV_ASIENTOS a2 ON ap.CUENTA=a2.CUENTA AND ap.TCO_ORIGEN=a2.TC
                AND ap.SUCURSAL_ORIGEN=a2.SUCURSAL AND ap.NUMERO_ORIGEN=a2.NUMERO AND ap.LETRA_ORIGEN=a2.LETRA
            WHERE ap.TC<>'VJ' AND ap.CUENTA IN (SELECT CODIGO FROM dbo.MA_CUENTAS WHERE TipoVista=N'PR')
                AND (@proveedor='' OR LTRIM(RTRIM(ap.CUENTA))=@proveedor)
            GROUP BY ap.CUENTA,ap.TCO_ORIGEN,ap.SUCURSAL_ORIGEN,ap.NUMERO_ORIGEN,ap.LETRA_ORIGEN,a2.IMPORTE,a2.FECHA,a2.IdVendedor
            UNION
            SELECT ap.CUENTA,ap.TC,ap.SUCURSAL,ap.NUMERO,ap.LETRA,SUM(ap.IMPORTE),a2.IMPORTE,a2.FECHA,a2.IdVendedor
            FROM dbo.MV_APLICACION ap JOIN dbo.MV_ASIENTOS a2 ON ap.CUENTA=a2.CUENTA AND ap.TC=a2.TC
                AND ap.SUCURSAL=a2.SUCURSAL AND ap.NUMERO=a2.NUMERO AND ap.LETRA=a2.LETRA
            WHERE ap.TC<>'VJ' AND ap.TCO_ORIGEN NOT IN ('OT','PR','RM','RMC','NP','OC')
                AND ap.CUENTA IN (SELECT CODIGO FROM dbo.MA_CUENTAS WHERE TipoVista=N'PR')
                AND (@proveedor='' OR LTRIM(RTRIM(ap.CUENTA))=@proveedor)
            GROUP BY ap.CUENTA,ap.TC,ap.SUCURSAL,ap.NUMERO,ap.LETRA,a2.IMPORTE,a2.FECHA,a2.IdVendedor
        ) ap ON a.CUENTA=ap.CUENTA AND a.TC=ap.TC AND a.SUCURSAL=ap.SUCURSAL AND a.NUMERO=ap.NUMERO AND a.LETRA=ap.LETRA
        WHERE c.TipoVista=N'PR' AND a.[DEBE-HABER]=N'H' AND a.IMPORTE-ISNULL(ap.IMPORTE,0)>0 AND (a.EsResumen IS NULL OR a.EsResumen=0)
            AND (@proveedor='' OR LTRIM(RTRIM(a.CUENTA))=@proveedor)
        """;

    /// <summary>
    /// Comprobantes pendientes de pago de todos los proveedores, uno por fila. Respeta las mismas reglas que la orden:
    /// unidad del usuario si CHEQUESXUNEGOCIO, sin proformas si el usuario no las ve, anuladas afuera y motivo de bloqueo.
    /// </summary>
    public Task<IReadOnlyList<OrdenPagoPendiente>> PendientesAsync(CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<OrdenPagoPendiente>>("Pendientes", async (cn, user, session, token) =>
        {
            var unidad = UnidadDe(user);
            var filtraUnidad = (await ConfigAsync(cn, null, "CHEQUESXUNEGOCIO", token)).Equals("SI", StringComparison.OrdinalIgnoreCase);
            var verProforma = user.VerProforma
                && (await ConfigAsync(cn, null, "USAFACTURAPROFORMA", token)).Equals("SI", StringComparison.OrdinalIgnoreCase);
            var filas = (await cn.QueryAsync<OrdenPagoPendiente>(Cmd($"""
                SELECT LTRIM(RTRIM(v.CUENTA)) Proveedor,LTRIM(RTRIM(v.TC)) Tc,v.SUCURSAL Sucursal,v.NUMERO Numero,v.LETRA Letra,
                    MIN(v.FECHA) Fecha,MIN(v.VENCIMIENTO) Vencimiento,CONVERT(decimal(18,2),SUM(v.SALDO)) Saldo,
                    LTRIM(RTRIM(MAX(v.UNEGOCIO))) Unidad,LTRIM(RTRIM(MAX(v.MONEDA))) Moneda,
                    CONVERT(bit,MAX(CASE WHEN ISNULL(v.Estado,0)=1 THEN 1 ELSE 0 END)) Bloqueado,
                    MAX(LTRIM(RTRIM(ISNULL(v.CABNOMBRE,'')))) Nombre
                FROM ({ImpagosProveedoresSql}) v
                WHERE (@unidad='' OR LTRIM(RTRIM(v.UNEGOCIO))=@unidad)
                    AND NOT EXISTS(SELECT 1 FROM dbo.V_MV_CPTE c WHERE c.TC=v.TC AND c.SUCURSAL=v.SUCURSAL AND c.NUMERO=v.NUMERO AND c.LETRA=v.LETRA
                        AND c.IDCOMPLEMENTO=0 AND ISNULL(c.ANULADA,0)=1)
                GROUP BY LTRIM(RTRIM(v.CUENTA)),LTRIM(RTRIM(v.TC)),v.SUCURSAL,v.NUMERO,v.LETRA
                HAVING CONVERT(decimal(18,2),SUM(v.SALDO))>0;
                """, new { proveedor = "", unidad = filtraUnidad ? unidad : "" }, null, token))).AsList();
            // Nombre y CUIT del maestro (la tabla es chica: se lee una vez y se cruza en memoria).
            var maestro = (await cn.QueryAsync<OrdenPagoProveedor>(Cmd("""
                SELECT LTRIM(RTRIM(CODIGO)) Codigo,LTRIM(RTRIM(ISNULL(RAZON_SOCIAL,''))) Nombre,LTRIM(RTRIM(ISNULL(NUMERO_DOCUMENTO,''))) Documento
                FROM dbo.Vt_Proveedores;
                """, null, null, token))).GroupBy(p => p.Codigo).ToDictionary(g => g.Key, g => g.First());
            var resultado = new List<OrdenPagoPendiente>(filas.Count);
            foreach (var f in filas)
            {
                f.Proforma = EsDebitoProforma(f.Tc);
                if (f.Proforma && !verProforma) continue;
                if (maestro.TryGetValue(f.Proveedor, out var p)) { if (p.Nombre.Length > 0) f.Nombre = p.Nombre; f.Cuit = p.Documento; }
                f.Motivo = f.Moneda is not ("" or "1") ? "Moneda extranjera: se paga en una próxima versión."
                    : f.Bloqueado ? "Comprobante bloqueado para su pago." : "";
                resultado.Add(f);
            }
            VerificarSesion(session);
            return resultado;
        }, ct);

    /// <summary>Tarjeta «Pagado este mes»: órdenes de pago y pagos proforma vigentes del mes y de hoy.</summary>
    public Task<OrdenPagoIndicadores> IndicadoresAsync(CancellationToken ct = default)
        => Ejecutar("Indicadores", async (cn, user, session, token) =>
        {
            var unidad = UnidadDe(user);
            var filtraUnidad = (await ConfigAsync(cn, null, "CHEQUESXUNEGOCIO", token)).Equals("SI", StringComparison.OrdinalIgnoreCase);
            var hoy = DateTime.Today;
            var r = await cn.QuerySingleAsync<OrdenPagoIndicadores>(Cmd("""
                SELECT CONVERT(decimal(18,2),ISNULL(SUM(IMPORTE),0)) PagadoMes,COUNT(*) OrdenesMes,
                    CONVERT(decimal(18,2),ISNULL(SUM(CASE WHEN FECHA>=@hoy THEN IMPORTE END),0)) PagadoHoy,
                    ISNULL(SUM(CASE WHEN FECHA>=@hoy THEN 1 ELSE 0 END),0) OrdenesHoy
                FROM dbo.V_MV_CPTE
                WHERE TC IN (@opg,@pgfp) AND FECHA>=@mes AND FECHA<@manana AND IDCOMPLEMENTO=0 AND ISNULL(ANULADA,0)=0
                    AND (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad);
                """, new { hoy, mes = new DateTime(hoy.Year, hoy.Month, 1), manana = hoy.AddDays(1), opg = TcOrdenPago, pgfp = TcProforma,
                    unidad = filtraUnidad ? unidad : "" }, null, token));
            VerificarSesion(session);
            return r;
        }, ct);

    /// <summary>
    /// Saldo final de cada proveedor (lo que se le debe: haber menos debe de su cuenta corriente, con pagos, anticipos y notas
    /// de crédito). Se pide solo al ver la lista «Solo proveedores».
    /// </summary>
    public Task<IReadOnlyList<OrdenPagoSaldoProveedor>> SaldosProveedoresAsync(CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<OrdenPagoSaldoProveedor>>("SaldosProveedores", async (cn, user, session, token) =>
        {
            var unidad = UnidadDe(user);
            var filtraUnidad = (await ConfigAsync(cn, null, "CHEQUESXUNEGOCIO", token)).Equals("SI", StringComparison.OrdinalIgnoreCase);
            var filas = (await cn.QueryAsync<OrdenPagoSaldoProveedor>(Cmd("""
                SELECT LTRIM(RTRIM(a.CUENTA)) Proveedor,
                    CONVERT(decimal(18,2),SUM(CASE WHEN a.[DEBE-HABER]='H' THEN a.IMPORTE WHEN a.[DEBE-HABER]='D' THEN -a.IMPORTE ELSE 0 END)) Saldo
                FROM dbo.MV_ASIENTOS a JOIN dbo.MA_CUENTAS c ON c.CODIGO=a.CUENTA AND c.TipoVista=N'PR'
                WHERE ISNULL(a.EsResumen,0)=0 AND (@unidad='' OR LTRIM(RTRIM(a.UNEGOCIO))=@unidad)
                GROUP BY LTRIM(RTRIM(a.CUENTA));
                """, new { unidad = filtraUnidad ? unidad : "" }, null, token))).AsList();
            VerificarSesion(session);
            return filas;
        }, ct);
}

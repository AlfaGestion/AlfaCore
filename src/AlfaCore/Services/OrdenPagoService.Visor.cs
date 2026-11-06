using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Visor de órdenes de pago (OPG) y pagos proforma (PGFP) ya cargados: listado y detalle con historial y trazabilidad.
public sealed partial class OrdenPagoService
{
    private static string Patron(string texto) => "%" + texto.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    /// <summary>tc: "" (todas), "OPG" o "PGFP". estado: "" (todas), "ACTIVAS" o "ANULADAS". Un usuario sin acceso a todas las unidades ve solo la suya.</summary>
    public Task<IReadOnlyList<OrdenPagoFila>> ConsultarAsync(DateTime desde, DateTime hasta, string texto, string tc, string estado, CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<OrdenPagoFila>>("Consultar", async (cn, user, session, token) =>
        {
            texto = (texto ?? string.Empty).Trim(); tc = (tc ?? string.Empty).Trim().ToUpperInvariant(); estado = (estado ?? string.Empty).Trim().ToUpperInvariant();
            if (desde.Year < 1900 || hasta < desde || hasta.Year > 9998 || texto.Length > 100 || tc is not ("" or TcOrdenPago or TcProforma) || estado is not ("" or "ACTIVAS" or "ANULADAS"))
                throw new InvalidOperationException("Revisá las fechas y los filtros.");
            var filas = (await cn.QueryAsync<OrdenPagoFila>(Cmd("""
                SELECT TOP(501) c.ID Id, LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(c.IDCOMPROBANTE)) Comprobante, c.FECHA Fecha,
                    LTRIM(RTRIM(ISNULL(c.CUENTA,''))) Cuenta, LTRIM(RTRIM(ISNULL(c.NOMBRE,''))) Nombre, LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) Unidad,
                    CONVERT(decimal(18,2),ISNULL(c.IMPORTE,0)) Importe, CONVERT(bit,ISNULL(c.ANULADA,0)) Anulada,
                    CONVERT(bit,CASE WHEN LTRIM(RTRIM(c.TC))='OPG' AND ISNULL(c.FINALIZADA,0)=0 THEN 1 ELSE 0 END) Pendiente,
                    CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.ALFACORE_PAGO_OPERACION o WHERE o.Comprobante=c.IDCOMPROBANTE AND LTRIM(RTRIM(o.Tc))=LTRIM(RTRIM(c.TC))) THEN 1 ELSE 0 END) Web
                FROM dbo.V_MV_CPTE c
                WHERE LTRIM(RTRIM(c.TC)) IN ('OPG','PGFP') AND (@tc='' OR LTRIM(RTRIM(c.TC))=@tc) AND ISNULL(c.IDCOMPLEMENTO,0)=0
                    AND c.FECHA>=@desde AND c.FECHA<@fin
                    AND (@estado='' OR (@estado='ANULADAS' AND ISNULL(c.ANULADA,0)=1) OR (@estado='ACTIVAS' AND ISNULL(c.ANULADA,0)=0))
                    AND (@admin=1 OR LTRIM(RTRIM(c.UNEGOCIO))=@unidad)
                    AND (@texto='' OR c.NOMBRE LIKE @patron ESCAPE '~' OR c.CUENTA LIKE @patron ESCAPE '~' OR c.IDCOMPROBANTE LIKE @patron ESCAPE '~')
                ORDER BY c.FECHA DESC, c.ID DESC;
                """, new { desde = desde.Date, fin = hasta.Date.AddDays(1), tc, estado, admin = user.Admin, unidad = user.Unidad.Trim(), texto, patron = Patron(texto) }, null, token))).AsList();
            await CargarUltimosEnviosAsync(cn, filas, token);
            VerificarSesion(session);
            return filas;
        }, ct);

    /// <summary>Último mail enviado de cada orden, en una sola consulta para todo el listado (con la red, una por fila tardaría minutos).</summary>
    private static async Task CargarUltimosEnviosAsync(SqlConnection cn, List<OrdenPagoFila> filas, CancellationToken ct)
    {
        if (filas.Count == 0) return;
        var envios = (await cn.QueryAsync<(string Tc, string Id, DateTime Fecha, string Para, string Status)>(Cmd("""
            IF OBJECT_ID(N'dbo.V_MA_EMAILMOV', N'U') IS NULL SELECT TOP 0 N'' Tc, N'' Id, CAST(NULL AS datetime) Fecha, N'' Para, N'' Status
            ELSE SELECT LTRIM(RTRIM(TC)) Tc, LTRIM(RTRIM(IdComprobante)) Id, FechaHora Fecha, ISNULL(Para, N'') Para, ISNULL(Status, N'') Status
                FROM dbo.V_MA_EMAILMOV WHERE LTRIM(RTRIM(TC)) IN (N'OPG', N'PGFP') AND LTRIM(RTRIM(IdComprobante)) IN @ids AND ISNULL(Tipo, 'E') = 'E';
            """, new { ids = filas.Select(f => f.Comprobante).Distinct().ToArray() }, null, ct))).ToList();
        var ultimo = envios.GroupBy(e => (e.Tc, e.Id)).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Fecha).First());
        foreach (var f in filas)
            if (ultimo.TryGetValue((f.Tc, f.Comprobante), out var e)) f.UltimoEnvio = $"{e.Fecha:dd/MM/yyyy HH:mm} · {e.Para} · {e.Status}";
    }

    private sealed class CabeceraDetalle
    {
        public int Id { get; set; } public string Tc { get; set; } = ""; public string Comprobante { get; set; } = ""; public string Sucursal { get; set; } = ""; public string Numero { get; set; } = "";
        public string Letra { get; set; } = ""; public DateTime Fecha { get; set; } public string Cuenta { get; set; } = ""; public string Nombre { get; set; } = "";
        public string Unidad { get; set; } = ""; public decimal Importe { get; set; } public bool Anulada { get; set; } public bool Finalizada { get; set; }
        public string Documento { get; set; } = ""; public string Usuario { get; set; } = ""; public string Comentarios { get; set; } = ""; public DateTime? Grabada { get; set; }
        public bool Web { get; set; }
        public decimal Retencion { get; set; } public string RetencionNro { get; set; } = ""; public decimal RetencionBase { get; set; }
    }

    private sealed class AplicacionFila
    {
        public string PagadorTc { get; set; } = ""; public string PagadorId { get; set; } = ""; public string Tc { get; set; } = ""; public string Comprobante { get; set; } = "";
        public decimal Importe { get; set; } public DateTime? Fecha { get; set; }
    }

    private sealed class UsoFila
    {
        public string Tc { get; set; } = ""; public string Comprobante { get; set; } = ""; public string OrdenTc { get; set; } = ""; public string OrdenId { get; set; } = ""; public decimal Importe { get; set; }
    }

    public Task<OrdenPagoDetalle?> DetalleAsync(string tc, string idComprobante, CancellationToken ct = default)
        => Ejecutar<OrdenPagoDetalle?>("Detalle", async (cn, user, session, token) =>
        {
            tc = (tc ?? string.Empty).Trim().ToUpperInvariant(); idComprobante = (idComprobante ?? string.Empty).Trim();
            if (tc is not (TcOrdenPago or TcProforma) || idComprobante.Length != 13) return null;
            var h = await cn.QueryFirstOrDefaultAsync<CabeceraDetalle>(Cmd("""
                SELECT c.ID Id, LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(c.IDCOMPROBANTE)) Comprobante, c.SUCURSAL Sucursal, c.NUMERO Numero, c.LETRA Letra, c.FECHA Fecha,
                    LTRIM(RTRIM(ISNULL(c.CUENTA,''))) Cuenta, LTRIM(RTRIM(ISNULL(c.NOMBRE,''))) Nombre, LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) Unidad,
                    CONVERT(decimal(18,2),ISNULL(c.IMPORTE,0)) Importe, CONVERT(bit,ISNULL(c.ANULADA,0)) Anulada, CONVERT(bit,ISNULL(c.FINALIZADA,0)) Finalizada,
                    LTRIM(RTRIM(ISNULL(c.DOCUMENTONUMERO,''))) Documento, LTRIM(RTRIM(ISNULL(c.USUARIO,''))) Usuario, LTRIM(RTRIM(ISNULL(c.COMENTARIOS,''))) Comentarios,
                    c.FechaHora_Grabacion Grabada, CONVERT(decimal(18,2),ISNULL(c.RETGAN_Importe,0)) Retencion, LTRIM(RTRIM(ISNULL(c.RETGAN_NRO,''))) RetencionNro,
                    CONVERT(decimal(18,2),ISNULL(c.RETGAN_BaseImponible,0)) RetencionBase,
                    CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.ALFACORE_PAGO_OPERACION o WHERE o.Comprobante=c.IDCOMPROBANTE AND LTRIM(RTRIM(o.Tc))=LTRIM(RTRIM(c.TC))) THEN 1 ELSE 0 END) Web
                FROM dbo.V_MV_CPTE c WHERE LTRIM(RTRIM(c.TC))=@tc AND c.IDCOMPROBANTE=@id AND ISNULL(c.IDCOMPLEMENTO,0)=0;
                """, new { tc, id = idComprobante }, null, token));
            if (h is null) return null;
            if (!user.Admin && h.Unidad != user.Unidad.Trim()) throw new InvalidOperationException("La orden de pago es de otra unidad de negocio.");

            var detalle = new OrdenPagoDetalle
            {
                Cabecera = new OrdenPagoFila { Id = h.Id, Tc = h.Tc, Comprobante = h.Comprobante, Fecha = h.Fecha, Cuenta = h.Cuenta, Nombre = h.Nombre, Unidad = h.Unidad,
                    Importe = h.Importe, Anulada = h.Anulada, Pendiente = h.Tc == TcOrdenPago && !h.Finalizada, Web = h.Web },
                Documento = h.Documento, Usuario = h.Usuario, Comentarios = h.Comentarios, Grabada = h.Grabada,
                RetencionGanancias = h.Retencion, RetencionCertificado = h.RetencionNro, RetencionBase = h.RetencionBase
            };

            // Qué canceló: la propia orden y, a través de ella, los créditos aplicados (NC, anticipos).
            var aplicaciones = (await cn.QueryAsync<AplicacionFila>(Cmd("""
                SELECT LTRIM(RTRIM(ap.TC)) PagadorTc, ap.IDCOMPROBANTE PagadorId, LTRIM(RTRIM(ap.TCO_ORIGEN)) Tc, ap.IDComprobante_Origen Comprobante, ap.IMPORTE Importe, f.FECHA Fecha
                FROM dbo.MV_APLICACION ap
                OUTER APPLY (SELECT TOP (1) c.FECHA FROM dbo.C_MV_CPTE c WHERE c.TC=ap.TCO_ORIGEN AND c.IDCOMPROBANTE=ap.IDComprobante_Origen) f
                WHERE (LTRIM(RTRIM(ap.TC_PRINT))=@tc AND ap.IDCOMPROBANTE_PRINT=@id) OR (LTRIM(RTRIM(ap.TC))=@tc AND ap.IDCOMPROBANTE=@id)
                ORDER BY f.FECHA, ap.IDComprobante_Origen;
                """, new { tc, id = idComprobante }, null, token))).AsList();
            bool EsLaOrden(AplicacionFila a) => a.PagadorTc == tc && a.PagadorId == idComprobante;
            detalle.Cancelados = [.. aplicaciones.GroupBy(a => (a.Tc, a.Comprobante)).Select(g => new OrdenPagoCancelado
            {
                Tc = g.Key.Tc, Comprobante = g.Key.Comprobante, Fecha = g.Min(a => a.Fecha), PorOrden = g.Where(EsLaOrden).Sum(a => a.Importe), PorCreditos = g.Where(a => !EsLaOrden(a)).Sum(a => a.Importe)
            })];
            detalle.CreditosUsados = [.. aplicaciones.Where(a => !EsLaOrden(a)).GroupBy(a => (a.PagadorTc, a.PagadorId))
                .Select(g => new OrdenPagoRelacion { Tc = g.Key.PagadorTc, Comprobante = g.Key.PagadorId, Importe = g.Sum(a => a.Importe) })];

            // Lo que quedó a favor del proveedor y dónde se usó después.
            detalle.SaldoAFavor = await cn.ExecuteScalarAsync<decimal>(Cmd("""
                SELECT CONVERT(decimal(18,2),ISNULL(SUM(SALDO),0)) FROM dbo.CO_CPTES_CREDITOS_PENDIENTES
                WHERE LTRIM(RTRIM(TC))=@tc AND SUCURSAL=@suc AND NUMERO=@num AND LETRA=@letra;
                """, new { tc, suc = h.Sucursal, num = h.Numero, letra = h.Letra }, null, token));
            detalle.UsosDelSaldo = [.. (await cn.QueryAsync<UsoFila>(Cmd("""
                SELECT LTRIM(RTRIM(ap.TCO_ORIGEN)) Tc, ap.IDComprobante_Origen Comprobante, LTRIM(RTRIM(ISNULL(ap.TC_PRINT,''))) OrdenTc, ISNULL(ap.IDCOMPROBANTE_PRINT,'') OrdenId, ap.IMPORTE Importe
                FROM dbo.MV_APLICACION ap
                WHERE LTRIM(RTRIM(ap.TC))=@tc AND ap.IDCOMPROBANTE=@id AND NOT (LTRIM(RTRIM(ISNULL(ap.TC_PRINT,'')))=@tc AND ISNULL(ap.IDCOMPROBANTE_PRINT,'')=@id)
                ORDER BY ap.IDCOMPROBANTE_PRINT;
                """, new { tc, id = idComprobante }, null, token))).Select(u => new OrdenPagoRelacion { Tc = u.Tc, Comprobante = u.Comprobante, Orden = $"{u.OrdenTc} {u.OrdenId}".Trim(), Importe = u.Importe })];

            // Asiento y medios de pago (líneas al Haber).
            detalle.Asiento = (await cn.QueryAsync<OrdenPagoAsientoLinea>(Cmd("""
                SELECT ISNULL(a.[NUMERO ASIENTO],0) Asiento, ISNULL(a.SECUENCIA,0) Secuencia, LTRIM(RTRIM(a.CUENTA)) Cuenta,
                    ISNULL(NULLIF(LTRIM(RTRIM(c.DESCRIPCION)),''),'') Descripcion, a.[DEBE-HABER] DebeHaber, CONVERT(decimal(18,2),a.IMPORTE) Importe
                FROM dbo.MV_ASIENTOS a LEFT JOIN dbo.MA_CUENTAS c ON c.CODIGO=a.CUENTA
                WHERE LTRIM(RTRIM(a.TC))=@tc AND a.SUCURSAL=@suc AND a.NUMERO=@num AND a.LETRA=@letra
                ORDER BY ISNULL(a.[NUMERO ASIENTO],0), ISNULL(a.SECUENCIA,0);
                """, new { tc, suc = h.Sucursal, num = h.Numero, letra = h.Letra }, null, token))).AsList();
            detalle.Medios = (await cn.QueryAsync<OrdenPagoMedioDetalle>(Cmd($"""
                SELECT LTRIM(RTRIM(a.CUENTA)) Cuenta, ISNULL(NULLIF(LTRIM(RTRIM(c.DESCRIPCION)),''),LTRIM(RTRIM(a.CUENTA))) Descripcion, CONVERT(decimal(18,2),a.IMPORTE) Importe,
                    ISNULL(LTRIM(RTRIM(a.NroComprobanteBancario)),'') Cheque, CASE WHEN ISNULL(a.NroComprobanteBancario,'')<>'' THEN a.VENCIMIENTO END Vencimiento
                FROM dbo.MV_ASIENTOS a LEFT JOIN dbo.MA_CUENTAS c ON c.CODIGO=a.CUENTA
                WHERE LTRIM(RTRIM(a.TC))=@tc AND a.SUCURSAL=@suc AND a.NUMERO=@num AND a.LETRA=@letra AND a.[DEBE-HABER]='H'
                    AND 0={SqlLineaEsRetencion}
                ORDER BY ISNULL(a.[NUMERO ASIENTO],0), ISNULL(a.SECUENCIA,0);
                """, new { tc, suc = h.Sucursal, num = h.Numero, letra = h.Letra, ret = h.Retencion }, null, token))).AsList();

            // Historial: acciones del comprobante (alta, modificación, baja, impresiones) y mails enviados.
            var eventos = (await cn.QueryAsync<OrdenPagoEvento>(Cmd("""
                SELECT a.FECHAHORA Fecha, LTRIM(RTRIM(ISNULL(a.TIPO_ACCION,''))) Tipo, ISNULL(a.USUARIO,'') Usuario, ISNULL(a.PC,'') Pc,
                    LTRIM(RTRIM(ISNULL(a.SYSTEMUSER,''))) + CASE WHEN ISNULL(a.Proceso,'')<>'' THEN CASE WHEN ISNULL(a.SYSTEMUSER,'')<>'' THEN ' · ' ELSE '' END + a.Proceso ELSE '' END Detalle
                FROM dbo.V_MV_CpteAcciones a WHERE LTRIM(RTRIM(a.TC))=@tc AND a.IDCOMPROBANTE=@id;
                """, new { tc, id = idComprobante }, null, token))).AsList();
            eventos.ForEach(e => e.Tipo = ComprobanteAcciones.Nombre(e.Tipo));
            eventos.AddRange(await cn.QueryAsync<OrdenPagoEvento>(Cmd("""
                IF OBJECT_ID(N'dbo.V_MA_EMAILMOV', N'U') IS NULL SELECT TOP 0 CAST(NULL AS datetime) Fecha, N'' Tipo, N'' Usuario, N'' Pc, N'' Detalle
                ELSE SELECT FechaHora Fecha, N'Mail' Tipo, ISNULL(Usuario,N'') Usuario, N'' Pc, N'Enviado a ' + ISNULL(Para,N'') + CASE WHEN ISNULL(Status,N'')<>N'' THEN N' · ' + Status ELSE N'' END Detalle
                FROM dbo.V_MA_EMAILMOV WHERE LTRIM(RTRIM(TC))=@tc AND LTRIM(RTRIM(IdComprobante))=@id AND ISNULL(Tipo,'E')='E';
                """, new { tc, id = idComprobante }, null, token)));
            detalle.Historial = [.. eventos.OrderByDescending(e => e.Fecha)];
            VerificarSesion(session);
            return detalle;
        }, ct);
}

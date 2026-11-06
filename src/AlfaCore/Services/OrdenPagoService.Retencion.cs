using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Retención de Ganancias de la orden de pago (v2). Misma mecánica que FrmMvPagos.CalculaRetenciones del legacy
// (acumulativa por mes y por CUIT, mínimo no imponible, escala, mínimo excluido), con una diferencia deliberada:
// la base de cada factura es proporcional a lo que se paga de ella (neto × pagado / total). El anticipo sin factura
// y la prioridad del pago real en medios se tratan como en el legacy.
public sealed partial class OrdenPagoService
{
    /// <summary>Marca (0/1) la línea al Haber del asiento que es la retención de Ganancias de la orden (necesita el parámetro @ret).</summary>
    internal const string SqlLineaEsRetencion = """
        CASE WHEN @ret>0 AND a.IMPORTE=@ret AND LTRIM(RTRIM(a.CUENTA)) IN (
            SELECT LTRIM(RTRIM(r.CUENTA)) FROM dbo.V_TA_RETENCIONES r WHERE LTRIM(RTRIM(ISNULL(r.CUENTA,'')))<>''
            UNION SELECT LTRIM(RTRIM(i.PG_RETGAN)) FROM dbo.TA_CUENTASIVA i WHERE LTRIM(RTRIM(ISNULL(i.PG_RETGAN,'')))<>'') THEN 1 ELSE 0 END
        """;

    internal sealed record EscalaRetencion(decimal Desde, decimal Hasta, decimal Importe, decimal Porcentaje, decimal SobreExcedente);

    internal sealed record RetencionEntrada(
        string Regimen, decimal BaseFacturas, decimal NoGravado, decimal Debitos, decimal PagoReal, decimal DescuentoCreditos,
        decimal BaseAnterior, decimal RetenidoAntes, decimal MinimoNoImponible, decimal MinimoExcluido, decimal Alicuota, IReadOnlyList<EscalaRetencion> Escala);

    private static decimal Redondeo(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>Regla pura del cálculo (sin base de datos).</summary>
    internal static OrdenPagoRetencion CalcularRetencion(RetencionEntrada e)
    {
        var baseGrav = e.BaseFacturas; var noGrav = e.NoGravado; var descuento = e.DescuentoCreditos;
        // Anticipo sin factura (o facturas sin base): se retiene sobre el pago real.
        if (baseGrav + noGrav == 0) baseGrav = e.PagoReal;
        // Los créditos aplicados bajan la base, pero nunca por debajo del pago real en medios.
        if (e.Debitos < e.PagoReal) { baseGrav = e.PagoReal; descuento = 0; }
        else if (e.Debitos - descuento < e.PagoReal) descuento = e.Debitos - e.PagoReal;
        if (descuento > baseGrav) { baseGrav = Math.Min(descuento, e.PagoReal); }
        else baseGrav -= descuento;
        baseGrav = Redondeo(baseGrav); noGrav = Redondeo(noGrav);
        if (baseGrav == 0 && noGrav == 0) return OrdenPagoRetencion.NoAplica("No hay base imponible para retener.");

        var aportaEsta = baseGrav + noGrav;
        var acumulada = e.BaseAnterior + aportaEsta;
        if (e.RetenidoAntes == 0 && e.MinimoNoImponible > acumulada)
            return new(true, "", e.Regimen, e.Alicuota, aportaEsta, e.BaseAnterior, e.MinimoNoImponible, 0, 0, e.RetenidoAntes, 0);

        var imponible = acumulada - e.MinimoNoImponible;
        var retencion = e.Alicuota > 0 ? imponible * e.Alicuota / 100 : 0m;
        foreach (var tramo in e.Escala.Where(t => imponible >= t.Desde && imponible <= t.Hasta))
        {
            retencion += tramo.Importe;
            if (tramo.Porcentaje > 0) retencion += (imponible - tramo.SobreExcedente) * tramo.Porcentaje / 100;
        }
        var importe = Redondeo(Redondeo(retencion) - e.RetenidoAntes);
        if (importe < Math.Max(e.MinimoExcluido, 0)) importe = 0;
        return new(true, "", e.Regimen, e.Alicuota, aportaEsta, e.BaseAnterior, e.MinimoNoImponible, Redondeo(imponible), Redondeo(retencion), e.RetenidoAntes, importe);
    }

    private sealed class RegimenGanancias
    {
        public string Id { get; set; } = ""; public string Descripcion { get; set; } = ""; public decimal MinimoNoImponible { get; set; }
        public decimal MinimoExcluido { get; set; } public decimal Porcentaje { get; set; } public bool NoAcumulativa { get; set; } public string Cuenta { get; set; } = "";
    }

    private sealed class ProveedorRetencion
    {
        public string Cuit { get; set; } = ""; public string Regimen { get; set; } = ""; public string Iva { get; set; } = ""; public DateTime? ExentoHasta { get; set; }
    }

    private sealed class LineaFactura
    {
        public string Tc { get; set; } = ""; public string Sucursal { get; set; } = ""; public string Numero { get; set; } = ""; public string Letra { get; set; } = "";
        public decimal Importe { get; set; } public decimal Gravado { get; set; } public decimal Exento { get; set; } public decimal NoGravado { get; set; } public decimal Total { get; set; }
    }

    /// <summary>Datos del régimen y de la cuenta contable de la retención; null si no corresponde retener por configuración.</summary>
    internal sealed record ContextoRetencion(string RegimenId, string Cuit, string Cuenta, decimal Alicuota, string Descripcion);

    /// <summary>Calcula la retención con los importes de la solicitud (no graba nada). Para la pantalla.</summary>
    public Task<OrdenPagoRetencion> CalcularRetencionAsync(OrdenPagoSolicitud solicitud, CancellationToken ct = default)
        => Ejecutar("CalcularRetencion", async (cn, user, session, token) =>
        {
            var r = await CalcularRetencionAsync(cn, null, solicitud, null, token);
            VerificarSesion(session);
            return r.Retencion;
        }, ct);

    /// <summary>Si el proveedor está sujeto a retención de Ganancias hoy (para mostrar u ocultar el cálculo).</summary>
    private async Task<bool> RetieneAsync(SqlConnection cn, SqlTransaction? tx, string proveedor, bool proforma, DateTime fecha, CancellationToken ct)
        => !proforma && (await ContextoAsync(cn, tx, proveedor, fecha, ct)).Contexto is not null;

    private async Task<(ContextoRetencion? Contexto, string Motivo, RegimenGanancias? Regimen, ProveedorRetencion? Proveedor)> ContextoAsync(
        SqlConnection cn, SqlTransaction? tx, string proveedor, DateTime fecha, CancellationToken ct)
    {
        if (!(await ConfigAsync(cn, tx, "AGENTEDERETENCIONGAN", ct)).Equals("SI", StringComparison.OrdinalIgnoreCase))
            return (null, "La empresa no es agente de retención de Ganancias.", null, null);
        var p = await cn.QuerySingleOrDefaultAsync<ProveedorRetencion>(Cmd("""
            SELECT LTRIM(RTRIM(ISNULL(NUMERO_DOCUMENTO,''))) Cuit, LTRIM(RTRIM(ISNULL(RETGAN_IdRetencion,''))) Regimen, LTRIM(RTRIM(ISNULL(IVA,''))) Iva,
                (SELECT TOP(1) a.FhVtoExento FROM dbo.MA_CUENTASADIC a WHERE LTRIM(RTRIM(a.CODIGO))=LTRIM(RTRIM(v.CODIGO))) ExentoHasta
            FROM dbo.Vt_Proveedores v WHERE LTRIM(RTRIM(CODIGO))=@proveedor;
            """, new { proveedor = proveedor.Trim() }, tx, ct));
        if (p is null || p.Regimen.Length == 0) return (null, "El proveedor no tiene régimen de retención de Ganancias.", null, p);
        if (p.ExentoHasta is { } hasta && hasta.Date > fecha.Date) return (null, $"El proveedor está exento de Ganancias hasta el {hasta:dd/MM/yyyy}.", null, p);
        var id = CodigoPk.Format(p.Regimen, 4);
        var reg = await cn.QuerySingleOrDefaultAsync<RegimenGanancias>(Cmd("""
            SELECT LTRIM(RTRIM(IdRetencion)) Id, LTRIM(RTRIM(ISNULL(Descripcion,''))) Descripcion, CONVERT(decimal(18,4),ISNULL(MinimoNoImponible,0)) MinimoNoImponible,
                CONVERT(decimal(18,4),ISNULL(MinimoExcluido,0)) MinimoExcluido, CONVERT(decimal(18,4),ISNULL(PORCENTAJE,0)) Porcentaje,
                CONVERT(bit,ISNULL(NoAcumulativa,0)) NoAcumulativa, LTRIM(RTRIM(ISNULL(CUENTA,''))) Cuenta
            FROM dbo.V_TA_RETENCIONES WHERE LTRIM(RTRIM(IdRetencion))=@id;
            """, new { id = id.Trim() }, tx, ct));
        if (reg is null) throw new InvalidOperationException($"El régimen de retención {p.Regimen} del proveedor no existe. Revisá la ficha del proveedor.");
        if ((await ConfigAsync(cn, tx, "ARCH_RET_" + p.Regimen.TrimStart('0'), ct)).Length > 0)
            throw new InvalidOperationException("El régimen de retención usa la alícuota del padrón (ARCH_RET). Cargá esta orden de pago desde el sistema legacy.");
        var cuenta = reg.Cuenta;
        if (cuenta.Length == 0)
            cuenta = await cn.QueryFirstOrDefaultAsync<string>(Cmd("SELECT TOP(1) LTRIM(RTRIM(ISNULL(PG_RETGAN,''))) FROM dbo.TA_CUENTASIVA;", null, tx, ct)) ?? "";
        if (cuenta.Length == 0) throw new InvalidOperationException("No se informó la cuenta contable de la retención de Ganancias.");
        if (p.Cuit.Length == 0) throw new InvalidOperationException("El proveedor no tiene CUIT: es necesario para acumular sus retenciones del mes.");
        return (new ContextoRetencion(reg.Id, p.Cuit, cuenta, reg.Porcentaje, reg.Descripcion), "", reg, p);
    }

    /// <summary>Retención y datos para grabarla. <paramref name="hastaId"/> excluye comprobantes posteriores (para recalcular el histórico).</summary>
    internal async Task<(OrdenPagoRetencion Retencion, ContextoRetencion? Contexto)> CalcularRetencionAsync(
        SqlConnection cn, SqlTransaction? tx, OrdenPagoSolicitud s, int? hastaId, CancellationToken ct)
    {
        if (s.Proforma) return (OrdenPagoRetencion.NoAplica("El pago proforma no retiene."), null);
        var (ctx, motivo, reg, prov) = await ContextoAsync(cn, tx, s.Proveedor, s.Fecha, ct);
        if (ctx is null || reg is null || prov is null) return (OrdenPagoRetencion.NoAplica(motivo), null);

        var pares = s.Comprobantes.Select(c => c.Clave).Concat(s.Creditos.Select(c => c.Clave)).Distinct().ToList();
        var args = new DynamicParameters(); args.Add("cuenta", s.Proveedor.Trim());
        var condiciones = pares.Select((k, i) =>
        {
            args.Add("t" + i, k.Tc); args.Add("s" + i, k.Sucursal); args.Add("n" + i, k.Numero); args.Add("l" + i, k.Letra);
            return $"(LTRIM(RTRIM(TC))=@t{i} AND SUCURSAL=@s{i} AND NUMERO=@n{i} AND LETRA=@l{i})";
        });
        var lineas = pares.Count == 0 ? [] : (await cn.QueryAsync<LineaFactura>(Cmd($"""
            SELECT LTRIM(RTRIM(TC)) Tc, SUCURSAL Sucursal, NUMERO Numero, LETRA Letra, CONVERT(decimal(18,4),ISNULL(IMPORTE,0)) Importe,
                CONVERT(decimal(18,4),ISNULL(LIVA_ImpNetoGrav,0)) Gravado, CONVERT(decimal(18,4),ISNULL(Liva_Exento,0)) Exento,
                CONVERT(decimal(18,4),ISNULL(LIVA_ImpNetoNGrav,0)) NoGravado, CONVERT(decimal(18,4),ISNULL(LIVA_TOTAL,0)) Total
            FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(CUENTA))=@cuenta AND [DEBE-HABER]='H' AND ({string.Join(" OR ", condiciones)}) ORDER BY SECUENCIA;
            """, args, tx, ct))).AsList();
        LineaFactura? Linea(OrdenPagoClave k) => lineas.FirstOrDefault(l => l.Tc == k.Tc && l.Sucursal == k.Sucursal && l.Numero == k.Numero && l.Letra == k.Letra);

        var sobreImpInterno = (await ConfigAsync(cn, tx, "CALCULARETENCIONSOBREIMPINT", ct)).Equals("SI", StringComparison.OrdinalIgnoreCase);
        var consumidorFinal = prov.Iva.TrimStart('0') == "5";
        decimal baseFacturas = 0, noGravado = 0;
        foreach (var c in s.Comprobantes)
        {
            var l = Linea(c.Clave);
            if (l is null) { baseFacturas += c.Importe; continue; }
            var total = l.Total > 0 ? l.Total : l.Importe;
            var fraccion = total > 0 ? Math.Min(1m, c.Importe / total) : 1m;
            var neto = l.Gravado + (sobreImpInterno ? l.Exento : 0);
            if (neto == 0 && l.NoGravado == 0) baseFacturas += c.Importe;                 // sin desglose de IVA: se retiene sobre lo pagado
            else baseFacturas += (consumidorFinal ? l.Total : neto) * fraccion;
            noGravado += l.NoGravado * fraccion;
        }
        // Créditos: se descuenta su parte neta (una NC discrimina IVA; un anticipo no).
        decimal descuento = 0;
        foreach (var c in s.Creditos)
        {
            var l = Linea(c.Clave);
            var factor = l is { Total: > 0, Gravado: > 0 } ? l.Gravado / l.Total : 1m;
            descuento += c.Importe * factor;
        }

        // Retenido y base ya acumulados en el mes con el mismo CUIT y régimen (desde la fecha de fin de exención si cae en el mes).
        decimal anterior = 0, retenido = 0;
        if (!reg.NoAcumulativa)
        {
            var inicio = new DateTime(s.Fecha.Year, s.Fecha.Month, 1);
            if (prov.ExentoHasta is { } fin && fin.Year == s.Fecha.Year && fin.Month == s.Fecha.Month) inicio = fin.Date;
            var acum = await cn.QuerySingleAsync<(decimal Base, decimal Retenido)>(Cmd("""
                SELECT CONVERT(decimal(18,4),ISNULL(SUM(RETGAN_BaseImponible),0)), CONVERT(decimal(18,4),ISNULL(SUM(RETGAN_Importe),0))
                FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK)
                WHERE LTRIM(RTRIM(DOCUMENTONUMERO))=@cuit AND ((FINALIZADA=1 AND LTRIM(RTRIM(TC))='OPG') OR LTRIM(RTRIM(TC)) IN ('PG','PGFP'))
                    AND FECHA>=@inicio AND FECHA<@fin AND LTRIM(RTRIM(RETGAN_IdRetencion))=@regimen AND ISNULL(ANULADA,0)=0 AND TCORIGEN<>'0'
                    AND (@hastaId IS NULL OR ID<@hastaId);
                """, new { cuit = ctx.Cuit, inicio, fin = new DateTime(s.Fecha.Year, s.Fecha.Month, 1).AddMonths(1), regimen = ctx.RegimenId, hastaId }, tx, ct));
            (anterior, retenido) = (acum.Base, acum.Retenido);
        }
        var escala = (await cn.QueryAsync<EscalaRetencion>(Cmd("""
            SELECT CONVERT(decimal(18,4),Desde) Desde, CONVERT(decimal(18,4),Hasta) Hasta, CONVERT(decimal(18,4),ISNULL(Importe,0)) Importe,
                CONVERT(decimal(18,4),ISNULL(Porcentaje,0)) Porcentaje, CONVERT(decimal(18,4),ISNULL(SobreExcedente,0)) SobreExcedente
            FROM dbo.V_TA_ESCALARETENCIONES WHERE LTRIM(RTRIM(IdRetencion))=@regimen;
            """, new { regimen = ctx.RegimenId }, tx, ct))).AsList();
        var resultado = CalcularRetencion(new RetencionEntrada(
            ctx.Descripcion, Redondeo(baseFacturas), Redondeo(noGravado), s.Comprobantes.Sum(c => c.Importe), s.Medios.Sum(m => m.Importe) + s.ChequesTerceros.Sum(c => c.Importe), Redondeo(descuento),
            anterior, retenido, reg.MinimoNoImponible, reg.MinimoExcluido, reg.Porcentaje, escala));
        return (resultado, ctx);
    }
}

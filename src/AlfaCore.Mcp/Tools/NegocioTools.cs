using System.ComponentModel;
using AlfaCore.Mcp.Infra;
using ModelContextProtocol.Server;

namespace AlfaCore.Mcp.Tools;

/// <summary>
/// Herramientas de negocio. TODO el SQL es parametrizado y de solo lectura sobre dir.vw_*; los unicos fragmentos de texto
/// interpolados salen de listas cerradas (nunca de lo que escribe el modelo). La logica de negocio (signo de NC, anulados,
/// neto sin IVA, costo y margen) vive una sola vez en las vistas.
/// </summary>
[McpServerToolType]
public sealed class NegocioTools(ToolRunner runner, SqlExecutor sql)
{
    private const string FiltroVentas = "Fecha >= @Desde AND Fecha < @Hasta AND (@IncluirProformas = 1 OR EsProforma = 0)";

    private static string[] AgruparPor => ["dia", "mes", "vendedor", "rubro", "cliente"];
    private static string[] CriteriosRanking => ["ganancia", "cantidad", "facturacion"];

    private static string Criterio(McpRuntimeSettings cfg) =>
        "Importes en pesos, NETOS de IVA. Las notas de crédito restan; los comprobantes anulados, remitos, pedidos y cobranzas no cuentan. " +
        (cfg.IncluirProformas ? "Incluye ventas proforma (no fiscales). " : "Solo comprobantes del libro de IVA (sin proformas). ") +
        "Costo = costo registrado en cada línea al momento de la venta; Margen = neto - costo.";

    private static Dictionary<string, object?> ParamsVentas(DateTime desde, DateTime hastaExclusivo, McpRuntimeSettings cfg) => new()
    {
        ["Desde"] = desde,
        ["Hasta"] = hastaExclusivo,
        ["IncluirProformas"] = cfg.IncluirProformas ? 1 : 0
    };

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "ventas_resumen", ReadOnly = true, Idempotent = true)]
    [Description("Resumen de ventas (neto sin IVA), costo, margen y cantidad de comprobantes de un período, agrupado por día, mes, vendedor, rubro o cliente. " +
                 "Usarla para preguntas como 'ventas del mes', 'ventas por vendedor' o 'ventas por rubro'. Las notas de crédito restan. " +
                 "Devuelve también los totales del período completo (aunque la lista se recorte).")]
    public Task<ToolResponse> VentasResumen(
        [Description("Fecha inicial inclusive, formato AAAA-MM-DD (ej. 2026-10-01).")] string desde,
        [Description("Fecha final inclusive, formato AAAA-MM-DD (ej. 2026-10-31).")] string hasta,
        [Description("Cómo agrupar: dia, mes, vendedor, rubro o cliente. Por defecto: mes.")] string? agrupar_por = null,
        CancellationToken ct = default)
        => runner.RunAsync("ventas_resumen", new { desde, hasta, agrupar_por }, async cfg =>
        {
            var (d, h) = Parametros.Rango(desde, hasta);
            var grupo = Parametros.Opcion(agrupar_por, "agrupar_por", AgruparPor, "mes");
            // Listas cerradas: la expresion y el orden salen de aca, nunca del texto del modelo.
            var (expr, orden) = grupo switch
            {
                "dia" => ("CONVERT(char(10), Fecha, 120)", "Grupo ASC"),
                "mes" => ("AnioMes", "Grupo ASC"),
                "vendedor" => ("VendedorNombre", "Ventas DESC"),
                "rubro" => ("RubroNombre", "Ventas DESC"),
                _ => ("ClienteNombre", "Ventas DESC")
            };
            var r = new ToolResponse { Criterio = Criterio(cfg) };
            var p = ParamsVentas(d, h, cfg);
            var res = await sql.QueryAsync(cfg, $"""
                SELECT {expr} AS Grupo,
                       CAST(SUM(NetoSinIva) AS decimal(18,2)) AS Ventas,
                       CAST(SUM(CostoTotal) AS decimal(18,2)) AS Costo,
                       CAST(SUM(Margen) AS decimal(18,2)) AS Margen,
                       CAST(CASE WHEN SUM(NetoSinIva) = 0 THEN NULL ELSE 100.0 * SUM(Margen) / SUM(NetoSinIva) END AS decimal(9,2)) AS MargenPct,
                       COUNT(DISTINCT Tc + N'|' + Comprobante) AS Comprobantes
                FROM dir.vw_Ventas
                WHERE {FiltroVentas}
                GROUP BY {expr}
                ORDER BY {orden}
                """, p, ct);
            r.Filas = res.Filas;
            r.Truncado = res.Truncado;
            if (res.Truncado) r.Advertencias.Add($"Hay más de {cfg.MaxFilas} grupos; la lista está recortada (los totales son del período completo).");
            r.Totales = await TotalesAsync(cfg, p, r, ct);
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "ranking_articulos", ReadOnly = true, Idempotent = true)]
    [Description("Ranking de artículos más vendidos en un período, por ganancia (margen en pesos), cantidad vendida o facturación (neto sin IVA). " +
                 "Usarla para '¿qué artículo me dejó más ganancia?' o '¿cuáles son los 10 más vendidos?'. " +
                 "La ganancia usa el costo registrado en cada venta (histórico), no el costo de hoy.")]
    public Task<ToolResponse> RankingArticulos(
        [Description("Fecha inicial inclusive, AAAA-MM-DD.")] string desde,
        [Description("Fecha final inclusive, AAAA-MM-DD.")] string hasta,
        [Description("Criterio de orden: ganancia, cantidad o facturacion. Por defecto: ganancia.")] string? criterio = null,
        [Description("Cuántos artículos devolver (por defecto 10; máximo según configuración).")] int? top = null,
        CancellationToken ct = default)
        => runner.RunAsync("ranking_articulos", new { desde, hasta, criterio, top }, async cfg =>
        {
            var (d, h) = Parametros.Rango(desde, hasta);
            var crit = Parametros.Opcion(criterio, "criterio", CriteriosRanking, "ganancia");
            var r = new ToolResponse { Criterio = Criterio(cfg) };
            var t = Parametros.Top(top, cfg, r);
            var orden = crit switch { "cantidad" => "SUM(Cantidad)", "facturacion" => "SUM(NetoSinIva)", _ => "SUM(Margen)" };
            var p = ParamsVentas(d, h, cfg);
            p["Top"] = t;
            var res = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Top) ArticuloCodigo,
                       MAX(ArticuloDescripcion) AS Articulo,
                       MAX(RubroNombre) AS Rubro,
                       CAST(SUM(Cantidad) AS decimal(18,2)) AS Cantidad,
                       CAST(SUM(NetoSinIva) AS decimal(18,2)) AS Facturacion,
                       CAST(SUM(CostoTotal) AS decimal(18,2)) AS Costo,
                       CAST(SUM(Margen) AS decimal(18,2)) AS Ganancia,
                       CAST(CASE WHEN SUM(NetoSinIva) = 0 THEN NULL ELSE 100.0 * SUM(Margen) / SUM(NetoSinIva) END AS decimal(9,2)) AS MargenPct
                FROM dir.vw_Ventas
                WHERE {FiltroVentas}
                GROUP BY ArticuloCodigo
                ORDER BY {orden} DESC, ArticuloCodigo
                """, p, ct);
            r.Filas = res.Filas;
            r.Totales = await TotalesAsync(cfg, p, r, ct);
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "comparar_periodos", ReadOnly = true, Idempotent = true)]
    [Description("Compara dos períodos (A contra B): ventas netas, costo, margen y comprobantes, con variación absoluta y porcentual de B respecto de A, " +
                 "y el detalle por rubro. Usarla para 'comparar este mes con el anterior' (A = mes anterior, B = mes actual) o 'este año contra el pasado'.")]
    public Task<ToolResponse> CompararPeriodos(
        [Description("Inicio del período A (base de comparación), AAAA-MM-DD.")] string desde_a,
        [Description("Fin del período A, inclusive, AAAA-MM-DD.")] string hasta_a,
        [Description("Inicio del período B (el que se compara), AAAA-MM-DD.")] string desde_b,
        [Description("Fin del período B, inclusive, AAAA-MM-DD.")] string hasta_b,
        CancellationToken ct = default)
        => runner.RunAsync("comparar_periodos", new { desde_a, hasta_a, desde_b, hasta_b }, async cfg =>
        {
            var (da, ha) = Parametros.Rango(desde_a, hasta_a, "desde_a", "hasta_a");
            var (db, hb) = Parametros.Rango(desde_b, hasta_b, "desde_b", "hasta_b");
            var r = new ToolResponse { Criterio = Criterio(cfg) + " Variación = B respecto de A." };
            if (da < hb && db < ha) r.Advertencias.Add("Los períodos A y B se superponen: las ventas del tramo común cuentan en ambos.");
            if ((ha - da).Days != (hb - db).Days) r.Advertencias.Add($"Los períodos tienen distinta duración ({(ha - da).Days} y {(hb - db).Days} días): la comparación absoluta puede ser engañosa; mirar el promedio diario.");

            var p = new Dictionary<string, object?>
            {
                ["DA"] = da, ["HA"] = ha, ["DB"] = db, ["HB"] = hb, ["IncluirProformas"] = cfg.IncluirProformas ? 1 : 0
            };
            const string enA = "(Fecha >= @DA AND Fecha < @HA)";
            const string enB = "(Fecha >= @DB AND Fecha < @HB)";
            const string filtro = $"({enA} OR {enB}) AND (@IncluirProformas = 1 OR EsProforma = 0)";

            var tot = (await sql.QueryAsync(cfg, $"""
                SELECT CAST(ISNULL(SUM(CASE WHEN {enA} THEN NetoSinIva END), 0) AS decimal(18,2)) AS VentasA,
                       CAST(ISNULL(SUM(CASE WHEN {enB} THEN NetoSinIva END), 0) AS decimal(18,2)) AS VentasB,
                       CAST(ISNULL(SUM(CASE WHEN {enA} THEN CostoTotal END), 0) AS decimal(18,2)) AS CostoA,
                       CAST(ISNULL(SUM(CASE WHEN {enB} THEN CostoTotal END), 0) AS decimal(18,2)) AS CostoB,
                       CAST(ISNULL(SUM(CASE WHEN {enA} THEN Margen END), 0) AS decimal(18,2)) AS MargenA,
                       CAST(ISNULL(SUM(CASE WHEN {enB} THEN Margen END), 0) AS decimal(18,2)) AS MargenB,
                       COUNT(DISTINCT CASE WHEN {enA} THEN Tc + N'|' + Comprobante END) AS ComprobantesA,
                       COUNT(DISTINCT CASE WHEN {enB} THEN Tc + N'|' + Comprobante END) AS ComprobantesB,
                       COUNT(*) AS Lineas,
                       ISNULL(SUM(SinCosto), 0) AS LineasSinCosto
                FROM dir.vw_Ventas
                WHERE {filtro}
                """, p, ct)).Filas.Single();

            var diasA = (ha - da).Days;
            var diasB = (hb - db).Days;
            r.Totales = new Dictionary<string, object?>
            {
                ["PeriodoA"] = $"{da:yyyy-MM-dd} a {ha.AddDays(-1):yyyy-MM-dd} ({diasA} días)",
                ["PeriodoB"] = $"{db:yyyy-MM-dd} a {hb.AddDays(-1):yyyy-MM-dd} ({diasB} días)",
                ["Ventas"] = Comparar(tot["VentasA"], tot["VentasB"]),
                ["Costo"] = Comparar(tot["CostoA"], tot["CostoB"]),
                ["Margen"] = Comparar(tot["MargenA"], tot["MargenB"]),
                ["Comprobantes"] = Comparar(tot["ComprobantesA"], tot["ComprobantesB"]),
                ["VentaDiariaPromedio"] = Comparar(Dividir(tot["VentasA"], diasA), Dividir(tot["VentasB"], diasB))
            };
            AdvertirSinCosto(cfg, Convert.ToInt64(tot["Lineas"]), Convert.ToInt64(tot["LineasSinCosto"]), r);

            p["Max"] = cfg.MaxFilas + 1;
            var rub = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Max) RubroNombre AS Rubro,
                       CAST(ISNULL(SUM(CASE WHEN {enA} THEN NetoSinIva END), 0) AS decimal(18,2)) AS VentasA,
                       CAST(ISNULL(SUM(CASE WHEN {enB} THEN NetoSinIva END), 0) AS decimal(18,2)) AS VentasB,
                       CAST(ISNULL(SUM(CASE WHEN {enA} THEN Margen END), 0) AS decimal(18,2)) AS MargenA,
                       CAST(ISNULL(SUM(CASE WHEN {enB} THEN Margen END), 0) AS decimal(18,2)) AS MargenB
                FROM dir.vw_Ventas
                WHERE {filtro}
                GROUP BY RubroNombre
                ORDER BY ABS(ISNULL(SUM(CASE WHEN {enB} THEN NetoSinIva END), 0) - ISNULL(SUM(CASE WHEN {enA} THEN NetoSinIva END), 0)) DESC
                """, p, ct);
            foreach (var f in rub.Filas)
            {
                var a = Convert.ToDecimal(f["VentasA"]);
                var b = Convert.ToDecimal(f["VentasB"]);
                f["Variacion"] = b - a;
                f["VariacionPct"] = a == 0 ? null : Math.Round((b - a) / Math.Abs(a) * 100m, 2);
            }
            r.Filas = rub.Filas;
            r.Truncado = rub.Truncado;
            if (rub.Truncado) r.Advertencias.Add($"Hay más de {cfg.MaxFilas} rubros; el detalle está recortado (los totales son completos).");
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "clientes_top", ReadOnly = true, Idempotent = true)]
    [Description("Los clientes que más compraron en un período (neto sin IVA), con su margen, cantidad de comprobantes y fecha de última compra. " +
                 "Usarla para '¿quiénes son mis mejores clientes?'.")]
    public Task<ToolResponse> ClientesTop(
        [Description("Fecha inicial inclusive, AAAA-MM-DD.")] string desde,
        [Description("Fecha final inclusive, AAAA-MM-DD.")] string hasta,
        [Description("Cuántos clientes devolver (por defecto 10).")] int? top = null,
        CancellationToken ct = default)
        => runner.RunAsync("clientes_top", new { desde, hasta, top }, async cfg =>
        {
            var (d, h) = Parametros.Rango(desde, hasta);
            var r = new ToolResponse { Criterio = Criterio(cfg) };
            var t = Parametros.Top(top, cfg, r);
            var p = ParamsVentas(d, h, cfg);
            p["Top"] = t;
            var res = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Top) ClienteCodigo,
                       MAX(ClienteNombre) AS Cliente,
                       CAST(SUM(NetoSinIva) AS decimal(18,2)) AS Ventas,
                       CAST(SUM(Margen) AS decimal(18,2)) AS Margen,
                       CAST(CASE WHEN SUM(NetoSinIva) = 0 THEN NULL ELSE 100.0 * SUM(Margen) / SUM(NetoSinIva) END AS decimal(9,2)) AS MargenPct,
                       COUNT(DISTINCT Tc + N'|' + Comprobante) AS Comprobantes,
                       MAX(Fecha) AS UltimaCompra
                FROM dir.vw_Ventas
                WHERE {FiltroVentas}
                GROUP BY ClienteCodigo
                ORDER BY SUM(NetoSinIva) DESC, ClienteCodigo
                """, p, ct);
            r.Filas = res.Filas;
            r.Totales = await TotalesAsync(cfg, p, r, ct);
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "deuda_clientes", ReadOnly = true, Idempotent = true)]
    [Description("Clientes con saldo deudor en cuenta corriente, de mayor a menor: deuda total, parte vencida, vencida a más de 90 días y comprobantes abiertos. " +
                 "Usarla para '¿quién me debe más?' o 'deudores de más de X pesos'. Es el saldo de HOY (los créditos pendientes ya restan).")]
    public Task<ToolResponse> DeudaClientes(
        [Description("Deuda mínima en pesos para incluir al cliente (por defecto 0: todos los que deben).")] decimal? minimo = null,
        [Description("Cuántos clientes devolver (por defecto 10).")] int? top = null,
        CancellationToken ct = default)
        => runner.RunAsync("deuda_clientes", new { minimo, top }, async cfg =>
        {
            var r = new ToolResponse { Criterio = "Saldo de cuenta corriente al día de hoy en pesos (VE_CPTES_SALDOS_VENTAS). Vencido = fecha de vencimiento anterior a hoy." };
            var t = Parametros.Top(top, cfg, r);
            var p = new Dictionary<string, object?> { ["Top"] = t, ["Minimo"] = minimo ?? 0m };
            const string agrupado = """
                FROM dir.vw_CuentaCorriente
                GROUP BY ClienteCodigo
                HAVING SUM(Saldo) > 0 AND SUM(Saldo) >= @Minimo
                """;
            var res = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Top) ClienteCodigo,
                       MAX(ClienteNombre) AS Cliente,
                       CAST(SUM(Saldo) AS decimal(18,2)) AS Deuda,
                       CAST(SUM(CASE WHEN DiasVencido > 0 THEN Saldo ELSE 0 END) AS decimal(18,2)) AS Vencido,
                       CAST(SUM(CASE WHEN DiasVencido > 90 THEN Saldo ELSE 0 END) AS decimal(18,2)) AS VencidoMasDe90,
                       MAX(CASE WHEN DiasVencido > 0 THEN DiasVencido END) AS MaxDiasVencido,
                       COUNT(*) AS Comprobantes
                {agrupado}
                ORDER BY SUM(Saldo) DESC, ClienteCodigo
                """, p, ct);
            r.Filas = res.Filas;
            var tot = (await sql.QueryAsync(cfg, $"""
                SELECT COUNT(*) AS ClientesConDeuda, CAST(ISNULL(SUM(x.Deuda), 0) AS decimal(18,2)) AS DeudaTotal
                FROM (SELECT SUM(Saldo) AS Deuda {agrupado}) x
                """, p, ct)).Filas.Single();
            r.Totales = tot;
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "stock_critico", ReadOnly = true, Idempotent = true)]
    [Description("Artículos con stock en o por debajo de su punto de pedido (stock total de todos los depósitos), ordenados por faltante. " +
                 "Usarla para '¿qué tengo que reponer?'. Solo considera artículos activos que tienen punto de pedido cargado.")]
    public Task<ToolResponse> StockCritico(
        [Description("Cuántos artículos devolver (por defecto 10).")] int? top = null,
        CancellationToken ct = default)
        => runner.RunAsync("stock_critico", new { top }, async cfg =>
        {
            var r = new ToolResponse { Criterio = "Stock = suma de todos los depósitos (unidad base). Crítico = stock <= punto de pedido. Faltante = punto de pedido - stock. Se excluyen artículos suspendidos o sin punto de pedido." };
            var t = Parametros.Top(top, cfg, r);
            var p = new Dictionary<string, object?> { ["Top"] = t };
            const string agrupado = """
                FROM dir.vw_Stock
                WHERE PuntoPedido > 0 AND (Suspendido = 0 OR Suspendido IS NULL)
                GROUP BY ArticuloCodigo
                HAVING SUM(Stock) <= MAX(PuntoPedido)
                """;
            var res = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Top) ArticuloCodigo,
                       MAX(Descripcion) AS Articulo,
                       MAX(RubroNombre) AS Rubro,
                       CAST(SUM(Stock) AS decimal(18,2)) AS Stock,
                       CAST(MAX(PuntoPedido) AS decimal(18,2)) AS PuntoPedido,
                       CAST(MAX(PuntoPedido) - SUM(Stock) AS decimal(18,2)) AS Faltante,
                       CAST(MAX(CostoActual) AS decimal(18,2)) AS CostoActual
                {agrupado}
                ORDER BY MAX(PuntoPedido) - SUM(Stock) DESC, ArticuloCodigo
                """, p, ct);
            r.Filas = res.Filas;
            r.Totales = (await sql.QueryAsync(cfg, $"SELECT COUNT(*) AS ArticulosCriticos FROM (SELECT ArticuloCodigo {agrupado}) x", p, ct)).Filas.Single();
            return r;
        }, ct);

    // ---- helpers --------------------------------------------------------------------------------------------------------

    /// <summary>Totales del periodo completo (no recortados) + advertencia si hay muchas lineas sin costo.</summary>
    private async Task<Dictionary<string, object?>> TotalesAsync(McpRuntimeSettings cfg, Dictionary<string, object?> p, ToolResponse r, CancellationToken ct)
    {
        var t = (await sql.QueryAsync(cfg, $"""
            SELECT CAST(ISNULL(SUM(NetoSinIva), 0) AS decimal(18,2)) AS Ventas,
                   CAST(ISNULL(SUM(CostoTotal), 0) AS decimal(18,2)) AS Costo,
                   CAST(ISNULL(SUM(Margen), 0) AS decimal(18,2)) AS Margen,
                   CAST(CASE WHEN SUM(NetoSinIva) = 0 THEN NULL ELSE 100.0 * SUM(Margen) / SUM(NetoSinIva) END AS decimal(9,2)) AS MargenPct,
                   COUNT(DISTINCT Tc + N'|' + Comprobante) AS Comprobantes,
                   COUNT(*) AS Lineas,
                   ISNULL(SUM(SinCosto), 0) AS LineasSinCosto
            FROM dir.vw_Ventas
            WHERE {FiltroVentas}
            """, p, ct)).Filas.Single();
        AdvertirSinCosto(cfg, Convert.ToInt64(t["Lineas"]), Convert.ToInt64(t["LineasSinCosto"]), r);
        if (Convert.ToInt64(t["Lineas"]) == 0) r.Advertencias.Add("No hay ventas en el período indicado.");
        return t;
    }

    private static void AdvertirSinCosto(McpRuntimeSettings cfg, long lineas, long sinCosto, ToolResponse r)
    {
        if (lineas == 0) return;
        var pct = 100m * sinCosto / lineas;
        if (pct > cfg.AdvertirSinCostoPct)
            r.Advertencias.Add($"El {pct:0.#}% de las líneas del período no tiene costo registrado: el margen y la ganancia están sobreestimados.");
    }

    private static decimal? Dividir(object? valor, int dias) => dias <= 0 ? null : Math.Round(Convert.ToDecimal(valor) / dias, 2);

    private static Dictionary<string, object?> Comparar(object? a, object? b)
    {
        var va = a is null ? (decimal?)null : Convert.ToDecimal(a);
        var vb = b is null ? (decimal?)null : Convert.ToDecimal(b);
        return new Dictionary<string, object?>
        {
            ["A"] = va,
            ["B"] = vb,
            ["Variacion"] = va is null || vb is null ? null : vb - va,
            ["VariacionPct"] = va is null or 0 || vb is null ? null : Math.Round((vb.Value - va.Value) / Math.Abs(va.Value) * 100m, 2)
        };
    }
}

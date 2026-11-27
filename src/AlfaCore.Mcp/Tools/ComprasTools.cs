using System.ComponentModel;
using AlfaCore.Mcp.Infra;
using ModelContextProtocol.Server;

namespace AlfaCore.Mcp.Tools;

/// <summary>
/// Compras y proveedores. Requieren el script 013 (dir.vw_Proveedores, vw_Compras, vw_ComprasDetalle, vw_CuentaCorrienteProveedores).
/// Misma regla que el resto: SQL parametrizado; lo unico interpolado sale de listas cerradas o de nombres de parametro propios.
/// </summary>
[McpServerToolType]
public sealed class ComprasTools(ToolRunner runner, SqlExecutor sql)
{
    private const string FiltroCompras = "Fecha >= @Desde AND Fecha < @Hasta AND (@IncluirProformas = 1 OR EsProforma = 0)";
    private static string[] AgruparPor => ["mes", "dia", "proveedor", "tipo", "comprobante"];

    private static string Criterio(McpRuntimeSettings cfg) =>
        "Fuente: libro de IVA compras (incluye gastos cargados en contabilidad, como liquidaciones de tarjeta y gastos bancarios)" +
        (cfg.IncluirProformas ? " más las compras proforma (no fiscales). " : "; sin proformas. ") +
        "Importes en pesos; las notas de crédito restan. NetoSinIva = gravado + no gravado + exento. Percepciones = IVA + IIBB + ganancias.";

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "buscar_proveedores", Title = "Buscar proveedores", ReadOnly = true, Idempotent = true)]
    [Description("Busca proveedores por parte del nombre / razón social, del código o del CUIT. Todas las palabras deben aparecer, en cualquier orden " +
                 "(no distingue tildes ni mayúsculas). Usarla SIEMPRE antes de las herramientas que piden el código de un proveedor " +
                 "(ficha_proveedor, o el filtro 'proveedor' de compras_resumen). Si hay varios parecidos, preguntar al usuario cuál es.")]
    public Task<ToolResponse> BuscarProveedores(
        [Description("Texto a buscar, ej. 'distribuidora' o un CUIT.")] string texto,
        [Description("Cuántos resultados devolver (por defecto 10).")] int? top = null,
        CancellationToken ct = default)
        => runner.RunAsync("buscar_proveedores", new { texto, top }, async cfg =>
        {
            var palabras = Parametros.Palabras(texto, "texto");
            var r = new ToolResponse { Criterio = "Coincidencia por nombre, código o CUIT. Primero el código exacto, luego los activos, por nombre." };
            var t = Parametros.Top(top, cfg, r);
            var p = new Dictionary<string, object?> { ["Max"] = t + 1, ["Texto"] = texto.Trim() };
            var cond = new List<string>();
            for (var i = 0; i < palabras.Length; i++)
            {
                p[$"P{i}"] = Parametros.Contiene(palabras[i]);
                p[$"Q{i}"] = Parametros.Contiene(palabras[i].Replace("-", "").Replace(".", ""));
                cond.Add($"(RazonSocial LIKE @P{i} OR ProveedorCodigo LIKE @P{i} OR REPLACE(REPLACE(Documento, N'-', N''), N'.', N'') LIKE @Q{i})");
            }
            var res = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Max) ProveedorCodigo, RazonSocial, Documento, CondicionIva, Localidad, Provincia, DadoDeBaja, Bloqueado
                FROM dir.vw_Proveedores
                WHERE {string.Join(" AND ", cond)}
                ORDER BY CASE WHEN ProveedorCodigo = @Texto THEN 0 ELSE 1 END, DadoDeBaja, RazonSocial
                """, p, ct, maxFilas: t);
            r.Filas = res.Filas;
            r.Truncado = res.Truncado;
            if (res.Truncado) r.Advertencias.Add($"Hay más de {t} coincidencias: mostrar estas y pedir al usuario un dato más preciso.");
            if (res.Filas.Count == 0) r.Advertencias.Add("Ningún proveedor coincide. Probar con menos palabras o solo una parte del nombre.");
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "compras_resumen", Title = "Resumen de compras", ReadOnly = true, Idempotent = true)]
    [Description("Resumen de compras y gastos con IVA de un período: neto, IVA, percepciones, total y comprobantes, agrupado por mes, día, proveedor, " +
                 "tipo de comprobante o comprobante. Usarla para '¿cuánto compré este mes?', 'compras por proveedor', 'IVA compras de octubre' o, " +
                 "con 'proveedor', '¿qué le compré a X?'. Las notas de crédito restan. Devuelve los totales del período completo.")]
    public Task<ToolResponse> ComprasResumen(
        [Description("Fecha inicial inclusive, AAAA-MM-DD.")] string desde,
        [Description("Fecha final inclusive, AAAA-MM-DD.")] string hasta,
        [Description("Cómo agrupar: mes, dia, proveedor, tipo o comprobante. Por defecto: mes.")] string? agrupar_por = null,
        [Description("Opcional: código exacto de UN proveedor (obtenerlo con buscar_proveedores).")] string? proveedor = null,
        CancellationToken ct = default)
        => runner.RunAsync("compras_resumen", new { desde, hasta, agrupar_por, proveedor }, async cfg =>
        {
            var (d, h) = Parametros.Rango(desde, hasta);
            var grupo = Parametros.Opcion(agrupar_por, "agrupar_por", AgruparPor, "mes");
            var (expr, orden) = grupo switch
            {
                "dia" => ("CONVERT(char(10), Fecha, 120)", "Grupo ASC"),
                "proveedor" => ("ProveedorCodigo + N' - ' + ProveedorNombre", "Total DESC"),
                "tipo" => ("Tc + N' - ' + TipoComprobante", "Total DESC"),
                "comprobante" => ("CONVERT(char(10), Fecha, 120) + N' ' + Tc + N' ' + Comprobante + N' ' + ProveedorNombre", "Grupo ASC"),
                _ => ("AnioMes", "Grupo ASC")
            };
            var p = new Dictionary<string, object?> { ["Desde"] = d, ["Hasta"] = h, ["IncluirProformas"] = cfg.IncluirProformas ? 1 : 0 };
            var filtro = FiltroCompras;
            var criterio = Criterio(cfg);
            if (Parametros.CodigoOpcional(proveedor, "proveedor") is { } prov)
            {
                p["Proveedor"] = prov;
                filtro += " AND ProveedorCodigo = @Proveedor";
                criterio += $" Solo el proveedor {prov}.";
            }
            var r = new ToolResponse { Criterio = criterio };
            const string importes = """
                CAST(SUM(NetoSinIva) AS decimal(18,2)) AS Neto,
                CAST(SUM(Iva) AS decimal(18,2)) AS Iva,
                CAST(SUM(Percepciones) AS decimal(18,2)) AS Percepciones,
                CAST(SUM(Total) AS decimal(18,2)) AS Total,
                COUNT(*) AS Comprobantes
                """;
            var res = await sql.QueryAsync(cfg, $"""
                SELECT {expr} AS Grupo, {importes}
                FROM dir.vw_Compras
                WHERE {filtro}
                GROUP BY {expr}
                ORDER BY {orden}
                """, p, ct);
            r.Filas = res.Filas;
            r.Truncado = res.Truncado;
            if (res.Truncado) r.Advertencias.Add($"Hay más de {cfg.MaxFilas} grupos; la lista está recortada (los totales son del período completo).");
            r.Totales = (await sql.QueryAsync(cfg, $"""
                SELECT CAST(ISNULL(SUM(NetoSinIva), 0) AS decimal(18,2)) AS Neto,
                       CAST(ISNULL(SUM(Iva), 0) AS decimal(18,2)) AS Iva,
                       CAST(ISNULL(SUM(Percepciones), 0) AS decimal(18,2)) AS Percepciones,
                       CAST(ISNULL(SUM(Total), 0) AS decimal(18,2)) AS Total,
                       COUNT(*) AS Comprobantes,
                       CAST(ISNULL(SUM(CASE WHEN EsProforma = 1 THEN Total ELSE 0 END), 0) AS decimal(18,2)) AS TotalProformas,
                       CAST(ISNULL(SUM(CASE WHEN TieneCabecera = 0 THEN Total ELSE 0 END), 0) AS decimal(18,2)) AS TotalGastosContables
                FROM dir.vw_Compras
                WHERE {filtro}
                """, p, ct)).Filas.Single();
            if (Convert.ToInt32(r.Totales["Comprobantes"]) == 0)
                r.Advertencias.Add(p.ContainsKey("Proveedor")
                    ? "No hay compras en el período para ese proveedor. Si no es lo esperado, verificar el código con buscar_proveedores."
                    : "No hay compras en el período indicado.");
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "deuda_proveedores", Title = "Deuda con proveedores", ReadOnly = true, Idempotent = true)]
    [Description("A qué proveedores les debemos, de mayor a menor: facturas impagas, créditos a favor sin aplicar (pagos a cuenta, notas de crédito), " +
                 "deuda neta, vencido y vencido a más de 90 días. Usarla para '¿a quién le debo?', '¿cuánto debo a proveedores?' o '¿qué tengo vencido para pagar?'. " +
                 "Es el saldo de HOY.")]
    public Task<ToolResponse> DeudaProveedores(
        [Description("Deuda neta mínima en pesos para incluir al proveedor (por defecto 0: todos a los que se les debe).")] decimal? minimo = null,
        [Description("Cuántos proveedores devolver (por defecto 10).")] int? top = null,
        CancellationToken ct = default)
        => runner.RunAsync("deuda_proveedores", new { minimo, top }, async cfg =>
        {
            var r = new ToolResponse
            {
                Criterio = "Saldo de cuenta corriente con proveedores al día de hoy, en pesos. Facturas = comprobantes impagos; Creditos = pagos a cuenta y " +
                           "notas de crédito sin aplicar (restan); DeudaNeta = Facturas + Creditos. Vencido = facturas con vencimiento anterior a hoy."
            };
            var t = Parametros.Top(top, cfg, r);
            var p = new Dictionary<string, object?> { ["Top"] = t, ["Minimo"] = minimo ?? 0m };
            const string agrupado = """
                FROM dir.vw_CuentaCorrienteProveedores
                GROUP BY ProveedorCodigo
                HAVING SUM(Saldo) > 0 AND SUM(Saldo) >= @Minimo
                """;
            var res = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Top) ProveedorCodigo,
                       MAX(ProveedorNombre) AS Proveedor,
                       CAST(SUM(CASE WHEN EsCredito = 0 THEN Saldo ELSE 0 END) AS decimal(18,2)) AS Facturas,
                       CAST(SUM(CASE WHEN EsCredito = 1 THEN Saldo ELSE 0 END) AS decimal(18,2)) AS Creditos,
                       CAST(SUM(Saldo) AS decimal(18,2)) AS DeudaNeta,
                       CAST(SUM(CASE WHEN DiasVencido > 0 THEN Saldo ELSE 0 END) AS decimal(18,2)) AS Vencido,
                       CAST(SUM(CASE WHEN DiasVencido > 90 THEN Saldo ELSE 0 END) AS decimal(18,2)) AS VencidoMasDe90,
                       MAX(DiasVencido) AS MaxDiasVencido
                {agrupado}
                ORDER BY SUM(Saldo) DESC, ProveedorCodigo
                """, p, ct);
            r.Filas = res.Filas;
            r.Totales = (await sql.QueryAsync(cfg, """
                SELECT CAST(ISNULL(SUM(CASE WHEN EsCredito = 0 THEN Saldo ELSE 0 END), 0) AS decimal(18,2)) AS FacturasImpagas,
                       CAST(ISNULL(SUM(CASE WHEN EsCredito = 1 THEN Saldo ELSE 0 END), 0) AS decimal(18,2)) AS CreditosAFavor,
                       CAST(ISNULL(SUM(Saldo), 0) AS decimal(18,2)) AS DeudaNetaTotal,
                       CAST(ISNULL(SUM(CASE WHEN DiasVencido > 0 THEN Saldo ELSE 0 END), 0) AS decimal(18,2)) AS VencidoTotal,
                       COUNT(DISTINCT ProveedorCodigo) AS ProveedoresConSaldo
                FROM dir.vw_CuentaCorrienteProveedores
                """, p, ct)).Filas.Single();
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "ficha_proveedor", Title = "Ficha y cuenta corriente de un proveedor", ReadOnly = true, Idempotent = true)]
    [Description("Ficha de UN proveedor: datos, deuda de hoy (facturas impagas, créditos a favor, deuda neta, vencido), compras de los últimos 12 meses " +
                 "y la lista de comprobantes abiertos con vencimiento. Usarla para '¿cuánto le debo a X?', '¿qué facturas de X tengo sin pagar?' o 'contame de X'. " +
                 "Pide el código exacto: obtenerlo antes con buscar_proveedores.")]
    public Task<ToolResponse> FichaProveedor(
        [Description("Código exacto del proveedor (columna ProveedorCodigo de buscar_proveedores).")] string proveedor,
        CancellationToken ct = default)
        => runner.RunAsync("ficha_proveedor", new { proveedor }, async cfg =>
        {
            var codigo = Parametros.Codigo(proveedor, "proveedor");
            var p = new Dictionary<string, object?>
            {
                ["P"] = codigo,
                ["Desde"] = DateTime.Today.AddMonths(-12),
                ["IncluirProformas"] = cfg.IncluirProformas ? 1 : 0
            };
            var datos = (await sql.QueryAsync(cfg, """
                SELECT ProveedorCodigo, RazonSocial, Documento, CondicionIva, Localidad, Provincia, DadoDeBaja, Bloqueado
                FROM dir.vw_Proveedores WHERE ProveedorCodigo = @P
                """, p, ct)).Filas.FirstOrDefault()
                ?? throw new ToolUserException($"No existe el proveedor con código '{codigo}'. Buscarlo primero con buscar_proveedores.");

            var r = new ToolResponse
            {
                Criterio = "Deuda = saldo HOY en pesos. Filas = comprobantes abiertos: Saldo > 0 facturas impagas; Saldo < 0 créditos a favor sin aplicar " +
                           "(pagos a cuenta, notas de crédito). Compras12Meses = total con impuestos desde " + $"{p["Desde"]:yyyy-MM-dd}" +
                           (cfg.IncluirProformas ? " (incluye proformas)." : " (sin proformas).")
            };
            var abiertos = await sql.QueryAsync(cfg, """
                SELECT Tc, Comprobante, Fecha, Vencimiento, Saldo, DiasVencido, Tramo
                FROM dir.vw_CuentaCorrienteProveedores WHERE ProveedorCodigo = @P
                ORDER BY EsCredito, Vencimiento, Comprobante
                """, p, ct);
            r.Filas = abiertos.Filas;
            r.Truncado = abiertos.Truncado;

            var deuda = (await sql.QueryAsync(cfg, """
                SELECT CAST(ISNULL(SUM(CASE WHEN EsCredito = 0 THEN Saldo ELSE 0 END), 0) AS decimal(18,2)) AS FacturasImpagas,
                       CAST(ISNULL(SUM(CASE WHEN EsCredito = 1 THEN Saldo ELSE 0 END), 0) AS decimal(18,2)) AS CreditosAFavor,
                       CAST(ISNULL(SUM(Saldo), 0) AS decimal(18,2)) AS DeudaNeta,
                       CAST(ISNULL(SUM(CASE WHEN DiasVencido > 0 THEN Saldo ELSE 0 END), 0) AS decimal(18,2)) AS Vencido,
                       CAST(ISNULL(SUM(CASE WHEN DiasVencido > 90 THEN Saldo ELSE 0 END), 0) AS decimal(18,2)) AS VencidoMasDe90,
                       COUNT(*) AS ComprobantesAbiertos
                FROM dir.vw_CuentaCorrienteProveedores WHERE ProveedorCodigo = @P
                """, p, ct)).Filas.Single();
            var compras = (await sql.QueryAsync(cfg, """
                SELECT CAST(ISNULL(SUM(NetoSinIva), 0) AS decimal(18,2)) AS ComprasNeto12Meses,
                       CAST(ISNULL(SUM(Total), 0) AS decimal(18,2)) AS Compras12Meses,
                       COUNT(*) AS Comprobantes12Meses,
                       MAX(Fecha) AS UltimaCompra
                FROM dir.vw_Compras
                WHERE Fecha >= @Desde AND (@IncluirProformas = 1 OR EsProforma = 0) AND ProveedorCodigo = @P
                """, p, ct)).Filas.Single();

            var t = new Dictionary<string, object?>(datos);
            foreach (var (k, v) in deuda) t[k] = v;
            foreach (var (k, v) in compras) t[k] = v;
            r.Totales = t;

            if (abiertos.Truncado) r.Advertencias.Add($"Tiene más de {abiertos.Filas.Count} comprobantes abiertos: la lista está recortada (los totales son completos).");
            if (Convert.ToInt32(datos["DadoDeBaja"]) == 1) r.Advertencias.Add("El proveedor está dado de baja.");
            if (Convert.ToDecimal(deuda["DeudaNeta"]) < 0) r.Advertencias.Add("El saldo es a nuestro favor: hay más créditos sin aplicar que facturas impagas.");
            if (compras["UltimaCompra"] is null) r.Advertencias.Add("No tiene compras en los últimos 12 meses.");
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "compras_articulo", Title = "Compras de un artículo", ReadOnly = true, Idempotent = true)]
    [Description("Historial de compras de UN artículo: a qué proveedor, cuándo, cuánto y a qué costo unitario neto (sin IVA), de la más reciente a la más vieja, " +
                 "con último costo, costo promedio ponderado y proveedores distintos. Usarla para '¿a quién le compro X y a cuánto?', '¿cuánto pagué X la última vez?' " +
                 "o '¿cómo subió el costo de X?'. Pide el código exacto: obtenerlo antes con buscar_articulos.")]
    public Task<ToolResponse> ComprasArticulo(
        [Description("Código exacto del artículo (columna ArticuloCodigo de buscar_articulos).")] string articulo,
        [Description("Fecha inicial inclusive, AAAA-MM-DD. Por defecto: 12 meses atrás.")] string? desde = null,
        [Description("Fecha final inclusive, AAAA-MM-DD. Por defecto: hoy.")] string? hasta = null,
        [Description("Cuántas compras listar (por defecto 20).")] int? top = null,
        CancellationToken ct = default)
        => runner.RunAsync("compras_articulo", new { articulo, desde, hasta, top }, async cfg =>
        {
            var codigo = Parametros.Codigo(articulo, "articulo");
            var (d, h) = Parametros.Rango(desde ?? DateTime.Today.AddMonths(-12).ToString("yyyy-MM-dd"), hasta ?? DateTime.Today.ToString("yyyy-MM-dd"));
            var r = new ToolResponse
            {
                Criterio = "Solo compras con línea de artículo (facturas, notas de crédito y proformas de compra). CostoUnitario = precio neto unitario " +
                           "de compra, sin IVA y con el descuento de la línea. Las notas de crédito restan cantidad. " +
                           (cfg.IncluirProformas ? "Incluye proformas." : "Sin proformas.")
            };
            var t = top ?? 20;
            if (t < 1) throw new ToolUserException("'top' debe ser 1 o más.");
            var p = new Dictionary<string, object?>
            {
                ["A"] = codigo, ["Desde"] = d, ["Hasta"] = h, ["IncluirProformas"] = cfg.IncluirProformas ? 1 : 0,
                ["Top"] = Math.Min(t, cfg.TopMaximo)
            };
            const string filtro = "ArticuloCodigo = @A AND Fecha >= @Desde AND Fecha < @Hasta AND (@IncluirProformas = 1 OR EsProforma = 0)";
            var res = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Top) Fecha, Tc, Comprobante, ProveedorCodigo, ProveedorNombre AS Proveedor,
                       CAST(Cantidad AS decimal(18,2)) AS Cantidad, CAST(CostoUnitario AS decimal(18,2)) AS CostoUnitario, NetoSinIva
                FROM dir.vw_ComprasDetalle
                WHERE {filtro}
                ORDER BY Fecha DESC, Comprobante
                """, p, ct);
            r.Filas = res.Filas;
            var tot = (await sql.QueryAsync(cfg, $"""
                SELECT CAST(ISNULL(SUM(Cantidad), 0) AS decimal(18,2)) AS CantidadComprada,
                       CAST(ISNULL(SUM(NetoSinIva), 0) AS decimal(18,2)) AS NetoComprado,
                       CAST(CASE WHEN SUM(Cantidad) = 0 THEN NULL ELSE SUM(NetoSinIva) / SUM(Cantidad) END AS decimal(18,2)) AS CostoPromedioPonderado,
                       COUNT(DISTINCT ProveedorCodigo) AS Proveedores,
                       COUNT(*) AS Compras,
                       MAX(Fecha) AS UltimaCompra
                FROM dir.vw_ComprasDetalle
                WHERE {filtro}
                """, p, ct)).Filas.Single();
            tot["UltimoCosto"] = res.Filas.FirstOrDefault(f => Convert.ToDecimal(f["Cantidad"]) > 0)?["CostoUnitario"];
            r.Totales = tot;
            if (t > cfg.TopMaximo) r.Advertencias.Add($"Se pidieron {t} compras; el máximo es {cfg.TopMaximo}.");
            if (Convert.ToInt32(tot["Compras"]) == 0)
                r.Advertencias.Add("No hay compras de ese artículo en el período. Probar con un período más largo o verificar el código con buscar_articulos.");
            else if (Convert.ToInt32(tot["Compras"]) > res.Filas.Count)
                r.Advertencias.Add($"Hay {tot["Compras"]} compras en el período; se listan las {res.Filas.Count} más recientes (los totales son completos).");
            return r;
        }, ct);
}

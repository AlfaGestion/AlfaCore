using System.ComponentModel;
using AlfaCore.Mcp.Infra;
using ModelContextProtocol.Server;

namespace AlfaCore.Mcp.Tools;

/// <summary>
/// Busquedas por texto (para que el modelo resuelva "Perez" o "tornillo 8mm" a un codigo exacto) y fichas de un cliente / articulo.
/// Solo usan las vistas dir.* que ya existen (no requieren scripts nuevos). El SQL es parametrizado: lo unico que se arma con texto
/// son los nombres de parametro @P0..@P4, nunca lo que escribe el modelo.
/// </summary>
[McpServerToolType]
public sealed class FichaTools(ToolRunner runner, SqlExecutor sql)
{
    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "buscar_clientes", Title = "Buscar clientes", ReadOnly = true, Idempotent = true)]
    [Description("Busca clientes por parte del nombre / razón social, del código o del CUIT/DNI. Todas las palabras deben aparecer, en cualquier orden " +
                 "(no distingue tildes ni mayúsculas). Usarla SIEMPRE antes de las herramientas que piden el código de un cliente " +
                 "(ficha_cliente, o el filtro 'cliente' de ventas_resumen / ranking_articulos). Si hay varios parecidos, preguntar al usuario cuál es.")]
    public Task<ToolResponse> BuscarClientes(
        [Description("Texto a buscar, ej. 'perez' o 'ferreteria norte' o un CUIT.")] string texto,
        [Description("Cuántos resultados devolver (por defecto 10).")] int? top = null,
        CancellationToken ct = default)
        => runner.RunAsync("buscar_clientes", new { texto, top }, async cfg =>
        {
            var palabras = Parametros.Palabras(texto, "texto");
            var r = new ToolResponse { Criterio = "Coincidencia por nombre, código o documento. Primero el código exacto, luego los activos, por nombre. DadoDeBaja = 1: cliente dado de baja." };
            var t = Parametros.Top(top, cfg, r);
            var p = new Dictionary<string, object?> { ["Max"] = t + 1, ["Texto"] = texto.Trim() };
            var cond = new List<string>();
            for (var i = 0; i < palabras.Length; i++)
            {
                p[$"P{i}"] = Parametros.Contiene(palabras[i]);
                p[$"Q{i}"] = Parametros.Contiene(palabras[i].Replace("-", "").Replace(".", ""));
                cond.Add($"(RazonSocial LIKE @P{i} OR ClienteCodigo LIKE @P{i} OR REPLACE(REPLACE(Documento, N'-', N''), N'.', N'') LIKE @Q{i})");
            }
            var where = string.Join(" AND ", cond);
            var res = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Max) ClienteCodigo, RazonSocial, Documento, CondicionIva, Localidad, Provincia, VendedorNombre,
                       CASE WHEN ISNULL(DadoDeBaja, 0) = 0 THEN 0 ELSE 1 END AS DadoDeBaja
                FROM dir.vw_Clientes
                WHERE {where}
                ORDER BY CASE WHEN ClienteCodigo = @Texto THEN 0 ELSE 1 END, CASE WHEN ISNULL(DadoDeBaja, 0) = 0 THEN 0 ELSE 1 END, RazonSocial
                """, p, ct, maxFilas: t);
            r.Filas = res.Filas;
            r.Truncado = res.Truncado;
            if (res.Truncado) r.Advertencias.Add($"Hay más de {t} coincidencias: mostrar estas y pedir al usuario un dato más preciso.");
            if (res.Filas.Count == 0) r.Advertencias.Add("Ningún cliente coincide. Probar con menos palabras o solo una parte del nombre.");
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "buscar_articulos", Title = "Buscar artículos", ReadOnly = true, Idempotent = true)]
    [Description("Busca artículos por parte de la descripción o del código. Todas las palabras deben aparecer, en cualquier orden (no distingue tildes ni mayúsculas). " +
                 "Usarla SIEMPRE antes de las herramientas que piden el código de un artículo (ficha_articulo, o el filtro 'articulo' de ventas_resumen / clientes_top). " +
                 "Si hay varios parecidos, preguntar al usuario cuál es.")]
    public Task<ToolResponse> BuscarArticulos(
        [Description("Texto a buscar, ej. 'tornillo 8' o 'cable utp'.")] string texto,
        [Description("Cuántos resultados devolver (por defecto 10).")] int? top = null,
        CancellationToken ct = default)
        => runner.RunAsync("buscar_articulos", new { texto, top }, async cfg =>
        {
            var palabras = Parametros.Palabras(texto, "texto");
            var r = new ToolResponse { Criterio = "Coincidencia por descripción o código. Primero el código exacto, luego los no suspendidos, por descripción. CostoActual = costo vigente hoy." };
            var t = Parametros.Top(top, cfg, r);
            var p = new Dictionary<string, object?> { ["Max"] = t + 1, ["Texto"] = texto.Trim() };
            var cond = new List<string>();
            for (var i = 0; i < palabras.Length; i++)
            {
                p[$"P{i}"] = Parametros.Contiene(palabras[i]);
                cond.Add($"(Descripcion LIKE @P{i} OR ArticuloCodigo LIKE @P{i})");
            }
            var where = string.Join(" AND ", cond);
            var res = await sql.QueryAsync(cfg, $"""
                SELECT TOP (@Max) ArticuloCodigo, Descripcion, RubroNombre AS Rubro, Unidad, Moneda,
                       CAST(CostoActual AS decimal(18,2)) AS CostoActual, CAST(Precio1 AS decimal(18,2)) AS Precio1,
                       CASE WHEN ISNULL(Suspendido, 0) = 0 THEN 0 ELSE 1 END AS Suspendido
                FROM dir.vw_Articulos
                WHERE {where}
                ORDER BY CASE WHEN ArticuloCodigo = @Texto THEN 0 ELSE 1 END, CASE WHEN ISNULL(Suspendido, 0) = 0 THEN 0 ELSE 1 END, Descripcion
                """, p, ct, maxFilas: t);
            r.Filas = res.Filas;
            r.Truncado = res.Truncado;
            if (res.Truncado) r.Advertencias.Add($"Hay más de {t} coincidencias: mostrar estas y pedir al usuario un dato más preciso.");
            if (res.Filas.Count == 0) r.Advertencias.Add("Ningún artículo coincide. Probar con menos palabras o solo una parte de la descripción.");
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "ficha_cliente", Title = "Ficha y cuenta corriente de un cliente", ReadOnly = true, Idempotent = true)]
    [Description("Ficha de UN cliente: datos, deuda de hoy (total, vencida, vencida a más de 90 días, límite de crédito y disponible), " +
                 "compras de los últimos 12 meses, y la lista de comprobantes abiertos de su cuenta corriente con vencimiento y días de atraso. " +
                 "Usarla para '¿cuánto me debe X?', '¿qué facturas tiene impagas X?' o 'contame de X'. Pide el código exacto: obtenerlo antes con buscar_clientes.")]
    public Task<ToolResponse> FichaCliente(
        [Description("Código exacto del cliente (columna ClienteCodigo de buscar_clientes).")] string cliente,
        CancellationToken ct = default)
        => runner.RunAsync("ficha_cliente", new { cliente }, async cfg =>
        {
            var codigo = Parametros.Codigo(cliente, "cliente");
            var p = new Dictionary<string, object?>
            {
                ["C"] = codigo,
                ["Desde"] = DateTime.Today.AddMonths(-12),
                ["IncluirProformas"] = cfg.IncluirProformas ? 1 : 0
            };
            var datos = (await sql.QueryAsync(cfg, """
                SELECT ClienteCodigo, RazonSocial, Documento, CondicionIva, Localidad, Provincia, VendedorNombre,
                       CAST(LimiteCredito AS decimal(18,2)) AS LimiteCredito, CASE WHEN ISNULL(DadoDeBaja, 0) = 0 THEN 0 ELSE 1 END AS DadoDeBaja
                FROM dir.vw_Clientes WHERE ClienteCodigo = @C
                """, p, ct)).Filas.FirstOrDefault()
                ?? throw new ToolUserException($"No existe el cliente con código '{codigo}'. Buscarlo primero con buscar_clientes.");

            var r = new ToolResponse
            {
                Criterio = "Deuda = saldo de cuenta corriente HOY en pesos (los créditos pendientes restan). Vencido = vencimiento anterior a hoy. " +
                           "Filas = comprobantes con saldo abierto, del más viejo al más nuevo. Compras12Meses = ventas netas sin IVA a este cliente desde " +
                           $"{p["Desde"]:yyyy-MM-dd}" + (cfg.IncluirProformas ? " (incluye proformas)." : " (sin proformas).")
            };

            var abiertos = await sql.QueryAsync(cfg, """
                SELECT Tc, Comprobante, Fecha, Vencimiento, Saldo, DiasVencido, Tramo
                FROM dir.vw_CuentaCorriente WHERE ClienteCodigo = @C
                ORDER BY Fecha, Comprobante
                """, p, ct);
            r.Filas = abiertos.Filas;
            r.Truncado = abiertos.Truncado;

            Dictionary<string, object?> deuda;
            if (!abiertos.Truncado)
            {
                decimal Suma(Func<Dictionary<string, object?>, bool> f) => abiertos.Filas.Where(f).Sum(x => Convert.ToDecimal(x["Saldo"]));
                deuda = new Dictionary<string, object?>
                {
                    ["Deuda"] = Suma(_ => true),
                    ["Vencido"] = Suma(x => Convert.ToInt32(x["DiasVencido"]) > 0),
                    ["VencidoMasDe90"] = Suma(x => Convert.ToInt32(x["DiasVencido"]) > 90),
                    ["ComprobantesAbiertos"] = abiertos.Filas.Count
                };
            }
            else
            {
                r.Advertencias.Add($"Tiene más de {abiertos.Filas.Count} comprobantes abiertos: la lista está recortada (los totales son completos).");
                deuda = (await sql.QueryAsync(cfg, """
                    SELECT CAST(ISNULL(SUM(Saldo), 0) AS decimal(18,2)) AS Deuda,
                           CAST(ISNULL(SUM(CASE WHEN DiasVencido > 0 THEN Saldo ELSE 0 END), 0) AS decimal(18,2)) AS Vencido,
                           CAST(ISNULL(SUM(CASE WHEN DiasVencido > 90 THEN Saldo ELSE 0 END), 0) AS decimal(18,2)) AS VencidoMasDe90,
                           COUNT(*) AS ComprobantesAbiertos
                    FROM dir.vw_CuentaCorriente WHERE ClienteCodigo = @C
                    """, p, ct)).Filas.Single();
            }

            var ventas = (await sql.QueryAsync(cfg, """
                SELECT CAST(ISNULL(SUM(NetoSinIva), 0) AS decimal(18,2)) AS Compras12Meses,
                       CAST(ISNULL(SUM(Margen), 0) AS decimal(18,2)) AS Margen12Meses,
                       COUNT(DISTINCT Tc + N'|' + Comprobante) AS Comprobantes12Meses,
                       MAX(Fecha) AS UltimaCompra
                FROM dir.vw_Ventas
                WHERE Fecha >= @Desde AND (@IncluirProformas = 1 OR EsProforma = 0) AND ClienteCodigo = @C
                """, p, ct)).Filas.Single();

            var t = new Dictionary<string, object?>(datos);
            foreach (var (k, v) in deuda) t[k] = v;
            var limite = Convert.ToDecimal(datos["LimiteCredito"] ?? 0m);
            t["DisponibleCredito"] = limite > 0 ? limite - Convert.ToDecimal(deuda["Deuda"]) : null;
            foreach (var (k, v) in ventas) t[k] = v;
            r.Totales = t;

            if (Convert.ToInt32(datos["DadoDeBaja"]) == 1) r.Advertencias.Add("El cliente está dado de baja.");
            if (limite > 0 && Convert.ToDecimal(deuda["Deuda"]) > limite) r.Advertencias.Add("La deuda supera el límite de crédito.");
            if (ventas["UltimaCompra"] is null) r.Advertencias.Add("No tiene compras en los últimos 12 meses.");
            return r;
        }, ct);

    // ------------------------------------------------------------------------------------------------------------------
    [McpServerTool(Name = "ficha_articulo", Title = "Ficha de un artículo", ReadOnly = true, Idempotent = true)]
    [Description("Ficha de UN artículo: rubro, costo actual, precios de lista, punto de pedido, stock por depósito y ventas de los últimos 12 meses " +
                 "(cantidad, facturación, margen, última venta). Usarla para '¿cuánto stock hay de X?', '¿a cuánto lo vendo?' o 'contame de X'. " +
                 "Pide el código exacto: obtenerlo antes con buscar_articulos.")]
    public Task<ToolResponse> FichaArticulo(
        [Description("Código exacto del artículo (columna ArticuloCodigo de buscar_articulos).")] string articulo,
        CancellationToken ct = default)
        => runner.RunAsync("ficha_articulo", new { articulo }, async cfg =>
        {
            var codigo = Parametros.Codigo(articulo, "articulo");
            var p = new Dictionary<string, object?>
            {
                ["A"] = codigo,
                ["Desde"] = DateTime.Today.AddMonths(-12),
                ["IncluirProformas"] = cfg.IncluirProformas ? 1 : 0
            };
            var datos = (await sql.QueryAsync(cfg, """
                SELECT ArticuloCodigo, Descripcion, RubroNombre AS Rubro, FamiliaCodigo, Unidad, Moneda,
                       CAST(CostoActual AS decimal(18,2)) AS CostoActual,
                       CAST(Precio1 AS decimal(18,2)) AS Precio1, CAST(Precio2 AS decimal(18,2)) AS Precio2, CAST(Precio3 AS decimal(18,2)) AS Precio3,
                       CAST(PuntoPedido AS decimal(18,2)) AS PuntoPedido, CAST(PuntoMaximo AS decimal(18,2)) AS PuntoMaximo,
                       CASE WHEN ISNULL(Suspendido, 0) = 0 THEN 0 ELSE 1 END AS Suspendido
                FROM dir.vw_Articulos WHERE ArticuloCodigo = @A
                """, p, ct)).Filas.FirstOrDefault()
                ?? throw new ToolUserException($"No existe el artículo con código '{codigo}'. Buscarlo primero con buscar_articulos.");

            var r = new ToolResponse
            {
                Criterio = "Filas = stock por depósito (unidad base). CostoActual y Precio1..3 son los valores vigentes HOY del maestro, en la moneda indicada " +
                           "(los precios de lista pueden incluir o no IVA según cómo los cargue la empresa). Ventas de los últimos 12 meses desde " +
                           $"{p["Desde"]:yyyy-MM-dd}, netas sin IVA, con signo (las notas de crédito restan)."
            };
            var stock = await sql.QueryAsync(cfg, """
                SELECT DepositoCodigo, DepositoNombre, CAST(Stock AS decimal(18,2)) AS Stock
                FROM dir.vw_Stock WHERE ArticuloCodigo = @A
                ORDER BY DepositoNombre
                """, p, ct);
            r.Filas = stock.Filas;
            r.Truncado = stock.Truncado;

            var ventas = (await sql.QueryAsync(cfg, """
                SELECT CAST(ISNULL(SUM(Cantidad), 0) AS decimal(18,2)) AS CantidadVendida12Meses,
                       CAST(ISNULL(SUM(NetoSinIva), 0) AS decimal(18,2)) AS Facturacion12Meses,
                       CAST(ISNULL(SUM(Margen), 0) AS decimal(18,2)) AS Margen12Meses,
                       COUNT(DISTINCT ClienteCodigo) AS Clientes12Meses,
                       MAX(Fecha) AS UltimaVenta
                FROM dir.vw_Ventas
                WHERE Fecha >= @Desde AND (@IncluirProformas = 1 OR EsProforma = 0) AND ArticuloCodigo = @A
                """, p, ct)).Filas.Single();

            var total = stock.Filas.Sum(x => Convert.ToDecimal(x["Stock"]));
            var t = new Dictionary<string, object?>(datos) { ["StockTotal"] = total };
            foreach (var (k, v) in ventas) t[k] = v;
            r.Totales = t;

            if (Convert.ToInt32(datos["Suspendido"]) == 1) r.Advertencias.Add("El artículo está suspendido.");
            var pp = Convert.ToDecimal(datos["PuntoPedido"]);
            if (pp > 0 && total <= pp) r.Advertencias.Add($"Stock total ({total:0.##}) en o por debajo del punto de pedido ({pp:0.##}): reponer.");
            if (stock.Filas.Count == 0) r.Advertencias.Add("No tiene movimientos de stock registrados.");
            return r;
        }, ct);
}

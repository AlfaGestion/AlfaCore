using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class InformesIaService
{
    private QueryDefinition BuildProvidersGrowth(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var prior = ComputePriorPeriodFilters(filters);
        var sql = $"""
            SELECT TOP ({MaxRows})
                currentData.Codigo,
                currentData.Proveedor,
                currentData.TotalActual,
                ISNULL(priorData.TotalAnterior, 0) AS TotalAnterior,
                CASE
                    WHEN ISNULL(priorData.TotalAnterior, 0) > 0
                        THEN (currentData.TotalActual - priorData.TotalAnterior) / priorData.TotalAnterior
                    ELSE NULL
                END AS Variacion
            FROM (
                SELECT
                    c.CUENTA AS Codigo,
                    COALESCE(NULLIF(MAX(c.RAZON_SOCIAL), ''), c.CUENTA) AS Proveedor,
                    SUM(c.ImporteDashboard) AS TotalActual
                FROM vw_compras_cabecera_dashboard c
                WHERE {BuildHeaderFilterClause("c", "Cur")}
                GROUP BY c.CUENTA
            ) currentData
            LEFT JOIN (
                SELECT
                    c.CUENTA AS Codigo,
                    SUM(c.ImporteDashboard) AS TotalAnterior
                FROM vw_compras_cabecera_dashboard c
                WHERE {BuildHeaderFilterClause("c", "Pr")}
                GROUP BY c.CUENTA
            ) priorData ON priorData.Codigo = currentData.Codigo
            ORDER BY
                CASE
                    WHEN ISNULL(priorData.TotalAnterior, 0) > 0
                        THEN (currentData.TotalActual - priorData.TotalAnterior) / priorData.TotalAnterior
                    ELSE -1
                END DESC,
                currentData.TotalActual DESC
            """;

        return new QueryDefinition("providers-growth", "Proveedores con mayor crecimiento", "Variación del gasto contra el período anterior", "ranking", sql, filters, prior,
            ColumnSet(("Codigo", "Código", "texto", "left"), ("Proveedor", "Proveedor", "texto", "left"), ("TotalActual", "Total actual", "moneda", "right"), ("TotalAnterior", "Total anterior", "moneda", "right"), ("Variacion", "Variación", "porcentaje", "right")),
            "bar", "Proveedor", "Variacion", "porcentaje", ResolveIncludeChart(preferencias, normalized),
            AliasMap(("codigo", "Codigo"), ("cuenta", "Codigo"), ("proveedor", "Proveedor"), ("nombre", "Proveedor"), ("total actual", "TotalActual"), ("total anterior", "TotalAnterior"), ("variacion", "Variacion")),
            ["vw_compras_cabecera_dashboard"],
            Suggestions.Where(x => x.Categoria is "Ranking" or "Comparación").Take(4).ToList());
    }

    private QueryDefinition BuildArticlesPriceIncrease(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var prior = ComputePriorPeriodFilters(filters);
        var sql = $"""
            SELECT TOP ({MaxRows})
                currentData.IdArticulo,
                currentData.Articulo,
                currentData.PrecioActual,
                ISNULL(priorData.PrecioAnterior, 0) AS PrecioAnterior,
                CASE
                    WHEN ISNULL(priorData.PrecioAnterior, 0) > 0
                        THEN (currentData.PrecioActual - priorData.PrecioAnterior) / priorData.PrecioAnterior
                    ELSE NULL
                END AS Variacion,
                currentData.TotalComprado
            FROM (
                SELECT
                    LTRIM(RTRIM(d.IDARTICULO)) AS IdArticulo,
                    COALESCE(NULLIF(MAX(d.DESCRIPCION_ARTICULO), ''), LTRIM(RTRIM(d.IDARTICULO))) AS Articulo,
                    AVG(CAST(d.COSTO AS decimal(18,4))) AS PrecioActual,
                    SUM(d.TotalDashboard) AS TotalComprado
                FROM vw_compras_detalle_dashboard d
                WHERE {BuildDetailFilterClause("d", "Cur")}
                GROUP BY LTRIM(RTRIM(d.IDARTICULO))
            ) currentData
            LEFT JOIN (
                SELECT
                    LTRIM(RTRIM(d.IDARTICULO)) AS IdArticulo,
                    AVG(CAST(d.COSTO AS decimal(18,4))) AS PrecioAnterior
                FROM vw_compras_detalle_dashboard d
                WHERE {BuildDetailFilterClause("d", "Pr")}
                GROUP BY LTRIM(RTRIM(d.IDARTICULO))
            ) priorData ON priorData.IdArticulo = currentData.IdArticulo
            ORDER BY
                CASE
                    WHEN ISNULL(priorData.PrecioAnterior, 0) > 0
                        THEN (currentData.PrecioActual - priorData.PrecioAnterior) / priorData.PrecioAnterior
                    ELSE -1
                END DESC,
                currentData.TotalComprado DESC
            """;

        return new QueryDefinition("articles-price-increase", "Artículos con mayor aumento", "Variación de costo promedio respecto del período anterior", "variacion", sql, filters, prior,
            ColumnSet(("IdArticulo", "Código", "texto", "left"), ("Articulo", "Artículo", "texto", "left"), ("PrecioActual", "Precio actual", "moneda", "right"), ("PrecioAnterior", "Precio anterior", "moneda", "right"), ("Variacion", "Variación", "porcentaje", "right"), ("TotalComprado", "Total comprado", "moneda", "right")),
            "bar", "Articulo", "Variacion", "porcentaje", ResolveIncludeChart(preferencias, normalized),
            AliasMap(("codigo", "IdArticulo"), ("articulo", "Articulo"), ("descripcion", "Articulo"), ("precio actual", "PrecioActual"), ("precio anterior", "PrecioAnterior"), ("variacion", "Variacion"), ("total", "TotalComprado")),
            ["vw_compras_detalle_dashboard"],
            Suggestions.Where(x => x.Categoria is "Variación" or "Comparación").Take(4).ToList());
    }

    private QueryDefinition BuildRubrosShare(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var sql = $"""
            SELECT TOP ({MaxRows})
                COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro') AS Rubro,
                SUM(d.TotalDashboard) AS Total,
                CASE WHEN totals.TotalGeneral > 0 THEN SUM(d.TotalDashboard) / totals.TotalGeneral ELSE 0 END AS Participacion
            FROM vw_compras_detalle_dashboard d
            CROSS JOIN (
                SELECT ISNULL(SUM(d2.TotalDashboard), 0) AS TotalGeneral
                FROM vw_compras_detalle_dashboard d2
                WHERE {BuildDetailFilterClause("d2", "Cur")}
            ) totals
            WHERE {BuildDetailFilterClause("d", "Cur")}
            GROUP BY COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro'), totals.TotalGeneral
            ORDER BY SUM(d.TotalDashboard) DESC
            """;

        return new QueryDefinition("rubros-share", "Rubros con más participación", "Concentración del gasto por rubro", "concentracion", sql, filters, null,
            ColumnSet(("Rubro", "Rubro", "texto", "left"), ("Total", "Total", "moneda", "right"), ("Participacion", "Participación", "porcentaje", "right")),
            "bar", "Rubro", "Participacion", "porcentaje", ResolveIncludeChart(preferencias, normalized),
            AliasMap(("rubro", "Rubro"), ("total", "Total"), ("participacion", "Participacion")),
            ["vw_compras_detalle_dashboard"],
            Suggestions.Where(x => x.Categoria is "Concentración" or "Comparación").Take(4).ToList());
    }

    private QueryDefinition BuildFamiliesGrowth(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var prior = ComputePriorPeriodFilters(filters);
        var sql = $"""
            SELECT TOP ({MaxRows})
                currentData.Familia,
                COALESCE(NULLIF(fj.Descripcion, ''), currentData.Familia) AS Descripcion,
                currentData.TotalActual,
                ISNULL(priorData.TotalAnterior, 0) AS TotalAnterior,
                CASE
                    WHEN ISNULL(priorData.TotalAnterior, 0) > 0
                        THEN (currentData.TotalActual - priorData.TotalAnterior) / priorData.TotalAnterior
                    ELSE NULL
                END AS Variacion
            FROM (
                SELECT d.FAMILIA AS Familia, SUM(d.TotalDashboard) AS TotalActual
                FROM vw_compras_detalle_dashboard d
                WHERE {BuildDetailFilterClause("d", "Cur")}
                GROUP BY d.FAMILIA
            ) currentData
            LEFT JOIN (
                SELECT d.FAMILIA AS Familia, SUM(d.TotalDashboard) AS TotalAnterior
                FROM vw_compras_detalle_dashboard d
                WHERE {BuildDetailFilterClause("d", "Pr")}
                GROUP BY d.FAMILIA
            ) priorData ON priorData.Familia = currentData.Familia
            LEFT JOIN vw_familias_jerarquia fj ON fj.IdFamilia = currentData.Familia
            ORDER BY
                CASE
                    WHEN ISNULL(priorData.TotalAnterior, 0) > 0
                        THEN (currentData.TotalActual - priorData.TotalAnterior) / priorData.TotalAnterior
                    ELSE -1
                END DESC,
                currentData.TotalActual DESC
            """;

        return new QueryDefinition("families-growth", "Familias que más crecieron", "Variación contra el período anterior", "comparacion", sql, filters, prior,
            ColumnSet(("Familia", "Familia", "texto", "left"), ("Descripcion", "Descripción", "texto", "left"), ("TotalActual", "Total actual", "moneda", "right"), ("TotalAnterior", "Total anterior", "moneda", "right"), ("Variacion", "Variación", "porcentaje", "right")),
            "bar", "Descripcion", "Variacion", "porcentaje", ResolveIncludeChart(preferencias, normalized),
            AliasMap(("familia", "Familia"), ("descripcion", "Descripcion"), ("total actual", "TotalActual"), ("total anterior", "TotalAnterior"), ("variacion", "Variacion")),
            ["vw_compras_detalle_dashboard", "vw_familias_jerarquia"],
            Suggestions.Where(x => x.Categoria is "Comparación" or "Concentración").Take(4).ToList());
    }

    private QueryDefinition BuildTopComprobantes(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var sql = $"""
            SELECT TOP ({MaxRows})
                ISNULL(c.TC, '') + ' ' + ISNULL(c.IDCOMPROBANTE, '') AS Comprobante,
                COALESCE(NULLIF(c.RAZON_SOCIAL, ''), c.CUENTA) AS Proveedor,
                c.FECHA AS Fecha,
                c.ImporteDashboard AS Importe,
                c.EstadoComprobante AS Estado
            FROM vw_compras_cabecera_dashboard c
            WHERE {BuildHeaderFilterClause("c", "Cur")}
            ORDER BY c.ImporteDashboard DESC, c.FECHA DESC
            """;

        return new QueryDefinition("top-comprobantes", "Comprobantes con mayor importe", "Máximos importes dentro del período filtrado", "top", sql, filters, null,
            ColumnSet(("Comprobante", "Comprobante", "texto", "left"), ("Proveedor", "Proveedor", "texto", "left"), ("Fecha", "Fecha", "fecha", "left"), ("Importe", "Importe", "moneda", "right"), ("Estado", "Estado", "texto", "left")),
            "bar", "Comprobante", "Importe", "moneda", ResolveIncludeChart(preferencias, normalized),
            AliasMap(("comprobante", "Comprobante"), ("proveedor", "Proveedor"), ("nombre", "Proveedor"), ("fecha", "Fecha"), ("importe", "Importe"), ("total", "Importe"), ("estado", "Estado")),
            ["vw_compras_cabecera_dashboard"],
            Suggestions.Where(x => x.Categoria is "Top" or "Ranking").Take(4).ToList());
    }

    private QueryDefinition BuildUsersActivity(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var sql = $"""
            SELECT TOP ({MaxRows})
                COALESCE(NULLIF(c.USUARIO, ''), 'Sin usuario') AS Usuario,
                COUNT(*) AS Comprobantes,
                SUM(c.ImporteDashboard) AS ImporteTotal,
                AVG(CAST(c.ImporteDashboard AS decimal(18,2))) AS TicketPromedio,
                MAX(c.FECHA) AS UltimaActividad
            FROM vw_compras_cabecera_dashboard c
            WHERE {BuildHeaderFilterClause("c", "Cur")}
            GROUP BY COALESCE(NULLIF(c.USUARIO, ''), 'Sin usuario')
            ORDER BY COUNT(*) DESC, SUM(c.ImporteDashboard) DESC
            """;

        return new QueryDefinition("users-activity", "Usuarios con más actividad", "Volumen de comprobantes y monto cargado", "actividad", sql, filters, null,
            ColumnSet(("Usuario", "Usuario", "texto", "left"), ("Comprobantes", "Comprobantes", "numero", "right"), ("ImporteTotal", "Importe total", "moneda", "right"), ("TicketPromedio", "Ticket promedio", "moneda", "right"), ("UltimaActividad", "Última actividad", "fecha", "left")),
            "bar", "Usuario", "Comprobantes", "numero", ResolveIncludeChart(preferencias, normalized),
            AliasMap(("usuario", "Usuario"), ("comprobantes", "Comprobantes"), ("importe total", "ImporteTotal"), ("ticket promedio", "TicketPromedio"), ("ultima actividad", "UltimaActividad")),
            ["vw_compras_cabecera_dashboard"],
            Suggestions.Where(x => x.Categoria is "Actividad" or "Ranking").Take(4).ToList());
    }

    private QueryDefinition BuildProviderConcentration(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var sql = $"""
            SELECT TOP ({MaxRows})
                currentData.Proveedor,
                currentData.Total,
                CASE WHEN totals.TotalGeneral > 0 THEN currentData.Total / totals.TotalGeneral ELSE 0 END AS Participacion
            FROM (
                SELECT
                    COALESCE(NULLIF(c.RAZON_SOCIAL, ''), c.CUENTA) AS Proveedor,
                    SUM(c.ImporteDashboard) AS Total
                FROM vw_compras_cabecera_dashboard c
                WHERE {BuildHeaderFilterClause("c", "Cur")}
                GROUP BY COALESCE(NULLIF(c.RAZON_SOCIAL, ''), c.CUENTA)
            ) currentData
            CROSS JOIN (
                SELECT ISNULL(SUM(c.ImporteDashboard), 0) AS TotalGeneral
                FROM vw_compras_cabecera_dashboard c
                WHERE {BuildHeaderFilterClause("c", "Cur")}
            ) totals
            ORDER BY currentData.Total DESC
            """;

        return new QueryDefinition("provider-concentration", "Concentración del gasto por proveedor", "Participación del gasto entre los principales proveedores", "concentracion", sql, filters, null,
            ColumnSet(("Proveedor", "Proveedor", "texto", "left"), ("Total", "Total", "moneda", "right"), ("Participacion", "Participación", "porcentaje", "right")),
            "bar", "Proveedor", "Participacion", "porcentaje", ResolveIncludeChart(preferencias, normalized),
            AliasMap(("proveedor", "Proveedor"), ("nombre", "Proveedor"), ("total", "Total"), ("participacion", "Participacion")),
            ["vw_compras_cabecera_dashboard"],
            Suggestions.Where(x => x.Categoria == "Concentración").Take(4).ToList());
    }

    private QueryDefinition BuildMonthlyEvolution(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var scoped = CloneFilters(filters);
        scoped.FechaDesde = null;
        scoped.FechaHasta = null;
        var sql = $"""
            SELECT TOP (12)
                RIGHT('0' + CONVERT(varchar(2), MONTH(c.FECHA)), 2) + '/' + CONVERT(varchar(4), YEAR(c.FECHA)) AS Periodo,
                SUM(c.ImporteDashboard) AS Total
            FROM vw_compras_cabecera_dashboard c
            WHERE {BuildHeaderFilterClause("c", "Cur")}
            GROUP BY YEAR(c.FECHA), MONTH(c.FECHA)
            ORDER BY YEAR(c.FECHA) DESC, MONTH(c.FECHA) DESC
            """;

        return new QueryDefinition("monthly-evolution", "Evolución de compras por mes", "Serie mensual de los últimos 12 meses disponibles", "evolucion", sql, scoped, null,
            ColumnSet(("Periodo", "Período", "texto", "left"), ("Total", "Total", "moneda", "right")),
            "line", "Periodo", "Total", "moneda", ResolveIncludeChart(preferencias, normalized),
            AliasMap(("periodo", "Periodo"), ("mes", "Periodo"), ("total", "Total")),
            ["vw_compras_cabecera_dashboard"],
            Suggestions.Where(x => x.Categoria is "Evolución" or "Concentración").Take(4).ToList());
    }

    private QueryDefinition BuildRubrosVsPrior(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var prior = ComputePriorPeriodFilters(filters);
        var sql = $"""
            SELECT TOP ({MaxRows})
                currentData.Rubro,
                currentData.TotalActual,
                ISNULL(priorData.TotalAnterior, 0) AS TotalAnterior,
                currentData.TotalActual - ISNULL(priorData.TotalAnterior, 0) AS Diferencia,
                CASE
                    WHEN ISNULL(priorData.TotalAnterior, 0) > 0
                        THEN (currentData.TotalActual - priorData.TotalAnterior) / priorData.TotalAnterior
                    ELSE NULL
                END AS Variacion
            FROM (
                SELECT
                    COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro') AS Rubro,
                    SUM(d.TotalDashboard) AS TotalActual
                FROM vw_compras_detalle_dashboard d
                WHERE {BuildDetailFilterClause("d", "Cur")}
                GROUP BY COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro')
            ) currentData
            LEFT JOIN (
                SELECT
                    COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro') AS Rubro,
                    SUM(d.TotalDashboard) AS TotalAnterior
                FROM vw_compras_detalle_dashboard d
                WHERE {BuildDetailFilterClause("d", "Pr")}
                GROUP BY COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro')
            ) priorData ON priorData.Rubro = currentData.Rubro
            ORDER BY
                CASE
                    WHEN ISNULL(priorData.TotalAnterior, 0) > 0
                        THEN (currentData.TotalActual - priorData.TotalAnterior) / priorData.TotalAnterior
                    ELSE -1
                END DESC,
                currentData.TotalActual DESC
            """;

        return new QueryDefinition("rubros-vs-prior", "Rubros comparados contra el período anterior", "Diferencia absoluta y relativa por rubro", "comparacion", sql, filters, prior,
            ColumnSet(("Rubro", "Rubro", "texto", "left"), ("TotalActual", "Total actual", "moneda", "right"), ("TotalAnterior", "Total anterior", "moneda", "right"), ("Diferencia", "Diferencia", "moneda", "right"), ("Variacion", "Variación", "porcentaje", "right")),
            "bar", "Rubro", "Variacion", "porcentaje", ResolveIncludeChart(preferencias, normalized),
            AliasMap(("rubro", "Rubro"), ("total actual", "TotalActual"), ("total anterior", "TotalAnterior"), ("diferencia", "Diferencia"), ("variacion", "Variacion")),
            ["vw_compras_detalle_dashboard"],
            Suggestions.Where(x => x.Categoria == "Comparación").Take(4).ToList());
    }

    private QueryDefinition BuildComprobantesLedger(DashboardFilters filters, InformeIaPreferencesDto preferencias, string normalized)
    {
        var sql = $"""
            SELECT TOP ({MaxRows})
                c.FECHA AS FechaFactura,
                c.CUENTA AS Cuenta,
                COALESCE(NULLIF(c.RAZON_SOCIAL, ''), c.CUENTA) AS Nombre,
                c.NetoDashboard AS ImporteSinIva,
                c.IvaDashboard AS Iva,
                c.ImporteDashboard AS Total,
                c.USUARIO AS Usuario,
                c.FECHA AS FechaHora,
                c.TC AS Tc,
                c.IDCOMPROBANTE AS IdComprobante,
                c.EstadoComprobante AS Estado
            FROM vw_compras_cabecera_dashboard c
            WHERE {BuildHeaderFilterClause("c", "Cur")}
            ORDER BY c.FECHA DESC, c.IDCOMPROBANTE DESC
            """;

        return new QueryDefinition("comprobantes-ledger", "Listado de compras", "Vista tabular de comprobantes para análisis operativo", "listado", sql, filters, null,
            ColumnSet(("FechaFactura", "Fecha factura", "fecha", "left"), ("Cuenta", "Cuenta", "texto", "left"), ("Nombre", "Nombre", "texto", "left"), ("ImporteSinIva", "Importe sin IVA", "moneda", "right"), ("Iva", "IVA", "moneda", "right"), ("Total", "Total", "moneda", "right"), ("Usuario", "Usuario", "texto", "left"), ("FechaHora", "Fecha y hora", "fechahora", "left"), ("Tc", "TC", "texto", "left"), ("IdComprobante", "Comprobante", "texto", "left"), ("Estado", "Estado", "texto", "left")),
            "bar", "Nombre", "Total", "moneda", ResolveIncludeChart(preferencias, normalized),
            AliasMap(
                ("fecha factura", "FechaFactura"),
                ("fecha comprobante", "FechaFactura"),
                ("fecha emision", "FechaFactura"),
                ("fecha", "FechaFactura"),
                ("cuenta", "Cuenta"),
                ("cuenta proveedor", "Cuenta"),
                ("codigo proveedor", "Cuenta"),
                ("nombre", "Nombre"),
                ("razon social", "Nombre"),
                ("proveedor", "Nombre"),
                ("importe sin iva", "ImporteSinIva"),
                ("importe neto", "ImporteSinIva"),
                ("neto", "ImporteSinIva"),
                ("subtotal", "ImporteSinIva"),
                ("iva", "Iva"),
                ("iva credito fiscal", "Iva"),
                ("importe iva", "Iva"),
                ("total", "Total"),
                ("importe total", "Total"),
                ("monto total", "Total"),
                ("usuario", "Usuario"),
                ("usuario carga", "Usuario"),
                ("usuario que cargo", "Usuario"),
                ("fecha y hora", "FechaHora"),
                ("fecha hora", "FechaHora"),
                ("hora", "FechaHora"),
                ("tc", "Tc"),
                ("tipo comprobante", "Tc"),
                ("comprobante", "IdComprobante"),
                ("idcomprobante", "IdComprobante"),
                ("id comprobante", "IdComprobante"),
                ("numero", "IdComprobante"),
                ("numero comprobante", "IdComprobante"),
                ("nro comprobante", "IdComprobante"),
                ("estado", "Estado")),
            ["vw_compras_cabecera_dashboard"],
            Suggestions.Where(x => x.Categoria is "Top" or "Actividad" or "Comparación").Take(4).ToList());
    }

    private async Task<RawQueryResult> ExecuteQueryAsync(QueryDefinition definition, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(definition.Sql, connection) { CommandTimeout = 20 };
        AddFilterParameters(command, definition.Filters, "Cur");
        if (definition.PriorFilters is not null)
        {
            AddFilterParameters(command, definition.PriorFilters, "Pr");
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = definition.Columns.Count > 0 ? definition.Columns : InferColumns(reader);
        var rows = new List<RawRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(new RawRow(values));
            if (rows.Count >= MaxRows)
            {
                break;
            }
        }

        return new RawQueryResult(columns, rows);
    }

    private static List<InformeIaColumnDto> InferColumns(SqlDataReader reader)
    {
        var schema = reader.GetColumnSchema();
        var columns = new List<InformeIaColumnDto>(schema.Count);

        foreach (var column in schema)
        {
            var key = string.IsNullOrWhiteSpace(column.ColumnName) ? $"Columna{columns.Count + 1}" : column.ColumnName;
            var format = InferFormat(column.DataType);
            columns.Add(new InformeIaColumnDto
            {
                Key = key,
                Label = key.Replace('_', ' '),
                Format = format,
                Align = format is "numero" or "moneda" or "porcentaje" ? "right" : "left"
            });
        }

        return columns;
    }

    private static string InferFormat(Type? dataType)
    {
        if (dataType is null)
        {
            return "texto";
        }

        if (dataType == typeof(DateTime) || dataType == typeof(DateTimeOffset))
        {
            return "fechahora";
        }

        if (dataType == typeof(decimal)
            || dataType == typeof(double)
            || dataType == typeof(float)
            || dataType == typeof(short)
            || dataType == typeof(int)
            || dataType == typeof(long))
        {
            return "numero";
        }

        return "texto";
    }
}

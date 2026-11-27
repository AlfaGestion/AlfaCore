using System.ComponentModel;
using AlfaCore.Mcp.Infra;
using AlfaCore.Mcp.Security;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;

namespace AlfaCore.Mcp.Tools;

/// <summary>Consulta libre (acotada) y descripcion del esquema, para preguntas que las herramientas de negocio no cubren.</summary>
[McpServerToolType]
public sealed class SqlTools(ToolRunner runner, SqlExecutor ejecutor)
{
    [McpServerTool(Name = "describir_esquema", Title = "Describir vistas", ReadOnly = true, Idempotent = true)]
    [Description("Lista las vistas analíticas del esquema dir con sus columnas y tipos. Llamarla ANTES de consultar_sql para conocer los nombres exactos.")]
    public Task<ToolResponse> DescribirEsquema(CancellationToken ct = default)
        => runner.RunAsync("describir_esquema", null, async cfg =>
        {
            var res = await ejecutor.QueryAsync(cfg, """
                SELECT TABLE_NAME AS Vista, COLUMN_NAME AS Columna, DATA_TYPE AS Tipo
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = N'dir' AND TABLE_NAME <> N'vw_Config'
                ORDER BY TABLE_NAME, ORDINAL_POSITION
                """, null, ct, maxFilas: 2000);
            var r = new ToolResponse
            {
                Criterio = "Una fila por vista. dir.vw_Ventas = una fila por línea vendida, con signo aplicado (NC negativas), neto sin IVA, costo y margen. " +
                           "dir.vw_CuentaCorriente = saldos abiertos por comprobante. dir.vw_Stock = existencia por artículo y depósito."
            };
            foreach (var g in res.Filas.GroupBy(f => (string)f["Vista"]!))
                r.Filas.Add(new Dictionary<string, object?>
                {
                    ["Vista"] = "dir." + g.Key,
                    ["Columnas"] = string.Join(", ", g.Select(c => $"{c["Columna"]} ({c["Tipo"]})"))
                });
            return r;
        }, ct);

    [McpServerTool(Name = "consultar_sql", Title = "Consulta SQL de solo lectura", ReadOnly = true, Idempotent = true)]
    [Description("Ejecuta una consulta SELECT de T-SQL propia SOLO sobre las vistas del esquema dir (dir.vw_Ventas, dir.vw_Articulos, dir.vw_Clientes, dir.vw_CuentaCorriente, dir.vw_Stock). " +
                 "Usarla únicamente cuando ninguna otra herramienta responda la pregunta. Una sola sentencia, sin variables, sin funciones de tabla; CTEs permitidas. " +
                 "Compatible con SQL Server 2008: sin LAG/LEAD, IIF, CONCAT, FORMAT ni OFFSET/FETCH. Los literales de fecha se interpretan como AAAA-MM-DD. " +
                 "Las ventas ya vienen con signo y neto sin IVA en dir.vw_Ventas (columnas NetoSinIva, CostoTotal, Margen): sumarlas sin volver a aplicar signo. " +
                 "El resultado se recorta al máximo configurado. Llamar antes a describir_esquema para ver las columnas.")]
    public Task<ToolResponse> ConsultarSql(
        [Description("Sentencia SELECT en T-SQL, ej: SELECT TOP 10 RubroNombre, SUM(NetoSinIva) AS Ventas FROM dir.vw_Ventas WHERE Fecha >= '2026-10-01' GROUP BY RubroNombre ORDER BY 2 DESC")] string sql,
        [Description("Máximo de filas a devolver (no puede superar el máximo configurado).")] int? max_filas = null,
        CancellationToken ct = default)
        => runner.RunAsync("consultar_sql", new { sql, max_filas }, async cfg =>
        {
            if (!cfg.ConsultarSqlHabilitado)
                throw new ToolUserException("consultar_sql está deshabilitada (clave IA_MCP_CONSULTAR_SQL_HABILITADO).");
            var v = SqlSelectValidator.Validar(sql);
            if (!v.Valido) throw new ToolUserException(v.Error!);
            if (max_filas is < 1) throw new ToolUserException("'max_filas' debe ser 1 o más.");

            var r = new ToolResponse { Criterio = "Resultado de la consulta tal como la escribió el solicitante; sin signo ni filtros adicionales." };
            try
            {
                var res = await ejecutor.QueryAsync(cfg, sql, null, ct, max_filas);
                r.Filas = res.Filas;
                r.Truncado = res.Truncado;
                if (res.Truncado) r.Advertencias.Add($"Resultado recortado a {res.Filas.Count} filas. Agregar filtros, agrupar o usar TOP.");
            }
            catch (SqlException ex) when (ex.Number != -2)
            {
                // Los errores de SQL (columna inexistente, tipo, sintaxis de version) ayudan al modelo a corregirse.
                throw new ToolUserException("Error de SQL: " + ex.Message);
            }
            return r;
        }, ct);
}

using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CierreCajaService(IConfiguration configuration, ISessionService sessions,
    IPermissionService permissions, IAppEventService events) : ICierreCajaService
{
    private string ConnectionString => !string.IsNullOrWhiteSpace(sessions.GetConnectionString())
        ? sessions.GetConnectionString() : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No hay una base activa.");

    public Task<IReadOnlyList<CierreCajaOpcion>> GetUnidadesAsync(CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<CierreCajaOpcion>>("CargarUnidades", async () =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var rows = await cn.QueryAsync<CierreCajaOpcion>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(Codigo)) Codigo, ISNULL(Descripcion,'') Descripcion
                FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo))<>'' ORDER BY Codigo;
                """, cancellationToken: ct));
            return rows.AsList();
        }, ct);

    public Task<IReadOnlyList<CierreCajaOpcion>> GetCajasAsync(DateTime fecha, CancellationToken ct = default, string unidad = "")
        => ExecuteAsync<IReadOnlyList<CierreCajaOpcion>>("CargarCajas", async () =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var rows = await cn.QueryAsync<CierreCajaOpcion>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(a.IdCajas)) AS Codigo, ISNULL(c.Descripcion,'') AS Descripcion
                FROM dbo.MV_ASIENTOS a LEFT JOIN dbo.V_Ta_Cajas c ON LTRIM(RTRIM(c.IdCajas))=LTRIM(RTRIM(a.IdCajas))
                WHERE a.Fecha>=@Fecha AND a.Fecha<DATEADD(day,1,@Fecha) AND LTRIM(RTRIM(a.IdCajas))<>''
                  AND (@Unidad='' OR LTRIM(RTRIM(a.UNEGOCIO))=@Unidad)
                GROUP BY LTRIM(RTRIM(a.IdCajas)),c.Descripcion ORDER BY Codigo;
                """, new { Fecha = fecha.Date, Unidad = unidad.Trim() }, cancellationToken: ct));
            return rows.AsList();
        }, ct);

    public Task<CierreCajaPagina> ConsultarAsync(CierreCajaFiltros filtros, string seccion, int pagina = 1, int pageSize = 50, CancellationToken ct = default)
        => ConsultarCoreAsync(filtros, seccion, pagina, pageSize, false, ct);

    public Task<CierreCajaPagina> ExportarSeccionAsync(CierreCajaFiltros filtros, string seccion, CancellationToken ct = default)
        => ConsultarCoreAsync(filtros, seccion, 1, 50, true, ct);

    private Task<CierreCajaPagina> ConsultarCoreAsync(CierreCajaFiltros filtros, string seccion, int pagina, int pageSize, bool exportar, CancellationToken ct)
        => ExecuteAsync("ConsultarCierre", async () =>
        {
            var definition = CierreCajaSecciones.Activas(filtros).SingleOrDefault(s => s.Clave == seccion)
                ?? throw new ArgumentException("La sección solicitada no está habilitada en el informe.");
            if (pagina < 1 || pageSize is < 1 or > 200 || filtros.Caja.Trim().Length > 4 || filtros.UnidadNegocio.Trim().Length > 4)
                throw new ArgumentException("Revisá la caja y la página solicitada.");
            var sql = BuildSql(definition, exportar);
            await using var cn = new SqlConnection(ConnectionString);
            using var results = await cn.QueryMultipleAsync(new CommandDefinition(sql,
                BuildParameters(filtros, definition, pagina, pageSize), commandTimeout: 120, cancellationToken: ct));
            var totals = ToDictionary(await results.ReadSingleAsync());
            if (exportar && Convert.ToInt32(totals["TotalCount"]) > 1_048_570)
                throw new InvalidOperationException("El informe supera el límite de filas de Excel. Reducí los filtros.");
            var rows = (await results.ReadAsync()).Select(row => (IReadOnlyDictionary<string, object?>)ToDictionary(row)).ToArray();
            return new CierreCajaPagina(Convert.ToInt32(totals["TotalCount"]), rows, totals);
        }, ct);

    internal static string BuildSql(CierreCajaSeccion section, bool exportar = false)
    {
        // Consulta y nombres de columnas provienen exclusivamente del catálogo compilado.
        using var stream = typeof(CierreCajaService).Assembly.GetManifestResourceStream($"AlfaCore.Services.CierreCajaSql.{section.Consulta}.sql")
            ?? throw new InvalidOperationException("No se encontró la consulta del informe.");
        using var reader = new StreamReader(stream);
        var totals = string.Join("", section.Columnas.Where(c => c.Totaliza)
            .Select(c => $", COALESCE(SUM(CONVERT(decimal(28,2), [{c.Campo}])),0) AS [{c.Campo}]"));
        var alcance = string.Empty;
        if (section.Consulta is "consolidado" or "efectivo")
        {
            using var scope = typeof(CierreCajaService).Assembly.GetManifestResourceStream("AlfaCore.Services.CierreCajaSql.alcance.sql")!;
            using var scopeReader = new StreamReader(scope);
            alcance = scopeReader.ReadToEnd();
        }
        return alcance + reader.ReadToEnd() + $"\nSELECT COUNT(*) AS TotalCount{totals} FROM #Reporte;\n"
            + $"SELECT * FROM #Reporte ORDER BY {section.Orden}"
            + (exportar ? ";" : " OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;");
    }

    internal static DynamicParameters BuildParameters(CierreCajaFiltros filtros, CierreCajaSeccion section, int pagina, int pageSize)
    {
        var args = new DynamicParameters();
        args.Add("fecha", filtros.Fecha.Date, System.Data.DbType.Date);
        args.Add("idcaja", filtros.Caja.Trim(), System.Data.DbType.String, size: 4);
        args.Add("unidad", filtros.UnidadNegocio.Trim(), System.Data.DbType.String, size: 4);
        args.Add("Offset", checked((pagina - 1) * pageSize));
        args.Add("PageSize", pageSize);
        switch (section.Consulta)
        {
            case "efectivo": args.Add("initial", filtros.SaldoInicial); break;
            case "consolidado": args.Add("csaldoInicial", filtros.SaldoInicial); break;
            case "diario": args.Add("esMensual", section.Clave == "mensual"); break;
            case "ventas": case "ctacte": args.Add("cobranzas", section.Clave is "cobranzas" or "cobranzas-ctacte"); break;
            case "movimientos": args.Add("tipo", section.Clave == "egresos" ? "E" : "I"); break;
        }
        return args;
    }

    private static Dictionary<string, object?> ToDictionary(object row)
        => new((IDictionary<string, object?>)row, StringComparer.OrdinalIgnoreCase);

    private async Task<T> ExecuteAsync<T>(string action, Func<Task<T>> operation, CancellationToken ct)
    {
        try
        {
            var allowed = await permissions.GetAllowedTaskKeysAsync(ct);
            if (allowed is { Count: > 0 } && !allowed.Contains(CierreCajaSecciones.MenuKey))
                throw new InvalidOperationException("No tenés permiso para consultar el cierre de caja.");
            return await operation();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var incident = await events.LogErrorAsync("Cierre de caja", action, ex,
                "No se pudo consultar el cierre de caja.", ct: ct);
            throw new AppUserFacingException("No se pudo consultar el cierre de caja.", incident, ex);
        }
    }
}

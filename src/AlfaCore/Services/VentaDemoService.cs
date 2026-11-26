using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class VentaDemoService(
    IConfiguration configuration,
    IAppEventService appEvents) : IVentaDemoService
{
    // Se consulta en cada navegación del layout (barra DEMO): cache corto para no ir a la central siempre.
    private static readonly TimeSpan CacheDuracion = TimeSpan.FromMinutes(5);
    private static IReadOnlyList<VentaDemoDto>? _cache;
    private static DateTime _cacheVenceUtc = DateTime.MinValue;
    private static readonly SemaphoreSlim CacheLock = new(1, 1);

    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public async Task<VentaDemoDto?> GetDemoActivaAsync(string familia, CancellationToken ct = default)
    {
        var clave = (familia ?? string.Empty).Trim();
        if (clave.Length == 0)
            return null;

        var demos = await GetDemosActivasAsync(ct).ConfigureAwait(false);
        return demos.FirstOrDefault(d => string.Equals(d.Familia, clave, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<VentaDemoDto?> GetDemoPorLoginAsync(string? centralLogin, CancellationToken ct = default)
    {
        var login = (centralLogin ?? string.Empty).Trim();
        if (login.Length == 0)
            return null;

        var demos = await GetDemosActivasAsync(ct).ConfigureAwait(false);
        return demos.FirstOrDefault(d => string.Equals(d.CentralLogin, login, StringComparison.OrdinalIgnoreCase));
    }

    public string? GetClaveDemo(string familia)
    {
        var clave = (familia ?? string.Empty).Trim().ToUpperInvariant();
        if (clave.Length == 0)
            return null;

        var valor = configuration[$"VentaDemo:Claves:{clave}"];
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    private async Task<IReadOnlyList<VentaDemoDto>> GetDemosActivasAsync(CancellationToken ct)
    {
        if (_cache is not null && DateTime.UtcNow < _cacheVenceUtc)
            return _cache;

        // Sin ALFA_CENTRAL configurada (instalación propia de un cliente) no hay demos: no es un error.
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral")))
            return [];

        await CacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache is not null && DateTime.UtcNow < _cacheVenceUtc)
                return _cache;

            const string sql = """
                IF OBJECT_ID(N'dbo.VentaDemos', N'U') IS NOT NULL
                    SELECT
                        LTRIM(RTRIM(Familia)) AS Familia,
                        IdBase,
                        LTRIM(RTRIM(CentralLogin)) AS CentralLogin,
                        LTRIM(RTRIM(ISNULL(RutaInicial, N''))) AS RutaInicial,
                        LTRIM(RTRIM(ISNULL(NegocioEjemplo, N''))) AS NegocioEjemplo,
                        Activo
                    FROM dbo.VentaDemos
                    WHERE Activo = 1;
                ELSE
                    -- Script venta_demos.sql todavía no aplicado: mismo formato, sin filas.
                    SELECT
                        CAST(N'' AS nvarchar(30)) AS Familia,
                        CAST(0 AS int) AS IdBase,
                        CAST(N'' AS nvarchar(100)) AS CentralLogin,
                        CAST(N'' AS nvarchar(200)) AS RutaInicial,
                        CAST(N'' AS nvarchar(100)) AS NegocioEjemplo,
                        CAST(0 AS bit) AS Activo
                    WHERE 1 = 0;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            var filas = (await cn.QueryAsync<VentaDemoDto>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false))
                .ToArray();

            _cache = filas;
            _cacheVenceUtc = DateTime.UtcNow.Add(CacheDuracion);
            return filas;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(
                "VentaOnline",
                "LeerDemos",
                ex,
                "No se pudieron leer las demos de la página de venta.",
                null,
                AppEventSeverity.Warning,
                ct).ConfigureAwait(false);

            // Sin cache: no se reintenta en cada render durante un corte de la central.
            _cache ??= [];
            _cacheVenceUtc = DateTime.UtcNow.AddMinutes(1);
            return _cache;
        }
        finally
        {
            CacheLock.Release();
        }
    }
}

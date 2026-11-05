using System.Globalization;

namespace AlfaCore.Services;

/// <summary>
/// Endpoints de solo lectura para el resumen diario (/api/brief/*). Autenticación por Bearer token
/// fijo (Brief:Token) y conexión propia de solo lectura (Brief:ConnectionString), ambos fuera del repo.
/// Si falta alguno de los dos, las rutas responden 404 como si no existieran.
/// </summary>
public static class BriefEndpoints
{
    public static void MapBriefEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/brief").AllowAnonymous();

        group.MapGet("/soporte/pendientes", (HttpContext http, IBriefService brief, ILogger<BriefLog> logger, CancellationToken ct)
            => EjecutarAsync(http, brief, logger, "soporte/pendientes",
                async () => Results.Json(await brief.GetSoportePendientesAsync(ct))));

        group.MapGet("/actividad", (string? desde, HttpContext http, IBriefService brief, ILogger<BriefLog> logger, CancellationToken ct)
            => EjecutarAsync(http, brief, logger, "actividad", async () =>
            {
                var fecha = DateTime.Today.AddDays(-7);
                if (!string.IsNullOrWhiteSpace(desde)
                    && !DateTime.TryParseExact(desde.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
                {
                    return Results.BadRequest(new { error = "El parámetro desde debe tener el formato AAAA-MM-DD." });
                }

                return Results.Json(await brief.GetActividadAsync(fecha, ct));
            }));

        group.MapGet("/crm/oportunidades", (bool? soloMias, HttpContext http, IBriefService brief, ILogger<BriefLog> logger, CancellationToken ct)
            => EjecutarAsync(http, brief, logger, "crm/oportunidades",
                async () => Results.Json(await brief.GetOportunidadesAsync(soloMias ?? false, ct))));
    }

    private static async Task<IResult> EjecutarAsync(
        HttpContext http, IBriefService brief, ILogger<BriefLog> logger, string ruta, Func<Task<IResult>> accion)
    {
        http.Response.Headers.CacheControl = "no-store";

        if (!brief.EstaHabilitado)
            return Results.NotFound();

        if (!brief.TokenValido(LeerBearer(http.Request)))
        {
            http.Response.Headers.WWWAuthenticate = "Bearer";
            return Results.Json(new { error = "No autorizado." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        try
        {
            return await accion();
        }
        catch (BriefConfigException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            var incidente = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            logger.LogError(ex, "Brief {Ruta} falló. Incidente {Incidente}", ruta, incidente);
            return Results.Json(
                new { error = "No se pudo generar el resumen.", incidente },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string? LeerBearer(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefijo = "Bearer ";
        return header.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
            ? header[prefijo.Length..].Trim()
            : null;
    }

    /// <summary>Categoría de logging; evita pasar un tipo estático como argumento genérico.</summary>
    public sealed class BriefLog;
}

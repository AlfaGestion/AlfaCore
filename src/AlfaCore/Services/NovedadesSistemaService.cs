using System.Collections.Concurrent;
using AlfaCore.Models;

namespace AlfaCore.Services;

public interface INovedadesSistemaService
{
    /// <summary>Novedades de AlfaCore con su contenido y lo que ya leyó el usuario actual.</summary>
    Task<IReadOnlyList<NovedadSistemaDto>> GetAsync(CancellationToken ct = default);

    Task MarcarLeidaAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// Novedades de AlfaCore: el catálogo y el contenido viven en el código
/// (<see cref="NovedadesSistemaCatalogo"/> + App_Data/novedades-sistema/*.html); lo leído se guarda
/// por usuario en la base de la empresa, con el mismo registro que la campana.
/// </summary>
public sealed class NovedadesSistemaService(
    IWebHostEnvironment environment,
    ICentroAvisosService centroAvisos,
    IAppEventService appEvents) : INovedadesSistemaService
{
    private static readonly ConcurrentDictionary<string, string> ContenidoCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<NovedadSistemaDto>> GetAsync(CancellationToken ct = default)
    {
        var leidas = await centroAvisos.GetClavesLeidasAsync(ct);
        var lista = new List<NovedadSistemaDto>();
        foreach (var info in NovedadesSistemaCatalogo.Todas.OrderByDescending(n => n.Fecha))
        {
            lista.Add(new NovedadSistemaDto
            {
                Info = info,
                Contenido = await LeerContenidoAsync(info, ct),
                Leida = leidas.Contains(info.ClaveAviso)
            });
        }

        return lista;
    }

    public Task MarcarLeidaAsync(string id, CancellationToken ct = default)
        => NovedadesSistemaCatalogo.Buscar(id) is { } info
            ? centroAvisos.MarcarLeidosAsync([info.ClaveAviso], ct)
            : Task.CompletedTask;

    private async Task<string> LeerContenidoAsync(NovedadSistemaInfo info, CancellationToken ct)
    {
        if (ContenidoCache.TryGetValue(info.Archivo, out var cacheado))
            return cacheado;

        var ruta = Path.Combine(environment.ContentRootPath, "App_Data", "novedades-sistema", info.Archivo);
        try
        {
            var html = await File.ReadAllTextAsync(ruta, ct);
            ContenidoCache[info.Archivo] = html;
            return html;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await appEvents.LogErrorAsync("Novedades", "LeerNovedadSistema", ex,
                "No se pudo leer el contenido de una novedad de AlfaCore.", new { info.Id, info.Archivo },
                AppEventSeverity.Warning, ct);
            return "<p>No se pudo cargar el contenido de esta novedad.</p>";
        }
    }
}

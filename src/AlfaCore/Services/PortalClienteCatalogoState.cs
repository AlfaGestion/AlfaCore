using AlfaCore.Models;
using System.Collections.Concurrent;

namespace AlfaCore.Services;

/// <summary>Conserva los catálogos consultados durante el circuito del Portal Cliente. Las
/// páginas del portal son componentes distintos, por eso un estado local del componente se
/// perdía al navegar a Pedidos y volver.</summary>
public interface IPortalClienteCatalogoState
{
    CatalogosCatalogoDetalleDto? GetCatalogo(string? idWeb, int? idBase, int idInsert, string? codigoCliente);
    void SetCatalogo(string? idWeb, int? idBase, int idInsert, string? codigoCliente, CatalogosCatalogoDetalleDto catalogo);
    CatalogosCatalogoDetalleDto? GetCarrito(string? idWeb, int? idBase, int idInsert, string? codigoCliente);
    void SetCarrito(string? idWeb, int? idBase, int idInsert, string? codigoCliente, CatalogosCatalogoDetalleDto carrito);
}

public sealed class PortalClienteCatalogoState : IPortalClienteCatalogoState
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, CacheEntry> _catalogos = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CacheEntry> _carritos = new(StringComparer.OrdinalIgnoreCase);

    public CatalogosCatalogoDetalleDto? GetCatalogo(string? idWeb, int? idBase, int idInsert, string? codigoCliente)
        => GetValid(_catalogos, BuildKey(idWeb, idBase, idInsert, codigoCliente));

    public void SetCatalogo(string? idWeb, int? idBase, int idInsert, string? codigoCliente, CatalogosCatalogoDetalleDto catalogo)
        => _catalogos[BuildKey(idWeb, idBase, idInsert, codigoCliente)] = new CacheEntry(catalogo, DateTime.UtcNow);

    public CatalogosCatalogoDetalleDto? GetCarrito(string? idWeb, int? idBase, int idInsert, string? codigoCliente)
        => GetValid(_carritos, BuildKey(idWeb, idBase, idInsert, codigoCliente));

    public void SetCarrito(string? idWeb, int? idBase, int idInsert, string? codigoCliente, CatalogosCatalogoDetalleDto carrito)
        => _carritos[BuildKey(idWeb, idBase, idInsert, codigoCliente)] = new CacheEntry(carrito, DateTime.UtcNow);

    private static CatalogosCatalogoDetalleDto? GetValid(ConcurrentDictionary<string, CacheEntry> cache, string key)
    {
        if (!cache.TryGetValue(key, out var entry))
            return null;
        if (DateTime.UtcNow - entry.CreatedAt > CacheDuration)
        {
            cache.TryRemove(key, out _);
            return null;
        }
        return entry.Value;
    }

    private static string BuildKey(string? idWeb, int? idBase, int idInsert, string? codigoCliente)
        => $"{(idWeb ?? string.Empty).Trim().ToUpperInvariant()}|{idBase ?? 0}|{idInsert}|{(codigoCliente ?? string.Empty).Trim().ToUpperInvariant()}";

    private sealed record CacheEntry(CatalogosCatalogoDetalleDto Value, DateTime CreatedAt);
}

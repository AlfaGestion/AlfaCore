using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IFavoritesService
{
    Task<IReadOnlyList<ShellMenuNodeDto>> GetFavoritesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetFavoriteKeysAsync(CancellationToken ct = default);
    Task<bool> IsFavoriteAsync(string clave, CancellationToken ct = default);
    Task ToggleFavoriteAsync(string menu, string clave, CancellationToken ct = default);
}

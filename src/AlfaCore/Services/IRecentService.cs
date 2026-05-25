using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IRecentService
{
    Task<IReadOnlyList<ShellMenuNodeDto>> GetRecentsAsync(int maxRows = 8, CancellationToken ct = default);
    Task RegisterRecentAsync(string menu, string clave, CancellationToken ct = default);
}

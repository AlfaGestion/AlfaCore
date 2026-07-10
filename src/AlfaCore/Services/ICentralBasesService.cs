using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralBasesService
{
    Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default);
    Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default);
    Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default);
}

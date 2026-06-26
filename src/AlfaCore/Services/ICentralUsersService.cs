using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralUsersService
{
    Task<IReadOnlyList<UsuarioCentralGridDto>> GetAllAsync(CancellationToken ct = default);
}

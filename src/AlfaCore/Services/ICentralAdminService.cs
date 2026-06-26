using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralAdminService
{
    Task<IReadOnlyList<AdminClienteDto>> GetClientesAsync(CancellationToken ct = default);
    Task<AdminClienteDto?> GetClienteAsync(string idCliente, CancellationToken ct = default);
    Task<IReadOnlyList<ClienteAlfaLookupDto>> SearchVtClientesAsync(string term, int take = 25, CancellationToken ct = default);
    Task CreateClienteAsync(CrearClienteRequest request, CancellationToken ct = default);
    Task UpdateClienteAsync(CrearClienteRequest request, CancellationToken ct = default);
    Task<string?> TryResolveInitialPasswordAsync(string idCliente, CancellationToken ct = default);

    Task<IReadOnlyList<AdminBaseDto>> GetBasesAsync(CancellationToken ct = default);
    Task<AdminBaseDto?> GetBaseAsync(int idBase, CancellationToken ct = default);
    Task CreateBaseAsync(CrearBaseRequest request, CancellationToken ct = default);
    Task UpdateBaseAsync(CrearBaseRequest request, CancellationToken ct = default);
    Task DeleteBaseAsync(int idBase, CancellationToken ct = default);

    Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(CancellationToken ct = default);
    Task<AdminUserDto?> GetUserAsync(string userName, CancellationToken ct = default);
    Task CreateUserAsync(CrearUserRequest request, CancellationToken ct = default);
    Task UpdateUserAsync(CrearUserRequest request, CancellationToken ct = default);
    Task DeleteUserAsync(string userName, CancellationToken ct = default);
}

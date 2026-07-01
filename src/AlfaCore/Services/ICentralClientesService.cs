using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralClientesService
{
    Task<ClienteCentralDto?> GetByIdClienteAsync(string idCliente, CancellationToken ct = default);
    Task<ClienteCentralDto?> GetByIdWebAsync(string idWeb, CancellationToken ct = default);
    Task<IReadOnlyList<ClienteCentralDto>> GetAllAsync(CancellationToken ct = default);
    Task<string> GenerateAndSaveIdWebAsync(string idCliente, string razonSocial, CancellationToken ct = default);
}

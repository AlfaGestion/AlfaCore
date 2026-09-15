using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralCompraIaService
{
    Task<IReadOnlyList<BaseCentralDto>> GetBasesHabilitadasAsync(CancellationToken ct = default);
    Task<IReadOnlySet<int>> GetBasesSeleccionadasAsync(CancellationToken ct = default);
    Task SetBaseSeleccionadaAsync(int idBase, string idCliente, bool seleccionada, CancellationToken ct = default);
}

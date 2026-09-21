using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Mismo rol que ICentralCompraIaService pero para Conversaciones: qué bases están
/// habilitadas para que los jobs de fondo (bot-espera, mensajes programados) las procesen.</summary>
public interface IConversacionesHabilitacionService
{
    Task<IReadOnlyList<BaseCentralDto>> GetBasesHabilitadasAsync(CancellationToken ct = default);
    Task<IReadOnlySet<int>> GetBasesSeleccionadasAsync(CancellationToken ct = default);
    Task SetBaseSeleccionadaAsync(int idBase, string idCliente, bool seleccionada, CancellationToken ct = default);
}

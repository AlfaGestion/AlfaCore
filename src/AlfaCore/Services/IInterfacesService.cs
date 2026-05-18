using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IInterfacesService
{
    Task<IReadOnlyList<InterfacesEstadoOptionDto>> GetStatesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InterfacesTipoDocumentoOptionDto>> GetDocumentTypesAsync(CancellationToken ct = default);
    Task<PagedResult<InterfacesInboxItemDto>> SearchAsync(InterfacesFilters filters, CancellationToken ct = default);
    Task<InterfacesDetalleDto?> GetByIdAsync(long idComprobanteRecibido, CancellationToken ct = default);
    Task<long> CreateAsync(InterfacesCrearComprobanteRequest request, CancellationToken ct = default);
    Task UpdateAsync(InterfacesActualizarComprobanteRequest request, CancellationToken ct = default);
    Task AddAttachmentsAsync(InterfacesAgregarAdjuntosRequest request, CancellationToken ct = default);
    Task RemoveAttachmentAsync(InterfacesEliminarAdjuntoRequest request, CancellationToken ct = default);
    Task ChangeStatusAsync(InterfacesCambioEstadoRequest request, CancellationToken ct = default);
    Task<InterfacesCompraIaResultadoDto> QueueCompraDetectionAsync(InterfacesDetectarCompraRequest request, CancellationToken ct = default);
    Task<InterfacesCompraIaResultadoDto> DetectCompraAsync(InterfacesDetectarCompraRequest request, CancellationToken ct = default);
    Task<InterfacesCompraIaQueueSnapshotDto> GetCompraIaQueueSnapshotAsync(CancellationToken ct = default);
    Task CancelCompraDetectionAsync(InterfacesCompraIaAccionRequest request, CancellationToken ct = default);
    Task RetryCompraDetectionAsync(InterfacesCompraIaAccionRequest request, CancellationToken ct = default);
    Task<int> ProcessCompraIaQueueAsync(CancellationToken ct = default);
    Task<int> DeleteAsync(InterfacesEliminarComprobantesRequest request, CancellationToken ct = default);
    Task<InterfacesAdjuntoServeDto?> GetAttachmentForServeAsync(long idAdjunto, CancellationToken ct = default);
    Task<InterfacesViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, InterfacesViewSettingsDto settings, CancellationToken ct = default);
}

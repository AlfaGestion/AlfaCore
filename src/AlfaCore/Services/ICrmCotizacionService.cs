using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICrmCotizacionService
{
    Task<CrmCotizacionPricingContextDto> ResolvePricingContextAsync(string? clienteCodigo, CancellationToken ct = default);
    Task<IReadOnlyList<CrmCotizacionArticuloDto>> SearchArticulosAsync(string? clienteCodigo, string texto, int take = 25, CancellationToken ct = default);
    Task<IReadOnlyList<CrmCotizacionDto>> GetByOportunidadAsync(long idOportunidad, CancellationToken ct = default);
    Task<CrmCotizacionDetailDto?> GetByIdAsync(long idCotizacion, CancellationToken ct = default);
    Task<long> SaveAsync(CrmCotizacionSaveRequest request, CancellationToken ct = default);
    Task ChangeEstadoAsync(long idCotizacion, string estado, string? usuarioAccion = null, CancellationToken ct = default);
    Task DeleteAsync(long idCotizacion, string? usuarioAccion = null, CancellationToken ct = default);
    Task<string> GenerateServiceProposalAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default);
    Task<IReadOnlyList<CrmCotizacionAiLineaSugeridaDto>> SuggestLinesFromPromptAsync(string? clienteCodigo, string prompt, CancellationToken ct = default);
    Task<CrmCotizacionShareDto> EnsureShareAsync(long idCotizacion, CancellationToken ct = default);
    Task SendByEmailAsync(long idCotizacion, string destinatario, string? publicUrl = null, CancellationToken ct = default);
    Task<string?> RenderPublicHtmlAsync(int idBase, string token, CancellationToken ct = default);
}

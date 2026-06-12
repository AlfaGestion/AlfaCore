using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICargaViajesService
{
    Task<PagedResult<CargaViajesGridItemDto>> SearchViajesAsync(CargaViajesFilters filters, CancellationToken ct = default);
    Task<CargaViajesDetailDto?> GetViajeByIdAsync(int id, CancellationToken ct = default);
    Task<int> SaveViajeAsync(CargaViajeSaveRequest request, CancellationToken ct = default);
    Task AnularViajeAsync(int id, string? usuarioAccion = null, CancellationToken ct = default);

    Task<PagedResult<CargaViajeTarifaGridItemDto>> SearchTarifasAsync(CargaViajesFilters filters, CancellationToken ct = default);
    Task<CargaViajeTarifaGridItemDto?> GetTarifaByIdAsync(string idLista, CancellationToken ct = default);
    Task<string> SaveTarifaAsync(CargaViajeTarifaSaveRequest request, CancellationToken ct = default);
    Task BajaTarifaAsync(string idLista, CancellationToken ct = default);

    Task<PagedResult<CargaViajeChoferGridItemDto>> SearchChoferesAsync(CargaViajesFilters filters, CancellationToken ct = default);
    Task<CargaViajeChoferGridItemDto?> GetChoferByIdAsync(string codigo, CancellationToken ct = default);
    Task<string> SaveChoferAsync(CargaViajeChoferSaveRequest request, CancellationToken ct = default);
    Task BajaChoferAsync(string codigo, CancellationToken ct = default);

    Task<PagedResult<CargaViajeDestinoGridItemDto>> SearchDestinosAsync(CargaViajesFilters filters, CancellationToken ct = default);
    Task<CargaViajeDestinoGridItemDto?> GetDestinoByIdAsync(string codigo, CancellationToken ct = default);
    Task<string> SaveDestinoAsync(CargaViajeDestinoSaveRequest request, CancellationToken ct = default);
    Task BajaDestinoAsync(string codigo, CancellationToken ct = default);

    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchChoferLookupAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchDestinosLookupAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchTipoVehiculosLookupAsync(string texto, CancellationToken ct = default);
    Task<decimal> GetTarifaClienteAsync(string cliente, string destino, string tipoVehiculo, CancellationToken ct = default);
    Task<decimal> GetTarifaFleteroAsync(string chofer, string destino, string tipoVehiculo, CancellationToken ct = default);
    Task<CargaViajesLookupDto> GetLookupsAsync(CancellationToken ct = default);

    Task<CargaViajesViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, CargaViajesViewSettingsDto settings, CancellationToken ct = default);
    Task<CargaViajesConfigDto> GetConfiguracionAsync(CancellationToken ct = default);
    Task SaveConfiguracionAsync(CargaViajesConfigDto config, CancellationToken ct = default);

    Task<string> GetNextIdComprobanteAsync(CancellationToken ct = default);
    Task<string> GetSucursalConfiguradaAsync(CancellationToken ct = default);
}

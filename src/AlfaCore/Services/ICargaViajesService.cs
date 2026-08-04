using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICargaViajesService
{
    Task EnsureViajesSchemaAsync(CancellationToken ct = default);
    Task<PagedResult<CargaViajesGridItemDto>> SearchViajesAsync(CargaViajesFilters filters, CancellationToken ct = default);
    Task<CargaViajesDetailDto?> GetViajeByIdAsync(int id, CancellationToken ct = default);
    Task<CargaViajePreviewDto?> GetViajePreviewAsync(int id, CancellationToken ct = default);
    Task<int> SaveViajeAsync(CargaViajeSaveRequest request, CancellationToken ct = default);
    Task AnularViajeAsync(int id, string? usuarioAccion = null, CancellationToken ct = default);

    Task<PagedResult<CargaViajeTarifaGridItemDto>> SearchTarifasAsync(CargaViajesFilters filters, CancellationToken ct = default);
    Task<CargaViajeTarifaGridItemDto?> GetTarifaByIdAsync(string idLista, CancellationToken ct = default);
    Task<CargaViajeTarifaGridItemDto?> GetTarifaByIdAsync(int id, CancellationToken ct = default);
    Task ActualizarTarifaImporteAsync(int id, string idLista, decimal importe, CancellationToken ct = default);
    Task<string> SaveTarifaAsync(CargaViajeTarifaSaveRequest request, CancellationToken ct = default);
    Task ActualizarAdicionalesTarifasClienteAsync(string clienteCodigo, IReadOnlyCollection<string> listasSeleccionadas, CargaViajeTarifaSaveRequest source, CancellationToken ct = default);
    Task BajaTarifaAsync(string idLista, CancellationToken ct = default);
    Task BajaTarifaAsync(int id, string idLista, CancellationToken ct = default);

    Task<PagedResult<CargaViajeChoferGridItemDto>> SearchChoferesAsync(CargaViajesFilters filters, CancellationToken ct = default);
    Task<CargaViajeChoferGridItemDto?> GetChoferByIdAsync(string codigo, CancellationToken ct = default);
    Task<string> SaveChoferAsync(CargaViajeChoferSaveRequest request, CancellationToken ct = default);
    Task BajaChoferAsync(string codigo, CancellationToken ct = default);
    Task<string> GetNextCodigoChoferAsync(CancellationToken ct = default);

    Task<PagedResult<CargaViajeDestinoGridItemDto>> SearchDestinosAsync(CargaViajesFilters filters, CancellationToken ct = default);
    Task<CargaViajeDestinoGridItemDto?> GetDestinoByIdAsync(string codigo, CancellationToken ct = default);
    Task<string> SaveDestinoAsync(CargaViajeDestinoSaveRequest request, CancellationToken ct = default);
    Task BajaDestinoAsync(string codigo, CancellationToken ct = default);
    Task<string> GetNextCodigoDestinoAsync(CancellationToken ct = default);
    Task<CargaViajeLookupOptionDto> CreateDestinoRapidoAsync(string descripcion, CancellationToken ct = default);

    Task<PagedResult<CargaViajeTipoVehiculoGridItemDto>> SearchTipoVehiculosAsync(CargaViajesFilters filters, CancellationToken ct = default);
    Task<CargaViajeTipoVehiculoGridItemDto?> GetTipoVehiculoByIdAsync(string codigo, CancellationToken ct = default);
    Task<string> SaveTipoVehiculoAsync(CargaViajeTipoVehiculoSaveRequest request, CancellationToken ct = default);
    Task BajaTipoVehiculoAsync(string codigo, CancellationToken ct = default);
    Task<string> GetNextCodigoTipoVehiculoAsync(CancellationToken ct = default);
    Task<bool> TipoVehiculoTieneActivoAsync(CancellationToken ct = default);
    Task<CargaViajeLookupOptionDto> CreateTipoVehiculoRapidoAsync(string descripcion, CancellationToken ct = default);

    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchChoferLookupAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchChoferLookupAsync(string texto, bool incluirChoferes, bool incluirFleteros, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchFleterosLookupAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchDestinosLookupAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchTipoVehiculosLookupAsync(string texto, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchTarifasLookupAsync(string texto, CancellationToken ct = default);
    Task<CargaViajeLookupOptionDto> CreateChoferRapidoAsync(string nombre, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeTarifaClienteResumenDto>> GetTarifasPorClienteAsync(string clienteCodigo, string? texto = null, CancellationToken ct = default);
    Task<CargaViajeTarifaGridItemDto?> GetTarifaClienteAsync(string cliente, string destino, string tipoVehiculo, CancellationToken ct = default);
    Task<decimal> GetTarifaFleteroAsync(string chofer, string destino, string tipoVehiculo, CancellationToken ct = default);
    Task<CargaViajeTarifaGridItemDto?> GetTarifaFleteroDetalleAsync(string chofer, string destino, string tipoVehiculo, CancellationToken ct = default);
    Task<CargaViajeTarifaGridItemDto?> ClonarTarifaParaViajeAsync(string idListaBase, CargaViajeSaveRequest viaje, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeReporteLiquidacionRowDto>> SearchLiquidacionChoferesAsync(CargaViajesReporteLiquidacionFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeReporteClienteRowDto>> SearchReporteClientesAsync(CargaViajesReporteLiquidacionFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<CargaViajeLiquidacionRowDto>> SearchLiquidacionesFletesAsync(CargaViajesLiquidacionFilters filters, CancellationToken ct = default);
    Task<int> MarcarFletesPagadosAsync(CargaViajesMarcarPagadoRequest request, CancellationToken ct = default);
    Task<CargaViajesLookupDto> GetLookupsAsync(CancellationToken ct = default);

    Task<CargaViajesViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(string userName, CargaViajesViewSettingsDto settings, CancellationToken ct = default);
    Task<CargaViajeTipoVehiculoViewSettingsDto> GetTipoVehiculoViewSettingsAsync(string userName, CancellationToken ct = default);
    Task SaveTipoVehiculoViewSettingsAsync(string userName, CargaViajeTipoVehiculoViewSettingsDto settings, CancellationToken ct = default);
    Task<CargaViajesConfigDto> GetConfiguracionAsync(CancellationToken ct = default);
    Task SaveConfiguracionAsync(CargaViajesConfigDto config, CancellationToken ct = default);

    Task<string> GetNextIdComprobanteAsync(CancellationToken ct = default);
    Task<string> GetSucursalConfiguradaAsync(CancellationToken ct = default);
}

using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICuentasComercialesService
{
    Task<PagedResult<CuentaComercialGridItemDto>> SearchAsync(CuentaComercialTipo tipo, CuentaComercialFilters filters, CancellationToken ct = default);
    Task<CuentaComercialDetailDto?> GetByIdAsync(CuentaComercialTipo tipo, string codigo, CancellationToken ct = default);
    Task<string> GetSuggestedCodigoAsync(CuentaComercialTipo tipo, CancellationToken ct = default);
    Task<CuentaComercialLookupDataDto> GetLookupDataAsync(CuentaComercialTipo tipo, CancellationToken ct = default);
    Task<string> SaveAsync(CuentaComercialTipo tipo, CuentaComercialSaveRequest request, CancellationToken ct = default);
    Task DeactivateAsync(CuentaComercialTipo tipo, string codigo, CancellationToken ct = default);

    // Clave del Portal Cliente (MA_CUENTASADIC.CLAVE) — acción específica y separada del guardado
    // general de la ficha: nunca se lee ni se muestra la clave existente, y esta operación es la
    // única que la modifica. Solo aplica a Clientes (Proveedores no tienen Portal Cliente).
    Task SetClavePortalClienteAsync(string codigoCliente, string nuevaClave, string usuarioEjecuta, CancellationToken ct = default);
    Task<IReadOnlyList<CuentaComercialContactoDto>> GetContactosAsync(CuentaComercialTipo tipo, string cuentaCodigo, CancellationToken ct = default);
    Task<IReadOnlyList<CuentaComercialContactoCandidateDto>> SearchContactosParaVincularAsync(CuentaComercialTipo tipo, string cuentaCodigo, string texto, CancellationToken ct = default);
    Task<int> CreateContactoAsync(CuentaComercialTipo tipo, CuentaComercialContactoCreateRequest request, CancellationToken ct = default);
    Task LinkContactoAsync(CuentaComercialTipo tipo, CuentaComercialContactoLinkRequest request, CancellationToken ct = default);
    Task UpdateContactoQuickAsync(CuentaComercialTipo tipo, CuentaComercialContactoQuickUpdateRequest request, CancellationToken ct = default);
    Task<CuentaComercialViewSettingsDto> GetViewSettingsAsync(CuentaComercialTipo tipo, string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(CuentaComercialTipo tipo, string userName, CuentaComercialViewSettingsDto settings, CancellationToken ct = default);
}

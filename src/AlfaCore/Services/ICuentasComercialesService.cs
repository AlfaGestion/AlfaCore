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
    Task UnlinkContactoAsync(CuentaComercialTipo tipo, CuentaComercialContactoUnlinkRequest request, CancellationToken ct = default);
    Task UpdateContactoQuickAsync(CuentaComercialTipo tipo, CuentaComercialContactoQuickUpdateRequest request, CancellationToken ct = default);
    Task<CuentaComercialViewSettingsDto> GetViewSettingsAsync(CuentaComercialTipo tipo, string userName, CancellationToken ct = default);
    Task SaveViewSettingsAsync(CuentaComercialTipo tipo, string userName, CuentaComercialViewSettingsDto settings, CancellationToken ct = default);

    // Vistas fijas de facturación automática (solo clientes), ver CuentaComercialVistaFijaKeys.
    Task<IReadOnlyList<FacturacionAutomaticaVistaFilaDto>> GetFacturacionAutomaticaVistaAsync(string vista, string texto, CancellationToken ct = default);

    /// <summary>Detalles de facturación automática que tienen los clientes indicados.</summary>
    Task<IReadOnlyList<FacturacionAutomaticaConceptoUsoDto>> GetConceptosFacturacionAutomaticaAsync(IReadOnlyList<string> codigos, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>Agrega un concepto a la facturación automática de los clientes indicados (como el lote del VB6).</summary>
    Task<FacturacionAutomaticaLoteResultadoDto> AgregarConceptoFacturacionAutomaticaAsync(IReadOnlyList<string> codigos, string detalle,
        decimal importe, bool esPorcentaje, int cuota, int cuotas, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>Quita de los clientes indicados los renglones con ese detalle (opcionalmente solo los de cuotas cumplidas).</summary>
    Task<FacturacionAutomaticaLoteResultadoDto> QuitarConceptoFacturacionAutomaticaAsync(IReadOnlyList<string> codigos, string detalle,
        bool soloCumplidos, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>Aumenta, reduce o redondea los importes de facturación automática. Con aplicar=false solo calcula cómo quedaría.</summary>
    Task<FacturacionAutomaticaAumentoResultadoDto> AumentarFacturacionAutomaticaAsync(FacturacionAutomaticaAumentoPedido pedido, bool aplicar,
        CancellationToken ct = default)
        => throw new NotSupportedException();
}

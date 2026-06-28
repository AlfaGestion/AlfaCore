using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaService
{
    Task<PuntoVentaContextDto> GetContextAsync(CancellationToken ct = default);
    Task<PuntoVentaSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(PuntoVentaSettingsDto settings, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaPaymentMethodDto>> GetPaymentMethodsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaFamilyDto>> GetFamiliasAsync(CancellationToken ct = default);
    Task<PuntoVentaCatalogDto> SearchArticulosAsync(PuntoVentaCatalogFiltersDto filters, CancellationToken ct = default);
    Task<PuntoVentaSaleResultDto> CreateSaleAsync(PuntoVentaSaleRequestDto request, CancellationToken ct = default);
    Task<PuntoVentaReceiptContextDto> GetReceiptContextAsync(string cuentaCliente, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaReceiptListItemDto>> GetRecentReceiptsAsync(string tipoComprobante, CancellationToken ct = default);
    Task<PuntoVentaReceiptDataDto> GetReceiptDataAsync(int idComprobante, CancellationToken ct = default);
    Task MarkReceiptPrintedAsync(int idComprobante, CancellationToken ct = default);
    Task SendReceiptByEmailAsync(PuntoVentaReceiptEmailRequestDto request, CancellationToken ct = default);
    Task<PuntoVentaArticleImageDto?> GetArticleImageForServeAsync(string idArticulo, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaCuentaImputacionDto>> GetCuentasImputacionAsync(CancellationToken ct = default);
    Task<PuntoVentaMovimientoCajaResultDto> CrearMovimientoCajaAsync(PuntoVentaMovimientoCajaRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaMovimientoCajaDetalleDto>> GetDetalleCajaHoyAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaConsolidadoCajaDto>> GetConsolidadoCajaHoyAsync(CancellationToken ct = default);
}

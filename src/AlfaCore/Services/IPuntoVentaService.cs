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
    Task<PuntoVentaArticleDto?> GetArticuloPorCodigoAsync(string codigo, CancellationToken ct = default);
    Task<PuntoVentaSaleResultDto> CreateSaleAsync(PuntoVentaSaleRequestDto request, CancellationToken ct = default);
    /// <summary>Reintenta pedir el CAE de un comprobante ya creado (quedó "Rechazado" o "Pendiente").
    /// No vuelve a tocar V_MV_Cpte/cobranza -- solo pide de nuevo el CAE y persiste el nuevo intento.</summary>
    Task<ArcaCaeIntentoDto> RetryCaeAsync(int idComprobante, CancellationToken ct = default);
    Task<PuntoVentaReceiptContextDto> GetReceiptContextAsync(string cuentaCliente, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaReceiptListItemDto>> GetRecentReceiptsAsync(string tipoComprobante, string? sucursal = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, CancellationToken ct = default);
    Task<PuntoVentaReceiptDataDto> GetReceiptDataAsync(int idComprobante, CancellationToken ct = default);
    Task MarkReceiptPrintedAsync(int idComprobante, CancellationToken ct = default);
    Task SendReceiptByEmailAsync(PuntoVentaReceiptEmailRequestDto request, CancellationToken ct = default);
    Task<PuntoVentaArticleImageDto?> GetArticleImageForServeAsync(string idArticulo, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaCuentaImputacionDto>> GetCuentasImputacionAsync(CancellationToken ct = default);
    Task<PuntoVentaMovimientoCajaResultDto> CrearMovimientoCajaAsync(PuntoVentaMovimientoCajaRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaMovimientoCajaDetalleDto>> GetDetalleCajaHoyAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PuntoVentaConsolidadoCajaDto>> GetConsolidadoCajaHoyAsync(CancellationToken ct = default);
}

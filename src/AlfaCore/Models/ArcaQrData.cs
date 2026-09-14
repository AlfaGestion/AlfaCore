namespace AlfaCore.Models;

/// <summary>Datos de emisión autorizados; no forman parte de la plantilla editable.</summary>
public sealed record ArcaQrData(
    DateTime Fecha, string Cuit, int PuntoVenta, int TipoComprobante, long Numero,
    decimal Importe, string Moneda, decimal Cotizacion, int? TipoDocumentoReceptor,
    string? NumeroDocumentoReceptor, string TipoAutorizacion, string Autorizacion);

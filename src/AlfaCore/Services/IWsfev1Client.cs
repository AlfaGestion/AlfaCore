using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Cliente WSFEv1 (Web Service de Facturación Electrónica, versión 1) de AFIP/ARCA -- solo
/// las dos operaciones que necesita el flujo de Factura A/B/C del POS: consultar el último comprobante
/// autorizado (para numerar antes de crear el comprobante local) y solicitar el CAE. Un solo intento
/// por llamada, sin reintento automático -- esa decisión es del orquestador.</summary>
public interface IWsfev1Client
{
    Task<long> ObtenerUltimoAutorizadoAsync(WsaaTicket ticket, string cuit, int ptoVta, int cbteTipo, ArcaAmbiente ambiente, string? wsfeUrl, CancellationToken ct);

    Task<ArcaCaeResultadoDto> SolicitarCaeAsync(WsaaTicket ticket, ArcaCaeSolicitudDto solicitud, ArcaAmbiente ambiente, string? wsfeUrl, CancellationToken ct);
}

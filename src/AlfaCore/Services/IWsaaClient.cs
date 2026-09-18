using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Autenticación WSAA (Web Service de Autenticación y Autorización) de AFIP/ARCA: firma un
/// Ticket de Requerimiento de Acceso (TRA) con el certificado del emisor y lo cambia por un Ticket de
/// Acceso (token+sign) válido por ~12hs. Cachea el ticket en dbo.ARCA_WSAA_TICKET (compartible entre
/// workers de IIS/Kestrel) respetando su expiración real, no una ventana fija.</summary>
public interface IWsaaClient
{
    Task<WsaaTicket> ObtenerTicketAsync(SqlConnection cn, ArcaEmisorConfig emisor, CancellationToken ct);
}

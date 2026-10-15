using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Comprueba homologación, autentica WSAA y consulta numeración; nunca solicita CAE.</summary>
public sealed class CargaComprobantesElectronicaVerificador(
    IArcaConfigService config, IWsaaClient wsaa, IWsfev1Client wsfe)
{
    public async Task<CargaComprobantesElectronicaEstado> VerificarAsync(SqlConnection cn,
        string unidadNegocio, int puntoVentaEsperado, CancellationToken ct)
    {
        var emisor = await config.ResolveEmisorAsync(cn, unidadNegocio, ct)
            ?? throw new CargaComprobantesReglaException("No hay un emisor electrónico configurado para esta unidad de negocio.");
        ValidarHomologacion(emisor, puntoVentaEsperado);
        var ticket = await wsaa.ObtenerTicketAsync(cn, emisor, ct);
        return new CargaComprobantesElectronicaEstado
        {
            Ambiente = "HOMOLOGACION", PuntoVenta = emisor.PuntoVentaElectronico,
            UltimaFacturaA = await wsfe.ObtenerUltimoAutorizadoAsync(ticket, emisor.Cuit, emisor.PuntoVentaElectronico, 1, emisor.Ambiente, emisor.WsfeUrl, ct),
            UltimaFacturaB = await wsfe.ObtenerUltimoAutorizadoAsync(ticket, emisor.Cuit, emisor.PuntoVentaElectronico, 6, emisor.Ambiente, emisor.WsfeUrl, ct),
            UltimaFacturaC = await wsfe.ObtenerUltimoAutorizadoAsync(ticket, emisor.Cuit, emisor.PuntoVentaElectronico, 11, emisor.Ambiente, emisor.WsfeUrl, ct)
        };
    }

    public static void ValidarHomologacion(ArcaEmisorConfig emisor, int puntoVentaEsperado)
    {
        if (emisor.Ambiente != ArcaAmbiente.Homologacion)
            throw new CargaComprobantesReglaException("Esta prueba admite exclusivamente homologación. La configuración actual corresponde a producción.");
        if (puntoVentaEsperado <= 0 || emisor.PuntoVentaElectronico != puntoVentaEsperado)
            throw new CargaComprobantesReglaException($"La unidad utiliza el punto de venta {emisor.PuntoVentaElectronico}; no coincide con el punto de prueba {puntoVentaEsperado}. Revisá el emisor del origen.");
        if (!EndpointHomologacion(emisor.WsaaUrl, "wsaa.homo.arca.gov.ar", "wsaahomo.afip.gov.ar") ||
            !EndpointHomologacion(emisor.WsfeUrl, "wswhomo.afip.gov.ar"))
            throw new CargaComprobantesReglaException("Las direcciones WSAA/WSFE no corresponden a los servicios de homologación admitidos.");
    }

    private static bool EndpointHomologacion(string endpoint, params string[] hosts)
        => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.UserInfo.Length == 0 && hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}

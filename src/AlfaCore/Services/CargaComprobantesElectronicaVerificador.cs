using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Comprueba el ambiente, autentica WSAA y consulta numeración; nunca solicita CAE.</summary>
public sealed class CargaComprobantesElectronicaVerificador(
    IArcaConfigService config, IWsaaClient wsaa, IWsfev1Client wsfe)
{
    public async Task<CargaComprobantesElectronicaEstado> VerificarAsync(SqlConnection cn,
        string unidadNegocio, int puntoVentaEsperado, CancellationToken ct)
    {
        var emisor = await config.ResolveEmisorAsync(cn, unidadNegocio, ct)
            ?? throw new CargaComprobantesReglaException("No hay un emisor electrónico configurado para esta unidad de negocio.");
        // Solo consulta: no exige que la emisión real esté habilitada.
        ValidarAmbiente(emisor, puntoVentaEsperado, exigirEmisionHabilitada: false);
        var ticket = await wsaa.ObtenerTicketAsync(cn, emisor, ct);
        return new CargaComprobantesElectronicaEstado
        {
            Ambiente = emisor.Ambiente == ArcaAmbiente.Produccion ? ArcaModoEmision.Produccion : ArcaModoEmision.Homologacion,
            PuntoVenta = emisor.PuntoVentaElectronico,
            UltimaFacturaA = await wsfe.ObtenerUltimoAutorizadoAsync(ticket, emisor.Cuit, emisor.PuntoVentaElectronico, 1, emisor.Ambiente, emisor.WsfeUrl, ct),
            UltimaFacturaB = await wsfe.ObtenerUltimoAutorizadoAsync(ticket, emisor.Cuit, emisor.PuntoVentaElectronico, 6, emisor.Ambiente, emisor.WsfeUrl, ct),
            UltimaFacturaC = await wsfe.ObtenerUltimoAutorizadoAsync(ticket, emisor.Cuit, emisor.PuntoVentaElectronico, 11, emisor.Ambiente, emisor.WsfeUrl, ct)
        };
    }

    /// <summary>
    /// Base de prueba: solo homologación (servicios oficiales de homologación).
    /// Base real: solo producción y, para grabar o pedir CAE, con la emisión real habilitada.
    /// Una base real nunca graba comprobantes de homologación.
    /// </summary>
    public static void ValidarAmbiente(ArcaEmisorConfig emisor, int puntoVentaEsperado, bool exigirEmisionHabilitada = true)
    {
        if (emisor.EsBasePrueba)
        {
            if (emisor.Ambiente != ArcaAmbiente.Homologacion)
                throw new CargaComprobantesReglaException("Esta es una base de prueba: solo puede facturar en homologación.");
            if (!Endpoint(emisor.WsaaUrl, "wsaa.homo.arca.gov.ar", "wsaahomo.afip.gov.ar") || !Endpoint(emisor.WsfeUrl, "wswhomo.afip.gov.ar"))
                throw new CargaComprobantesReglaException("Las direcciones WSAA/WSFE no corresponden a los servicios de homologación admitidos.");
        }
        else
        {
            if (exigirEmisionHabilitada && !emisor.EmisionRealAlfaCore)
                throw new CargaComprobantesReglaException("Esta base no está habilitada para emitir comprobantes reales desde AlfaCore. " +
                    "Se activa en Configuración General → Ventas → Facturación electrónica («Emitir comprobantes reales desde AlfaCore»). No se generó nada.");
            if (emisor.Ambiente != ArcaAmbiente.Produccion)
                throw new CargaComprobantesReglaException("Esta es una base real y ARCA está configurado en homologación: AlfaCore no graba comprobantes de prueba en una base real. " +
                    "Poné el ambiente en Producción en Configuración General → Ventas → Facturación electrónica.");
            if (!Endpoint(emisor.WsaaUrl, "wsaa.afip.gov.ar", "wsaa.arca.gov.ar") || !Endpoint(emisor.WsfeUrl, "servicios1.afip.gov.ar", "servicios1.arca.gov.ar"))
                throw new CargaComprobantesReglaException("Las direcciones WSAA/WSFE no corresponden a los servicios de producción de ARCA.");
        }
        if (puntoVentaEsperado <= 0 || emisor.PuntoVentaElectronico != puntoVentaEsperado)
            throw new CargaComprobantesReglaException($"La unidad utiliza el punto de venta {emisor.PuntoVentaElectronico}; no coincide con el punto {puntoVentaEsperado} del comprobante. Revisá el emisor del origen.");
    }

    /// <summary>La «prueba ARCA» pide un CAE sin grabar el comprobante: solo se admite en homologación.</summary>
    public static void ValidarSoloHomologacion(ArcaEmisorConfig emisor, int puntoVentaEsperado)
    {
        if (emisor.Ambiente != ArcaAmbiente.Homologacion)
            throw new CargaComprobantesReglaException("La prueba de autorización solo se hace en homologación: en producción pediría un CAE real sin grabar la factura.");
        ValidarAmbiente(emisor with { EsBasePrueba = true }, puntoVentaEsperado);
    }

    private static bool Endpoint(string endpoint, params string[] hosts)
        => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.UserInfo.Length == 0 && hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}

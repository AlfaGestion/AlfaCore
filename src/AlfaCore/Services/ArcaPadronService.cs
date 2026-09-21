using System.Text;
using System.Xml.Linq;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class ArcaPadronService(
    IArcaConfigService arcaConfig,
    IWsaaClient wsaaClient,
    IHttpClientFactory httpClientFactory,
    ISessionService sessionService,
    IConfiguration configuration,
    IAppEventService appEvents) : IArcaPadronService
{
    private const string ModuleName = "ArcaPadron";
    private const string Servicio = "ws_sr_constancia_inscripcion";
    private const string UrlHomologacion = "https://awshomo.arca.gov.ar/sr-padron/webservices/personaServiceA5";
    private const string UrlProduccion = "https://aws.arca.gov.ar/sr-padron/webservices/personaServiceA5";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<ArcaPadronPersonaDto> ConsultarPersonaAsync(string cuit, CancellationToken ct = default)
    {
        var digits = new string((cuit ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != 11)
            throw new InvalidOperationException("Ingresá un CUIT válido de 11 dígitos para consultar ARCA.");

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            var emisor = await arcaConfig.ResolvePadronEmisorAsync(cn, ct)
                ?? throw new InvalidOperationException("No hay una configuración propia de padrón ARCA con certificado disponible.");
            var ticket = await wsaaClient.ObtenerTicketAsync(cn, emisor, ct, Servicio);
            var response = await ConsultarAsync(ticket, emisor, digits, ct);
            return ParsePersona(response, digits);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (ArcaAutenticacionException ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "ConsultarPersona", ex,
                "No se pudo consultar el contribuyente en ARCA.", new { Cuit = digits }, ct: ct);

            var noAutorizado = ex.Message.Contains("coe.notAuthorized", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("Computador no autorizado", StringComparison.OrdinalIgnoreCase);
            var certificadoVencido = ex.Message.Contains("cms.cert.expired", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("Certificado expirado", StringComparison.OrdinalIgnoreCase);
            var mensaje = noAutorizado
                ? "ARCA no autorizó el certificado para consultar el padrón. Habilitá este computador/certificado para el servicio ws_sr_constancia_inscripcion en homologación."
                : certificadoVencido
                    ? "El certificado configurado para consultar el padrón ARCA está vencido. Reemplazalo por un certificado vigente de padrón."
                : "No se pudo autenticar contra ARCA para consultar el padrón.";
            throw new InvalidOperationException(mensaje, ex);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "ConsultarPersona", ex,
                "No se pudo consultar el contribuyente en ARCA.", new { Cuit = digits }, ct: ct);
            throw new InvalidOperationException("No se pudo consultar el CUIT en ARCA.", ex);
        }
    }

    private async Task<string> ConsultarAsync(WsaaTicket ticket, ArcaEmisorConfig emisor, string cuit, CancellationToken ct)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace a5 = "http://a5.soap.ws.server.puc.sr/";
        var body = new XElement(a5 + "getPersona_v2",
            new XElement("token", ticket.Token),
            new XElement("sign", ticket.Sign),
            new XElement("cuitRepresentada", emisor.Cuit),
            new XElement("idPersona", cuit));
        var request = new XDocument(
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", soap),
                new XAttribute(XNamespace.Xmlns + "a5", a5),
                new XElement(soap + "Header"),
                new XElement(soap + "Body", body))).ToString(SaveOptions.DisableFormatting);

        var url = emisor.Ambiente == ArcaAmbiente.Produccion ? UrlProduccion : UrlHomologacion;
        var client = httpClientFactory.CreateClient("Arca");
        using var content = new StringContent(request, Encoding.UTF8, "text/xml");
        using var response = await client.PostAsync(url, content, ct);
        var xml = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ARCA respondió {(int)response.StatusCode} al consultar el padrón.");
        return xml;
    }

    private static ArcaPadronPersonaDto ParsePersona(string xml, string cuit)
    {
        var doc = XDocument.Parse(xml);
        var error = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"ARCA no pudo informar el CUIT: {error}");

        var general = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("datosGenerales", StringComparison.OrdinalIgnoreCase));
        if (general is null)
            throw new InvalidOperationException("ARCA no devolvió datos para el CUIT consultado.");

        string Value(string name) => general.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? string.Empty;
        var domicilio = general.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("domicilioFiscal", StringComparison.OrdinalIgnoreCase));
        string Address(string name) => domicilio?.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? string.Empty;
        var razonSocial = Value("razonSocial");
        var nombre = Value("nombre");
        var apellido = Value("apellido");

        return new ArcaPadronPersonaDto
        {
            Cuit = cuit,
            RazonSocial = string.IsNullOrWhiteSpace(razonSocial) ? $"{apellido} {nombre}".Trim() : razonSocial,
            Nombre = nombre,
            Apellido = apellido,
            CondicionIva = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Contains("condicion", StringComparison.OrdinalIgnoreCase) && x.Name.LocalName.Contains("iva", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? string.Empty,
            Calle = Address("direccion"),
            Localidad = Address("localidad"),
            Provincia = Address("descripcionProvincia"),
            CodigoPostal = Address("codPostal")
        };
    }
}

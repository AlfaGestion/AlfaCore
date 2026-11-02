using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Llamadas SOAP al padrón de ARCA, compartidas por la consulta de cada base y el servicio central.
/// CUIT: ws_sr_constancia_inscripcion (personaServiceA5.getPersona_v2).
/// DNI: ws_sr_padron_a13 (personaServiceA13.getIdPersonaListByDocumento) devuelve los CUIT del documento.
/// </summary>
public static class ArcaPadronSoap
{
    public const string ServicioCuit = "ws_sr_constancia_inscripcion";
    public const string ServicioDni = "ws_sr_padron_a13";
    // El endpoint documentado bajo aws.arca.gov.ar no resuelve desde este servidor; el alias de AFIP sí.
    private const string UrlCuitProduccion = "https://aws.afip.gov.ar/sr-padron/webservices/personaServiceA5";
    private const string UrlCuitHomologacion = "https://awshomo.arca.gov.ar/sr-padron/webservices/personaServiceA5";
    private const string UrlDniProduccion = "https://aws.afip.gov.ar/sr-padron/webservices/personaServiceA13";
    private const string UrlDniHomologacion = "https://awshomo.arca.gov.ar/sr-padron/webservices/personaServiceA13";

    public static string SoloDigitos(string? valor) => new((valor ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>
    /// CUIT en nombre del cual se consulta: el del certificado (SERIALNUMBER=CUIT ...). Así un certificado
    /// sirve aunque la base tenga otro CUIT de facturación; si el certificado no lo trae, el de la configuración.
    /// </summary>
    public static string CuitRepresentada(ArcaEmisorConfig emisor)
    {
        try
        {
            using var cert = X509Certificate2.CreateFromPem(emisor.CertificadoPem);
            var marca = cert.Subject.Split(',').Select(x => x.Trim())
                .FirstOrDefault(x => x.StartsWith("SERIALNUMBER=", StringComparison.OrdinalIgnoreCase) || x.StartsWith("OID.2.5.4.5=", StringComparison.OrdinalIgnoreCase));
            var digitos = SoloDigitos(marca);
            if (digitos.Length == 11) return digitos;
        }
        catch (Exception) { /* certificado ilegible: se usa la configuración */ }
        return emisor.Cuit;
    }

    public static DateTime? Vencimiento(ArcaEmisorConfig emisor)
    {
        try { using var cert = X509Certificate2.CreateFromPem(emisor.CertificadoPem); return cert.NotAfter; }
        catch (Exception) { return null; }
    }

    public static async Task<ArcaPadronPersonaDto> ConsultarCuitAsync(HttpClient http, WsaaTicket ticket, ArcaEmisorConfig emisor, string cuit, CancellationToken ct)
    {
        XNamespace a5 = "http://a5.soap.ws.server.puc.sr/";
        var xml = await PostAsync(http, emisor.Ambiente == ArcaAmbiente.Produccion ? UrlCuitProduccion : UrlCuitHomologacion,
            new XElement(a5 + "getPersona_v2",
                new XElement("token", ticket.Token), new XElement("sign", ticket.Sign),
                new XElement("cuitRepresentada", CuitRepresentada(emisor)), new XElement("idPersona", cuit)), a5, "a5", ct);
        return ParsePersona(xml, cuit);
    }

    public static async Task<IReadOnlyList<string>> CuitsPorDniAsync(HttpClient http, WsaaTicket ticket, ArcaEmisorConfig emisor, string dni, CancellationToken ct)
    {
        XNamespace a13 = "http://a13.soap.ws.server.puc.sr/";
        var xml = await PostAsync(http, emisor.Ambiente == ArcaAmbiente.Produccion ? UrlDniProduccion : UrlDniHomologacion,
            new XElement(a13 + "getIdPersonaListByDocumento",
                new XElement("token", ticket.Token), new XElement("sign", ticket.Sign),
                new XElement("cuitRepresentada", CuitRepresentada(emisor)), new XElement("documento", dni)), a13, "a13", ct);
        var doc = XDocument.Parse(xml);
        var fault = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "faultstring")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(fault))
        {
            // "No existe persona con ese Id" / documento inexistente: sin resultados, no es una falla.
            if (fault.Contains("no existe", StringComparison.OrdinalIgnoreCase) || fault.Contains("inexistente", StringComparison.OrdinalIgnoreCase))
                return [];
            throw new InvalidOperationException($"ARCA no pudo buscar el documento: {fault}");
        }
        return doc.Descendants().Where(x => x.Name.LocalName == "idPersona")
            .Select(x => SoloDigitos(x.Value)).Where(x => x.Length == 11).Distinct().ToList();
    }

    private static async Task<string> PostAsync(HttpClient http, string url, XElement body, XNamespace ns, string prefijo, CancellationToken ct)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        var request = new XDocument(new XElement(soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", soap), new XAttribute(XNamespace.Xmlns + prefijo, ns),
            new XElement(soap + "Header"), new XElement(soap + "Body", body))).ToString(SaveOptions.DisableFormatting);
        using var content = new StringContent(request, Encoding.UTF8, "text/xml");
        using var response = await http.PostAsync(url, content, ct);
        var xml = await response.Content.ReadAsStringAsync(ct);
        // ARCA informa "persona inexistente" como SOAP Fault con HTTP 500: se devuelve para interpretarlo.
        if (!response.IsSuccessStatusCode && !xml.Contains("faultstring", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ARCA respondió {(int)response.StatusCode} al consultar el padrón.");
        return xml;
    }

    public static ArcaPadronPersonaDto ParsePersona(string xml, string cuit)
    {
        var doc = XDocument.Parse(xml);
        var fault = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "faultstring")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(fault))
            throw new InvalidOperationException($"ARCA no pudo informar el CUIT: {fault}");
        var error = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"ARCA no pudo informar el CUIT: {error}");

        var general = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("datosGenerales", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("ARCA no devolvió datos para el CUIT consultado.");

        string Value(string name) => general.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? string.Empty;
        var domicilio = general.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("domicilioFiscal", StringComparison.OrdinalIgnoreCase));
        string Address(string name) => domicilio?.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? string.Empty;
        var razonSocial = Value("razonSocial");
        var nombre = Value("nombre");
        var apellido = Value("apellido");
        var impIva = FindValue(doc, "imp_iva", "impIva", "condicionIva", "situacionIva");
        var monotributo = FindValue(doc, "monotributo");
        var tieneIvaEnRegimenGeneral = doc.Descendants()
            .Where(x => x.Name.LocalName.Equals("impuesto", StringComparison.OrdinalIgnoreCase))
            .Any(x => x.Descendants().Any(y => y.Name.LocalName.Equals("idImpuesto", StringComparison.OrdinalIgnoreCase) && y.Value.Trim() == "30")
                || x.Descendants().Any(y => y.Name.LocalName.Equals("descripcionImpuesto", StringComparison.OrdinalIgnoreCase)
                    && y.Value.Contains("IVA", StringComparison.OrdinalIgnoreCase)));
        // La constancia informa el régimen simplificado en datosMonotributo (no en un campo "monotributo").
        var esMonotributo = doc.Descendants().Any(x => x.Name.LocalName.Equals("datosMonotributo", StringComparison.OrdinalIgnoreCase) && x.HasElements);
        var condicion = ResolverCondicionIva(impIva, esMonotributo ? "S" : monotributo, tieneIvaEnRegimenGeneral);

        return new ArcaPadronPersonaDto
        {
            Cuit = cuit,
            RazonSocial = string.IsNullOrWhiteSpace(razonSocial) ? $"{apellido} {nombre}".Trim() : razonSocial,
            Nombre = nombre,
            Apellido = apellido,
            CondicionIva = condicion.Descripcion,
            CondicionIvaCodigo = condicion.Codigo,
            DocumentoTipoCodigo = "1", // TA_TIPODOCUMENTO: C.U.I.T.
            Calle = Address("direccion"),
            Localidad = Address("localidad"),
            Provincia = Address("descripcionProvincia"),
            CodigoPostal = Address("codPostal")
        };
    }

    private static string FindValue(XDocument doc, params string[] names)
    {
        var normalized = names.Select(NormalizeName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return doc.Descendants().Where(x => normalized.Contains(NormalizeName(x.Name.LocalName)))
            .Select(x => x.Value.Trim()).FirstOrDefault(x => x.Length > 0) ?? string.Empty;
    }

    private static string NormalizeName(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());

    private static (string Codigo, string Descripcion) ResolverCondicionIva(string impIva, string monotributo, bool tieneIvaEnRegimenGeneral)
    {
        var iva = NormalizeName(impIva).ToUpperInvariant();
        var mono = NormalizeName(monotributo).ToUpperInvariant();
        if (mono.Length > 0 && mono is not "NI" and not "N")
            return ("5", "RESPONSABLE MONOTRIBUTO");
        return iva switch
        {
            "AC" or "S" => ("1", "RESPONSABLE INSCRIPTO"),
            "NI" or "N" => ("2", "RESPONSABLE NO INSCRIPTO"),
            "EX" => ("4", "IVA EXENTO"),
            _ when tieneIvaEnRegimenGeneral => ("1", "RESPONSABLE INSCRIPTO"),
            _ => ("3", "A CONSUMIDOR FINAL")
        };
    }
}

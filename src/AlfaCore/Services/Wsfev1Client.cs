using System.Globalization;
using System.Text;
using System.Xml.Linq;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class Wsfev1Client(IHttpClientFactory httpClientFactory) : IWsfev1Client
{
    private const string UrlHomologacion = "https://wswhomo.afip.gov.ar/wsfev1/service.asmx";
    private const string UrlProduccion = "https://servicios1.afip.gov.ar/wsfev1/service.asmx";
    private static readonly XNamespace Ns = "http://ar.gov.afip.dif.FEV1/";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";

    public async Task<long> ObtenerUltimoAutorizadoAsync(WsaaTicket ticket, string cuit, int ptoVta, int cbteTipo, ArcaAmbiente ambiente, CancellationToken ct)
    {
        var body = new XElement(Ns + "FECompUltimoAutorizado",
            BuildAuth(ticket, cuit),
            new XElement(Ns + "PtoVta", ptoVta),
            new XElement(Ns + "CbteTipo", cbteTipo));

        var response = await PostAsync(body, ambiente, ct);
        var result = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECompUltimoAutorizadoResult")
            ?? throw new ArcaWsfeException("La respuesta de FECompUltimoAutorizado no trae el resultado esperado.");

        VerificarErrores(result, "FECompUltimoAutorizado");

        var cbteNroTexto = result.Descendants().FirstOrDefault(x => x.Name.LocalName == "CbteNro")?.Value
            ?? throw new ArcaWsfeException("FECompUltimoAutorizado no devolvió CbteNro.");

        return long.Parse(cbteNroTexto, CultureInfo.InvariantCulture);
    }

    public async Task<ArcaCaeResultadoDto> SolicitarCaeAsync(WsaaTicket ticket, ArcaCaeSolicitudDto solicitud, ArcaAmbiente ambiente, CancellationToken ct)
    {
        try
        {
            var detalle = new XElement(Ns + "FECAEDetRequest",
                new XElement(Ns + "Concepto", solicitud.Concepto),
                new XElement(Ns + "DocTipo", solicitud.DocTipo),
                new XElement(Ns + "DocNro", solicitud.DocNro),
                new XElement(Ns + "CbteDesde", solicitud.CbteDesde),
                new XElement(Ns + "CbteHasta", solicitud.CbteHasta),
                new XElement(Ns + "CbteFch", solicitud.CbteFch.ToString("yyyyMMdd")),
                new XElement(Ns + "ImpTotal", Formatear(solicitud.ImpTotal)),
                new XElement(Ns + "ImpTotConc", Formatear(solicitud.ImpTotConc)),
                new XElement(Ns + "ImpNeto", Formatear(solicitud.ImpNeto)),
                new XElement(Ns + "ImpOpEx", Formatear(solicitud.ImpOpEx)),
                new XElement(Ns + "ImpTrib", Formatear(solicitud.ImpTrib)),
                new XElement(Ns + "ImpIVA", Formatear(solicitud.ImpIva)),
                new XElement(Ns + "MonId", solicitud.MonId),
                new XElement(Ns + "MonCotiz", Formatear(solicitud.MonCotiz)),
                new XElement(Ns + "CondicionIVAReceptorId", solicitud.CondicionIvaReceptorId));

            if (solicitud.Ivas.Count > 0)
            {
                detalle.Add(new XElement(Ns + "Iva",
                    solicitud.Ivas.Select(iva => new XElement(Ns + "AlicIva",
                        new XElement(Ns + "Id", iva.Id),
                        new XElement(Ns + "BaseImp", Formatear(iva.BaseImponible)),
                        new XElement(Ns + "Importe", Formatear(iva.Importe))))));
            }

            var body = new XElement(Ns + "FECAESolicitar",
                BuildAuth(ticket, solicitud.Cuit),
                new XElement(Ns + "FeCAEReq",
                    new XElement(Ns + "FeCabReq",
                        new XElement(Ns + "CantReg", 1),
                        new XElement(Ns + "PtoVta", solicitud.PtoVta),
                        new XElement(Ns + "CbteTipo", solicitud.CbteTipo)),
                    new XElement(Ns + "FeDetReq", detalle)));

            var response = await PostAsync(body, ambiente, ct);
            var result = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECAESolicitarResult")
                ?? throw new ArcaWsfeException("La respuesta de FECAESolicitar no trae el resultado esperado.");

            return ParseResultado(result, solicitud.CbteDesde, solicitud.CbteHasta);
        }
        catch (ArcaWsfeException ex)
        {
            return ArcaCaeResultadoDto.Fallo(ex.Message, solicitud.CbteDesde, solicitud.CbteHasta);
        }
        catch (Exception ex)
        {
            return ArcaCaeResultadoDto.Fallo(ex.Message, solicitud.CbteDesde, solicitud.CbteHasta);
        }
    }

    private static ArcaCaeResultadoDto ParseResultado(XElement result, long cbteDesde, long cbteHasta)
    {
        var resultadoCabecera = result.Descendants().FirstOrDefault(x => x.Name.LocalName == "FeCabResp")
            ?.Elements().FirstOrDefault(x => x.Name.LocalName == "Resultado")?.Value;

        var detResp = result.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECAEDetResponse");

        var observaciones = (detResp?.Descendants().FirstOrDefault(x => x.Name.LocalName == "Observaciones")?.Elements() ?? [])
            .Select(obs => new ArcaObservacionDto(
                int.TryParse(obs.Elements().FirstOrDefault(x => x.Name.LocalName == "Code")?.Value, out var code) ? code : 0,
                obs.Elements().FirstOrDefault(x => x.Name.LocalName == "Msg")?.Value ?? string.Empty))
            .ToList();

        var errores = (result.Descendants().FirstOrDefault(x => x.Name.LocalName == "Errors")?.Elements() ?? [])
            .Select(err => new ArcaObservacionDto(
                int.TryParse(err.Elements().FirstOrDefault(x => x.Name.LocalName == "Code")?.Value, out var code) ? code : 0,
                err.Elements().FirstOrDefault(x => x.Name.LocalName == "Msg")?.Value ?? string.Empty))
            .ToList();

        if (detResp is null && errores.Count > 0)
            return new ArcaCaeResultadoDto(true, "R", cbteDesde, cbteHasta, null, null, errores, null);

        var resultadoDetalle = detResp?.Elements().FirstOrDefault(x => x.Name.LocalName == "Resultado")?.Value;
        var resultado = resultadoDetalle ?? resultadoCabecera ?? "R";

        var cae = detResp?.Elements().FirstOrDefault(x => x.Name.LocalName == "CAE")?.Value;
        var vtoTexto = detResp?.Elements().FirstOrDefault(x => x.Name.LocalName == "CAEFchVto")?.Value;
        DateTime? vto = null;
        if (!string.IsNullOrWhiteSpace(vtoTexto) && DateTime.TryParseExact(vtoTexto, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedVto))
            vto = parsedVto;

        return new ArcaCaeResultadoDto(true, resultado, cbteDesde, cbteHasta, cae, vto, [.. observaciones, .. errores], null);
    }

    private static XElement BuildAuth(WsaaTicket ticket, string cuit) => new(Ns + "Auth",
        new XElement(Ns + "Token", ticket.Token),
        new XElement(Ns + "Sign", ticket.Sign),
        new XElement(Ns + "Cuit", cuit));

    private static string Formatear(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);

    private static void VerificarErrores(XElement result, string operacion)
    {
        var errores = result.Descendants().FirstOrDefault(x => x.Name.LocalName == "Errors");
        if (errores is null) return;

        var mensajes = errores.Elements()
            .Select(err => $"{err.Elements().FirstOrDefault(x => x.Name.LocalName == "Code")?.Value}: {err.Elements().FirstOrDefault(x => x.Name.LocalName == "Msg")?.Value}");
        throw new ArcaWsfeException($"{operacion} devolvió error de AFIP: {string.Join(" | ", mensajes)}");
    }

    private async Task<XElement> PostAsync(XElement body, ArcaAmbiente ambiente, CancellationToken ct)
    {
        var envelope = new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soap.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ar", Ns.NamespaceName),
            new XElement(Soap + "Header"),
            new XElement(Soap + "Body", body));

        var url = ambiente == ArcaAmbiente.Produccion ? UrlProduccion : UrlHomologacion;
        var client = httpClientFactory.CreateClient("Arca");
        using var content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", $"{Ns.NamespaceName}{body.Name.LocalName}");
        using var response = await client.PostAsync(url, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new ArcaWsfeException($"WSFEv1 respondió {(int)response.StatusCode}: {responseBody}");

        try
        {
            return XElement.Parse(responseBody);
        }
        catch (Exception ex)
        {
            throw new ArcaWsfeException("La respuesta de WSFEv1 no es XML válido.", ex);
        }
    }
}

public sealed class ArcaWsfeException : Exception
{
    public ArcaWsfeException(string message) : base(message) { }
    public ArcaWsfeException(string message, Exception inner) : base(message, inner) { }
}

using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class WsaaClient(IHttpClientFactory httpClientFactory, IAppEventService appEvents) : IWsaaClient
{
    private const string ModuleName = "ArcaFacturacionElectronica";
    private static readonly TimeSpan MargenRenovacion = TimeSpan.FromMinutes(10);

    private const string UrlWsaaHomologacion = "https://wsaahomo.afip.gov.ar/ws/services/LoginCms";
    private const string UrlWsaaProduccion = "https://wsaa.afip.gov.ar/ws/services/LoginCms";

    public async Task<WsaaTicket> ObtenerTicketAsync(SqlConnection cn, ArcaEmisorConfig emisor, CancellationToken ct, string servicio = "wsfe")
    {
        servicio = string.IsNullOrWhiteSpace(servicio) ? "wsfe" : servicio.Trim();
        var ambienteTexto = emisor.Ambiente == ArcaAmbiente.Produccion ? "PRODUCCION" : "HOMOLOGACION";

        var cacheado = string.Equals(servicio, "wsfe", StringComparison.OrdinalIgnoreCase)
            ? await LeerCacheAsync(cn, emisor.Cuit, ambienteTexto, ct)
            : null;
        if (cacheado is not null && cacheado.VigentePara(DateTime.UtcNow, MargenRenovacion))
            return cacheado;

        try
        {
            var ticket = await AutenticarAsync(emisor, servicio, ct);
            if (string.Equals(servicio, "wsfe", StringComparison.OrdinalIgnoreCase))
                await GuardarCacheAsync(cn, emisor.Cuit, ambienteTexto, ticket, ct);
            return ticket;
        }
        catch (Exception ex) when (ex is not ArcaAutenticacionException)
        {
            await appEvents.LogErrorAsync(ModuleName, "WsaaAutenticar", ex, "No se pudo autenticar contra WSAA (AFIP).", new { emisor.Cuit, Ambiente = ambienteTexto }, ct: ct);
            throw new ArcaAutenticacionException("No se pudo autenticar contra WSAA (AFIP).", ex);
        }
    }

    private async Task<WsaaTicket> AutenticarAsync(ArcaEmisorConfig emisor, string servicio, CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var uniqueId = ((long)(ahora - DateTime.UnixEpoch).TotalSeconds).ToString();
        var generationTime = ahora.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var expirationTime = ahora.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:sszzz");

        var traXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <loginTicketRequest version="1.0">
              <header>
                <uniqueId>{uniqueId}</uniqueId>
                <generationTime>{generationTime}</generationTime>
                <expirationTime>{expirationTime}</expirationTime>
              </header>
              <service>{System.Security.SecurityElement.Escape(servicio)}</service>
            </loginTicketRequest>
            """;

        var cmsBase64 = FirmarCms(traXml, emisor.CertificadoPem, emisor.ClavePrivadaPem);

        var url = emisor.Ambiente == ArcaAmbiente.Produccion ? UrlWsaaProduccion : UrlWsaaHomologacion;
        var soapRequest = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:wsaa="http://wsaa.view.sua.dvadac.desein.afip.gov">
              <soapenv:Header/>
              <soapenv:Body>
                <wsaa:loginCms>
                  <wsaa:in0>{cmsBase64}</wsaa:in0>
                </wsaa:loginCms>
              </soapenv:Body>
            </soapenv:Envelope>
            """;

        var client = httpClientFactory.CreateClient("Arca");
        using var content = new StringContent(soapRequest, Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", "");
        using var response = await client.PostAsync(url, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new ArcaAutenticacionException($"WSAA respondió {(int)response.StatusCode}: {responseBody}");

        return ParseLoginCmsResponse(responseBody);
    }

    private static string FirmarCms(string traXml, string certificadoPem, string clavePrivadaPem)
    {
        using var cert = X509Certificate2.CreateFromPem(certificadoPem, clavePrivadaPem);
        var traBytes = Encoding.UTF8.GetBytes(traXml);
        var contentInfo = new ContentInfo(traBytes);
        var signedCms = new SignedCms(contentInfo, detached: false);
        var signer = new CmsSigner(cert) { IncludeOption = X509IncludeOption.EndCertOnly };
        signedCms.ComputeSignature(signer);
        return Convert.ToBase64String(signedCms.Encode());
    }

    private static WsaaTicket ParseLoginCmsResponse(string soapResponseBody)
    {
        XDocument envelope;
        try
        {
            envelope = XDocument.Parse(soapResponseBody);
        }
        catch (Exception ex)
        {
            throw new ArcaAutenticacionException("La respuesta de WSAA no es XML válido.", ex);
        }

        var loginCmsReturn = envelope.Descendants().FirstOrDefault(x => x.Name.LocalName == "loginCmsReturn")?.Value
            ?? throw new ArcaAutenticacionException($"La respuesta de WSAA no tiene el nodo esperado: {soapResponseBody}");

        var ticketXml = XDocument.Parse(loginCmsReturn);
        var token = ticketXml.Descendants("token").FirstOrDefault()?.Value
            ?? throw new ArcaAutenticacionException("El Ticket de Acceso de WSAA no trae token.");
        var sign = ticketXml.Descendants("sign").FirstOrDefault()?.Value
            ?? throw new ArcaAutenticacionException("El Ticket de Acceso de WSAA no trae sign.");
        var expirationTimeText = ticketXml.Descendants("expirationTime").FirstOrDefault()?.Value
            ?? throw new ArcaAutenticacionException("El Ticket de Acceso de WSAA no trae expirationTime.");

        var expirationTime = DateTimeOffset.Parse(expirationTimeText).UtcDateTime;
        return new WsaaTicket(token, sign, expirationTime);
    }

    private static async Task<WsaaTicket?> LeerCacheAsync(SqlConnection cn, string cuit, string ambiente, CancellationToken ct)
    {
        if (!await ExistsTablaAsync(cn, ct))
            return null;

        const string sql = """
            SELECT Token, Sign, FechaExpiracionUtc
            FROM dbo.ARCA_WSAA_TICKET
            WHERE Cuit = @Cuit AND Ambiente = @Ambiente;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Cuit", cuit);
        cmd.Parameters.AddWithValue("@Ambiente", ambiente);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new WsaaTicket(rd.GetString(0), rd.GetString(1), DateTime.SpecifyKind(rd.GetDateTime(2), DateTimeKind.Utc));
    }

    private static async Task GuardarCacheAsync(SqlConnection cn, string cuit, string ambiente, WsaaTicket ticket, CancellationToken ct)
    {
        if (!await ExistsTablaAsync(cn, ct))
            return;

        const string sql = """
            UPDATE dbo.ARCA_WSAA_TICKET
            SET Token = @Token, Sign = @Sign, FechaGeneracionUtc = @FechaGeneracionUtc, FechaExpiracionUtc = @FechaExpiracionUtc
            WHERE Cuit = @Cuit AND Ambiente = @Ambiente;

            IF @@ROWCOUNT = 0
                INSERT INTO dbo.ARCA_WSAA_TICKET (Cuit, Ambiente, Token, Sign, FechaGeneracionUtc, FechaExpiracionUtc)
                VALUES (@Cuit, @Ambiente, @Token, @Sign, @FechaGeneracionUtc, @FechaExpiracionUtc);
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Cuit", cuit);
        cmd.Parameters.AddWithValue("@Ambiente", ambiente);
        cmd.Parameters.AddWithValue("@Token", ticket.Token);
        cmd.Parameters.AddWithValue("@Sign", ticket.Sign);
        cmd.Parameters.AddWithValue("@FechaGeneracionUtc", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@FechaExpiracionUtc", ticket.ExpirationTimeUtc);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> ExistsTablaAsync(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT OBJECT_ID(N'dbo.ARCA_WSAA_TICKET', N'U');", cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null and not DBNull;
    }
}

public sealed class ArcaAutenticacionException : Exception
{
    public ArcaAutenticacionException(string message) : base(message) { }
    public ArcaAutenticacionException(string message, Exception inner) : base(message, inner) { }
}

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
    private const int ArcaSqlCommandTimeoutSeconds = 120;
    // El TRA que enviamos a WSAA tiene una vigencia de 10 minutos. Si el margen
    // también es de 10 minutos, `VigentePara` nunca puede ser verdadero y cada
    // venta vuelve a ejecutar LoginCms, agregando una espera innecesaria y
    // aumentando la probabilidad de timeout contra ARCA.
    private static readonly TimeSpan MargenRenovacion = TimeSpan.FromSeconds(30);

    private const string UrlWsaaHomologacion = "https://wsaahomo.afip.gov.ar/ws/services/LoginCms";
    private const string UrlWsaaProduccion = "https://wsaa.afip.gov.ar/ws/services/LoginCms";
    private WsaaTicket? _ticketWsfeMemoria;
    private string? _claveTicketWsfeMemoria;

    public async Task<WsaaTicket> ObtenerTicketAsync(SqlConnection cn, ArcaEmisorConfig emisor, CancellationToken ct, string servicio = "wsfe")
    {
        servicio = string.IsNullOrWhiteSpace(servicio) ? "wsfe" : servicio.Trim();
        var ambienteTexto = emisor.Ambiente == ArcaAmbiente.Produccion ? "PRODUCCION" : "HOMOLOGACION";
        var claveMemoria = $"{emisor.Cuit}|{ambienteTexto}|{servicio}";

        if (string.Equals(servicio, "wsfe", StringComparison.OrdinalIgnoreCase)
            && string.Equals(_claveTicketWsfeMemoria, claveMemoria, StringComparison.OrdinalIgnoreCase)
            && _ticketWsfeMemoria is not null
            && _ticketWsfeMemoria.VigentePara(DateTime.UtcNow, MargenRenovacion))
            return _ticketWsfeMemoria;

        if (!string.Equals(servicio, "wsfe", StringComparison.OrdinalIgnoreCase))
            return await ObtenerTicketServicioAsync(cn, emisor, ambienteTexto, servicio, ct);

        var cacheado = await LeerCacheAsync(cn, emisor.Cuit, ambienteTexto, ct);
        if (cacheado is not null && cacheado.VigentePara(DateTime.UtcNow, MargenRenovacion))
            return cacheado;

        try
        {
            var ticket = await AutenticarAsync(emisor, servicio, ct);
            if (string.Equals(servicio, "wsfe", StringComparison.OrdinalIgnoreCase))
            {
                _claveTicketWsfeMemoria = claveMemoria;
                _ticketWsfeMemoria = ticket;
                await GuardarCacheAsync(cn, emisor.Cuit, ambienteTexto, ticket, ct);
            }
            return ticket;
        }
        catch (Exception ex) when (ex is not ArcaAutenticacionException)
        {
            await appEvents.LogErrorAsync(ModuleName, "WsaaAutenticar", ex, "No se pudo autenticar contra WSAA (AFIP).", new { emisor.Cuit, Ambiente = ambienteTexto }, ct: ct);
            throw new ArcaAutenticacionException("No se pudo autenticar contra WSAA (AFIP).", ex);
        }
    }

    // ── Servicios distintos de wsfe (padrón, etc.) ────────────────────────────────────────────────
    // ARCA entrega un solo ticket vigente (~12 h) por certificado y servicio: pedir otro mientras
    // el anterior sigue vigente devuelve "ya posee un TA válido". Por eso el ticket se guarda en
    // memoria (todo el proceso) y en dbo.ARCA_WSAA_TICKET_SERVICIO, y se pide bajo un bloqueo SQL
    // para que dos consultas simultáneas no lo pidan dos veces.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WsaaTicket> TicketsServicio = new(StringComparer.OrdinalIgnoreCase);

    private async Task<WsaaTicket> ObtenerTicketServicioAsync(SqlConnection cn, ArcaEmisorConfig emisor, string ambiente, string servicio, CancellationToken ct)
    {
        var huella = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(emisor.CertificadoPem)))[..16];
        var clave = $"{emisor.Cuit}|{ambiente}|{servicio}|{huella}";
        if (TicketsServicio.TryGetValue(clave, out var enMemoria) && enMemoria.VigentePara(DateTime.UtcNow, MargenRenovacion))
            return enMemoria;

        var recurso = $"WSAA.{emisor.Cuit}.{ambiente}.{servicio}";
        await using (var lockCmd = new SqlCommand(
            "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@Recurso,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=60000; SELECT @r;", cn)
            { CommandTimeout = ArcaSqlCommandTimeoutSeconds })
        {
            lockCmd.Parameters.AddWithValue("@Recurso", recurso);
            if (Convert.ToInt32(await lockCmd.ExecuteScalarAsync(ct)) < 0)
                throw new ArcaAutenticacionException("Hay otra consulta autenticándose en ARCA con el mismo certificado. Probá de nuevo en unos segundos.");
        }
        try
        {
            // Otra consulta pudo haberlo obtenido mientras se esperaba el bloqueo.
            var guardado = await LeerTicketServicioAsync(cn, emisor.Cuit, ambiente, servicio, huella, ct);
            if (guardado is not null && guardado.VigentePara(DateTime.UtcNow, MargenRenovacion))
            {
                TicketsServicio[clave] = guardado;
                return guardado;
            }
            WsaaTicket ticket;
            try { ticket = await AutenticarAsync(emisor, servicio, ct); }
            catch (ArcaAutenticacionException ex) when (ex.Message.Contains("alreadyAuthenticated", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("TA valido", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("TA válido", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArcaAutenticacionException(
                    $"ARCA ya entregó un ticket vigente de {servicio} para este certificado a otro equipo o instalación, y no lo vuelve a dar hasta que venza (hasta 12 horas). " +
                    "Si el certificado se usa en varias instalaciones, conviene que consulten a través del servicio central.", ex);
            }
            TicketsServicio[clave] = ticket;
            await GuardarTicketServicioAsync(cn, emisor.Cuit, ambiente, servicio, huella, ticket, ct);
            return ticket;
        }
        catch (Exception ex) when (ex is not ArcaAutenticacionException and not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "WsaaAutenticarServicio", ex, "No se pudo autenticar contra WSAA (AFIP).",
                new { emisor.Cuit, Ambiente = ambiente, Servicio = servicio }, ct: ct);
            throw new ArcaAutenticacionException("No se pudo autenticar contra WSAA (AFIP).", ex);
        }
        finally
        {
            await using var unlock = new SqlCommand("EXEC sys.sp_releaseapplock @Resource=@Recurso,@LockOwner='Session';", cn);
            unlock.Parameters.AddWithValue("@Recurso", recurso);
            try { await unlock.ExecuteNonQueryAsync(CancellationToken.None); } catch (SqlException) { }
        }
    }

    private static async Task<WsaaTicket?> LeerTicketServicioAsync(SqlConnection cn, string cuit, string ambiente, string servicio, string huella, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.ARCA_WSAA_TICKET_SERVICIO', N'U') IS NOT NULL
                SELECT Token, Sign, FechaExpiracionUtc FROM dbo.ARCA_WSAA_TICKET_SERVICIO
                WHERE Cuit = @Cuit AND Ambiente = @Ambiente AND Servicio = @Servicio AND Certificado = @Huella;
            """, cn) { CommandTimeout = ArcaSqlCommandTimeoutSeconds };
        cmd.Parameters.AddWithValue("@Cuit", cuit);
        cmd.Parameters.AddWithValue("@Ambiente", ambiente);
        cmd.Parameters.AddWithValue("@Servicio", servicio);
        cmd.Parameters.AddWithValue("@Huella", huella);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) return null;
        return new WsaaTicket(rd.GetString(0), rd.GetString(1), DateTime.SpecifyKind(rd.GetDateTime(2), DateTimeKind.Utc));
    }

    private static async Task GuardarTicketServicioAsync(SqlConnection cn, string cuit, string ambiente, string servicio, string huella, WsaaTicket ticket, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.ARCA_WSAA_TICKET_SERVICIO', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.ARCA_WSAA_TICKET_SERVICIO
                SET Certificado = @Huella, Token = @Token, Sign = @Sign, FechaGeneracionUtc = SYSUTCDATETIME(), FechaExpiracionUtc = @Expira
                WHERE Cuit = @Cuit AND Ambiente = @Ambiente AND Servicio = @Servicio;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.ARCA_WSAA_TICKET_SERVICIO (Cuit, Ambiente, Servicio, Certificado, Token, Sign, FechaGeneracionUtc, FechaExpiracionUtc)
                    VALUES (@Cuit, @Ambiente, @Servicio, @Huella, @Token, @Sign, SYSUTCDATETIME(), @Expira);
            END;
            """, cn) { CommandTimeout = ArcaSqlCommandTimeoutSeconds };
        cmd.Parameters.AddWithValue("@Cuit", cuit);
        cmd.Parameters.AddWithValue("@Ambiente", ambiente);
        cmd.Parameters.AddWithValue("@Servicio", servicio);
        cmd.Parameters.AddWithValue("@Huella", huella);
        cmd.Parameters.AddWithValue("@Token", ticket.Token);
        cmd.Parameters.AddWithValue("@Sign", ticket.Sign);
        cmd.Parameters.AddWithValue("@Expira", ticket.ExpirationTimeUtc);
        await cmd.ExecuteNonQueryAsync(ct);
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

        var url = string.IsNullOrWhiteSpace(emisor.WsaaUrl)
            ? (emisor.Ambiente == ArcaAmbiente.Produccion ? UrlWsaaProduccion : UrlWsaaHomologacion)
            : NormalizarEndpoint(emisor.WsaaUrl);
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

    private static string NormalizarEndpoint(string url)
    {
        var resultado = url.Trim();
        var indice = resultado.IndexOf('?', StringComparison.Ordinal);
        return indice >= 0 && resultado[(indice + 1)..].Equals("wsdl", StringComparison.OrdinalIgnoreCase)
            ? resultado[..indice]
            : resultado;
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

        await using var cmd = new SqlCommand(sql, cn)
        {
            CommandTimeout = ArcaSqlCommandTimeoutSeconds
        };
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

        await using var cmd = new SqlCommand(sql, cn)
        {
            CommandTimeout = ArcaSqlCommandTimeoutSeconds
        };
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
        await using var cmd = new SqlCommand("SELECT OBJECT_ID(N'dbo.ARCA_WSAA_TICKET', N'U');", cn)
        {
            CommandTimeout = ArcaSqlCommandTimeoutSeconds
        };
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null and not DBNull;
    }
}

public sealed class ArcaAutenticacionException : Exception
{
    public ArcaAutenticacionException(string message) : base(message) { }
    public ArcaAutenticacionException(string message, Exception inner) : base(message, inner) { }
}

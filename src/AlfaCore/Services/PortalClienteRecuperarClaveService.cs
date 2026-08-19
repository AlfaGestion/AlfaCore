using System.Security.Cryptography;
using System.Net;
using System.Net.Mail;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Recuperación de contraseña del Portal Cliente (MA_CUENTASADIC.CLAVE). No toca el login actual:
// solo agrega la posibilidad de generar un token de un solo uso, con vencimiento, para establecer
// una CLAVE nueva. El token nunca se guarda en texto plano (se persiste su hash SHA-256).
// Los mensajes de "Solicitar" son deliberadamente genéricos (mismo texto exista o no la cuenta)
// para no revelar si un código/email está registrado, salvo el caso de email ambiguo, donde hace
// falta pedir el código para poder continuar.
public sealed class PortalClienteRecuperarClaveService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IPortalClienteRecuperarClaveService
{
    private const string ModuleName = "PortalClienteRecuperarClave";
    private const string MensajeGenerico = "Si los datos ingresados corresponden a una cuenta con email registrado, te enviaremos las instrucciones para recuperar el acceso.";
    private const string MensajeEmailAmbiguo = "El email está asociado a más de una cuenta. Ingresá tu código de cliente.";
    private const string MensajeSinEmail = "No tenemos un email registrado para esta cuenta. Contactate con la empresa para recuperar el acceso.";
    private const string MensajeTokenInvalido = "El enlace no es válido o ya venció. Solicitá uno nuevo.";
    private const string MensajeClaveActualizada = "Tu contraseña se actualizó correctamente. Ya podés ingresar con la nueva clave.";
    private const int ExpiracionMinutos = 60;
    private const int ClaveLongitudMaxima = 15;
    private const int ClaveLongitudMinima = 4;

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<PortalClienteRecuperarClaveResultDto> SolicitarAsync(PortalClienteRecuperarClaveRequestDto request, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            var identificador = (request.Identificador ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(identificador))
                return new PortalClienteRecuperarClaveResultDto { Mensaje = MensajeGenerico };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            if (!await SqlObjectExistsAsync(cn, "VT_CLIENTES", ct) || !await SqlObjectExistsAsync(cn, "MA_CUENTASADIC", ct))
                return new PortalClienteRecuperarClaveResultDto { Mensaje = MensajeGenerico };

            await EnsureResetTokenTableAsync(cn, ct);

            var esEmail = identificador.Contains('@');
            var codigoCliente = identificador;

            if (esEmail)
            {
                var codigos = (await cn.QueryAsync<string>(new CommandDefinition(
                    """
                    SELECT DISTINCT LTRIM(RTRIM(cli.CODIGO)) AS Codigo
                    FROM dbo.VT_CLIENTES cli
                    WHERE UPPER(LTRIM(RTRIM(ISNULL(cli.MAIL, '')))) = UPPER(LTRIM(RTRIM(@Email)));
                    """,
                    new { Email = identificador },
                    cancellationToken: ct))).ToList();

                if (codigos.Count == 0)
                    return new PortalClienteRecuperarClaveResultDto { Mensaje = MensajeGenerico };

                if (codigos.Count > 1)
                    return new PortalClienteRecuperarClaveResultDto { RequiereCodigoCliente = true, Mensaje = MensajeEmailAmbiguo };

                codigoCliente = codigos[0];
            }

            var cliente = await cn.QuerySingleOrDefaultAsync<ClienteRow>(new CommandDefinition(
                """
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(cli.CODIGO)), '') AS CodigoCliente,
                    ISNULL(LTRIM(RTRIM(cli.RAZON_SOCIAL)), '') AS RazonSocial,
                    ISNULL(LTRIM(RTRIM(cli.MAIL)), '') AS Email
                FROM dbo.VT_CLIENTES cli
                WHERE UPPER(LTRIM(RTRIM(ISNULL(cli.CODIGO, '')))) = UPPER(LTRIM(RTRIM(@CodigoCliente)));
                """,
                new { CodigoCliente = codigoCliente },
                cancellationToken: ct));

            if (cliente is null)
                return new PortalClienteRecuperarClaveResultDto { Mensaje = MensajeGenerico };

            if (string.IsNullOrWhiteSpace(cliente.Email))
                return new PortalClienteRecuperarClaveResultDto { Mensaje = MensajeSinEmail };

            if (!await SqlObjectExistsAsync(cn, "ALFACORE_CLIENTE_RESET_TOKEN", ct))
                return new PortalClienteRecuperarClaveResultDto { Mensaje = MensajeGenerico };

            var token = GenerarToken();
            var tokenHash = HashToken(token);
            var expiracion = DateTime.Now.AddMinutes(ExpiracionMinutos);

            await using (var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct))
            {
                // Cualquier token pendiente anterior para este cliente queda inutilizado: un link
                // viejo (por ejemplo, reenviado por error) no debe seguir sirviendo.
                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.ALFACORE_CLIENTE_RESET_TOKEN
                    SET Usado = 1, FechaHora_Uso = GETDATE()
                    WHERE UPPER(LTRIM(RTRIM(CodigoCliente))) = UPPER(LTRIM(RTRIM(@CodigoCliente)))
                      AND Usado = 0;
                    """,
                    new { CodigoCliente = cliente.CodigoCliente },
                    transaction: tx,
                    cancellationToken: ct));

                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.ALFACORE_CLIENTE_RESET_TOKEN
                        (CodigoCliente, IdWeb, IdBase, TokenHash, FechaHora_Expiracion)
                    VALUES
                        (@CodigoCliente, @IdWeb, @IdBase, @TokenHash, @Expiracion);
                    """,
                    new
                    {
                        CodigoCliente = cliente.CodigoCliente,
                        IdWeb = string.IsNullOrWhiteSpace(request.IdWeb) ? null : request.IdWeb.Trim(),
                        request.IdBase,
                        TokenHash = tokenHash,
                        Expiracion = expiracion
                    },
                    transaction: tx,
                    cancellationToken: ct));

                await tx.CommitAsync(ct);
            }

            var urlRestablecer = $"{(request.UrlBaseRestablecer ?? string.Empty).TrimEnd('/')}?token={Uri.EscapeDataString(token)}";
            var nombreEmpresa = await ResolveCompanyNameAsync(cn, request.NombreEmpresa, ct);
            var logoUrlAbsoluta = await ResolveLogoUrlAsync(cn, request.LogoUrlAbsoluta, request.UrlBaseRestablecer, ct);

            var enviado = await SendRecoveryEmailAsync(
                cliente.Email,
                cliente.RazonSocial,
                nombreEmpresa,
                logoUrlAbsoluta,
                urlRestablecer,
                ct);

            if (!enviado)
            {
                await appEvents.LogErrorAsync(
                    ModuleName, "Solicitar", new InvalidOperationException("Fallo el envío del email de recuperación."),
                    "No se pudo enviar el email de recuperación de contraseña.",
                    new { cliente.CodigoCliente }, AppEventSeverity.Warning, ct);
            }

            return new PortalClienteRecuperarClaveResultDto { Mensaje = MensajeGenerico };
        }
        catch (Exception ex)
        {
            // Nunca se propaga el error real: revelar una falla técnica acá también filtra
            // información (por ejemplo, que el código/email sí existe). Se responde siempre el
            // mismo mensaje genérico y se deja constancia para diagnóstico interno.
            await appEvents.LogErrorAsync(
                ModuleName, "Solicitar", ex, "No se pudo procesar la solicitud de recuperación de contraseña.",
                new { request?.Identificador }, AppEventSeverity.Warning, ct);
            return new PortalClienteRecuperarClaveResultDto { Mensaje = MensajeGenerico };
        }
    }

    public async Task<PortalClienteValidarTokenResultDto> ValidarTokenAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var trimmed = (token ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return new PortalClienteValidarTokenResultDto { Valido = false, Mensaje = MensajeTokenInvalido };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            await EnsureResetTokenTableAsync(cn, ct);

            var vigente = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.ALFACORE_CLIENTE_RESET_TOKEN
                WHERE TokenHash = @Hash AND Usado = 0 AND FechaHora_Expiracion > GETDATE();
                """,
                new { Hash = HashToken(trimmed) },
                cancellationToken: ct));

            return vigente > 0
                ? new PortalClienteValidarTokenResultDto { Valido = true }
                : new PortalClienteValidarTokenResultDto { Valido = false, Mensaje = MensajeTokenInvalido };
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "ValidarToken", ex, "No se pudo validar el enlace de recuperación.", null, AppEventSeverity.Warning, ct);
            return new PortalClienteValidarTokenResultDto { Valido = false, Mensaje = MensajeTokenInvalido };
        }
    }

    public async Task<PortalClienteRestablecerClaveResultDto> RestablecerAsync(PortalClienteRestablecerClaveRequestDto request, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            var token = (request.Token ?? string.Empty).Trim();
            var nueva = request.NuevaClave ?? string.Empty;
            var confirmar = request.ConfirmarClave ?? string.Empty;

            if (string.IsNullOrWhiteSpace(token))
                return new PortalClienteRestablecerClaveResultDto { Exito = false, Mensaje = MensajeTokenInvalido };

            if (nueva.Length < ClaveLongitudMinima)
                return new PortalClienteRestablecerClaveResultDto { Exito = false, Mensaje = $"La contraseña debe tener al menos {ClaveLongitudMinima} caracteres." };

            if (nueva.Length > ClaveLongitudMaxima)
                return new PortalClienteRestablecerClaveResultDto { Exito = false, Mensaje = $"La contraseña no puede tener más de {ClaveLongitudMaxima} caracteres." };

            if (!string.Equals(nueva, confirmar, StringComparison.Ordinal))
                return new PortalClienteRestablecerClaveResultDto { Exito = false, Mensaje = "Las contraseñas no coinciden." };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            if (!await SqlObjectExistsAsync(cn, "MA_CUENTASADIC", ct))
                return new PortalClienteRestablecerClaveResultDto { Exito = false, Mensaje = MensajeTokenInvalido };

            await EnsureResetTokenTableAsync(cn, ct);

            var tokenRow = await cn.QuerySingleOrDefaultAsync<TokenRow>(new CommandDefinition(
                """
                SELECT TOP (1) IdToken, CodigoCliente
                FROM dbo.ALFACORE_CLIENTE_RESET_TOKEN
                WHERE TokenHash = @Hash AND Usado = 0 AND FechaHora_Expiracion > GETDATE();
                """,
                new { Hash = HashToken(token) },
                cancellationToken: ct));

            if (tokenRow is null)
                return new PortalClienteRestablecerClaveResultDto { Exito = false, Mensaje = MensajeTokenInvalido };

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            try
            {
                // Sólo actualiza CLAVE: nunca toca NUMERO_DOCUMENTO ni ninguna otra columna.
                var actualizado = await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.MA_CUENTASADIC SET CLAVE = @Clave WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));",
                    new { Clave = nueva, Codigo = tokenRow.CodigoCliente },
                    transaction: tx,
                    cancellationToken: ct));

                if (actualizado == 0)
                {
                    await tx.RollbackAsync(ct);
                    return new PortalClienteRestablecerClaveResultDto { Exito = false, Mensaje = "No pudimos actualizar tu contraseña. Contactate con la empresa." };
                }

                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.ALFACORE_CLIENTE_RESET_TOKEN
                    SET Usado = 1, FechaHora_Uso = GETDATE()
                    WHERE UPPER(LTRIM(RTRIM(CodigoCliente))) = UPPER(LTRIM(RTRIM(@Codigo)))
                      AND Usado = 0;
                    """,
                    new { Codigo = tokenRow.CodigoCliente },
                    transaction: tx,
                    cancellationToken: ct));

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            await appEvents.LogAuditAsync(
                ModuleName, "Restablecer", "MA_CUENTASADIC", tokenRow.CodigoCliente,
                "El cliente restableció su contraseña mediante el enlace de recuperación del Portal Cliente.",
                new { CodigoCliente = tokenRow.CodigoCliente }, ct);

            return new PortalClienteRestablecerClaveResultDto { Exito = true, Mensaje = MensajeClaveActualizada };
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "Restablecer", ex, "No se pudo restablecer la contraseña.", null, AppEventSeverity.Warning, ct);
            return new PortalClienteRestablecerClaveResultDto { Exito = false, Mensaje = "No pudimos actualizar tu contraseña en este momento. Intentá nuevamente." };
        }
    }

    private static string GenerarToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static async Task<bool> SqlObjectExistsAsync(SqlConnection cn, string objectName, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE
                WHEN OBJECT_ID(@ObjectName, 'U') IS NOT NULL THEN 1
                WHEN OBJECT_ID(@ObjectName, 'V') IS NOT NULL THEN 1
                ELSE 0
            END;
            """;

        var exists = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { ObjectName = $"dbo.{objectName}" }, cancellationToken: ct));
        return exists == 1;
    }

    private async Task<bool> SendRecoveryEmailAsync(
        string emailDestino,
        string nombreCliente,
        string nombreEmpresa,
        string? logoUrlAbsoluta,
        string urlRestablecer,
        CancellationToken ct)
    {
        var smtpServer = ResolveServerConfig("RegistroPublico:EmailServer", "EMAIL_SERVER");
        var smtpPort = ResolveServerConfig("RegistroPublico:EmailPort", "EMAIL_PORT");
        var smtpAccount = ResolveServerConfig("RegistroPublico:EmailAccount", "EMAIL_CTA");
        var smtpPassword = ResolveServerConfig("RegistroPublico:EmailPassword", "EMAIL_PASS");
        var smtpSsl = ResolveServerConfig("RegistroPublico:EmailSsl", "EMAIL_SSL");

        if (string.IsNullOrWhiteSpace(smtpServer) ||
            string.IsNullOrWhiteSpace(smtpPort) ||
            string.IsNullOrWhiteSpace(smtpAccount) ||
            string.IsNullOrWhiteSpace(smtpPassword))
        {
            throw new InvalidOperationException("Falta configurar el correo saliente del registro público en el servidor.");
        }

        if (!int.TryParse(smtpPort, out var port) || port <= 0)
            throw new InvalidOperationException("El puerto SMTP del registro público no es válido.");

        using var message = new MailMessage
        {
            From = new MailAddress(smtpAccount.Trim(), string.IsNullOrWhiteSpace(nombreEmpresa) ? smtpAccount.Trim() : nombreEmpresa.Trim()),
            Subject = string.IsNullOrWhiteSpace(nombreEmpresa) ? "Recuperar contraseña" : $"Recuperar contraseña - {nombreEmpresa.Trim()}",
            Body = BuildRecoveryEmailHtml(nombreCliente, nombreEmpresa, logoUrlAbsoluta, urlRestablecer),
            IsBodyHtml = true
        };

        message.To.Add(emailDestino.Trim());

        using var client = new SmtpClient(smtpServer.Trim(), port)
        {
            EnableSsl = smtpSsl.Equals("SI", StringComparison.OrdinalIgnoreCase)
                        || smtpSsl.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                        || smtpSsl.Equals("1", StringComparison.OrdinalIgnoreCase),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(smtpAccount.Trim(), smtpPassword.Trim())
        };

        await client.SendMailAsync(message, ct);
        return true;
    }

    private string ResolveServerConfig(string primaryKey, string fallbackKey)
        => configuration[primaryKey]?.Trim()
           ?? configuration[fallbackKey]?.Trim()
           ?? string.Empty;

    private async Task<string> ResolveCompanyNameAsync(SqlConnection cn, string fallbackName, CancellationToken ct)
    {
        var nombre = await ReadConfigValueAsync(cn, "NOMBRE", ct);
        if (!string.IsNullOrWhiteSpace(nombre))
            return nombre.Trim();

        return string.IsNullOrWhiteSpace(fallbackName) ? string.Empty : fallbackName.Trim();
    }

    private async Task<string?> ResolveLogoUrlAsync(SqlConnection cn, string? fallbackLogoUrl, string? baseUrl, CancellationToken ct)
    {
        var logo = await ReadConfigValueAsync(cn, "LOGO", ct);
        if (string.IsNullOrWhiteSpace(logo))
            logo = configuration["RegistroPublico:LogoUrl"]?.Trim() ?? configuration["LOGO"]?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(logo))
            logo = fallbackLogoUrl?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(logo))
            return null;

        if (Uri.TryCreate(logo, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            return new Uri(baseUri, logo.TrimStart('/')).ToString();

        return logo;
    }

    private static async Task<string> ReadConfigValueAsync(SqlConnection cn, string clave, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) ISNULL(VALOR, N'')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(LTRIM(RTRIM(@Clave)));
            """;

        try
        {
            return (await cn.ExecuteScalarAsync<string>(new CommandDefinition(sql, new { Clave = clave }, cancellationToken: ct))) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildRecoveryEmailHtml(string nombreCliente, string nombreEmpresa, string? logoUrlAbsoluta, string urlRestablecer)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Recuperar contraseña</title></head>");
        sb.Append("<body style=\"margin:0;padding:24px;background:#f4f7fb;font-family:Segoe UI,Arial,sans-serif;color:#142133;\">");
        sb.Append("<div style=\"max-width:720px;margin:0 auto;background:#ffffff;border:1px solid #d9e3ef;border-radius:18px;overflow:hidden;\">");
        sb.Append("<div style=\"padding:24px 28px;background:#0f2138;color:#ffffff;\">");
        if (!string.IsNullOrWhiteSpace(logoUrlAbsoluta))
            sb.Append("<img src=\"").Append(E(logoUrlAbsoluta)).Append("\" alt=\"").Append(E(nombreEmpresa))
              .Append("\" style=\"max-height:44px;max-width:220px;display:block;margin-bottom:10px;\" />");
        sb.Append("<h1 style=\"margin:0;font-size:28px;\">Recuperar contraseña</h1>");
        sb.Append("</div>");
        sb.Append("<div style=\"padding:28px;\">");
        sb.Append("<p style=\"margin:0 0 16px;\">Hola").Append(string.IsNullOrWhiteSpace(nombreCliente) ? "" : $" {E(nombreCliente)}").Append(".</p>");
        sb.Append("<p style=\"margin:0 0 16px;\">Recibimos una solicitud para restablecer tu contraseña de acceso al Portal Cliente. Si fuiste vos, hacé clic en el siguiente botón para definir una nueva contraseña:</p>");
        sb.Append($"<p style=\"margin:24px 0;\"><a href=\"{E(urlRestablecer)}\" style=\"display:inline-block;padding:12px 20px;background:#0b74c9;color:#ffffff;text-decoration:none;border-radius:10px;font-weight:600;\">Definir nueva contraseña</a></p>");
        sb.Append("<p style=\"margin:0 0 8px;font-size:12px;color:#64748b;\">Este enlace vence en 1 hora y solo puede usarse una vez.</p>");
        sb.Append("<p style=\"margin:0;font-size:12px;color:#64748b;\">Si no solicitaste este cambio, podés ignorar este email: tu contraseña actual sigue funcionando normalmente.</p>");
        sb.Append("</div></div></body></html>");
        return sb.ToString();
    }

    private static async Task EnsureResetTokenTableAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ALFACORE_CLIENTE_RESET_TOKEN
                (
                    IdToken                 int IDENTITY(1,1) NOT NULL,
                    CodigoCliente           nvarchar(30) NOT NULL,
                    IdWeb                   nvarchar(100) NULL,
                    IdBase                  int NULL,
                    TokenHash               nvarchar(64) NOT NULL,
                    FechaHora_Creacion      datetime NOT NULL CONSTRAINT DF_ALFACORE_CLIENTE_RESET_TOKEN_FHCreacion DEFAULT (GETDATE()),
                    FechaHora_Expiracion    datetime NOT NULL,
                    Usado                   bit NOT NULL CONSTRAINT DF_ALFACORE_CLIENTE_RESET_TOKEN_Usado DEFAULT (0),
                    FechaHora_Uso           datetime NULL,
                    CONSTRAINT PK_ALFACORE_CLIENTE_RESET_TOKEN PRIMARY KEY CLUSTERED (IdToken ASC)
                );
            END;

            IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN', N'U') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1
                   FROM sys.indexes
                   WHERE name = N'UX_ALFACORE_CLIENTE_RESET_TOKEN_HASH'
                     AND object_id = OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN')
               )
            BEGIN
                CREATE UNIQUE NONCLUSTERED INDEX UX_ALFACORE_CLIENTE_RESET_TOKEN_HASH
                    ON dbo.ALFACORE_CLIENTE_RESET_TOKEN (TokenHash ASC);
            END;

            IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN', N'U') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1
                   FROM sys.indexes
                   WHERE name = N'IX_ALFACORE_CLIENTE_RESET_TOKEN_CLIENTE'
                     AND object_id = OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN')
               )
            BEGIN
                CREATE NONCLUSTERED INDEX IX_ALFACORE_CLIENTE_RESET_TOKEN_CLIENTE
                    ON dbo.ALFACORE_CLIENTE_RESET_TOKEN (CodigoCliente ASC, Usado ASC);
            END;
            """;

        await cn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    private sealed class ClienteRow
    {
        public string CodigoCliente { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    private sealed class TokenRow
    {
        public int IdToken { get; set; }
        public string CodigoCliente { get; set; } = string.Empty;
    }
}

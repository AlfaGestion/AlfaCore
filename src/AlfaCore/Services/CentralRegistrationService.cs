using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface ICentralRegistrationService
{
    Task<PublicRegistrationResult> RegisterAsync(PublicRegistrationRequest request, CancellationToken ct = default);
    Task<PublicVerificationResult> VerifyAsync(string code, CancellationToken ct = default);
}

public sealed class CentralRegistrationService(
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    IRecaptchaValidationService recaptchaValidationService,
    ICentralProvisioningService provisioningService,
    IAppEventService appEvents) : ICentralRegistrationService
{
    private const string ModuleName = "RegistroPublico";
    private const string AccountType = "M";

    private string CentralConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    private string AlfaGestionConnectionString => configuration.GetConnectionString("AlfaGestion")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<PublicRegistrationResult> RegisterAsync(PublicRegistrationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var normalized = Normalize(request);
            ValidateRequest(normalized);

            var remoteIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            var recaptcha = await recaptchaValidationService.ValidateAsync(normalized.RecaptchaToken, remoteIp, ct);
            if (!recaptcha.Success)
            {
                return new PublicRegistrationResult
                {
                    Success = false,
                    Message = recaptcha.Message
                };
            }

            await using var central = new SqlConnection(CentralConnectionString);
            await central.OpenAsync(ct);

            if (await EmailAlreadyExistsAsync(central, normalized.Email, ct))
            {
                return new PublicRegistrationResult
                {
                    Success = false,
                    Message = "El email ya se encuentra registrado."
                };
            }

            var idCliente = await CreateOfficialCustomerAsync(normalized, ct);
            var verificationCode = Guid.NewGuid().ToString("N");

            await using var tx = (SqlTransaction)await central.BeginTransactionAsync(ct);
            try
            {
                await InsertCentralClientAsync(central, tx, idCliente, normalized, verificationCode, ct);
                await InsertCentralUserAsync(central, tx, idCliente, normalized, ct);

                var verificationUrl = BuildVerificationUrl(normalized.PublicBaseUrl, verificationCode);
                await SendVerificationEmailAsync(normalized, verificationUrl, ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            await appEvents.LogAuditAsync(
                ModuleName,
                "Register",
                "ALFA_CENTRAL.clientes",
                idCliente,
                "Registro público creado y pendiente de verificación.",
                new
                {
                    idCliente,
                    normalized.Nombre,
                    normalized.Email
                },
                ct);

            return new PublicRegistrationResult
            {
                Success = true,
                Message = "Registro exitoso. Revisá tu correo para confirmar la cuenta.",
                VerificationEmailSent = true
            };
        }
        catch (InvalidOperationException ex)
        {
            return new PublicRegistrationResult
            {
                Success = false,
                Message = ex.Message
            };
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                ModuleName,
                "Register",
                ex,
                "No se pudo completar el registro público.",
                new
                {
                    request.Email,
                    request.Nombre,
                    request.Cuit
                },
                AppEventSeverity.Error,
                ct);

            return new PublicRegistrationResult
            {
                Success = false,
                Message = "No se pudo completar el registro. Si el problema sigue, avisá a soporte.",
                IncidentId = incidentId
            };
        }
    }

    public async Task<PublicVerificationResult> VerifyAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new PublicVerificationResult
            {
                Success = false,
                Message = "El código de verificación es inválido."
            };
        }

        try
        {
            await using var central = new SqlConnection(CentralConnectionString);
            await central.OpenAsync(ct);

            var pending = await LoadPendingRegistrationAsync(central, code.Trim(), ct);
            if (pending is null)
            {
                return new PublicVerificationResult
                {
                    Success = false,
                    Message = "El código de verificación no existe o ya fue usado."
                };
            }

            await MarkVerifiedAsync(central, pending.IdCliente, code.Trim(), ct);
            var official = await LoadOfficialCustomerAsync(pending.IdCliente, ct);

            var provisioning = await provisioningService.ProvisionAsync(new PublicProvisioningRequest
            {
                IdCliente = pending.IdCliente,
                RazonSocial = official.RazonSocial,
                Telefono = official.Telefono,
                Email = pending.Email,
                Cuit = official.Cuit,
                Iva = official.Iva,
                UserName = pending.Email,
                Password = pending.Password
            }, ct);

            return new PublicVerificationResult
            {
                Success = provisioning.Success,
                AccountVerified = true,
                ProvisioningCompleted = provisioning.Success,
                Message = provisioning.Success
                    ? "La cuenta fue confirmada y la base quedó preparada correctamente."
                    : provisioning.Message,
                DatabaseName = provisioning.DatabaseName
            };
        }
        catch (InvalidOperationException ex)
        {
            return new PublicVerificationResult
            {
                Success = false,
                AccountVerified = true,
                ProvisioningCompleted = false,
                Message = ex.Message
            };
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                ModuleName,
                "Verify",
                ex,
                "No se pudo verificar la cuenta pública.",
                new { Code = code.Trim() },
                AppEventSeverity.Error,
                ct);

            return new PublicVerificationResult
            {
                Success = false,
                Message = "No se pudo confirmar la cuenta. Si el problema sigue, avisá a soporte.",
                IncidentId = incidentId
            };
        }
    }

    private static PublicRegistrationRequest Normalize(PublicRegistrationRequest request)
        => new()
        {
            Nombre = request.Nombre?.Trim() ?? string.Empty,
            Telefono = request.Telefono?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty,
            Password = request.Password ?? string.Empty,
            PasswordConfirmacion = request.PasswordConfirmacion ?? string.Empty,
            Cuit = request.Cuit?.Trim() ?? string.Empty,
            Iva = request.Iva?.Trim() ?? string.Empty,
            RecaptchaToken = request.RecaptchaToken?.Trim() ?? string.Empty,
            PublicBaseUrl = request.PublicBaseUrl?.Trim() ?? string.Empty
        };

    private static void ValidateRequest(PublicRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre))
            throw new InvalidOperationException("El nombre es requerido.");
        if (request.Nombre.Length > 50)
            throw new InvalidOperationException("El nombre no puede superar los 50 caracteres.");
        if (string.IsNullOrWhiteSpace(request.Telefono))
            throw new InvalidOperationException("El teléfono es requerido.");
        if (request.Telefono.Length > 50)
            throw new InvalidOperationException("El teléfono no puede superar los 50 caracteres.");
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new InvalidOperationException("El email es requerido.");
        if (!MailAddress.TryCreate(request.Email, out _))
            throw new InvalidOperationException("El email ingresado no es válido.");
        if (request.Password.Length < 5 || request.Password.Length > 50)
            throw new InvalidOperationException("La contraseña debe tener entre 5 y 50 caracteres.");
        if (!string.Equals(request.Password, request.PasswordConfirmacion, StringComparison.Ordinal))
            throw new InvalidOperationException("Las contraseñas no coinciden.");
    }

    private async Task<bool> EmailAlreadyExistsAsync(SqlConnection central, string email, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.users
            WHERE UPPER(LTRIM(RTRIM([user]))) = UPPER(LTRIM(RTRIM(@Email)));
            """;

        var count = await central.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Email = email }, cancellationToken: ct));
        return count > 0;
    }

    private async Task<string> CreateOfficialCustomerAsync(PublicRegistrationRequest request, CancellationToken ct)
    {
        await using var cn = new SqlConnection(AlfaGestionConnectionString);
        await cn.OpenAsync(ct);

        await using var cmd = new SqlCommand("dbo.sp_web_altaClienteAlfa", cn)
        {
            CommandType = CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@pNombre", request.Nombre);
        cmd.Parameters.AddWithValue("@pEmail", DbNullable(request.Email));
        cmd.Parameters.AddWithValue("@pTel", DbNullable(request.Telefono));
        cmd.Parameters.AddWithValue("@pCuit", DbNullable(request.Cuit));
        cmd.Parameters.AddWithValue("@pIva", DbNullable(request.Iva));
        var codeParam = new SqlParameter("@pCodigoCuenta", SqlDbType.VarChar, 9)
        {
            Direction = ParameterDirection.Output
        };
        cmd.Parameters.Add(codeParam);

        await cmd.ExecuteNonQueryAsync(ct);

        var idCliente = Convert.ToString(codeParam.Value)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(idCliente))
            throw new InvalidOperationException("No se pudo crear el cliente oficial en Alfa Gestión.");

        return idCliente;
    }

    private async Task InsertCentralClientAsync(
        SqlConnection central,
        SqlTransaction tx,
        string idCliente,
        PublicRegistrationRequest request,
        string verificationCode,
        CancellationToken ct)
    {
        var columns = await GetColumnNamesAsync(central, tx, "Clientes", ct);
        var names = new List<string> { "idcliente", "nombre", "superadmin" };
        var values = new List<string> { "@IdCliente", "@Nombre", "@SuperAdmin" };

        if (columns.Contains("idweb"))
        {
            names.Add("idweb");
            values.Add("@IdWeb");
        }

        if (columns.Contains("password"))
        {
            names.Add("password");
            values.Add("@Password");
        }

        if (columns.Contains("verified"))
        {
            names.Add("verified");
            values.Add("@Verified");
        }

        if (columns.Contains("type"))
        {
            names.Add("type");
            values.Add("@Type");
        }

        if (columns.Contains("verified_code"))
        {
            names.Add("verified_code");
            values.Add("@VerifiedCode");
        }

        if (columns.Contains("created"))
        {
            names.Add("created");
            values.Add("@Created");
        }

        var sql = $"""
            INSERT INTO dbo.Clientes ({string.Join(", ", names)})
            VALUES ({string.Join(", ", values)});
            """;

        await central.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                IdCliente = idCliente,
                Nombre = request.Nombre,
                SuperAdmin = 0,
                IdWeb = string.Empty,
                Password = request.Password,
                Verified = 0,
                Type = AccountType,
                VerifiedCode = verificationCode,
                Created = DateTime.Now
            },
            tx,
            cancellationToken: ct));
    }

    private static async Task InsertCentralUserAsync(
        SqlConnection central,
        SqlTransaction tx,
        string idCliente,
        PublicRegistrationRequest request,
        CancellationToken ct)
    {
        var columns = await GetColumnNamesAsync(central, tx, "users", ct);
        var names = new List<string> { "[user]", "password", "idcliente" };
        var values = new List<string> { "@UserName", "@Password", "@IdCliente" };

        if (columns.Contains("isadmin"))
        {
            names.Add("isAdmin");
            values.Add("@IsAdmin");
        }

        if (columns.Contains("name"))
        {
            names.Add("name");
            values.Add("@Name");
        }

        var sql = $"""
            INSERT INTO dbo.users ({string.Join(", ", names)})
            VALUES ({string.Join(", ", values)});
            """;

        await central.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                UserName = request.Email,
                Password = request.Password,
                IdCliente = idCliente,
                IsAdmin = 1,
                Name = request.Nombre
            },
            tx,
            cancellationToken: ct));
    }

    private async Task SendVerificationEmailAsync(PublicRegistrationRequest request, string verificationUrl, CancellationToken ct)
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
            From = new MailAddress(smtpAccount.Trim()),
            Subject = "Verificación cuenta Alfa Net",
            Body = BuildVerificationEmailHtml(request.Nombre, verificationUrl),
            IsBodyHtml = true
        };

        message.To.Add(request.Email.Trim());

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
    }

    private string ResolveServerConfig(string primaryKey, string fallbackKey)
        => configuration[primaryKey]?.Trim()
           ?? configuration[fallbackKey]?.Trim()
           ?? string.Empty;

    private static string BuildVerificationEmailHtml(string nombre, string verificationUrl)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="es">
            <head>
              <meta charset="utf-8" />
              <title>Verificación cuenta Alfa Net</title>
            </head>
            <body style="font-family:Segoe UI,Arial,sans-serif;background:#f4f7fb;padding:24px;color:#142133;">
              <div style="max-width:720px;margin:0 auto;background:#ffffff;border:1px solid #d9e3ef;border-radius:18px;overflow:hidden;">
                <div style="padding:24px 28px;background:#0f2138;color:#ffffff;">
                  <h1 style="margin:0;font-size:28px;">Alfa Net Web</h1>
                  <p style="margin:8px 0 0;font-size:15px;opacity:.92;">Confirmación de cuenta</p>
                </div>
                <div style="padding:28px;">
            """);
        sb.Append($"<p style=\"margin:0 0 16px;\">Hola {E(nombre)}.</p>");
        sb.Append("<p style=\"margin:0 0 16px;\">Gracias por registrarte. Solo falta confirmar tu cuenta para preparar tu primer acceso.</p>");
        sb.Append($"<p style=\"margin:24px 0;\"><a href=\"{E(verificationUrl)}\" style=\"display:inline-block;padding:12px 20px;background:#0b74c9;color:#ffffff;text-decoration:none;border-radius:10px;font-weight:600;\">Confirmar cuenta</a></p>");
        sb.Append("<p style=\"margin:0 0 8px;\">Si el botón no funciona, copiá este enlace en tu navegador:</p>");
        sb.Append($"<p style=\"word-break:break-all;margin:0;color:#0b74c9;\">{E(verificationUrl)}</p>");
        sb.Append("""
                </div>
              </div>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    private static string BuildVerificationUrl(string publicBaseUrl, string code)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? throw new InvalidOperationException("No se pudo construir la URL pública de verificación.")
            : publicBaseUrl.Trim().TrimEnd('/');

        return $"{baseUrl}/verify/{Uri.EscapeDataString(code)}";
    }

    private async Task<PendingRegistrationRow?> LoadPendingRegistrationAsync(SqlConnection central, string code, CancellationToken ct)
    {
        var columns = await GetColumnNamesAsync(central, null, "Clientes", ct);
        if (!columns.Contains("verified_code"))
            throw new InvalidOperationException("La base central no tiene la columna verified_code para validar la cuenta.");

        var sql = $"""
            SELECT TOP (1)
                c.idcliente AS IdCliente,
                ISNULL(u.[user], '') AS Email,
                ISNULL(u.password, '') AS Password
            FROM dbo.Clientes c
            LEFT JOIN dbo.users u ON u.idcliente = c.idcliente
            WHERE UPPER(LTRIM(RTRIM(c.verified_code))) = UPPER(LTRIM(RTRIM(@Code)))
              {(columns.Contains("verified") ? "AND ISNULL(c.verified, 0) = 0" : string.Empty)};
            """;

        return await central.QuerySingleOrDefaultAsync<PendingRegistrationRow>(new CommandDefinition(sql, new { Code = code }, cancellationToken: ct));
    }

    private static async Task MarkVerifiedAsync(SqlConnection central, string idCliente, string code, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.Clientes
            SET verified = 1
            WHERE UPPER(LTRIM(RTRIM(idcliente))) = UPPER(LTRIM(RTRIM(@IdCliente)))
              AND UPPER(LTRIM(RTRIM(verified_code))) = UPPER(LTRIM(RTRIM(@Code)));
            """;

        await central.ExecuteAsync(new CommandDefinition(sql, new { IdCliente = idCliente, Code = code }, cancellationToken: ct));
    }

    private async Task<OfficialCustomerRow> LoadOfficialCustomerAsync(string idCliente, CancellationToken ct)
    {
        await using var cn = new SqlConnection(AlfaGestionConnectionString);
        await cn.OpenAsync(ct);

        const string sql = """
            SELECT TOP (1)
                ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(250), RAZON_SOCIAL))), '') AS RazonSocial,
                ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(50), MAIL))), '') AS Email,
                ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(50), TELEFONO))), '') AS Telefono,
                ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(50), NUMERO_DOCUMENTO))), '') AS Cuit,
                ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(10), IVA))), '') AS Iva
            FROM dbo.VT_CLIENTES
            WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), CODIGO)))) = UPPER(LTRIM(RTRIM(@IdCliente)));
            """;

        var row = await cn.QuerySingleOrDefaultAsync<OfficialCustomerRow>(new CommandDefinition(sql, new { IdCliente = idCliente }, cancellationToken: ct));
        if (row is null)
            throw new InvalidOperationException("La cuenta fue verificada, pero no se pudo recuperar el cliente oficial para aprovisionar la base.");

        return row;
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(SqlConnection cn, SqlTransaction? tx, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT LOWER(name)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@ObjectName);
            """;

        var rows = await cn.QueryAsync<string>(new CommandDefinition(sql, new { ObjectName = $"dbo.{tableName}" }, tx, cancellationToken: ct));
        return rows.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static object DbNullable(string value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private sealed class PendingRegistrationRow
    {
        public string IdCliente { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    private sealed class OfficialCustomerRow
    {
        public string RazonSocial { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Cuit { get; set; } = string.Empty;
        public string Iva { get; set; } = string.Empty;
    }
}

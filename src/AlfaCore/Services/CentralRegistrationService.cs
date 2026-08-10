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

    /// <summary>
    /// Autoservicio: arranca la prueba gratuita de 30 días para los módulos elegidos justo
    /// después de confirmar el email (ver Verify.razor). Se identifica al cliente por el mismo
    /// código de verificación (no por un IdCliente que llegue del navegador) para no exponer un
    /// endpoint que active módulos de cualquier cliente sin probar nada.
    /// </summary>
    Task<PublicTrialResult> IniciarPruebaModulosAsync(string code, IReadOnlyCollection<int> idsModulos, CancellationToken ct = default);
}

public sealed class CentralRegistrationService(
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    IRecaptchaValidationService recaptchaValidationService,
    ICentralProvisioningService provisioningService,
    ICentralAdminService centralAdminService,
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

            // sp_web_altaClienteAlfa (el alta oficial en ALFANET2007, con el CUIT) NO se llama
            // acá — se pospone hasta confirmar el email (ver VerifyAsync). Mientras tanto todo
            // vive en dbo.RegistroPublicoPendiente, una tabla aparte de dbo.Clientes/dbo.users:
            // así un registro que nunca confirma (bot, typo, se arrepiente) no deja basura
            // permanente ni ocupa un CUIT en el registro oficial de clientes.
            if (await CuitYaRegistradoAsync(normalized.Cuit, ct))
            {
                return new PublicRegistrationResult
                {
                    Success = false,
                    Message = "Ya existe una cuenta registrada con ese CUIT. Iniciá sesión con la cuenta existente, o contactá a soporte si necesitás agregar otro usuario."
                };
            }

            await using var central = new SqlConnection(CentralConnectionString);
            await central.OpenAsync(ct);

            if (await ExistsConfirmedUserByEmailAsync(central, normalized.Email, ct))
            {
                return new PublicRegistrationResult
                {
                    Success = false,
                    Message = "El email ya se encuentra registrado."
                };
            }

            var pendingExistente = await LoadPendingByEmailAsync(central, normalized.Email, ct);
            var verificationCode = pendingExistente is not null
                ? pendingExistente.VerifiedCode // mismo código: si ya reservó un idCliente en un intento anterior, VerifyAsync lo reusa.
                : Guid.NewGuid().ToString("N");

            await UpsertPendingAsync(central, normalized, verificationCode, ct);

            var verificationUrl = BuildVerificationUrl(normalized.PublicBaseUrl, verificationCode);
            await SendVerificationEmailAsync(normalized, verificationUrl, ct);

            await appEvents.LogAuditAsync(
                ModuleName,
                pendingExistente is not null ? "RegisterRetry" : "Register",
                "ALFA_CENTRAL.RegistroPublicoPendiente",
                normalized.Email,
                pendingExistente is not null
                    ? "Registro público reenviado (todavía sin confirmar)."
                    : "Registro público pendiente de confirmación creado.",
                new { normalized.Nombre, normalized.Email },
                ct);

            return new PublicRegistrationResult
            {
                Success = true,
                Message = pendingExistente is not null
                    ? "Ya habías empezado este registro. Te reenviamos el correo para confirmarlo."
                    : "Registro exitoso. Revisá tu correo para confirmar la cuenta.",
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

            var pending = await LoadPendingByCodeAsync(central, code.Trim(), ct);
            if (pending is null)
            {
                // La fila pendiente se borra apenas se confirma — pero el link de confirmación
                // suele visitarse dos veces sin que el usuario haga nada (webmails/filtros de
                // seguridad que pre-visitan links de un email para escanearlos antes de que el
                // usuario lo abra). Si ese primer hit automático ya confirmó la cuenta, acá no
                // hay que devolver error: dbo.Clientes.verified_code sigue guardando el código
                // después de confirmar, así que se puede resolver igual y devolver éxito de
                // nuevo (entre otras cosas, para que Verify.razor pueda mostrar el selector de
                // módulos a la visita real del usuario, no solo a la del bot).
                var yaConfirmado = await LoadConfirmedByCodeAsync(central, code.Trim(), ct);
                if (yaConfirmado is not null)
                {
                    // El primer hit (bot) ya activó el módulo de la landing si correspondía —
                    // se reintenta acá (es idempotente, ver ActivarConEstadoAsync) solo para poder
                    // devolver el nombre y que Verify.razor salte el selector también en esta visita.
                    string? moduloPreactivadoConfirmado = null;
                    if (!string.IsNullOrWhiteSpace(yaConfirmado.ModuloSlugLanding))
                        moduloPreactivadoConfirmado = await TryActivarModuloDeLandingAsync(yaConfirmado.IdCliente, yaConfirmado.ModuloSlugLanding, ct);

                    return new PublicVerificationResult
                    {
                        Success = true,
                        AccountVerified = true,
                        ProvisioningCompleted = true,
                        Message = "La cuenta ya había sido confirmada y la base ya está preparada.",
                        ModuloPreactivado = moduloPreactivadoConfirmado
                    };
                }

                return new PublicVerificationResult
                {
                    Success = false,
                    Message = "El código de verificación no existe o ya fue usado."
                };
            }

            // El alta oficial (CUIT incluido) recién se crea acá, al confirmar — no en
            // RegisterAsync. Si un intento anterior ya reservó un idCliente pero el
            // aprovisionamiento falló después, se reusa el mismo en vez de pedir uno nuevo (evita
            // acumular altas oficiales huérfanas en ALFANET2007 en cada reintento del mismo link).
            var idCliente = pending.IdClienteReservado;
            if (string.IsNullOrWhiteSpace(idCliente))
            {
                idCliente = await CreateOfficialCustomerAsync(new PublicRegistrationRequest
                {
                    Nombre = pending.Nombre,
                    Telefono = pending.Telefono,
                    Email = pending.Email,
                    Cuit = pending.Cuit,
                    Iva = pending.Iva
                }, ct);
                await SaveIdClienteReservadoAsync(central, pending.VerifiedCode, idCliente, ct);
            }

            var official = await LoadOfficialCustomerAsync(idCliente, ct);

            var provisioning = await provisioningService.ProvisionAsync(new PublicProvisioningRequest
            {
                IdCliente = idCliente,
                RazonSocial = official.RazonSocial,
                Telefono = official.Telefono,
                Email = pending.Email,
                Cuit = official.Cuit,
                Iva = official.Iva,
                UserName = pending.Email,
                Password = pending.Password
            }, ct);

            if (provisioning.Success)
            {
                await using var tx = (SqlTransaction)await central.BeginTransactionAsync(ct);
                try
                {
                    await InsertCentralClientAsync(central, tx, idCliente, pending.Nombre, pending.Password, code.Trim(), pending.ModuloSlug, ct);
                    await InsertCentralUserAsync(central, tx, idCliente, pending.Email, pending.Password, pending.Nombre, ct);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }

                await DeletePendingAsync(central, pending.VerifiedCode, ct);
            }

            string? moduloPreactivado = null;
            if (provisioning.Success && !string.IsNullOrWhiteSpace(pending.ModuloSlug))
                moduloPreactivado = await TryActivarModuloDeLandingAsync(idCliente, pending.ModuloSlug, ct);

            return new PublicVerificationResult
            {
                Success = provisioning.Success,
                AccountVerified = provisioning.Success,
                ProvisioningCompleted = provisioning.Success,
                Message = provisioning.Success
                    ? "La cuenta fue confirmada y la base quedó preparada correctamente."
                    : provisioning.Message,
                DatabaseName = provisioning.DatabaseName,
                ModuloPreactivado = moduloPreactivado
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

    public async Task<PublicTrialResult> IniciarPruebaModulosAsync(string code, IReadOnlyCollection<int> idsModulos, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new PublicTrialResult { Success = false, Message = "El código de verificación es inválido." };

        if (idsModulos.Count == 0)
            return new PublicTrialResult { Success = true, Message = "No se eligió ningún módulo para probar." };

        try
        {
            await using var central = new SqlConnection(CentralConnectionString);
            await central.OpenAsync(ct);

            var pending = await LoadRegistrationByCodeAsync(central, code.Trim(), ct);
            if (pending is null || !pending.Verified)
            {
                return new PublicTrialResult
                {
                    Success = false,
                    Message = "No se pudo identificar la cuenta para activar la prueba. Confirmá primero tu cuenta desde el link del email."
                };
            }

            await centralAdminService.IniciarPruebaModulosAsync(new IniciarPruebaModulosRequest
            {
                IdCliente = pending.IdCliente,
                IdsModulos = idsModulos.ToList()
            }, ct);

            return new PublicTrialResult { Success = true, Message = "Prueba gratuita activada por 30 días." };
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                ModuleName,
                "IniciarPruebaModulos",
                ex,
                "No se pudo activar la prueba gratuita de módulos.",
                new { Code = code.Trim(), IdsModulos = idsModulos },
                AppEventSeverity.Error,
                ct);

            return new PublicTrialResult
            {
                Success = false,
                Message = $"No se pudo activar la prueba gratuita. Podés seguir usando el sistema y activarla más tarde desde soporte. (Incidente: {incidentId})"
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
            PublicBaseUrl = request.PublicBaseUrl?.Trim() ?? string.Empty,
            ModuloSlug = string.IsNullOrWhiteSpace(request.ModuloSlug) ? null : request.ModuloSlug.Trim()
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

    /// <summary>
    /// Compara solo dígitos (CUIT puede llegar con o sin guiones) contra NUMERO_DOCUMENTO en el
    /// registro oficial de clientes — la misma tabla que llena sp_web_altaClienteAlfa al dar de
    /// alta una cuenta nueva.
    /// </summary>
    private async Task<bool> CuitYaRegistradoAsync(string cuit, CancellationToken ct)
    {
        var soloDigitos = new string(cuit.Where(char.IsDigit).ToArray());
        if (soloDigitos.Length == 0)
            return false;

        const string sql = """
            SELECT COUNT(1)
            FROM dbo.MA_CUENTASADIC
            WHERE REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(NUMERO_DOCUMENTO, ''))), '-', ''), '.', '') = @Cuit;
            """;

        await using var cn = new SqlConnection(AlfaGestionConnectionString);
        await cn.OpenAsync(ct);
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Cuit = soloDigitos }, cancellationToken: ct));
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
        // El SP espera '' (no NULL) para disparar su propio default de IVA ("IF @pIva = ''
        // SET @pIva = '   1'") cuando no se cargó condición de IVA — pasar DBNull.Value ahí hace
        // que esa comparación nunca dispare y la columna quede NULL en vez de con el default.
        cmd.Parameters.AddWithValue("@pIva", request.Iva ?? string.Empty);
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
        string nombre,
        string password,
        string verificationCode,
        string? moduloSlug,
        CancellationToken ct)
    {
        var columns = await GetColumnNamesAsync(central, tx, "Clientes", ct);
        var names = new List<string> { "idcliente", "nombre", "superadmin" };
        var values = new List<string> { "@IdCliente", "@Nombre", "@SuperAdmin" };

        // Persiste el módulo elegido en la landing más allá de que dbo.RegistroPublicoPendiente
        // se borre al confirmar — lo necesita el camino de "cuenta ya confirmada" de VerifyAsync
        // cuando el link se pre-visita (webmails/scanners) antes que el usuario real haga clic.
        if (columns.Contains("modulosluglanding") && !string.IsNullOrWhiteSpace(moduloSlug))
        {
            names.Add("ModuloSlugLanding");
            values.Add("@ModuloSlugLanding");
        }

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
                Nombre = nombre,
                SuperAdmin = 0,
                IdWeb = string.Empty,
                Password = password,
                Verified = 1,
                Type = AccountType,
                VerifiedCode = verificationCode,
                Created = DateTime.Now,
                ModuloSlugLanding = moduloSlug
            },
            tx,
            cancellationToken: ct));
    }

    private static async Task InsertCentralUserAsync(
        SqlConnection central,
        SqlTransaction tx,
        string idCliente,
        string email,
        string password,
        string nombre,
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
                UserName = email,
                Password = password,
                IdCliente = idCliente,
                IsAdmin = 1,
                Name = nombre
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

    private async Task<RegistrationRow?> LoadRegistrationByCodeAsync(SqlConnection central, string code, CancellationToken ct)
    {
        var columns = await GetColumnNamesAsync(central, null, "Clientes", ct);
        if (!columns.Contains("verified_code"))
            throw new InvalidOperationException("La base central no tiene la columna verified_code para validar la cuenta.");

        // verified_code vive en dbo.Clientes (uno por idcliente), pero dbo.users puede tener MÁS
        // de una fila para el mismo idcliente si el mismo CUIT se registró de nuevo con otro email
        // (sp_web_altaClienteAlfa reutiliza el idCliente). Sin ORDER BY, el TOP(1) del join podía
        // devolver cualquiera de esos emails — casi siempre el más viejo, no el dueño real del
        // código que se está verificando ahora. Como verified_code se pisa con cada registro
        // nuevo, el registro más reciente (id más alto en dbo.users) es el que corresponde a ESTE
        // código.
        var sql = $"""
            SELECT TOP (1)
                c.idcliente AS IdCliente,
                ISNULL(u.[user], '') AS Email,
                ISNULL(u.password, '') AS Password,
                {(columns.Contains("verified") ? "ISNULL(c.verified, 0)" : "CAST(0 AS bit)")} AS Verified
            FROM dbo.Clientes c
            LEFT JOIN dbo.users u ON u.idcliente = c.idcliente
            WHERE UPPER(LTRIM(RTRIM(c.verified_code))) = UPPER(LTRIM(RTRIM(@Code)))
            ORDER BY u.id DESC
            ;
            """;

        return await central.QuerySingleOrDefaultAsync<RegistrationRow>(new CommandDefinition(sql, new { Code = code }, cancellationToken: ct));
    }

    private static async Task<bool> ExistsConfirmedUserByEmailAsync(SqlConnection central, string email, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(1) FROM dbo.users WHERE UPPER(LTRIM(RTRIM([user]))) = UPPER(LTRIM(RTRIM(@Email)));";
        var count = await central.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Email = email }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<PendingRegistrationRow?> LoadPendingByEmailAsync(SqlConnection central, string email, CancellationToken ct)
    {
        const string sql = """
            SELECT VerifiedCode, Nombre, Telefono, Email, Password, ISNULL(Cuit, '') AS Cuit, ISNULL(Iva, '') AS Iva, IdClienteReservado, ModuloSlug
            FROM dbo.RegistroPublicoPendiente
            WHERE UPPER(LTRIM(RTRIM(Email))) = UPPER(LTRIM(RTRIM(@Email)));
            """;

        return await central.QuerySingleOrDefaultAsync<PendingRegistrationRow>(new CommandDefinition(sql, new { Email = email }, cancellationToken: ct));
    }

    private static async Task<PendingRegistrationRow?> LoadPendingByCodeAsync(SqlConnection central, string code, CancellationToken ct)
    {
        const string sql = """
            SELECT VerifiedCode, Nombre, Telefono, Email, Password, ISNULL(Cuit, '') AS Cuit, ISNULL(Iva, '') AS Iva, IdClienteReservado, ModuloSlug
            FROM dbo.RegistroPublicoPendiente
            WHERE UPPER(LTRIM(RTRIM(VerifiedCode))) = UPPER(LTRIM(RTRIM(@Code)));
            """;

        return await central.QuerySingleOrDefaultAsync<PendingRegistrationRow>(new CommandDefinition(sql, new { Code = code }, cancellationToken: ct));
    }

    /// <summary>
    /// Activa la prueba de 30 días del módulo que el visitante eligió en /landing/{slug}, sin
    /// pasarlo por el selector manual de Verify.razor. No es fatal si falla (el módulo no existe
    /// más, AlfaKnowledge no pudo aprovisionar, etc.) — la cuenta ya quedó creada igual; devuelve
    /// null y Verify.razor cae al selector manual como si no hubiera venido de una landing.
    /// </summary>
    private async Task<string?> TryActivarModuloDeLandingAsync(string idCliente, string moduloSlug, CancellationToken ct)
    {
        try
        {
            var contenido = LandingContenidoCatalogo.Todos.FirstOrDefault(m => string.Equals(m.Slug, moduloSlug, StringComparison.OrdinalIgnoreCase));
            if (contenido is null)
                return null;

            var modulos = await centralAdminService.GetModulosAsync(ct);
            var modulo = modulos.FirstOrDefault(m => string.Equals(m.Codigo, contenido.Codigo, StringComparison.OrdinalIgnoreCase) && m.Activo);
            if (modulo is null)
                return null;

            await centralAdminService.IniciarPruebaModulosAsync(new IniciarPruebaModulosRequest
            {
                IdCliente = idCliente,
                IdsModulos = [modulo.Id]
            }, ct);

            return contenido.Nombre;
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                ModuleName,
                "ActivarModuloDeLanding",
                ex,
                "No se pudo activar automáticamente el módulo elegido en la landing.",
                new { idCliente, moduloSlug },
                AppEventSeverity.Warning,
                ct);
            return null;
        }
    }

    /// <summary>Cuenta ya confirmada cuyo verified_code coincide — ver comentario en VerifyAsync.</summary>
    private static async Task<ConfirmedClientRow?> LoadConfirmedByCodeAsync(SqlConnection central, string code, CancellationToken ct)
    {
        var columns = await GetColumnNamesAsync(central, null, "Clientes", ct);
        var moduloSlugSelect = columns.Contains("modulosluglanding") ? "ModuloSlugLanding" : "CAST(NULL AS nvarchar(50)) AS ModuloSlugLanding";

        var sql = $"""
            SELECT TOP (1) idcliente AS IdCliente, {moduloSlugSelect}
            FROM dbo.Clientes
            WHERE UPPER(LTRIM(RTRIM(verified_code))) = UPPER(LTRIM(RTRIM(@Code)));
            """;

        return await central.QuerySingleOrDefaultAsync<ConfirmedClientRow>(new CommandDefinition(sql, new { Code = code }, cancellationToken: ct));
    }

    /// <summary>
    /// Un registro por email: si ya había uno pendiente (mismo <paramref name="verificationCode"/>
    /// que <see cref="LoadPendingByEmailAsync"/> encontró), lo actualiza con los datos nuevos; si
    /// no, inserta uno. El <c>UNIQUE (Email)</c> de la tabla es la garantía real contra duplicados.
    /// </summary>
    private static async Task UpsertPendingAsync(SqlConnection central, PublicRegistrationRequest request, string verificationCode, CancellationToken ct)
    {
        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.RegistroPublicoPendiente WHERE UPPER(LTRIM(RTRIM(Email))) = UPPER(LTRIM(RTRIM(@Email))))
                UPDATE dbo.RegistroPublicoPendiente
                SET Nombre = @Nombre, Telefono = @Telefono, Password = @Password, Cuit = @Cuit, Iva = @Iva, ModuloSlug = @ModuloSlug, CreatedUtc = GETUTCDATE()
                WHERE UPPER(LTRIM(RTRIM(Email))) = UPPER(LTRIM(RTRIM(@Email)));
            ELSE
                INSERT INTO dbo.RegistroPublicoPendiente (VerifiedCode, Nombre, Telefono, Email, Password, Cuit, Iva, ModuloSlug)
                VALUES (@VerifiedCode, @Nombre, @Telefono, @Email, @Password, @Cuit, @Iva, @ModuloSlug);
            """;

        await central.ExecuteAsync(new CommandDefinition(sql, new
        {
            VerifiedCode = verificationCode,
            request.Nombre,
            request.Telefono,
            request.Email,
            request.Password,
            Cuit = string.IsNullOrWhiteSpace(request.Cuit) ? null : request.Cuit,
            Iva = string.IsNullOrWhiteSpace(request.Iva) ? null : request.Iva,
            ModuloSlug = string.IsNullOrWhiteSpace(request.ModuloSlug) ? null : request.ModuloSlug
        }, cancellationToken: ct));
    }

    private static async Task SaveIdClienteReservadoAsync(SqlConnection central, string verificationCode, string idCliente, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.RegistroPublicoPendiente
            SET IdClienteReservado = @IdCliente
            WHERE UPPER(LTRIM(RTRIM(VerifiedCode))) = UPPER(LTRIM(RTRIM(@Code)));
            """;

        await central.ExecuteAsync(new CommandDefinition(sql, new { IdCliente = idCliente, Code = verificationCode }, cancellationToken: ct));
    }

    private static async Task DeletePendingAsync(SqlConnection central, string verificationCode, CancellationToken ct)
    {
        const string sql = "DELETE FROM dbo.RegistroPublicoPendiente WHERE UPPER(LTRIM(RTRIM(VerifiedCode))) = UPPER(LTRIM(RTRIM(@Code)));";
        await central.ExecuteAsync(new CommandDefinition(sql, new { Code = verificationCode }, cancellationToken: ct));
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

    private sealed class RegistrationRow
    {
        public string IdCliente { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool Verified { get; set; }
        public string VerifiedCode { get; set; } = string.Empty;
    }

    private sealed class OfficialCustomerRow
    {
        public string RazonSocial { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Cuit { get; set; } = string.Empty;
        public string Iva { get; set; } = string.Empty;
    }

    private sealed class ConfirmedClientRow
    {
        public string IdCliente { get; set; } = string.Empty;
        public string? ModuloSlugLanding { get; set; }
    }

    private sealed class PendingRegistrationRow
    {
        public string VerifiedCode { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Cuit { get; set; } = string.Empty;
        public string Iva { get; set; } = string.Empty;
        public string? IdClienteReservado { get; set; }
        public string? ModuloSlug { get; set; }
    }
}

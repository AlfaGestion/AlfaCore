using System.Security.Cryptography;
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
    IAppEventService appEvents,
    IPedidosEmailService pedidosEmailSvc) : IPortalClienteRecuperarClaveService
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

            var enviado = await pedidosEmailSvc.EnviarRecuperacionClaveAsync(
                cliente.Email, cliente.RazonSocial, request.NombreEmpresa ?? string.Empty, request.LogoUrlAbsoluta, urlRestablecer, ct);

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

            if (!await SqlObjectExistsAsync(cn, "ALFACORE_CLIENTE_RESET_TOKEN", ct))
                return new PortalClienteValidarTokenResultDto { Valido = false, Mensaje = MensajeTokenInvalido };

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

            if (!await SqlObjectExistsAsync(cn, "ALFACORE_CLIENTE_RESET_TOKEN", ct) || !await SqlObjectExistsAsync(cn, "MA_CUENTASADIC", ct))
                return new PortalClienteRestablecerClaveResultDto { Exito = false, Mensaje = MensajeTokenInvalido };

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

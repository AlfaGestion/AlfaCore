using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PortalClienteRegistrarEmailService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IPortalClienteRegistrarEmailService
{
    private const string ModuleName = "PortalClienteRegistrarEmail";
    private const int ExpiracionMinutos = 60;
    private const int ClaveLongitudMinima = 4;
    private const int ClaveLongitudMaxima = 15;

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<string> GenerarEnlaceAsync(PortalClienteGenerarEnlaceEmailRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var codigo = (request.CodigoCliente ?? string.Empty).Trim();
        var idWeb = (request.IdWeb ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(idWeb) || request.IdBase <= 0)
            throw new InvalidOperationException("No se pudo identificar el cliente y la base del Portal Cliente.");
        if (!Uri.TryCreate(request.UrlBaseRegistro, UriKind.Absolute, out var url)
            || !Uri.UnescapeDataString(url.AbsolutePath).Contains($"/{idWeb}/{request.IdBase}/portal-cliente/registrar-email", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El enlace para registrar el email está incompleto.");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await EnsureTableAsync(cn, ct);

        var email = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) ISNULL(MAIL, '') FROM dbo.MA_CUENTASADIC WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));",
            new { Codigo = codigo }, cancellationToken: ct));
        if (!string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("El cliente ya tiene un email registrado.");

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ALFACORE_CLIENTE_EMAIL_TOKEN SET Usado = 1, FechaHora_Uso = GETDATE()
            WHERE UPPER(LTRIM(RTRIM(CodigoCliente))) = UPPER(LTRIM(RTRIM(@Codigo))) AND Usado = 0;
            INSERT INTO dbo.ALFACORE_CLIENTE_EMAIL_TOKEN (CodigoCliente, IdWeb, IdBase, TokenHash, FechaHora_Expiracion)
            VALUES (@Codigo, @IdWeb, @IdBase, @Hash, DATEADD(MINUTE, @Expiracion, GETDATE()));
            """,
            new { Codigo = codigo, IdWeb = idWeb, IdBase = request.IdBase, Hash = hash, Expiracion = ExpiracionMinutos }, cancellationToken: ct));

        return $"{request.UrlBaseRegistro.TrimEnd('/')}?token={Uri.EscapeDataString(token)}";
    }

    public async Task<PortalClienteRegistrarEmailResultDto> RegistrarAsync(PortalClienteRegistrarEmailRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var token = (request.Token ?? string.Empty).Trim();
            var email = (request.Email ?? string.Empty).Trim();
            var nuevaClave = request.NuevaClave ?? string.Empty;
            var confirmarClave = request.ConfirmarClave ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
                return new() { Mensaje = "El enlace o el email no son válidos." };
            try { _ = new MailAddress(email); } catch (FormatException) { return new() { Mensaje = "Ingresá un email válido." }; }
            if (nuevaClave.Length < ClaveLongitudMinima || nuevaClave.Length > ClaveLongitudMaxima)
                return new() { Mensaje = $"La contraseña debe tener entre {ClaveLongitudMinima} y {ClaveLongitudMaxima} caracteres." };
            if (!string.Equals(nuevaClave, confirmarClave, StringComparison.Ordinal))
                return new() { Mensaje = "Las contraseñas no coinciden." };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await EnsureTableAsync(cn, ct);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
            var row = await cn.QuerySingleOrDefaultAsync<EmailTokenRow>(new CommandDefinition(
                "SELECT TOP (1) IdToken, CodigoCliente FROM dbo.ALFACORE_CLIENTE_EMAIL_TOKEN WHERE TokenHash=@Hash AND Usado=0 AND FechaHora_Expiracion>GETDATE();",
                new { Hash = hash }, cancellationToken: ct));
            if (row is null) return new() { Mensaje = "El enlace no es válido o ya venció." };

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var updated = await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.MA_CUENTASADIC SET MAIL=@Email, CLAVE=@Clave WHERE UPPER(LTRIM(RTRIM(CODIGO)))=UPPER(LTRIM(RTRIM(@Codigo))) AND ISNULL(LTRIM(RTRIM(MAIL)), '')='';",
                new { Email = email, Clave = nuevaClave, Codigo = row.CodigoCliente }, transaction: tx, cancellationToken: ct));
            if (updated == 0) { await tx.RollbackAsync(ct); return new() { Mensaje = "El cliente ya tiene un email o no está disponible." }; }
            await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.ALFACORE_CLIENTE_EMAIL_TOKEN SET Usado=1, FechaHora_Uso=GETDATE() WHERE IdToken=@IdToken;", new { row.IdToken }, transaction: tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            await appEvents.LogAuditAsync(ModuleName, "Registrar", "MA_CUENTASADIC", row.CodigoCliente, "El cliente registró su email desde un enlace público del Portal Cliente.", new { row.CodigoCliente }, ct);
            return new() { Exito = true, Mensaje = "Email y contraseña registrados correctamente. Ya podés ingresar al Portal Cliente." };
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "Registrar", ex, "No se pudo registrar el email del Portal Cliente.", null, AppEventSeverity.Warning, ct);
            return new() { Mensaje = "No pudimos registrar el email en este momento. Intentá nuevamente." };
        }
    }

    private static async Task EnsureTableAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteAsync(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_EMAIL_TOKEN', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ALFACORE_CLIENTE_EMAIL_TOKEN (
                    IdToken int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ALFACORE_CLIENTE_EMAIL_TOKEN PRIMARY KEY,
                    CodigoCliente nvarchar(30) NOT NULL, IdWeb nvarchar(100) NULL, IdBase int NULL,
                    TokenHash nvarchar(64) NOT NULL, FechaHora_Creacion datetime NOT NULL DEFAULT(GETDATE()),
                    FechaHora_Expiracion datetime NOT NULL, Usado bit NOT NULL DEFAULT(0), FechaHora_Uso datetime NULL
                );
                CREATE UNIQUE INDEX UX_ALFACORE_CLIENTE_EMAIL_TOKEN_HASH ON dbo.ALFACORE_CLIENTE_EMAIL_TOKEN(TokenHash);
                CREATE INDEX IX_ALFACORE_CLIENTE_EMAIL_TOKEN_CLIENTE ON dbo.ALFACORE_CLIENTE_EMAIL_TOKEN(CodigoCliente, Usado);
            END;
            """, cancellationToken: ct));

    private sealed class EmailTokenRow { public int IdToken { get; set; } public string CodigoCliente { get; set; } = string.Empty; }
}

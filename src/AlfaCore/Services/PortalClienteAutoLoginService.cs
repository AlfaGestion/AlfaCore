using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PortalClienteAutoLoginService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IPortalClienteAutoLoginService
{
    private const string ModuleName = "PortalClienteAutoLogin";
    private const int ExpiracionMinutos = 60;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TableLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> ReadyTables = new(StringComparer.Ordinal);

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<string> GenerarEnlaceAsync(PortalClienteGenerarAutoLoginRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var codigo = request.CodigoCliente.Trim();
        var idWeb = request.IdWeb.Trim();
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(idWeb) || request.IdBase <= 0)
            throw new InvalidOperationException("No se pudo identificar la cuenta del Portal Cliente.");
        if (!Uri.TryCreate(request.UrlBasePortal, UriKind.Absolute, out var portalUri))
            throw new InvalidOperationException("El enlace del Portal Cliente no es válido.");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await EnsureTableOnceAsync(cn, ct);

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await cn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN
                (CodigoCliente, RazonSocial, Email, IdWeb, IdBase, TokenHash, FechaHora_Expiracion)
            VALUES (@Codigo, @RazonSocial, @Email, @IdWeb, @IdBase, @TokenHash, DATEADD(MINUTE, @ExpiracionMinutos, GETDATE()));
            """,
            new
            {
                Codigo = codigo,
                RazonSocial = request.RazonSocial.Trim(),
                Email = request.Email.Trim(),
                IdWeb = idWeb,
                request.IdBase,
                TokenHash = HashToken(token),
                ExpiracionMinutos = ExpiracionMinutos
            }, cancellationToken: ct));

        return QueryHelpers.AddQueryString($"{portalUri.GetLeftPart(UriPartial.Path).TrimEnd('/')}/acceso", "token", token);
    }

    public async Task<PortalClienteAutoLoginResultDto> ConsumirAsync(string token, string idWeb, int idBase, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(idWeb) || idBase <= 0)
            return InvalidResult();

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            if (!await SqlObjectExistsAsync(cn, "ALFACORE_CLIENTE_AUTOLOGIN_TOKEN", ct))
                return InvalidResult();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var row = await cn.QuerySingleOrDefaultAsync<AutoLoginRow>(new CommandDefinition(
                """
                SELECT TOP (1) IdToken, CodigoCliente, RazonSocial, Email, IdWeb, IdBase
                FROM dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN
                WHERE TokenHash = @TokenHash
                  AND UPPER(LTRIM(RTRIM(IdWeb))) = UPPER(LTRIM(RTRIM(@IdWeb)))
                  AND IdBase = @IdBase
                  AND Usado = 0
                  AND FechaHora_Expiracion > GETDATE();
                """, new { TokenHash = HashToken(token), IdWeb = idWeb.Trim(), idBase }, transaction: tx, cancellationToken: ct));
            if (row is null)
            {
                await tx.RollbackAsync(ct);
                return InvalidResult();
            }

            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN SET Usado = 1, FechaHora_Uso = GETDATE() WHERE IdToken = @IdToken AND Usado = 0;",
                new { row.IdToken }, transaction: tx, cancellationToken: ct));
            await tx.CommitAsync(ct);

            return new PortalClienteAutoLoginResultDto
            {
                Exito = true,
                Sesion = new CatalogosClienteSessionInfo
                {
                    CodigoCliente = row.CodigoCliente,
                    RazonSocial = row.RazonSocial,
                    Email = row.Email,
                    IdWeb = row.IdWeb,
                    IdBase = row.IdBase
                }
            };
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, "Consumir", ex,
                "No se pudo validar el enlace de acceso automático del Portal Cliente.", null, AppEventSeverity.Error, ct);
            return new PortalClienteAutoLoginResultDto { Mensaje = $"No se pudo validar el enlace. Incidente: {incidentId}" };
        }
    }

    private static PortalClienteAutoLoginResultDto InvalidResult()
        => new() { Mensaje = "El enlace no es válido, ya fue utilizado o venció. Solicitá uno nuevo." };

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static async Task<bool> SqlObjectExistsAsync(SqlConnection cn, string objectName, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM sys.objects WHERE object_id = OBJECT_ID(@ObjectName) AND type = 'U';",
            new { ObjectName = $"dbo.{objectName}" }, cancellationToken: ct)) > 0;

    private async Task EnsureTableOnceAsync(SqlConnection cn, CancellationToken ct)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cn.ConnectionString)));
        if (ReadyTables.ContainsKey(key))
            return;

        var gate = TableLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (ReadyTables.ContainsKey(key))
                return;

            await EnsureTableAsync(cn, ct);
            ReadyTables[key] = 0;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task EnsureTableAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN
                (
                    IdToken int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN PRIMARY KEY,
                    CodigoCliente nvarchar(30) NOT NULL,
                    RazonSocial nvarchar(250) NOT NULL,
                    Email nvarchar(320) NOT NULL,
                    IdWeb nvarchar(100) NOT NULL,
                    IdBase int NOT NULL,
                    TokenHash nvarchar(64) NOT NULL,
                    FechaHora_Creacion datetime NOT NULL CONSTRAINT DF_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN_Creacion DEFAULT (GETDATE()),
                    FechaHora_Expiracion datetime NOT NULL,
                    Usado bit NOT NULL CONSTRAINT DF_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN_Usado DEFAULT (0),
                    FechaHora_Uso datetime NULL
                );
            END;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN_HASH' AND object_id = OBJECT_ID(N'dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN'))
                CREATE UNIQUE NONCLUSTERED INDEX UX_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN_HASH ON dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN(TokenHash);
            """;
        await cn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    private sealed class AutoLoginRow
    {
        public int IdToken { get; init; }
        public string CodigoCliente { get; init; } = string.Empty;
        public string RazonSocial { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string IdWeb { get; init; } = string.Empty;
        public int IdBase { get; init; }
    }
}

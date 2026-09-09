using AlfaCore.Configuration;
using AlfaCore.Models;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace AlfaCore.Services;

public sealed class WhatsAppSecureVault : IWhatsAppCredentialVault, IWhatsAppPhonePinVault
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment? _environment;
    private readonly WhatsAppEmbeddedSignupOptions _options;
    private string? _connectionString;
    private IDataProtectionProvider? _dataProtection;
    private IDataProtector? _credentialProtector;
    private IDataProtector? _pinProtector;

    public WhatsAppSecureVault(IConfiguration configuration, IOptions<WhatsAppEmbeddedSignupOptions> options, IHostEnvironment? environment = null)
    {
        _configuration = configuration;
        _environment = environment;
        _options = options.Value;
    }

    async Task<WhatsAppCredentialReference> IWhatsAppCredentialVault.StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct)
        => new(await StoreInternalAsync("CREDENTIAL", context, secret, GetCredentialProtector(), ct));

    async Task<WhatsAppCredentialReference?> IWhatsAppCredentialVault.FindActiveCredentialAsync(int idBase, string wabaId, string phoneNumberId, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) SecretReference
            FROM dbo.WhatsAppSecureVault
            WHERE SecretType = N'CREDENTIAL'
              AND IdBase = @IdBase AND WabaId = @WabaId AND PhoneNumberId = @PhoneNumberId
              AND RevokedAtUtc IS NULL
              AND (ExpiresAtUtc IS NULL OR ExpiresAtUtc > SYSUTCDATETIME())
            ORDER BY ModifiedAtUtc DESC, CreatedAtUtc DESC;
            """;
        await using var cn = new SqlConnection(ConnectionString);
        var value = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(sql, new
        {
            IdBase = idBase,
            WabaId = (wabaId ?? string.Empty).Trim(),
            PhoneNumberId = (phoneNumberId ?? string.Empty).Trim()
        }, cancellationToken: ct));
        return string.IsNullOrWhiteSpace(value) ? null : new WhatsAppCredentialReference(value);
    }

    async Task<ReadOnlyMemory<char>> IWhatsAppCredentialVault.GetAsync(WhatsAppCredentialReference reference, CancellationToken ct)
        => (await GetInternalAsync(reference.Value, "CREDENTIAL", GetCredentialProtector(), ct)).AsMemory();

    async Task<WhatsAppVaultSecretContext?> IWhatsAppCredentialVault.GetContextAsync(WhatsAppCredentialReference reference, CancellationToken ct)
    {
        const string sql = """
            SELECT IdBase,IdOnboarding,MetaBusinessId,WabaId,PhoneNumberId,Purpose,ExpiresAtUtc
            FROM dbo.WhatsAppSecureVault
            WHERE SecretReference=@Reference AND SecretType='CREDENTIAL' AND RevokedAtUtc IS NULL;
            """;
        await using var cn = new SqlConnection(ConnectionString);
        return await cn.QuerySingleOrDefaultAsync<WhatsAppVaultSecretContext>(new CommandDefinition(sql, new { Reference = NormalizeReference(reference.Value) }, cancellationToken: ct));
    }

    Task IWhatsAppCredentialVault.RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct)
        => RemoveInternalAsync(reference.Value, "CREDENTIAL", ct);

    async Task<WhatsAppPhonePinReference> IWhatsAppPhonePinVault.GetOrCreateAsync(WhatsAppVaultSecretContext context, CancellationToken ct)
    {
        const string selectSql = """
            SELECT TOP (1) SecretReference
            FROM dbo.WhatsAppSecureVault WITH (UPDLOCK, HOLDLOCK)
            WHERE SecretType='PHONE_PIN' AND IdBase=@IdBase AND IdOnboarding=@IdOnboarding
              AND PhoneNumberId=@PhoneNumberId AND RevokedAtUtc IS NULL
              AND (ExpiresAtUtc IS NULL OR ExpiresAtUtc > SYSUTCDATETIME())
            ORDER BY CreatedAtUtc DESC;
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var existing = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(selectSql, new
        {
            context.IdBase,
            context.IdOnboarding,
            PhoneNumberId = context.PhoneNumberId.Trim()
        }, transaction: tx, cancellationToken: ct));
        if (!string.IsNullOrWhiteSpace(existing))
        {
            await tx.CommitAsync(ct);
            return new WhatsAppPhonePinReference(existing);
        }

        var pin = new char[6];
        var value = RandomNumberGenerator.GetInt32(1_000_000);
        value.TryFormat(pin.AsSpan(), out _, "D6");
        var reference = Guid.NewGuid().ToString("N");
        var protectedValue = GetPinProtector().Protect(new string(pin));
        Array.Clear(pin);
        const string insertSql = """
            INSERT dbo.WhatsAppSecureVault
            (SecretReference,SecretType,IdBase,IdOnboarding,MetaBusinessId,WabaId,PhoneNumberId,Purpose,ProtectedValue,ExpiresAtUtc,CreatedAtUtc,ModifiedAtUtc)
            VALUES
            (@Reference,'PHONE_PIN',@IdBase,@IdOnboarding,@MetaBusinessId,@WabaId,@PhoneNumberId,@Purpose,@ProtectedValue,@ExpiresAtUtc,SYSUTCDATETIME(),SYSUTCDATETIME());
            """;
        await cn.ExecuteAsync(new CommandDefinition(insertSql, new
        {
            Reference = reference,
            context.IdBase,
            context.IdOnboarding,
            MetaBusinessId = context.MetaBusinessId.Trim(),
            WabaId = context.WabaId.Trim(),
            PhoneNumberId = context.PhoneNumberId.Trim(),
            Purpose = context.Purpose.Trim(),
            ProtectedValue = protectedValue,
            context.ExpiresAtUtc
        }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new WhatsAppPhonePinReference(reference);
    }

    async Task<WhatsAppPhonePinReference> IWhatsAppPhonePinVault.StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> pin, CancellationToken ct)
    {
        if (pin.Length != 6 || pin.Span.ToArray().Any(static value => value is < '0' or > '9'))
            throw new ArgumentException("El PIN debe contener exactamente seis dígitos.", nameof(pin));
        return new(await StoreInternalAsync("PHONE_PIN", context, pin, GetPinProtector(), ct));
    }

    async Task<ReadOnlyMemory<char>> IWhatsAppPhonePinVault.GetAsync(WhatsAppPhonePinReference reference, CancellationToken ct)
        => (await GetInternalAsync(reference.Value, "PHONE_PIN", GetPinProtector(), ct)).AsMemory();

    Task IWhatsAppPhonePinVault.RemoveAsync(WhatsAppPhonePinReference reference, CancellationToken ct)
        => RemoveInternalAsync(reference.Value, "PHONE_PIN", ct);

    private async Task<string> StoreInternalAsync(string secretType, WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, IDataProtector protector, CancellationToken ct)
    {
        EnsureDurableKeyRingConfigured();
        if (secret.IsEmpty) throw new ArgumentException("El secreto no puede estar vacío.", nameof(secret));
        if (context.IdBase <= 0) throw new ArgumentOutOfRangeException(nameof(context));

        var reference = Guid.NewGuid().ToString("N");
        var protectedValue = protector.Protect(secret.ToString());
        const string sql = """
            INSERT dbo.WhatsAppSecureVault
            (SecretReference,SecretType,IdBase,IdOnboarding,MetaBusinessId,WabaId,PhoneNumberId,Purpose,ProtectedValue,ExpiresAtUtc,CreatedAtUtc,ModifiedAtUtc)
            VALUES
            (@Reference,@SecretType,@IdBase,@IdOnboarding,@MetaBusinessId,@WabaId,@PhoneNumberId,@Purpose,@ProtectedValue,@ExpiresAtUtc,SYSUTCDATETIME(),SYSUTCDATETIME());
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Reference = reference, SecretType = secretType, context.IdBase, context.IdOnboarding,
            MetaBusinessId = context.MetaBusinessId.Trim(), WabaId = context.WabaId.Trim(), PhoneNumberId = context.PhoneNumberId.Trim(),
            Purpose = context.Purpose.Trim(), ProtectedValue = protectedValue, context.ExpiresAtUtc
        }, cancellationToken: ct));
        return reference;
    }

    private async Task<string> GetInternalAsync(string reference, string secretType, IDataProtector protector, CancellationToken ct)
    {
        EnsureDurableKeyRingConfigured();
        const string sql = """
            SELECT ProtectedValue FROM dbo.WhatsAppSecureVault
            WHERE SecretReference=@Reference AND SecretType=@SecretType AND RevokedAtUtc IS NULL
              AND (ExpiresAtUtc IS NULL OR ExpiresAtUtc > SYSUTCDATETIME());
            """;
        await using var cn = new SqlConnection(ConnectionString);
        var protectedValue = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(sql, new { Reference = NormalizeReference(reference), SecretType = secretType }, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(protectedValue)) throw new InvalidOperationException("La referencia segura no existe, venció o fue revocada.");
        return protector.Unprotect(protectedValue);
    }

    private async Task RemoveInternalAsync(string reference, string secretType, CancellationToken ct)
    {
        const string sql = "UPDATE dbo.WhatsAppSecureVault SET RevokedAtUtc=SYSUTCDATETIME(),ModifiedAtUtc=SYSUTCDATETIME() WHERE SecretReference=@Reference AND SecretType=@SecretType AND RevokedAtUtc IS NULL";
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition(sql, new { Reference = NormalizeReference(reference), SecretType = secretType }, cancellationToken: ct));
    }

    private string ConnectionString
        => _connectionString ??= WhatsAppEmbeddedSignupConnection.Resolve(_configuration, _environment);

    private IDataProtector GetCredentialProtector()
        => _credentialProtector ??= WhatsAppEmbeddedSignupDataProtection.ProtectorFor(
            DataProtection, WhatsAppEmbeddedSignupDataProtection.CredentialSecretType);

    private IDataProtector GetPinProtector()
        => _pinProtector ??= WhatsAppEmbeddedSignupDataProtection.ProtectorFor(
            DataProtection, WhatsAppEmbeddedSignupDataProtection.PinSecretType);

    private IDataProtectionProvider DataProtection
        => _dataProtection ??= WhatsAppEmbeddedSignupDataProtection.Create(
            _options.DataProtectionKeysPath,
            _options.DataProtectionKeyProtection,
            _options.DataProtectionCertificateThumbprint);

    private void EnsureDurableKeyRingConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.DataProtectionKeysPath) || !Path.IsPathRooted(_options.DataProtectionKeysPath))
            throw new InvalidOperationException("El vault está bloqueado: falta configurar una ruta absoluta y persistente para Data Protection Keys.");

        if (_options.DataProtectionKeyProtection == WhatsAppDataProtectionKeyProtection.Certificate
            && string.IsNullOrWhiteSpace(_options.DataProtectionCertificateThumbprint))
            throw new InvalidOperationException(
                "El vault está bloqueado: DataProtectionKeyProtection=Certificate requiere DataProtectionCertificateThumbprint.");
    }

    private static string NormalizeReference(string reference)
        => Guid.TryParseExact(reference?.Trim(), "N", out _) ? reference.Trim() : throw new ArgumentException("Referencia segura inválida.", nameof(reference));
}

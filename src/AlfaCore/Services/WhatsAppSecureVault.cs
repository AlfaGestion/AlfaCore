using AlfaCore.Configuration;
using AlfaCore.Models;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class WhatsAppSecureVault : IWhatsAppCredentialVault, IWhatsAppPhonePinVault
{
    private readonly string _connectionString;
    private readonly IDataProtector _credentialProtector;
    private readonly IDataProtector _pinProtector;
    private readonly WhatsAppEmbeddedSignupOptions _options;

    public WhatsAppSecureVault(IConfiguration configuration, IDataProtectionProvider dataProtection, IOptions<WhatsAppEmbeddedSignupOptions> options, IHostEnvironment? environment = null)
    {
        _connectionString = WhatsAppEmbeddedSignupConnection.Resolve(configuration, environment);
        _credentialProtector = dataProtection.CreateProtector("WhatsAppEmbeddedSignup", "Credential", "v1");
        _pinProtector = dataProtection.CreateProtector("WhatsAppEmbeddedSignup", "PhonePin", "v1");
        _options = options.Value;
    }

    async Task<WhatsAppCredentialReference> IWhatsAppCredentialVault.StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct)
        => new(await StoreInternalAsync("CREDENTIAL", context, secret, _credentialProtector, ct));

    async Task<ReadOnlyMemory<char>> IWhatsAppCredentialVault.GetAsync(WhatsAppCredentialReference reference, CancellationToken ct)
        => (await GetInternalAsync(reference.Value, "CREDENTIAL", _credentialProtector, ct)).AsMemory();

    Task IWhatsAppCredentialVault.RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct)
        => RemoveInternalAsync(reference.Value, "CREDENTIAL", ct);

    async Task<WhatsAppPhonePinReference> IWhatsAppPhonePinVault.StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> pin, CancellationToken ct)
    {
        if (pin.Length != 6 || pin.Span.ToArray().Any(static value => value is < '0' or > '9'))
            throw new ArgumentException("El PIN debe contener exactamente seis dígitos.", nameof(pin));
        return new(await StoreInternalAsync("PHONE_PIN", context, pin, _pinProtector, ct));
    }

    async Task<ReadOnlyMemory<char>> IWhatsAppPhonePinVault.GetAsync(WhatsAppPhonePinReference reference, CancellationToken ct)
        => (await GetInternalAsync(reference.Value, "PHONE_PIN", _pinProtector, ct)).AsMemory();

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
        await using var cn = new SqlConnection(_connectionString);
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
        await using var cn = new SqlConnection(_connectionString);
        var protectedValue = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(sql, new { Reference = NormalizeReference(reference), SecretType = secretType }, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(protectedValue)) throw new InvalidOperationException("La referencia segura no existe, venció o fue revocada.");
        return protector.Unprotect(protectedValue);
    }

    private async Task RemoveInternalAsync(string reference, string secretType, CancellationToken ct)
    {
        const string sql = "UPDATE dbo.WhatsAppSecureVault SET RevokedAtUtc=SYSUTCDATETIME(),ModifiedAtUtc=SYSUTCDATETIME() WHERE SecretReference=@Reference AND SecretType=@SecretType AND RevokedAtUtc IS NULL";
        await using var cn = new SqlConnection(_connectionString);
        await cn.ExecuteAsync(new CommandDefinition(sql, new { Reference = NormalizeReference(reference), SecretType = secretType }, cancellationToken: ct));
    }

    private void EnsureDurableKeyRingConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.DataProtectionKeysPath) || !Path.IsPathRooted(_options.DataProtectionKeysPath))
            throw new InvalidOperationException("El vault está bloqueado: falta configurar una ruta absoluta y persistente para Data Protection Keys.");
    }

    private static string NormalizeReference(string reference)
        => Guid.TryParseExact(reference?.Trim(), "N", out _) ? reference.Trim() : throw new ArgumentException("Referencia segura inválida.", nameof(reference));
}

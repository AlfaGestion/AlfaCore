using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AlfaCore.Configuration;
using AlfaCore.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppVaultMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "alfacore-vaultmig-" + Guid.NewGuid().ToString("N"));
    private readonly List<X509Certificate2> _certs = [];

    private string NewKeyDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private X509Certificate2 NewCert(string cn)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, critical: true));
        using var selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3));
        var portable = new X509Certificate2(selfSigned.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
        _certs.Add(portable);
        return portable;
    }

    private IDataProtectionProvider CertProvider(string keyDir, X509Certificate2 cert)
        => WhatsAppEmbeddedSignupDataProtection.Create(
            keyDir, WhatsAppDataProtectionKeyProtection.Certificate, certificateThumbprint: null, certificateOverride: cert);

    // --- constantes de formato en reposo (si cambian, se orfana dbo.WhatsAppSecureVault) --------

    [Fact]
    public void DataProtectionConstants_AreFrozen()
    {
        Assert.Equal("AlfaCore.WhatsAppEmbeddedSignup", WhatsAppEmbeddedSignupDataProtection.ApplicationName);
        Assert.Equal("WhatsAppEmbeddedSignup", WhatsAppEmbeddedSignupDataProtection.PurposeRoot);
        Assert.Equal("Credential", WhatsAppEmbeddedSignupDataProtection.CredentialPurpose);
        Assert.Equal("PhonePin", WhatsAppEmbeddedSignupDataProtection.PinPurpose);
        Assert.Equal("v1", WhatsAppEmbeddedSignupDataProtection.PurposeVersion);
        Assert.Equal("CREDENTIAL", WhatsAppEmbeddedSignupDataProtection.CredentialSecretType);
        Assert.Equal("PHONE_PIN", WhatsAppEmbeddedSignupDataProtection.PinSecretType);
        Assert.Equal("Credential", WhatsAppEmbeddedSignupDataProtection.PurposeFor("CREDENTIAL"));
        Assert.Equal("PhonePin", WhatsAppEmbeddedSignupDataProtection.PurposeFor("PHONE_PIN"));
    }

    [Theory]
    [InlineData("  ab 12 CD ef  ", "AB12CDEF")]
    [InlineData("\u200eAB:12:cd", "AB12CD")]
    public void NormalizeThumbprint_StripsNoiseAndUppercases(string input, string expected)
        => Assert.Equal(expected, WhatsAppEmbeddedSignupDataProtection.NormalizeThumbprint(input));

    // --- soporte Certificate en el proveedor DP -------------------------------------------------

    [Fact]
    public void Create_CertificateMode_RoundTripsAndPersistsAcrossProviders()
    {
        var keyDir = NewKeyDir("cert-persist");
        var cert = NewCert("dp-persist");

        var first = CertProvider(keyDir, cert)
            .CreateProtector(WhatsAppEmbeddedSignupDataProtection.PurposeRoot, "Credential", "v1");
        var payload = first.Protect("token-abc-123");

        // Un segundo proveedor sobre el MISMO directorio + MISMO certificado abre lo que protegió el primero.
        var second = CertProvider(keyDir, cert)
            .CreateProtector(WhatsAppEmbeddedSignupDataProtection.PurposeRoot, "Credential", "v1");
        Assert.Equal("token-abc-123", second.Unprotect(payload));
    }

    [Fact]
    public void HasDataProtectionKeyRingConfiguration_RequiresThumbprintOnlyForCertificateMode()
    {
        var baseOptions = new WhatsAppEmbeddedSignupOptions { DataProtectionKeysPath = @"C:\AlfaCore\Keys" };

        baseOptions.DataProtectionKeyProtection = WhatsAppDataProtectionKeyProtection.DpapiCurrentUser;
        Assert.True(baseOptions.HasDataProtectionKeyRingConfiguration());

        baseOptions.DataProtectionKeyProtection = WhatsAppDataProtectionKeyProtection.DpapiLocalMachine;
        Assert.True(baseOptions.HasDataProtectionKeyRingConfiguration());

        baseOptions.DataProtectionKeyProtection = WhatsAppDataProtectionKeyProtection.Certificate;
        Assert.False(baseOptions.HasDataProtectionKeyRingConfiguration());

        baseOptions.DataProtectionCertificateThumbprint = "ABCDEF0123456789";
        Assert.True(baseOptions.HasDataProtectionKeyRingConfiguration());
    }

    // --- parsing de argumentos ---------------------------------------------------------------

    [Fact]
    public void IsRequested_DetectsTheVerbAnywhere()
    {
        Assert.True(WhatsAppVaultMigrationCommand.IsRequested(["x", "--migrate-whatsapp-vault", "--id-base", "84"]));
        Assert.False(WhatsAppVaultMigrationCommand.IsRequested(["--server=foo", "--id-base", "84"]));
    }

    [Fact]
    public void Parse_DefaultsToDryRun_AndAcceptsBothArgForms()
    {
        var keyDir = NewKeyDir("parse-old");
        var args = new[]
        {
            "--migrate-whatsapp-vault",
            "--id-base", "84",
            "--old-keys", keyDir,
            "--old-protection", "dpapi-current-user",
            "--new-keys=" + Path.Combine(_root, "parse-new"),
            "--new-protection=certificate",
            "--new-cert-thumbprint=AB12CD34",
        };

        var parsed = WhatsAppVaultMigrationCommand.MigrationArgs.Parse(args);

        Assert.Equal(84, parsed.IdBase);
        Assert.Equal(keyDir, parsed.OldKeyRingPath);
        Assert.Equal(WhatsAppDataProtectionKeyProtection.DpapiCurrentUser, parsed.OldProtection);
        Assert.Equal(WhatsAppDataProtectionKeyProtection.Certificate, parsed.NewProtection);
        Assert.Equal("AB12CD34", parsed.NewCertificateThumbprint);
        Assert.False(parsed.Commit);
        Assert.Equal("AlfaCentral", parsed.CentralConnectionName);
    }

    [Fact]
    public void Parse_CommitIsExplicitFlagOnly()
    {
        var keyDir = NewKeyDir("parse-commit-old");
        var parsed = WhatsAppVaultMigrationCommand.MigrationArgs.Parse(
        [
            "--id-base", "84",
            "--old-keys", keyDir, "--old-protection", "dpapi-current-user",
            "--new-keys", Path.Combine(_root, "parse-commit-new"), "--new-protection", "dpapi-local-machine",
            "--commit",
        ]);
        Assert.True(parsed.Commit);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("abc")]
    public void Parse_RejectsNonPositiveIdBase(string idBase)
    {
        var keyDir = NewKeyDir("parse-bad-idbase-" + idBase.Replace("-", "m"));
        var ex = Assert.Throws<ArgumentException>(() => WhatsAppVaultMigrationCommand.MigrationArgs.Parse(
        [
            "--id-base", idBase,
            "--old-keys", keyDir, "--old-protection", "dpapi-current-user",
            "--new-keys", Path.Combine(_root, "x"), "--new-protection", "dpapi-local-machine",
        ]));
        Assert.Contains("--id-base", ex.Message);
    }

    [Fact]
    public void Parse_CertificateModeRequiresThumbprint()
    {
        var keyDir = NewKeyDir("parse-cert-missing");
        var ex = Assert.Throws<ArgumentException>(() => WhatsAppVaultMigrationCommand.MigrationArgs.Parse(
        [
            "--id-base", "84",
            "--old-keys", keyDir, "--old-protection", "dpapi-current-user",
            "--new-keys", Path.Combine(_root, "y"), "--new-protection", "certificate",
        ]));
        Assert.Contains("--new-cert-thumbprint", ex.Message);
    }

    [Fact]
    public void Parse_RejectsUnknownProtectionMode()
    {
        var keyDir = NewKeyDir("parse-bad-mode");
        Assert.Throws<ArgumentException>(() => WhatsAppVaultMigrationCommand.MigrationArgs.Parse(
        [
            "--id-base", "84",
            "--old-keys", keyDir, "--old-protection", "rot13",
            "--new-keys", Path.Combine(_root, "z"), "--new-protection", "dpapi-local-machine",
        ]));
    }

    [Fact]
    public void Parse_RejectsMissingOldKeyRing()
    {
        var ex = Assert.Throws<ArgumentException>(() => WhatsAppVaultMigrationCommand.MigrationArgs.Parse(
        [
            "--id-base", "84",
            "--old-keys", Path.Combine(_root, "does-not-exist"), "--old-protection", "dpapi-current-user",
            "--new-keys", Path.Combine(_root, "z"), "--new-protection", "dpapi-local-machine",
        ]));
        Assert.Contains("--old-keys", ex.Message);
    }

    [Fact]
    public void Parse_RejectsSameOldAndNewKeyRing()
    {
        var keyDir = NewKeyDir("parse-same");
        var ex = Assert.Throws<ArgumentException>(() => WhatsAppVaultMigrationCommand.MigrationArgs.Parse(
        [
            "--id-base", "84",
            "--old-keys", keyDir, "--old-protection", "dpapi-current-user",
            "--new-keys", keyDir, "--new-protection", "dpapi-local-machine",
        ]));
        Assert.Contains("mismo directorio", ex.Message);
    }

    // --- núcleo Reprotect ------------------------------------------------------------------

    [Fact]
    public void Reprotect_RewrapsCredentialAndPin_AcrossKeyRings_WithRoundTrip()
    {
        var oldProvider = CertProvider(NewKeyDir("rp-old"), NewCert("old"));
        var newProvider = CertProvider(NewKeyDir("rp-new"), NewCert("new"));

        const string credentialPlain = "EAAG-super-long-system-user-token-value";
        const string pinPlain = "246813";
        var rows = new[]
        {
            new VaultRow("11111111111111111111111111111111", "CREDENTIAL", 84,
                WhatsAppEmbeddedSignupDataProtection.ProtectorFor(oldProvider, "CREDENTIAL").Protect(credentialPlain)),
            new VaultRow("22222222222222222222222222222222", "PHONE_PIN", 84,
                WhatsAppEmbeddedSignupDataProtection.ProtectorFor(oldProvider, "PHONE_PIN").Protect(pinPlain)),
        };

        var results = WhatsAppVaultMigrationCommand.Reprotect(
            rows, 84,
            t => WhatsAppEmbeddedSignupDataProtection.ProtectorFor(oldProvider, t),
            t => WhatsAppEmbeddedSignupDataProtection.ProtectorFor(newProvider, t));

        Assert.All(results, r => Assert.Equal(VaultRowMigrationStatus.Ready, r.Status));

        Assert.Equal(credentialPlain, WhatsAppEmbeddedSignupDataProtection.ProtectorFor(newProvider, "CREDENTIAL")
            .Unprotect(results[0].ReprotectedValue!));
        Assert.Equal(pinPlain, WhatsAppEmbeddedSignupDataProtection.ProtectorFor(newProvider, "PHONE_PIN")
            .Unprotect(results[1].ReprotectedValue!));

        // El plaintext no aparece en nada imprimible.
        var printable = string.Join("\n", WhatsAppVaultMigrationCommand.FormatRows(results));
        Assert.DoesNotContain(credentialPlain, printable, StringComparison.Ordinal);
        Assert.DoesNotContain(pinPlain, printable, StringComparison.Ordinal);
        Assert.DoesNotContain(rows[0].ProtectedValue, printable, StringComparison.Ordinal);
        Assert.DoesNotContain(results[0].ReprotectedValue!, printable, StringComparison.Ordinal);
    }

    [Fact]
    public void Reprotect_IsIdempotent_WhenRowAlreadyUsesNewKeyRing()
    {
        var oldProvider = CertProvider(NewKeyDir("idem-old"), NewCert("old"));
        var newProvider = CertProvider(NewKeyDir("idem-new"), NewCert("new"));

        var alreadyNew = WhatsAppEmbeddedSignupDataProtection.ProtectorFor(newProvider, "CREDENTIAL").Protect("token");
        var rows = new[] { new VaultRow("33333333333333333333333333333333", "CREDENTIAL", 84, alreadyNew) };

        var results = WhatsAppVaultMigrationCommand.Reprotect(
            rows, 84,
            t => WhatsAppEmbeddedSignupDataProtection.ProtectorFor(oldProvider, t),
            t => WhatsAppEmbeddedSignupDataProtection.ProtectorFor(newProvider, t));

        Assert.Single(results);
        Assert.Equal(VaultRowMigrationStatus.AlreadyMigrated, results[0].Status);
        Assert.Null(results[0].ReprotectedValue);
    }

    [Fact]
    public void Reprotect_AbortsOnRowFromAnotherBase()
    {
        var oldProvider = CertProvider(NewKeyDir("scope-old"), NewCert("old"));
        var newProvider = CertProvider(NewKeyDir("scope-new"), NewCert("new"));
        var rows = new[]
        {
            new VaultRow("44444444444444444444444444444444", "CREDENTIAL", 106,
                WhatsAppEmbeddedSignupDataProtection.ProtectorFor(oldProvider, "CREDENTIAL").Protect("x")),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => WhatsAppVaultMigrationCommand.Reprotect(
            rows, 84,
            t => WhatsAppEmbeddedSignupDataProtection.ProtectorFor(oldProvider, t),
            t => WhatsAppEmbeddedSignupDataProtection.ProtectorFor(newProvider, t)));
        Assert.Contains("fuera de scope", ex.Message);
        Assert.Contains("106", ex.Message);
    }

    [Fact]
    public void Reprotect_AbortsWhenRoundTripDoesNotMatch()
    {
        var oldProvider = CertProvider(NewKeyDir("rt-old"), NewCert("old"));
        var newProvider = CertProvider(NewKeyDir("rt-new"), NewCert("new"));
        var rows = new[]
        {
            new VaultRow("55555555555555555555555555555555", "CREDENTIAL", 84,
                WhatsAppEmbeddedSignupDataProtection.ProtectorFor(oldProvider, "CREDENTIAL").Protect("payload")),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => WhatsAppVaultMigrationCommand.Reprotect(
            rows, 84,
            t => WhatsAppEmbeddedSignupDataProtection.ProtectorFor(oldProvider, t),
            t => new MutatingProtector(WhatsAppEmbeddedSignupDataProtection.ProtectorFor(newProvider, t))));
        Assert.Contains("Round-trip", ex.Message);
    }

    // --- verify-keyring ------------------------------------------------------------------

    [Fact]
    public void VerifyKeyring_CertificateMode_OkForIndependentProvider()
    {
        var keyDir = NewKeyDir("probe-ok");
        var cert = NewCert("probe");

        // Sembrar el key ring con un provider previo (como haría el dry-run al crearlo).
        _ = CertProvider(keyDir, cert)
            .CreateProtector(WhatsAppEmbeddedSignupDataProtection.PurposeRoot, "Credential", "v1")
            .Protect("seed");

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
        try
        {
            var writer = new StringWriter();
            var exit = WhatsAppKeyRingProbeCommand.Run(
                ["--verify-keyring", "--keys", keyDir, "--protection", "certificate", "--cert-thumbprint", cert.Thumbprint],
                writer);

            Assert.Equal(0, exit);
            Assert.Contains("VERIFY KEYRING: OK", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            store.Remove(cert);
        }
    }

    [Fact]
    public void VerifyKeyring_Certificate_RequiresThumbprint()
    {
        var writer = new StringWriter();
        var exit = WhatsAppKeyRingProbeCommand.Run(
            ["--verify-keyring", "--keys", NewKeyDir("probe-nothumb"), "--protection", "certificate"], writer);

        Assert.Equal(2, exit);
        Assert.Contains("--cert-thumbprint", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class MutatingProtector(IDataProtector inner) : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => new MutatingProtector(inner.CreateProtector(purpose));
        public byte[] Protect(byte[] plaintext) => inner.Protect(plaintext);
        public byte[] Unprotect(byte[] protectedData)
        {
            var original = inner.Unprotect(protectedData);
            var tampered = new byte[original.Length + 1];
            Array.Copy(original, tampered, original.Length);
            tampered[^1] = 0x21;
            return tampered;
        }
    }

    public void Dispose()
    {
        foreach (var cert in _certs)
            cert.Dispose();
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup del tempdir
        }
    }
}

using System.Security.Cryptography.X509Certificates;
using AlfaCore.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace AlfaCore.Services;

/// <summary>
/// Construcción única del proveedor aislado de Data Protection que usa el vault de WhatsApp
/// Embedded Signup. El <see cref="ApplicationName"/> y la cadena de purposes
/// (<see cref="PurposeRoot"/> / <see cref="CredentialPurpose"/> / <see cref="PinPurpose"/> /
/// <see cref="PurposeVersion"/>) son parte del formato en reposo: cambiarlos deja ilegible todo
/// <c>dbo.WhatsAppSecureVault</c>. El migrador y el vault deben usar exactamente estas constantes.
/// </summary>
internal static class WhatsAppEmbeddedSignupDataProtection
{
    public const string ApplicationName = "AlfaCore.WhatsAppEmbeddedSignup";
    public const string PurposeRoot = "WhatsAppEmbeddedSignup";
    public const string CredentialPurpose = "Credential";
    public const string PinPurpose = "PhonePin";
    public const string PurposeVersion = "v1";

    public const string CredentialSecretType = "CREDENTIAL";
    public const string PinSecretType = "PHONE_PIN";

    public static IDataProtectionProvider Create(
        string keyRingPath,
        WhatsAppDataProtectionKeyProtection protection,
        string? certificateThumbprint,
        X509Certificate2? certificateOverride = null)
    {
        if (string.IsNullOrWhiteSpace(keyRingPath) || !Path.IsPathRooted(keyRingPath))
            throw new InvalidOperationException(
                "El vault está bloqueado: falta configurar una ruta absoluta y persistente para Data Protection Keys.");

        var keyDirectory = Directory.CreateDirectory(keyRingPath);
        return DataProtectionProvider.Create(keyDirectory, builder =>
        {
            builder.SetApplicationName(ApplicationName);
            switch (protection)
            {
                case WhatsAppDataProtectionKeyProtection.DpapiCurrentUser:
                    if (OperatingSystem.IsWindows())
                        builder.ProtectKeysWithDpapi();
                    break;
                case WhatsAppDataProtectionKeyProtection.DpapiLocalMachine:
                    if (OperatingSystem.IsWindows())
                        builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
                    break;
                case WhatsAppDataProtectionKeyProtection.Certificate:
                    var certificate = certificateOverride ?? ResolveCertificate(certificateThumbprint);
                    builder.ProtectKeysWithCertificate(certificate);
                    if (certificate.HasPrivateKey)
                        builder.UnprotectKeysWithAnyCertificate(certificate);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(protection), protection, "Modo de protección de keys de Data Protection no soportado.");
            }
        });
    }

    public static string PurposeFor(string secretType)
        => string.Equals(secretType, PinSecretType, StringComparison.OrdinalIgnoreCase)
            ? PinPurpose
            : CredentialPurpose;

    public static IDataProtector ProtectorFor(IDataProtectionProvider provider, string secretType)
        => provider.CreateProtector(PurposeRoot, PurposeFor(secretType), PurposeVersion);

    public static string NormalizeThumbprint(string? thumbprint)
        => new string((thumbprint ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToUpperInvariant();

    public static X509Certificate2 ResolveCertificate(string? thumbprint)
    {
        var normalized = NormalizeThumbprint(thumbprint);
        if (normalized.Length == 0)
            throw new InvalidOperationException(
                "El modo Certificate requiere WhatsAppEmbeddedSignup:DataProtectionCertificateThumbprint.");

        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var match = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false);
            if (match.Count > 0)
                return match[0];
        }

        throw new InvalidOperationException(
            $"No se encontró el certificado de Data Protection (thumbprint {normalized}) en LocalMachine\\My ni CurrentUser\\My.");
    }
}

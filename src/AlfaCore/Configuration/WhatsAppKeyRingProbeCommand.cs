using AlfaCore.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AlfaCore.Configuration;

/// <summary>
/// Modo one-shot <c>--verify-keyring</c>: prueba, con dos proveedores independientes creados desde
/// cero, que un key ring de Data Protection es abrible en este proceso/identidad. Protege un valor
/// de sondeo con el primero y lo descifra con el segundo, para ambos purposes del vault. No lee ni
/// escribe SQL, no muestra material sensible. Sirve para simular el servidor productivo antes de
/// cualquier <c>--commit</c> y para validar la portabilidad del key ring nuevo.
/// </summary>
internal static class WhatsAppKeyRingProbeCommand
{
    public const string Verb = "--verify-keyring";

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, Verb, StringComparison.OrdinalIgnoreCase));

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        ProbeArgs parsed;
        try
        {
            parsed = ProbeArgs.Parse(args);
        }
        catch (Exception ex)
        {
            output.WriteLine($"ERROR (argumentos): {ex.Message}");
            output.WriteLine(Usage);
            return 2;
        }

        output.WriteLine("== verify-keyring ==");
        output.WriteLine($"key ring   : {parsed.KeyRingPath}");
        output.WriteLine($"protection : {parsed.Protection}");
        if (parsed.Protection == WhatsAppDataProtectionKeyProtection.Certificate)
            output.WriteLine($"cert       : {parsed.CertificateThumbprint}");

        try
        {
            var probe = "probe-" + Guid.NewGuid().ToString("N");

            var writer = WhatsAppEmbeddedSignupDataProtection.Create(
                parsed.KeyRingPath, parsed.Protection, parsed.CertificateThumbprint);
            var sealedCredential = WhatsAppEmbeddedSignupDataProtection
                .ProtectorFor(writer, WhatsAppEmbeddedSignupDataProtection.CredentialSecretType).Protect(probe);
            var sealedPin = WhatsAppEmbeddedSignupDataProtection
                .ProtectorFor(writer, WhatsAppEmbeddedSignupDataProtection.PinSecretType).Protect(probe);

            // Segundo proveedor desde cero: simula otro proceso (el del App Pool productivo).
            var reader = WhatsAppEmbeddedSignupDataProtection.Create(
                parsed.KeyRingPath, parsed.Protection, parsed.CertificateThumbprint);
            var openedCredential = WhatsAppEmbeddedSignupDataProtection
                .ProtectorFor(reader, WhatsAppEmbeddedSignupDataProtection.CredentialSecretType).Unprotect(sealedCredential);
            var openedPin = WhatsAppEmbeddedSignupDataProtection
                .ProtectorFor(reader, WhatsAppEmbeddedSignupDataProtection.PinSecretType).Unprotect(sealedPin);

            if (!string.Equals(openedCredential, probe, StringComparison.Ordinal)
                || !string.Equals(openedPin, probe, StringComparison.Ordinal))
            {
                output.WriteLine("VERIFY KEYRING: ERROR (round-trip no coincide)");
                return 1;
            }

            output.WriteLine("VERIFY KEYRING: OK (CREDENTIAL + PHONE_PIN, proveedor independiente)");
            return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine($"VERIFY KEYRING: ERROR ({ex.GetType().Name}: {ex.Message})");
            return 1;
        }
    }

    public const string Usage = """
        Uso:
          AlfaCore --verify-keyring --keys <ruta>
                   --protection <dpapi-current-user|dpapi-local-machine|certificate> [--cert-thumbprint <tp>]
        """;

    internal sealed record ProbeArgs(
        string KeyRingPath,
        WhatsAppDataProtectionKeyProtection Protection,
        string? CertificateThumbprint)
    {
        public static ProbeArgs Parse(IReadOnlyList<string> args)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Count; i++)
            {
                var token = args[i];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                    continue;
                var eq = token.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0)
                    map[token[2..eq]] = token[(eq + 1)..];
                else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    map[token[2..]] = args[++i];
            }

            var keys = map.TryGetValue("keys", out var k) && !string.IsNullOrWhiteSpace(k)
                ? k
                : throw new ArgumentException("Falta el argumento obligatorio --keys.");
            if (!Path.IsPathRooted(keys))
                throw new ArgumentException("--keys debe ser una ruta absoluta.");

            var protectionRaw = map.TryGetValue("protection", out var p) && !string.IsNullOrWhiteSpace(p)
                ? p
                : throw new ArgumentException("Falta el argumento obligatorio --protection.");
            var protection = protectionRaw.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
            {
                "dpapicurrentuser" => WhatsAppDataProtectionKeyProtection.DpapiCurrentUser,
                "dpapilocalmachine" => WhatsAppDataProtectionKeyProtection.DpapiLocalMachine,
                "certificate" => WhatsAppDataProtectionKeyProtection.Certificate,
                _ => throw new ArgumentException($"--protection inválido: '{protectionRaw}'.")
            };

            var thumb = map.GetValueOrDefault("cert-thumbprint");
            if (protection == WhatsAppDataProtectionKeyProtection.Certificate && string.IsNullOrWhiteSpace(thumb))
                throw new ArgumentException("--protection certificate requiere --cert-thumbprint.");

            return new ProbeArgs(keys, protection, thumb);
        }
    }
}

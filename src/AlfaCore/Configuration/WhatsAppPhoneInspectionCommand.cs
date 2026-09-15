using System.Net.Http.Headers;
using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;

namespace AlfaCore.Configuration;

/// <summary>
/// Modo one-shot 100% READ-ONLY <c>--inspect-whatsapp-phone --id-base &lt;idBase&gt; --phone-number-id
/// &lt;phoneNumberId&gt;</c>: pensado para correr en SERVER-ALFACENTRAL con el Vault productivo real,
/// igual que <c>--verify-keyring</c> (se resuelve ANTES de CreateBuilder, no arranca Kestrel ni hosted
/// services).
///
/// Flujo:
///  1. Ownership central (<see cref="IWhatsAppAssetOwnershipStore.GetPhoneOwnershipAsync"/>) — exige que
///     el PhoneNumberId pertenezca a IdBase. Sin ownership, o de otra base, corta ACÁ: nunca llega a
///     resolver credencial ni a llamar a Graph.
///  2. Credencial exclusivamente vía <see cref="IWhatsAppRuntimeCredentialResolver"/> (implementación
///     real: <see cref="WhatsAppRuntimeCredentialResolver"/> sobre <see cref="WhatsAppSecureVault"/> con
///     el Data Protection productivo) — un asset con ownership ES NUNCA cae a legacy; si algo falta,
///     error controlado, no fallback.
///  3. Un único GET a Graph para ese PhoneNumberId. Ningún POST/PATCH/DELETE. Campos no disponibles en
///     la versión de Graph actual se informan como "NO DISPONIBLE", nunca se inventan.
///
/// Nunca imprime: access token, ProtectedValue, SecretReference, AppSecret, PIN, header Authorization.
/// </summary>
internal static class WhatsAppPhoneInspectionCommand
{
    public const string Verb = "--inspect-whatsapp-phone";

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, Verb, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IConfiguration configuration, TextWriter output, CancellationToken ct)
    {
        var idBaseArg = ReadOption(args, "--id-base");
        var phoneNumberId = ReadOption(args, "--phone-number-id")?.Trim();
        if (!int.TryParse(idBaseArg, out var idBase) || idBase <= 0 || string.IsNullOrWhiteSpace(phoneNumberId))
        {
            output.WriteLine("Uso: AlfaCore --inspect-whatsapp-phone --id-base <idBase> --phone-number-id <phoneNumberId>");
            return 1;
        }

        var options = configuration.GetSection(WhatsAppEmbeddedSignupOptions.SectionName).Get<WhatsAppEmbeddedSignupOptions>() ?? new();
        var optionsWrapper = Options.Create(options);
        IWhatsAppAssetOwnershipStore ownershipStore = new WhatsAppAssetOwnershipStore(configuration);
        IWhatsAppCredentialVault credentialVault = new WhatsAppSecureVault(configuration, optionsWrapper);
        IWhatsAppRuntimeCredentialResolver resolver = new WhatsAppRuntimeCredentialResolver(ownershipStore, credentialVault, optionsWrapper);
        using var httpClient = new HttpClient();

        return await ExecuteAsync(idBase, phoneNumberId, ownershipStore, resolver, httpClient, options.GraphBaseUrl, output, ct);
    }

    /// <summary>
    /// Núcleo testeable: recibe las dependencias por interfaz (ownership store, resolver de credencial,
    /// HttpClient) para poder probarse con dobles, sin tocar SQL/Vault/Graph reales.
    /// </summary>
    internal static async Task<int> ExecuteAsync(
        int idBase,
        string phoneNumberId,
        IWhatsAppAssetOwnershipStore ownershipStore,
        IWhatsAppRuntimeCredentialResolver credentialResolver,
        HttpClient httpClient,
        string graphBaseUrl,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine("== inspect-whatsapp-phone (read-only) ==");
        output.WriteLine($"BASE = {idBase}");
        output.WriteLine($"PHONE_NUMBER_ID = {phoneNumberId}");

        WhatsAppPhoneOwnership? ownership;
        try
        {
            if (!await ownershipStore.IsSchemaAvailableAsync(ct))
            {
                output.WriteLine("OWNERSHIP = ERROR (esquema central no disponible)");
                WriteBlockedFromOwnership(output);
                return 1;
            }
            ownership = await ownershipStore.GetPhoneOwnershipAsync(phoneNumberId, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine($"OWNERSHIP = ERROR ({ex.GetType().Name})");
            WriteBlockedFromOwnership(output);
            return 1;
        }

        if (ownership is null)
        {
            output.WriteLine("OWNERSHIP = ERROR (sin ownership central para este PhoneNumberId)");
            WriteBlockedFromOwnership(output);
            return 1;
        }
        if (ownership.IdBase != idBase)
        {
            output.WriteLine("OWNERSHIP = ERROR (pertenece a otra base — cross-tenant, bloqueado antes de Graph)");
            WriteBlockedFromOwnership(output);
            return 1;
        }
        output.WriteLine("OWNERSHIP = OK");

        WhatsAppRuntimeCredential credential;
        try
        {
            // legacyConfig vacío a propósito: con ownership ES confirmado arriba, el resolver NUNCA
            // debería usarlo (ver comentario de clase); si por lo que sea lo hiciera, se detecta abajo.
            credential = await credentialResolver.ResolveAsync(idBase, null, phoneNumberId, new ConversacionWhatsAppConfigDto(), ct);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WhatsAppEmbeddedVaultUnavailableException or WhatsAppEmbeddedSchemaUnavailableException)
        {
            output.WriteLine($"VAULT_CREDENTIAL = ERROR ({ex.GetType().Name})");
            WriteBlockedFromVault(output);
            return 1;
        }

        if (credential.Origin != WhatsAppRuntimeCredentialOrigin.EmbeddedSignup)
        {
            output.WriteLine("VAULT_CREDENTIAL = ERROR (resolvió Legacy pese a tener ownership ES; abortado antes de Graph)");
            WriteBlockedFromVault(output);
            return 1;
        }
        output.WriteLine("VAULT_CREDENTIAL = OK");

        var baseUrl = graphBaseUrl.TrimEnd('/');
        var version = credential.GraphVersion.Trim('/');
        var fields = Uri.EscapeDataString("id,display_phone_number,verified_name,quality_rating,platform_type,is_on_biz_app");
        var uri = $"{baseUrl}/{version}/{Uri.EscapeDataString(phoneNumberId)}?fields={fields}";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        // El token vive sólo en este header en memoria; nunca se escribe a `output`.

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        output.WriteLine($"GRAPH HTTP = {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            WriteFields(output, null);
            return 1;
        }

        using var document = JsonDocument.Parse(body);
        WriteFields(output, document.RootElement);

        var numberFields = Uri.EscapeDataString("id,display_phone_number,name_status,new_name_status,certificate,new_certificate");
        var numbersUri = $"{baseUrl}/{version}/{Uri.EscapeDataString(credential.WabaId)}/phone_numbers?fields={numberFields}";
        using var numbersRequest = new HttpRequestMessage(HttpMethod.Get, numbersUri);
        numbersRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);

        using var numbersResponse = await httpClient.SendAsync(numbersRequest, ct);
        var numbersBody = await numbersResponse.Content.ReadAsStringAsync(ct);
        output.WriteLine($"PHONE_NUMBERS GRAPH HTTP = {(int)numbersResponse.StatusCode}");

        if (!numbersResponse.IsSuccessStatusCode)
        {
            WriteNameStatusFields(output, null);
            return 1;
        }

        using var numbersDocument = JsonDocument.Parse(numbersBody);
        WriteNameStatusFields(output, FindPhoneNumber(numbersDocument.RootElement, phoneNumberId));
        return 0;
    }

    private static void WriteBlockedFromOwnership(TextWriter output)
    {
        output.WriteLine("VAULT_CREDENTIAL = N/A (bloqueado)");
        WriteBlockedFromVault(output);
    }

    private static void WriteBlockedFromVault(TextWriter output)
    {
        output.WriteLine("GRAPH HTTP = N/A (bloqueado)");
        WriteFields(output, null);
    }

    private static void WriteFields(TextWriter output, JsonElement? root)
    {
        output.WriteLine($"platform_type = {GetField(root, "platform_type")}");
        output.WriteLine($"is_on_biz_app = {GetBoolField(root, "is_on_biz_app")}");
        output.WriteLine($"display_phone_number = {GetField(root, "display_phone_number")}");
        output.WriteLine($"verified_name = {GetField(root, "verified_name")}");
        output.WriteLine($"quality_rating = {GetField(root, "quality_rating")}");
    }

    private static void WriteNameStatusFields(TextWriter output, JsonElement? phoneNumber)
    {
        var nameStatus = GetField(phoneNumber, "name_status");
        var newNameStatus = GetField(phoneNumber, "new_name_status");
        var certificatePresent = IsPresent(phoneNumber, "certificate");
        var newCertificatePresent = IsPresent(phoneNumber, "new_certificate");
        var diagnosis = ClassifyNameStatus(nameStatus, newNameStatus, newCertificatePresent);

        output.WriteLine($"NAME_STATUS = {nameStatus}");
        output.WriteLine($"NEW_NAME_STATUS = {newNameStatus}");
        output.WriteLine($"CERTIFICATE_PRESENT = {certificatePresent}");
        output.WriteLine($"NEW_CERTIFICATE_PRESENT = {newCertificatePresent}");
        output.WriteLine($"REGISTRATION_AFTER_NAME_CHANGE_NEEDED = {RegistrationAfterNameChangeNeeded(diagnosis)}");
        output.WriteLine($"EVIDENCE = {BuildNameStatusEvidence(diagnosis, nameStatus, newNameStatus, newCertificatePresent)}");
    }

    private static string ClassifyNameStatus(string nameStatus, string newNameStatus, bool newCertificatePresent)
    {
        if (string.Equals(nameStatus, "NO DISPONIBLE", StringComparison.Ordinal)
            || string.Equals(newNameStatus, "NO DISPONIBLE", StringComparison.Ordinal))
            return "D";

        if (!string.Equals(nameStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            return "A";

        if (string.Equals(newNameStatus, "APPROVED", StringComparison.OrdinalIgnoreCase) && newCertificatePresent)
            return "B";

        if (string.Equals(nameStatus, "APPROVED", StringComparison.OrdinalIgnoreCase)
            && string.Equals(newNameStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            return "C";

        return "D";
    }

    private static string RegistrationAfterNameChangeNeeded(string diagnosis)
        => diagnosis == "B" ? "SI" : "NO CONFIRMABLE";

    private static string BuildNameStatusEvidence(string diagnosis, string nameStatus, string newNameStatus, bool newCertificatePresent)
        => diagnosis switch
        {
            "A" => $"A) name_status != APPROVED ({nameStatus}).",
            "B" => $"B) new_name_status = APPROVED y new_certificate presente ({newCertificatePresent}).",
            "C" => "C) name_status y new_name_status APPROVED; si el outbound sigue en 131037, Meta sigue rechazando pese a esos estados.",
            _ => $"D) campos no disponibles o combinacion no concluyente (name_status={nameStatus}, new_name_status={newNameStatus}, new_certificate_present={newCertificatePresent})."
        };

    private static JsonElement? FindPhoneNumber(JsonElement root, string phoneNumberId)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("id", out var id)
                && string.Equals(id.ToString(), phoneNumberId, StringComparison.Ordinal))
                return item;
        }

        return null;
    }

    private static string GetField(JsonElement? root, string propertyName)
        => root is { } element && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "NO DISPONIBLE")
            : "NO DISPONIBLE";

    private static string GetBoolField(JsonElement? root, string propertyName)
        => root is { } element && element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean().ToString()
            : "NO DISPONIBLE";

    private static bool IsPresent(JsonElement? root, string propertyName)
    {
        if (root is not { } element || !element.TryGetProperty(propertyName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.Object => value.EnumerateObject().Any(),
            _ => true
        };
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}

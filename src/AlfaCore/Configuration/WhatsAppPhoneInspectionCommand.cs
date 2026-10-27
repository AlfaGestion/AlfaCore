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
            WriteGraphError(output, "GRAPH", body);
            WriteFields(output, null);
            await WriteAccessDiagnosticsAsync(httpClient, baseUrl, version, credential, output, ct);
            return 1;
        }

        using var document = JsonDocument.Parse(body);
        WriteFields(output, document.RootElement);

        // Campos de estado/bloqueo pedidos aparte: si Meta no reconoce alguno en esta versión de Graph
        // (400), no debe tapar los datos básicos de arriba ni los demás campos.
        await WriteOptionalFieldsAsync(httpClient, baseUrl, version, phoneNumberId, credential.AccessToken, "status,messaging_limit_tier", output, ct,
            root =>
            {
                output.WriteLine($"status = {GetField(root, "status")}");
                output.WriteLine($"messaging_limit_tier = {GetField(root, "messaging_limit_tier")}");
            });
        await WriteOptionalFieldsAsync(httpClient, baseUrl, version, phoneNumberId, credential.AccessToken, "health_status", output, ct,
            root => WriteHealthStatus(output, root));

        var numberFields = Uri.EscapeDataString("id,display_phone_number,name_status,new_name_status,certificate,new_certificate");
        var numbersUri = $"{baseUrl}/{version}/{Uri.EscapeDataString(credential.WabaId)}/phone_numbers?fields={numberFields}";
        using var numbersRequest = new HttpRequestMessage(HttpMethod.Get, numbersUri);
        numbersRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);

        using var numbersResponse = await httpClient.SendAsync(numbersRequest, ct);
        var numbersBody = await numbersResponse.Content.ReadAsStringAsync(ct);
        output.WriteLine($"PHONE_NUMBERS GRAPH HTTP = {(int)numbersResponse.StatusCode}");

        if (!numbersResponse.IsSuccessStatusCode)
        {
            WriteGraphError(output, "PHONE_NUMBERS GRAPH", numbersBody);
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

    /// <summary>
    /// Cuando Graph no deja leer el número (típicamente 100/33): distingue entre WABA caída/restringida,
    /// número quitado de la WABA y permiso revocado al token (debug_token → granular_scopes). Sólo GET.
    /// </summary>
    private static async Task WriteAccessDiagnosticsAsync(
        HttpClient httpClient,
        string baseUrl,
        string version,
        WhatsAppRuntimeCredential credential,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine($"WABA_ID = {credential.WabaId}");
        await WriteOptionalFieldsAsync(httpClient, baseUrl, version, credential.WabaId, credential.AccessToken, "id,name,account_review_status", output, ct,
            root =>
            {
                output.WriteLine($"waba_name = {GetField(root, "name")}");
                output.WriteLine($"waba_account_review_status = {GetField(root, "account_review_status")}");
            });
        await WriteOptionalFieldsAsync(httpClient, baseUrl, version, credential.WabaId, credential.AccessToken, "health_status", output, ct,
            root => WriteHealthStatus(output, root));
        await WriteOptionalFieldsAsync(httpClient, baseUrl, version, $"{credential.WabaId}/phone_numbers", credential.AccessToken, "id,display_phone_number,status", output, ct,
            root =>
            {
                if (root is not { } element || !element.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    output.WriteLine("WABA_PHONE_NUMBERS = NO DISPONIBLE");
                    return;
                }
                output.WriteLine($"WABA_PHONE_NUMBERS = {data.GetArrayLength()}");
                foreach (var item in data.EnumerateArray())
                    output.WriteLine($"  {GetField(item, "id")} {GetField(item, "display_phone_number")} status={GetField(item, "status")}");
            });

        // debug_token sobre el mismo token: el token viaja sólo a Meta (query + header) y nunca se imprime;
        // de la respuesta sólo se muestran validez, vencimientos y los ids a los que da acceso.
        var debugUri = $"{baseUrl}/{version}/debug_token?input_token={Uri.EscapeDataString(credential.AccessToken)}";
        using var debugRequest = new HttpRequestMessage(HttpMethod.Get, debugUri);
        debugRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        using var debugResponse = await httpClient.SendAsync(debugRequest, ct);
        var debugBody = await debugResponse.Content.ReadAsStringAsync(ct);
        if (!debugResponse.IsSuccessStatusCode)
        {
            output.WriteLine($"TOKEN_DEBUG HTTP = {(int)debugResponse.StatusCode}");
            WriteGraphError(output, "TOKEN_DEBUG", debugBody);
            return;
        }

        using var debugDocument = JsonDocument.Parse(debugBody);
        if (!debugDocument.RootElement.TryGetProperty("data", out var tokenData) || tokenData.ValueKind != JsonValueKind.Object)
        {
            output.WriteLine("TOKEN_DEBUG = NO DISPONIBLE");
            return;
        }

        output.WriteLine($"TOKEN_IS_VALID = {GetBoolField(tokenData, "is_valid")}");
        output.WriteLine($"TOKEN_TYPE = {GetField(tokenData, "type")}");
        output.WriteLine($"TOKEN_EXPIRES_AT = {(tokenData.TryGetProperty("expires_at", out var exp) ? exp.ToString() : "NO DISPONIBLE")}");
        if (tokenData.TryGetProperty("error", out var tokenError) && tokenError.ValueKind == JsonValueKind.Object)
            output.WriteLine($"TOKEN_ERROR = {GetField(tokenError, "message")}");
        if (!tokenData.TryGetProperty("granular_scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
            return;

        foreach (var scope in scopes.EnumerateArray())
        {
            var targets = scope.TryGetProperty("target_ids", out var ids) && ids.ValueKind == JsonValueKind.Array
                ? string.Join(",", ids.EnumerateArray().Select(i => i.ToString()))
                : "(todos)";
            output.WriteLine($"TOKEN_SCOPE {GetField(scope, "scope")} -> {targets}");
        }
    }

    private static async Task WriteOptionalFieldsAsync(
        HttpClient httpClient,
        string baseUrl,
        string version,
        string objectPath,
        string accessToken,
        string fields,
        TextWriter output,
        CancellationToken ct,
        Action<JsonElement?> write)
    {
        var uri = $"{baseUrl}/{version}/{objectPath}?fields={Uri.EscapeDataString(fields)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            output.WriteLine($"GRAPH ({fields}) HTTP = {(int)response.StatusCode}");
            WriteGraphError(output, $"GRAPH ({fields})", body);
            write(null);
            return;
        }

        using var document = JsonDocument.Parse(body);
        write(document.RootElement);
    }

    /// <summary>
    /// Imprime el objeto "error" de Graph (mensaje, código, subcódigo, fbtrace_id). Meta no incluye el
    /// token en estas respuestas; si el cuerpo no es JSON se informa sin volcarlo.
    /// </summary>
    private static void WriteGraphError(TextWriter output, string label, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                output.WriteLine($"{label} ERROR = respuesta sin objeto error");
                return;
            }

            var code = error.TryGetProperty("code", out var c) ? c.ToString() : "NO DISPONIBLE";
            var subcode = error.TryGetProperty("error_subcode", out var s) ? s.ToString() : "NO DISPONIBLE";
            output.WriteLine($"{label} ERROR = ({code}/{subcode}) {GetField(error, "message")}");
            var userMessage = GetField(error, "error_user_msg");
            if (userMessage != "NO DISPONIBLE")
                output.WriteLine($"{label} ERROR_USUARIO = {userMessage}");
            output.WriteLine($"{label} FBTRACE_ID = {GetField(error, "fbtrace_id")}");
        }
        catch (JsonException)
        {
            output.WriteLine($"{label} ERROR = respuesta no JSON");
        }
    }

    /// <summary>
    /// health_status de Meta: dice si el número puede enviar (AVAILABLE / LIMITED / BLOCKED) y, por cada
    /// entidad (app, negocio, WABA, número), los errores con su motivo y posible solución. Es la única
    /// fuente que explica un bloqueo o restricción del número.
    /// </summary>
    private static void WriteHealthStatus(TextWriter output, JsonElement? root)
    {
        if (root is not { } element
            || !element.TryGetProperty("health_status", out var health)
            || health.ValueKind != JsonValueKind.Object)
        {
            output.WriteLine("HEALTH_CAN_SEND_MESSAGE = NO DISPONIBLE");
            return;
        }

        output.WriteLine($"HEALTH_CAN_SEND_MESSAGE = {GetField(health, "can_send_message")}");
        if (!health.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Array)
            return;

        foreach (var entity in entities.EnumerateArray())
        {
            if (entity.ValueKind != JsonValueKind.Object)
                continue;

            output.WriteLine($"HEALTH {GetField(entity, "entity_type")} = {GetField(entity, "can_send_message")}");
            WriteHealthList(output, entity, "errors", "ERROR");
            WriteHealthList(output, entity, "additional_info", "INFO");
        }
    }

    private static void WriteHealthList(TextWriter output, JsonElement entity, string propertyName, string label)
    {
        if (!entity.TryGetProperty(propertyName, out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                output.WriteLine($"  {label}: {item.GetString()}");
                continue;
            }
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var code = item.TryGetProperty("error_code", out var c) ? c.ToString() : "NO DISPONIBLE";
            output.WriteLine($"  {label} {code}: {GetField(item, "error_description")}");
            var solution = GetField(item, "possible_solution");
            if (solution != "NO DISPONIBLE")
                output.WriteLine($"    SOLUCION: {solution}");
        }
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
        var nameKind = ClassifyMetaNameStatus(nameStatus);
        var newNameKind = ClassifyMetaNameStatus(newNameStatus);

        if (newNameKind == "SUCCESS"
            && string.Equals(newNameStatus, "APPROVED", StringComparison.OrdinalIgnoreCase)
            && newCertificatePresent)
            return "NAME_CHANGE_READY_TO_REGISTER";

        if (nameKind == "SUCCESS" && newNameKind == "SUCCESS")
            return "SUCCESS";

        if (nameKind == "WAITING" || newNameKind == "WAITING")
            return "WAITING";

        if (nameKind == "REJECTED" || newNameKind == "REJECTED")
            return "REJECTED";

        if (nameKind == "CERTIFICATE_PROBLEM" || newNameKind == "CERTIFICATE_PROBLEM")
            return "CERTIFICATE_PROBLEM";

        return "UNKNOWN";
    }

    private static string ClassifyMetaNameStatus(string status)
    {
        if (string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "AVAILABLE_WITHOUT_REVIEW", StringComparison.OrdinalIgnoreCase))
            return "SUCCESS";

        if (string.Equals(status, "PENDING_REVIEW", StringComparison.OrdinalIgnoreCase))
            return "WAITING";

        if (string.Equals(status, "DECLINED", StringComparison.OrdinalIgnoreCase))
            return "REJECTED";

        if (string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "NONE", StringComparison.OrdinalIgnoreCase))
            return "CERTIFICATE_PROBLEM";

        return "UNKNOWN";
    }

    private static string RegistrationAfterNameChangeNeeded(string diagnosis)
        => diagnosis == "NAME_CHANGE_READY_TO_REGISTER" ? "SI" : diagnosis == "UNKNOWN" ? "NO CONFIRMABLE" : "NO";

    private static string BuildNameStatusEvidence(string diagnosis, string nameStatus, string newNameStatus, bool newCertificatePresent)
        => diagnosis switch
        {
            "NAME_CHANGE_READY_TO_REGISTER" => "NAME_CHANGE_READY_TO_REGISTER) new_name_status = APPROVED y new_certificate presente.",
            "SUCCESS" => "SUCCESS) name_status/new_name_status listos para uso (APPROVED o AVAILABLE_WITHOUT_REVIEW).",
            "WAITING" => $"WAITING) Meta todavía tiene revisión pendiente (name_status={nameStatus}, new_name_status={newNameStatus}).",
            "REJECTED" => $"REJECTED) Meta rechazó el nombre visible (name_status={nameStatus}, new_name_status={newNameStatus}).",
            "CERTIFICATE_PROBLEM" => $"CERTIFICATE_PROBLEM) Meta informa problema de certificado/status (name_status={nameStatus}, new_name_status={newNameStatus}).",
            _ => $"UNKNOWN) combinación no concluyente (name_status={nameStatus}, new_name_status={newNameStatus}, new_certificate_present={newCertificatePresent})."
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

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using static AlfaCore.Configuration.WhatsAppSubscriptionInspectionCommand;

namespace AlfaCore.Configuration;

/// <summary>
/// Modo one-shot administrativo <c>--repair-whatsapp-waba-subscription</c>: inspecciona y, sólo con
/// <c>--commit</c>, actualiza el override de callback de UN WABA ya onboardeado, llamando exactamente una
/// vez al método productivo <see cref="IMetaWhatsAppManagementClient.EnsureWabaSubscriptionAsync"/>
/// (self-check + GET + POST si hace falta + GET de confirmación). Sin <c>--commit</c> es dry-run: sólo GET.
///
/// Reglas fijas:
///  - se resuelve en Program.Main ANTES de CreateBuilder: no arranca Kestrel ni hosted services;
///  - no toca onboarding, no escribe SQL (ReadOnlyCentralBasesService: nunca genera WebhookToken);
///  - secretos sólo desde las fuentes normales del host (Vault, config): ninguno se acepta por CLI;
///  - fail-closed: base/WABA/phone deben coincidir con ownership central antes de cualquier llamada a Meta;
///  - nunca imprime AccessToken, VerifyToken, WebhookToken, AppSecret, URLs completas ni bodies crudos.
///
/// Rollback: el mismo comando con <c>--callback-base-url https://host-anterior</c> reemplaza sólo
/// esquema/host/puerto del callback del tenant y conserva WebhookPath + WebhookToken + VerifyToken del host.
/// </summary>
internal static class WhatsAppWabaSubscriptionRepairCommand
{
    public const string Verb = "--repair-whatsapp-waba-subscription";

    internal const int ExitOk = 0;
    internal const int ExitGuardFailed = 1;
    internal const int ExitUsage = 2;
    internal const int ExitMetaFailed = 3;
    internal const int ExitPostVerifyFailed = 4;
    internal const int ExitPhoneUnverified = 5;

    private const string Usage =
        "Uso: AlfaCore --repair-whatsapp-waba-subscription --id-base <N> --waba-id <ID> --phone-number-id <ID> " +
        "--expect-callback-host <host> [--callback-base-url <https://host>] [--dry-run | --commit]";

    // Ningún secreto se acepta por línea de comandos (quedaría en el historial/lista de procesos).
    private static readonly string[] ForbiddenSecretOptions =
        ["--access-token", "--token", "--verify-token", "--webhook-token", "--app-secret", "--secret"];

    private static readonly Regex MetaIdPattern = new(@"^\d{5,25}$", RegexOptions.CultureInvariant);

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, Verb, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IConfiguration configuration, TextWriter output, CancellationToken ct)
    {
        if (!TryParseOptions(args, out var options, out var usageError))
        {
            output.WriteLine($"ERROR = {usageError}");
            output.WriteLine(Usage);
            return ExitUsage;
        }

        var embeddedOptions = configuration.GetSection(WhatsAppEmbeddedSignupOptions.SectionName).Get<WhatsAppEmbeddedSignupOptions>() ?? new();
        var optionsWrapper = Options.Create(embeddedOptions);
        var whatsAppOptions = configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>() ?? new();

        // Mismo armado que --inspect-whatsapp-subscription: central sólo-lectura (EnsureWebhookTokenAsync
        // lanza), sesión one-shot en memoria, routing provider productivo.
        var centralBases = new ReadOnlyCentralBasesService(configuration);
        var session = new SessionService(new OneShotConexionClienteService());
        var configService = new ConversacionesConfigService(
            configuration,
            session,
            new NullAppEventService(),
            Options.Create(whatsAppOptions),
            new SingleClientFactory(new HttpClient()),
            new OneShotAppUserSessionService(),
            new AllowAllConversacionesAuthorizationService(),
            centralBases);
        IWhatsAppWabaRoutingProvider routingProvider = new WhatsAppWabaRoutingProvider(centralBases, session, configService, optionsWrapper);
        if (options.CallbackBaseUrl is not null)
            routingProvider = new CallbackBaseUrlOverrideRoutingProvider(routingProvider, options.CallbackBaseUrl);

        IWhatsAppAssetOwnershipStore ownershipStore = new WhatsAppAssetOwnershipStore(configuration);
        var vault = new WhatsAppSecureVault(configuration, optionsWrapper);
        IWhatsAppRuntimeCredentialResolver resolver = new WhatsAppRuntimeCredentialResolver(ownershipStore, vault, optionsWrapper);
        using var httpClient = new HttpClient();
        // El management client recibe el MISMO routing provider (eventualmente envuelto): así
        // EnsureWabaSubscriptionAsync calcula exactamente el callback objetivo validado acá.
        IMetaWhatsAppManagementClient managementClient = new MetaWhatsAppManagementClient(
            new SingleClientFactory(httpClient), vault, vault, routingProvider, optionsWrapper);

        return await ExecuteAsync(options, embeddedOptions, centralBases, ownershipStore, resolver, routingProvider, managementClient, httpClient, output, ct);
    }

    internal sealed record RepairOptions(
        int IdBase,
        string WabaId,
        string PhoneNumberId,
        string ExpectCallbackHost,
        Uri? CallbackBaseUrl,
        bool Commit);

    internal static bool TryParseOptions(IReadOnlyList<string> args, out RepairOptions options, out string error)
    {
        options = new RepairOptions(0, string.Empty, string.Empty, string.Empty, null, false);

        var forbidden = args.FirstOrDefault(a => ForbiddenSecretOptions.Any(f =>
            string.Equals(a, f, StringComparison.OrdinalIgnoreCase) || a.StartsWith(f + "=", StringComparison.OrdinalIgnoreCase)));
        if (forbidden is not null)
        {
            error = "No se aceptan secretos por línea de comandos (se toman del Vault/configuración del host).";
            return false;
        }

        var dryRun = args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));
        var commit = args.Any(a => string.Equals(a, "--commit", StringComparison.OrdinalIgnoreCase));
        if (dryRun && commit)
        {
            error = "--dry-run y --commit son excluyentes.";
            return false;
        }

        if (!int.TryParse(ReadOption(args, "--id-base"), out var idBase) || idBase <= 0)
        {
            error = "--id-base inválido.";
            return false;
        }

        var wabaId = (ReadOption(args, "--waba-id") ?? string.Empty).Trim();
        var phoneNumberId = (ReadOption(args, "--phone-number-id") ?? string.Empty).Trim();
        if (!MetaIdPattern.IsMatch(wabaId) || !MetaIdPattern.IsMatch(phoneNumberId))
        {
            error = "--waba-id y --phone-number-id deben ser IDs numéricos de Meta.";
            return false;
        }

        var expectHost = (ReadOption(args, "--expect-callback-host") ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (expectHost.Length == 0 || Uri.CheckHostName(expectHost) == UriHostNameType.Unknown)
        {
            error = "--expect-callback-host es obligatorio y debe ser un host (sin esquema ni path).";
            return false;
        }

        Uri? callbackBaseUrl = null;
        var callbackBaseUrlArg = ReadOption(args, "--callback-base-url");
        if (callbackBaseUrlArg is not null)
        {
            if (!Uri.TryCreate(callbackBaseUrlArg.Trim(), UriKind.Absolute, out var parsed)
                || parsed.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(parsed.UserInfo)
                || parsed.AbsolutePath != "/"
                || !string.IsNullOrEmpty(parsed.Query)
                || !string.IsNullOrEmpty(parsed.Fragment))
            {
                error = "--callback-base-url debe ser https://host[:puerto] sin path, query ni credenciales.";
                return false;
            }

            callbackBaseUrl = parsed;
        }

        options = new RepairOptions(idBase, wabaId, phoneNumberId, expectHost, callbackBaseUrl, commit);
        error = string.Empty;
        return true;
    }

    internal static async Task<int> ExecuteAsync(
        RepairOptions options,
        WhatsAppEmbeddedSignupOptions embeddedOptions,
        ICentralBasesService centralBases,
        IWhatsAppAssetOwnershipStore ownershipStore,
        IWhatsAppRuntimeCredentialResolver credentialResolver,
        IWhatsAppWabaRoutingProvider routingProvider,
        IMetaWhatsAppManagementClient managementClient,
        HttpClient httpClient,
        TextWriter rawOutput,
        CancellationToken ct)
    {
        var output = new RedactingOutput(rawOutput);
        output.WriteLine("== repair-whatsapp-waba-subscription ==");
        output.WriteLine($"MODE = {(options.Commit ? "COMMIT" : "DRY-RUN")}");
        output.WriteLine($"BASE = {options.IdBase}");
        output.WriteLine($"WABA_ID = {options.WabaId}");
        output.WriteLine($"PHONE_NUMBER_ID = {options.PhoneNumberId}");
        output.WriteLine($"EXPECT_CALLBACK_HOST = {options.ExpectCallbackHost}");
        output.WriteLine($"CALLBACK_BASE_URL_OVERRIDE = {(options.CallbackBaseUrl is null ? "N/A" : FormatCallbackHost(options.CallbackBaseUrl.AbsoluteUri))}");
        output.WriteLine("");

        // 1. Configuración del host.
        output.WriteLine($"EMBEDDED_SIGNUP_ENABLED = {embeddedOptions.Enabled}");
        if (!embeddedOptions.Enabled)
            return Abort(output, "EMBEDDED_SIGNUP_DISABLED");
        var appId = (embeddedOptions.AppId ?? string.Empty).Trim();
        output.WriteLine($"APP_ID = {ValueOrEmpty(appId)}");
        if (appId.Length == 0)
            return Abort(output, "APP_ID_MISSING");

        // 2. Base central + WebhookToken existente (nunca se genera).
        BaseCentralDto? baseCentral;
        try
        {
            baseCentral = await centralBases.GetByIdAsync(options.IdBase, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            output.WriteLine($"BASE_LOOKUP = ERROR ({ex.GetType().Name})");
            return Abort(output, "BASE_LOOKUP_FAILED");
        }

        if (baseCentral is null || baseCentral.IdBase != options.IdBase)
            return Abort(output, "BASE_NOT_FOUND");
        var webhookToken = (baseCentral.WebhookToken ?? string.Empty).Trim();
        output.AddSecret(webhookToken);
        output.WriteLine($"WEBHOOK_TOKEN_PRESENT = {webhookToken.Length > 0}");
        if (webhookToken.Length == 0)
            return Abort(output, "WEBHOOK_TOKEN_MISSING");

        // 3. Ownership central: WABA y phone de esta base, phone dentro del WABA pedido.
        try
        {
            if (!await ownershipStore.IsSchemaAvailableAsync(ct))
            {
                output.WriteLine("OWNERSHIP = ERROR (esquema central no disponible)");
                return Abort(output, "OWNERSHIP_SCHEMA_UNAVAILABLE");
            }

            var wabaOwnership = await ownershipStore.GetWabaOwnershipAsync(options.WabaId, ct);
            if (wabaOwnership is null || wabaOwnership.IdBase != options.IdBase)
            {
                output.WriteLine("OWNERSHIP = ERROR (el WABA no pertenece a esta base)");
                return Abort(output, "WABA_OWNERSHIP_MISMATCH");
            }

            var phoneOwnership = await ownershipStore.GetPhoneOwnershipAsync(options.PhoneNumberId, ct);
            if (phoneOwnership is null || phoneOwnership.IdBase != options.IdBase)
            {
                output.WriteLine("OWNERSHIP = ERROR (el número no pertenece a esta base)");
                return Abort(output, "PHONE_OWNERSHIP_MISMATCH");
            }

            if (!string.Equals(phoneOwnership.WabaId, options.WabaId, StringComparison.Ordinal))
            {
                output.WriteLine("OWNERSHIP = ERROR (el número no pertenece al WABA indicado)");
                return Abort(output, "PHONE_WABA_MISMATCH");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            output.WriteLine($"OWNERSHIP = ERROR ({ex.GetType().Name})");
            return Abort(output, "OWNERSHIP_LOOKUP_FAILED");
        }

        output.WriteLine("OWNERSHIP = OK");

        // 4. Credencial runtime del número (Vault) -- la misma que usa el runtime.
        WhatsAppRuntimeCredential credential;
        try
        {
            credential = await credentialResolver.ResolveAsync(options.IdBase, null, options.PhoneNumberId, new ConversacionWhatsAppConfigDto(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            output.WriteLine($"VAULT_CREDENTIAL = ERROR ({ex.GetType().Name})");
            return Abort(output, "CREDENTIAL_UNAVAILABLE");
        }

        output.AddSecret(credential.AccessToken);
        if (credential.Origin != WhatsAppRuntimeCredentialOrigin.EmbeddedSignup
            || credential.CredentialReference is null
            || string.IsNullOrWhiteSpace(credential.AccessToken)
            || !string.Equals(credential.PhoneNumberId, options.PhoneNumberId, StringComparison.Ordinal)
            || !string.Equals(credential.WabaId, options.WabaId, StringComparison.Ordinal))
        {
            output.WriteLine("VAULT_CREDENTIAL = ERROR (la credencial no es la Embedded Signup de este número/WABA)");
            return Abort(output, "CREDENTIAL_MISMATCH");
        }

        output.WriteLine("VAULT_CREDENTIAL = OK");
        output.WriteLine("");

        // 5. Callback objetivo: el mismo routing provider que usará EnsureWabaSubscriptionAsync.
        WhatsAppWabaRoutingConfiguration routing;
        try
        {
            routing = await routingProvider.GetAsync(options.IdBase, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            output.WriteLine($"ROUTING = ERROR ({ex.GetType().Name})");
            output.WriteLine($"ROUTING_ERROR_SUMMARY = {SafeMessage(ex.Message)}");
            return Abort(output, "ROUTING_FAILED");
        }

        output.AddSecret(routing.VerifyToken);
        var verifyTokenPresent = !string.IsNullOrWhiteSpace(routing.VerifyToken);
        output.WriteLine($"VERIFY_TOKEN_PRESENT = {verifyTokenPresent}");
        if (!verifyTokenPresent)
            return Abort(output, "VERIFY_TOKEN_MISSING");

        var targetCallback = (routing.CallbackUrl ?? string.Empty).Trim();
        output.WriteLine($"TARGET_HOST = {FormatCallbackHost(targetCallback)}");
        output.WriteLine($"TARGET_PATH = {MaskWebhookPath(targetCallback)}");
        if (!Uri.TryCreate(targetCallback, UriKind.Absolute, out var targetUri) || targetUri.Scheme != Uri.UriSchemeHttps)
            return Abort(output, "TARGET_CALLBACK_INVALID");
        if (!string.Equals(targetUri.Host.TrimEnd('.'), options.ExpectCallbackHost, StringComparison.OrdinalIgnoreCase))
            return Abort(output, "TARGET_HOST_MISMATCH");
        var targetTokenMatches = string.Equals(LastPathSegment(targetCallback), webhookToken, StringComparison.Ordinal);
        output.WriteLine($"TARGET_TOKEN_MATCHES_BASE_TOKEN = {targetTokenMatches}");
        if (!targetTokenMatches)
            return Abort(output, "TARGET_TOKEN_MISMATCH");
        var targetNormalized = NormalizeCallbackUri(targetCallback);

        // 6. Self-check del callback objetivo con el VerifyToken efectivo del host.
        var challengeOk = await VerifyCallbackChallengeAsync(httpClient, targetCallback, routing.VerifyToken, output, ct);
        output.WriteLine($"CALLBACK_CHALLENGE = {(challengeOk ? "OK" : "ERROR")}");
        if (!challengeOk)
            return Abort(output, "CALLBACK_CHALLENGE_FAILED");
        output.WriteLine("");

        // 7. Estado actual en Meta (read-only).
        var graphRoot = BuildGraphRoot(embeddedOptions.GraphBaseUrl, credential);
        var current = await GetSubscribedAppsAsync(httpClient, graphRoot, options.WabaId, credential.AccessToken, ct);
        output.WriteLine($"CURRENT_SUBSCRIBED_APPS_HTTP = {current.HttpStatusText}");
        if (!current.Success)
        {
            WriteGraphError(output, "CURRENT_SUBSCRIBED_APPS", current.Error);
            return Abort(output, "SUBSCRIBED_APPS_GET_FAILED");
        }

        var currentItem = SelectOwnItem(current.Items, appId);
        var currentTokenMatches = currentItem is not null
            && string.Equals(LastPathSegment(currentItem.OverrideCallbackUri), webhookToken, StringComparison.Ordinal);
        var alreadyConfirmed = IsOurSubscriptionConfirmed(current.Items, appId, targetNormalized);
        output.WriteLine($"CURRENT_SUBSCRIBED_APPS_COUNT = {current.Items.Count}");
        output.WriteLine($"CURRENT_APP_ID = {ValueOrEmpty(currentItem?.AppId)}");
        output.WriteLine($"CURRENT_OVERRIDE_HOST = {FormatCallbackHost(currentItem?.OverrideCallbackUri)}");
        output.WriteLine($"CURRENT_OVERRIDE_PATH = {MaskWebhookPath(currentItem?.OverrideCallbackUri)}");
        output.WriteLine($"CURRENT_OVERRIDE_MATCHES_TARGET = {CallbackMatches(currentItem?.OverrideCallbackUri, targetNormalized)}");
        output.WriteLine($"CURRENT_OVERRIDE_TOKEN_MATCHES_BASE_TOKEN = {currentTokenMatches}");

        // Un override a nivel número taparía el del WABA. Fail-closed para --commit: si no se pudo
        // VERIFICAR la configuración del número (GET fallido, JSON inválido, schema desconocido o
        // phone_number presente pero no interpretable) no se asume "sin override".
        var phone = await GetPhoneWebhookAsync(httpClient, graphRoot, options.PhoneNumberId, credential.AccessToken, ct);
        WritePhoneWebhookState(output, "PHONE", phone, targetNormalized);
        var phoneLevelConflict = phone.Verified && phone.PhoneLevelPresent && !CallbackMatches(phone.PhoneLevelCallback, targetNormalized);

        var wouldPost = !alreadyConfirmed;
        output.WriteLine($"WOULD_POST = {wouldPost}");

        var commitBlocker = !currentTokenMatches ? "CURRENT_OVERRIDE_TOKEN_MISMATCH"
            : !phone.Verified ? "PHONE_WEBHOOK_UNVERIFIABLE"
            : phoneLevelConflict ? "PHONE_LEVEL_OVERRIDE_PRESENT"
            : null;
        output.WriteLine($"COMMIT_ALLOWED = {commitBlocker is null}");
        if (commitBlocker is not null)
            output.WriteLine($"COMMIT_BLOCKED_REASON = {commitBlocker}");
        output.WriteLine("");

        if (!options.Commit)
        {
            output.WriteLine("POST_EXECUTED = False");
            output.WriteLine("RESULT = DRY_RUN_OK");
            return ExitOk;
        }

        // 8. Commit: guardas adicionales y exactamente una llamada al método productivo.
        if (commitBlocker is not null)
            return Abort(output, commitBlocker);

        if (alreadyConfirmed)
        {
            output.WriteLine("POST_EXECUTED = False");
            output.WriteLine("RESULT = ALREADY_CONFIGURED");
            return ExitOk;
        }

        try
        {
            await managementClient.EnsureWabaSubscriptionAsync(options.WabaId, options.IdBase, credential.CredentialReference!, ct);
        }
        catch (MetaWhatsAppManagementException ex)
        {
            output.WriteLine("ENSURE_SUBSCRIPTION = ERROR");
            output.WriteLine($"META_ERROR_CODE = {ValueOrEmpty(ex.ErrorCode)}");
            output.WriteLine($"META_ERROR_TYPE = {ValueOrEmpty(ex.ErrorType)}");
            output.WriteLine($"META_ERROR_MESSAGE = {SafeMessage(ex.MetaErrorMessage ?? ex.Message)}");
            output.WriteLine("RESULT = META_FAILED");
            return ExitMetaFailed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            output.WriteLine("ENSURE_SUBSCRIPTION = ERROR");
            output.WriteLine("META_ERROR_CODE = N/A");
            output.WriteLine($"META_ERROR_TYPE = {ex.GetType().Name}");
            output.WriteLine($"META_ERROR_MESSAGE = {SafeMessage(ex.Message)}");
            output.WriteLine("RESULT = META_FAILED");
            return ExitMetaFailed;
        }

        output.WriteLine("ENSURE_SUBSCRIPTION = OK");
        output.WriteLine("");

        // 9. Post-verify independiente (read-only).
        var final = await GetSubscribedAppsAsync(httpClient, graphRoot, options.WabaId, credential.AccessToken, ct);
        output.WriteLine($"FINAL_SUBSCRIBED_APPS_HTTP = {final.HttpStatusText}");
        var finalItem = final.Success ? SelectOwnItem(final.Items, appId) : null;
        var finalConfirmed = final.Success && IsOurSubscriptionConfirmed(final.Items, appId, targetNormalized);
        output.WriteLine($"FINAL_OVERRIDE_HOST = {FormatCallbackHost(finalItem?.OverrideCallbackUri)}");
        output.WriteLine($"FINAL_OVERRIDE_PATH = {MaskWebhookPath(finalItem?.OverrideCallbackUri)}");
        output.WriteLine($"FINAL_OVERRIDE_MATCHES_TARGET = {finalConfirmed}");

        var finalPhone = await GetPhoneWebhookAsync(httpClient, graphRoot, options.PhoneNumberId, credential.AccessToken, ct);
        WritePhoneWebhookState(output, "PHONE_FINAL", finalPhone, targetNormalized);
        var phoneFinalMatches = finalPhone.Verified && CallbackMatches(finalPhone.EffectiveCallback, targetNormalized);
        output.WriteLine($"PHONE_FINAL_MATCHES_TARGET = {phoneFinalMatches}");

        output.WriteLine("POST_EXECUTED = True");
        if (!finalConfirmed || (finalPhone.Verified && !phoneFinalMatches))
        {
            output.WriteLine("RESULT = POST_VERIFY_FAILED");
            return ExitPostVerifyFailed;
        }

        // subscribed_apps confirma, pero el callback efectivo del número no pudo verificarse: el POST
        // quedó hecho, pero no se reporta como éxito completo.
        if (!finalPhone.Verified)
        {
            output.WriteLine("RESULT = COMMITTED_PHONE_UNVERIFIED");
            return ExitPhoneUnverified;
        }

        output.WriteLine("RESULT = COMMITTED");
        return ExitOk;
    }

    private static int Abort(RedactingOutput output, string reason)
    {
        output.WriteLine("POST_EXECUTED = False");
        output.WriteLine($"ABORT_REASON = {reason}");
        output.WriteLine("RESULT = ABORTED");
        return ExitGuardFailed;
    }

    /// <summary>El ítem de NUESTRA app: id == AppId; si Meta omitió el id (visto en Base4264), el
    /// primer ítem sin id que traiga override. Nunca el ítem de otra app con id explícito.</summary>
    private static SubscribedAppItem? SelectOwnItem(IReadOnlyList<SubscribedAppItem> items, string appId)
        => items.FirstOrDefault(i => string.Equals(i.AppId, appId, StringComparison.Ordinal))
           ?? items.FirstOrDefault(i => i.AppId is null && !string.IsNullOrWhiteSpace(i.OverrideCallbackUri));

    private static bool CallbackMatches(string? callbackUrl, string targetNormalized)
        => !string.IsNullOrWhiteSpace(callbackUrl)
           && string.Equals(NormalizeCallbackUri(callbackUrl), targetNormalized, StringComparison.Ordinal);

    internal static string LastPathSegment(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return string.Empty;
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? string.Empty : Uri.UnescapeDataString(segments[^1]);
    }

    private static string SafeMessage(string? message)
        => RedactWebhookTokens(Sanitize(message));

    private static async Task<bool> VerifyCallbackChallengeAsync(HttpClient httpClient, string callbackUrl, string verifyToken, RedactingOutput output, CancellationToken ct)
    {
        var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        var separator = callbackUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = $"{callbackUrl}{separator}hub.mode=subscribe&hub.verify_token={Uri.EscapeDataString(verifyToken)}&hub.challenge={challenge}";
        try
        {
            using var response = await httpClient.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            output.WriteLine($"CALLBACK_CHALLENGE_HTTP = {(int)response.StatusCode}");
            return response.IsSuccessStatusCode && string.Equals(body.Trim(), challenge, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            output.WriteLine("CALLBACK_CHALLENGE_HTTP = N/A");
            output.WriteLine($"CALLBACK_CHALLENGE_ERROR_TYPE = {ResolveCallbackExceptionType(ex)}");
            return false;
        }
    }

    private sealed record GraphGetResult(bool Success, string HttpStatusText, string Body, GraphErrorInfo? Error);

    private sealed record SubscribedAppsResult(bool Success, string HttpStatusText, IReadOnlyList<SubscribedAppItem> Items, GraphErrorInfo? Error);

    /// <summary>
    /// Estado explícito del GET /{phone}?fields=webhook_configuration. Nunca se reduce a un null
    /// ambiguo: <see cref="Verified"/> sólo es true si el GET respondió 2xx, el schema se entendió y,
    /// si existe el nivel phone_number, su valor también se entendió.
    /// </summary>
    internal sealed record PhoneWebhookState(
        bool GetOk,
        string HttpStatusText,
        GraphErrorInfo? Error,
        bool ConfigParseable,
        bool PhoneLevelPresent,
        bool PhoneLevelParseable,
        string? PhoneLevelCallback,
        string? EffectiveLevel,
        string? EffectiveCallback)
    {
        public bool Verified => GetOk && ConfigParseable && (!PhoneLevelPresent || PhoneLevelParseable);

        public static PhoneWebhookState GetFailed(string httpStatusText, GraphErrorInfo? error)
            => new(false, httpStatusText, error, false, false, false, null, null, null);
    }

    private static readonly string[] KnownPhoneWebhookLevels = ["phone_number", "whatsapp_business_account", "application"];

    private static async Task<GraphGetResult> GraphGetAsync(HttpClient httpClient, string uri, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return response.IsSuccessStatusCode
                ? new GraphGetResult(true, ((int)response.StatusCode).ToString(), body, null)
                : new GraphGetResult(false, ((int)response.StatusCode).ToString(), string.Empty, ParseGraphError(body));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new GraphGetResult(false, "N/A", string.Empty, new GraphErrorInfo(string.Empty, ResolveCallbackExceptionType(ex), ex.Message));
        }
    }

    private static async Task<SubscribedAppsResult> GetSubscribedAppsAsync(HttpClient httpClient, string graphRoot, string wabaId, string accessToken, CancellationToken ct)
    {
        var uri = $"{graphRoot}/{Uri.EscapeDataString(wabaId)}/subscribed_apps?fields={Uri.EscapeDataString("id,override_callback_uri")}&limit=100";
        var result = await GraphGetAsync(httpClient, uri, accessToken, ct);
        return result.Success
            ? new SubscribedAppsResult(true, result.HttpStatusText, ParseSubscribedApps(result.Body), null)
            : new SubscribedAppsResult(false, result.HttpStatusText, [], result.Error);
    }

    private static async Task<PhoneWebhookState> GetPhoneWebhookAsync(HttpClient httpClient, string graphRoot, string phoneNumberId, string accessToken, CancellationToken ct)
    {
        var uri = $"{graphRoot}/{Uri.EscapeDataString(phoneNumberId)}?fields={Uri.EscapeDataString("webhook_configuration")}";
        var result = await GraphGetAsync(httpClient, uri, accessToken, ct);
        return result.Success
            ? ParsePhoneWebhookState(result.HttpStatusText, result.Body)
            : PhoneWebhookState.GetFailed(result.HttpStatusText, result.Error);
    }

    /// <summary>
    /// Parser estricto (más que el del inspector, que es sólo informativo):
    ///  - JSON inválido, raíz no-objeto o webhook_configuration ausente/no-objeto => no interpretable;
    ///  - webhook_configuration sin ningún nivel conocido interpretable => schema desconocido;
    ///  - un nivel es interpretable sólo si trae una URL https absoluta (string u objeto con
    ///    override_callback_uri/callback_uri/callback_url/url/uri); null u otra forma => no interpretable;
    ///  - phone_number presente pero no interpretable queda marcado aparte (bloquea --commit).
    /// </summary>
    internal static PhoneWebhookState ParsePhoneWebhookState(string httpStatusText, string body)
    {
        var unparseable = new PhoneWebhookState(true, httpStatusText, null, false, false, false, null, null, null);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("webhook_configuration", out var config)
                || config.ValueKind != JsonValueKind.Object)
                return unparseable;

            var parsedLevels = new List<(string Level, string CallbackUrl)>();
            var phoneLevelPresent = false;
            string? phoneLevelCallback = null;
            foreach (var property in config.EnumerateObject())
            {
                var level = property.Name.Trim().ToLowerInvariant();
                if (!KnownPhoneWebhookLevels.Contains(level, StringComparer.Ordinal))
                    continue;

                var callback = TryReadStrictCallback(property.Value);
                if (level == "phone_number")
                {
                    phoneLevelPresent = true;
                    phoneLevelCallback = callback;
                }

                if (callback is not null)
                    parsedLevels.Add((level, callback));
            }

            if (parsedLevels.Count == 0)
                return unparseable with { PhoneLevelPresent = phoneLevelPresent };

            var (effectiveLevel, effectiveCallback) = SelectEffectivePhoneWebhook(parsedLevels);
            return new PhoneWebhookState(
                GetOk: true,
                HttpStatusText: httpStatusText,
                Error: null,
                ConfigParseable: true,
                PhoneLevelPresent: phoneLevelPresent,
                PhoneLevelParseable: phoneLevelPresent && phoneLevelCallback is not null,
                PhoneLevelCallback: phoneLevelCallback,
                EffectiveLevel: effectiveLevel,
                EffectiveCallback: effectiveCallback);
        }
        catch (JsonException)
        {
            return unparseable;
        }
    }

    private static string? TryReadStrictCallback(JsonElement value)
    {
        string? candidate = null;
        if (value.ValueKind == JsonValueKind.String)
        {
            candidate = value.GetString();
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "override_callback_uri", "callback_uri", "callback_url", "url", "uri" })
            {
                if (value.TryGetProperty(name, out var inner) && inner.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(inner.GetString()))
                {
                    candidate = inner.GetString();
                    break;
                }
            }
        }

        candidate = candidate?.Trim();
        return !string.IsNullOrEmpty(candidate)
               && Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps
            ? candidate
            : null;
    }

    private static void WritePhoneWebhookState(RedactingOutput output, string prefix, PhoneWebhookState state, string targetNormalized)
    {
        output.WriteLine($"{prefix}_WEBHOOK_HTTP = {state.HttpStatusText}");
        if (!state.GetOk)
            WriteGraphError(output, $"{prefix}_WEBHOOK", state.Error);
        output.WriteLine($"{prefix}_WEBHOOK_CONFIG_PARSEABLE = {(state.GetOk ? state.ConfigParseable.ToString() : "N/A")}");
        output.WriteLine($"{prefix}_WEBHOOK_VERIFIED = {state.Verified}");
        // Estado desconocido => N/A, nunca un False inventado.
        var levelKnown = state.GetOk && (state.ConfigParseable || state.PhoneLevelPresent);
        output.WriteLine($"{prefix}_LEVEL_OVERRIDE_PRESENT = {(levelKnown ? state.PhoneLevelPresent.ToString() : "N/A")}");
        output.WriteLine($"{prefix}_LEVEL_OVERRIDE_PARSEABLE = {(state.PhoneLevelPresent ? state.PhoneLevelParseable.ToString() : "N/A")}");
        output.WriteLine($"{prefix}_LEVEL_OVERRIDE_MATCHES_TARGET = {(state.PhoneLevelPresent && state.PhoneLevelParseable ? CallbackMatches(state.PhoneLevelCallback, targetNormalized).ToString() : "N/A")}");
        output.WriteLine($"{prefix}_EFFECTIVE_LEVEL = {(state.Verified ? state.EffectiveLevel ?? "N/A" : "N/A")}");
        output.WriteLine($"{prefix}_EFFECTIVE_HOST = {(state.Verified ? FormatCallbackHost(state.EffectiveCallback) : "N/A")}");
        output.WriteLine($"{prefix}_EFFECTIVE_PATH = {(state.Verified ? MaskWebhookPath(state.EffectiveCallback) : "N/A")}");
    }

    private static void WriteGraphError(RedactingOutput output, string prefix, GraphErrorInfo? error)
    {
        output.WriteLine($"{prefix}_ERROR_CODE = {ValueOrEmpty(error?.Code)}");
        output.WriteLine($"{prefix}_ERROR_TYPE = {ValueOrEmpty(error?.Type)}");
        output.WriteLine($"{prefix}_ERROR_SUMMARY = {SafeMessage(error?.Summary)}");
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                return args[i + 1];
        }

        return null;
    }

    /// <summary>
    /// Envoltorio de rollback: toma el callback que calcula el routing provider real (WebhookPath +
    /// WebhookToken de la base) y reemplaza SÓLO esquema/host/puerto. El VerifyToken sigue siendo el
    /// efectivo del host que ejecuta el comando.
    /// </summary>
    internal sealed class CallbackBaseUrlOverrideRoutingProvider(IWhatsAppWabaRoutingProvider inner, Uri callbackBaseUrl) : IWhatsAppWabaRoutingProvider
    {
        public async Task<WhatsAppWabaRoutingConfiguration> GetAsync(int idBase, CancellationToken ct = default)
        {
            var routing = await inner.GetAsync(idBase, ct);
            if (!Uri.TryCreate(routing.CallbackUrl, UriKind.Absolute, out var original))
                throw new WhatsAppCallbackRoutingConfigurationException("El callback calculado para la base no es una URL absoluta.");

            var builder = new UriBuilder(original)
            {
                Scheme = callbackBaseUrl.Scheme,
                Host = callbackBaseUrl.Host,
                Port = callbackBaseUrl.IsDefaultPort ? -1 : callbackBaseUrl.Port
            };
            return routing with { CallbackUrl = builder.Uri.AbsoluteUri };
        }
    }

    /// <summary>Defensa en profundidad: cualquier secreto conocido en memoria (WebhookToken,
    /// AccessToken, VerifyToken) se reemplaza antes de escribir, aunque un mensaje lo arrastre.</summary>
    private sealed class RedactingOutput(TextWriter inner)
    {
        private readonly List<string> _secrets = [];

        public void AddSecret(string? secret)
        {
            var value = (secret ?? string.Empty).Trim();
            if (value.Length >= 4 && !_secrets.Contains(value, StringComparer.Ordinal))
                _secrets.Add(value);
        }

        public void WriteLine(string line)
        {
            foreach (var secret in _secrets)
                line = line.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            inner.WriteLine(line);
        }
    }
}

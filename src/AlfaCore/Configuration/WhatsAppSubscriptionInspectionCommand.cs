using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AlfaCore.Configuration;

/// <summary>
/// Modo one-shot 100% READ-ONLY para diagnosticar el paso SUBSCRIBING_WABAS sin ejecutar el POST de
/// suscripcion. Reproduce las dos lecturas previas del flujo real: resolucion de routing + self-check
/// del callback publico, y GET /{wabaId}/subscribed_apps con la credencial runtime del numero.
/// </summary>
internal static class WhatsAppSubscriptionInspectionCommand
{
    public const string Verb = "--inspect-whatsapp-subscription";

    private const string EvidenceRoutingConfigurationFailed = "ROUTING_CONFIGURATION_FAILED";
    private const string EvidenceCallbackSelfCheckFailed = "CALLBACK_SELF_CHECK_FAILED";
    private const string EvidenceAppAlreadySubscribed = "APP_ALREADY_SUBSCRIBED";
    private const string EvidenceAppNotSubscribed = "APP_NOT_SUBSCRIBED";
    private const string EvidenceGraphSubscribedAppsFailed = "GRAPH_SUBSCRIBED_APPS_FAILED";
    private const string EvidenceNoFailureReproduced = "NO_FAILURE_REPRODUCED_READ_ONLY";

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, Verb, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IConfiguration configuration, TextWriter output, CancellationToken ct)
    {
        var idBaseArg = ReadOption(args, "--id-base");
        var compareIdBaseArg = ReadOption(args, "--compare-id-base");
        var phoneNumberId = ReadOption(args, "--phone-number-id")?.Trim();
        var wabaId = ReadOption(args, "--waba-id")?.Trim();
        int? compareIdBase = int.TryParse(compareIdBaseArg, out var parsedCompareIdBase) && parsedCompareIdBase > 0
            ? parsedCompareIdBase
            : null;
        if (!int.TryParse(idBaseArg, out var idBase) || idBase <= 0 || string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(wabaId))
        {
            output.WriteLine("Uso: AlfaCore --inspect-whatsapp-subscription --id-base <idBase> --phone-number-id <phoneNumberId> --waba-id <wabaId> [--compare-id-base <idBase>]");
            return 1;
        }

        var embeddedOptions = configuration.GetSection(WhatsAppEmbeddedSignupOptions.SectionName).Get<WhatsAppEmbeddedSignupOptions>() ?? new();
        var optionsWrapper = Options.Create(embeddedOptions);
        var whatsAppOptions = configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>() ?? new();

        // Rastreo puntual pedido por incidente Base4271/Base4264 (2026-09): el env var Machine
        // WhatsApp__VerifyToken se confirmó presente en el proceso justo antes de lanzar
        // AlfaCore.exe, pero el inspector seguía viendo VERIFY_TOKEN_PRESENT=False. Estas 4 líneas
        // ubican en qué eslabón exacto de Environment -> IConfiguration -> WhatsAppOptions ->
        // fallback de ConversacionesConfigService desaparece -- sólo booleano, nunca el valor.
        var fallbackOptionsWrapper = Options.Create(whatsAppOptions);
        WriteVerifyTokenTrace(output, configuration, whatsAppOptions, fallbackOptionsWrapper.Value);

        var centralBases = new ReadOnlyCentralBasesService(configuration);
        var session = new SessionService(new OneShotConexionClienteService());
        var configService = new ConversacionesConfigService(
            configuration,
            session,
            new NullAppEventService(),
            fallbackOptionsWrapper,
            new SingleClientFactory(new HttpClient()),
            new OneShotAppUserSessionService(),
            new AllowAllConversacionesAuthorizationService(),
            centralBases);
        IWhatsAppWabaRoutingProvider routingProvider = new WhatsAppWabaRoutingProvider(centralBases, session, configService, optionsWrapper);
        IWhatsAppAssetOwnershipStore ownershipStore = new WhatsAppAssetOwnershipStore(configuration);
        IWhatsAppCredentialVault credentialVault = new WhatsAppSecureVault(configuration, optionsWrapper);
        IWhatsAppRuntimeCredentialResolver resolver = new WhatsAppRuntimeCredentialResolver(ownershipStore, credentialVault, optionsWrapper);
        using var httpClient = new HttpClient();

        return await ExecuteAsync(
            idBase,
            phoneNumberId,
            wabaId,
            embeddedOptions.AppId,
            ownershipStore,
            resolver,
            routingProvider,
            httpClient,
            embeddedOptions.GraphBaseUrl,
            output,
            ct,
            (baseId, ct2) => InspectRoutingSourceAsync(baseId, centralBases, session, configService, embeddedOptions, ct2),
            compareIdBase);
    }

    internal static async Task<int> ExecuteAsync(
        int idBase,
        string phoneNumberId,
        string wabaId,
        string expectedAppId,
        IWhatsAppAssetOwnershipStore ownershipStore,
        IWhatsAppRuntimeCredentialResolver credentialResolver,
        IWhatsAppWabaRoutingProvider routingProvider,
        HttpClient httpClient,
        string graphBaseUrl,
        TextWriter output,
        CancellationToken ct,
        Func<int, CancellationToken, Task<RoutingSourceInspection>>? inspectRoutingSource = null,
        int? compareIdBase = null)
    {
        output.WriteLine("== inspect-whatsapp-subscription (read-only) ==");
        output.WriteLine($"BASE = {idBase}");
        output.WriteLine($"PHONE_NUMBER_ID = {phoneNumberId}");
        output.WriteLine($"WABA_ID = {wabaId}");
        output.WriteLine("");

        var normalizedWabaId = (wabaId ?? string.Empty).Trim();
        var normalizedPhoneNumberId = (phoneNumberId ?? string.Empty).Trim();
        var normalizedExpectedAppId = (expectedAppId ?? string.Empty).Trim();

        WhatsAppPhoneOwnership? ownership;
        try
        {
            if (!await ownershipStore.IsSchemaAvailableAsync(ct))
            {
                output.WriteLine("OWNERSHIP = ERROR (esquema central no disponible)");
                WriteOwnershipBlocked(output, normalizedExpectedAppId);
                return 1;
            }

            ownership = await ownershipStore.GetPhoneOwnershipAsync(normalizedPhoneNumberId, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine($"OWNERSHIP = ERROR ({ex.GetType().Name})");
            WriteOwnershipBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        if (ownership is null)
        {
            output.WriteLine("OWNERSHIP = ERROR (sin ownership central para este PhoneNumberId)");
            WriteOwnershipBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        if (ownership.IdBase != idBase)
        {
            output.WriteLine("OWNERSHIP = ERROR (PhoneNumberId pertenece a otra base)");
            WriteOwnershipBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        if (!string.Equals(ownership.WabaId, normalizedWabaId, StringComparison.Ordinal))
        {
            output.WriteLine("OWNERSHIP = ERROR (WabaId no coincide con el ownership del PhoneNumberId)");
            WriteOwnershipBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        output.WriteLine("OWNERSHIP = OK");

        WhatsAppRuntimeCredential credential;
        try
        {
            credential = await credentialResolver.ResolveAsync(idBase, null, normalizedPhoneNumberId, new ConversacionWhatsAppConfigDto(), ct);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WhatsAppEmbeddedVaultUnavailableException or WhatsAppEmbeddedSchemaUnavailableException)
        {
            output.WriteLine($"VAULT_CREDENTIAL = ERROR ({ex.GetType().Name})");
            WriteCredentialBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        if (credential.Origin != WhatsAppRuntimeCredentialOrigin.EmbeddedSignup
            || !string.Equals(credential.PhoneNumberId, normalizedPhoneNumberId, StringComparison.Ordinal)
            || !string.Equals(credential.WabaId, normalizedWabaId, StringComparison.Ordinal))
        {
            output.WriteLine("VAULT_CREDENTIAL = ERROR (la credencial runtime no coincide con ownership)");
            WriteCredentialBlocked(output, normalizedExpectedAppId);
            return 1;
        }

        output.WriteLine("VAULT_CREDENTIAL = OK");
        output.WriteLine("");

        var callback = await InspectCallbackAsync(idBase, routingProvider, httpClient, output, ct, inspectRoutingSource);
        output.WriteLine("");
        var subscribedApps = await InspectSubscribedAppsAsync(
            httpClient,
            graphBaseUrl,
            credential,
            normalizedWabaId,
            normalizedExpectedAppId,
            callback.NormalizedCallbackUrl,
            output,
            ct);

        var evidence = ResolveEvidence(callback, subscribedApps);
        if (compareIdBase is > 0 && inspectRoutingSource is not null)
        {
            output.WriteLine("");
            await WriteRoutingComparisonAsync(idBase, compareIdBase.Value, inspectRoutingSource, output, ct);
        }
        output.WriteLine($"EVIDENCE = {evidence}");
        return 0;
    }

    private static async Task<CallbackInspectionResult> InspectCallbackAsync(
        int idBase,
        IWhatsAppWabaRoutingProvider routingProvider,
        HttpClient httpClient,
        TextWriter output,
        CancellationToken ct,
        Func<int, CancellationToken, Task<RoutingSourceInspection>>? inspectRoutingSource)
    {
        RoutingSourceInspection? source = null;
        if (inspectRoutingSource is not null)
        {
            source = await inspectRoutingSource(idBase, ct);
            WriteRoutingSource(output, source);
            output.WriteLine("");
        }

        output.WriteLine("=== CALLBACK SELF-CHECK ===");
        // Siempre el WhatsAppWabaRoutingProvider real -- el mismo que usa EnsureWabaSubscriptionAsync
        // en producción. Antes existía una reconstrucción paralela en
        // RoutingSourceInspection.ToRoutingConfiguration() que armaba el callback sin el WebhookPath
        // (PublicBaseUrl + "/" + WebhookToken, sin "/api/conversaciones/whatsapp/webhook" en medio) --
        // eso hacía que el self-check del inspector probara una URL que la app real nunca construye,
        // produciendo un CALLBACK_SELF_CHECK_FAILED que no reflejaba el comportamiento real (Base4271,
        // 2026-09). RoutingSourceInspection sigue existiendo sólo para las líneas de presentación
        // (TENANT_PUBLIC_BASE_URL_*, GLOBAL_CALLBACK_BASE_URL_*, VERIFY_TOKEN_SOURCE, etc.) -- ya no
        // participa en construir ninguna URL funcional.
        WhatsAppWabaRoutingConfiguration? routing = null;
        try
        {
            routing = await routingProvider.GetAsync(idBase, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine("CALLBACK_ROUTING_RESOLVED = False");
            output.WriteLine("CALLBACK_HOST = N/A");
            output.WriteLine("CALLBACK_HTTP = N/A");
            output.WriteLine("CALLBACK_REACHABLE = False");
            output.WriteLine("CALLBACK_ELAPSED_MS = 0");
            output.WriteLine($"CALLBACK_ERROR_TYPE = {ex.GetType().Name}");
            output.WriteLine($"CALLBACK_ERROR_SUMMARY = {Sanitize(ex.Message)}");
            return new CallbackInspectionResult(RoutingResolved: false, Reachable: false, NormalizeCallbackUri(null), source);
        }

        var callbackHost = TryGetHost(routing.CallbackUrl);
        output.WriteLine("CALLBACK_ROUTING_RESOLVED = True");
        output.WriteLine($"CALLBACK_HOST = {callbackHost}");
        output.WriteLine($"CALLBACK_PATH = {MaskCallbackPath(routing.CallbackUrl)}");

        var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        var verifyUrl = BuildCallbackVerificationUrl(routing.CallbackUrl, routing.VerifyToken, challenge);
        output.WriteLine($"CALLBACK_QUERY_KEYS = {DescribeQueryKeys(verifyUrl)}");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.GetAsync(verifyUrl, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            stopwatch.Stop();
            var bodyMatchesChallenge = string.Equals(body.Trim(), challenge, StringComparison.Ordinal);
            var reachable = response.IsSuccessStatusCode && bodyMatchesChallenge;
            var contentType = response.Content.Headers.ContentType?.ToString();
            output.WriteLine($"CALLBACK_HTTP = {(int)response.StatusCode}");
            output.WriteLine($"CALLBACK_REACHABLE = {reachable}");
            output.WriteLine($"CALLBACK_ELAPSED_MS = {stopwatch.ElapsedMilliseconds}");
            output.WriteLine($"CALLBACK_RESPONSE_CONTENT_TYPE = {ValueOrEmpty(contentType)}");
            output.WriteLine($"CALLBACK_RESPONSE_LENGTH = {body.Length}");
            output.WriteLine($"CALLBACK_RESPONSE_KIND = {ClassifyCallbackResponseKind(body, contentType, bodyMatchesChallenge)}");
            output.WriteLine($"CALLBACK_RESPONSE_MATCHES_CHALLENGE = {bodyMatchesChallenge}");
            output.WriteLine($"CALLBACK_ERROR_TYPE = {(reachable ? "N/A" : "CallbackVerificationFailed")}");
            output.WriteLine($"CALLBACK_ERROR_SUMMARY = {(reachable ? "N/A" : "El callback no devolvio el challenge esperado.")}");
            return new CallbackInspectionResult(RoutingResolved: true, reachable, NormalizeCallbackUri(routing.CallbackUrl), source);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            output.WriteLine("CALLBACK_HTTP = N/A");
            output.WriteLine("CALLBACK_REACHABLE = False");
            output.WriteLine($"CALLBACK_ELAPSED_MS = {stopwatch.ElapsedMilliseconds}");
            output.WriteLine($"CALLBACK_ERROR_TYPE = {ResolveCallbackExceptionType(ex)}");
            output.WriteLine($"CALLBACK_ERROR_SUMMARY = {Sanitize(ex.Message)}");
            return new CallbackInspectionResult(RoutingResolved: true, Reachable: false, NormalizeCallbackUri(routing.CallbackUrl), source);
        }
    }

    private static async Task<SubscribedAppsInspectionResult> InspectSubscribedAppsAsync(
        HttpClient httpClient,
        string graphBaseUrl,
        WhatsAppRuntimeCredential credential,
        string wabaId,
        string expectedAppId,
        string expectedCallback,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine("=== SUBSCRIBED_APPS GET ===");
        output.WriteLine($"EXPECTED_APP_ID = {ValueOrEmpty(expectedAppId)}");

        var baseUrl = (string.IsNullOrWhiteSpace(graphBaseUrl) ? "https://graph.facebook.com" : graphBaseUrl).TrimEnd('/');
        var version = (string.IsNullOrWhiteSpace(credential.GraphVersion) ? "v26.0" : credential.GraphVersion).Trim('/');
        var uri = $"{baseUrl}/{version}/{Uri.EscapeDataString(wabaId)}/subscribed_apps?fields={Uri.EscapeDataString("id,override_callback_uri")}&limit=100";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        output.WriteLine($"SUBSCRIBED_APPS_HTTP = {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            var error = ParseGraphError(body);
            output.WriteLine("SUBSCRIBED_APPS_COUNT = N/A");
            output.WriteLine("EXPECTED_APP_ID_FOUND = False");
            output.WriteLine("APP_ALREADY_SUBSCRIBED = False");
            output.WriteLine($"ERROR_CODE = {ValueOrEmpty(error.Code)}");
            output.WriteLine($"ERROR_TYPE = {ValueOrEmpty(error.Type)}");
            output.WriteLine($"ERROR_SUMMARY = {ValueOrEmpty(error.Summary)}");
            return new SubscribedAppsInspectionResult(GraphSuccess: false, AppAlreadySubscribed: false, ExpectedAppIdFound: false);
        }

        var items = ParseSubscribedApps(body);
        var expectedFound = !string.IsNullOrWhiteSpace(expectedAppId)
            && items.Any(item => string.Equals(item.AppId, expectedAppId, StringComparison.Ordinal));
        var alreadySubscribed = IsOurSubscriptionConfirmed(items, expectedAppId, expectedCallback);

        output.WriteLine($"SUBSCRIBED_APPS_COUNT = {items.Count}");
        output.WriteLine($"EXPECTED_APP_ID_FOUND = {expectedFound}");
        output.WriteLine($"APP_ALREADY_SUBSCRIBED = {alreadySubscribed}");
        output.WriteLine("ERROR_CODE = N/A");
        output.WriteLine("ERROR_TYPE = N/A");
        output.WriteLine("ERROR_SUMMARY = N/A");
        return new SubscribedAppsInspectionResult(GraphSuccess: true, alreadySubscribed, expectedFound);
    }

    private static string ResolveEvidence(CallbackInspectionResult callback, SubscribedAppsInspectionResult subscribedApps)
    {
        if (!callback.RoutingResolved)
            return EvidenceRoutingConfigurationFailed;
        if (!callback.Reachable)
            return EvidenceCallbackSelfCheckFailed;
        if (!subscribedApps.GraphSuccess)
            return EvidenceGraphSubscribedAppsFailed;
        if (subscribedApps.AppAlreadySubscribed)
            return EvidenceAppAlreadySubscribed;
        if (!subscribedApps.ExpectedAppIdFound)
            return EvidenceAppNotSubscribed;
        return EvidenceNoFailureReproduced;
    }

    /// <summary>internal sólo para permitir el test de regresión del incidente
    /// WEBHOOK_ROUTE_MATCHED/NotSupportedException (2026-09) -- visibilidad, cero cambio de
    /// comportamiento.</summary>
    internal static async Task<RoutingSourceInspection> InspectRoutingSourceAsync(
        int idBase,
        ICentralBasesService centralBases,
        ISessionService sessionService,
        IConversacionesConfigService configService,
        WhatsAppEmbeddedSignupOptions options,
        CancellationToken ct)
    {
        try
        {
            var centralBase = await centralBases.GetByIdAsync(idBase, ct);
            if (centralBase is null)
                return BuildRoutingSourceInspection(idBase, string.Empty, options.CallbackBaseUrl, string.Empty, string.Empty, "CENTRAL_BASE_NOT_FOUND");

            sessionService.SetWebhookOverride(new SessionDto
            {
                Id = SessionDto.BuildGuidFromBaseId(idBase),
                BaseId = idBase,
                Nombre = centralBase.Nombre,
                Servidor = centralBase.DbServer,
                BaseDatos = centralBase.DbName,
                Usuario = centralBase.DbUser,
                Password = centralBase.DbPassword,
                TrustServerCertificate = true,
                Activa = true
            });

            var config = await configService.GetWhatsAppConfigAsync(ct);

            // Sólo presencia (nunca el valor) -- distingue si el VerifyToken vino del tenant o cayó al
            // fallback global, sin necesidad de tocar ConversacionesConfigService (que ya sólo expone
            // el valor fusionado). Nunca falla la inspección si esta lectura extra no puede resolverse.
            var tenantVerifyTokenPresent = await HasTenantConfigValueAsync(centralBase, "CONV_WHATSAPP_VERIFY_TOKEN", ct);

            // Reproduce exactamente TryResolveWebhookTenantAsync (Program.cs): el mismo WebhookToken
            // que va a usar el self-check debe resolver, por lookup inverso, a ESTA base -- si esto da
            // True pero el self-check sigue fallando, el problema no está en el token ni en la DB, está
            // en la capa de enrutamiento/infra delante de la app (IIS/rewrite/self-loopback).
            //
            // Aislado en su propio try/catch a propósito -- regresión confirmada (commit 6771726d):
            // ReadOnlyCentralBasesService.GetByWebhookTokenAsync es un stub que SIEMPRE lanza
            // NotSupportedException ("no se usa" -- era cierto hasta que este mismo diagnóstico
            // empezó a llamarlo). Sin este aislamiento, esa excepción escapaba al catch general del
            // método y tiraba abajo TODO RoutingSourceInspection -- incluido el config.VerifyToken
            // que ya se había resuelto bien un par de líneas más arriba -- produciendo el falso
            // negativo VERIFY_TOKEN_PRESENT=False/VERIFY_TOKEN_SOURCE=NONE ya confirmado contra las
            // DB reales de Base4264/Base4271. Este diagnóstico es best-effort: si no se puede
            // verificar (por este stub u otra causa), es N/A -- nunca debe poder tumbar el resto de
            // la inspección.
            bool? webhookRouteMatched = null;
            if (!string.IsNullOrWhiteSpace(centralBase.WebhookToken))
            {
                try
                {
                    webhookRouteMatched = (await centralBases.GetByWebhookTokenAsync(centralBase.WebhookToken, ct))?.IdBase == idBase;
                }
                catch (Exception)
                {
                    webhookRouteMatched = null;
                }
            }

            return BuildRoutingSourceInspection(
                idBase, config.PublicBaseUrl, options.CallbackBaseUrl, config.VerifyToken, centralBase.WebhookToken,
                forcedFailureReason: null, tenantVerifyTokenPresent: tenantVerifyTokenPresent, webhookRouteMatched: webhookRouteMatched);
        }
        catch (Exception ex)
        {
            return BuildRoutingSourceInspection(idBase, string.Empty, options.CallbackBaseUrl, string.Empty, string.Empty,
                $"ROUTING_CONFIGURATION_EXCEPTION:{ex.GetType().Name}");
        }
    }

    /// <summary>Sólo confirma presencia (no vacío) de una clave en TA_CONFIGURACION de la base tenant
    /// -- nunca devuelve ni loguea el valor. Cualquier error de conexión/permiso se trata como "no
    /// presente" (diagnóstico best-effort, nunca debe tumbar el resto de la inspección read-only).</summary>
    private static async Task<bool> HasTenantConfigValueAsync(BaseCentralDto centralBase, string clave, CancellationToken ct)
    {
        try
        {
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = centralBase.DbServer,
                InitialCatalog = centralBase.DbName,
                UserID = centralBase.DbUser,
                Password = centralBase.DbPassword,
                TrustServerCertificate = true,
                ApplicationName = "AlfaCore"
            }.ConnectionString;
            await using var cn = new SqlConnection(connectionString);
            const string sql = "SELECT VALOR FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;";
            var valor = await cn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(sql, new { Clave = clave }, cancellationToken: ct));
            return !string.IsNullOrWhiteSpace(valor);
        }
        catch
        {
            return false;
        }
    }

    internal static RoutingSourceInspection BuildRoutingSourceInspection(
        int idBase,
        string? tenantPublicBaseUrl,
        string? globalCallbackBaseUrl,
        string? verifyToken,
        string? webhookToken,
        string? forcedFailureReason = null,
        bool tenantVerifyTokenPresent = false,
        bool? webhookRouteMatched = null)
    {
        var tenant = UrlInspection.From(tenantPublicBaseUrl);
        var global = UrlInspection.From(globalCallbackBaseUrl);
        var selectedSource = tenant.Present
            ? "TENANT_PUBLIC_BASE_URL"
            : global.Present
                ? "GLOBAL_CALLBACK_BASE_URL"
                : "NONE";
        var effective = selectedSource == "TENANT_PUBLIC_BASE_URL"
            ? tenant
            : selectedSource == "GLOBAL_CALLBACK_BASE_URL"
                ? global
                : UrlInspection.From(string.Empty);
        var verifyTokenPresent = !string.IsNullOrWhiteSpace(verifyToken);
        var webhookTokenPresent = !string.IsNullOrWhiteSpace(webhookToken);
        var failureReason = !string.IsNullOrWhiteSpace(forcedFailureReason)
            ? forcedFailureReason.Trim()
            : ResolveRoutingFailureReason(selectedSource, effective, verifyTokenPresent, webhookTokenPresent);
        var verifyTokenSource = tenantVerifyTokenPresent ? "TENANT" : verifyTokenPresent ? "GLOBAL" : "NONE";

        return new RoutingSourceInspection(
            idBase,
            tenant,
            global,
            selectedSource,
            effective,
            verifyTokenPresent,
            webhookTokenPresent,
            failureReason,
            verifyToken?.Trim() ?? string.Empty,
            webhookToken?.Trim() ?? string.Empty,
            verifyTokenSource,
            webhookRouteMatched);
    }

    private static string ResolveRoutingFailureReason(string selectedSource, UrlInspection effective, bool verifyTokenPresent, bool webhookTokenPresent)
    {
        if (selectedSource == "NONE")
            return "PUBLIC_BASE_URL_MISSING";
        if (!effective.IsAbsolute)
            return selectedSource + "_NOT_ABSOLUTE";
        if (!effective.IsHttps)
            return selectedSource + "_NOT_HTTPS";
        if (!verifyTokenPresent)
            return "VERIFY_TOKEN_MISSING";
        if (!webhookTokenPresent)
            return "WEBHOOK_TOKEN_MISSING_READ_ONLY";
        return "N/A";
    }

    private static void WriteRoutingSource(TextWriter output, RoutingSourceInspection source)
    {
        output.WriteLine("=== ROUTING SOURCE ===");
        output.WriteLine($"TENANT_PUBLIC_BASE_URL_PRESENT = {source.TenantPublicBaseUrl.Present}");
        output.WriteLine($"TENANT_PUBLIC_BASE_URL_VALUE_SANITIZED = {source.TenantPublicBaseUrl.ValueSanitized}");
        output.WriteLine($"TENANT_PUBLIC_BASE_URL_IS_ABSOLUTE = {source.TenantPublicBaseUrl.IsAbsolute}");
        output.WriteLine($"TENANT_PUBLIC_BASE_URL_IS_HTTPS = {source.TenantPublicBaseUrl.IsHttps}");
        output.WriteLine($"GLOBAL_CALLBACK_BASE_URL_PRESENT = {source.GlobalCallbackBaseUrl.Present}");
        output.WriteLine($"GLOBAL_CALLBACK_BASE_URL_VALUE_SANITIZED = {source.GlobalCallbackBaseUrl.ValueSanitized}");
        output.WriteLine($"GLOBAL_CALLBACK_BASE_URL_IS_ABSOLUTE = {source.GlobalCallbackBaseUrl.IsAbsolute}");
        output.WriteLine($"GLOBAL_CALLBACK_BASE_URL_IS_HTTPS = {source.GlobalCallbackBaseUrl.IsHttps}");
        output.WriteLine($"SELECTED_ROUTING_SOURCE = {source.SelectedRoutingSource}");
        output.WriteLine($"EFFECTIVE_PUBLIC_BASE_URL_SANITIZED = {source.EffectivePublicBaseUrl.ValueSanitized}");
        output.WriteLine($"EFFECTIVE_PUBLIC_BASE_URL_VALID = {source.EffectivePublicBaseUrlValid}");
        output.WriteLine($"VERIFY_TOKEN_PRESENT = {source.VerifyTokenPresent}");
        output.WriteLine($"VERIFY_TOKEN_SOURCE = {source.VerifyTokenSource}");
        output.WriteLine($"WEBHOOK_TOKEN_PRESENT = {source.WebhookTokenPresent}");
        output.WriteLine($"WEBHOOK_ROUTE_MATCHED = {(source.WebhookRouteMatched.HasValue ? source.WebhookRouteMatched.Value.ToString() : "N/A")}");
        output.WriteLine($"ROUTING_FAILURE_REASON = {source.RoutingFailureReason}");
    }

    private static async Task WriteRoutingComparisonAsync(
        int idBase,
        int compareIdBase,
        Func<int, CancellationToken, Task<RoutingSourceInspection>> inspectRoutingSource,
        TextWriter output,
        CancellationToken ct)
    {
        var first = await inspectRoutingSource(idBase, ct);
        var second = await inspectRoutingSource(compareIdBase, ct);
        var byId = new[] { first, second }.ToDictionary(x => x.IdBase);

        output.WriteLine("=== ROUTING COMPARISON ===");
        foreach (var item in byId.OrderBy(x => x.Key))
        {
            output.WriteLine($"BASE{item.Key}_ROUTING_SOURCE = {item.Value.SelectedRoutingSource}");
            output.WriteLine($"BASE{item.Key}_EFFECTIVE_URL_VALID = {item.Value.EffectivePublicBaseUrlValid}");
        }

        output.WriteLine($"DIFFERENCE = {BuildRoutingDifference(first, second)}");
        output.WriteLine($"COMPARISON_EVIDENCE = {BuildRoutingComparisonEvidence(first, second)}");
    }

    private static string BuildRoutingDifference(RoutingSourceInspection first, RoutingSourceInspection second)
    {
        var differences = new List<string>();
        if (!string.Equals(first.SelectedRoutingSource, second.SelectedRoutingSource, StringComparison.Ordinal))
            differences.Add("fuente distinta");
        if (first.EffectivePublicBaseUrlValid != second.EffectivePublicBaseUrlValid)
            differences.Add("validez distinta");
        if (!string.Equals(first.RoutingFailureReason, second.RoutingFailureReason, StringComparison.Ordinal))
            differences.Add("motivo distinto");
        return differences.Count == 0 ? "sin diferencia de routing" : string.Join("; ", differences);
    }

    private static string BuildRoutingComparisonEvidence(RoutingSourceInspection first, RoutingSourceInspection second)
    {
        if (first.EffectivePublicBaseUrlValid && second.EffectivePublicBaseUrlValid)
            return "ambas bases tienen URL publica efectiva valida";
        if (!first.EffectivePublicBaseUrlValid && !second.EffectivePublicBaseUrlValid)
            return "ambas bases tienen routing invalido";
        var failing = first.EffectivePublicBaseUrlValid ? second : first;
        var ok = first.EffectivePublicBaseUrlValid ? first : second;
        return $"BASE{failing.IdBase} falla por {failing.RoutingFailureReason}; BASE{ok.IdBase} tiene routing valido";
    }

    private static IReadOnlyList<SubscribedAppItem> ParseSubscribedApps(string body)
    {
        var items = new List<SubscribedAppItem>();
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return items;

            foreach (var item in data.EnumerateArray())
            {
                var appId = TryReadMetaId(item, "id");
                var callback = TryReadString(item, "override_callback_uri");
                if (appId is not null || !string.IsNullOrWhiteSpace(callback))
                    items.Add(new SubscribedAppItem(appId, callback));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return items;
    }

    private static GraphErrorInfo ParseGraphError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : string.Empty;
                var type = TryReadString(error, "type");
                var summary = Sanitize(TryReadString(error, "message"));
                return new GraphErrorInfo(code, type, summary);
            }
        }
        catch (JsonException)
        {
        }

        return new GraphErrorInfo(string.Empty, string.Empty, Sanitize(body));
    }

    private static bool IsOurSubscriptionConfirmed(IReadOnlyList<SubscribedAppItem> items, string expectedAppId, string expectedCallback)
    {
        foreach (var item in items)
        {
            if (item.AppId is not null)
            {
                if (string.Equals(item.AppId, expectedAppId, StringComparison.Ordinal)
                    && string.Equals(NormalizeCallbackUri(item.OverrideCallbackUri), expectedCallback, StringComparison.Ordinal))
                    return true;
                continue;
            }

            if (string.Equals(NormalizeCallbackUri(item.OverrideCallbackUri), expectedCallback, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Rastreo Environment -> IConfiguration -> WhatsAppOptions -> fallback de
    /// ConversacionesConfigService, sólo presencia -- nunca el valor, longitud, hash ni prefijo del
    /// token. "WhatsApp__VerifyToken" es la convención de .NET para el separador de sección de
    /// configuración (doble guion bajo = ":") -- mapea a la clave "WhatsApp:VerifyToken".
    /// </summary>
    internal static void WriteVerifyTokenTrace(
        TextWriter output,
        IConfiguration configuration,
        WhatsAppOptions whatsAppOptions,
        WhatsAppOptions fallbackOptionsPassedToConfigService)
    {
        output.WriteLine($"RAW_PROCESS_ENV_VERIFY_TOKEN_PRESENT = {!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WhatsApp__VerifyToken"))}");
        output.WriteLine($"RAW_CONFIGURATION_VERIFY_TOKEN_PRESENT = {!string.IsNullOrWhiteSpace(configuration["WhatsApp:VerifyToken"])}");
        output.WriteLine($"RAW_OPTIONS_VERIFY_TOKEN_PRESENT = {!string.IsNullOrWhiteSpace(whatsAppOptions.VerifyToken)}");
        output.WriteLine($"FALLBACK_OPTIONS_VERIFY_TOKEN_PRESENT = {!string.IsNullOrWhiteSpace(fallbackOptionsPassedToConfigService.VerifyToken)}");
        output.WriteLine("");
    }

    private static string BuildCallbackVerificationUrl(string callbackUrl, string verifyToken, string challenge)
    {
        var separator = callbackUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{callbackUrl}{separator}hub.mode=subscribe&hub.verify_token={Uri.EscapeDataString(verifyToken)}&hub.challenge={Uri.EscapeDataString(challenge)}";
    }

    /// <summary>Shape del path sin el WebhookToken real -- WhatsAppWabaRoutingProvider lo agrega como
    /// último segmento (ver GetAsync); acá se reemplaza siempre por "{token}", nunca se imprime.</summary>
    private static string MaskCallbackPath(string callbackUrl)
    {
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri))
            return "N/A";
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return "/";
        segments[^1] = "{token}";
        return "/" + string.Join('/', segments);
    }

    /// <summary>Sólo los nombres de los parámetros de query que arma BuildCallbackVerificationUrl --
    /// nunca sus valores (hub.verify_token y hub.challenge nunca se imprimen).</summary>
    private static string DescribeQueryKeys(string url)
    {
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
            return "N/A";
        var keys = url[(queryStart + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)[0])
            .ToArray();
        return keys.Length == 0 ? "N/A" : string.Join(",", keys);
    }

    /// <summary>Clasifica el body de la respuesta del self-check SIN imprimirlo -- sólo su forma. Útil
    /// para distinguir "el callback devolvió otra cosa" (p. ej. la SPA de Blazor sirviendo su index.html
    /// por un fallback de ruta) de "el callback nunca respondió nada".</summary>
    private static string ClassifyCallbackResponseKind(string body, string? contentType, bool matchesChallenge)
    {
        if (matchesChallenge)
            return "CHALLENGE";
        if (string.IsNullOrEmpty(body))
            return "EMPTY";
        var trimmed = body.TrimStart();
        if ((contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) ?? false)
            || trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            return "HTML";
        if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false)
            return "JSON";
        if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
        {
            try
            {
                JsonDocument.Parse(trimmed);
                return "JSON";
            }
            catch (JsonException)
            {
                // No era JSON válido pese a empezar con { o [ -- cae a TEXT.
            }
        }
        return "TEXT";
    }

    private static string NormalizeCallbackUri(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed.TrimEnd('/');
        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : uri.AbsolutePath;
        return uri.GetLeftPart(UriPartial.Authority) + path + uri.Query;
    }

    private static string? TryReadMetaId(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim(),
            JsonValueKind.Number when value.TryGetInt64(out var id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string TryReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string TryGetHost(string callbackUrl)
        => Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri) ? uri.Host : "N/A";

    private static string ResolveCallbackExceptionType(Exception ex)
        => ex is TaskCanceledException ? "Timeout" : ex.GetType().Name;

    private static string Sanitize(string? value)
    {
        var text = Regex.Replace(value ?? string.Empty, @"[\u0000-\u001F]+", " ").Trim();
        if (text.Length == 0)
            return "N/A";
        text = Regex.Replace(text, @"(?i)(hub\.verify_token=)[^&\s]+", "$1[REDACTED]");
        text = Regex.Replace(text, @"(?i)(access_token=)[^&\s]+", "$1[REDACTED]");
        text = Regex.Replace(text, @"(?i)(verify_token=)[^&\s]+", "$1[REDACTED]");
        text = Regex.Replace(text, @"(?i)(token=)[^&\s]+", "$1[REDACTED]");
        text = Regex.Replace(text, @"(?i)(Authorization:\s*Bearer\s+)[A-Za-z0-9._\-]+", "$1[REDACTED]");
        return text.Length <= 300 ? text : text[..300] + "...";
    }

    private static string ValueOrEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? "N/A" : Sanitize(value);

    private static void WriteOwnershipBlocked(TextWriter output, string expectedAppId)
    {
        output.WriteLine("");
        output.WriteLine("=== CALLBACK SELF-CHECK ===");
        output.WriteLine("CALLBACK_ROUTING_RESOLVED = False");
        output.WriteLine("CALLBACK_HOST = N/A");
        output.WriteLine("CALLBACK_HTTP = N/A");
        output.WriteLine("CALLBACK_REACHABLE = False");
        output.WriteLine("CALLBACK_ELAPSED_MS = 0");
        output.WriteLine("CALLBACK_ERROR_TYPE = OWNERSHIP_BLOCKED");
        output.WriteLine("CALLBACK_ERROR_SUMMARY = Bloqueado antes de resolver routing.");
        output.WriteLine("");
        WriteSkippedSubscribedApps(output, expectedAppId, "OWNERSHIP_BLOCKED");
    }

    private static void WriteCredentialBlocked(TextWriter output, string expectedAppId)
    {
        output.WriteLine("");
        output.WriteLine("=== CALLBACK SELF-CHECK ===");
        output.WriteLine("CALLBACK_ROUTING_RESOLVED = False");
        output.WriteLine("CALLBACK_HOST = N/A");
        output.WriteLine("CALLBACK_HTTP = N/A");
        output.WriteLine("CALLBACK_REACHABLE = False");
        output.WriteLine("CALLBACK_ELAPSED_MS = 0");
        output.WriteLine("CALLBACK_ERROR_TYPE = CREDENTIAL_BLOCKED");
        output.WriteLine("CALLBACK_ERROR_SUMMARY = Bloqueado antes de llamar a Graph.");
        output.WriteLine("");
        WriteSkippedSubscribedApps(output, expectedAppId, "CREDENTIAL_BLOCKED");
    }

    private static void WriteSkippedSubscribedApps(TextWriter output, string expectedAppId, string evidence)
    {
        output.WriteLine("=== SUBSCRIBED_APPS GET ===");
        output.WriteLine($"EXPECTED_APP_ID = {ValueOrEmpty(expectedAppId)}");
        output.WriteLine("SUBSCRIBED_APPS_HTTP = N/A");
        output.WriteLine("SUBSCRIBED_APPS_COUNT = N/A");
        output.WriteLine("EXPECTED_APP_ID_FOUND = False");
        output.WriteLine("APP_ALREADY_SUBSCRIBED = False");
        output.WriteLine("ERROR_CODE = N/A");
        output.WriteLine("ERROR_TYPE = N/A");
        output.WriteLine("ERROR_SUMMARY = N/A");
        output.WriteLine($"EVIDENCE = {evidence}");
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

    internal sealed record RoutingSourceInspection(
        int IdBase,
        UrlInspection TenantPublicBaseUrl,
        UrlInspection GlobalCallbackBaseUrl,
        string SelectedRoutingSource,
        UrlInspection EffectivePublicBaseUrl,
        bool VerifyTokenPresent,
        bool WebhookTokenPresent,
        string RoutingFailureReason,
        string VerifyToken,
        string WebhookToken,
        string VerifyTokenSource = "NONE",
        bool? WebhookRouteMatched = null)
    {
        public bool EffectivePublicBaseUrlValid => EffectivePublicBaseUrl.IsAbsolute && EffectivePublicBaseUrl.IsHttps;
    }

    internal sealed record UrlInspection(string RawTrimmed, bool Present, string ValueSanitized, bool IsAbsolute, bool IsHttps)
    {
        public static UrlInspection From(string? value)
        {
            var raw = (value ?? string.Empty).Trim();
            if (raw.Length == 0)
                return new UrlInspection(string.Empty, false, "N/A", false, false);

            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : uri.AbsolutePath;
                var sanitized = uri.GetLeftPart(UriPartial.Authority) + path;
                return new UrlInspection(raw, true, sanitized, true, uri.Scheme == Uri.UriSchemeHttps);
            }

            var noQuery = raw.Split('?', '#')[0].TrimEnd('/');
            return new UrlInspection(raw, true, Sanitize(noQuery), false, false);
        }
    }

    private sealed record CallbackInspectionResult(bool RoutingResolved, bool Reachable, string NormalizedCallbackUrl, RoutingSourceInspection? Source);
    private sealed record SubscribedAppsInspectionResult(bool GraphSuccess, bool AppAlreadySubscribed, bool ExpectedAppIdFound);
    private sealed record SubscribedAppItem(string? AppId, string OverrideCallbackUri);
    private sealed record GraphErrorInfo(string Code, string Type, string Summary);

    private sealed class ReadOnlyCentralBasesService(IConfiguration configuration) : ICentralBasesService
    {
        private const string SelectColumns = """
            id AS IdBase,
            idcliente AS IdCliente,
            ISNULL(nombre, '') AS Nombre,
            ISNULL(dbserver, '') AS DbServer,
            ISNULL(dbname, '') AS DbName,
            ISNULL(dbuser, '') AS DbUser,
            ISNULL(dbpassword, '') AS DbPassword,
            WebhookToken
            """;

        private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
            ?? throw new InvalidOperationException("No se configuro la cadena de conexion 'ConnectionStrings:AlfaCentral'.");

        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default)
            => Task.FromException<IReadOnlyList<BaseCentralDto>>(new NotSupportedException("Inspector read-only: GetByClienteAsync no se usa."));

        public async Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
        {
            var sql = $"""
                SELECT TOP (1) {SelectColumns}
                FROM dbo.bases
                WHERE id = @IdBase;
                """;
            await using var cn = new SqlConnection(ConnectionString);
            return await cn.QuerySingleOrDefaultAsync<BaseCentralDto>(new CommandDefinition(sql, new { IdBase = idBase }, cancellationToken: ct));
        }

        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromException<IReadOnlyList<BaseCentralDto>>(new NotSupportedException("Inspector read-only: GetAllAsync no se usa."));

        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default)
            => Task.FromException<BaseCentralDto?>(new NotSupportedException("Inspector read-only: GetByWebhookTokenAsync no se usa."));

        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default)
            => Task.FromException<string>(new InvalidOperationException("La base central no tiene WebhookToken y el inspector read-only no puede generarlo."));
    }

    private sealed class OneShotConexionClienteService : IConexionClienteService
    {
        private SessionDto? _session;
        public event Action? SessionChanged;

        public string GetConnectionString()
            => _session is null
                ? string.Empty
                : new SqlConnectionStringBuilder
                {
                    DataSource = _session.Servidor,
                    InitialCatalog = _session.BaseDatos,
                    UserID = _session.Usuario,
                    Password = _session.Password,
                    TrustServerCertificate = _session.TrustServerCertificate,
                    ApplicationName = "AlfaCore"
                }.ConnectionString;

        public SessionDto? GetActiveSession() => _session;
        public SessionDto? GetWebhookOverride(int expectedBaseId) => _session?.BaseId == expectedBaseId ? _session : null;
        public void SetWebhookOverride(SessionDto session) { _session = session; SessionChanged?.Invoke(); }
        public void ClearWebhookOverride() { _session = null; SessionChanged?.Invoke(); }
        public IReadOnlyList<SessionDto> GetAllSessions() => _session is null ? [] : [_session];
        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() => ClearWebhookOverride();
    }

    private sealed class NullAppEventService : IAppEventService
    {
        public Task<string> LogErrorAsync(string module, string action, Exception exception, string userMessage, object? data = null, AppEventSeverity severity = AppEventSeverity.Error, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid().ToString("N"));

        public Task<string> LogAuditAsync(string module, string action, string entityType, string entityId, string message, object? data = null, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid().ToString("N"));

        public Task<Guid> WriteAuditAsync(AuditWriteRequest request, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> WriteAuditAsync(AuditWriteRequest request, SqlConnection connection, SqlTransaction transaction, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<AuditActivityPageDto> GetActivityAsync(string entityType, string recordId, int pageNumber = 1, int pageSize = 20, CancellationToken ct = default)
            => Task.FromResult(new AuditActivityPageDto { PageNumber = pageNumber, PageSize = pageSize });

        public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(CancellationToken ct = default)
            => Task.FromResult(new AuditSchemaAvailabilityDto { Available = false });

        public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(SqlConnection connection, SqlTransaction? transaction = null, CancellationToken ct = default)
            => Task.FromResult(new AuditSchemaAvailabilityDto { Available = false });
    }

    private sealed class OneShotAppUserSessionService : IAppUserSessionService
    {
        public event Action? StateChanged { add { } remove { } }
        public bool IsAuthenticated => false;
        public AppUserSessionInfo? CurrentUser => null;
        public bool RequiresInternalLogin => false;
        public string? CurrentToken => null;
        public Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public void AdoptInternalUser(AppUserSessionInfo internalUser) => throw new NotSupportedException();
        public bool TryRestoreFromToken(string token) => false;
        public void Logout() { }
        public void HandleSqlSessionChanged() { }
        public string GetCurrentUserName(string fallback = "") => fallback;
        public bool IsAuthorizedForSession(Guid? activeSessionId) => true;
        public void EnsureAuthorizedForSession(Guid? activeSessionId) { }
    }

    private sealed class AllowAllConversacionesAuthorizationService : IConversacionesAuthorizationService
    {
        public Task<bool> CanManageAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanManageAsync(int? expectedBaseId, CancellationToken ct = default) => Task.FromResult(true);
        public Task EnsureCanManageAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanManageAsync(int? expectedBaseId, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanAttendConversationAsync(long idConversacion, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanAttendConversationAsync(long idConversacion, string connectionString, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanUseWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanUseWhatsAppNumeroAsync(int idNumero, string connectionString, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

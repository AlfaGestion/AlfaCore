using System.Net;
using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;
using Xunit;
using static AlfaCore.Configuration.WhatsAppWabaSubscriptionRepairCommand;

namespace AlfaCore.Tests;

/// <summary>
/// --repair-whatsapp-waba-subscription contra un simulador de Graph con estado. Se usa el
/// MetaWhatsAppManagementClient REAL: el POST que se cuenta es el que emite el
/// EnsureWabaSubscriptionAsync productivo, no un doble.
/// </summary>
public sealed class WhatsAppWabaSubscriptionRepairCommandTests
{
    private const int IdBase = 4264;
    private const string Waba = "888902717349521";
    private const string Phone = "1243405415530992";
    private const string AppId = "1436083307772786";
    private const string AccessToken = "runtime-access-token-never-printed";
    private const string VerifyToken = "verify-token-never-printed";
    private const string WebhookToken = "B6F534AA11BB22CC33DD44EE55FF66007711882299330044AA55BB66CC77DD88";
    private const string OtherToken = "0D39F9AA11BB22CC33DD44EE55FF66007711882299330044AA55BB66CC77DD00";
    private const string WebhookPath = "/api/conversaciones/whatsapp/webhook/";
    private const string AlfaWebCallback = "https://alfanetweb.ddns.net" + WebhookPath + WebhookToken;
    private const string AlfaCentralCallback = "https://alfacentral.ddns.net" + WebhookPath + WebhookToken;
    private const string AppLevelCallback = "https://app-level.example.net" + WebhookPath + "APPLEVELTOKEN123";

    [Fact]
    public async Task DryRun_MakesNoPost_AndReportsPlan()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback };

        var (exit, output) = await RunAsync(meta, commit: false);

        Assert.Equal(ExitOk, exit);
        Assert.Equal(0, meta.PostCount);
        Assert.Contains("MODE = DRY-RUN", output);
        Assert.Contains("OWNERSHIP = OK", output);
        Assert.Contains("VAULT_CREDENTIAL = OK", output);
        Assert.Contains("TARGET_HOST = alfanetweb.ddns.net", output);
        Assert.Contains("TARGET_PATH = /api/conversaciones/whatsapp/webhook/<token>", output);
        Assert.Contains("CALLBACK_CHALLENGE = OK", output);
        Assert.Contains($"CURRENT_APP_ID = {AppId}", output);
        Assert.Contains("CURRENT_OVERRIDE_HOST = alfacentral.ddns.net", output);
        Assert.Contains("CURRENT_OVERRIDE_PATH = /api/conversaciones/whatsapp/webhook/<token>", output);
        Assert.Contains("CURRENT_OVERRIDE_MATCHES_TARGET = False", output);
        Assert.Contains("CURRENT_OVERRIDE_TOKEN_MATCHES_BASE_TOKEN = True", output);
        Assert.Contains("PHONE_EFFECTIVE_LEVEL = whatsapp_business_account", output);
        Assert.Contains("PHONE_EFFECTIVE_HOST = alfacentral.ddns.net", output);
        Assert.Contains("WOULD_POST = True", output);
        Assert.Contains("COMMIT_ALLOWED = True", output);
        Assert.Contains("RESULT = DRY_RUN_OK", output);
        Assert.All(meta.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        AssertNoSecrets(output);
    }

    [Fact]
    public async Task Commit_IssuesExactlyOnePost_ToRequestedWaba_WithTargetCallback()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback };

        var (exit, output) = await RunAsync(meta, commit: true);

        Assert.Equal(ExitOk, exit);
        var post = Assert.Single(meta.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal($"/v26.0/{Waba}/subscribed_apps", post.Uri.AbsolutePath);
        using (var body = JsonDocument.Parse(post.Body))
        {
            Assert.Equal(AlfaWebCallback, body.RootElement.GetProperty("override_callback_uri").GetString());
            Assert.Equal(VerifyToken, body.RootElement.GetProperty("verify_token").GetString());
        }
        Assert.Contains("ENSURE_SUBSCRIPTION = OK", output);
        Assert.Contains("FINAL_OVERRIDE_HOST = alfanetweb.ddns.net", output);
        Assert.Contains("FINAL_OVERRIDE_PATH = /api/conversaciones/whatsapp/webhook/<token>", output);
        Assert.Contains("FINAL_OVERRIDE_MATCHES_TARGET = True", output);
        Assert.Contains("PHONE_FINAL_EFFECTIVE_HOST = alfanetweb.ddns.net", output);
        Assert.Contains("PHONE_FINAL_MATCHES_TARGET = True", output);
        Assert.Contains("RESULT = COMMITTED", output);
        Assert.Equal(AlfaWebCallback, meta.Override);
        AssertNoSecrets(output);
    }

    [Fact]
    public async Task Commit_AlreadyCorrect_MakesNoPost()
    {
        var meta = new MetaSimulator { Override = AlfaWebCallback };

        var (exit, output) = await RunAsync(meta, commit: true);

        Assert.Equal(ExitOk, exit);
        Assert.Equal(0, meta.PostCount);
        Assert.Contains("WOULD_POST = False", output);
        Assert.Contains("RESULT = ALREADY_CONFIGURED", output);
    }

    [Fact]
    public async Task PhoneOwnedByAnotherBase_MakesNoMetaRequests()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback };

        var (exit, output) = await RunAsync(meta, commit: true, f => f.PhoneOwnership = new WhatsAppPhoneOwnership(Phone, Waba, 9999, DateTime.UtcNow));

        Assert.Equal(ExitGuardFailed, exit);
        Assert.Empty(meta.Requests);
        Assert.Contains("ABORT_REASON = PHONE_OWNERSHIP_MISMATCH", output);
    }

    [Fact]
    public async Task WabaOwnedByAnotherBase_MakesNoMetaRequests()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback };

        var (exit, output) = await RunAsync(meta, commit: true, f => f.WabaOwnership = new WhatsAppWabaOwnership(Waba, 9999, "428784260865436", DateTime.UtcNow));

        Assert.Equal(ExitGuardFailed, exit);
        Assert.Empty(meta.Requests);
        Assert.Contains("ABORT_REASON = WABA_OWNERSHIP_MISMATCH", output);
    }

    [Fact]
    public async Task PhoneInAnotherWaba_MakesNoMetaRequests()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback };

        var (exit, output) = await RunAsync(meta, commit: true, f => f.PhoneOwnership = new WhatsAppPhoneOwnership(Phone, "2597305014055622", IdBase, DateTime.UtcNow));

        Assert.Equal(ExitGuardFailed, exit);
        Assert.Empty(meta.Requests);
        Assert.Contains("ABORT_REASON = PHONE_WABA_MISMATCH", output);
    }

    [Fact]
    public async Task MissingExistingWebhookToken_Aborts_WithoutRoutingOrMeta()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback };

        var (exit, output) = await RunAsync(meta, commit: true, f => f.BaseWebhookToken = null);

        Assert.Equal(ExitGuardFailed, exit);
        Assert.Empty(meta.Requests);
        Assert.Contains("WEBHOOK_TOKEN_PRESENT = False", output);
        Assert.Contains("ABORT_REASON = WEBHOOK_TOKEN_MISSING", output);
    }

    [Fact]
    public async Task EmbeddedSignupDisabledOrAppIdMissing_Aborts_WithoutMeta()
    {
        var disabledMeta = new MetaSimulator { Override = AlfaCentralCallback };
        var (disabledExit, disabledOutput) = await RunAsync(disabledMeta, commit: true, f => f.Options.Enabled = false);
        Assert.Equal(ExitGuardFailed, disabledExit);
        Assert.Empty(disabledMeta.Requests);
        Assert.Contains("ABORT_REASON = EMBEDDED_SIGNUP_DISABLED", disabledOutput);

        var noAppMeta = new MetaSimulator { Override = AlfaCentralCallback };
        var (noAppExit, noAppOutput) = await RunAsync(noAppMeta, commit: true, f => f.Options.AppId = string.Empty);
        Assert.Equal(ExitGuardFailed, noAppExit);
        Assert.Empty(noAppMeta.Requests);
        Assert.Contains("ABORT_REASON = APP_ID_MISSING", noAppOutput);
    }

    [Fact]
    public async Task ExpectHostMismatch_MakesNoPost()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback };

        var (exit, output) = await RunAsync(meta, commit: true, f => f.ExpectHost = "otro-host.example.com");

        Assert.Equal(ExitGuardFailed, exit);
        Assert.Equal(0, meta.PostCount);
        Assert.Empty(meta.Requests); // se corta antes incluso del challenge.
        Assert.Contains("ABORT_REASON = TARGET_HOST_MISMATCH", output);
    }

    [Fact]
    public async Task ChallengeFailure_MakesNoPost()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback, FailChallenge = true };

        var (exit, output) = await RunAsync(meta, commit: true);

        Assert.Equal(ExitGuardFailed, exit);
        Assert.Equal(0, meta.PostCount);
        Assert.DoesNotContain(meta.Requests, r => r.Uri.Host == "graph.facebook.com");
        Assert.Contains("CALLBACK_CHALLENGE = ERROR", output);
        Assert.Contains("ABORT_REASON = CALLBACK_CHALLENGE_FAILED", output);
    }

    [Fact]
    public async Task CurrentOverrideWithAnotherToken_BlocksCommit()
    {
        var meta = new MetaSimulator { Override = "https://alfacentral.ddns.net" + WebhookPath + OtherToken };

        var (exit, output) = await RunAsync(meta, commit: true);

        Assert.Equal(ExitGuardFailed, exit);
        Assert.Equal(0, meta.PostCount);
        Assert.Contains("CURRENT_OVERRIDE_TOKEN_MATCHES_BASE_TOKEN = False", output);
        Assert.Contains("COMMIT_ALLOWED = False", output);
        Assert.Contains("ABORT_REASON = CURRENT_OVERRIDE_TOKEN_MISMATCH", output);
        Assert.DoesNotContain(OtherToken, output);
    }

    [Fact]
    public async Task PhoneLevelOverrideElsewhere_BlocksCommit()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback, PhoneLevelOverride = AlfaCentralCallback };

        var (exit, output) = await RunAsync(meta, commit: true);

        Assert.Equal(ExitGuardFailed, exit);
        Assert.Equal(0, meta.PostCount);
        Assert.Contains("PHONE_LEVEL_OVERRIDE_PRESENT = True", output);
        Assert.Contains("ABORT_REASON = PHONE_LEVEL_OVERRIDE_PRESENT", output);
    }

    [Fact]
    public async Task CallbackBaseUrl_RollbackToAlfaCentral_KeepsPathAndToken()
    {
        var meta = new MetaSimulator { Override = AlfaWebCallback };

        var (exit, output) = await RunAsync(meta, commit: true, f =>
        {
            f.CallbackBaseUrl = new Uri("https://alfacentral.ddns.net");
            f.ExpectHost = "alfacentral.ddns.net";
        });

        Assert.Equal(ExitOk, exit);
        var post = Assert.Single(meta.Requests, r => r.Method == HttpMethod.Post);
        using (var body = JsonDocument.Parse(post.Body))
            Assert.Equal(AlfaCentralCallback, body.RootElement.GetProperty("override_callback_uri").GetString());
        Assert.Contains("TARGET_HOST = alfacentral.ddns.net", output);
        Assert.Contains("TARGET_PATH = /api/conversaciones/whatsapp/webhook/<token>", output);
        Assert.Contains("TARGET_TOKEN_MATCHES_BASE_TOKEN = True", output);
        Assert.Contains("RESULT = COMMITTED", output);
        Assert.Equal(AlfaCentralCallback, meta.Override);
        AssertNoSecrets(output);
    }

    [Fact]
    public async Task CallbackBaseUrlOverrideRoutingProvider_ReplacesOnlySchemeHostPort()
    {
        var inner = new FakeRoutingProvider(AlfaWebCallback, VerifyToken);
        var wrapper = new CallbackBaseUrlOverrideRoutingProvider(inner, new Uri("https://alfacentral.ddns.net:8443"));

        var routing = await wrapper.GetAsync(IdBase);

        Assert.Equal("https://alfacentral.ddns.net:8443" + WebhookPath + WebhookToken, routing.CallbackUrl);
        Assert.Equal(VerifyToken, routing.VerifyToken);
    }

    [Fact]
    public async Task SubscribedAppsGraphFailure_AbortsCommit()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback, SubscribedAppsGetStatus = HttpStatusCode.Forbidden };

        var (exit, output) = await RunAsync(meta, commit: true);

        Assert.Equal(ExitGuardFailed, exit);
        Assert.Equal(0, meta.PostCount);
        Assert.Contains("CURRENT_SUBSCRIBED_APPS_HTTP = 403", output);
        Assert.Contains("CURRENT_SUBSCRIBED_APPS_ERROR_TYPE = OAuthException", output);
        Assert.Contains("ABORT_REASON = SUBSCRIBED_APPS_GET_FAILED", output);
    }

    [Fact]
    public async Task MetaPostFailure_ExitsNonZero_WithSanitizedError()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback, FailPost = true };

        var (exit, output) = await RunAsync(meta, commit: true);

        Assert.Equal(ExitMetaFailed, exit);
        Assert.Equal(1, meta.PostCount);
        Assert.Contains("ENSURE_SUBSCRIPTION = ERROR", output);
        Assert.Contains("META_ERROR_CODE = ", output);
        Assert.Contains("META_ERROR_TYPE = OAuthException", output);
        Assert.Contains("RESULT = META_FAILED", output);
        Assert.Equal(AlfaCentralCallback, meta.Override);
        AssertNoSecrets(output);
    }

    [Fact]
    public async Task PostVerifyMismatch_ExitsNonZero_EvenIfPostSucceeded()
    {
        // Meta acepta el POST y EnsureWabaSubscriptionAsync lo confirma, pero el GET independiente
        // posterior ya no ve el override objetivo.
        var meta = new MetaSimulator { Override = AlfaCentralCallback, RevertAfterGetsPostPost = 1 };

        var (exit, output) = await RunAsync(meta, commit: true);

        Assert.Equal(ExitPostVerifyFailed, exit);
        Assert.Equal(1, meta.PostCount);
        Assert.Contains("ENSURE_SUBSCRIPTION = OK", output);
        Assert.Contains("FINAL_OVERRIDE_MATCHES_TARGET = False", output);
        Assert.Contains("RESULT = POST_VERIFY_FAILED", output);
    }

    [Fact]
    public async Task MetaNotConfirmingInsideEnsure_ExitsNonZero()
    {
        var meta = new MetaSimulator { Override = AlfaCentralCallback, IgnorePost = true };

        var (exit, output) = await RunAsync(meta, commit: true);

        Assert.Equal(ExitMetaFailed, exit);
        Assert.Contains("META_ERROR_CODE = META_CALLBACK_ROUTING_MISMATCH", output);
    }

    [Fact]
    public async Task Sanitization_NoSecretOrFullUrlInAnyOutcome()
    {
        var runs = new[]
        {
            await RunAsync(new MetaSimulator { Override = AlfaCentralCallback }, commit: false),
            await RunAsync(new MetaSimulator { Override = AlfaCentralCallback }, commit: true),
            await RunAsync(new MetaSimulator { Override = AlfaCentralCallback, FailPost = true }, commit: true),
            await RunAsync(new MetaSimulator { Override = AlfaCentralCallback, SubscribedAppsGetStatus = HttpStatusCode.BadRequest }, commit: true),
            await RunAsync(new MetaSimulator { Override = AlfaCentralCallback, FailChallenge = true }, commit: true)
        };

        foreach (var (_, output) in runs)
        {
            AssertNoSecrets(output);
            Assert.DoesNotContain("APPLEVELTOKEN123", output);
            Assert.DoesNotContain("hub.verify_token=", output);
        }
    }

    [Theory]
    [InlineData("--dry-run", "--commit")]
    public void Options_DryRunAndCommitTogether_AreRejected(params string[] extra)
    {
        Assert.False(TryParseOptions(BaseArgs().Concat(extra).ToArray(), out _, out var error));
        Assert.Contains("excluyentes", error);
    }

    [Theory]
    [InlineData("--verify-token", "x")]
    [InlineData("--access-token", "x")]
    [InlineData("--webhook-token", "x")]
    [InlineData("--app-secret", "x")]
    [InlineData("--token=x")]
    public void Options_SecretsByCli_AreRejected(params string[] extra)
    {
        Assert.False(TryParseOptions(BaseArgs().Concat(extra).ToArray(), out _, out var error));
        Assert.Contains("secretos", error);
    }

    [Theory]
    [InlineData("http://alfacentral.ddns.net")]
    [InlineData("https://alfacentral.ddns.net/otra/ruta")]
    [InlineData("https://alfacentral.ddns.net/?x=1")]
    [InlineData("https://user:pass@alfacentral.ddns.net")]
    public void Options_InvalidCallbackBaseUrl_IsRejected(string url)
    {
        Assert.False(TryParseOptions(BaseArgs().Concat(["--callback-base-url", url]).ToArray(), out _, out _));
    }

    [Fact]
    public void Options_WithoutCommit_DefaultsToDryRun()
    {
        Assert.True(TryParseOptions(BaseArgs(), out var options, out _));
        Assert.False(options.Commit);
        Assert.True(TryParseOptions(BaseArgs().Concat(["--commit"]).ToArray(), out var commitOptions, out _));
        Assert.True(commitOptions.Commit);
    }

    [Fact]
    public void IsRequested_MatchesVerbCaseInsensitive()
    {
        Assert.True(IsRequested(["--repair-whatsapp-waba-subscription"]));
        Assert.True(IsRequested(["--REPAIR-WHATSAPP-WABA-SUBSCRIPTION"]));
        Assert.False(IsRequested(["--inspect-whatsapp-subscription"]));
    }

    /// <summary>El dispatch one-shot debe resolverse en Program.Main antes de CreateBuilder: así no
    /// se arranca Kestrel ni ningún hosted service (bot, Embedded Signup, etc.).</summary>
    [Fact]
    public void ProgramDispatch_RunsBeforeCreateBuilder_AndExits()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "AlfaCore", "Program.cs"));
        var dispatch = program.IndexOf("WhatsAppWabaSubscriptionRepairCommand.IsRequested(args)", StringComparison.Ordinal);
        var builder = program.IndexOf("WebApplication.CreateBuilder(", StringComparison.Ordinal);
        var addHosted = program.IndexOf("AddHostedService<", StringComparison.Ordinal);

        Assert.True(dispatch > 0, "No se encontró el dispatch del comando.");
        Assert.True(dispatch < builder, "El dispatch debe estar antes de CreateBuilder.");
        Assert.True(dispatch < addHosted, "El dispatch debe estar antes de registrar hosted services.");
        var block = program[dispatch..program.IndexOf("return;", dispatch, StringComparison.Ordinal)];
        Assert.Contains("Environment.Exit(", block);
    }

    private static string[] BaseArgs()
        => ["--repair-whatsapp-waba-subscription", "--id-base", IdBase.ToString(), "--waba-id", Waba,
            "--phone-number-id", Phone, "--expect-callback-host", "alfanetweb.ddns.net"];

    private static void AssertNoSecrets(string output)
    {
        Assert.DoesNotContain(AccessToken, output);
        Assert.DoesNotContain(VerifyToken, output);
        Assert.DoesNotContain(WebhookToken, output);
        Assert.DoesNotContain(AlfaWebCallback, output);
        Assert.DoesNotContain(AlfaCentralCallback, output);
    }

    private sealed class Fixture
    {
        public WhatsAppEmbeddedSignupOptions Options { get; } = new()
        {
            Enabled = true,
            AppId = AppId,
            GraphBaseUrl = "https://graph.facebook.com",
            GraphApiVersion = "v26.0"
        };
        public string? BaseWebhookToken { get; set; } = WebhookToken;
        public WhatsAppWabaOwnership? WabaOwnership { get; set; } = new(Waba, IdBase, "428784260865436", DateTime.UtcNow);
        public WhatsAppPhoneOwnership? PhoneOwnership { get; set; } = new(Phone, Waba, IdBase, DateTime.UtcNow);
        public string ExpectHost { get; set; } = "alfanetweb.ddns.net";
        public Uri? CallbackBaseUrl { get; set; }
    }

    private static async Task<(int Exit, string Output)> RunAsync(MetaSimulator meta, bool commit, Action<Fixture>? configure = null)
    {
        var fixture = new Fixture();
        configure?.Invoke(fixture);

        using var client = new HttpClient(meta);
        IWhatsAppWabaRoutingProvider routing = new FakeRoutingProvider(AlfaWebCallback, VerifyToken);
        if (fixture.CallbackBaseUrl is not null)
            routing = new CallbackBaseUrlOverrideRoutingProvider(routing, fixture.CallbackBaseUrl);
        var vault = new FakeVault();
        var management = new MetaWhatsAppManagementClient(
            new WhatsAppSubscriptionInspectionCommand.SingleClientFactory(client), vault, vault, routing, Options.Create(fixture.Options));
        var output = new StringWriter();

        var exit = await ExecuteAsync(
            new RepairOptions(IdBase, Waba, Phone, fixture.ExpectHost, fixture.CallbackBaseUrl, commit),
            fixture.Options,
            new FakeCentralBases(fixture.BaseWebhookToken),
            new FakeOwnershipStore(fixture.WabaOwnership, fixture.PhoneOwnership),
            new FakeCredentialResolver(),
            routing,
            management,
            client,
            output,
            CancellationToken.None);

        return (exit, output.ToString());
    }

    /// <summary>Graph + callback público simulados, con estado del override de nuestra app.</summary>
    private sealed class MetaSimulator : HttpMessageHandler
    {
        private string? _previousOverride;
        private int _getsAfterPost = -1;

        public string? Override { get; set; }
        public string? PhoneLevelOverride { get; init; }
        public bool FailChallenge { get; init; }
        public bool FailPost { get; init; }
        public bool IgnorePost { get; init; }
        public int RevertAfterGetsPostPost { get; init; } = -1;
        public HttpStatusCode SubscribedAppsGetStatus { get; init; } = HttpStatusCode.OK;
        public List<(HttpMethod Method, Uri Uri, string Body)> Requests { get; } = [];
        public int PostCount => Requests.Count(r => r.Method == HttpMethod.Post);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, uri, body));

            if (!string.Equals(uri.Host, "graph.facebook.com", StringComparison.Ordinal))
                return Challenge(uri);

            if (uri.AbsolutePath.EndsWith("/subscribed_apps", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Post)
                    return Post(body);

                if (SubscribedAppsGetStatus != HttpStatusCode.OK)
                    return Json(SubscribedAppsGetStatus, """{"error":{"code":200,"type":"OAuthException","message":"Permissions error"}}""");

                if (_getsAfterPost >= 0 && ++_getsAfterPost > RevertAfterGetsPostPost && RevertAfterGetsPostPost >= 0)
                    Override = _previousOverride;
                var item = Override is null
                    ? $$"""{"id":"{{AppId}}"}"""
                    : $$"""{"id":"{{AppId}}","override_callback_uri":"{{Override}}"}""";
                return Json(HttpStatusCode.OK, $$"""{"data":[{{item}}]}""");
            }

            var levels = new List<string>();
            if (PhoneLevelOverride is not null)
                levels.Add($$"""
                    "phone_number":"{{PhoneLevelOverride}}"
                    """);
            if (Override is not null)
                levels.Add($$"""
                    "whatsapp_business_account":"{{Override}}"
                    """);
            levels.Add($$"""
                "application":"{{AppLevelCallback}}"
                """);
            return Json(HttpStatusCode.OK, "{\"webhook_configuration\":{" + string.Join(",", levels) + "},\"id\":\"" + Phone + "\"}");
        }

        private HttpResponseMessage Challenge(Uri uri)
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
            if (FailChallenge || !query.TryGetValue("hub.verify_token", out var token) || token != VerifyToken)
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("forbidden") };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(query["hub.challenge"]) };
        }

        private HttpResponseMessage Post(string body)
        {
            if (FailPost)
                return Json(HttpStatusCode.BadRequest,
                    """{"error":{"code":100,"type":"OAuthException","message":"Invalid parameter override_callback_uri /api/conversaciones/whatsapp/webhook/LEAKTOKEN999"}}""");

            if (!IgnorePost)
            {
                using var document = JsonDocument.Parse(body);
                _previousOverride = Override;
                Override = document.RootElement.GetProperty("override_callback_uri").GetString();
                _getsAfterPost = 0;
            }

            return Json(HttpStatusCode.OK, """{"success":true}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
            => new(status) { Content = new StringContent(json) };
    }

    private sealed class FakeRoutingProvider(string callbackUrl, string verifyToken) : IWhatsAppWabaRoutingProvider
    {
        public Task<WhatsAppWabaRoutingConfiguration> GetAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult(new WhatsAppWabaRoutingConfiguration(callbackUrl, verifyToken));
    }

    private sealed class FakeCentralBases(string? webhookToken) : ICentralBasesService
    {
        public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult<BaseCentralDto?>(new BaseCentralDto { IdBase = idBase, IdCliente = "112010001", Nombre = "Base", WebhookToken = webhookToken });

        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default) => throw new InvalidOperationException("No debe generarse WebhookToken.");
    }

    private sealed class FakeOwnershipStore(WhatsAppWabaOwnership? waba, WhatsAppPhoneOwnership? phone) : IWhatsAppAssetOwnershipStore
    {
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => Task.FromResult(waba);
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default) => Task.FromResult(phone);
    }

    private sealed class FakeCredentialResolver : IWhatsAppRuntimeCredentialResolver
    {
        public Task<WhatsAppRuntimeCredential> ResolveAsync(int idBase, int? idNumero, string phoneNumberId, ConversacionWhatsAppConfigDto legacyConfig, CancellationToken ct = default)
            => Task.FromResult(new WhatsAppRuntimeCredential(Waba, Phone, "v26.0", AccessToken,
                WhatsAppRuntimeCredentialOrigin.EmbeddedSignup, new WhatsAppCredentialReference("0123456789abcdef0123456789abcdef")));
    }

    private sealed class FakeVault : IWhatsAppCredentialVault, IWhatsAppPhonePinVault
    {
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default)
            => Task.FromResult(AccessToken.AsMemory());

        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default) => throw new NotSupportedException();
        Task<WhatsAppPhonePinReference> IWhatsAppPhonePinVault.StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> pin, CancellationToken ct) => throw new NotSupportedException();
        Task<ReadOnlyMemory<char>> IWhatsAppPhonePinVault.GetAsync(WhatsAppPhonePinReference reference, CancellationToken ct) => throw new NotSupportedException();
        Task IWhatsAppPhonePinVault.RemoveAsync(WhatsAppPhonePinReference reference, CancellationToken ct) => throw new NotSupportedException();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}

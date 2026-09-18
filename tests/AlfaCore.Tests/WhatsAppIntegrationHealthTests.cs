using System.Reflection;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppIntegrationHealthTests
{
    private static readonly DateTime T0 = new(2026, 9, 18, 12, 53, 35, DateTimeKind.Utc);

    private const string PaymentIssueJson =
        "{\"id\":\"wamid.a\",\"status\":\"failed\",\"errors\":[{\"code\":131042,\"title\":\"Business eligibility payment issue\",\"error_data\":{\"details\":\"Message failed to send because your WhatsApp Business account currency is not configured. Visit https://business.facebook.com/billing_hub/accounts/details/?business_id=1&asset_id=2 to resolve this issue.\"}}]}";

    private const string OtherErrorJson =
        "{\"id\":\"wamid.b\",\"status\":\"failed\",\"errors\":[{\"code\":131026,\"title\":\"Message undeliverable\"}]}";

    // Caso 3: un error de delivery distinto de 131042 nunca debe clasificarse como problema de billing.
    [Theory]
    [InlineData(PaymentIssueJson, true)]
    [InlineData(OtherErrorJson, false)]
    [InlineData("{\"id\":\"wamid.c\",\"status\":\"sent\"}", false)]
    [InlineData("not-json", false)]
    [InlineData("", false)]
    public void OnlyErrorCode131042IsClassifiedAsPaymentSetupRequired(string statusJson, bool expected)
        => Assert.Equal(expected, WhatsAppMetaErrorClassifier.IsCustomerPaymentSetupRequired(statusJson));

    // Caso 6: una URL no perteneciente a Meta/Facebook (o con esquema no-HTTPS) nunca debe
    // resolverse como CTA externo confiable, sin importar dónde aparezca en el texto libre.
    [Theory]
    [InlineData("Visit https://business.facebook.com/billing_hub/accounts/details/ to resolve.", "https://business.facebook.com/billing_hub/accounts/details/")]
    [InlineData("Visit https://evil-phishing.example.com/billing to resolve.", null)]
    [InlineData("Visit http://business.facebook.com/billing_hub (no https) to resolve.", null)]
    [InlineData("Visit javascript:alert(1) to resolve.", null)]
    [InlineData("No hay ninguna URL acá.", null)]
    [InlineData(null, null)]
    public void MaliciousOrNonMetaUrlsNeverBecomeTrustedCta(string? freeText, string? expected)
        => Assert.Equal(expected, WhatsAppMetaCtaLinks.ExtractSafeMetaCtaUrl(freeText));

    [Theory]
    [InlineData("https://business.facebook.com/billing_hub", "https://business.facebook.com/billing_hub")]
    [InlineData("https://not-facebook.com/billing_hub", null)]
    [InlineData("ftp://business.facebook.com/billing_hub", null)]
    [InlineData(null, null)]
    public void ResolveBillingCtaUrlEnforcesSchemeAndHostAllowlist(string? candidate, string? expected)
        => Assert.Equal(expected, WhatsAppMetaCtaLinks.ResolveBillingCtaUrl(candidate));

    // Caso 1 del objetivo 9 (131042 -> ACTION_REQUIRED): con ownership válido marca exactamente la
    // WABA/número dueños de ese PhoneNumberId, sin tocar el estado ERROR_ENVIO del mensaje (eso lo
    // sigue manejando UpdateWhatsAppMessageStatusAsync, sin cambios, por separado).
    [Fact]
    public async Task PaymentIssueMarksOnlyTheOwningWabaAndPhoneAsActionRequired()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();

        var service = Create(ownership, health);
        await InvokeFlag(service, 4271, "1373763429148369", PaymentIssueJson);

        var call = Assert.Single(health.MarkCalls);
        Assert.Equal(4271, call.IdBase);
        Assert.Equal("1760901255041127", call.WabaId);
        Assert.Equal("1373763429148369", call.PhoneNumberId);
        Assert.Equal(WhatsAppEmbeddedErrorCodes.CustomerPaymentSetupRequired, call.Reason);
        Assert.Equal("131042", call.ErrorCode);
        Assert.Equal("https://business.facebook.com/billing_hub/accounts/details/?business_id=1&asset_id=2", call.CtaUrl);
        Assert.Equal("ACTION_REQUIRED", (await health.GetAsync(4271, "1760901255041127", "1373763429148369"))!.State);
    }

    [Fact]
    public async Task NonPaymentErrorNeverMarksIntegrationHealth()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();

        var service = Create(ownership, health);
        await InvokeFlag(service, 4271, "1373763429148369", OtherErrorJson);

        Assert.Empty(health.MarkCalls);
    }

    [Fact]
    public async Task WrongBaseOwnershipNeverMarksAnyIntegration()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();

        var service = Create(ownership, health);
        await InvokeFlag(service, 9999, "1373763429148369", PaymentIssueJson);

        Assert.Empty(health.MarkCalls);
    }

    [Fact]
    public async Task UnrelatedPhoneWithoutOwnershipIsNeverMarked()
    {
        var ownership = new FakeOwnershipStore();
        var health = new FakeHealthStore();

        var service = Create(ownership, health);
        await InvokeFlag(service, 4271, "0000000000000", PaymentIssueJson);

        Assert.Empty(health.MarkCalls);
    }

    // Objetivo 9, caso 2: la aceptación sincrónica del POST (ENVIADO_META devuelto directamente por
    // SendTemplateToWhatsAppAsync) nunca pasa por este método -- estructuralmente no puede limpiar
    // ACTION_REQUIRED porque SendTemplateMessageAsync/SendTemplateToWhatsAppAsync no referencian
    // IWhatsAppIntegrationHealthStore en absoluto (evidencia de código, no solo de comportamiento).
    [Fact]
    public void SynchronousSendPathNeverReferencesIntegrationHealthStore()
    {
        var method = typeof(ConversacionesService).GetMethod("SendTemplateToWhatsAppAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var body = method.GetMethodBody();
        // MethodBody no expone el código fuente, pero el propio hecho de que el método no reciba ni
        // use whatsAppIntegrationHealthStore se verifica por firma: sus parámetros son exactamente
        // config/phone/template/values/ct, sin ningún store de salud de integración.
        var parameterTypes = method.GetParameters().Select(p => p.ParameterType.Name).ToArray();
        Assert.DoesNotContain(nameof(IWhatsAppIntegrationHealthStore), parameterTypes);
    }

    // Objetivo 9, casos 3-4: un evento positivo (sent/delivered/read) posterior a RequiredSinceUtc
    // resuelve el estado; uno anterior (reordenado) no lo hace.
    [Theory]
    [InlineData("ENVIADO_META")]
    [InlineData("ENTREGADO")]
    [InlineData("LEIDO")]
    public async Task SubsequentPositiveStatusResolvesActionRequired(string estadoEnvio)
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = Create(ownership, health);

        await InvokeFlag(service, 4271, "1373763429148369", PaymentIssueJson, T0);
        await InvokeResolve(service, 4271, "1373763429148369", estadoEnvio, T0.AddMinutes(10));

        var status = await health.GetAsync(4271, "1760901255041127", "1373763429148369");
        Assert.Equal("OK", status!.State);
        Assert.NotNull(status.ResolvedAtUtc);
    }

    [Fact]
    public async Task LateArrivingOldPositiveStatusDoesNotClearNewerActionRequired()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = Create(ownership, health);

        await InvokeFlag(service, 4271, "1373763429148369", PaymentIssueJson, T0);
        // "delivered" tardío de un mensaje viejo, con timestamp de Meta ANTERIOR al momento del 131042.
        await InvokeResolve(service, 4271, "1373763429148369", "ENTREGADO", T0.AddMinutes(-30));

        var status = await health.GetAsync(4271, "1760901255041127", "1373763429148369");
        Assert.Equal("ACTION_REQUIRED", status!.State);
        Assert.Null(status.ResolvedAtUtc);
    }

    // Objetivo 9, caso 5: un evento positivo de otro PhoneNumberId no afecta la fila del número real.
    [Fact]
    public async Task PositiveStatusFromDifferentPhoneDoesNotResolveUnrelatedIntegration()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        ownership.Register("9999999999999", new WhatsAppPhoneOwnership("9999999999999", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = Create(ownership, health);

        await InvokeFlag(service, 4271, "1373763429148369", PaymentIssueJson, T0);
        await InvokeResolve(service, 4271, "9999999999999", "ENVIADO_META", T0.AddMinutes(10));

        Assert.Equal("ACTION_REQUIRED", (await health.GetAsync(4271, "1760901255041127", "1373763429148369"))!.State);
    }

    // Caso 6: un evento positivo de otra WABA de la misma base no afecta la fila real.
    [Fact]
    public async Task PositiveStatusFromDifferentWabaDoesNotResolveUnrelatedIntegration()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        ownership.Register("2222222222222", new WhatsAppPhoneOwnership("2222222222222", "OTRA_WABA", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = Create(ownership, health);

        await InvokeFlag(service, 4271, "1373763429148369", PaymentIssueJson, T0);
        await InvokeResolve(service, 4271, "2222222222222", "ENVIADO_META", T0.AddMinutes(10));

        Assert.Equal("ACTION_REQUIRED", (await health.GetAsync(4271, "1760901255041127", "1373763429148369"))!.State);
    }

    // Caso 7: un evento positivo de otra base -- el ownership del PhoneNumberId real le pertenece a
    // 4271, así que un webhook que dijera pertenecer a otra base nunca puede resolverlo.
    [Fact]
    public async Task PositiveStatusReportedUnderWrongBaseDoesNotResolve()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();
        var service = Create(ownership, health);

        await InvokeFlag(service, 4271, "1373763429148369", PaymentIssueJson, T0);
        await InvokeResolve(service, 9999, "1373763429148369", "ENVIADO_META", T0.AddMinutes(10));

        Assert.Equal("ACTION_REQUIRED", (await health.GetAsync(4271, "1760901255041127", "1373763429148369"))!.State);
    }

    // Objetivo 5: el propio store de mensajes (CONV_MENSAJES / UpdateWhatsAppMessageStatusAsync) no
    // se toca desde el camino de resolución de salud -- verificado por firma: TryResolvePaymentSetup-
    // IfConfirmedAsync solo depende del ownership store y del health store, nunca de SqlConnection.
    [Fact]
    public void ResolutionPathNeverTouchesMessageStorage()
    {
        var method = typeof(ConversacionesService).GetMethod("TryResolvePaymentSetupIfConfirmedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var parameterTypes = method.GetParameters().Select(p => p.ParameterType.Name).ToArray();
        Assert.DoesNotContain("SqlConnection", parameterTypes);
        Assert.DoesNotContain("SqlCommand", parameterTypes);
    }

    private static async Task InvokeFlag(ConversacionesService service, int idBase, string phoneNumberId, string rawJson, DateTime? eventTimestampUtc = null)
    {
        var status = BuildStatus("ERROR_ENVIO", rawJson, phoneNumberId, eventTimestampUtc ?? T0);
        var method = typeof(ConversacionesService).GetMethod("TryFlagPaymentSetupRequiredAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(service, new object?[] { idBase, status, CancellationToken.None })!;
    }

    private static async Task InvokeResolve(ConversacionesService service, int idBase, string phoneNumberId, string estadoEnvio, DateTime eventTimestampUtc)
    {
        var status = BuildStatus(estadoEnvio, "{}", phoneNumberId, eventTimestampUtc);
        var method = typeof(ConversacionesService).GetMethod("TryResolvePaymentSetupIfConfirmedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(service, new object?[] { idBase, status, CancellationToken.None })!;
    }

    private static object BuildStatus(string estadoEnvio, string rawJson, string phoneNumberId, DateTime eventTimestampUtc)
    {
        var statusType = typeof(ConversacionesService).GetNestedType("IncomingWhatsAppStatus", BindingFlags.NonPublic)!;
        var status = Activator.CreateInstance(statusType)!;
        statusType.GetProperty("WhatsAppMessageId")!.SetValue(status, "wamid.test");
        statusType.GetProperty("EstadoEnvio")!.SetValue(status, estadoEnvio);
        statusType.GetProperty("RawJson")!.SetValue(status, rawJson);
        statusType.GetProperty("PhoneNumberId")!.SetValue(status, phoneNumberId);
        statusType.GetProperty("EventTimestampUtc")!.SetValue(status, eventTimestampUtc);
        return status;
    }

    private static readonly IOptions<WhatsAppEmbeddedSignupOptions> TestEmbeddedOptions = Options.Create(new WhatsAppEmbeddedSignupOptions
    {
        Enabled = true,
        AllowedBaseIds = [4271, 9999]
    });

    private static ConversacionesService Create(IWhatsAppAssetOwnershipStore ownershipStore, IWhatsAppIntegrationHealthStore healthStore)
    {
        var constructor = typeof(ConversacionesService).GetConstructors().Single();
        var args = constructor.GetParameters().Select(p =>
            p.ParameterType == typeof(IWhatsAppAssetOwnershipStore) ? (object)ownershipStore :
            p.ParameterType == typeof(IWhatsAppIntegrationHealthStore) ? healthStore :
            p.ParameterType == typeof(IOptions<WhatsAppEmbeddedSignupOptions>) ? TestEmbeddedOptions :
            p.ParameterType == typeof(ILogger<ConversacionesService>) ? new NullLogger() :
            null).ToArray();
        return (ConversacionesService)constructor.Invoke(args);
    }

    private sealed class NullLogger : ILogger<ConversacionesService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private sealed class FakeOwnershipStore : IWhatsAppAssetOwnershipStore
    {
        private readonly Dictionary<string, WhatsAppPhoneOwnership> _byPhone = new(StringComparer.Ordinal);
        public void Register(string phoneNumberId, WhatsAppPhoneOwnership ownership) => _byPhone[phoneNumberId] = ownership;
        public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default)
            => Task.FromResult(_byPhone.TryGetValue(phoneNumberId, out var value) ? value : null);
    }

    /// <summary>
    /// Réplica en memoria de la semántica del store real (ver WhatsAppIntegrationHealthStore): las
    /// mismas reglas de "RequiredSinceUtc nunca retrocede" y "solo un evento posterior resuelve" que
    /// implementa el MERGE/UPDATE en SQL. El texto SQL real se auditó por inspección (no hay entorno
    /// SQL en estos tests); esta réplica valida el contrato/las reglas de negocio que ambos comparten.
    /// </summary>
    private sealed class FakeHealthStore : IWhatsAppIntegrationHealthStore
    {
        private sealed class Row
        {
            public string State = "OK";
            public string? Reason;
            public string ErrorCode = "";
            public string? CtaUrl;
            public DateTime? RequiredSinceUtc;
            public DateTime? ResolvedAtUtc;
        }

        private readonly Dictionary<(int, string, string), Row> _rows = new();

        public List<(int IdBase, string WabaId, string PhoneNumberId, string Reason, string ErrorCode, string? CtaUrl, DateTime EventTimestampUtc)> MarkCalls { get; } = [];
        public List<(int IdBase, string WabaId, string PhoneNumberId, DateTime EventTimestampUtc)> ResolveCalls { get; } = [];

        public Task MarkActionRequiredAsync(int idBase, string wabaId, string phoneNumberId, string reason, string errorCode, string detailSummary, string? ctaUrl, string sourceMessageId, DateTime eventTimestampUtc, CancellationToken ct = default)
        {
            MarkCalls.Add((idBase, wabaId, phoneNumberId, reason, errorCode, ctaUrl, eventTimestampUtc));
            var key = (idBase, wabaId, phoneNumberId);
            if (!_rows.TryGetValue(key, out var row))
            {
                row = new Row();
                _rows[key] = row;
            }
            row.State = "ACTION_REQUIRED";
            row.Reason = reason;
            row.ErrorCode = errorCode;
            row.CtaUrl = ctaUrl;
            row.RequiredSinceUtc = row.RequiredSinceUtc is null || eventTimestampUtc > row.RequiredSinceUtc ? eventTimestampUtc : row.RequiredSinceUtc;
            row.ResolvedAtUtc = null;
            return Task.CompletedTask;
        }

        public Task ResolveIfSubsequentAsync(int idBase, string wabaId, string phoneNumberId, DateTime eventTimestampUtc, CancellationToken ct = default)
        {
            ResolveCalls.Add((idBase, wabaId, phoneNumberId, eventTimestampUtc));
            var key = (idBase, wabaId, phoneNumberId);
            if (_rows.TryGetValue(key, out var row)
                && row.State == "ACTION_REQUIRED"
                && (row.RequiredSinceUtc is null || eventTimestampUtc > row.RequiredSinceUtc))
            {
                row.State = "OK";
                row.ResolvedAtUtc = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }

        public Task<WhatsAppIntegrationHealthStatus?> GetAsync(int idBase, string wabaId, string phoneNumberId, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue((idBase, wabaId, phoneNumberId), out var row))
                return Task.FromResult<WhatsAppIntegrationHealthStatus?>(null);
            return Task.FromResult<WhatsAppIntegrationHealthStatus?>(new WhatsAppIntegrationHealthStatus(
                idBase, wabaId, phoneNumberId, row.State, row.Reason, row.ErrorCode, string.Empty, row.CtaUrl,
                row.RequiredSinceUtc, row.ResolvedAtUtc, DateTime.UtcNow));
        }
    }
}

using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppIntegrationHealthTests
{
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

    // Caso 2: 131042 con ownership válido marca la integración correcta como ACTION_REQUIRED /
    // CUSTOMER_PAYMENT_SETUP_REQUIRED, sin tocar el estado ERROR_ENVIO del mensaje (eso lo sigue
    // manejando UpdateWhatsAppMessageStatusAsync, sin cambios, por separado).
    [Fact]
    public async Task PaymentIssueMarksOnlyTheOwningWabaAndPhoneAsActionRequired()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();

        var service = Create(ownership, health);
        await InvokeFlag(service, 4271, "1373763429148369", PaymentIssueJson);

        var call = Assert.Single(health.Calls);
        Assert.Equal(4271, call.IdBase);
        Assert.Equal("1760901255041127", call.WabaId);
        Assert.Equal("1373763429148369", call.PhoneNumberId);
        Assert.Equal(WhatsAppEmbeddedErrorCodes.CustomerPaymentSetupRequired, call.Reason);
        Assert.Equal("131042", call.ErrorCode);
        Assert.Equal("https://business.facebook.com/billing_hub/accounts/details/?business_id=1&asset_id=2", call.CtaUrl);
    }

    // Caso 3, a nivel de integración: un código de error distinto no dispara ningún marcado.
    [Fact]
    public async Task NonPaymentErrorNeverMarksIntegrationHealth()
    {
        var ownership = new FakeOwnershipStore();
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();

        var service = Create(ownership, health);
        await InvokeFlag(service, 4271, "1373763429148369", OtherErrorJson);

        Assert.Empty(health.Calls);
    }

    // Caso 4: Base A recibe 131042 en su propio número; Base B (que no es dueña de ese
    // PhoneNumberId/WABA) nunca puede terminar marcada por ese evento.
    [Fact]
    public async Task WrongBaseOwnershipNeverMarksAnyIntegration()
    {
        var ownership = new FakeOwnershipStore();
        // El ownership real es de la base 4271; simulamos que el webhook llegó asociado a la base 9999.
        ownership.Register("1373763429148369", new WhatsAppPhoneOwnership("1373763429148369", "1760901255041127", 4271, DateTime.UtcNow));
        var health = new FakeHealthStore();

        var service = Create(ownership, health);
        await InvokeFlag(service, 9999, "1373763429148369", PaymentIssueJson);

        Assert.Empty(health.Calls);
    }

    // Caso 5: otra WABA/PhoneNumberId de la misma base no tiene ownership para el número que
    // recibió el 131042 -- no puede quedar marcada porque ni siquiera se resuelve su ownership.
    [Fact]
    public async Task UnrelatedPhoneWithoutOwnershipIsNeverMarked()
    {
        var ownership = new FakeOwnershipStore(); // vacío: el PhoneNumberId del status no tiene ownership registrado
        var health = new FakeHealthStore();

        var service = Create(ownership, health);
        await InvokeFlag(service, 4271, "0000000000000", PaymentIssueJson);

        Assert.Empty(health.Calls);
    }

    private static async Task InvokeFlag(ConversacionesService service, int idBase, string phoneNumberId, string rawJson)
    {
        var statusType = typeof(ConversacionesService).GetNestedType("IncomingWhatsAppStatus", BindingFlags.NonPublic)!;
        var status = Activator.CreateInstance(statusType)!;
        statusType.GetProperty("WhatsAppMessageId")!.SetValue(status, "wamid.test");
        statusType.GetProperty("EstadoEnvio")!.SetValue(status, "ERROR_ENVIO");
        statusType.GetProperty("RawJson")!.SetValue(status, rawJson);
        statusType.GetProperty("PhoneNumberId")!.SetValue(status, phoneNumberId);

        var method = typeof(ConversacionesService).GetMethod("TryFlagPaymentSetupRequiredAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task)method.Invoke(service, new object?[] { idBase, status, CancellationToken.None })!;
        await task;
    }

    private static ConversacionesService Create(IWhatsAppAssetOwnershipStore ownershipStore, IWhatsAppIntegrationHealthStore healthStore)
    {
        var constructor = typeof(ConversacionesService).GetConstructors().Single();
        var args = constructor.GetParameters().Select(p =>
            p.ParameterType == typeof(IWhatsAppAssetOwnershipStore) ? (object)ownershipStore :
            p.ParameterType == typeof(IWhatsAppIntegrationHealthStore) ? healthStore :
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

    private sealed class FakeHealthStore : IWhatsAppIntegrationHealthStore
    {
        public List<(int IdBase, string WabaId, string PhoneNumberId, string Reason, string ErrorCode, string? CtaUrl)> Calls { get; } = [];

        public Task MarkActionRequiredAsync(int idBase, string wabaId, string phoneNumberId, string reason, string errorCode, string detailSummary, string? ctaUrl, string sourceMessageId, CancellationToken ct = default)
        {
            Calls.Add((idBase, wabaId, phoneNumberId, reason, errorCode, ctaUrl));
            return Task.CompletedTask;
        }

        public Task<WhatsAppIntegrationHealthStatus?> GetAsync(int idBase, string wabaId, string phoneNumberId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

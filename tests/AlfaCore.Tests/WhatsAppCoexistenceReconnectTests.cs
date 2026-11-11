using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppCoexistenceReconnectTests
{
    [Theory]
    [InlineData("ACCOUNT_OFFBOARDED", "RECOVERING")]
    [InlineData("ACCOUNT_RECONNECTED", "CONNECTED")]
    [InlineData("PARTNER_REMOVED", "DISCONNECTED")]
    [InlineData("PARTNER_APP_UNINSTALLED", "DISCONNECTED")]
    public void AccountEventWithoutPhone_UsesEntryWabaAndMetaTimestamp(string eventName, string state)
    {
        using var payload = JsonDocument.Parse("""
            {"object":"whatsapp_business_account","entry":[{"id":"123456","time":1794416400,
              "changes":[{"field":"account_update","value":{"event":"EVENT_NAME",
                "data":{"disconnection_info":{"reason":"PRIMARY_INACTIVITY","initiated_by":"SYSTEM"}}}}]}]}
            """.Replace("EVENT_NAME", eventName, StringComparison.Ordinal));
        var accountEvent = Assert.Single(WhatsAppCoexistenceAccountEvents.Parse(payload.RootElement));
        Assert.Equal("123456", accountEvent.WabaId);
        Assert.Equal(state, accountEvent.State);
        Assert.Equal("PRIMARY_INACTIVITY", accountEvent.Reason);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794416400).UtcDateTime, accountEvent.TimestampUtc);
        Assert.True(WhatsAppCoexistenceAccountEvents.IsAccountOnly(payload.RootElement));
        Assert.Empty(ConversacionesService.ExtractWhatsAppPhoneNumberIds(payload.RootElement));
    }

    [Fact]
    public void MixedEnvelope_DoesNotBypassPhoneOwnershipGuard()
    {
        using var payload = JsonDocument.Parse("""
            {"entry":[{"id":"123456","time":1794416400,"changes":[
              {"field":"account_update","value":{"event":"ACCOUNT_RECONNECTED"}},
              {"field":"messages","value":{"messages":[]}}]}]}
            """);
        Assert.False(WhatsAppCoexistenceAccountEvents.IsAccountOnly(payload.RootElement));
    }

    [Theory]
    [InlineData("{\"entry\":[null]}")]
    [InlineData("{\"entry\":[{\"id\":\"123\",\"changes\":[null]}]}")]
    [InlineData("{\"entry\":[{\"id\":\"123\",\"changes\":[{\"field\":\"account_update\",\"value\":null}]}]}")]
    public void UnknownOrMalformedAccountEnvelope_DoesNotBecomeTrusted(string raw)
    {
        using var payload = JsonDocument.Parse(raw);
        Assert.Empty(WhatsAppCoexistenceAccountEvents.Parse(payload.RootElement));
        Assert.False(WhatsAppCoexistenceAccountEvents.IsAccountOnly(payload.RootElement));
    }

    [Theory]
    [InlineData("PARTNER_APP_INSTALLED")]
    [InlineData("VERIFIED_ACCOUNT")]
    [InlineData("VOLUME_BASED_PRICING_TIER_UPDATE")]
    public void OtherAccountUpdateEvents_AreRecognizedForRoutingButDoNotChangeState(string eventName)
    {
        // Al suscribir account_update, Meta manda estos (y otros) por el mismo canal. Tienen que poder
        // rutearse y contestarse 200; si no, Meta reintenta y puede frenar los avisos de toda la app.
        var raw = """
            {"object":"whatsapp_business_account","entry":[{"id":"123456","time":1794416400,
              "changes":[{"field":"account_update","value":{"event":"EVENT_NAME"}}]}]}
            """.Replace("EVENT_NAME", eventName, StringComparison.Ordinal);
        using var payload = JsonDocument.Parse(raw);
        Assert.Empty(WhatsAppCoexistenceAccountEvents.Parse(payload.RootElement));
        Assert.True(WhatsAppCoexistenceAccountEvents.IsAccountOnly(payload.RootElement));
        Assert.Equal(["123456"], WhatsAppCoexistenceAccountEvents.ExtractWabaIds(raw));
        Assert.Equal(["123456"], WhatsAppCoexistenceAccountEvents.ExtractAccountUpdateWabaIds(payload.RootElement));
    }

    [Fact]
    public void MessagesPayload_HasNoAccountUpdateWabas()
    {
        const string raw = """{"entry":[{"id":"123456","changes":[{"field":"messages","value":{"metadata":{"phone_number_id":"999"}}}]}]}""";
        Assert.Empty(WhatsAppCoexistenceAccountEvents.ExtractWabaIds(raw));
    }

    [Fact]
    public void AccountOwnerSelectsEmbeddedSecret_AndCrossTenantIsRejected()
    {
        var options = new WhatsAppEmbeddedSignupOptions { Enabled = true, AppSecret = "embedded-secret" };
        var owner = new WhatsAppWabaOwnership("123456", 84, "business", DateTime.UtcNow);
        var accepted = Program.ResolveWhatsAppWebhookAppSecret(options, 84, [], false, true, "legacy-secret", [owner]);
        Assert.Equal(Program.WhatsAppWebhookSecretOutcome.EmbeddedSignup, accepted.Outcome);
        Assert.Equal("embedded-secret", accepted.Secret);
        var rejected = Program.ResolveWhatsAppWebhookAppSecret(options, 106, [], false, true, "legacy-secret", [owner]);
        Assert.Equal(Program.WhatsAppWebhookSecretOutcome.RejectCrossTenant, rejected.Outcome);
        Assert.Empty(rejected.Secret);
    }
}

using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;
using WindowMessage = AlfaCore.Services.ConversacionesService.WhatsAppWindowMessage;

namespace AlfaCore.Tests;

public sealed class WhatsAppSendWindowTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 43, 0);
    private const string Recipient = "5492944202289";
    private const string OldRecipient = "5492944371611";
    private const string LineA = "111111111";
    private const string LineB = "222222222";

    private static WindowMessage Incoming(long id, DateTime time, string? line = LineA, string? source = null)
        => new()
        {
            IdMensaje = id, FechaHora = time, Direction = "ENTRANTE", SistemaAutor = source, TelefonoWhatsApp = Recipient,
            PayloadJson = line is null ? "{}" : JsonSerializer.Serialize(new { metadata = new { phone_number_id = line } })
        };

    private static WindowMessage Outgoing(long id, DateTime time, string state = "ERROR_ENVIO", string line = LineA, int code = 131047)
        => new()
        {
            IdMensaje = id, FechaHora = time, Direction = "SALIENTE", EstadoEnvio = state, TelefonoWhatsApp = Recipient,
            PayloadJson = JsonSerializer.Serialize(new { phone_number_id = line, errors = new[] { new { code } } })
        };

    private static ConversacionWhatsAppVentanaDto Evaluate(params WindowMessage[] messages)
        => ConversacionesService.EvaluateWhatsAppSendWindow(messages, LineA, LineA, Now, Recipient);

    [Fact]
    public void ReportedCase_ClientReplyAndRejectedSendHaveDifferentRecipients()
    {
        var reply = Incoming(89146, Now.AddMinutes(-1));
        reply.PayloadJson = JsonSerializer.Serialize(new
        {
            metadata = new { phone_number_id = LineA },
            message = new { from = Recipient, text = new { body = "perfecto gracias" } }
        });
        var failed = Outgoing(89147, Now);
        failed.TelefonoWhatsApp = OldRecipient;
        failed.PayloadJson = JsonSerializer.Serialize(new { recipient_id = OldRecipient, errors = new[] { new { code = 131047 } } });

        Assert.False(ConversacionesService.EvaluateWhatsAppSendWindow([reply, failed], LineA, LineA, Now, OldRecipient).Activa);
        var corrected = ConversacionesService.EvaluateWhatsAppSendWindow([reply, failed], LineA, LineA, Now, Recipient);
        Assert.True(corrected.Activa);
        Assert.False(corrected.RechazadaPorMeta);
    }

    [Fact]
    public void PayloadSenderOverridesStaleMessagePhone()
    {
        var reply = Incoming(1, Now.AddMinutes(-1));
        reply.TelefonoWhatsApp = OldRecipient;
        reply.PayloadJson = JsonSerializer.Serialize(new { metadata = new { phone_number_id = LineA }, message = new { from = Recipient } });
        Assert.True(Evaluate(reply).Activa);
        Assert.False(ConversacionesService.EvaluateWhatsAppSendWindow([reply], LineA, LineA, Now, OldRecipient).Activa);
    }

    [Fact]
    public void SharedContactDoesNotMergeDifferentRecipients()
    {
        var groups = ConversacionesService.BuildDuplicateGroups([
            new() { IdConversacion = 1, IdContacto = 371, IdNumeroWhatsApp = 1, TelefonoWhatsApp = Recipient },
            new() { IdConversacion = 2, IdContacto = 371, IdNumeroWhatsApp = 1, TelefonoWhatsApp = OldRecipient }
        ]);
        Assert.Empty(groups);
    }

    [Fact]
    public void EquivalentPhoneFormatsMergeOnlyWithinSameSupportLine()
    {
        var groups = ConversacionesService.BuildDuplicateGroups([
            new() { IdConversacion = 1, IdNumeroWhatsApp = 1, TelefonoWhatsApp = Recipient },
            new() { IdConversacion = 2, IdNumeroWhatsApp = 1, TelefonoWhatsApp = "2944202289" },
            new() { IdConversacion = 3, IdNumeroWhatsApp = 2, TelefonoWhatsApp = Recipient }
        ]).ToList();
        Assert.Single(groups);
        Assert.Equal(new long[] { 1, 2 }, groups[0].Select(x => x.IdConversacion));
    }

    [Fact]
    public void WindowBelongsToReceivingNumber_EvenWhenConversationWasReassigned()
    {
        var messages = new[] { Incoming(1, Now.AddMinutes(-2), LineB) };
        Assert.False(ConversacionesService.EvaluateWhatsAppSendWindow(messages, LineA, LineA, Now, Recipient).Activa);
        Assert.True(ConversacionesService.EvaluateWhatsAppSendWindow(messages, LineB, LineA, Now, Recipient).Activa);
    }

    [Fact]
    public void OtherLinesNewMessageCannotExtendAnExpiredWindow()
    {
        var result = Evaluate(Incoming(1, Now.AddHours(-25)), Incoming(2, Now.AddMinutes(-2), LineB));
        Assert.False(result.Activa);
    }

    [Fact]
    public void QrMessageCannotOpenMetaApiWindow()
    {
        Assert.False(Evaluate(Incoming(1, Now.AddMinutes(-2), source: "WHATSAPP_WEB")).Activa);
        Assert.False(Evaluate(Incoming(1, Now.AddMinutes(-2), line: null, source: "WHATSAPP_WEB")).Activa);
    }

    [Fact]
    public void LegacyMessageWithoutMetadataOnlyAppliesToItsAssociatedNumber()
    {
        var messages = new[] { Incoming(1, Now.AddMinutes(-2), line: null) };
        Assert.True(ConversacionesService.EvaluateWhatsAppSendWindow(messages, LineA, LineA, Now, Recipient).Activa);
        Assert.False(ConversacionesService.EvaluateWhatsAppSendWindow(messages, LineB, LineA, Now, Recipient).Activa);
        Assert.False(ConversacionesService.EvaluateWhatsAppSendWindow(messages, LineA, "", Now, Recipient).Activa);
    }

    [Fact]
    public void MetaRejectionOverridesLocalEstimate()
    {
        var result = Evaluate(Incoming(1, Now.AddMinutes(-2)), Outgoing(2, Now.AddMinutes(-1)));
        Assert.False(result.Activa);
        Assert.True(result.RechazadaPorMeta);
    }

    [Fact]
    public void NewClientMessageToSameNumberClearsRejection()
    {
        var result = Evaluate(Incoming(1, Now.AddMinutes(-3)), Outgoing(2, Now.AddMinutes(-2)), Incoming(3, Now.AddMinutes(-1)));
        Assert.True(result.Activa);
        Assert.False(result.RechazadaPorMeta);
        Assert.Equal(Now.AddMinutes(-1).AddHours(24), result.Vencimiento);
    }

    [Fact]
    public void NewClientMessageToAnotherNumberDoesNotClearRejection()
    {
        Assert.True(Evaluate(Incoming(1, Now.AddMinutes(-3)), Outgoing(2, Now.AddMinutes(-2)),
            Incoming(3, Now.AddMinutes(-1), LineB)).RechazadaPorMeta);
    }

    [Theory]
    [InlineData("PENDIENTE")]
    [InlineData("ENVIADO_META")]
    [InlineData("ERROR_ENVIO")]
    public void PendingOrUnrelatedErrorCannotClearRejection(string state)
    {
        Assert.True(Evaluate(Incoming(1, Now.AddMinutes(-3)), Outgoing(2, Now.AddMinutes(-2)),
            Outgoing(3, Now.AddMinutes(-1), state, code: 131026)).RechazadaPorMeta);
    }

    [Theory]
    [InlineData("ENTREGADO")]
    [InlineData("LEIDO")]
    public void DeliveredTemplateCannotReopenFreeTextWindow(string state)
    {
        Assert.True(Evaluate(Incoming(1, Now.AddMinutes(-3)), Outgoing(2, Now.AddMinutes(-2)),
            Outgoing(3, Now.AddMinutes(-1), state)).RechazadaPorMeta);
    }

    [Fact]
    public void RejectionFromOtherNumberDoesNotCloseThisWindow()
    {
        Assert.True(Evaluate(Incoming(1, Now.AddMinutes(-2)), Outgoing(2, Now.AddMinutes(-1), line: LineB)).Activa);
    }

    [Fact]
    public void DelayedFailureOfMessageBeforeClientReplyDoesNotCloseNewWindow()
    {
        Assert.True(Evaluate(Outgoing(1, Now.AddMinutes(-2)), Incoming(2, Now.AddMinutes(-1))).Activa);
    }

    [Fact]
    public void EqualTimestampsUseMessageOrder()
    {
        Assert.True(Evaluate(Outgoing(1, Now), Incoming(2, Now)).Activa);
        Assert.True(Evaluate(Incoming(1, Now), Outgoing(2, Now)).RechazadaPorMeta);
    }

    [Fact]
    public void ExpiredAndFutureTimestampsCannotOpenWindow()
    {
        Assert.False(Evaluate(Incoming(1, Now.AddHours(-24))).Activa);
        Assert.False(Evaluate(Incoming(1, Now.AddSeconds(1))).Activa);
        Assert.True(Evaluate(Incoming(1, Now.AddHours(-24).AddSeconds(1))).Activa);
    }

    [Fact]
    public void StatusEnrichmentPreservesGraphErrorAndReceivingNumber()
    {
        const string status = """{"status":"failed","errors":[{"code":131047}],"id":"wamid.test"}""";
        var enriched = ConversacionesService.AddWhatsAppPhoneNumberIdToPayload(status, LineB);
        Assert.Equal("131047", WhatsAppOutboundErrorClassifier.ExtractGraphError(enriched).Code);
        using var doc = JsonDocument.Parse(enriched);
        Assert.Equal(LineB, doc.RootElement.GetProperty("phone_number_id").GetString());
        Assert.Equal("wamid.test", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("not-json", ConversacionesService.AddWhatsAppPhoneNumberIdToPayload("not-json", LineA));
    }
}

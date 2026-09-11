using System.Text.Json;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Parsers puros (sin SQL) para los tres tipos de webhook de Coexistence: history, smb_app_state_sync
/// (sobre nuevo, sin "entry") y smb_message_echoes (sobre clásico, field="smb_message_echoes", antes
/// ignorado). No hay muestra de payload real confirmada por Meta para estos tres -- los shapes acá son
/// los documentados por partners oficiales; los parsers son deliberadamente fail-safe (devuelven
/// null/vacío, nunca lanzan) para que un nombre de campo distinto en producción no tumbe el webhook.
/// </summary>
public sealed class WhatsAppCoexistenceSyncParsingTests
{
    [Fact]
    public void HistoryEvent_ParsesThreadsMessagesAndChunkMetadata()
    {
        var root = Parse("""
            {
              "id": "wa-history-1",
              "event": "history",
              "data": {
                "metadata": { "phone_number_id": "1233329726536711" },
                "history": [
                  {
                    "metadata": { "phase": "1", "chunk_order": 1, "progress": "50" },
                    "threads": [
                      { "messages": [
                        { "id": "wamid.h1", "from": "5491100000001", "type": "text", "timestamp": "1700000000", "text": { "body": "hola" } }
                      ] }
                    ]
                  },
                  {
                    "metadata": { "phase": "2", "chunk_order": 2, "progress": "100" },
                    "threads": [
                      { "messages": [
                        { "id": "wamid.h2", "from": "5491100000001", "to": "1233329726536711", "type": "text", "timestamp": "1700000100", "text": { "body": "hola de vuelta" } }
                      ] }
                    ]
                  }
                ]
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root);

        Assert.NotNull(result);
        Assert.Equal("1233329726536711", result!.PhoneNumberId);
        Assert.Equal(2, result.ThreadCount);
        Assert.Equal(2, result.Messages.Count);
        Assert.Equal(["1", "2"], result.Phases);
    }

    [Fact]
    public void HistoryEvent_InfersDirectionFromToPresence()
    {
        var root = Parse("""
            {
              "event": "history",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "history": [ { "threads": [ { "messages": [
                  { "id": "wamid.in", "from": "5491100000001", "type": "text", "timestamp": "1700000000", "text": { "body": "del cliente" } },
                  { "id": "wamid.out", "from": "111", "to": "5491100000001", "type": "text", "timestamp": "1700000001", "text": { "body": "del negocio" } }
                ] } ] } ]
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root)!;

        var inbound = result.Messages.Single(m => m.WhatsAppMessageId == "wamid.in");
        var outbound = result.Messages.Single(m => m.WhatsAppMessageId == "wamid.out");
        Assert.Equal("ENTRANTE", inbound.Direction);
        Assert.Equal("5491100000001", inbound.Phone);
        Assert.Equal("SALIENTE", outbound.Direction);
        Assert.Equal("5491100000001", outbound.Phone); // Phone = la contraparte (cliente), nunca el propio negocio.
    }

    [Fact]
    public void HistoryEvent_PreservesRealHistoricalTimestamp()
    {
        var root = Parse("""
            {
              "event": "history",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "history": [ { "threads": [ { "messages": [
                  { "id": "wamid.old", "from": "5491100000001", "type": "text", "timestamp": "1577836800", "text": { "body": "mensaje viejo" } }
                ] } ] } ]
              }
            }
            """);

        var message = ConversacionesService.ParseIncomingHistorySync(root)!.Messages.Single();

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), message.Timestamp.ToUniversalTime());
    }

    [Fact]
    public void HistoryEvent_OutOfOrderChunks_AllMessagesStillParsed()
    {
        // chunk_order 3 llega listado antes que 1: el parser no reordena ni descarta, junta todo.
        var root = Parse("""
            {
              "event": "history",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "history": [
                  { "metadata": { "chunk_order": 3 }, "threads": [ { "messages": [ { "id": "wamid.c3", "from": "a", "type": "text", "timestamp": "1700000003" } ] } ] },
                  { "metadata": { "chunk_order": 1 }, "threads": [ { "messages": [ { "id": "wamid.c1", "from": "a", "type": "text", "timestamp": "1700000001" } ] } ] }
                ]
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root)!;

        Assert.Equal(2, result.Messages.Count);
        Assert.Contains(result.Messages, m => m.WhatsAppMessageId == "wamid.c3");
        Assert.Contains(result.Messages, m => m.WhatsAppMessageId == "wamid.c1");
    }

    [Fact]
    public void HistoryEvent_MessageWithoutId_IsSkippedNotThrown()
    {
        var root = Parse("""
            {
              "event": "history",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "history": [ { "threads": [ { "messages": [
                  { "from": "5491100000001", "type": "text", "timestamp": "1700000000", "text": { "body": "sin id" } }
                ] } ] } ]
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root)!;

        Assert.Empty(result.Messages);
    }

    [Fact]
    public void HistoryEvent_ErrorCode2593109_IsExtractedFromErrorsArray()
    {
        var root = Parse("""
            {
              "event": "history",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "errors": [ { "code": 2593109, "message": "History sync is turned off by the business from the WhatsApp Business App." } ]
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root)!;

        Assert.Equal(2593109, result.ErrorCode);
        Assert.Empty(result.Messages); // el evento de error no trae threads utilizables.
    }

    [Fact]
    public void HistoryEvent_ContextStatus_IsExtractedWhenPresent()
    {
        var root = Parse("""
            {
              "event": "history",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "history_context": { "status": "complete" }
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root)!;

        Assert.Equal("complete", result.ContextStatus);
    }

    [Theory]
    [InlineData("""{"event":"smb_app_state_sync","data":{"metadata":{"phone_number_id":"111"}}}""")]  // evento distinto
    [InlineData("""{"data":{"metadata":{"phone_number_id":"111"},"history":[]}}""")]                    // sin "event"
    [InlineData("""{"entry":[]}""")]                                                                    // sobre clásico, sin "event"/"data"
    public void HistoryEvent_WrongOrMissingEnvelope_ReturnsNull(string payload)
    {
        Assert.Null(ConversacionesService.ParseIncomingHistorySync(Parse(payload)));
    }

    [Fact]
    public void StateSync_ParsesContactFields()
    {
        var root = Parse("""
            {
              "event": "smb_app_state_sync",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "state_sync": [
                  { "type": "contact", "action": "add", "contact": { "full_name": "Juan Pérez", "first_name": "Juan", "phone_number": "+54 9 11 0000-0000" }, "timestamp": "1700000000" }
                ]
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingStateSync(root)!;

        var contact = Assert.Single(result.Contacts);
        Assert.Equal("contact", contact.Type);
        Assert.Equal("add", contact.Action);
        Assert.Equal("Juan Pérez", contact.FullName);
        Assert.Equal("Juan", contact.FirstName);
        Assert.Equal("5491100000000", contact.PhoneNumber); // normalizado, igual que el resto del parser.
    }

    [Fact]
    public void StateSync_NonContactType_IsIgnoredGracefully()
    {
        var root = Parse("""
            {
              "event": "smb_app_state_sync",
              "data": { "metadata": { "phone_number_id": "111" }, "state_sync": [
                { "type": "something_else", "phone_number": "111" }
              ] }
            }
            """);

        var result = ConversacionesService.ParseIncomingStateSync(root)!;

        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void StateSync_MissingPhoneNumber_IsSkipped()
    {
        var root = Parse("""
            {
              "event": "smb_app_state_sync",
              "data": { "metadata": { "phone_number_id": "111" }, "state_sync": [
                { "type": "contact", "full_name": "Sin Teléfono" }
              ] }
            }
            """);

        var result = ConversacionesService.ParseIncomingStateSync(root)!;

        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void MessageEchoes_ParsedFromClassicEnvelope_AlwaysOutboundWithCustomerAsPhone()
    {
        var root = Parse("""
            {
              "entry": [ { "changes": [
                { "field": "messages", "value": { "metadata": { "phone_number_id": "111" }, "messages": [ { "id": "wamid.normal" } ] } },
                { "field": "smb_message_echoes", "value": {
                    "metadata": { "phone_number_id": "111" },
                    "message_echoes": [
                      { "id": "wamid.echo1", "from": "111", "to": "5491100000001", "type": "text", "timestamp": "1700000000", "text": { "body": "enviado desde la app" } }
                    ]
                } }
              ] } ]
            }
            """);

        var echoes = ConversacionesService.ParseIncomingMessageEchoes(root);

        var echo = Assert.Single(echoes);
        Assert.Equal("wamid.echo1", echo.WhatsAppMessageId);
        Assert.Equal("SALIENTE", echo.Direction);
        Assert.Equal("5491100000001", echo.Phone); // el cliente, no el propio negocio.
        Assert.Equal("111", echo.PhoneNumberId);
    }

    [Fact]
    public void MessageEchoes_MissingTo_IsSkippedRatherThanGuessed()
    {
        var root = Parse("""
            {
              "entry": [ { "changes": [
                { "field": "smb_message_echoes", "value": { "metadata": { "phone_number_id": "111" }, "message_echoes": [
                  { "id": "wamid.echo-no-to", "from": "111", "type": "text", "timestamp": "1700000000" }
                ] } }
              ] } ]
            }
            """);

        Assert.Empty(ConversacionesService.ParseIncomingMessageEchoes(root));
    }

    [Fact]
    public void MessageEchoes_OtherFields_AreIgnored()
    {
        var root = Parse("""
            {
              "entry": [ { "changes": [
                { "field": "messages", "value": { "metadata": { "phone_number_id": "111" }, "messages": [ { "id": "wamid.normal", "from": "5491100000001", "type": "text", "timestamp": "1700000000" } ] } }
              ] } ]
            }
            """);

        Assert.Empty(ConversacionesService.ParseIncomingMessageEchoes(root));
    }

    [Fact]
    public void ExtractPhoneNumberIds_RecognizesNewEnvelope_ForHistoryAndStateSync()
    {
        var history = Parse("""{"event":"history","data":{"metadata":{"phone_number_id":"222"}}}""");
        var stateSync = Parse("""{"event":"smb_app_state_sync","data":{"metadata":{"phone_number_id":"333"}}}""");

        Assert.Contains("222", ConversacionesService.ExtractWhatsAppPhoneNumberIds(history));
        Assert.Contains("333", ConversacionesService.ExtractWhatsAppPhoneNumberIds(stateSync));
    }

    [Fact]
    public void ExtractPhoneNumberIds_StillRecognizesClassicEnvelope()
    {
        var classic = Parse("""{"entry":[{"changes":[{"field":"messages","value":{"metadata":{"phone_number_id":"444"}}}]}]}""");

        Assert.Contains("444", ConversacionesService.ExtractWhatsAppPhoneNumberIds(classic));
    }

    [Fact]
    public void ProgramExtractPhoneNumberIds_RecognizesNewEnvelope()
    {
        // Selector de Program.cs (opera sobre el body crudo, antes de validar firma) -- mismo sobre nuevo.
        var raw = """{"event":"history","data":{"metadata":{"phone_number_id":"555"}}}""";

        Assert.Contains("555", AlfaCore.Program.ExtractWhatsAppPhoneNumberIds(raw));
    }

    [Fact]
    public void ClassifyHistorySyncWebhook_FirstChunkWithoutStatus_IsInProgress()
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.InProgress,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: null, contextStatus: null));

    [Fact]
    public void ClassifyHistorySyncWebhook_InProgressStatus_IsInProgress()
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.InProgress,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: null, contextStatus: "in_progress"));

    [Theory]
    [InlineData("complete")]
    [InlineData("COMPLETE")]
    [InlineData("history_sync_complete")]
    public void ClassifyHistorySyncWebhook_CompleteStatus_IsCompleted(string status)
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.Completed,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: null, contextStatus: status));

    [Fact]
    public void ClassifyHistorySyncWebhook_Error2593109_IsDeclined_EvenWithCompleteStatus()
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.Declined,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: 2593109, contextStatus: "complete"));

    [Fact]
    public void ClassifyHistorySyncWebhook_OtherErrorCode_IsErrorNotDeclined()
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.Error,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: 1, contextStatus: null));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}

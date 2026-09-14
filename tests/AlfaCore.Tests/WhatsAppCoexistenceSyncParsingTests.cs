using System.Text.Json;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Parsers puros (sin SQL) para los tres tipos de webhook de Coexistence: history, smb_app_state_sync
/// y smb_message_echoes. Confirmado por Meta (developers.facebook.com) y por dos partners
/// independientes (360dialog, Gupshup), 2026-09: "history" y "smb_app_state_sync" pueden llegar en
/// CUALQUIERA de dos sobres documentados -- no es que uno reemplazó al otro -- así que AlfaCore acepta
/// los dos, nunca elige uno solo:
///   A) sobre nuevo: { id, event:"history"|"smb_app_state_sync", data:{...} }, sin "entry"
///   B) sobre clásico: { object, entry:[{ changes:[{ field:"history"|"smb_app_state_sync", value:{...} }] }] }
/// "smb_message_echoes" sólo documenta el sobre clásico (B).
/// Los parsers son deliberadamente fail-safe (devuelven null/vacío, nunca lanzan) para que un nombre de
/// campo distinto en producción no tumbe el webhook.
/// </summary>
public sealed class WhatsAppCoexistenceSyncParsingTests
{
    [Fact]
    public void HistoryEnvelopeA_Approved_ParsesThreadsMessagesChunkMetadataAndProgress()
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
                        { "id": "wamid.h2", "from": "5491100000001", "to": "1233329726536711", "timestamp": "1700000100", "type": "text", "text": { "body": "hola de vuelta" } }
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
        Assert.Equal(100, result.Progress); // máximo visto entre los chunks del webhook.
    }

    [Fact]
    public void HistoryEnvelopeB_Approved_ParsesThreadsMessagesChunkMetadataAndProgress()
    {
        // Sobre clásico confirmado por Meta oficial y por dos partners (360dialog, Gupshup): mismo
        // patrón que messages/statuses/smb_message_echoes -- entry[].changes[].field="history".
        var root = Parse("""
            {
              "object": "whatsapp_business_account",
              "entry": [
                {
                  "id": "<WABA_ID>",
                  "changes": [
                    {
                      "field": "history",
                      "value": {
                        "messaging_product": "whatsapp",
                        "metadata": { "display_phone_number": "+1 555 000 0000", "phone_number_id": "1233329726536711" },
                        "history": [
                          {
                            "metadata": { "phase": 1, "chunk_order": 1, "progress": 100 },
                            "threads": [
                              {
                                "id": "5491100000001",
                                "messages": [
                                  { "from": "5491100000001", "to": "1233329726536711", "id": "wamid.b1", "timestamp": "1700000000", "type": "text", "text": { "body": "hola" } }
                                ]
                              }
                            ]
                          }
                        ]
                      }
                    }
                  ]
                }
              ]
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root);

        Assert.NotNull(result);
        Assert.Equal("1233329726536711", result!.PhoneNumberId);
        Assert.Equal(1, result.ThreadCount);
        var message = Assert.Single(result.Messages);
        Assert.Equal("wamid.b1", message.WhatsAppMessageId);
        Assert.Equal(["1"], result.Phases); // phase numérico (1, no "1") también se acepta.
        Assert.Equal(100, result.Progress);
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
    public void HistoryEnvelopeA_Declined2593109_IsExtractedFromChunkErrorsArray()
    {
        // Confirmado por Meta: errors[] es hermano de "metadata"/"threads" DENTRO de cada chunk de
        // history[], no a nivel de "data".
        var root = Parse("""
            {
              "event": "history",
              "data": {
                "messaging_product": "whatsapp",
                "metadata": { "phone_number_id": "111" },
                "history": [
                  { "errors": [ { "code": 2593109, "title": "History sync is turned off by the business from the WhatsApp Business App" } ] }
                ]
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root)!;

        Assert.Equal(2593109, result.ErrorCode);
        Assert.Empty(result.Messages); // el chunk de error no trae threads utilizables.
        Assert.Null(result.Progress);
    }

    [Fact]
    public void HistoryEnvelopeB_Declined2593109_IsExtractedFromChunkErrorsArray()
    {
        var root = Parse("""
            {
              "object": "whatsapp_business_account",
              "entry": [
                {
                  "id": "<WABA_ID>",
                  "changes": [
                    {
                      "field": "history",
                      "value": {
                        "messaging_product": "whatsapp",
                        "metadata": { "phone_number_id": "111" },
                        "history": [
                          { "errors": [ { "code": 2593109, "title": "History sync is turned off by the business from the WhatsApp Business App" } ] }
                        ]
                      }
                    }
                  ]
                }
              ]
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root)!;

        Assert.Equal(2593109, result.ErrorCode);
        Assert.Empty(result.Messages);
    }

    [Theory]
    [InlineData("""{"event":"smb_app_state_sync","data":{"metadata":{"phone_number_id":"111"}}}""")]  // evento distinto
    [InlineData("""{"data":{"metadata":{"phone_number_id":"111"},"history":[]}}""")]                    // sin "event"
    [InlineData("""{"entry":[{"changes":[{"field":"messages","value":{}}]}]}""")]                       // sobre clásico, field distinto
    [InlineData("""{"entry":[]}""")]                                                                    // sobre clásico vacío
    public void HistoryEvent_WrongOrMissingEnvelope_ReturnsNull(string payload)
    {
        Assert.Null(ConversacionesService.ParseIncomingHistorySync(Parse(payload)));
    }

    [Fact]
    public void StateSyncEnvelopeA_ParsesContactFields()
    {
        var root = Parse("""
            {
              "event": "smb_app_state_sync",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "state_sync": [
                  { "type": "contact", "action": "add", "contact": { "full_name": "Juan Pérez", "first_name": "Juan", "phone_number": "+54 9 11 0000-0000" }, "metadata": { "timestamp": "1700000000" } }
                ]
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingStateSync(root)!;

        Assert.Equal("111", result.PhoneNumberId);
        var contact = Assert.Single(result.Contacts);
        Assert.Equal("contact", contact.Type);
        Assert.Equal("add", contact.Action);
        Assert.Equal("Juan Pérez", contact.FullName);
        Assert.Equal("Juan", contact.FirstName);
        Assert.Equal("5491100000000", contact.PhoneNumber); // normalizado, igual que el resto del parser.
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc), contact.Timestamp.ToUniversalTime());
    }

    [Fact]
    public void StateSyncEnvelopeB_ParsesContactFields()
    {
        // Sobre clásico confirmado por Meta oficial y Gupshup: entry[].changes[].field="smb_app_state_sync".
        var root = Parse("""
            {
              "object": "whatsapp_business_account",
              "entry": [
                {
                  "id": "<WABA_ID>",
                  "changes": [
                    {
                      "field": "smb_app_state_sync",
                      "value": {
                        "messaging_product": "whatsapp",
                        "metadata": { "phone_number_id": "222" },
                        "state_sync": [
                          { "type": "contact", "contact": { "full_name": "María López", "first_name": "María", "phone_number": "5491100000002" }, "action": "add", "metadata": { "timestamp": "1700000000" } }
                        ]
                      }
                    }
                  ]
                }
              ]
            }
            """);

        var result = ConversacionesService.ParseIncomingStateSync(root)!;

        Assert.Equal("222", result.PhoneNumberId);
        var contact = Assert.Single(result.Contacts);
        Assert.Equal("María López", contact.FullName);
        Assert.Equal("5491100000002", contact.PhoneNumber);
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
    public void ExtractPhoneNumberIds_RecognizesClassicEnvelope_ForHistoryAndStateSync()
    {
        // Mismo sobre clásico que messages/statuses -- el guard no filtra por field, así que ya
        // reconoce phone_number_id para history/smb_app_state_sync en entry[].changes[].value.metadata.
        var history = Parse("""{"entry":[{"changes":[{"field":"history","value":{"metadata":{"phone_number_id":"777"}}}]}]}""");
        var stateSync = Parse("""{"entry":[{"changes":[{"field":"smb_app_state_sync","value":{"metadata":{"phone_number_id":"888"}}}]}]}""");

        Assert.Contains("777", ConversacionesService.ExtractWhatsAppPhoneNumberIds(history));
        Assert.Contains("888", ConversacionesService.ExtractWhatsAppPhoneNumberIds(stateSync));
    }

    [Fact]
    public void ExtractPhoneNumberIds_StillRecognizesClassicEnvelopeForMessages()
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
    public void ProgramExtractPhoneNumberIds_RecognizesClassicEnvelope_ForHistory()
    {
        var raw = """{"entry":[{"changes":[{"field":"history","value":{"metadata":{"phone_number_id":"666"}}}]}]}""";

        Assert.Contains("666", AlfaCore.Program.ExtractWhatsAppPhoneNumberIds(raw));
    }

    [Fact]
    public void ClassifyHistorySyncWebhook_NoProgress_IsInProgress()
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.InProgress,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: null, progress: null));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    public void ClassifyHistorySyncWebhook_ProgressBelow100_IsInProgress(int progress)
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.InProgress,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: null, progress: progress));

    [Fact]
    public void ClassifyHistorySyncWebhook_Progress100_IsCompleted()
        // Confirmado por Meta: "A value of 100 indicates that synchronization is complete."
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.Completed,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: null, progress: 100));

    [Fact]
    public void ClassifyHistorySyncWebhook_PerMessageHistoryContextStatusRead_NeverMeansCompleted()
    {
        // history_context.status="read" es el estado de UN mensaje puntual (threads[].messages[].history_context),
        // nunca una señal de que el sync completo terminó -- el parser ya no lo expone como ContextStatus
        // en absoluto; sólo Progress importa acá. Un chunk sin progress==100 nunca es Completed, sin
        // importar qué digan los mensajes individuales que contiene.
        var root = Parse("""
            {
              "event": "history",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "history": [
                  {
                    "metadata": { "phase": "1", "chunk_order": 1, "progress": "40" },
                    "threads": [ { "messages": [
                      { "id": "wamid.read1", "from": "5491100000001", "type": "text", "timestamp": "1700000000", "text": { "body": "leído" }, "history_context": { "status": "read" } }
                    ] } ]
                  }
                ]
              }
            }
            """);

        var result = ConversacionesService.ParseIncomingHistorySync(root)!;

        Assert.Equal(40, result.Progress);
        Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.InProgress,
            ConversacionesService.ClassifyHistorySyncWebhook(result.ErrorCode, result.Progress));
    }

    [Fact]
    public void ClassifyHistorySyncWebhook_Error2593109_IsDeclined_EvenWithProgress100()
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.Declined,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: 2593109, progress: 100));

    [Fact]
    public void ClassifyHistorySyncWebhook_OtherErrorCode_IsErrorNotDeclined()
        => Assert.Equal(
            ConversacionesService.WhatsAppHistorySyncWebhookOutcome.Error,
            ConversacionesService.ClassifyHistorySyncWebhook(errorCode: 1, progress: null));

    [Fact]
    public void ClassicMessagesEnvelope_NeverMisparsedAsHistoryOrStateSync_NoRegression()
    {
        // Sanity check de no-regresión: agregar el sobre nuevo (A) y el reconocimiento de field="history"/
        // "smb_app_state_sync" en el sobre clásico (B) no puede hacer que un payload normal de
        // "messages"/"statuses" (field="messages") se interprete por error como history/state_sync.
        var root = Parse("""
            {
              "object": "whatsapp_business_account",
              "entry": [ { "changes": [
                { "field": "messages", "value": { "metadata": { "phone_number_id": "111" }, "contacts": [ { "wa_id": "5491100000001" } ], "messages": [
                  { "id": "wamid.msg1", "from": "5491100000001", "type": "text", "timestamp": "1700000000", "text": { "body": "hola" } }
                ] } }
              ] } ]
            }
            """);

        Assert.Null(ConversacionesService.ParseIncomingHistorySync(root));
        Assert.Null(ConversacionesService.ParseIncomingStateSync(root));
        Assert.Empty(ConversacionesService.ParseIncomingMessageEchoes(root));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}

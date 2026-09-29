using System.Text.Json;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppHistoricalMediaRecoveryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ServiceSource = File.ReadAllText(
        Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));

    [Fact]
    public void HistoryParser_PreservesImageAttachmentMediaId()
    {
        var root = Parse("""
            {
              "event": "history",
              "data": {
                "metadata": { "phone_number_id": "111" },
                "history": [ { "threads": [ { "messages": [
                  { "id": "wamid.h-image", "from": "5491100000001", "type": "image", "timestamp": "1700000000",
                    "image": { "id": "media-history-image", "mime_type": "image/jpeg" } }
                ] } ] } ]
              }
            }
            """);

        var message = ConversacionesService.ParseIncomingHistorySync(root)!.Messages.Single();

        var attachment = Assert.Single(message.Attachments);
        Assert.Equal("media-history-image", attachment.MediaId);
        Assert.Equal("IMAGE", attachment.TipoArchivo);
    }

    [Fact]
    public void EchoParser_PreservesDocumentAttachmentMediaId()
    {
        var root = Parse("""
            {
              "entry": [ { "changes": [
                { "field": "smb_message_echoes", "value": {
                    "metadata": { "phone_number_id": "111" },
                    "message_echoes": [
                      { "id": "wamid.echo-doc", "from": "111", "to": "5491100000001", "type": "document", "timestamp": "1700000000",
                        "document": { "id": "media-echo-doc", "mime_type": "application/pdf", "filename": "factura.pdf" } }
                    ]
                } }
              ] } ]
            }
            """);

        var message = ConversacionesService.ParseIncomingMessageEchoes(root).Single();

        var attachment = Assert.Single(message.Attachments);
        Assert.Equal("media-echo-doc", attachment.MediaId);
        Assert.Equal("DOCUMENT", attachment.TipoArchivo);
        Assert.Equal("factura.pdf", attachment.FileName);
    }

    [Fact]
    public void MediaIdExtraction_UnderstandsPersistedWrapper()
    {
        var payload = """
            {
              "metadata": { "phone_number_id": "111" },
              "message": {
                "id": "wamid.message-id-not-media",
                "type": "video",
                "video": { "id": "media-video-123", "mime_type": "video/mp4" }
              }
            }
            """;

        Assert.Equal("media-video-123", ConversacionesService.TryExtractWhatsAppMediaId(payload, "VIDEO"));
    }

    [Fact]
    public void MediaIdExtraction_DoesNotTreatMessageIdAsMediaId()
    {
        var payload = """
            {
              "metadata": { "phone_number_id": "111" },
              "message": { "id": "wamid.only-message-id", "type": "text", "text": { "body": "hola" } }
            }
            """;

        Assert.Null(ConversacionesService.TryExtractWhatsAppMediaId(payload, "IMAGE"));
    }

    [Fact]
    public void WebhookHistoryAndEchoPaths_AttemptBestEffortMediaStorage()
    {
        var history = BlockBetween("request.TraceStage?.Invoke(\"PROCESSING_HISTORY\")", "request.TraceStage?.Invoke(\"PROCESSING_ECHOES\")");
        var echoes = BlockBetween("request.TraceStage?.Invoke(\"PROCESSING_ECHOES\")", "request.TraceStage?.Invoke(\"PROCESSING_STATE_SYNC\")");

        Assert.Contains("TryStoreWebhookAttachmentsAsync(", history, StringComparison.Ordinal);
        Assert.Contains("\"history\"", history, StringComparison.Ordinal);
        Assert.Contains("TryStoreWebhookAttachmentsAsync(", echoes, StringComparison.Ordinal);
        Assert.Contains("\"echo\"", echoes, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryQueries_AreNotCutOffByMessageAge()
    {
        Assert.DoesNotContain("RecoveryCutoff", ServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CanRecoverMediaByAge", ServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachmentRecoveryMaxAge", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryAndHydration_UseRuntimeCredentialsBeforeGraphMediaCalls()
    {
        var hydrate = BlockBetween("private async Task HydrateMissingIncomingMediaAsync", "private async Task<List<PendingMediaHydration>>");
        var recover = BlockBetween("private async Task<string> TryRecoverAttachmentFileAsync", "private bool CanAttemptAttachmentRecovery");

        Assert.Contains("ResolveRuntimeWhatsAppMediaConfigAsync", hydrate, StringComparison.Ordinal);
        Assert.Contains("StoreIncomingAttachmentsAsync", hydrate, StringComparison.Ordinal);
        Assert.True(hydrate.IndexOf("ResolveRuntimeWhatsAppMediaConfigAsync", StringComparison.Ordinal)
                    < hydrate.IndexOf("StoreIncomingAttachmentsAsync", StringComparison.Ordinal));

        Assert.Contains("ResolveRuntimeWhatsAppMediaConfigAsync", recover, StringComparison.Ordinal);
        Assert.Contains("GetWhatsAppMediaAsync(config", recover, StringComparison.Ordinal);
        Assert.True(recover.IndexOf("ResolveRuntimeWhatsAppMediaConfigAsync", StringComparison.Ordinal)
                    < recover.IndexOf("GetWhatsAppMediaAsync(config", StringComparison.Ordinal));
    }

    [Fact]
    public void TemporaryMetaFailures_AreNotMarkedAsPermanentRecoveryFailures()
    {
        var recover = BlockBetween("private async Task<string> TryRecoverAttachmentFileAsync", "private bool CanAttemptAttachmentRecovery");

        Assert.Contains("catch (WhatsAppMediaDownloadException ex) when (ex.IsPermanent)", recover, StringComparison.Ordinal);
        Assert.Contains("\"NO_DISPONIBLE_META\"", recover, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RECUPERACION_FALLIDA\"", recover, StringComparison.Ordinal);
    }

    private static string BlockBetween(string start, string end)
    {
        var startIndex = ServiceSource.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"No se encontro inicio: {start}");
        var endIndex = ServiceSource.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"No se encontro fin: {end}");
        return ServiceSource[startIndex..endIndex];
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}

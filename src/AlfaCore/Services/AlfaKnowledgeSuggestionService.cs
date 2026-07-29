using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class AlfaKnowledgeSuggestionService(
    IHttpClientFactory httpClientFactory,
    IOptions<AlfaKnowledgeOptions> options,
    IAppEventService appEvents,
    ILogger<AlfaKnowledgeSuggestionService> logger) : IAlfaKnowledgeSuggestionService
{
    private const string ModuleName = "AlfaKnowledge";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => options.Value.IsConfigured;

    public string FullChatUrl
        => options.Value.IsConfigured ? $"{options.Value.BaseUrl.TrimEnd('/')}/" : string.Empty;

    public string GetCitationUrl(AlfaKnowledgeSuggestionCitation citation)
    {
        var sourceReference = citation.SourceReference?.Trim();
        if (Uri.TryCreate(sourceReference, UriKind.Absolute, out var sourceUri)
            && sourceUri.Scheme is "http" or "https")
        {
            return sourceUri.AbsoluteUri;
        }

        var baseUrl = FullChatUrl.TrimEnd('/');
        if (string.Equals(citation.SourceType, "WordFile", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(sourceReference))
        {
            return $"{baseUrl}/document-viewer.html?ref={Uri.EscapeDataString(sourceReference)}";
        }

        return $"{baseUrl}/?tab=buscar&query={Uri.EscapeDataString(citation.Title)}";
    }

    public async Task<AlfaKnowledgeSuggestionResult?> SuggestReplyAsync(
        string customerMessage,
        IReadOnlyList<ConversacionMensajeDto> recentMessages,
        long conversationId,
        string? instruction = null,
        IReadOnlyList<AlfaKnowledgeAssistantMessage>? assistantHistory = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            logger.LogWarning("AlfaKnowledge no está configurado (falta BaseUrl o ApiKey); no se pidió sugerencia.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(customerMessage) && string.IsNullOrWhiteSpace(instruction))
        {
            return null;
        }

        try
        {
            var history = recentMessages
                .Where(static message =>
                    (message.Direction == "ENTRANTE" || message.Direction == "SALIENTE")
                    && !string.IsNullOrWhiteSpace(message.Texto))
                .OrderBy(static message => message.FechaHora)
                .TakeLast(60)
                .Select(static message => new
                {
                    role = message.Direction == "ENTRANTE" ? "user" : "assistant",
                    content = message.Texto
                })
                .ToArray();

            var payload = new
            {
                customerMessage,
                history,
                instruction,
                assistantHistory = (assistantHistory ?? [])
                    .Where(static message => !string.IsNullOrWhiteSpace(message.Content))
                    .TakeLast(12)
                    .Select(static message => new
                    {
                        role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                            ? "assistant"
                            : "user",
                        content = message.Content
                    })
                    .ToArray(),
                externalSystem = "AlfaCore",
                externalConversationId = conversationId.ToString(CultureInfo.InvariantCulture),
                limit = 4
            };

            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds, 1));
            client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);

            var baseUrl = settings.BaseUrl.TrimEnd('/');
            using var response = await client.PostAsJsonAsync(
                $"{baseUrl}/api/external/suggest-reply",
                payload,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "AlfaKnowledge devolvió {StatusCode} al pedir una sugerencia: {Body}",
                    (int)response.StatusCode,
                    body);
                await LogFailureAsync(
                    "SuggestReply",
                    new HttpRequestException(
                        $"AlfaKnowledge devolvió HTTP {(int)response.StatusCode}.",
                        inner: null,
                        response.StatusCode),
                    "No se pudo obtener una sugerencia de AlfaKnowledge.",
                    new
                    {
                        StatusCode = (int)response.StatusCode,
                        ConversationId = conversationId,
                        ResponseBody = body
                    });
                return null;
            }

            var apiResult = await response.Content.ReadFromJsonAsync<SuggestReplyApiResponse>(
                JsonOptions,
                cancellationToken);

            if (apiResult is null)
            {
                return null;
            }

            return new AlfaKnowledgeSuggestionResult
            {
                InteractionId = apiResult.InteractionId,
                SuggestedReply = apiResult.SuggestedReply ?? string.Empty,
                NeedsClarification = apiResult.NeedsClarification,
                ClarificationQuestion = apiResult.ClarificationQuestion,
                HasSufficientContext = apiResult.HasSufficientContext,
                Citations = (apiResult.Citations ?? [])
                    .Select(static citation => new AlfaKnowledgeSuggestionCitation
                    {
                        CitationNumber = citation.CitationNumber,
                        ChunkKey = citation.ChunkKey ?? string.Empty,
                        SourceType = citation.SourceType ?? string.Empty,
                        Title = citation.Title ?? string.Empty,
                        SourceLabel = citation.SourceLabel ?? string.Empty,
                        SourceReference = citation.SourceReference
                    })
                    .ToList()
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "No se pudo obtener una sugerencia de AlfaKnowledge.");
            if (!cancellationToken.IsCancellationRequested)
            {
                await LogFailureAsync(
                    "SuggestReply",
                    ex,
                    "No se pudo obtener una sugerencia de AlfaKnowledge.",
                    new { ConversationId = conversationId });
            }
            return null;
        }
    }

    public async Task SendFeedbackAsync(Guid interactionId, bool isHelpful, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds, 1));

            var baseUrl = settings.BaseUrl.TrimEnd('/');
            var payload = new { interactionId, isHelpful };
            using var response = await client.PostAsJsonAsync($"{baseUrl}/api/feedback", payload, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AlfaKnowledge devolvió {StatusCode} al mandar feedback de sugerencia.", (int)response.StatusCode);
                await LogFailureAsync(
                    "SendFeedback",
                    new HttpRequestException(
                        $"AlfaKnowledge devolvió HTTP {(int)response.StatusCode} al registrar feedback.",
                        inner: null,
                        response.StatusCode),
                    "No se pudo registrar el feedback de la sugerencia de AlfaKnowledge.",
                    new
                    {
                        StatusCode = (int)response.StatusCode,
                        InteractionId = interactionId
                    });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "No se pudo mandar feedback de sugerencia a AlfaKnowledge.");
            if (!cancellationToken.IsCancellationRequested)
            {
                await LogFailureAsync(
                    "SendFeedback",
                    ex,
                    "No se pudo registrar el feedback de la sugerencia de AlfaKnowledge.",
                    new { InteractionId = interactionId });
            }
        }
    }

    private async Task LogFailureAsync(string action, Exception exception, string userMessage, object? data)
    {
        try
        {
            await appEvents.LogErrorAsync(
                ModuleName,
                action,
                exception,
                userMessage,
                data,
                ct: CancellationToken.None);
        }
        catch (Exception logException)
        {
            logger.LogWarning(
                logException,
                "No se pudo registrar en el log central el fallo {Module}/{Action}.",
                ModuleName,
                action);
        }
    }

    private sealed class SuggestReplyApiResponse
    {
        public Guid InteractionId { get; set; }
        public string? SuggestedReply { get; set; }
        public bool NeedsClarification { get; set; }
        public string? ClarificationQuestion { get; set; }
        public bool HasSufficientContext { get; set; }
        public List<SuggestReplyApiCitation>? Citations { get; set; }
    }

    private sealed class SuggestReplyApiCitation
    {
        public int CitationNumber { get; set; }
        public string? ChunkKey { get; set; }
        public string? SourceType { get; set; }
        public string? Title { get; set; }
        public string? SourceLabel { get; set; }
        public string? SourceReference { get; set; }
    }
}

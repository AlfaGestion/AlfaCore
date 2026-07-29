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
    ILogger<AlfaKnowledgeSuggestionService> logger) : IAlfaKnowledgeSuggestionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AlfaKnowledgeSuggestionResult?> SuggestReplyAsync(
        string customerMessage,
        IReadOnlyList<ConversacionMensajeDto> recentMessages,
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            logger.LogWarning("AlfaKnowledge no está configurado (falta BaseUrl o ApiKey); no se pidió sugerencia.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(customerMessage))
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
                .TakeLast(12)
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
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "No se pudo mandar feedback de sugerencia a AlfaKnowledge.");
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
        public string? Title { get; set; }
        public string? SourceLabel { get; set; }
        public string? SourceReference { get; set; }
    }
}

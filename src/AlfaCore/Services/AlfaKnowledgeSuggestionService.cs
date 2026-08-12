using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class AlfaKnowledgeSuggestionService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    ILogger<AlfaKnowledgeSuggestionService> logger) : IAlfaKnowledgeSuggestionService
{
    private const string ModuleName = "AlfaKnowledge";
    private const string KnowledgeBaseHeaderName = "X-Knowledge-Base-Id";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public bool IsConfigured => GetEffectiveSettings().IsConfigured;

    public string FullChatUrl
    {
        get
        {
            var settings = GetEffectiveSettings();
            return settings.IsConfigured ? $"{settings.BaseUrl.TrimEnd('/')}/" : string.Empty;
        }
    }

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
        string mode = AlfaKnowledgeSuggestionModes.ReplySuggestion,
        string? instruction = null,
        IReadOnlyList<AlfaKnowledgeAssistantMessage>? assistantHistory = null,
        AlfaKnowledgeImageInput? image = null,
        AlfaKnowledgeTextImprovementInput? textImprovement = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetEffectiveSettingsAsync(cancellationToken);
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

            // Instrucciones curadas del asistente (persona/tono/reglas): se anteponen SOLO a la
            // sugerencia de respuesta, no a transformaciones puntuales (mejorar/AnyDesk).
            var effectiveInstruction = instruction;
            if (string.Equals(mode, AlfaKnowledgeSuggestionModes.ReplySuggestion, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(settings.Instrucciones))
            {
                effectiveInstruction = string.IsNullOrWhiteSpace(instruction)
                    ? settings.Instrucciones.Trim()
                    : $"{settings.Instrucciones.Trim()}\n\n{instruction.Trim()}";
            }

            var payload = new
            {
                customerMessage,
                mode,
                history,
                instruction = effectiveInstruction,
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
                imageDataUrl = image?.DataUrl,
                imageFileName = image?.FileName,
                textToImprove = textImprovement?.TextToImprove,
                externalSystem = "AlfaCore",
                externalConversationId = conversationId.ToString(CultureInfo.InvariantCulture),
                limit = 4
            };

            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds, 1));
            client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
            TryAddKnowledgeBaseHeader(client, settings);

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
                ImageAnalyzed = apiResult.ImageAnalyzed,
                ImageContext = apiResult.ImageContext,
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
        var settings = await GetEffectiveSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds, 1));
            TryAddKnowledgeBaseHeader(client, settings);

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

    public async Task<bool> SaveCorrectionAsync(
        AlfaKnowledgeCorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await GetEffectiveSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            return false;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds, 1));
            client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
            TryAddKnowledgeBaseHeader(client, settings);

            var payload = new
            {
                interactionId = request.InteractionId,
                title = request.Title,
                originalQuestion = request.OriginalQuestion,
                originalAnswer = request.OriginalAnswer,
                correctedAnswer = request.CorrectedAnswer,
                sourceReference = request.SourceReference,
                correctionNotes = request.CorrectionNotes,
                createdBy = request.CreatedBy,
                isPublished = true
            };

            var baseUrl = settings.BaseUrl.TrimEnd('/');
            using var response = await client.PostAsJsonAsync(
                $"{baseUrl}/api/curated-knowledge/correction",
                payload,
                JsonOptions,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "AlfaKnowledge devolvió {StatusCode} al guardar una corrección: {Body}",
                (int)response.StatusCode,
                body);
            await LogFailureAsync(
                "SaveCorrection",
                new HttpRequestException(
                    $"AlfaKnowledge devolvió HTTP {(int)response.StatusCode} al guardar una corrección.",
                    inner: null,
                    response.StatusCode),
                "No se pudo guardar la respuesta corregida en AlfaKnowledge.",
                new
                {
                    StatusCode = (int)response.StatusCode,
                    request.InteractionId,
                    ResponseBody = body
                });
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "No se pudo guardar la corrección en AlfaKnowledge.");
            if (!cancellationToken.IsCancellationRequested)
            {
                await LogFailureAsync(
                    "SaveCorrection",
                    ex,
                    "No se pudo guardar la respuesta corregida en AlfaKnowledge.",
                    new { request.InteractionId });
            }

            return false;
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

    private AlfaKnowledgeOptions GetEffectiveSettings()
    {
        try
        {
            return LoadSettingsFromDatabase() ?? CreateEmptyOptions();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo leer la configuración de AlfaKnowledge desde TA_CONFIGURACION.");
            return CreateEmptyOptions();
        }
    }

    private async Task<AlfaKnowledgeOptions> GetEffectiveSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await LoadSettingsFromDatabaseAsync(cancellationToken) ?? CreateEmptyOptions();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogWarning(ex, "No se pudo leer la configuración de AlfaKnowledge desde TA_CONFIGURACION.");
            return CreateEmptyOptions();
        }
    }

    private AlfaKnowledgeOptions? LoadSettingsFromDatabase()
    {
        var connectionString = ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        using var cn = new SqlConnection(connectionString);
        cn.Open();
        var detailColumn = ResolveDetailColumn(cn);
        using var cmd = new SqlCommand(BuildSelectSql(detailColumn), cn);
        using var rd = cmd.ExecuteReader();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (rd.Read())
        {
            var key = GetString(rd, 0);
            var value = GetString(rd, 1);
            var detailValue = GetString(rd, 2);
            values[key] = ResolveStoredValue(value, detailValue);
        }

        return BuildOptions(values);
    }

    private async Task<AlfaKnowledgeOptions?> LoadSettingsFromDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(cancellationToken);
        var detailColumn = await ResolveDetailColumnAsync(cn, cancellationToken);
        await using var cmd = new SqlCommand(BuildSelectSql(detailColumn), cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await rd.ReadAsync(cancellationToken))
        {
            var key = GetString(rd, 0);
            var value = GetString(rd, 1);
            var detailValue = GetString(rd, 2);
            values[key] = ResolveStoredValue(value, detailValue);
        }

        return BuildOptions(values);
    }

    private AlfaKnowledgeOptions BuildOptions(Dictionary<string, string> values)
    {
        return new AlfaKnowledgeOptions
        {
            BaseUrl = ReadValue(values, "CONV_ALFAKNOWLEDGE_BASE_URL", string.Empty),
            ApiKey = ReadValue(values, "CONV_ALFAKNOWLEDGE_API_KEY", string.Empty),
            KnowledgeBaseId = ReadValue(values, "CONV_ALFAKNOWLEDGE_KNOWLEDGE_BASE_ID", string.Empty),
            Instrucciones = ReadValue(values, "CONV_ALFAKNOWLEDGE_INSTRUCCIONES", string.Empty),
            TimeoutSeconds = ReadIntValue(values, "CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS", 0, 15)
        };
    }

    private string ResolveConnectionString()
    {
        var sessionConnection = sessionService.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(sessionConnection))
            return sessionConnection;

        return configuration.GetConnectionString("AlfaGestion") ?? string.Empty;
    }

    private static void TryAddKnowledgeBaseHeader(HttpClient client, AlfaKnowledgeOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.KnowledgeBaseId))
            return;

        client.DefaultRequestHeaders.Remove(KnowledgeBaseHeaderName);
        client.DefaultRequestHeaders.Add(KnowledgeBaseHeaderName, settings.KnowledgeBaseId.Trim());
    }

    private static AlfaKnowledgeOptions CreateEmptyOptions()
        => new()
        {
            BaseUrl = string.Empty,
            ApiKey = string.Empty,
            KnowledgeBaseId = string.Empty,
            TimeoutSeconds = 15
        };

    private static async Task<string> ResolveDetailColumnAsync(SqlConnection cn, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        var column = Convert.ToString(result) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static string ResolveDetailColumn(SqlConnection cn)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END
            """;

        using var cmd = new SqlCommand(sql, cn);
        var result = cmd.ExecuteScalar();
        var column = Convert.ToString(result) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static string BuildSelectSql(string detailColumn)
        => $"""
            SELECT
                UPPER(LTRIM(RTRIM(CLAVE))),
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN
            (
                'CONV_ALFAKNOWLEDGE_BASE_URL',
                'CONV_ALFAKNOWLEDGE_API_KEY',
                'CONV_ALFAKNOWLEDGE_KNOWLEDGE_BASE_ID',
                'CONV_ALFAKNOWLEDGE_INSTRUCCIONES',
                'CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS'
            )
            """;

    private static string ReadValue(Dictionary<string, string> values, string key, string fallback)
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();

        return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
    }

    private static int ReadIntValue(Dictionary<string, string> values, string key, int fallback, int defaultValue)
    {
        if (values.TryGetValue(key, out var value)
            && int.TryParse(value, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }

        return fallback > 0 ? fallback : defaultValue;
    }

    private static string ResolveStoredValue(string value, string auxValue)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        return string.IsNullOrWhiteSpace(auxValue) ? string.Empty : auxValue.Trim();
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private sealed class SuggestReplyApiResponse
    {
        public Guid InteractionId { get; set; }
        public string? SuggestedReply { get; set; }
        public bool NeedsClarification { get; set; }
        public string? ClarificationQuestion { get; set; }
        public bool HasSufficientContext { get; set; }
        public bool ImageAnalyzed { get; set; }
        public string? ImageContext { get; set; }
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

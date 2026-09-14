using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class IaBackendProxyService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<IaBackendProxyService> logger) : IIaBackendProxyService
{
    private const int MaxSkewSeconds = 300;
    private const int NonceTtlSeconds = 600;

    // Mismo alcance que _SEEN_NONCES del servidor Python original: en memoria, válido para una
    // única instancia de proceso -- si algún día AlfaCore corre en varias instancias detrás de un
    // balanceador, esto necesitaría un store compartido (Redis/SQL). No es el caso hoy.
    private static readonly ConcurrentDictionary<string, DateTime> SeenNonces = new();

    private string CentralConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public async Task<IaBackendProcessOutcome> ProcessAsync(
        string rawBody,
        string clientId,
        string timestamp,
        string nonce,
        string signature,
        CancellationToken ct = default)
    {
        clientId = (clientId ?? string.Empty).Trim();
        timestamp = (timestamp ?? string.Empty).Trim();
        nonce = (nonce ?? string.Empty).Trim();
        signature = (signature ?? string.Empty).Trim().ToLowerInvariant();
        rawBody ??= string.Empty;

        if (clientId.Length == 0 || timestamp.Length == 0 || nonce.Length == 0 || signature.Length == 0)
            return IaBackendProcessOutcome.Error(401, "missing_auth_headers");

        if (!long.TryParse(timestamp, out var ts))
            return IaBackendProcessOutcome.Error(401, "bad_timestamp");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > MaxSkewSeconds)
            return IaBackendProcessOutcome.Error(401, "timestamp_out_of_range");

        string? secret;
        try
        {
            secret = await ResolveClientSecretAsync(clientId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IaBackendProxy: no se pudo resolver el secreto del cliente {ClientId}.", clientId);
            return IaBackendProcessOutcome.Error(500, "internal_error");
        }

        if (string.IsNullOrWhiteSpace(secret))
            return IaBackendProcessOutcome.Error(403, "unknown_client");

        var nonceKey = $"{clientId}:{nonce}";
        CleanupExpiredNonces();
        if (SeenNonces.ContainsKey(nonceKey))
            return IaBackendProcessOutcome.Error(409, "replay_detected");

        var expectedSignature = BuildSignature(secret, timestamp, nonce, rawBody);
        if (!FixedTimeEquals(expectedSignature, signature))
            return IaBackendProcessOutcome.Error(403, "invalid_signature");

        SeenNonces[nonceKey] = DateTime.UtcNow;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return IaBackendProcessOutcome.Error(400, "invalid_json_body");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var model = GetString(root, "model");
            if (string.IsNullOrWhiteSpace(model))
                return IaBackendProcessOutcome.Error(400, "model_required");

            if (!root.TryGetProperty("input", out var inputElement) || inputElement.ValueKind != JsonValueKind.Array)
                return IaBackendProcessOutcome.Error(400, "input_required");

            var maxOutputTokens = root.TryGetProperty("max_output_tokens", out var tokensEl) && tokensEl.ValueKind == JsonValueKind.Number
                ? tokensEl.GetInt32()
                : 4000;

            var textElement = root.TryGetProperty("text", out var textEl) && textEl.ValueKind != JsonValueKind.Null ? textEl : (JsonElement?)null;

            var opcion = (GetString(root, "opcion") ?? GetString(root, "task") ?? "FACTURAS").Trim().ToUpperInvariant();
            if (opcion.Length == 0) opcion = "FACTURAS";
            var archivo = GetString(root, "archivo_nombre") ?? GetString(root, "source_filename") ?? GetString(root, "filename")
                ?? GetString(root, "archivoNombre") ?? GetString(root, "file_name") ?? string.Empty;
            var idClientePayload = GetInt(root, "idcliente");

            // El límite se controla contra la identidad YA AUTENTICADA (el client_id del HMAC), no
            // contra un "idcliente" que venga suelto en el body -- ese es solo un dato para auditoría.
            var idClienteAutenticado = int.TryParse(clientId, out var idAuth) ? idAuth : (int?)null;
            if (idClienteAutenticado is { } idParaLimite)
            {
                var limiteSuperado = await CheckLimiteSuperadoAsync(idParaLimite, ct);
                if (limiteSuperado)
                {
                    await TryAuditAsync(idClientePayload, opcion, archivo, ok: false, error: "limit_exceeded", duracionMs: 0, ct);
                    return IaBackendProcessOutcome.Error(429, "limit_exceeded");
                }
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string outputText;
            try
            {
                outputText = await CallOpenAiAsync(model, maxOutputTokens, inputElement, textElement, ct);
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "IaBackendProxy: error llamando a OpenAI (modelo {Model}).", model);
                await TryAuditAsync(idClientePayload, opcion, archivo, ok: false, error: $"openai_error: {ex.Message}", duracionMs: (int)sw.ElapsedMilliseconds, ct);
                return IaBackendProcessOutcome.Error(500, $"openai_error: {ex.Message}");
            }
            sw.Stop();

            if (string.IsNullOrWhiteSpace(outputText))
            {
                await TryAuditAsync(idClientePayload, opcion, archivo, ok: false, error: "empty_model_response", duracionMs: (int)sw.ElapsedMilliseconds, ct);
                return IaBackendProcessOutcome.Error(502, "empty_model_response");
            }

            await TryAuditAsync(idClientePayload, opcion, archivo, ok: true, error: string.Empty, duracionMs: (int)sw.ElapsedMilliseconds, ct);
            return IaBackendProcessOutcome.Success(model, outputText);
        }
    }

    public async Task<IaBackendProcessOutcome> ResolveCredentialsAsync(string licenciaPrincipal, CancellationToken ct = default)
    {
        licenciaPrincipal = (licenciaPrincipal ?? string.Empty).Trim();
        if (licenciaPrincipal.Length == 0)
            return IaBackendProcessOutcome.Error(400, "licencia_required");

        try
        {
            await using var cn = new SqlConnection(CentralConnectionString);
            await cn.OpenAsync(ct);

            var row = await cn.QueryFirstOrDefaultAsync<(string IdCliente, string? KeyIa)>(new CommandDefinition(
                """
                SELECT TOP (1) LTRIM(RTRIM(idcliente)) AS IdCliente, LTRIM(RTRIM(KeyIa)) AS KeyIa
                FROM dbo.clientes
                WHERE LTRIM(RTRIM(LicenciaPrincipal)) = LTRIM(RTRIM(@Licencia))
                  AND LEN(LTRIM(RTRIM(ISNULL(LicenciaPrincipal, '')))) > 0;
                """,
                new { Licencia = licenciaPrincipal }, cancellationToken: ct));

            if (row.IdCliente is not { Length: > 0 })
                return IaBackendProcessOutcome.Error(404, "unknown_license");

            var keyIa = row.KeyIa;
            if (string.IsNullOrWhiteSpace(keyIa))
            {
                keyIa = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.clientes SET KeyIa = @KeyIa WHERE LTRIM(RTRIM(idcliente)) = @IdCliente;",
                    new { KeyIa = keyIa, row.IdCliente }, cancellationToken: ct));
                logger.LogInformation("IaBackendProxy: se autogeneró KeyIa para idcliente {IdCliente} (primera vez que resuelve credenciales).", row.IdCliente);
            }

            return new IaBackendProcessOutcome(200, new { ok = true, idcliente = row.IdCliente, keyIa });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IaBackendProxy: no se pudo resolver credenciales para la licencia informada.");
            return IaBackendProcessOutcome.Error(500, "internal_error");
        }
    }

    private async Task<string?> ResolveClientSecretAsync(string clientId, CancellationToken ct)
    {
        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        return await cn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT TOP (1) LTRIM(RTRIM(KeyIa))
            FROM dbo.clientes
            WHERE UPPER(LTRIM(RTRIM(idcliente))) = UPPER(LTRIM(RTRIM(@ClientId)))
              AND LEN(LTRIM(RTRIM(ISNULL(KeyIa, '')))) > 0;
            """,
            new { ClientId = clientId },
            cancellationToken: ct));
    }

    private async Task<bool> CheckLimiteSuperadoAsync(int idCliente, CancellationToken ct)
    {
        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);

        var limite = await cn.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(
            "SELECT TOP (1) limite FROM dbo.IA_LimitesConsultasGPT WHERE idcliente = @IdCliente AND vigente = 1;",
            new { IdCliente = idCliente }, cancellationToken: ct));

        if (limite is not { } limiteValor || limiteValor <= 0)
            return false;

        var inicioMes = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var consumidas = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.IA_ConsultasGPT WHERE idcliente = @IdCliente AND fecha_hora_grabacion >= @InicioMes;",
            new { IdCliente = idCliente, InicioMes = inicioMes }, cancellationToken: ct));

        return consumidas >= limiteValor;
    }

    private async Task TryAuditAsync(int? idCliente, string opcion, string archivo, bool ok, string error, int duracionMs, CancellationToken ct)
    {
        // Igual criterio que el servidor Python original: sin idcliente no hay a quién auditarle la
        // consulta, se omite en vez de insertar un registro huérfano. Nunca debe romper la respuesta
        // ya calculada -- por eso el try/catch silencioso (solo se loguea).
        if (idCliente is not { } id)
            return;

        try
        {
            await using var cn = new SqlConnection(CentralConnectionString);
            await cn.OpenAsync(ct);
            await cn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.IA_ConsultasGPT (idcliente, opcion, archivo_nombre, fecha_hora_grabacion, ok, error, duracion_ms)
                VALUES (@IdCliente, @Opcion, @Archivo, SYSUTCDATETIME(), @Ok, @Error, @DuracionMs);
                """,
                new { IdCliente = id, Opcion = opcion, Archivo = archivo, Ok = ok, Error = error, DuracionMs = duracionMs },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IaBackendProxy: no se pudo auditar la consulta de idcliente {IdCliente}.", id);
        }
    }

    private async Task<string> CallOpenAiAsync(string model, int maxOutputTokens, JsonElement input, JsonElement? text, CancellationToken ct)
    {
        var apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty).Trim();
        if (apiKey.Length == 0)
            throw new InvalidOperationException("Falta OPENAI_API_KEY en el entorno de AlfaCore.");

        var outgoing = new JsonObject
        {
            ["model"] = model,
            ["max_output_tokens"] = maxOutputTokens,
            ["input"] = JsonNode.Parse(input.GetRawText())
        };
        if (text is { } textValue)
            outgoing["text"] = JsonNode.Parse(textValue.GetRawText());

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var content = new StringContent(outgoing.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("https://api.openai.com/v1/responses", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI HTTP {(int)response.StatusCode}: {body}");

        return ExtractOutputText(body);
    }

    // La Responses API cruda NO trae un campo "output_text" de conveniencia (eso lo agrega el SDK
    // de Python/Node) -- hay que caminar output[].content[].text a mano, igual que el fallback que
    // ya tenía el servidor Python original.
    private static string ExtractOutputText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        if (doc.RootElement.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
        {
            var directText = direct.GetString();
            if (!string.IsNullOrWhiteSpace(directText))
                return directText.Trim();
        }

        if (!doc.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var contentArray) || contentArray.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var contentItem in contentArray.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                {
                    var value = textProp.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        parts.Add(value);
                }
            }
        }

        return string.Join("\n", parts).Trim();
    }

    private static void CleanupExpiredNonces()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-NonceTtlSeconds);
        foreach (var (key, seenAt) in SeenNonces)
        {
            if (seenAt < cutoff)
                SeenNonces.TryRemove(key, out _);
        }
    }

    private static string BuildSignature(string secret, string timestamp, string nonce, string body)
    {
        var message = Encoding.UTF8.GetBytes($"{timestamp}.{nonce}.{body}");
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(message);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        if (expectedBytes.Length != actualBytes.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string? GetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static int? GetInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), out var i) => i,
            _ => null
        };
    }
}

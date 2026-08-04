using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class ConversacionesConfigService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IOptions<WhatsAppOptions> whatsAppOptions,
    IHttpClientFactory httpClientFactory) : IConversacionesConfigService
{
    private const string ConfigGroup = "CONVERSACIONES";
    private const string DefaultWebhookPath = "/api/conversaciones/whatsapp/webhook";
    private const string DefaultInstagramWebhookPath = "/api/conversaciones/instagram/webhook";
    private const string DefaultFacebookWebhookPath = "/api/conversaciones/facebook/webhook";
    private const string DefaultMercadoLibreWebhookPath = "/api/conversaciones/mercadolibre/webhook";
    private const string DefaultMercadoLibreOAuthCallbackPath = "/api/conversaciones/mercadolibre/oauth/callback";
    private const string KnowledgeBaseHeaderName = "X-Knowledge-Base-Id";
    private readonly WhatsAppOptions _fallbackOptions = whatsAppOptions.Value;

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetWhatsAppConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var cmd = new SqlCommand(BuildSelectSql(detailColumn), cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var key = GetString(rd, 0);
                var value = GetString(rd, 1);
                var detailValue = GetString(rd, 2);
                values[key] = ResolveStoredValue(value, detailValue);
            }

            var config = new ConversacionWhatsAppConfigDto
            {
                VerifyToken = ReadValue(values, "CONV_WHATSAPP_VERIFY_TOKEN", _fallbackOptions.VerifyToken),
                AccessToken = ReadValue(values, "CONV_WHATSAPP_ACCESS_TOKEN", _fallbackOptions.AccessToken),
                PhoneNumberId = ReadValue(values, "CONV_WHATSAPP_PHONE_NUMBER_ID", _fallbackOptions.PhoneNumberId),
                BusinessAccountId = ReadValue(values, "CONV_WHATSAPP_BUSINESS_ACCOUNT_ID", _fallbackOptions.BusinessAccountId),
                AppSecret = ReadValue(values, "CONV_WHATSAPP_APP_SECRET", string.Empty),
                ApiVersion = ReadValue(values, "CONV_WHATSAPP_API_VERSION", _fallbackOptions.ApiVersion, "v22.0"),
                PublicBaseUrl = ReadValue(values, "CONV_WHATSAPP_PUBLIC_BASE_URL", string.Empty),
                WebhookPath = ReadValue(values, "CONV_WHATSAPP_WEBHOOK_PATH", _fallbackOptions.WebhookPath, DefaultWebhookPath),
                ConfigSource = ResolveConfigSource(values)
            };

            if (string.IsNullOrWhiteSpace(config.WebhookPath))
                config.WebhookPath = DefaultWebhookPath;

            return config;
        }, "No se pudo cargar la configuración de WhatsApp.", ct);

    public Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(string connectionString, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetWhatsAppConfigForConnection", async token =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("La cadena de conexión es obligatoria para cargar la configuración de WhatsApp.");

            return await LoadWhatsAppConfigFromConnectionAsync(connectionString, token);
        }, "No se pudo cargar la configuración de WhatsApp.", ct);

    private async Task<ConversacionWhatsAppConfigDto> LoadWhatsAppConfigFromConnectionAsync(string connectionString, CancellationToken token)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(token);
        var detailColumn = await ResolveDetailColumnAsync(cn, token);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using var cmd = new SqlCommand(BuildSelectSql(detailColumn), cn);
        await using var rd = await cmd.ExecuteReaderAsync(token);
        while (await rd.ReadAsync(token))
        {
            var key = GetString(rd, 0);
            var value = GetString(rd, 1);
            var detailValue = GetString(rd, 2);
            values[key] = ResolveStoredValue(value, detailValue);
        }

        var config = new ConversacionWhatsAppConfigDto
        {
            VerifyToken = ReadValue(values, "CONV_WHATSAPP_VERIFY_TOKEN", _fallbackOptions.VerifyToken),
            AccessToken = ReadValue(values, "CONV_WHATSAPP_ACCESS_TOKEN", _fallbackOptions.AccessToken),
            PhoneNumberId = ReadValue(values, "CONV_WHATSAPP_PHONE_NUMBER_ID", _fallbackOptions.PhoneNumberId),
            BusinessAccountId = ReadValue(values, "CONV_WHATSAPP_BUSINESS_ACCOUNT_ID", _fallbackOptions.BusinessAccountId),
            AppSecret = ReadValue(values, "CONV_WHATSAPP_APP_SECRET", string.Empty),
            ApiVersion = ReadValue(values, "CONV_WHATSAPP_API_VERSION", _fallbackOptions.ApiVersion, "v22.0"),
            PublicBaseUrl = ReadValue(values, "CONV_WHATSAPP_PUBLIC_BASE_URL", string.Empty),
            WebhookPath = ReadValue(values, "CONV_WHATSAPP_WEBHOOK_PATH", _fallbackOptions.WebhookPath, DefaultWebhookPath),
            ConfigSource = ResolveConfigSource(values)
        };

        if (string.IsNullOrWhiteSpace(config.WebhookPath))
            config.WebhookPath = DefaultWebhookPath;

        return config;
    }

    public async Task SaveWhatsAppConfigAsync(ConversacionWhatsAppConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await ExecuteLoggedAsync("Conversaciones", "SaveWhatsAppConfig", async token =>
        {
            var normalized = Normalize(config);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var item in BuildItems(normalized))
            {
                var stored = SplitStoredValue(item.Value);
                var sql = $"""
                    UPDATE dbo.TA_CONFIGURACION
                    SET
                        VALOR = @Valor,
                        {detailColumn} = @ValorAux,
                        GRUPO = @Grupo
                    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO)
                        VALUES (@Clave, @Valor, @ValorAux, @Grupo);
                    END;
                    """;

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);

                cmd.Parameters.AddWithValue("@ClaveNormalizada", item.Key.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Clave", item.Key);
                cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
                cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
                cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "SaveWhatsAppConfig",
                "TA_CONFIGURACION",
                ConfigGroup,
                "Configuración de WhatsApp actualizada.",
                new
                {
                    normalized.PhoneNumberId,
                    normalized.BusinessAccountId,
                    normalized.ApiVersion,
                    normalized.PublicBaseUrl,
                    normalized.WebhookPath
                },
                token);

            return true;
        }, "No se pudo guardar la configuración de WhatsApp.", ct);
    }

    public Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetInstagramConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var cmd = new SqlCommand(BuildInstagramSelectSql(detailColumn), cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var key = GetString(rd, 0);
                var value = GetString(rd, 1);
                var detailValue = GetString(rd, 2);
                values[key] = ResolveStoredValue(value, detailValue);
            }

            var config = new ConversacionInstagramConfigDto
            {
                AppId = ReadValue(values, "CONV_INSTAGRAM_APP_ID", string.Empty),
                AppSecret = ReadValue(values, "CONV_INSTAGRAM_APP_SECRET", string.Empty),
                VerifyToken = ReadValue(values, "CONV_INSTAGRAM_VERIFY_TOKEN", string.Empty),
                AccessToken = ReadValue(values, "CONV_INSTAGRAM_ACCESS_TOKEN", string.Empty),
                InstagramAccountId = ReadValue(values, "CONV_INSTAGRAM_ACCOUNT_ID", string.Empty),
                FacebookPageId = ReadValue(values, "CONV_INSTAGRAM_FACEBOOK_PAGE_ID", string.Empty),
                ApiVersion = ReadValue(values, "CONV_INSTAGRAM_API_VERSION", string.Empty, "v22.0"),
                PublicBaseUrl = ReadValue(values, "CONV_INSTAGRAM_PUBLIC_BASE_URL", string.Empty),
                WebhookPath = ReadValue(values, "CONV_INSTAGRAM_WEBHOOK_PATH", string.Empty, DefaultInstagramWebhookPath),
                ConfigSource = ResolveConfigSource(values, 9)
            };

            if (string.IsNullOrWhiteSpace(config.WebhookPath))
                config.WebhookPath = DefaultInstagramWebhookPath;

            return config;
        }, "No se pudo cargar la configuración de Instagram.", ct);

    public async Task SaveInstagramConfigAsync(ConversacionInstagramConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await ExecuteLoggedAsync("Conversaciones", "SaveInstagramConfig", async token =>
        {
            var normalized = Normalize(config);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var item in BuildItems(normalized))
            {
                var stored = SplitStoredValue(item.Value);
                var sql = $"""
                    UPDATE dbo.TA_CONFIGURACION
                    SET
                        VALOR = @Valor,
                        {detailColumn} = @ValorAux,
                        GRUPO = @Grupo
                    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO)
                        VALUES (@Clave, @Valor, @ValorAux, @Grupo);
                    END;
                    """;

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);

                cmd.Parameters.AddWithValue("@ClaveNormalizada", item.Key.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Clave", item.Key);
                cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
                cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
                cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "SaveInstagramConfig",
                "TA_CONFIGURACION",
                ConfigGroup,
                "Configuración de Instagram actualizada.",
                new
                {
                    normalized.AppId,
                    normalized.InstagramAccountId,
                    normalized.FacebookPageId,
                    normalized.ApiVersion,
                    normalized.PublicBaseUrl,
                    normalized.WebhookPath
                },
                token);

            return true;
        }, "No se pudo guardar la configuración de Instagram.", ct);
    }

    public Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetFacebookConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var cmd = new SqlCommand(BuildFacebookSelectSql(detailColumn), cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var key = GetString(rd, 0);
                var value = GetString(rd, 1);
                var detailValue = GetString(rd, 2);
                values[key] = ResolveStoredValue(value, detailValue);
            }

            var config = new ConversacionFacebookConfigDto
            {
                AppId = ReadValue(values, "CONV_FACEBOOK_APP_ID", string.Empty),
                AppSecret = ReadValue(values, "CONV_FACEBOOK_APP_SECRET", string.Empty),
                VerifyToken = ReadValue(values, "CONV_FACEBOOK_VERIFY_TOKEN", string.Empty),
                AccessToken = ReadValue(values, "CONV_FACEBOOK_ACCESS_TOKEN", string.Empty),
                PageId = ReadValue(values, "CONV_FACEBOOK_PAGE_ID", string.Empty),
                PageUsername = ReadValue(values, "CONV_FACEBOOK_PAGE_USERNAME", string.Empty),
                ApiVersion = ReadValue(values, "CONV_FACEBOOK_API_VERSION", string.Empty, "v22.0"),
                PublicBaseUrl = ReadValue(values, "CONV_FACEBOOK_PUBLIC_BASE_URL", string.Empty),
                WebhookPath = ReadValue(values, "CONV_FACEBOOK_WEBHOOK_PATH", string.Empty, DefaultFacebookWebhookPath),
                ConfigSource = ResolveConfigSource(values, 9)
            };

            if (string.IsNullOrWhiteSpace(config.WebhookPath))
                config.WebhookPath = DefaultFacebookWebhookPath;

            return config;
        }, "No se pudo cargar la configuración de Facebook.", ct);

    public async Task SaveFacebookConfigAsync(ConversacionFacebookConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await ExecuteLoggedAsync("Conversaciones", "SaveFacebookConfig", async token =>
        {
            var normalized = Normalize(config);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var item in BuildItems(normalized))
            {
                var stored = SplitStoredValue(item.Value);
                var sql = $"""
                    UPDATE dbo.TA_CONFIGURACION
                    SET
                        VALOR = @Valor,
                        {detailColumn} = @ValorAux,
                        GRUPO = @Grupo
                    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO)
                        VALUES (@Clave, @Valor, @ValorAux, @Grupo);
                    END;
                    """;

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@ClaveNormalizada", item.Key.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Clave", item.Key);
                cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
                cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
                cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "SaveFacebookConfig",
                "TA_CONFIGURACION",
                ConfigGroup,
                "Configuración de Facebook Messenger actualizada.",
                new
                {
                    normalized.AppId,
                    normalized.PageId,
                    normalized.PageUsername,
                    normalized.ApiVersion,
                    normalized.PublicBaseUrl,
                    normalized.WebhookPath
                },
                token);

            return true;
        }, "No se pudo guardar la configuración de Facebook.", ct);
    }

    public Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetMercadoLibreConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var cmd = new SqlCommand(BuildMercadoLibreSelectSql(detailColumn), cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var key = GetString(rd, 0);
                var value = GetString(rd, 1);
                var detailValue = GetString(rd, 2);
                values[key] = ResolveStoredValue(value, detailValue);
            }

            var config = new ConversacionMercadoLibreConfigDto
            {
                ClientId = ReadValue(values, "CONV_MELI_CLIENT_ID", string.Empty),
                ClientSecret = ReadValue(values, "CONV_MELI_CLIENT_SECRET", string.Empty),
                AccessToken = ReadValue(values, "CONV_MELI_ACCESS_TOKEN", string.Empty),
                RefreshToken = ReadValue(values, "CONV_MELI_REFRESH_TOKEN", string.Empty),
                SellerId = ReadValue(values, "CONV_MELI_SELLER_ID", string.Empty),
                SiteId = ReadValue(values, "CONV_MELI_SITE_ID", string.Empty, "MLA"),
                PublicBaseUrl = ReadValue(values, "CONV_MELI_PUBLIC_BASE_URL", string.Empty),
                WebhookPath = ReadValue(values, "CONV_MELI_WEBHOOK_PATH", string.Empty, DefaultMercadoLibreWebhookPath),
                OAuthCallbackPath = ReadValue(values, "CONV_MELI_OAUTH_CALLBACK_PATH", string.Empty, DefaultMercadoLibreOAuthCallbackPath),
                ApiBaseUrl = ReadValue(values, "CONV_MELI_API_BASE_URL", string.Empty, "https://api.mercadolibre.com"),
                ConfigSource = ResolveConfigSource(values, 10)
            };

            if (string.IsNullOrWhiteSpace(config.WebhookPath))
                config.WebhookPath = DefaultMercadoLibreWebhookPath;
            if (string.IsNullOrWhiteSpace(config.OAuthCallbackPath))
                config.OAuthCallbackPath = DefaultMercadoLibreOAuthCallbackPath;

            return config;
        }, "No se pudo cargar la configuración de Mercado Libre.", ct);

    public async Task SaveMercadoLibreConfigAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await ExecuteLoggedAsync("Conversaciones", "SaveMercadoLibreConfig", async token =>
        {
            var normalized = Normalize(config);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var item in BuildItems(normalized))
            {
                var stored = SplitStoredValue(item.Value);
                var sql = $"""
                    UPDATE dbo.TA_CONFIGURACION
                    SET
                        VALOR = @Valor,
                        {detailColumn} = @ValorAux,
                        GRUPO = @Grupo
                    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO)
                        VALUES (@Clave, @Valor, @ValorAux, @Grupo);
                    END;
                    """;

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@ClaveNormalizada", item.Key.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Clave", item.Key);
                cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
                cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
                cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "SaveMercadoLibreConfig",
                "TA_CONFIGURACION",
                ConfigGroup,
                "Configuración de Mercado Libre actualizada.",
                new
                {
                    normalized.ClientId,
                    normalized.SellerId,
                    normalized.SiteId,
                    normalized.PublicBaseUrl,
                    normalized.WebhookPath,
                    normalized.OAuthCallbackPath
                },
                token);

            return true;
        }, "No se pudo guardar la configuración de Mercado Libre.", ct);
    }

    public Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetAlfaKnowledgeConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var cmd = new SqlCommand(BuildAlfaKnowledgeSelectSql(detailColumn), cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var key = GetString(rd, 0);
                var value = GetString(rd, 1);
                var detailValue = GetString(rd, 2);
                values[key] = ResolveStoredValue(value, detailValue);
            }

            return new ConversacionAlfaKnowledgeConfigDto
            {
                BaseUrl = ReadValue(values, "CONV_ALFAKNOWLEDGE_BASE_URL", string.Empty),
                ApiKey = ReadValue(values, "CONV_ALFAKNOWLEDGE_API_KEY", string.Empty),
                KnowledgeBaseId = ReadValue(values, "CONV_ALFAKNOWLEDGE_KNOWLEDGE_BASE_ID", string.Empty),
                TimeoutSeconds = ReadIntValue(values, "CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS", 0, 15),
                ConfigSource = values.Count == 0 ? "sin_configurar" : "TA_CONFIGURACION"
            };
        }, "No se pudo cargar la configuración de AlfaKnowledge.", ct);

    public async Task SaveAlfaKnowledgeConfigAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await ExecuteLoggedAsync("Conversaciones", "SaveAlfaKnowledgeConfig", async token =>
        {
            var normalized = Normalize(config);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var item in BuildItems(normalized))
            {
                var stored = SplitStoredValue(item.Value);
                var sql = $"""
                    UPDATE dbo.TA_CONFIGURACION
                    SET
                        VALOR = @Valor,
                        {detailColumn} = @ValorAux,
                        GRUPO = @Grupo
                    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO)
                        VALUES (@Clave, @Valor, @ValorAux, @Grupo);
                    END;
                    """;

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@ClaveNormalizada", item.Key.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Clave", item.Key);
                cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
                cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
                cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "SaveAlfaKnowledgeConfig",
                "TA_CONFIGURACION",
                ConfigGroup,
                "Configuración de AlfaKnowledge actualizada.",
                new
                {
                    normalized.BaseUrl,
                    normalized.TimeoutSeconds
                },
                token);

            return true;
        }, "No se pudo guardar la configuración de AlfaKnowledge.", ct);
    }

    public Task<ConversacionAlfaKnowledgeConnectionTestResultDto> TestAlfaKnowledgeConnectionAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "TestAlfaKnowledgeConnection", async token =>
        {
            ArgumentNullException.ThrowIfNull(config);

            var normalized = Normalize(config);
            if (string.IsNullOrWhiteSpace(normalized.BaseUrl))
                throw new InvalidOperationException("Completá la Base URL de AlfaKnowledge antes de probar la conexión.");

            if (string.IsNullOrWhiteSpace(normalized.ApiKey))
                throw new InvalidOperationException("Completá la API Key de AlfaKnowledge antes de probar la conexión.");

            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Max(normalized.TimeoutSeconds, 1));

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{normalized.BaseUrl}/api/health/db");
            request.Headers.Add("X-Api-Key", normalized.ApiKey);

            if (!string.IsNullOrWhiteSpace(normalized.KnowledgeBaseId))
                request.Headers.Add(KnowledgeBaseHeaderName, normalized.KnowledgeBaseId);

            using var response = await client.SendAsync(request, token);
            var body = await response.Content.ReadAsStringAsync(token);

            string service = string.Empty;
            string database = string.Empty;
            string dataSource = string.Empty;
            string knowledgeBase = string.Empty;
            string message = string.Empty;

            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                service = ReadJsonString(root, "service");
                database = ReadJsonString(root, "database");
                dataSource = ReadJsonString(root, "dataSource");
                knowledgeBase = ReadJsonString(root, "knowledgeBase");
                message = ReadJsonString(root, "message");
            }

            return new ConversacionAlfaKnowledgeConnectionTestResultDto
            {
                Success = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                Service = service,
                Database = database,
                DataSource = dataSource,
                KnowledgeBase = knowledgeBase,
                Message = message
            };
        }, "No se pudo probar la conexión con AlfaKnowledge.", ct);

    private static async Task<string> ResolveDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        // Acepta ValorAux / VALOR_AUX / valor_aux y cae en DESCRIPCION solo como último recurso.
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var column = Convert.ToString(result) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(column))
            throw new InvalidOperationException("TA_CONFIGURACION no tiene columna ValorAux ni DESCRIPCION disponibles para guardar la configuración extendida.");

        return column;
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
                'CONV_WHATSAPP_VERIFY_TOKEN',
                'CONV_WHATSAPP_ACCESS_TOKEN',
                'CONV_WHATSAPP_PHONE_NUMBER_ID',
                'CONV_WHATSAPP_BUSINESS_ACCOUNT_ID',
                'CONV_WHATSAPP_APP_SECRET',
                'CONV_WHATSAPP_API_VERSION',
                'CONV_WHATSAPP_PUBLIC_BASE_URL',
                'CONV_WHATSAPP_WEBHOOK_PATH'
            )
            """;

    private static string BuildInstagramSelectSql(string detailColumn)
        => $"""
            SELECT
                UPPER(LTRIM(RTRIM(CLAVE))),
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN
            (
                'CONV_INSTAGRAM_APP_ID',
                'CONV_INSTAGRAM_APP_SECRET',
                'CONV_INSTAGRAM_VERIFY_TOKEN',
                'CONV_INSTAGRAM_ACCESS_TOKEN',
                'CONV_INSTAGRAM_ACCOUNT_ID',
                'CONV_INSTAGRAM_FACEBOOK_PAGE_ID',
                'CONV_INSTAGRAM_API_VERSION',
                'CONV_INSTAGRAM_PUBLIC_BASE_URL',
                'CONV_INSTAGRAM_WEBHOOK_PATH'
            )
            """;

    private static string BuildFacebookSelectSql(string detailColumn)
        => $"""
            SELECT
                UPPER(LTRIM(RTRIM(CLAVE))),
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN
            (
                'CONV_FACEBOOK_APP_ID',
                'CONV_FACEBOOK_APP_SECRET',
                'CONV_FACEBOOK_VERIFY_TOKEN',
                'CONV_FACEBOOK_ACCESS_TOKEN',
                'CONV_FACEBOOK_PAGE_ID',
                'CONV_FACEBOOK_PAGE_USERNAME',
                'CONV_FACEBOOK_API_VERSION',
                'CONV_FACEBOOK_PUBLIC_BASE_URL',
                'CONV_FACEBOOK_WEBHOOK_PATH'
            )
            """;

    private static string BuildMercadoLibreSelectSql(string detailColumn)
        => $"""
            SELECT
                UPPER(LTRIM(RTRIM(CLAVE))),
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN
            (
                'CONV_MELI_CLIENT_ID',
                'CONV_MELI_CLIENT_SECRET',
                'CONV_MELI_ACCESS_TOKEN',
                'CONV_MELI_REFRESH_TOKEN',
                'CONV_MELI_SELLER_ID',
                'CONV_MELI_SITE_ID',
                'CONV_MELI_PUBLIC_BASE_URL',
                'CONV_MELI_WEBHOOK_PATH',
                'CONV_MELI_OAUTH_CALLBACK_PATH',
                'CONV_MELI_API_BASE_URL'
            )
            """;

    private static string BuildAlfaKnowledgeSelectSql(string detailColumn)
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
                'CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS'
            )
            """;

    private static IEnumerable<(string Key, string Value)> BuildItems(ConversacionWhatsAppConfigDto config)
    {
        yield return ("CONV_WHATSAPP_VERIFY_TOKEN", config.VerifyToken);
        yield return ("CONV_WHATSAPP_ACCESS_TOKEN", config.AccessToken);
        yield return ("CONV_WHATSAPP_PHONE_NUMBER_ID", config.PhoneNumberId);
        yield return ("CONV_WHATSAPP_BUSINESS_ACCOUNT_ID", config.BusinessAccountId);
        yield return ("CONV_WHATSAPP_APP_SECRET", config.AppSecret);
        yield return ("CONV_WHATSAPP_API_VERSION", config.ApiVersion);
        yield return ("CONV_WHATSAPP_PUBLIC_BASE_URL", config.PublicBaseUrl);
        yield return ("CONV_WHATSAPP_WEBHOOK_PATH", config.WebhookPath);
    }

    private static IEnumerable<(string Key, string Value)> BuildItems(ConversacionInstagramConfigDto config)
    {
        yield return ("CONV_INSTAGRAM_APP_ID", config.AppId);
        yield return ("CONV_INSTAGRAM_APP_SECRET", config.AppSecret);
        yield return ("CONV_INSTAGRAM_VERIFY_TOKEN", config.VerifyToken);
        yield return ("CONV_INSTAGRAM_ACCESS_TOKEN", config.AccessToken);
        yield return ("CONV_INSTAGRAM_ACCOUNT_ID", config.InstagramAccountId);
        yield return ("CONV_INSTAGRAM_FACEBOOK_PAGE_ID", config.FacebookPageId);
        yield return ("CONV_INSTAGRAM_API_VERSION", config.ApiVersion);
        yield return ("CONV_INSTAGRAM_PUBLIC_BASE_URL", config.PublicBaseUrl);
        yield return ("CONV_INSTAGRAM_WEBHOOK_PATH", config.WebhookPath);
    }

    private static IEnumerable<(string Key, string Value)> BuildItems(ConversacionFacebookConfigDto config)
    {
        yield return ("CONV_FACEBOOK_APP_ID", config.AppId);
        yield return ("CONV_FACEBOOK_APP_SECRET", config.AppSecret);
        yield return ("CONV_FACEBOOK_VERIFY_TOKEN", config.VerifyToken);
        yield return ("CONV_FACEBOOK_ACCESS_TOKEN", config.AccessToken);
        yield return ("CONV_FACEBOOK_PAGE_ID", config.PageId);
        yield return ("CONV_FACEBOOK_PAGE_USERNAME", config.PageUsername);
        yield return ("CONV_FACEBOOK_API_VERSION", config.ApiVersion);
        yield return ("CONV_FACEBOOK_PUBLIC_BASE_URL", config.PublicBaseUrl);
        yield return ("CONV_FACEBOOK_WEBHOOK_PATH", config.WebhookPath);
    }

    private static IEnumerable<(string Key, string Value)> BuildItems(ConversacionMercadoLibreConfigDto config)
    {
        yield return ("CONV_MELI_CLIENT_ID", config.ClientId);
        yield return ("CONV_MELI_CLIENT_SECRET", config.ClientSecret);
        yield return ("CONV_MELI_ACCESS_TOKEN", config.AccessToken);
        yield return ("CONV_MELI_REFRESH_TOKEN", config.RefreshToken);
        yield return ("CONV_MELI_SELLER_ID", config.SellerId);
        yield return ("CONV_MELI_SITE_ID", config.SiteId);
        yield return ("CONV_MELI_PUBLIC_BASE_URL", config.PublicBaseUrl);
        yield return ("CONV_MELI_WEBHOOK_PATH", config.WebhookPath);
        yield return ("CONV_MELI_OAUTH_CALLBACK_PATH", config.OAuthCallbackPath);
        yield return ("CONV_MELI_API_BASE_URL", config.ApiBaseUrl);
    }

    private static IEnumerable<(string Key, string Value)> BuildItems(ConversacionAlfaKnowledgeConfigDto config)
    {
        yield return ("CONV_ALFAKNOWLEDGE_BASE_URL", config.BaseUrl);
        yield return ("CONV_ALFAKNOWLEDGE_API_KEY", config.ApiKey);
        yield return ("CONV_ALFAKNOWLEDGE_KNOWLEDGE_BASE_ID", config.KnowledgeBaseId);
        yield return ("CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS", config.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static ConversacionWhatsAppConfigDto Normalize(ConversacionWhatsAppConfigDto config)
    {
        var path = string.IsNullOrWhiteSpace(config.WebhookPath) ? DefaultWebhookPath : config.WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return new ConversacionWhatsAppConfigDto
        {
            VerifyToken = (config.VerifyToken ?? string.Empty).Trim(),
            AccessToken = (config.AccessToken ?? string.Empty).Trim(),
            PhoneNumberId = (config.PhoneNumberId ?? string.Empty).Trim(),
            BusinessAccountId = (config.BusinessAccountId ?? string.Empty).Trim(),
            AppSecret = (config.AppSecret ?? string.Empty).Trim(),
            ApiVersion = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v22.0" : config.ApiVersion.Trim(),
            PublicBaseUrl = NormalizeBaseUrl(config.PublicBaseUrl),
            WebhookPath = path,
            ConfigSource = string.Empty
        };
    }

    private static ConversacionInstagramConfigDto Normalize(ConversacionInstagramConfigDto config)
    {
        var path = string.IsNullOrWhiteSpace(config.WebhookPath) ? DefaultInstagramWebhookPath : config.WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return new ConversacionInstagramConfigDto
        {
            AppId = (config.AppId ?? string.Empty).Trim(),
            AppSecret = (config.AppSecret ?? string.Empty).Trim(),
            VerifyToken = (config.VerifyToken ?? string.Empty).Trim(),
            AccessToken = (config.AccessToken ?? string.Empty).Trim(),
            InstagramAccountId = (config.InstagramAccountId ?? string.Empty).Trim(),
            FacebookPageId = (config.FacebookPageId ?? string.Empty).Trim(),
            ApiVersion = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v22.0" : config.ApiVersion.Trim(),
            PublicBaseUrl = NormalizeBaseUrl(config.PublicBaseUrl),
            WebhookPath = path,
            ConfigSource = string.Empty
        };
    }

    private static ConversacionFacebookConfigDto Normalize(ConversacionFacebookConfigDto config)
    {
        var path = string.IsNullOrWhiteSpace(config.WebhookPath) ? DefaultFacebookWebhookPath : config.WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return new ConversacionFacebookConfigDto
        {
            AppId = (config.AppId ?? string.Empty).Trim(),
            AppSecret = (config.AppSecret ?? string.Empty).Trim(),
            VerifyToken = (config.VerifyToken ?? string.Empty).Trim(),
            AccessToken = (config.AccessToken ?? string.Empty).Trim(),
            PageId = (config.PageId ?? string.Empty).Trim(),
            PageUsername = (config.PageUsername ?? string.Empty).Trim().TrimStart('@'),
            ApiVersion = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v22.0" : config.ApiVersion.Trim(),
            PublicBaseUrl = NormalizeBaseUrl(config.PublicBaseUrl),
            WebhookPath = path,
            ConfigSource = string.Empty
        };
    }

    private static ConversacionMercadoLibreConfigDto Normalize(ConversacionMercadoLibreConfigDto config)
    {
        var webhookPath = string.IsNullOrWhiteSpace(config.WebhookPath) ? DefaultMercadoLibreWebhookPath : config.WebhookPath.Trim();
        if (!webhookPath.StartsWith('/'))
            webhookPath = "/" + webhookPath;

        var callbackPath = string.IsNullOrWhiteSpace(config.OAuthCallbackPath) ? DefaultMercadoLibreOAuthCallbackPath : config.OAuthCallbackPath.Trim();
        if (!callbackPath.StartsWith('/'))
            callbackPath = "/" + callbackPath;

        var apiBaseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "https://api.mercadolibre.com"
            : NormalizeBaseUrl(config.ApiBaseUrl);

        return new ConversacionMercadoLibreConfigDto
        {
            ClientId = (config.ClientId ?? string.Empty).Trim(),
            ClientSecret = (config.ClientSecret ?? string.Empty).Trim(),
            AccessToken = (config.AccessToken ?? string.Empty).Trim(),
            RefreshToken = (config.RefreshToken ?? string.Empty).Trim(),
            SellerId = (config.SellerId ?? string.Empty).Trim(),
            SiteId = string.IsNullOrWhiteSpace(config.SiteId) ? "MLA" : config.SiteId.Trim().ToUpperInvariant(),
            PublicBaseUrl = NormalizeBaseUrl(config.PublicBaseUrl),
            WebhookPath = webhookPath,
            OAuthCallbackPath = callbackPath,
            ApiBaseUrl = apiBaseUrl,
            ConfigSource = string.Empty
        };
    }

    private static ConversacionAlfaKnowledgeConfigDto Normalize(ConversacionAlfaKnowledgeConfigDto config)
    {
        var timeoutSeconds = config.TimeoutSeconds <= 0 ? 15 : config.TimeoutSeconds;

        return new ConversacionAlfaKnowledgeConfigDto
        {
            BaseUrl = NormalizeBaseUrl(config.BaseUrl),
            ApiKey = (config.ApiKey ?? string.Empty).Trim(),
            KnowledgeBaseId = (config.KnowledgeBaseId ?? string.Empty).Trim(),
            TimeoutSeconds = timeoutSeconds,
            ConfigSource = string.Empty
        };
    }

    private static string NormalizeBaseUrl(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('/');

    private static string ResolveConfigSource(Dictionary<string, string> values, int expectedKeys = 8)
    {
        if (values.Count == 0)
            return "appsettings";

        var hasFallback = values.Count < expectedKeys;
        return hasFallback ? "mixta" : "TA_CONFIGURACION";
    }

    private static string ReadValue(Dictionary<string, string> values, string key, string fallback, string defaultValue = "")
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();

        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();

        return defaultValue;
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

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (normalized.Length > 150)
            return (string.Empty, normalized);

        return (normalized, string.Empty);
    }

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static string ReadJsonString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return string.Empty;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new InvalidOperationException("La tabla TA_CONFIGURACION no está disponible en la base activa.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }
}

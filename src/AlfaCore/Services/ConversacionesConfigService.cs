using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QRCoder;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class ConversacionesConfigService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IOptions<WhatsAppOptions> whatsAppOptions,
    IHttpClientFactory httpClientFactory,
    IAppUserSessionService appUserSession) : IConversacionesConfigService
{
    private const string ConfigGroup = "CONVERSACIONES";
    private const string DefaultUrgenciaPalabras =
        "no puedo facturar, no me deja facturar, no factura, no anda el sistema, no arranca, no abre el sistema, sistema caido, no hay sistema, se cerro el sistema, no funciona el sistema";
    private const string DefaultWebhookPath = "/api/conversaciones/whatsapp/webhook";
    private const string DefaultInstagramWebhookPath = "/api/conversaciones/instagram/webhook";
    private const string DefaultFacebookWebhookPath = "/api/conversaciones/facebook/webhook";
    private const string DefaultMercadoLibreWebhookPath = "/api/conversaciones/mercadolibre/webhook";
    private const string DefaultMercadoLibreOAuthCallbackPath = "/api/conversaciones/mercadolibre/oauth/callback";
    private const string KnowledgeBaseHeaderName = "X-Knowledge-Base-Id";
    private const int WhatsAppExpectedConfigKeyCount = 21;
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
                ProviderMode = ReadValue(values, "CONV_WHATSAPP_PROVIDER_MODE", string.Empty, ConversacionWhatsAppProviderModes.MetaOnly),
                DefaultProvider = ReadValue(values, "CONV_WHATSAPP_DEFAULT_PROVIDER", string.Empty, ConversacionWhatsAppProviders.MetaCloud),
                VerifyToken = ReadValue(values, "CONV_WHATSAPP_VERIFY_TOKEN", _fallbackOptions.VerifyToken),
                AccessToken = ReadValue(values, "CONV_WHATSAPP_ACCESS_TOKEN", _fallbackOptions.AccessToken),
                PhoneNumberId = ReadValue(values, "CONV_WHATSAPP_PHONE_NUMBER_ID", _fallbackOptions.PhoneNumberId),
                BusinessAccountId = ReadValue(values, "CONV_WHATSAPP_BUSINESS_ACCOUNT_ID", _fallbackOptions.BusinessAccountId),
                AppSecret = ReadValue(values, "CONV_WHATSAPP_APP_SECRET", string.Empty),
                ApiVersion = ReadValue(values, "CONV_WHATSAPP_API_VERSION", _fallbackOptions.ApiVersion, "v22.0"),
                PublicBaseUrl = ReadValue(values, "CONV_WHATSAPP_PUBLIC_BASE_URL", string.Empty),
                WebhookPath = ReadValue(values, "CONV_WHATSAPP_WEBHOOK_PATH", _fallbackOptions.WebhookPath, DefaultWebhookPath),
                WebSessionMode = ReadValue(values, "CONV_WHATSAPP_WEB_SESSION_MODE", string.Empty, ConversacionWhatsAppWebSessionModes.Qr),
                WebPhoneNumber = ReadValue(values, "CONV_WHATSAPP_WEB_PHONE_NUMBER", string.Empty),
                WebSessionStatus = ReadValue(values, "CONV_WHATSAPP_WEB_SESSION_STATUS", string.Empty, ConversacionWhatsAppWebSessionStatuses.Disconnected),
                WebDisplayName = ReadValue(values, "CONV_WHATSAPP_WEB_DISPLAY_NAME", string.Empty),
                WebInstanceName = ReadValue(values, "CONV_WHATSAPP_WEB_INSTANCE_NAME", string.Empty),
                WebPairingToken = ReadValue(values, "CONV_WHATSAPP_WEB_PAIRING_TOKEN", string.Empty),
                WebPairingCode = ReadValue(values, "CONV_WHATSAPP_WEB_PAIRING_CODE", string.Empty),
                WebPairingQrPayload = ReadValue(values, "CONV_WHATSAPP_WEB_PAIRING_QR_PAYLOAD", string.Empty),
                WebPairingQrDataUrl = ReadValue(values, "CONV_WHATSAPP_WEB_PAIRING_QR_DATA_URL", string.Empty),
                WebPairingGeneratedAtUtc = ReadDateTimeValue(values, "CONV_WHATSAPP_WEB_PAIRING_GENERATED_AT_UTC"),
                WebPairingExpiresAtUtc = ReadDateTimeValue(values, "CONV_WHATSAPP_WEB_PAIRING_EXPIRES_AT_UTC"),
                ConfigSource = ResolveConfigSource(values, WhatsAppExpectedConfigKeyCount)
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
            ProviderMode = ReadValue(values, "CONV_WHATSAPP_PROVIDER_MODE", string.Empty, ConversacionWhatsAppProviderModes.MetaOnly),
            DefaultProvider = ReadValue(values, "CONV_WHATSAPP_DEFAULT_PROVIDER", string.Empty, ConversacionWhatsAppProviders.MetaCloud),
            VerifyToken = ReadValue(values, "CONV_WHATSAPP_VERIFY_TOKEN", _fallbackOptions.VerifyToken),
            AccessToken = ReadValue(values, "CONV_WHATSAPP_ACCESS_TOKEN", _fallbackOptions.AccessToken),
            PhoneNumberId = ReadValue(values, "CONV_WHATSAPP_PHONE_NUMBER_ID", _fallbackOptions.PhoneNumberId),
            BusinessAccountId = ReadValue(values, "CONV_WHATSAPP_BUSINESS_ACCOUNT_ID", _fallbackOptions.BusinessAccountId),
            AppSecret = ReadValue(values, "CONV_WHATSAPP_APP_SECRET", string.Empty),
            ApiVersion = ReadValue(values, "CONV_WHATSAPP_API_VERSION", _fallbackOptions.ApiVersion, "v22.0"),
            PublicBaseUrl = ReadValue(values, "CONV_WHATSAPP_PUBLIC_BASE_URL", string.Empty),
            WebhookPath = ReadValue(values, "CONV_WHATSAPP_WEBHOOK_PATH", _fallbackOptions.WebhookPath, DefaultWebhookPath),
            WebSessionMode = ReadValue(values, "CONV_WHATSAPP_WEB_SESSION_MODE", string.Empty, ConversacionWhatsAppWebSessionModes.Qr),
            WebPhoneNumber = ReadValue(values, "CONV_WHATSAPP_WEB_PHONE_NUMBER", string.Empty),
            WebSessionStatus = ReadValue(values, "CONV_WHATSAPP_WEB_SESSION_STATUS", string.Empty, ConversacionWhatsAppWebSessionStatuses.Disconnected),
            WebDisplayName = ReadValue(values, "CONV_WHATSAPP_WEB_DISPLAY_NAME", string.Empty),
            WebInstanceName = ReadValue(values, "CONV_WHATSAPP_WEB_INSTANCE_NAME", string.Empty),
            WebPairingToken = ReadValue(values, "CONV_WHATSAPP_WEB_PAIRING_TOKEN", string.Empty),
            WebPairingCode = ReadValue(values, "CONV_WHATSAPP_WEB_PAIRING_CODE", string.Empty),
            WebPairingQrPayload = ReadValue(values, "CONV_WHATSAPP_WEB_PAIRING_QR_PAYLOAD", string.Empty),
            WebPairingQrDataUrl = ReadValue(values, "CONV_WHATSAPP_WEB_PAIRING_QR_DATA_URL", string.Empty),
            WebPairingGeneratedAtUtc = ReadDateTimeValue(values, "CONV_WHATSAPP_WEB_PAIRING_GENERATED_AT_UTC"),
            WebPairingExpiresAtUtc = ReadDateTimeValue(values, "CONV_WHATSAPP_WEB_PAIRING_EXPIRES_AT_UTC"),
            ConfigSource = ResolveConfigSource(values, WhatsAppExpectedConfigKeyCount)
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
                    normalized.ProviderMode,
                    normalized.DefaultProvider,
                    normalized.PhoneNumberId,
                    normalized.BusinessAccountId,
                    normalized.ApiVersion,
                    normalized.PublicBaseUrl,
                    normalized.WebhookPath,
                    normalized.WebSessionMode,
                    normalized.WebSessionStatus,
                    normalized.WebInstanceName
                },
                token);

            return true;
        }, "No se pudo guardar la configuración de WhatsApp.", ct);
    }

    public Task<ConversacionWhatsAppConfigDto> GenerateWhatsAppWebPairingAsync(ConversacionWhatsAppWebPairingRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GenerateWhatsAppWebPairing", async token =>
        {
            request ??= new ConversacionWhatsAppWebPairingRequestDto();

            var config = await GetWhatsAppConfigAsync(token);
            var normalized = Normalize(config);
            var nowUtc = DateTime.UtcNow;
            var expiresUtc = nowUtc.AddMinutes(2);
            var instanceName = string.IsNullOrWhiteSpace(normalized.WebInstanceName)
                ? $"waweb-{Guid.NewGuid():N}"[..14]
                : normalized.WebInstanceName;
            var pairingToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var pairingCode = request.IncludeTextCode ? BuildPairingCode() : string.Empty;
            var payload = BuildWhatsAppWebPairingPayload(instanceName, normalized.WebSessionMode, normalized.WebPhoneNumber, pairingToken, pairingCode, expiresUtc);
            var qrDataUrl = BuildQrCodeDataUrl(payload);

            normalized.WebInstanceName = instanceName;
            normalized.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.PendingQr;
            normalized.WebPairingToken = pairingToken;
            normalized.WebPairingCode = pairingCode;
            normalized.WebPairingQrPayload = payload;
            normalized.WebPairingQrDataUrl = qrDataUrl;
            normalized.WebPairingGeneratedAtUtc = nowUtc;
            normalized.WebPairingExpiresAtUtc = expiresUtc;

            await SaveWhatsAppConfigAsync(normalized, token);
            return normalized;
        }, "No se pudo generar el QR de WhatsApp Web.", ct);

    public Task<ConversacionWhatsAppConfigDto> ClearWhatsAppWebPairingAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "ClearWhatsAppWebPairing", async token =>
        {
            var config = await GetWhatsAppConfigAsync(token);
            var normalized = Normalize(config);
            normalized.WebPairingToken = string.Empty;
            normalized.WebPairingCode = string.Empty;
            normalized.WebPairingQrPayload = string.Empty;
            normalized.WebPairingQrDataUrl = string.Empty;
            normalized.WebPairingGeneratedAtUtc = null;
            normalized.WebPairingExpiresAtUtc = null;
            if (!string.Equals(normalized.WebSessionStatus, ConversacionWhatsAppWebSessionStatuses.Connected, StringComparison.OrdinalIgnoreCase))
                normalized.WebSessionStatus = ConversacionWhatsAppWebSessionStatuses.Disconnected;

            await SaveWhatsAppConfigAsync(normalized, token);
            return normalized;
        }, "No se pudo limpiar el emparejamiento de WhatsApp Web.", ct);

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
                Instrucciones = ReadValue(values, "CONV_ALFAKNOWLEDGE_INSTRUCCIONES", string.Empty),
                TimeoutSeconds = ReadIntValue(values, "CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS", 0, 15),
                ConfigSource = values.Count == 0 ? "sin_configurar" : "TA_CONFIGURACION"
            };
        }, "No se pudo cargar la configuración de AlfaKnowledge.", ct);

    public Task SaveAlfaKnowledgeConfigAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return SaveAlfaKnowledgeConfigForConnectionAsync(ConnectionString, config, ct);
    }

    public Task SaveAlfaKnowledgeConfigForConnectionAsync(string connectionString, ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("La cadena de conexión es obligatoria para guardar la configuración de AlfaKnowledge.");

        return ExecuteLoggedAsync("Conversaciones", "SaveAlfaKnowledgeConfig", async token =>
        {
            var normalized = Normalize(config);

            await using var cn = new SqlConnection(connectionString);
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

    public Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetAutomatizacionesConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Bloque propio para que el DataReader se cierre antes de leer CONV_ASISTENTE en la misma
            // conexión (sin MARS, dos readers abiertos a la vez tiran "ya hay un DataReader abierto").
            await using (var cmd = new SqlCommand(BuildAutomatizacionesSelectSql(detailColumn), cn))
            await using (var rd = await cmd.ExecuteReaderAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    var key = GetString(rd, 0);
                    var value = GetString(rd, 1);
                    var detailValue = GetString(rd, 2);
                    values[key] = ResolveStoredValue(value, detailValue);
                }
            }

            var dias = ReadValue(values, "CONV_AUTOMATIZACIONES_DIAS", string.Empty, "LUN,MAR,MIE,JUE,VIE")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var diasSet = new HashSet<string>(dias, StringComparer.OrdinalIgnoreCase);

            var asistente = await ReadAsistenteAsync(cn, token);

            return new ConversacionAutomatizacionesConfigDto
            {
                AsistenteComportamiento = asistente.Comportamiento,
                AsistenteInformacion = asistente.Informacion,
                AsistentePolitica = asistente.Politica,
                Activo = ReadValue(values, "CONV_AUTOMATIZACIONES_ACTIVO", string.Empty) == "1",
                MensajeFueraHorario = ReadValue(values, "CONV_AUTOMATIZACIONES_MENSAJE", string.Empty,
                    "Gracias por escribirnos. Estamos fuera de nuestro horario de atención, te vamos a responder a la brevedad."),
                Lunes = diasSet.Contains("LUN"),
                Martes = diasSet.Contains("MAR"),
                Miercoles = diasSet.Contains("MIE"),
                Jueves = diasSet.Contains("JUE"),
                Viernes = diasSet.Contains("VIE"),
                Sabado = diasSet.Contains("SAB"),
                Domingo = diasSet.Contains("DOM"),
                HoraDesde = ReadValue(values, "CONV_AUTOMATIZACIONES_HORA_DESDE", string.Empty, "09:00"),
                HoraHasta = ReadValue(values, "CONV_AUTOMATIZACIONES_HORA_HASTA", string.Empty, "18:00"),
                BienvenidaActivo = ReadValue(values, "CONV_BIENVENIDA_ACTIVO", string.Empty) == "1",
                BienvenidaMensaje = ReadValue(values, "CONV_BIENVENIDA_MENSAJE", string.Empty,
                    "¡Hola! Gracias por escribirnos. En un momento te atendemos. 🙂"),
                BotActivo = ReadValue(values, "CONV_BOT_ACTIVO", string.Empty) == "1",
                BotSoloSinAsignar = ReadValue(values, "CONV_BOT_SOLO_SIN_ASIGNAR", string.Empty, "1") != "0",
                BotPalabrasEscalado = ReadValue(values, "CONV_BOT_PALABRAS_ESCALADO", string.Empty,
                    "humano, persona, reclamo, operador, hablar con alguien"),
                BotMaxRespuestas = ReadIntValue(values, "CONV_BOT_MAX_RESPUESTAS", 0, 5),
                BotSoloFueraHorario = ReadValue(values, "CONV_BOT_SOLO_FUERA_HORARIO", string.Empty) == "1",
                BotEsperaMinutos = ReadIntValue(values, "CONV_BOT_ESPERA_MINUTOS", 0, 0),
                AutoCierreActivo = ReadValue(values, "CONV_AUTOCIERRE_ACTIVO", string.Empty) == "1",
                AutoCierreHorasAviso = ReadIntValue(values, "CONV_AUTOCIERRE_HORAS_AVISO", 0, 23),
                AutoCierreHorasCierre = ReadIntValue(values, "CONV_AUTOCIERRE_HORAS_CIERRE", 0, 24),
                AutoCierreMensajeAviso = ReadValue(values, "CONV_AUTOCIERRE_MENSAJE_AVISO", string.Empty,
                    "¿Seguís ahí? Si no tenemos novedades, vamos a cerrar esta conversación. Escribinos cuando quieras. 🙂"),
                AutoCierreMensajeCierre = ReadValue(values, "CONV_AUTOCIERRE_MENSAJE_CIERRE", string.Empty,
                    "Cerramos esta conversación por inactividad. Cuando lo necesites, escribinos de nuevo. ¡Gracias!"),
                SlaActivo = ReadValue(values, "CONV_SLA_ACTIVO", string.Empty) == "1",
                SlaHorasRecordatorio = ReadIntValue(values, "CONV_SLA_HORAS_RECORDATORIO", 0, 2),
                SlaHorasReasignar = ReadIntValue(values, "CONV_SLA_HORAS_REASIGNAR", 0, 4),
                AsistenteFueraHorario = ReadValue(values, "CONV_ASISTENTE_FUERA_HORARIO", string.Empty) == "1",
                AsistenteUrgenciaPalabras = ReadValue(values, "CONV_ASISTENTE_URGENCIA_PALABRAS", string.Empty, DefaultUrgenciaPalabras),
                AsistenteUsaKnowledge = ReadValue(values, "CONV_ASISTENTE_USA_KNOWLEDGE", string.Empty, "1") != "0",
                AsistenteHerramientaPrecios = ReadValue(values, "CONV_ASISTENTE_HERRAMIENTA_PRECIOS", string.Empty) == "1",
                AsistenteHerramientaSaldoCliente = ReadValue(values, "CONV_ASISTENTE_HERRAMIENTA_SALDO_CLIENTE", string.Empty) == "1",
                AsistenteHerramientaSaldoProveedor = ReadValue(values, "CONV_ASISTENTE_HERRAMIENTA_SALDO_PROVEEDOR", string.Empty) == "1",
                AsistenteHerramientaPedidos = ReadValue(values, "CONV_ASISTENTE_HERRAMIENTA_PEDIDOS", string.Empty) == "1",
                AsistenteHerramientaPortalLink = ReadValue(values, "CONV_ASISTENTE_HERRAMIENTA_PORTAL_LINK", string.Empty) == "1",
                InformeInstrucciones = ReadValue(values, "CONV_INFORME_INSTRUCCIONES", string.Empty, ConversacionAutomatizacionesConfigDto.DefaultInformeInstrucciones),
                ConfigSource = values.Count == 0 ? "sin_configurar" : "TA_CONFIGURACION"
            };
        }, "No se pudo cargar la configuración de automatizaciones.", ct);

    public Task SaveAutomatizacionesConfigAsync(ConversacionAutomatizacionesConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        return ExecuteLoggedAsync("Conversaciones", "SaveAutomatizacionesConfig", async token =>
        {
            var dias = new List<string>();
            if (config.Lunes) dias.Add("LUN");
            if (config.Martes) dias.Add("MAR");
            if (config.Miercoles) dias.Add("MIE");
            if (config.Jueves) dias.Add("JUE");
            if (config.Viernes) dias.Add("VIE");
            if (config.Sabado) dias.Add("SAB");
            if (config.Domingo) dias.Add("DOM");

            var items = new[]
            {
                ("CONV_AUTOMATIZACIONES_ACTIVO", config.Activo ? "1" : "0"),
                ("CONV_AUTOMATIZACIONES_MENSAJE", (config.MensajeFueraHorario ?? string.Empty).Trim()),
                ("CONV_AUTOMATIZACIONES_DIAS", string.Join(',', dias)),
                ("CONV_AUTOMATIZACIONES_HORA_DESDE", (config.HoraDesde ?? string.Empty).Trim()),
                ("CONV_AUTOMATIZACIONES_HORA_HASTA", (config.HoraHasta ?? string.Empty).Trim()),
                ("CONV_BIENVENIDA_ACTIVO", config.BienvenidaActivo ? "1" : "0"),
                ("CONV_BIENVENIDA_MENSAJE", (config.BienvenidaMensaje ?? string.Empty).Trim()),
                ("CONV_BOT_ACTIVO", config.BotActivo ? "1" : "0"),
                ("CONV_BOT_SOLO_SIN_ASIGNAR", config.BotSoloSinAsignar ? "1" : "0"),
                ("CONV_BOT_PALABRAS_ESCALADO", (config.BotPalabrasEscalado ?? string.Empty).Trim()),
                ("CONV_BOT_MAX_RESPUESTAS", (config.BotMaxRespuestas <= 0 ? 5 : config.BotMaxRespuestas).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("CONV_BOT_SOLO_FUERA_HORARIO", config.BotSoloFueraHorario ? "1" : "0"),
                ("CONV_BOT_ESPERA_MINUTOS", Math.Max(0, config.BotEsperaMinutos).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("CONV_AUTOCIERRE_ACTIVO", config.AutoCierreActivo ? "1" : "0"),
                ("CONV_AUTOCIERRE_HORAS_AVISO", (config.AutoCierreHorasAviso <= 0 ? 23 : config.AutoCierreHorasAviso).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("CONV_AUTOCIERRE_HORAS_CIERRE", (config.AutoCierreHorasCierre <= 0 ? 24 : config.AutoCierreHorasCierre).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("CONV_AUTOCIERRE_MENSAJE_AVISO", (config.AutoCierreMensajeAviso ?? string.Empty).Trim()),
                ("CONV_AUTOCIERRE_MENSAJE_CIERRE", (config.AutoCierreMensajeCierre ?? string.Empty).Trim()),
                ("CONV_SLA_ACTIVO", config.SlaActivo ? "1" : "0"),
                ("CONV_SLA_HORAS_RECORDATORIO", (config.SlaHorasRecordatorio <= 0 ? 2 : config.SlaHorasRecordatorio).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("CONV_SLA_HORAS_REASIGNAR", (config.SlaHorasReasignar <= 0 ? 4 : config.SlaHorasReasignar).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("CONV_ASISTENTE_FUERA_HORARIO", config.AsistenteFueraHorario ? "1" : "0"),
                ("CONV_ASISTENTE_URGENCIA_PALABRAS", (config.AsistenteUrgenciaPalabras ?? string.Empty).Trim()),
                ("CONV_ASISTENTE_USA_KNOWLEDGE", config.AsistenteUsaKnowledge ? "1" : "0"),
                ("CONV_ASISTENTE_HERRAMIENTA_PRECIOS", config.AsistenteHerramientaPrecios ? "1" : "0"),
                ("CONV_ASISTENTE_HERRAMIENTA_SALDO_CLIENTE", config.AsistenteHerramientaSaldoCliente ? "1" : "0"),
                ("CONV_ASISTENTE_HERRAMIENTA_SALDO_PROVEEDOR", config.AsistenteHerramientaSaldoProveedor ? "1" : "0"),
                ("CONV_ASISTENTE_HERRAMIENTA_PEDIDOS", config.AsistenteHerramientaPedidos ? "1" : "0"),
                ("CONV_ASISTENTE_HERRAMIENTA_PORTAL_LINK", config.AsistenteHerramientaPortalLink ? "1" : "0"),
                ("CONV_INFORME_INSTRUCCIONES", (config.InformeInstrucciones ?? string.Empty).Trim())
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var item in items)
            {
                var stored = SplitStoredValue(item.Item2);
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
                cmd.Parameters.AddWithValue("@ClaveNormalizada", item.Item1.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@Clave", item.Item1);
                cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
                cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
                cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await SaveAsistenteAsync(cn, (SqlTransaction)tx, config, token);

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "SaveAutomatizacionesConfig",
                "TA_CONFIGURACION",
                ConfigGroup,
                "Configuración de automatizaciones actualizada.",
                new { config.Activo, Dias = dias },
                token);

            return true;
        }, "No se pudo guardar la configuración de automatizaciones.", ct);
    }

    public Task<IReadOnlyList<ConversacionReglaDto>> GetReglasAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetReglas", async token =>
        {
            var reglas = new List<ConversacionReglaDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // La tabla puede no existir todavía en bases donde no corrió la migración: devolvemos vacío.
            const string sql = """
                IF OBJECT_ID(N'dbo.CONV_REGLAS', N'U') IS NULL
                    SELECT TOP (0) 0 AS IdRegla, N'' AS Nombre, CAST(0 AS bit) AS Activa, 0 AS Orden,
                        N'' AS TipoCoincidencia, N'' AS Palabras, N'' AS Canal, CAST(0 AS bit) AS SoloSinAsignar,
                        N'' AS RespuestaTexto, N'' AS AsignarTecnico, N'' AS Prioridad, CAST(0 AS bit) AS Detener,
                        N'SIEMPRE' AS Horario, CAST(0 AS bit) AS SoloPrimerContacto
                ELSE
                    SELECT IdRegla, ISNULL(Nombre, N''), ISNULL(Activa, 0), ISNULL(Orden, 100),
                        ISNULL(TipoCoincidencia, N'CONTIENE'), ISNULL(Palabras, N''), ISNULL(Canal, N''),
                        ISNULL(SoloSinAsignar, 1), ISNULL(RespuestaTexto, N''), ISNULL(LTRIM(RTRIM(AsignarTecnico)), N''),
                        ISNULL(Prioridad, N''), ISNULL(Detener, 1),
                        ISNULL(Horario, N'SIEMPRE'), ISNULL(SoloPrimerContacto, 0)
                    FROM dbo.CONV_REGLAS
                    ORDER BY Orden, IdRegla;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                reglas.Add(new ConversacionReglaDto
                {
                    IdRegla = rd.GetInt32(0),
                    Nombre = GetString(rd, 1),
                    Activa = GetBool(rd, 2),
                    Orden = rd.IsDBNull(3) ? 100 : rd.GetInt32(3),
                    TipoCoincidencia = GetString(rd, 4),
                    Palabras = GetString(rd, 5),
                    Canal = GetString(rd, 6),
                    SoloSinAsignar = GetBool(rd, 7),
                    RespuestaTexto = GetString(rd, 8),
                    AsignarTecnico = GetString(rd, 9),
                    Prioridad = GetString(rd, 10),
                    Detener = GetBool(rd, 11),
                    Horario = GetString(rd, 12),
                    SoloPrimerContacto = GetBool(rd, 13)
                });
            }

            return (IReadOnlyList<ConversacionReglaDto>)reglas;
        }, "No se pudieron cargar las reglas de conversaciones.", ct);

    public Task<int> SaveReglaAsync(ConversacionReglaDto regla, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(regla);
        if (string.IsNullOrWhiteSpace(regla.Nombre))
            throw new InvalidOperationException("La regla necesita un nombre.");

        return ExecuteLoggedAsync("Conversaciones", "SaveRegla", async token =>
        {
            var tipo = NormalizeTipoCoincidencia(regla.TipoCoincidencia);
            var canal = NormalizeReglaCanal(regla.Canal);
            var prioridad = NormalizeReglaPrioridad(regla.Prioridad);
            var horario = NormalizeReglaHorario(regla.Horario);
            var tecnico = string.IsNullOrWhiteSpace(regla.AsignarTecnico) ? null : regla.AsignarTecnico.Trim();
            var usuario = appUserSession.GetCurrentUserName("SYSTEM");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await using var cmd = new SqlCommand(regla.IdRegla > 0
                ? """
                    UPDATE dbo.CONV_REGLAS SET
                        Nombre = @Nombre, Activa = @Activa, Orden = @Orden, TipoCoincidencia = @Tipo,
                        Palabras = @Palabras, Canal = @Canal, SoloSinAsignar = @SoloSinAsignar,
                        RespuestaTexto = @Respuesta, AsignarTecnico = @Tecnico, Prioridad = @Prioridad,
                        Detener = @Detener, Horario = @Horario, SoloPrimerContacto = @SoloPrimerContacto,
                        FechaModificacion = GETDATE(), UsuarioModificacion = @Usuario
                    WHERE IdRegla = @IdRegla;
                    SELECT @IdRegla;
                    """
                : """
                    INSERT INTO dbo.CONV_REGLAS
                        (Nombre, Activa, Orden, TipoCoincidencia, Palabras, Canal, SoloSinAsignar,
                         RespuestaTexto, AsignarTecnico, Prioridad, Detener, Horario, SoloPrimerContacto,
                         FechaAlta, UsuarioAlta)
                    VALUES
                        (@Nombre, @Activa, @Orden, @Tipo, @Palabras, @Canal, @SoloSinAsignar,
                         @Respuesta, @Tecnico, @Prioridad, @Detener, @Horario, @SoloPrimerContacto,
                         GETDATE(), @Usuario);
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """, cn);

            cmd.Parameters.AddWithValue("@Nombre", regla.Nombre.Trim());
            cmd.Parameters.AddWithValue("@Activa", regla.Activa);
            cmd.Parameters.AddWithValue("@Orden", regla.Orden <= 0 ? 100 : regla.Orden);
            cmd.Parameters.AddWithValue("@Tipo", tipo);
            cmd.Parameters.AddWithValue("@Palabras", (regla.Palabras ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("@Canal", canal);
            cmd.Parameters.AddWithValue("@SoloSinAsignar", regla.SoloSinAsignar);
            cmd.Parameters.AddWithValue("@Respuesta", (regla.RespuestaTexto ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("@Tecnico", DbNullable(tecnico));
            cmd.Parameters.AddWithValue("@Prioridad", string.IsNullOrEmpty(prioridad) ? (object)DBNull.Value : prioridad);
            cmd.Parameters.AddWithValue("@Detener", regla.Detener);
            cmd.Parameters.AddWithValue("@Horario", horario);
            cmd.Parameters.AddWithValue("@SoloPrimerContacto", regla.SoloPrimerContacto);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            if (regla.IdRegla > 0)
                cmd.Parameters.AddWithValue("@IdRegla", regla.IdRegla);

            var result = await cmd.ExecuteScalarAsync(token);
            var id = result is int i ? i : regla.IdRegla;

            await appEvents.LogAuditAsync(
                "Conversaciones", "SaveRegla", "CONV_REGLAS",
                id.ToString(CultureInfo.InvariantCulture),
                regla.IdRegla > 0 ? "Regla de conversaciones actualizada." : "Regla de conversaciones creada.",
                new { id, regla.Nombre, regla.Activa }, token);

            return id;
        }, "No se pudo guardar la regla de conversaciones.", ct);
    }

    public Task DeleteReglaAsync(int idRegla, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "DeleteRegla", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand("DELETE FROM dbo.CONV_REGLAS WHERE IdRegla = @IdRegla;", cn);
            cmd.Parameters.AddWithValue("@IdRegla", idRegla);
            await cmd.ExecuteNonQueryAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones", "DeleteRegla", "CONV_REGLAS",
                idRegla.ToString(CultureInfo.InvariantCulture),
                "Regla de conversaciones eliminada.", new { idRegla }, token);

            return true;
        }, "No se pudo eliminar la regla de conversaciones.", ct);

    private static string NormalizeTipoCoincidencia(string? tipo)
    {
        var t = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        return t is "IGUAL" or "EMPIEZA" ? t : "CONTIENE";
    }

    private static string NormalizeReglaCanal(string? canal)
    {
        var c = (canal ?? string.Empty).Trim().ToUpperInvariant();
        return c is "WHATSAPP" or "INSTAGRAM" or "FACEBOOK" or "MERCADOLIBRE" ? c : string.Empty;
    }

    private static string NormalizeReglaPrioridad(string? prioridad)
    {
        var p = (prioridad ?? string.Empty).Trim().ToUpperInvariant();
        return p is "BAJA" or "MEDIA" or "ALTA" or "URGENTE" ? p : string.Empty;
    }

    private static string NormalizeReglaHorario(string? horario)
    {
        var h = (horario ?? string.Empty).Trim().ToUpperInvariant();
        return h is "DENTRO" or "FUERA" ? h : "SIEMPRE";
    }

    private static string NormalizeAsistentePolitica(string? politica)
    {
        var p = (politica ?? string.Empty).Trim().ToUpperInvariant();
        return p is "SOLO_INFO" or "GENERAL" ? p : "GENERAL_AVISA";
    }

    private static async Task<(string Comportamiento, string Informacion, string Politica)> ReadAsistenteAsync(
        SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_ASISTENTE', N'U') IS NULL
                SELECT TOP (0) N'' AS Comportamiento, N'' AS Informacion, N'GENERAL_AVISA' AS Politica
            ELSE
                SELECT ISNULL(Comportamiento, N''), ISNULL(Informacion, N''), ISNULL(Politica, N'GENERAL_AVISA')
                FROM dbo.CONV_ASISTENTE WHERE Id = 1;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (await rd.ReadAsync(ct))
            return (GetString(rd, 0), GetString(rd, 1), NormalizeAsistentePolitica(GetString(rd, 2)));

        return (string.Empty, string.Empty, "GENERAL_AVISA");
    }

    private async Task SaveAsistenteAsync(SqlConnection cn, SqlTransaction tx,
        ConversacionAutomatizacionesConfigDto config, CancellationToken ct)
    {
        // Si la tabla no existe (migración no aplicada), no interrumpimos el guardado del resto.
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_ASISTENTE', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.CONV_ASISTENTE SET
                    Comportamiento = @Comportamiento, Informacion = @Informacion, Politica = @Politica,
                    FechaModificacion = GETDATE(), UsuarioModificacion = @Usuario
                WHERE Id = 1;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.CONV_ASISTENTE (Id, Comportamiento, Informacion, Politica, FechaModificacion, UsuarioModificacion)
                    VALUES (1, @Comportamiento, @Informacion, @Politica, GETDATE(), @Usuario);
            END;
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@Comportamiento", (config.AsistenteComportamiento ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("@Informacion", (config.AsistenteInformacion ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("@Politica", NormalizeAsistentePolitica(config.AsistentePolitica));
        cmd.Parameters.AddWithValue("@Usuario", appUserSession.GetCurrentUserName("SYSTEM"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetPrioridadConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            const string sql = """
                SELECT
                    UPPER(LTRIM(RTRIM(CLAVE))),
                    ISNULL(VALOR, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('CLASIFICA1', 'CLASIFICA2', 'CLASIFICA3')
                """;

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
                values[GetString(rd, 0)] = GetString(rd, 1);

            return new ConversacionPrioridadConfigDto
            {
                Clasifica1 = ReadValue(values, "CLASIFICA1", string.Empty),
                Clasifica2 = ReadValue(values, "CLASIFICA2", string.Empty),
                Clasifica3 = ReadValue(values, "CLASIFICA3", string.Empty)
            };
        }, "No se pudo cargar la configuración de prioridad de atención.", ct);

    public Task SavePrioridadConfigAsync(ConversacionPrioridadConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        return ExecuteLoggedAsync("Conversaciones", "SavePrioridadConfig", async token =>
        {
            var items = new[]
            {
                ("CLASIFICA1", (config.Clasifica1 ?? string.Empty).Trim()),
                ("CLASIFICA2", (config.Clasifica2 ?? string.Empty).Trim()),
                ("CLASIFICA3", (config.Clasifica3 ?? string.Empty).Trim())
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var (clave, valor) in items)
            {
                // Grupo DATOS a proposito: son las mismas claves globales que ya usa Desktop
                // (sp1_GrabaCfg), no exclusivas de Conversaciones — no las re-agrupamos.
                var sql = $"""
                    UPDATE dbo.TA_CONFIGURACION
                    SET VALOR = @Valor
                    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, GRUPO)
                        VALUES (@Clave, @Valor, 'DATOS');
                    END;
                    """;

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@ClaveNormalizada", clave);
                cmd.Parameters.AddWithValue("@Clave", clave);
                cmd.Parameters.AddWithValue("@Valor", DbNullable(valor));
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "SavePrioridadConfig",
                "TA_CONFIGURACION",
                "CLASIFICA1/2/3",
                "Prioridad de atención por clasificación actualizada.",
                new { config.Clasifica1, config.Clasifica2, config.Clasifica3 },
                token);

            return true;
        }, "No se pudo guardar la configuración de prioridad de atención.", ct);
    }

    public Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetClasificaciones", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // El código se devuelve SIN espacios: los códigos de TA_CLASIFICACIONES vienen
            // right-justified ("   1") y el valor guardado en CLASIFICA1/2/3 se guarda trimmeado,
            // así que el <option> debe usar el código trimmeado para que el select seleccione bien.
            const string sql = """
                SELECT DISTINCT ISNULL(LTRIM(RTRIM(Codigo)), ''), ISNULL(Descripcion, '')
                FROM dbo.TA_CLASIFICACIONES
                WHERE LTRIM(RTRIM(ISNULL(Codigo, ''))) <> ''
                ORDER BY 2, 1
                """;

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            var result = new List<ConversacionClasificacionOptionDto>();
            while (await rd.ReadAsync(token))
                result.Add(new ConversacionClasificacionOptionDto(GetString(rd, 0), GetString(rd, 1)));

            return (IReadOnlyList<ConversacionClasificacionOptionDto>)result;
        }, "No se pudieron cargar las clasificaciones de clientes.", ct);

    // Consulta TA_USUARIOS directo acá (en vez de reusar AutorizacionTareasService) a propósito:
    // AutorizacionTareasService depende de ICentralAdminService, que a su vez depende de
    // IConversacionesConfigService -- inyectar AutorizacionTareasService acá cierra un ciclo en el
    // contenedor de DI y tira "circular dependency detected" al arrancar la app.
    public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetUsuariosSistema", async token =>
        {
            var sistema = (appUserSession.CurrentUser?.SystemCode ?? string.Empty).Trim().ToUpperInvariant();
            if (sistema.Length == 0)
                return (IReadOnlyList<UsuarioSistemaDto>)Array.Empty<UsuarioSistemaDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string tableExistsSql = "SELECT COUNT(1) FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.TA_USUARIOS');";
            await using (var checkCmd = new SqlCommand(tableExistsSql, cn))
            {
                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) > 0;
                if (!exists)
                    return (IReadOnlyList<UsuarioSistemaDto>)Array.Empty<UsuarioSistemaDto>();
            }

            const string columnExistsSql = "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.TA_USUARIOS') AND LOWER(name) = 'activo';";
            bool hasActivo;
            await using (var checkCmd = new SqlCommand(columnExistsSql, cn))
            {
                hasActivo = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) > 0;
            }

            var sql = $"""
                SELECT
                    ISNULL(NOMBRE, ''),
                    ISNULL(SISTEMA, ''),
                    {(hasActivo ? "ISNULL(Activo, 1)" : "CAST(1 AS bit)")},
                    ISNULL(Administrador, 0),
                    ISNULL(EsGrupo, 0)
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND ISNULL(EsGrupo, 0) = 0
                  {(hasActivo ? "AND ISNULL(Activo, 1) = 1" : string.Empty)}
                ORDER BY NOMBRE;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Sistema", sistema);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            var result = new List<UsuarioSistemaDto>();
            while (await rd.ReadAsync(token))
            {
                result.Add(new UsuarioSistemaDto
                {
                    Nombre = GetString(rd, 0),
                    Sistema = GetString(rd, 1),
                    Activo = GetBool(rd, 2),
                    Administrador = GetBool(rd, 3),
                    EsGrupo = GetBool(rd, 4)
                });
            }

            return (IReadOnlyList<UsuarioSistemaDto>)result;
        }, "No se pudieron cargar los usuarios del sistema.", ct);

    public Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetWhatsAppNumeros", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sistema = (appUserSession.CurrentUser?.SystemCode ?? string.Empty).Trim().ToUpperInvariant();
            var numeroColumns = await GetTableColumnsAsync(cn, "dbo.CONV_WHATSAPP_NUMEROS", token);

            var numeros = new List<ConversacionWhatsAppNumeroDto>();
            var sqlNumeros = $"""
                SELECT
                    IdNumero,
                    ISNULL(PhoneNumberId, ''),
                    ISNULL(Nombre, ''),
                    {(HasColumn(numeroColumns, "Activo") ? "Activo" : "CAST(1 AS bit)")},
                    {SelectStringColumn(numeroColumns, "WebSessionMode")},
                    {SelectStringColumn(numeroColumns, "WebPhoneNumber")},
                    {SelectStringColumn(numeroColumns, "WebSessionStatus")},
                    {SelectStringColumn(numeroColumns, "WebDisplayName")},
                    {SelectStringColumn(numeroColumns, "WebInstanceName")},
                    {SelectStringColumn(numeroColumns, "WebPairingToken")},
                    {SelectStringColumn(numeroColumns, "WebPairingCode")},
                    {SelectStringColumn(numeroColumns, "WebPairingQrPayload")},
                    {SelectStringColumn(numeroColumns, "WebRuntimeState")},
                    {SelectStringColumn(numeroColumns, "WebLastError")},
                    {SelectNullableIntColumn(numeroColumns, "WebWorkerProcessId")},
                    {SelectNullableDateColumn(numeroColumns, "WebPairingGeneratedAtUtc")},
                    {SelectNullableDateColumn(numeroColumns, "WebPairingExpiresAtUtc")},
                    {SelectNullableDateColumn(numeroColumns, "WebRuntimeUpdatedAtUtc")}
                FROM dbo.CONV_WHATSAPP_NUMEROS
                ORDER BY Nombre;
                """;
            await using (var cmd = new SqlCommand(sqlNumeros, cn))
            await using (var rd = await cmd.ExecuteReaderAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    numeros.Add(MapWhatsAppNumero(rd));
                }
            }

            var usuariosPorNumero = new Dictionary<int, List<string>>();
            const string sqlUsuarios = """
                SELECT IdNumero, Usuario
                FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(Sistema))) = @Sistema;
                """;
            await using (var cmd = new SqlCommand(sqlUsuarios, cn))
            {
                cmd.Parameters.AddWithValue("@Sistema", sistema);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                while (await rd.ReadAsync(token))
                {
                    var idNumero = rd.GetInt32(0);
                    var usuario = GetString(rd, 1);
                    if (!usuariosPorNumero.TryGetValue(idNumero, out var lista))
                    {
                        lista = [];
                        usuariosPorNumero[idNumero] = lista;
                    }
                    lista.Add(usuario);
                }
            }

            foreach (var numero in numeros)
                numero.Usuarios = usuariosPorNumero.TryGetValue(numero.IdNumero, out var lista) ? lista : [];

            return (IReadOnlyList<ConversacionWhatsAppNumeroDto>)numeros;
        }, "No se pudieron cargar los números de WhatsApp.", ct);

    public async Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default)
    {
        if (idNumero <= 0)
            return null;

        var numeros = await GetWhatsAppNumerosAsync(ct);
        return numeros.FirstOrDefault(x => x.IdNumero == idNumero);
    }

    public async Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroByInstanceNameAsync(string instanceName, CancellationToken ct = default)
    {
        var normalized = (instanceName ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return null;

        var numeros = await GetWhatsAppNumerosAsync(ct);
        return numeros.FirstOrDefault(x => string.Equals(x.WebInstanceName, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public Task SaveWhatsAppNumeroAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(numero);

        return ExecuteLoggedAsync("Conversaciones", "SaveWhatsAppNumero", async token =>
        {
            var phoneNumberId = (numero.PhoneNumberId ?? string.Empty).Trim();
            var nombre = (numero.Nombre ?? string.Empty).Trim();
            if (phoneNumberId.Length == 0)
                throw new InvalidOperationException("El Phone Number ID es obligatorio.");
            if (nombre.Length == 0)
                throw new InvalidOperationException("El nombre del número es obligatorio.");

            var sistema = (appUserSession.CurrentUser?.SystemCode ?? string.Empty).Trim().ToUpperInvariant();
            if (sistema.Length == 0)
                throw new InvalidOperationException("No se pudo determinar el sistema del usuario actual.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var numeroColumns = await GetTableColumnsAsync(cn, "dbo.CONV_WHATSAPP_NUMEROS", token);
            var hasWebColumns = HasAnyWhatsAppWebColumns(numeroColumns);
            await using var tx = await cn.BeginTransactionAsync(token);

            int idNumero;
            if (numero.IdNumero > 0)
            {
                var sqlUpdate = $"""
                    UPDATE dbo.CONV_WHATSAPP_NUMEROS
                    SET
                        PhoneNumberId = @PhoneNumberId,
                        Nombre = @Nombre,
                        Activo = @Activo{BuildWhatsAppNumeroWebUpdateAssignments(numeroColumns)},
                        FechaHora_Modificacion = GETDATE()
                    WHERE IdNumero = @IdNumero;
                    """;
                await using var cmd = new SqlCommand(sqlUpdate, cn, (SqlTransaction)tx);
                FillWhatsAppNumeroParameters(cmd, numero, phoneNumberId, nombre, includeWebFields: hasWebColumns);
                cmd.Parameters.AddWithValue("@IdNumero", numero.IdNumero);
                await cmd.ExecuteNonQueryAsync(token);
                idNumero = numero.IdNumero;
            }
            else
            {
                var sqlInsert = $"""
                    INSERT INTO dbo.CONV_WHATSAPP_NUMEROS
                    (
                        PhoneNumberId,
                        Nombre,
                        Activo{BuildWhatsAppNumeroWebInsertColumns(numeroColumns)}
                    )
                    OUTPUT INSERTED.IdNumero
                    VALUES
                    (
                        @PhoneNumberId,
                        @Nombre,
                        @Activo{BuildWhatsAppNumeroWebInsertValues(numeroColumns)}
                    );
                    """;
                await using var cmd = new SqlCommand(sqlInsert, cn, (SqlTransaction)tx);
                FillWhatsAppNumeroParameters(cmd, numero, phoneNumberId, nombre, includeWebFields: hasWebColumns);
                idNumero = (int)(await cmd.ExecuteScalarAsync(token))!;
            }

            const string sqlDeleteUsuarios = """
                DELETE FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS
                WHERE IdNumero = @IdNumero AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema;
                """;
            await using (var cmd = new SqlCommand(sqlDeleteUsuarios, cn, (SqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@IdNumero", idNumero);
                cmd.Parameters.AddWithValue("@Sistema", sistema);
                await cmd.ExecuteNonQueryAsync(token);
            }

            foreach (var usuario in (numero.Usuarios ?? []).Select(u => (u ?? string.Empty).Trim()).Where(u => u.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                const string sqlInsertUsuario = """
                    INSERT INTO dbo.CONV_WHATSAPP_NUMERO_USUARIOS (IdNumero, Usuario, Sistema)
                    VALUES (@IdNumero, @Usuario, @Sistema);
                    """;
                await using var cmd = new SqlCommand(sqlInsertUsuario, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@IdNumero", idNumero);
                cmd.Parameters.AddWithValue("@Usuario", usuario);
                cmd.Parameters.AddWithValue("@Sistema", sistema);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "SaveWhatsAppNumero",
                "CONV_WHATSAPP_NUMEROS",
                phoneNumberId,
                $"Número de WhatsApp '{nombre}' guardado.",
                new { idNumero, phoneNumberId, nombre, numero.Activo, numero.Usuarios },
                token);

            return true;
        }, "No se pudo guardar el número de WhatsApp.", ct);
    }

    public Task SaveWhatsAppNumeroWebSessionAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(numero);
        if (numero.IdNumero <= 0)
            throw new InvalidOperationException("El número de WhatsApp debe existir antes de guardar su sesión Web.");

        return ExecuteLoggedAsync("Conversaciones", "SaveWhatsAppNumeroWebSession", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var numeroColumns = await GetTableColumnsAsync(cn, "dbo.CONV_WHATSAPP_NUMEROS", token);
            if (!HasAnyWhatsAppWebColumns(numeroColumns))
            {
                throw new InvalidOperationException(
                    "La base activa todavía no tiene las columnas de sesión Web por número. Corré la actualización 2026-08-18-010 antes de generar QR o vincular teléfonos.");
            }

            var sql = $"""
                UPDATE dbo.CONV_WHATSAPP_NUMEROS
                SET
                    FechaHora_Modificacion = GETDATE(){BuildWhatsAppNumeroWebUpdateAssignments(numeroColumns)}
                WHERE IdNumero = @IdNumero;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            FillWhatsAppNumeroParameters(cmd, numero, numero.PhoneNumberId, numero.Nombre, includeMetaFields: false, includeWebFields: true);
            cmd.Parameters.AddWithValue("@IdNumero", numero.IdNumero);
            await cmd.ExecuteNonQueryAsync(token);
            return true;
        }, "No se pudo guardar la sesión Web del número de WhatsApp.", ct);
    }

    public Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetConversacionAdministradores", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sistema = (appUserSession.CurrentUser?.SystemCode ?? string.Empty).Trim().ToUpperInvariant();

            const string sql = """
                SELECT Usuario
                FROM dbo.CONV_ADMINISTRADORES
                WHERE UPPER(LTRIM(RTRIM(Sistema))) = @Sistema;
                """;
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Sistema", sistema);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            var result = new List<string>();
            while (await rd.ReadAsync(token))
                result.Add(GetString(rd, 0));

            return (IReadOnlyList<string>)result;
        }, "No se pudieron cargar los administradores de conversaciones.", ct);

    public Task SaveConversacionAdministradoresAsync(IReadOnlyList<string> usuarios, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuarios);

        return ExecuteLoggedAsync("Conversaciones", "SaveConversacionAdministradores", async token =>
        {
            var sistema = (appUserSession.CurrentUser?.SystemCode ?? string.Empty).Trim().ToUpperInvariant();
            if (sistema.Length == 0)
                throw new InvalidOperationException("No se pudo determinar el sistema del usuario actual.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            const string sqlDelete = """
                DELETE FROM dbo.CONV_ADMINISTRADORES
                WHERE UPPER(LTRIM(RTRIM(Sistema))) = @Sistema;
                """;
            await using (var cmd = new SqlCommand(sqlDelete, cn, (SqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@Sistema", sistema);
                await cmd.ExecuteNonQueryAsync(token);
            }

            foreach (var usuario in usuarios.Select(u => (u ?? string.Empty).Trim()).Where(u => u.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                const string sqlInsert = """
                    INSERT INTO dbo.CONV_ADMINISTRADORES (Usuario, Sistema)
                    VALUES (@Usuario, @Sistema);
                    """;
                await using var cmd = new SqlCommand(sqlInsert, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@Usuario", usuario);
                cmd.Parameters.AddWithValue("@Sistema", sistema);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "SaveConversacionAdministradores",
                "CONV_ADMINISTRADORES",
                sistema,
                "Lista de administradores de conversaciones actualizada.",
                new { Usuarios = usuarios },
                token);

            return true;
        }, "No se pudieron guardar los administradores de conversaciones.", ct);
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
                'CONV_WHATSAPP_PROVIDER_MODE',
                'CONV_WHATSAPP_DEFAULT_PROVIDER',
                'CONV_WHATSAPP_VERIFY_TOKEN',
                'CONV_WHATSAPP_ACCESS_TOKEN',
                'CONV_WHATSAPP_PHONE_NUMBER_ID',
                'CONV_WHATSAPP_BUSINESS_ACCOUNT_ID',
                'CONV_WHATSAPP_APP_SECRET',
                'CONV_WHATSAPP_API_VERSION',
                'CONV_WHATSAPP_PUBLIC_BASE_URL',
                'CONV_WHATSAPP_WEB_SESSION_MODE',
                'CONV_WHATSAPP_WEB_PHONE_NUMBER',
                'CONV_WHATSAPP_WEB_SESSION_STATUS',
                'CONV_WHATSAPP_WEB_DISPLAY_NAME',
                'CONV_WHATSAPP_WEB_INSTANCE_NAME',
                'CONV_WHATSAPP_WEB_PAIRING_TOKEN',
                'CONV_WHATSAPP_WEB_PAIRING_CODE',
                'CONV_WHATSAPP_WEB_PAIRING_QR_PAYLOAD',
                'CONV_WHATSAPP_WEB_PAIRING_QR_DATA_URL',
                'CONV_WHATSAPP_WEB_PAIRING_GENERATED_AT_UTC',
                'CONV_WHATSAPP_WEB_PAIRING_EXPIRES_AT_UTC',
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
                'CONV_ALFAKNOWLEDGE_INSTRUCCIONES',
                'CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS'
            )
            """;

    private static string BuildAutomatizacionesSelectSql(string detailColumn)
        => $"""
            SELECT
                UPPER(LTRIM(RTRIM(CLAVE))),
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN
            (
                'CONV_AUTOMATIZACIONES_ACTIVO',
                'CONV_AUTOMATIZACIONES_MENSAJE',
                'CONV_AUTOMATIZACIONES_DIAS',
                'CONV_AUTOMATIZACIONES_HORA_DESDE',
                'CONV_AUTOMATIZACIONES_HORA_HASTA',
                'CONV_BIENVENIDA_ACTIVO',
                'CONV_BIENVENIDA_MENSAJE',
                'CONV_BOT_ACTIVO',
                'CONV_BOT_SOLO_SIN_ASIGNAR',
                'CONV_BOT_PALABRAS_ESCALADO',
                'CONV_BOT_MAX_RESPUESTAS',
                'CONV_BOT_SOLO_FUERA_HORARIO',
                'CONV_BOT_ESPERA_MINUTOS',
                'CONV_AUTOCIERRE_ACTIVO',
                'CONV_AUTOCIERRE_HORAS_AVISO',
                'CONV_AUTOCIERRE_HORAS_CIERRE',
                'CONV_AUTOCIERRE_MENSAJE_AVISO',
                'CONV_AUTOCIERRE_MENSAJE_CIERRE',
                'CONV_SLA_ACTIVO',
                'CONV_SLA_HORAS_RECORDATORIO',
                'CONV_SLA_HORAS_REASIGNAR',
                'CONV_ASISTENTE_FUERA_HORARIO',
                'CONV_ASISTENTE_URGENCIA_PALABRAS',
                'CONV_ASISTENTE_USA_KNOWLEDGE',
                'CONV_INFORME_INSTRUCCIONES'
            )
            """;

    private static IEnumerable<(string Key, string Value)> BuildItems(ConversacionWhatsAppConfigDto config)
    {
        yield return ("CONV_WHATSAPP_PROVIDER_MODE", config.ProviderMode);
        yield return ("CONV_WHATSAPP_DEFAULT_PROVIDER", config.DefaultProvider);
        yield return ("CONV_WHATSAPP_VERIFY_TOKEN", config.VerifyToken);
        yield return ("CONV_WHATSAPP_ACCESS_TOKEN", config.AccessToken);
        yield return ("CONV_WHATSAPP_PHONE_NUMBER_ID", config.PhoneNumberId);
        yield return ("CONV_WHATSAPP_BUSINESS_ACCOUNT_ID", config.BusinessAccountId);
        yield return ("CONV_WHATSAPP_APP_SECRET", config.AppSecret);
        yield return ("CONV_WHATSAPP_API_VERSION", config.ApiVersion);
        yield return ("CONV_WHATSAPP_PUBLIC_BASE_URL", config.PublicBaseUrl);
        yield return ("CONV_WHATSAPP_WEBHOOK_PATH", config.WebhookPath);
        yield return ("CONV_WHATSAPP_WEB_SESSION_MODE", config.WebSessionMode);
        yield return ("CONV_WHATSAPP_WEB_PHONE_NUMBER", config.WebPhoneNumber);
        yield return ("CONV_WHATSAPP_WEB_SESSION_STATUS", config.WebSessionStatus);
        yield return ("CONV_WHATSAPP_WEB_DISPLAY_NAME", config.WebDisplayName);
        yield return ("CONV_WHATSAPP_WEB_INSTANCE_NAME", config.WebInstanceName);
        yield return ("CONV_WHATSAPP_WEB_PAIRING_TOKEN", config.WebPairingToken);
        yield return ("CONV_WHATSAPP_WEB_PAIRING_CODE", config.WebPairingCode);
        yield return ("CONV_WHATSAPP_WEB_PAIRING_QR_PAYLOAD", config.WebPairingQrPayload);
        yield return ("CONV_WHATSAPP_WEB_PAIRING_QR_DATA_URL", config.WebPairingQrDataUrl);
        yield return ("CONV_WHATSAPP_WEB_PAIRING_GENERATED_AT_UTC", FormatUtc(config.WebPairingGeneratedAtUtc));
        yield return ("CONV_WHATSAPP_WEB_PAIRING_EXPIRES_AT_UTC", FormatUtc(config.WebPairingExpiresAtUtc));
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
        yield return ("CONV_ALFAKNOWLEDGE_INSTRUCCIONES", config.Instrucciones);
        yield return ("CONV_ALFAKNOWLEDGE_TIMEOUT_SECONDS", config.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static ConversacionWhatsAppConfigDto Normalize(ConversacionWhatsAppConfigDto config)
    {
        var path = string.IsNullOrWhiteSpace(config.WebhookPath) ? DefaultWebhookPath : config.WebhookPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return new ConversacionWhatsAppConfigDto
        {
            ProviderMode = NormalizeWhatsAppProviderMode(config.ProviderMode),
            DefaultProvider = NormalizeWhatsAppDefaultProvider(config.DefaultProvider),
            VerifyToken = (config.VerifyToken ?? string.Empty).Trim(),
            AccessToken = (config.AccessToken ?? string.Empty).Trim(),
            PhoneNumberId = (config.PhoneNumberId ?? string.Empty).Trim(),
            BusinessAccountId = (config.BusinessAccountId ?? string.Empty).Trim(),
            AppSecret = (config.AppSecret ?? string.Empty).Trim(),
            ApiVersion = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v22.0" : config.ApiVersion.Trim(),
            PublicBaseUrl = NormalizePublicBaseUrl(config.PublicBaseUrl, "WhatsApp"),
            WebhookPath = path,
            WebSessionMode = NormalizeWhatsAppWebSessionMode(config.WebSessionMode),
            WebPhoneNumber = (config.WebPhoneNumber ?? string.Empty).Trim(),
            WebSessionStatus = NormalizeWhatsAppWebSessionStatus(config.WebSessionStatus),
            WebDisplayName = (config.WebDisplayName ?? string.Empty).Trim(),
            WebInstanceName = (config.WebInstanceName ?? string.Empty).Trim(),
            WebPairingToken = (config.WebPairingToken ?? string.Empty).Trim(),
            WebPairingCode = NormalizePairingCode(config.WebPairingCode),
            WebPairingQrPayload = (config.WebPairingQrPayload ?? string.Empty).Trim(),
            WebPairingQrDataUrl = (config.WebPairingQrDataUrl ?? string.Empty).Trim(),
            WebPairingGeneratedAtUtc = NormalizeUtc(config.WebPairingGeneratedAtUtc),
            WebPairingExpiresAtUtc = NormalizeUtc(config.WebPairingExpiresAtUtc),
            ConfigSource = string.Empty
        };
    }

    private static string NormalizeWhatsAppProviderMode(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            ConversacionWhatsAppProviderModes.WebOnly => ConversacionWhatsAppProviderModes.WebOnly,
            ConversacionWhatsAppProviderModes.MetaAndWeb => ConversacionWhatsAppProviderModes.MetaAndWeb,
            _ => ConversacionWhatsAppProviderModes.MetaOnly
        };

    private static string NormalizeWhatsAppDefaultProvider(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            ConversacionWhatsAppProviders.WhatsAppWeb => ConversacionWhatsAppProviders.WhatsAppWeb,
            _ => ConversacionWhatsAppProviders.MetaCloud
        };

    private static string NormalizeWhatsAppWebSessionMode(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            ConversacionWhatsAppWebSessionModes.PhoneNumber => ConversacionWhatsAppWebSessionModes.PhoneNumber,
            _ => ConversacionWhatsAppWebSessionModes.Qr
        };

    private static string NormalizeWhatsAppWebSessionStatus(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            ConversacionWhatsAppWebSessionStatuses.PendingQr => ConversacionWhatsAppWebSessionStatuses.PendingQr,
            ConversacionWhatsAppWebSessionStatuses.Connected => ConversacionWhatsAppWebSessionStatuses.Connected,
            _ => ConversacionWhatsAppWebSessionStatuses.Disconnected
        };

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
            PublicBaseUrl = NormalizePublicBaseUrl(config.PublicBaseUrl, "Instagram"),
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
            PublicBaseUrl = NormalizePublicBaseUrl(config.PublicBaseUrl, "Facebook"),
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
            PublicBaseUrl = NormalizePublicBaseUrl(config.PublicBaseUrl, "Mercado Libre"),
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
            Instrucciones = (config.Instrucciones ?? string.Empty).Trim(),
            TimeoutSeconds = timeoutSeconds,
            ConfigSource = string.Empty
        };
    }

    private static string NormalizeBaseUrl(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('/');

    private static string NormalizePublicBaseUrl(string? value, string channelName)
    {
        var normalized = NormalizeBaseUrl(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException($"La base pública de {channelName} debe ser una URL absoluta http/https. Ejemplo: https://midominio.com");
        }

        if (normalized == "." || normalized == "/" || normalized.Contains(' '))
            throw new InvalidOperationException($"La base pública de {channelName} no parece válida. Revisala antes de guardar la configuración del canal.");

        return normalized;
    }

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

    private static DateTime? ReadDateTimeValue(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return null;

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
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

    private static string FormatUtc(DateTime? value)
        => value.HasValue
            ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : string.Empty;

    private static DateTime? NormalizeUtc(DateTime? value)
        => value.HasValue
            ? DateTime.SpecifyKind(value.Value.ToUniversalTime(), DateTimeKind.Utc)
            : null;

    private static string NormalizePairingCode(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string BuildPairingCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> buffer = stackalloc char[8];
        var bytes = RandomNumberGenerator.GetBytes(buffer.Length);
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = alphabet[bytes[i] % alphabet.Length];

        return $"{new string(buffer[..4])}-{new string(buffer[4..])}";
    }

    private static string BuildWhatsAppWebPairingPayload(string instanceName, string sessionMode, string phone, string pairingToken, string pairingCode, DateTime expiresUtc)
    {
        var payload = new
        {
            provider = ConversacionWhatsAppProviders.WhatsAppWeb,
            instance = instanceName,
            mode = sessionMode,
            phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
            token = pairingToken,
            code = string.IsNullOrWhiteSpace(pairingCode) ? null : pairingCode,
            expiresAtUtc = expiresUtc.ToString("O", CultureInfo.InvariantCulture)
        };

        return JsonSerializer.Serialize(payload);
    }

    private static ConversacionWhatsAppNumeroDto MapWhatsAppNumero(SqlDataReader rd)
    {
        var qrPayload = GetString(rd, 11);
        return new ConversacionWhatsAppNumeroDto
        {
            IdNumero = rd.GetInt32(0),
            PhoneNumberId = GetString(rd, 1),
            Nombre = GetString(rd, 2),
            Activo = rd.GetBoolean(3),
            WebSessionMode = NormalizeWhatsAppWebSessionMode(GetString(rd, 4)),
            WebPhoneNumber = GetString(rd, 5),
            WebSessionStatus = NormalizeWhatsAppWebSessionStatus(GetString(rd, 6)),
            WebDisplayName = GetString(rd, 7),
            WebInstanceName = GetString(rd, 8),
            WebPairingToken = GetString(rd, 9),
            WebPairingCode = GetString(rd, 10),
            WebPairingQrPayload = qrPayload,
            WebPairingQrDataUrl = string.IsNullOrWhiteSpace(qrPayload) ? string.Empty : BuildQrCodeDataUrl(qrPayload),
            WebRuntimeState = GetString(rd, 12),
            WebLastError = GetString(rd, 13),
            WebWorkerProcessId = rd.IsDBNull(14) ? null : rd.GetInt32(14),
            WebPairingGeneratedAtUtc = rd.IsDBNull(15) ? null : rd.GetDateTime(15),
            WebPairingExpiresAtUtc = rd.IsDBNull(16) ? null : rd.GetDateTime(16),
            WebRuntimeUpdatedAtUtc = rd.IsDBNull(17) ? null : rd.GetDateTime(17)
        };
    }

    private static void FillWhatsAppNumeroParameters(
        SqlCommand cmd,
        ConversacionWhatsAppNumeroDto numero,
        string phoneNumberId,
        string nombre,
        bool includeMetaFields = true,
        bool includeWebFields = true)
    {
        if (includeMetaFields)
        {
            cmd.Parameters.AddWithValue("@PhoneNumberId", phoneNumberId);
            cmd.Parameters.AddWithValue("@Nombre", nombre);
            cmd.Parameters.AddWithValue("@Activo", numero.Activo);
        }

        if (includeWebFields)
        {
            cmd.Parameters.AddWithValue("@WebSessionMode", NormalizeWhatsAppWebSessionMode(numero.WebSessionMode));
            cmd.Parameters.AddWithValue("@WebPhoneNumber", DbNullable((numero.WebPhoneNumber ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@WebSessionStatus", NormalizeWhatsAppWebSessionStatus(numero.WebSessionStatus));
            cmd.Parameters.AddWithValue("@WebDisplayName", DbNullable((numero.WebDisplayName ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@WebInstanceName", DbNullable((numero.WebInstanceName ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@WebPairingToken", DbNullable((numero.WebPairingToken ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@WebPairingCode", DbNullable((numero.WebPairingCode ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@WebPairingQrPayload", DbNullable((numero.WebPairingQrPayload ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@WebPairingGeneratedAtUtc", numero.WebPairingGeneratedAtUtc.HasValue ? numero.WebPairingGeneratedAtUtc.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@WebPairingExpiresAtUtc", numero.WebPairingExpiresAtUtc.HasValue ? numero.WebPairingExpiresAtUtc.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@WebRuntimeState", DbNullable((numero.WebRuntimeState ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@WebLastError", DbNullable((numero.WebLastError ?? string.Empty).Trim()));
            cmd.Parameters.AddWithValue("@WebWorkerProcessId", numero.WebWorkerProcessId.HasValue ? numero.WebWorkerProcessId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@WebRuntimeUpdatedAtUtc", numero.WebRuntimeUpdatedAtUtc.HasValue ? numero.WebRuntimeUpdatedAtUtc.Value : DBNull.Value);
        }
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT c.name
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(@TableName);
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@TableName", tableName);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await rd.ReadAsync(ct))
            result.Add(GetString(rd, 0));

        return result;
    }

    private static bool HasColumn(HashSet<string> columns, string columnName)
        => columns.Contains(columnName);

    private static bool HasAnyWhatsAppWebColumns(HashSet<string> columns)
        => HasColumn(columns, "WebSessionMode");

    private static string SelectStringColumn(HashSet<string> columns, string columnName)
        => HasColumn(columns, columnName) ? $"ISNULL({columnName}, '')" : "N''";

    private static string SelectNullableIntColumn(HashSet<string> columns, string columnName)
        => HasColumn(columns, columnName) ? columnName : "CAST(NULL AS int)";

    private static string SelectNullableDateColumn(HashSet<string> columns, string columnName)
        => HasColumn(columns, columnName) ? columnName : "CAST(NULL AS datetime)";

    private static string BuildWhatsAppNumeroWebUpdateAssignments(HashSet<string> columns)
    {
        var assignments = new List<string>();
        AddWebAssignment(columns, assignments, "WebSessionMode");
        AddWebAssignment(columns, assignments, "WebPhoneNumber");
        AddWebAssignment(columns, assignments, "WebSessionStatus");
        AddWebAssignment(columns, assignments, "WebDisplayName");
        AddWebAssignment(columns, assignments, "WebInstanceName");
        AddWebAssignment(columns, assignments, "WebPairingToken");
        AddWebAssignment(columns, assignments, "WebPairingCode");
        AddWebAssignment(columns, assignments, "WebPairingQrPayload");
        AddWebAssignment(columns, assignments, "WebPairingGeneratedAtUtc");
        AddWebAssignment(columns, assignments, "WebPairingExpiresAtUtc");
        AddWebAssignment(columns, assignments, "WebRuntimeState");
        AddWebAssignment(columns, assignments, "WebLastError");
        AddWebAssignment(columns, assignments, "WebWorkerProcessId");
        AddWebAssignment(columns, assignments, "WebRuntimeUpdatedAtUtc");
        return assignments.Count == 0 ? string.Empty : ",\n                        " + string.Join(",\n                        ", assignments);
    }

    private static string BuildWhatsAppNumeroWebInsertColumns(HashSet<string> columns)
    {
        var names = GetWhatsAppNumeroWebColumnNames(columns);
        return names.Count == 0 ? string.Empty : ",\n                        " + string.Join(",\n                        ", names);
    }

    private static string BuildWhatsAppNumeroWebInsertValues(HashSet<string> columns)
    {
        var names = GetWhatsAppNumeroWebColumnNames(columns);
        return names.Count == 0 ? string.Empty : ",\n                        @" + string.Join(",\n                        @", names);
    }

    private static List<string> GetWhatsAppNumeroWebColumnNames(HashSet<string> columns)
    {
        var names = new List<string>();
        AddWebColumnName(columns, names, "WebSessionMode");
        AddWebColumnName(columns, names, "WebPhoneNumber");
        AddWebColumnName(columns, names, "WebSessionStatus");
        AddWebColumnName(columns, names, "WebDisplayName");
        AddWebColumnName(columns, names, "WebInstanceName");
        AddWebColumnName(columns, names, "WebPairingToken");
        AddWebColumnName(columns, names, "WebPairingCode");
        AddWebColumnName(columns, names, "WebPairingQrPayload");
        AddWebColumnName(columns, names, "WebPairingGeneratedAtUtc");
        AddWebColumnName(columns, names, "WebPairingExpiresAtUtc");
        AddWebColumnName(columns, names, "WebRuntimeState");
        AddWebColumnName(columns, names, "WebLastError");
        AddWebColumnName(columns, names, "WebWorkerProcessId");
        AddWebColumnName(columns, names, "WebRuntimeUpdatedAtUtc");
        return names;
    }

    private static void AddWebAssignment(HashSet<string> columns, List<string> assignments, string columnName)
    {
        if (HasColumn(columns, columnName))
            assignments.Add($"{columnName} = @{columnName}");
    }

    private static void AddWebColumnName(HashSet<string> columns, List<string> names, string columnName)
    {
        if (HasColumn(columns, columnName))
            names.Add(columnName);
    }

    private static string BuildQrCodeDataUrl(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(qrData).GetGraphic(8);
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}";
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    // Convert.ToBoolean (no rd.GetBoolean) a propósito: TA_USUARIOS es una tabla legacy y en
    // algunas bases (ej. ALFANET2007) Administrador/EsGrupo/Activo están guardadas como int, no
    // bit -- GetBoolean tira InvalidCastException ahí, Convert.ToBoolean tolera ambos.
    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index), CultureInfo.InvariantCulture);

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

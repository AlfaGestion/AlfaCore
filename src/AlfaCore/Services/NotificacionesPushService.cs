using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebPush;

namespace AlfaCore.Services;

public sealed class NotificacionesPushService(
    IConfiguration configuration,
    ISessionService sessionService,
    IOptions<PushNotificationsOptions> options,
    IAppEventService appEvents) : INotificacionesPushService
{
    private const string ModuleName = "NotificacionesPush";
    private const string ConfigGroup = "CONVERSACIONES";
    private const string SubscriptionPrefix = "PUSH-SUB-";
    private const string PreferencesPrefix = "PUSH-PREF-";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<NotificacionesPushClientSettingsDto> GetClientSettingsAsync(string userName, string deviceId, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetClientSettings", async token =>
        {
            await EnsureUserCanUseConversacionesAsync(userName, token);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var preferences = await ReadPreferencesAsync(cn, userName, deviceId, token);
            var pushOptions = options.Value;

            return new NotificacionesPushClientSettingsDto
            {
                Configurado = pushOptions.IsConfigured,
                PublicKey = pushOptions.PublicKey.Trim(),
                PublicKeyConfigurada = pushOptions.HasPublicKey,
                PrivateKeyConfigurada = pushOptions.HasPrivateKey,
                SubjectConfigurado = pushOptions.HasSubject,
                ConfiguracionMensaje = pushOptions.GetConfigurationMessage(),
                Preferences = preferences
            };
        }, "No se pudo cargar la configuración de notificaciones.", ct);

    public Task SaveSubscriptionAsync(string userName, NotificacionesPushRegistrationRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveSubscription", async token =>
        {
            ValidateDeviceId(request.DeviceId);
            ValidateSubscription(request.Subscription);
            await EnsureUserCanUseConversacionesAsync(userName, token);

            var record = new StoredSubscription
            {
                UserName = NormalizeUser(userName),
                DeviceId = NormalizeDeviceId(request.DeviceId),
                Endpoint = request.Subscription.Endpoint.Trim(),
                P256dh = request.Subscription.P256dh.Trim(),
                Auth = request.Subscription.Auth.Trim(),
                UserAgent = (request.UserAgent ?? string.Empty).Trim(),
                Activo = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var existing = await ReadSubscriptionAsync(cn, userName, request.DeviceId, token);
            if (existing is not null && existing.CreatedAt != default)
                record.CreatedAt = existing.CreatedAt;

            var key = BuildSubscriptionKey(userName, request.DeviceId);
            await SaveConfigJsonAsync(cn, key, record, "Suscripción Web Push", token);
            await appEvents.LogAuditAsync(
                ModuleName,
                "SaveSubscription",
                "TA_CONFIGURACION",
                key,
                "Suscripción Web Push guardada.",
                new
                {
                    Usuario = record.UserName,
                    record.DeviceId,
                    Endpoint = EndpointPreview(record.Endpoint),
                    TieneP256dh = !string.IsNullOrWhiteSpace(record.P256dh),
                    TieneAuth = !string.IsNullOrWhiteSpace(record.Auth),
                    record.Activo,
                    record.CreatedAt,
                    record.UpdatedAt,
                    record.UserAgent
                },
                token);
        }, "No se pudo guardar la suscripción de notificaciones.", ct);

    public Task DeleteSubscriptionAsync(string userName, string deviceId, CancellationToken ct = default)
        => ExecuteLoggedAsync("DeleteSubscription", async token =>
        {
            ValidateDeviceId(deviceId);
            await EnsureUserCanUseConversacionesAsync(userName, token);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string sql = """
                DELETE FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """;

            var key = BuildSubscriptionKey(userName, deviceId);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Clave", key.ToUpperInvariant());
            var rows = await cmd.ExecuteNonQueryAsync(token);
            await appEvents.LogAuditAsync(
                ModuleName,
                "DeleteSubscription",
                "TA_CONFIGURACION",
                key,
                "Suscripción Web Push eliminada.",
                new { Usuario = NormalizeUser(userName), DeviceId = NormalizeDeviceId(deviceId), Rows = rows },
                token);
        }, "No se pudo eliminar la suscripción de notificaciones.", ct);

    public Task SavePreferencesAsync(string userName, NotificacionesPushPreferencesRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("SavePreferences", async token =>
        {
            ValidateDeviceId(request.DeviceId);
            await EnsureUserCanUseConversacionesAsync(userName, token);
            var preferences = NormalizePreferences(request.Preferences);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var key = BuildPreferencesKey(userName, request.DeviceId);
            await SaveConfigJsonAsync(cn, key, preferences, "Preferencias Web Push", token);
            await appEvents.LogAuditAsync(
                ModuleName,
                "SavePreferences",
                "TA_CONFIGURACION",
                key,
                "Preferencias Web Push guardadas.",
                new
                {
                    Usuario = NormalizeUser(userName),
                    DeviceId = NormalizeDeviceId(request.DeviceId),
                    preferences.Habilitadas,
                    preferences.Alcance,
                    preferences.Canales
                },
                token);
        }, "No se pudieron guardar las preferencias de notificaciones.", ct);

    public Task<NotificacionesPushSendResultDto> SendTestAsync(string userName, string deviceId, CancellationToken ct = default)
        => ExecuteLoggedAsync("SendTest", async token =>
        {
            ValidateDeviceId(deviceId);
            await EnsureUserCanUseConversacionesAsync(userName, token);
            EnsureVapidConfigured();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var subscriptions = (await ReadUserSubscriptionsAsync(cn, userName, token))
                .Where(x => x.Activo)
                .ToList();

            if (subscriptions.Count == 0)
                return new NotificacionesPushSendResultDto();

            await appEvents.LogAuditAsync(
                ModuleName,
                "SendTestStart",
                "TA_CONFIGURACION",
                NormalizeUser(userName),
                "Inicio de envío de prueba Web Push.",
                new
                {
                    Usuario = NormalizeUser(userName),
                    DeviceIdActual = NormalizeDeviceId(deviceId),
                    SuscripcionesActivas = subscriptions.Count,
                    Endpoints = subscriptions.Select(x => new { x.DeviceId, Endpoint = EndpointPreview(x.Endpoint) }).ToList()
                },
                token);

            var result = await SendToSubscriptionsAsync(cn, subscriptions, new PushPayload
            {
                Title = "Nuevo mensaje",
                Body = "Notificación de prueba de AlfaCore.",
                Url = "/conversaciones"
            }, token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "SendTestResult",
                "TA_CONFIGURACION",
                NormalizeUser(userName),
                "Resultado de envío de prueba Web Push.",
                new { Usuario = NormalizeUser(userName), result.TotalCount, result.SuccessCount, result.FailCount, result.Results },
                token);

            return result;
        }, "No se pudo enviar la notificación de prueba.", ct);

    public Task<NotificacionesPushDiagnosticsDto> GetDiagnosticsAsync(string userName, string deviceId, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetDiagnostics", async token =>
        {
            ValidateDeviceId(deviceId);
            await EnsureUserCanUseConversacionesAsync(userName, token);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var subscriptions = await ReadUserSubscriptionsAsync(cn, userName, token);
            var preferences = await ReadPreferencesAsync(cn, userName, deviceId, token);

            return new NotificacionesPushDiagnosticsDto
            {
                UserName = NormalizeUser(userName),
                DeviceId = NormalizeDeviceId(deviceId),
                SubscriptionCount = subscriptions.Count,
                ActiveSubscriptionCount = subscriptions.Count(x => x.Activo),
                Preferences = preferences,
                Subscriptions = subscriptions.Select(x => new NotificacionesPushEndpointResultDto
                {
                    DeviceId = x.DeviceId,
                    EndpointParcial = EndpointPreview(x.Endpoint),
                    Success = x.Activo,
                    StatusCode = x.LastStatusCode,
                    Error = x.LastError,
                    ResponseBody = x.LastProviderResponseBody
                }).ToList()
            };
        }, "No se pudo obtener el diagnóstico de notificaciones.", ct);

    public Task NotifyNewMessageAsync(long idConversacion, long idMensaje, CancellationToken ct = default)
        => ExecuteLoggedAsync("NotifyNewMessage", async token =>
        {
            var message = await GetNewMessageAsync(idConversacion, idMensaje, token);
            if (message is null)
                return;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var subscriptions = await ReadAllSubscriptionsAsync(cn, token);
            var activeSubscriptions = subscriptions.Where(x => x.Activo).ToList();
            var candidates = 0;
            var filteredByPreferences = 0;
            var filteredByPermissions = 0;

            await appEvents.LogAuditAsync(
                ModuleName,
                "NotifyNewMessageStart",
                "CONV_MENSAJES",
                message.IdMensaje.ToString(CultureInfo.InvariantCulture),
                "Inicio de evaluación de push por mensaje entrante.",
                new
                {
                    message.IdConversacion,
                    message.IdMensaje,
                    message.Canal,
                    message.IdTecnico,
                    SuscripcionesTotales = subscriptions.Count,
                    SuscripcionesActivas = activeSubscriptions.Count
                },
                token);

            var toSend = new List<StoredSubscription>();
            foreach (var subscription in activeSubscriptions)
            {
                candidates++;
                var preferences = await ReadPreferencesAsync(cn, subscription.UserName, subscription.DeviceId, token);
                if (!ShouldNotify(message, subscription.UserName, preferences))
                {
                    filteredByPreferences++;
                    continue;
                }

                if (!await UserCanUseConversacionesAsync(subscription.UserName, token))
                {
                    filteredByPermissions++;
                    continue;
                }

                toSend.Add(subscription);
            }

            var result = await SendToSubscriptionsAsync(cn, toSend, new PushPayload
            {
                Title = "Nuevo mensaje",
                Body = BuildBody(message),
                Url = $"/conversaciones?id={message.IdConversacion.ToString(CultureInfo.InvariantCulture)}",
                IdConversacion = message.IdConversacion,
                IdMensaje = message.IdMensaje,
                Canal = message.Canal
            }, token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "NotifyNewMessageResult",
                "CONV_MENSAJES",
                message.IdMensaje.ToString(CultureInfo.InvariantCulture),
                "Resultado de envío push por mensaje entrante.",
                new
                {
                    message.IdConversacion,
                    message.IdMensaje,
                    message.Canal,
                    message.IdTecnico,
                    Candidatos = candidates,
                    FiltradosPorPreferencias = filteredByPreferences,
                    FiltradosPorPermisos = filteredByPermissions,
                    SuscripcionesAEnviar = toSend.Count,
                    result.SuccessCount,
                    result.FailCount,
                    result.Results
                },
                token);
        }, "No se pudo enviar la notificación push de mensaje nuevo.", ct);

    public Task<bool> UserCanUseConversacionesAsync(string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync("UserCanUseConversaciones", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return false;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await UserCanUseConversacionesAsync(cn, userName, token);
        }, "No se pudo validar el permiso de conversaciones.", ct);

    private async Task<NotificacionMensajeNuevoDto?> GetNewMessageAsync(long idConversacion, long idMensaje, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                c.IdConversacion,
                m.IdMensaje,
                ISNULL(c.Canal, ''),
                COALESCE(NULLIF(LTRIM(RTRIM(mc.Nombre_y_Apellido)), ''), NULLIF(LTRIM(RTRIM(c.NombreVisible)), ''), NULLIF(LTRIM(RTRIM(cli.RAZON_SOCIAL)), ''), NULLIF(LTRIM(RTRIM(c.TelefonoWhatsApp)), ''), ''),
                ISNULL(m.Texto, ''),
                COALESCE(NULLIF(LTRIM(RTRIM(t.UsuarioAsociado)), ''), LTRIM(RTRIM(ISNULL(c.IdTecnico, ''))), '')
            FROM dbo.CONV_MENSAJES m
            INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
            LEFT JOIN dbo.MA_CONTACTOS mc ON mc.id = c.IdContacto
            LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = c.ClienteCodigo
            LEFT JOIN dbo.V_TA_Tecnicos t ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(c.IdTecnico))
            WHERE c.IdConversacion = @IdConversacion
              AND m.IdMensaje = @IdMensaje
              AND m.Direction = N'ENTRANTE';
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@IdMensaje", idMensaje);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new NotificacionMensajeNuevoDto
        {
            IdConversacion = rd.GetInt64(0),
            IdMensaje = rd.GetInt64(1),
            Canal = GetString(rd, 2),
            Contacto = GetString(rd, 3),
            Resumen = GetString(rd, 4),
            IdTecnico = GetString(rd, 5)
        };
    }

    private async Task<bool> UserCanUseConversacionesAsync(SqlConnection cn, string userName, CancellationToken ct)
    {
        if (!await TableExistsAsync(cn, "TA_USUARIOS", ct))
            return false;

        if (!await TableExistsAsync(cn, "TA_TAREAS", ct) || !await TableExistsAsync(cn, "TA_MENU", ct))
            return true;

        const string sql = """
            IF NOT EXISTS (
                SELECT 1
                FROM dbo.TA_MENU
                WHERE ISNULL(Habilitado, 1) = 1
                  AND (
                      UPPER(ISNULL(Menu, '')) LIKE N'%CONVERS%'
                      OR UPPER(ISNULL(Titulo, '')) LIKE N'%CONVERS%'
                      OR UPPER(ISNULL(Clave, '')) LIKE N'%CONVERS%'
                      OR UPPER(ISNULL(Nombre, '')) LIKE N'%CONVERS%'
                      OR UPPER(ISNULL(Proceso, '')) LIKE N'%CONVERS%'
                  )
            )
            BEGIN
                SELECT CAST(1 AS bit);
                RETURN;
            END;

            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM dbo.TA_TAREAS t
                INNER JOIN dbo.TA_MENU m
                    ON UPPER(LTRIM(RTRIM(m.Clave))) = UPPER(LTRIM(RTRIM(t.TAREA)))
                WHERE UPPER(LTRIM(RTRIM(t.USUARIO))) = @Usuario
                  AND ISNULL(m.Habilitado, 1) = 1
                  AND (
                      UPPER(ISNULL(m.Menu, '')) LIKE N'%CONVERS%'
                      OR UPPER(ISNULL(m.Titulo, '')) LIKE N'%CONVERS%'
                      OR UPPER(ISNULL(m.Clave, '')) LIKE N'%CONVERS%'
                      OR UPPER(ISNULL(m.Nombre, '')) LIKE N'%CONVERS%'
                      OR UPPER(ISNULL(m.Proceso, '')) LIKE N'%CONVERS%'
                  )
            ) THEN 1 ELSE 0 END AS bit);
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Usuario", NormalizeUser(userName));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is bool allowed && allowed;
    }

    private async Task<NotificacionesPushPreferencesDto> ReadPreferencesAsync(SqlConnection cn, string userName, string deviceId, CancellationToken ct)
    {
        var raw = await ReadConfigValueAsync(cn, BuildPreferencesKey(userName, deviceId), ct);
        if (string.IsNullOrWhiteSpace(raw))
            return new();

        try
        {
            return NormalizePreferences(JsonSerializer.Deserialize<NotificacionesPushPreferencesDto>(raw, JsonOptions));
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private async Task<StoredSubscription?> ReadSubscriptionAsync(SqlConnection cn, string userName, string deviceId, CancellationToken ct)
    {
        var raw = await ReadConfigValueAsync(cn, BuildSubscriptionKey(userName, deviceId), ct);
        return DeserializeSubscription(raw);
    }

    private async Task<List<StoredSubscription>> ReadAllSubscriptionsAsync(SqlConnection cn, CancellationToken ct)
    {
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var sql = $"""
            SELECT ISNULL(VALOR, ''), ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) LIKE @Prefix;
            """;

        var items = new List<StoredSubscription>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Prefix", $"{SubscriptionPrefix}%");
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var subscription = DeserializeSubscription(ResolveStoredValue(GetString(rd, 0), GetString(rd, 1)));
            if (subscription is not null)
                items.Add(subscription);
        }

        return items;
    }

    private async Task<List<StoredSubscription>> ReadUserSubscriptionsAsync(SqlConnection cn, string userName, CancellationToken ct)
    {
        var normalizedUser = NormalizeUser(userName);
        var items = await ReadAllSubscriptionsAsync(cn, ct);
        return items
            .Where(x => string.Equals(x.UserName, normalizedUser, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<string> ReadConfigValueAsync(SqlConnection cn, string key, CancellationToken ct)
    {
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var sql = $"""
            SELECT TOP (1) ISNULL(VALOR, ''), ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Clave", key.ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct)
            ? ResolveStoredValue(GetString(rd, 0), GetString(rd, 1))
            : string.Empty;
    }

    private async Task SaveConfigJsonAsync(SqlConnection cn, string key, object value, string description, CancellationToken ct)
    {
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var serialized = JsonSerializer.Serialize(value, JsonOptions);
        var stored = SplitStoredValue(serialized);
        var sql = $"""
            UPDATE dbo.TA_CONFIGURACION
            SET VALOR = @Valor,
                {detailColumn} = @ValorAux,
                GRUPO = @Grupo,
                FechaHora_Modificacion = GETDATE()
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.TA_CONFIGURACION
                (
                    CLAVE,
                    VALOR,
                    {detailColumn},
                    GRUPO,
                    FechaHora_Grabacion,
                    FechaHora_Modificacion
                )
                VALUES
                (
                    @Clave,
                    @Valor,
                    @ValorAux,
                    @Grupo,
                    GETDATE(),
                    GETDATE()
                );
            END;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@ClaveNormalizada", key.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Clave", key);
        cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
        cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
        cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
        await cmd.ExecuteNonQueryAsync(ct);

        await appEvents.LogAuditAsync(ModuleName, "SaveConfig", "TA_CONFIGURACION", key, description, null, ct);
    }

    private async Task<NotificacionesPushSendResultDto> SendToSubscriptionsAsync(
        SqlConnection cn,
        IReadOnlyCollection<StoredSubscription> subscriptions,
        PushPayload payload,
        CancellationToken ct)
    {
        var result = new NotificacionesPushSendResultDto { TotalCount = subscriptions.Count };
        foreach (var subscription in subscriptions)
        {
            var item = await SendToSubscriptionAsync(cn, subscription, payload, ct);
            result.Results.Add(item);
            if (item.Success)
                result.SuccessCount++;
            else
                result.FailCount++;
        }

        return result;
    }

    private async Task<NotificacionesPushEndpointResultDto> SendToSubscriptionAsync(
        SqlConnection cn,
        StoredSubscription subscription,
        PushPayload payload,
        CancellationToken ct)
    {
        var pushOptions = EnsureVapidConfigured();
        var result = new NotificacionesPushEndpointResultDto
        {
            DeviceId = subscription.DeviceId,
            EndpointParcial = EndpointPreview(subscription.Endpoint)
        };

        var webPushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
        var vapidDetails = new VapidDetails(pushOptions.Subject.Trim(), pushOptions.PublicKey.Trim(), pushOptions.PrivateKey.Trim());
        var client = new WebPushClient();
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        try
        {
            await appEvents.LogAuditAsync(
                ModuleName,
                "SendSubscriptionAttempt",
                "TA_CONFIGURACION",
                BuildSubscriptionKey(subscription.UserName, subscription.DeviceId),
                "Intento de envío a suscripción push.",
                new
                {
                    subscription.UserName,
                    subscription.DeviceId,
                    Endpoint = EndpointPreview(subscription.Endpoint),
                    payload.IdConversacion,
                    payload.IdMensaje,
                    payload.Canal
                },
                ct);

            await client.SendNotificationAsync(webPushSubscription, json, vapidDetails, ct);
            result.Success = true;
            await appEvents.LogAuditAsync(
                ModuleName,
                "SendSubscriptionOk",
                "TA_CONFIGURACION",
                BuildSubscriptionKey(subscription.UserName, subscription.DeviceId),
                "Proveedor push aceptó la notificación.",
                new { subscription.UserName, subscription.DeviceId, Endpoint = EndpointPreview(subscription.Endpoint), PayloadLength = json.Length },
                ct);
        }
        catch (WebPushException ex)
        {
            result.Success = false;
            result.StatusCode = Convert.ToInt32(ex.StatusCode, CultureInfo.InvariantCulture);
            result.ResponseBody = ReadExceptionTextProperty(ex, "ResponseBody", "Body", "Content");
            result.Error = BuildWebPushErrorMessage(result.StatusCode, ex.Message);
            subscription.LastStatusCode = result.StatusCode;
            subscription.LastError = result.Error;
            subscription.LastProviderResponseBody = result.ResponseBody;
            subscription.UpdatedAt = DateTimeOffset.UtcNow;

            if (result.StatusCode is 404 or 410)
                await DeactivateSubscriptionAsync(cn, subscription, result.StatusCode, result.Error, ct);
            else
                await SaveConfigJsonAsync(
                    cn,
                    BuildSubscriptionKey(subscription.UserName, subscription.DeviceId),
                    subscription,
                    "Estado de último envío Web Push",
                    ct);

            await appEvents.LogErrorAsync(
                ModuleName,
                "SendSubscriptionWebPushError",
                ex,
                result.Error,
                new
                {
                    subscription.UserName,
                    subscription.DeviceId,
                    Endpoint = EndpointPreview(subscription.Endpoint),
                    result.StatusCode,
                    result.ResponseBody,
                    Payload = json
                },
                AppEventSeverity.Warning,
                ct);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            await appEvents.LogErrorAsync(
                ModuleName,
                "SendSubscriptionError",
                ex,
                "No se pudo enviar una notificación push a una suscripción.",
                new { subscription.UserName, subscription.DeviceId, Endpoint = EndpointPreview(subscription.Endpoint), Payload = json },
                AppEventSeverity.Warning,
                ct);
        }

        return result;
    }

    private PushNotificationsOptions EnsureVapidConfigured()
    {
        var pushOptions = options.Value;
        if (!pushOptions.IsConfigured)
            throw new InvalidOperationException(pushOptions.GetConfigurationMessage());

        var subject = pushOptions.Subject.Trim();
        if (!subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            && !subject.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !subject.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PushNotifications:Subject debe tener formato mailto:admin@alfagestion.com o una URL http/https.");

        ValidateVapidKeys(pushOptions.PublicKey, pushOptions.PrivateKey);

        return pushOptions;
    }

    private async Task DeactivateSubscriptionAsync(
        SqlConnection cn,
        StoredSubscription subscription,
        int? statusCode,
        string error,
        CancellationToken ct)
    {
        subscription.Activo = false;
        subscription.LastStatusCode = statusCode;
        subscription.LastError = error;
        subscription.LastProviderResponseBody = string.Empty;
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveConfigJsonAsync(
            cn,
            BuildSubscriptionKey(subscription.UserName, subscription.DeviceId),
            subscription,
            "Suscripción Web Push inactiva",
            ct);
    }

    private async Task EnsureUserCanUseConversacionesAsync(string userName, CancellationToken ct)
    {
        if (!await UserCanUseConversacionesAsync(userName, ct))
            throw new InvalidOperationException("El usuario no tiene acceso al módulo Conversaciones.");
    }

    private static bool ShouldNotify(NotificacionMensajeNuevoDto message, string userName, NotificacionesPushPreferencesDto preferences)
    {
        if (!preferences.Habilitadas)
            return false;

        if (!preferences.Canales.Contains(message.Canal, StringComparer.OrdinalIgnoreCase))
            return false;

        if (string.Equals(preferences.Alcance, NotificacionesPushScopes.Asignadas, StringComparison.OrdinalIgnoreCase))
            return string.Equals(message.IdTecnico, NormalizeUser(userName), StringComparison.OrdinalIgnoreCase);

        return string.Equals(preferences.Alcance, NotificacionesPushScopes.Accesibles, StringComparison.OrdinalIgnoreCase);
    }

    private static NotificacionesPushPreferencesDto NormalizePreferences(NotificacionesPushPreferencesDto? preferences)
    {
        preferences ??= new();
        var alcance = string.Equals(preferences.Alcance, NotificacionesPushScopes.Accesibles, StringComparison.OrdinalIgnoreCase)
            ? NotificacionesPushScopes.Accesibles
            : NotificacionesPushScopes.Asignadas;

        var canales = preferences.Canales
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (canales.Count == 0)
            canales.Add("WHATSAPP");

        return new NotificacionesPushPreferencesDto
        {
            Habilitadas = preferences.Habilitadas,
            Alcance = alcance,
            Canales = canales
        };
    }

    private static StoredSubscription? DeserializeSubscription(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            var subscription = JsonSerializer.Deserialize<StoredSubscription>(raw, JsonOptions);
            return string.IsNullOrWhiteSpace(subscription?.Endpoint)
                ? null
                : subscription;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildBody(NotificacionMensajeNuevoDto message)
    {
        var channel = string.IsNullOrWhiteSpace(message.Canal) ? "Conversación" : message.Canal.Trim();
        var contact = string.IsNullOrWhiteSpace(message.Contacto) ? "contacto sin nombre" : message.Contacto.Trim();
        return $"{channel} · {contact}";
    }

    private static string BuildSubscriptionKey(string userName, string deviceId)
        => $"{SubscriptionPrefix}{HashPart(userName, 24)}-{HashPart(deviceId, 10)}";

    private static string BuildPreferencesKey(string userName, string deviceId)
        => $"{PreferencesPrefix}{HashPart(userName, 24)}-{HashPart(deviceId, 10)}";

    private static string HashPart(string value, int length)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((value ?? string.Empty).Trim().ToUpperInvariant())));
        return hash[..Math.Min(length, hash.Length)];
    }

    private static async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END, name;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var column = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(1) FROM sys.objects WHERE object_id = OBJECT_ID(@Nombre) AND type = N'U';";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Nombre", $"dbo.{tableName}");
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private async Task ExecuteLoggedAsync(string action, Func<CancellationToken, Task> operation, string userMessage, CancellationToken ct)
        => await ExecuteLoggedAsync(action, async token =>
        {
            await operation(token);
            return true;
        }, userMessage, ct);

    private static void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new InvalidOperationException("No se recibió el identificador del dispositivo.");
    }

    private static void ValidateSubscription(NotificacionesPushSubscriptionDto subscription)
    {
        if (string.IsNullOrWhiteSpace(subscription.Endpoint)
            || string.IsNullOrWhiteSpace(subscription.P256dh)
            || string.IsNullOrWhiteSpace(subscription.Auth))
            throw new InvalidOperationException("La suscripción push recibida no es válida.");
    }

    private static void ValidateVapidKeys(string publicKey, string privateKey)
    {
        var publicBytes = DecodeBase64Url(publicKey, "PushNotifications:PublicKey");
        var privateBytes = DecodeBase64Url(privateKey, "PushNotifications:PrivateKey");

        if (publicBytes.Length != 65 || publicBytes[0] != 4)
            throw new InvalidOperationException("PushNotifications:PublicKey no tiene el formato P-256 esperado.");

        if (privateBytes.Length != 32)
            throw new InvalidOperationException("PushNotifications:PrivateKey no tiene el formato P-256 esperado.");

        try
        {
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateBytes
            };

            using var ecdsa = ECDsa.Create(parameters);
            var publicParameters = ecdsa.ExportParameters(false);
            if (publicParameters.Q.X is null || publicParameters.Q.Y is null)
                throw new InvalidOperationException("No se pudo derivar la clave pública desde PushNotifications:PrivateKey.");

            var expectedPublicKey = new byte[65];
            expectedPublicKey[0] = 4;
            Buffer.BlockCopy(publicParameters.Q.X, 0, expectedPublicKey, 1, 32);
            Buffer.BlockCopy(publicParameters.Q.Y, 0, expectedPublicKey, 33, 32);

            if (!CryptographicOperations.FixedTimeEquals(publicBytes, expectedPublicKey))
                throw new InvalidOperationException("PushNotifications:PublicKey y PushNotifications:PrivateKey no pertenecen al mismo par VAPID.");
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("No se pudo validar el par VAPID configurado.", ex);
        }
    }

    private static byte[] DecodeBase64Url(string value, string name)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
            throw new InvalidOperationException($"{name} no puede estar vacía.");

        if (normalized.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-' && ch != '_'))
            throw new InvalidOperationException($"{name} no tiene formato base64url válido.");

        try
        {
            var padded = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            return Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{name} no tiene formato base64url válido.", ex);
        }
    }

    private static string BuildWebPushErrorMessage(int? statusCode, string message)
    {
        if (statusCode is 401 or 403)
            return "El proveedor push rechazó la autenticación VAPID (401/403). Verificá que PublicKey y PrivateKey pertenezcan al mismo par y que Subject use mailto:admin@alfagestion.com.";

        if (statusCode is 404 or 410)
            return "La suscripción push ya no existe en el navegador/proveedor y fue marcada como inactiva.";

        return string.IsNullOrWhiteSpace(message)
            ? "El proveedor push rechazó la notificación."
            : message;
    }

    private static string ReadExceptionTextProperty(Exception exception, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = exception.GetType().GetProperty(propertyName);
            if (property?.GetValue(exception) is string value && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static string EndpointPreview(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;

        var normalized = endpoint.Trim();
        return normalized.Length <= 42
            ? normalized
            : $"{normalized[..28]}...{normalized[^10..]}";
    }

    private static string NormalizeUser(string userName)
        => (userName ?? string.Empty).Trim().ToUpperInvariant();

    private static string NormalizeDeviceId(string deviceId)
        => (deviceId ?? string.Empty).Trim();

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length > 150 ? (string.Empty, normalized) : (normalized, string.Empty);
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;

    private sealed class StoredSubscription
    {
        public string UserName { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int? LastStatusCode { get; set; }
        public string LastError { get; set; } = string.Empty;
        public string LastProviderResponseBody { get; set; } = string.Empty;
    }

    private sealed class PushPayload
    {
        public string Title { get; set; } = "Nuevo mensaje";
        public string Body { get; set; } = string.Empty;
        public string Icon { get; set; } = "/icons/icon-192.png";
        public string Badge { get; set; } = "/icons/badge-96.png";
        public string Url { get; set; } = "/conversaciones";
        public long? IdConversacion { get; set; }
        public long? IdMensaje { get; set; }
        public string Canal { get; set; } = string.Empty;
    }
}

using System.Net.Http.Headers;
using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AlfaCore.Configuration;

/// <summary>
/// Modo one-shot 100% READ-ONLY <c>--inspect-whatsapp-runtime --id-base &lt;idBase&gt; --phone-number-id
/// &lt;phoneNumberId&gt;</c>: diagnóstico de inbound/outbound real para un número, usando los mismos
/// resolvers reales que la app (ownership central, <see cref="WhatsAppRuntimeCredentialResolver"/>,
/// resolución de base tenant vía dbo.bases) más lecturas SELECT-only de la base tenant
/// (CONV_WHATSAPP_NUMEROS / CONV_MENSAJES / CONV_WEBHOOK_LOG). Pensado para correr en
/// SERVER-ALFACENTRAL sin compartir credenciales con quien lo pidió -- sólo imprime resultados
/// sanitizados.
///
/// Igual que <see cref="WhatsAppPhoneInspectionCommand"/>: se resuelve ANTES de CreateBuilder (no
/// arranca Kestrel ni hosted services). Nunca hace POST/PATCH/DELETE a Graph, nunca hace
/// INSERT/UPDATE/DELETE en SQL (ni siquiera el alta perezosa de CONV_WHATSAPP_NUMEROS que sí hace el
/// runtime real al recibir un webhook -- acá SIEMPRE es un SELECT puro), nunca toca ownership/Vault
/// más allá de leerlos, nunca dispara onboarding.
///
/// Nunca imprime: access token, ProtectedValue, SecretReference, AppSecret, PIN, VerifyToken, usuario
/// ni password de la connection string de la base tenant (sólo servidor + nombre de base).
/// </summary>
internal static class WhatsAppRuntimeInspectionCommand
{
    public const string Verb = "--inspect-whatsapp-runtime";

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, Verb, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IConfiguration configuration, TextWriter output, CancellationToken ct)
    {
        var idBaseArg = ReadOption(args, "--id-base");
        var phoneNumberId = ReadOption(args, "--phone-number-id")?.Trim();
        var messageId = ReadLongOption(args, "--message-id");
        var templateId = ReadLongOption(args, "--template-id");
        if (!int.TryParse(idBaseArg, out var idBase) || idBase <= 0 || string.IsNullOrWhiteSpace(phoneNumberId))
        {
            output.WriteLine("Uso: AlfaCore --inspect-whatsapp-runtime --id-base <idBase> --phone-number-id <phoneNumberId> [--message-id <idMensaje>] [--template-id <idPlantilla>]");
            return 1;
        }

        var options = configuration.GetSection(WhatsAppEmbeddedSignupOptions.SectionName).Get<WhatsAppEmbeddedSignupOptions>() ?? new();
        var optionsWrapper = Options.Create(options);
        IWhatsAppAssetOwnershipStore ownershipStore = new WhatsAppAssetOwnershipStore(configuration);
        IWhatsAppCredentialVault credentialVault = new WhatsAppSecureVault(configuration, optionsWrapper);
        IWhatsAppRuntimeCredentialResolver resolver = new WhatsAppRuntimeCredentialResolver(ownershipStore, credentialVault, optionsWrapper);
        using var httpClient = new HttpClient();

        // Misma conexión central que ya usan ownership/Vault (soporta tanto
        // UseApplicationCentralConnection=true como WhatsAppEmbeddedSignup:CentralConnectionString
        // dedicada) -- nunca se asume ConnectionStrings:AlfaCentral a secas.
        var centralConnectionString = WhatsAppEmbeddedSignupConnection.Resolve(configuration, null);

        return await ExecuteAsync(
            idBase,
            phoneNumberId,
            ownershipStore,
            resolver,
            httpClient,
            options.GraphBaseUrl,
            ct2 => ResolveTenantBaseInfoAsync(centralConnectionString, idBase, ct2),
            (baseInfo, phone, ct2) => ResolveTenantNumeroAsync(baseInfo, phone, ct2),
            (baseInfo, idNumero, ct2) => ResolveOutboundMessageAsync(baseInfo, idNumero, messageId, ct2),
            (baseInfo, idPlantilla, ct2) => ResolveTenantTemplateAsync(baseInfo, idPlantilla, ct2),
            (baseInfo, phone, ct2) => ResolveLastWebhookLogAsync(baseInfo, phone, ct2),
            messageId,
            templateId,
            output,
            ct);
    }

    /// <summary>
    /// Núcleo testeable: ownership/credencial/Graph por interfaz (igual que
    /// <see cref="WhatsAppPhoneInspectionCommand.ExecuteAsync"/>), y las cuatro lecturas SQL reales
    /// (central + tenant) por delegado, para poder probarse con dobles sin SQL real.
    /// </summary>
    internal static async Task<int> ExecuteAsync(
        int idBase,
        string phoneNumberId,
        IWhatsAppAssetOwnershipStore ownershipStore,
        IWhatsAppRuntimeCredentialResolver credentialResolver,
        HttpClient httpClient,
        string graphBaseUrl,
        Func<CancellationToken, Task<TenantBaseInfo?>> resolveBaseInfo,
        Func<TenantBaseInfo, string, CancellationToken, Task<TenantNumeroInfo?>> resolveNumero,
        Func<TenantBaseInfo, int?, CancellationToken, Task<TenantOutboundMessageInfo?>> resolveLastOutbound,
        Func<TenantBaseInfo, long?, CancellationToken, Task<TenantTemplateInfo?>> resolveTemplate,
        Func<TenantBaseInfo, string, CancellationToken, Task<TenantWebhookLogInfo?>> resolveLastWebhookLog,
        long? messageId,
        long? templateId,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine("== inspect-whatsapp-runtime (read-only) ==");
        output.WriteLine($"BASE = {idBase}");
        if (messageId is > 0)
            output.WriteLine($"REQUESTED_MESSAGE_ID = {messageId}");
        if (templateId is > 0)
            output.WriteLine($"REQUESTED_TEMPLATE_ID = {templateId}");

        var baseInfo = await resolveBaseInfo(ct);
        if (baseInfo is null)
        {
            output.WriteLine("TENANT DATABASE = ERROR (la base no existe en dbo.bases)");
            output.WriteLine($"PHONE_NUMBER_ID = {phoneNumberId}");
            return 1;
        }
        output.WriteLine($"TENANT DATABASE = {baseInfo.DbServer}/{baseInfo.DbName}");
        output.WriteLine($"PHONE_NUMBER_ID = {phoneNumberId}");
        output.WriteLine("");

        WhatsAppPhoneOwnership? ownership;
        try
        {
            if (!await ownershipStore.IsSchemaAvailableAsync(ct))
            {
                output.WriteLine("OWNERSHIP = ERROR (esquema central no disponible)");
                WriteBlockedFromOwnership(output);
                return 1;
            }
            ownership = await ownershipStore.GetPhoneOwnershipAsync(phoneNumberId, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine($"OWNERSHIP = ERROR ({ex.GetType().Name})");
            WriteBlockedFromOwnership(output);
            return 1;
        }

        if (ownership is null)
        {
            output.WriteLine("OWNERSHIP = ERROR (sin ownership central para este PhoneNumberId)");
            WriteBlockedFromOwnership(output);
            return 1;
        }
        if (ownership.IdBase != idBase)
        {
            output.WriteLine("OWNERSHIP = ERROR (pertenece a otra base — cross-tenant, bloqueado antes de Graph)");
            WriteBlockedFromOwnership(output);
            return 1;
        }
        output.WriteLine("OWNERSHIP = OK");

        WhatsAppRuntimeCredential? credential = null;
        try
        {
            // legacyConfig vacío a propósito, igual que --inspect-whatsapp-phone: con ownership ES
            // confirmado arriba, el resolver NUNCA debería usarlo.
            credential = await credentialResolver.ResolveAsync(idBase, null, phoneNumberId, new ConversacionWhatsAppConfigDto(), ct);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WhatsAppEmbeddedVaultUnavailableException or WhatsAppEmbeddedSchemaUnavailableException)
        {
            output.WriteLine($"VAULT_CREDENTIAL = ERROR ({ex.GetType().Name})");
        }

        if (credential is not null && credential.Origin != WhatsAppRuntimeCredentialOrigin.EmbeddedSignup)
        {
            output.WriteLine("VAULT_CREDENTIAL = ERROR (resolvió Legacy pese a tener ownership ES; abortado antes de Graph)");
            credential = null;
        }
        else if (credential is not null)
        {
            output.WriteLine("VAULT_CREDENTIAL = OK");
        }
        output.WriteLine("");

        TenantNumeroInfo? numero = null;
        try
        {
            numero = await resolveNumero(baseInfo, phoneNumberId, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine($"ID_NUMERO = ERROR ({ex.GetType().Name})");
        }

        if (numero is null)
        {
            output.WriteLine("ID_NUMERO = NO ENCONTRADO en dbo.CONV_WHATSAPP_NUMEROS de la base tenant");
            output.WriteLine("NUMERO_ACTIVO = N/A");
        }
        else
        {
            output.WriteLine($"ID_NUMERO = {numero.IdNumero}");
            output.WriteLine($"NUMERO_ACTIVO = {numero.Activo}");
        }
        // Clasificación real: de dónde salió la credencial que se está usando para este número
        // (mismo resolver que usa el runtime real para inbound/outbound) -- nunca inferido del nombre.
        output.WriteLine($"ORIGEN/CLASIFICACION = {(credential is null ? "N/A (bloqueado por Vault)" : credential.Origin.ToString())}");

        output.WriteLine("");
        output.WriteLine("=== OUTBOUND ===");
        TenantOutboundMessageInfo? outbound = null;
        try
        {
            outbound = await resolveLastOutbound(baseInfo, numero?.IdNumero, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine($"LAST_OUTBOUND_MESSAGE_ID = ERROR ({ex.GetType().Name})");
        }

        if (outbound is null)
        {
            output.WriteLine("LAST_OUTBOUND_MESSAGE_ID = (sin mensajes SALIENTE encontrados para este número)");
            output.WriteLine("LAST_OUTBOUND_TIMESTAMP = N/A");
            output.WriteLine("LAST_OUTBOUND_STATUS = N/A");
            output.WriteLine("LAST_OUTBOUND_WHATSAPP_ID = N/A");
            output.WriteLine("LAST_OUTBOUND_ERROR_CODE = N/A");
            output.WriteLine("LAST_OUTBOUND_ERROR_SUMMARY = N/A");
        }
        else
        {
            var (errorCode, errorSummary) = ExtractOutboundErrorInfo(outbound.EstadoEnvio, outbound.PayloadJson);
            output.WriteLine($"LAST_OUTBOUND_MESSAGE_ID = {outbound.IdMensaje}");
            output.WriteLine($"LAST_OUTBOUND_TIMESTAMP = {outbound.FechaHora:O}");
            output.WriteLine($"LAST_OUTBOUND_STATUS = {(string.IsNullOrWhiteSpace(outbound.EstadoEnvio) ? "(vacío)" : outbound.EstadoEnvio)}");
            output.WriteLine($"LAST_OUTBOUND_WHATSAPP_ID = {(string.IsNullOrWhiteSpace(outbound.WhatsAppMessageId) ? "(vacío -- nunca confirmado por Meta)" : outbound.WhatsAppMessageId)}");
            output.WriteLine($"LAST_OUTBOUND_ERROR_CODE = {errorCode ?? "N/A"}");
            output.WriteLine($"LAST_OUTBOUND_ERROR_SUMMARY = {(string.IsNullOrWhiteSpace(errorSummary) ? "N/A" : errorSummary)}");
        }

        output.WriteLine("");
        output.WriteLine("=== TEMPLATE SEND DIAGNOSTIC ===");
        TenantTemplateInfo? template = null;
        try
        {
            template = await resolveTemplate(baseInfo, templateId, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine($"SEND_TEMPLATE_LOCAL_ID = ERROR ({ex.GetType().Name})");
        }

        var sendPhoneNumberId = credential?.PhoneNumberId ?? phoneNumberId;
        var sendWabaId = credential?.WabaId ?? ownership.WabaId;
        output.WriteLine($"SEND_PHONE_NUMBER_ID = {sendPhoneNumberId}");
        output.WriteLine($"SEND_WABA_ID = {sendWabaId}");
        if (template is null)
        {
            output.WriteLine("SEND_TEMPLATE_LOCAL_ID = N/A");
            output.WriteLine("SEND_META_TEMPLATE_ID = N/A");
            output.WriteLine("SEND_TEMPLATE_NAME = N/A");
            output.WriteLine("SEND_LANGUAGE_CODE = N/A");
            output.WriteLine("SEND_COMPONENTS = N/A");
        }
        else
        {
            output.WriteLine($"SEND_TEMPLATE_LOCAL_ID = {template.IdPlantilla}");
            output.WriteLine($"SEND_META_TEMPLATE_ID = {ValueOrEmpty(template.MetaTemplateId)}");
            output.WriteLine($"SEND_TEMPLATE_NAME = {ValueOrEmpty(template.NombreMeta)}");
            output.WriteLine($"SEND_LANGUAGE_CODE = {ValueOrEmpty(template.Idioma)}");
            output.WriteLine($"SEND_COMPONENTS = {DescribeSendComponents(template.CuerpoTexto)}");
        }

        output.WriteLine("");
        output.WriteLine("=== WEBHOOK ===");
        TenantWebhookLogInfo? webhookLog = null;
        try
        {
            webhookLog = await resolveLastWebhookLog(baseInfo, phoneNumberId, ct);
        }
        catch (Exception ex)
        {
            output.WriteLine($"LAST_META_WEBHOOK_TIMESTAMP = ERROR ({ex.GetType().Name})");
        }

        if (webhookLog is null)
        {
            output.WriteLine("LAST_META_WEBHOOK_TIMESTAMP = (sin filas en CONV_WEBHOOK_LOG para META_WHATSAPP)");
            output.WriteLine("LAST_META_WEBHOOK_SUCCESS = N/A");
            output.WriteLine("LAST_META_WEBHOOK_EVENT_TYPE = N/A");
            output.WriteLine("LAST_META_WEBHOOK_ERROR = N/A");
        }
        else
        {
            // CONV_WEBHOOK_LOG no tiene una columna PhoneNumberId propia -- la correlación es contra el
            // JSON resumen sanitizado guardado en PayloadJson (campo PhoneNumberIds). Si no se pudo
            // correlacionar, se dice explícitamente en vez de mostrar el dato como si fuera del número pedido.
            var correlationNote = webhookLog.CorrelatedToPhoneNumberId
                ? ""
                : " (NO CORRELACIONADO: CONV_WEBHOOK_LOG no tiene columna PhoneNumberId; ningún registro META_WHATSAPP reciente menciona este número en su PayloadJson -- se muestra el más reciente de cualquier número, sólo a título orientativo)";
            output.WriteLine($"LAST_META_WEBHOOK_TIMESTAMP = {webhookLog.FechaHoraRecepcion:O}{correlationNote}");
            output.WriteLine($"LAST_META_WEBHOOK_SUCCESS = {(webhookLog.ProcesadoOk.HasValue ? webhookLog.ProcesadoOk.Value.ToString() : "(pendiente/NULL)")}");
            output.WriteLine($"LAST_META_WEBHOOK_EVENT_TYPE = {BuildWebhookEventSummary(webhookLog.PayloadJson)}");
            output.WriteLine($"LAST_META_WEBHOOK_ERROR = {(string.IsNullOrWhiteSpace(webhookLog.ErrorDescripcion) ? "N/A" : Sanitize(webhookLog.ErrorDescripcion))}");
        }

        output.WriteLine("");
        output.WriteLine("=== GRAPH READ-ONLY ===");
        if (credential is null)
        {
            output.WriteLine("HTTP = N/A (bloqueado por Vault/DataProtection)");
            WriteGraphFields(output, null);
            return 1;
        }

        var baseUrl = graphBaseUrl.TrimEnd('/');
        var version = credential.GraphVersion.Trim('/');
        var fields = Uri.EscapeDataString("id,display_phone_number,verified_name,quality_rating,platform_type,is_on_biz_app");
        var uri = $"{baseUrl}/{version}/{Uri.EscapeDataString(phoneNumberId)}?fields={fields}";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        // El token vive sólo en este header en memoria; nunca se escribe a `output`.

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        output.WriteLine($"HTTP = {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            WriteGraphFields(output, null);
            return 1;
        }

        using var document = JsonDocument.Parse(body);
        WriteGraphFields(output, document.RootElement);

        if (template is not null)
        {
            output.WriteLine("");
            output.WriteLine("=== META TEMPLATE READ-ONLY ===");
            var metaTemplate = await FindMetaTemplateAsync(httpClient, baseUrl, version, credential.AccessToken, credential.WabaId, template, ct);
            WriteMetaTemplateComparison(output, template, credential.WabaId, metaTemplate);
        }
        return 0;
    }

    // ---- Lecturas SQL reales (SELECT-only) -----------------------------------------------------

    private static async Task<TenantBaseInfo?> ResolveTenantBaseInfoAsync(string centralConnectionString, int idBase, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                id AS IdBase,
                ISNULL(nombre, '') AS Nombre,
                ISNULL(dbserver, '') AS DbServer,
                ISNULL(dbname, '') AS DbName,
                ISNULL(dbuser, '') AS DbUser,
                ISNULL(dbpassword, '') AS DbPassword
            FROM dbo.bases
            WHERE id = @IdBase;
            """;
        await using var cn = new SqlConnection(centralConnectionString);
        return await cn.QuerySingleOrDefaultAsync<TenantBaseInfo>(new CommandDefinition(sql, new { IdBase = idBase }, cancellationToken: ct));
    }

    private static string BuildTenantConnectionString(TenantBaseInfo baseInfo)
        => new SqlConnectionStringBuilder
        {
            DataSource = baseInfo.DbServer,
            InitialCatalog = baseInfo.DbName,
            UserID = baseInfo.DbUser,
            Password = baseInfo.DbPassword,
            TrustServerCertificate = true
        }.ConnectionString;

    private static async Task<TenantNumeroInfo?> ResolveTenantNumeroAsync(TenantBaseInfo baseInfo, string phoneNumberId, CancellationToken ct)
    {
        // SELECT puro -- a diferencia de ResolveNumeroWhatsAppIdAsync (el que usa el runtime real
        // para inbound), este comando NUNCA inserta la fila si no existe.
        const string sql = "SELECT TOP (1) IdNumero, ISNULL(Nombre,'') AS Nombre, Activo FROM dbo.CONV_WHATSAPP_NUMEROS WHERE PhoneNumberId = @PhoneNumberId;";
        await using var cn = new SqlConnection(BuildTenantConnectionString(baseInfo));
        return await cn.QuerySingleOrDefaultAsync<TenantNumeroInfo>(new CommandDefinition(sql, new { PhoneNumberId = phoneNumberId }, cancellationToken: ct));
    }

    private static async Task<TenantOutboundMessageInfo?> ResolveOutboundMessageAsync(TenantBaseInfo baseInfo, int? idNumero, long? messageId, CancellationToken ct)
    {
        if (idNumero is null)
            return null;

        await using var cn = new SqlConnection(BuildTenantConnectionString(baseInfo));
        await cn.OpenAsync(ct);

        // "Auditar primero el esquema real... no asumir nombres": antes de consultar, se confirma que
        // las columnas que se van a usar existen tal cual en ESTA base tenant -- si alguna falta
        // (esquema real distinto al esperado), se informa en vez de asumir y fallar con un error críptico.
        const string expectedColumns = "IdMensaje,IdConversacion,Direction,EstadoEnvio,FechaHora,PayloadJson,WhatsAppMessageId";
        var missing = await FindMissingColumnsAsync(cn, "dbo.CONV_MENSAJES", expectedColumns.Split(','), ct);
        if (missing.Count > 0)
            throw new InvalidOperationException($"dbo.CONV_MENSAJES no tiene las columnas esperadas: {string.Join(", ", missing)} (esquema real distinto -- no se adivina).");

        if (messageId is > 0)
        {
            const string byIdSql = """
                SELECT TOP (1)
                    m.IdMensaje,
                    m.WhatsAppMessageId,
                    m.EstadoEnvio,
                    m.FechaHora,
                    m.PayloadJson
                FROM dbo.CONV_MENSAJES m
                JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
                WHERE c.IdNumeroWhatsApp = @IdNumero AND m.Direction = N'SALIENTE' AND m.IdMensaje = @IdMensaje
                ORDER BY m.FechaHora DESC;
                """;
            return await cn.QuerySingleOrDefaultAsync<TenantOutboundMessageInfo>(
                new CommandDefinition(byIdSql, new { IdNumero = idNumero.Value, IdMensaje = messageId.Value }, cancellationToken: ct));
        }

        const string sql = """
            SELECT TOP (1)
                m.IdMensaje,
                m.WhatsAppMessageId,
                m.EstadoEnvio,
                m.FechaHora,
                m.PayloadJson
            FROM dbo.CONV_MENSAJES m
            JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
            WHERE c.IdNumeroWhatsApp = @IdNumero AND m.Direction = N'SALIENTE'
            ORDER BY m.FechaHora DESC;
            """;
        return await cn.QuerySingleOrDefaultAsync<TenantOutboundMessageInfo>(new CommandDefinition(sql, new { IdNumero = idNumero.Value }, cancellationToken: ct));
    }

    private static async Task<TenantTemplateInfo?> ResolveTenantTemplateAsync(TenantBaseInfo baseInfo, long? templateId, CancellationToken ct)
    {
        if (templateId is not > 0)
            return null;

        await using var cn = new SqlConnection(BuildTenantConnectionString(baseInfo));
        await cn.OpenAsync(ct);

        const string expectedColumns = "IdPlantilla,NombreMeta,Idioma,EstadoMeta,MetaTemplateId,WabaId,Activa,CuerpoTexto";
        var missing = await FindMissingColumnsAsync(cn, "dbo.CONV_PLANTILLAS", expectedColumns.Split(','), ct);
        if (missing.Count > 0)
            throw new InvalidOperationException($"dbo.CONV_PLANTILLAS no tiene las columnas esperadas: {string.Join(", ", missing)} (esquema real distinto -- no se adivina).");

        const string sql = """
            SELECT TOP (1)
                IdPlantilla,
                ISNULL(NombreMeta, '') AS NombreMeta,
                ISNULL(Idioma, '') AS Idioma,
                ISNULL(EstadoMeta, '') AS EstadoMeta,
                ISNULL(MetaTemplateId, '') AS MetaTemplateId,
                ISNULL(WabaId, '') AS WabaId,
                ISNULL(Activa, 1) AS Activa,
                ISNULL(CuerpoTexto, '') AS CuerpoTexto
            FROM dbo.CONV_PLANTILLAS
            WHERE IdPlantilla = @IdPlantilla;
            """;
        return await cn.QuerySingleOrDefaultAsync<TenantTemplateInfo>(new CommandDefinition(sql, new { IdPlantilla = templateId.Value }, cancellationToken: ct));
    }

    private static async Task<TenantWebhookLogInfo?> ResolveLastWebhookLogAsync(TenantBaseInfo baseInfo, string phoneNumberId, CancellationToken ct)
    {
        await using var cn = new SqlConnection(BuildTenantConnectionString(baseInfo));
        await cn.OpenAsync(ct);

        const string expectedColumns = "Proveedor,PayloadJson,ProcesadoOk,FechaHoraRecepcion,ErrorDescripcion";
        var missing = await FindMissingColumnsAsync(cn, "dbo.CONV_WEBHOOK_LOG", expectedColumns.Split(','), ct);
        if (missing.Count > 0)
            throw new InvalidOperationException($"dbo.CONV_WEBHOOK_LOG no tiene las columnas esperadas: {string.Join(", ", missing)} (esquema real distinto -- no se adivina).");

        // CONV_WEBHOOK_LOG no tiene columna PhoneNumberId propia -- el único lugar donde puede vivir es
        // dentro del JSON resumen de PayloadJson (campo "PhoneNumberIds"). Se intenta correlacionar por
        // ese texto; si nada matchea, se cae al registro META_WHATSAPP más reciente sin filtrar, y el
        // llamador lo marca explícitamente como no correlacionado.
        const string correlatedSql = """
            SELECT TOP (1) PayloadJson, ProcesadoOk, FechaHoraRecepcion, ErrorDescripcion
            FROM dbo.CONV_WEBHOOK_LOG
            WHERE Proveedor = N'META_WHATSAPP' AND PayloadJson LIKE N'%' + @PhonePattern + N'%'
            ORDER BY FechaHoraRecepcion DESC;
            """;
        var phonePattern = $"\"{phoneNumberId}\"";
        var correlated = await cn.QuerySingleOrDefaultAsync<TenantWebhookLogRow>(new CommandDefinition(correlatedSql, new { PhonePattern = phonePattern }, cancellationToken: ct));
        if (correlated is not null)
            return correlated.ToInfo(correlatedToPhoneNumberId: true);

        const string fallbackSql = """
            SELECT TOP (1) PayloadJson, ProcesadoOk, FechaHoraRecepcion, ErrorDescripcion
            FROM dbo.CONV_WEBHOOK_LOG
            WHERE Proveedor = N'META_WHATSAPP'
            ORDER BY FechaHoraRecepcion DESC;
            """;
        var fallback = await cn.QuerySingleOrDefaultAsync<TenantWebhookLogRow>(new CommandDefinition(fallbackSql, cancellationToken: ct));
        return fallback?.ToInfo(correlatedToPhoneNumberId: false);
    }

    private static async Task<IReadOnlyList<string>> FindMissingColumnsAsync(SqlConnection cn, string tableName, IReadOnlyList<string> expectedColumns, CancellationToken ct)
    {
        const string sql = "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@TableName);";
        var present = (await cn.QueryAsync<string>(new CommandDefinition(sql, new { TableName = tableName }, cancellationToken: ct)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return expectedColumns.Where(c => !present.Contains(c.Trim())).ToArray();
    }

    // ---- Extracción/formato puro (testeable sin SQL) --------------------------------------------

    /// <summary>
    /// Sólo tiene sentido cuando EstadoEnvio indica un fallo (ERROR_ENVIO). PayloadJson puede tener dos
    /// formas reales: (a) BuildDeliveryErrorPayload -- {"Error","Type","FechaHora"} generado por
    /// AlfaCore cuando algo falla ANTES o AL LLAMAR a Graph (SendToWhatsAppAsync embebe
    /// "Meta devolvió {status}: {body-de-Graph}" dentro de "Error" si Graph sí llegó a responder); o
    /// (b) un objeto con "error" directo si en algún punto se guarda la respuesta cruda de Graph. Se
    /// intenta extraer code/message reales de Meta si están embebidos; si no, se devuelve el texto
    /// sanitizado tal cual, marcando en qué etapa ocurrió (antes de Graph vs. Graph respondió).
    /// </summary>
    internal static (string? Code, string? Summary) ExtractOutboundErrorInfo(string? estadoEnvio, string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson) || !string.Equals(estadoEnvio, "ERROR_ENVIO", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, Sanitize(payloadJson));

            if (root.TryGetProperty("Error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
            {
                var errorText = errorProp.GetString() ?? string.Empty;
                var braceIndex = errorText.IndexOf('{');
                if (braceIndex >= 0)
                {
                    try
                    {
                        using var inner = JsonDocument.Parse(errorText[braceIndex..]);
                        if (inner.RootElement.ValueKind == JsonValueKind.Object
                            && inner.RootElement.TryGetProperty("error", out var innerError)
                            && innerError.ValueKind == JsonValueKind.Object)
                        {
                            var code = innerError.TryGetProperty("code", out var codeProp) ? codeProp.ToString() : null;
                            var message = innerError.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String
                                ? messageProp.GetString()
                                : null;
                            return (code, Sanitize("[GRAPH] " + (message ?? errorText)));
                        }
                    }
                    catch (JsonException)
                    {
                        // No había JSON embebido real (ej. un mensaje de excepción con una llave suelta) -- se usa el texto tal cual abajo.
                    }
                }

                var type = root.TryGetProperty("Type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String ? typeProp.GetString() : null;
                var stage = string.Equals(type, "System.Net.Http.HttpRequestException", StringComparison.Ordinal) ? "[GRAPH] " : "[ANTES_DE_GRAPH] ";
                return (null, Sanitize(stage + errorText));
            }

            if (root.TryGetProperty("error", out var directError) && directError.ValueKind == JsonValueKind.Object)
            {
                var code = directError.TryGetProperty("code", out var codeProp2) ? codeProp2.ToString() : null;
                var message = directError.TryGetProperty("message", out var messageProp2) && messageProp2.ValueKind == JsonValueKind.String
                    ? messageProp2.GetString()
                    : null;
                return (code, Sanitize("[GRAPH] " + (message ?? "")));
            }

            return (null, Sanitize(payloadJson));
        }
        catch (JsonException)
        {
            return (null, Sanitize(payloadJson));
        }
    }

    /// <summary>Resumen legible del PayloadJson sanitizado de CONV_WEBHOOK_LOG (ver BuildWhatsAppWebhookLogPayload en ConversacionesService) -- nunca el body crudo de Meta.</summary>
    internal static string BuildWebhookEventSummary(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return "(sin datos)";
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return "(no parseable)";

            var parts = new List<string>();
            var messageCount = ReadInt(root, "MessageCount");
            var statusCount = ReadInt(root, "StatusCount");
            if (messageCount > 0) parts.Add($"messages={messageCount}");
            if (statusCount > 0) parts.Add($"statuses={statusCount}");

            if (root.TryGetProperty("EventTypes", out var eventTypes) && eventTypes.ValueKind == JsonValueKind.Array)
                foreach (var e in eventTypes.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                        parts.Add(e.GetString()!);

            if (root.TryGetProperty("ErrorCodes", out var errorCodes) && errorCodes.ValueKind == JsonValueKind.Array && errorCodes.GetArrayLength() > 0)
                parts.Add($"errorCodes=[{string.Join(",", errorCodes.EnumerateArray().Select(e => e.ToString()))}]");

            return parts.Count == 0 ? "(vacío)" : string.Join(" + ", parts);
        }
        catch (JsonException)
        {
            return "(no parseable)";
        }
    }

    private static int ReadInt(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var cleaned = new string(text.Where(static c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length <= 400 ? cleaned : cleaned[..400] + "…";
    }

    private static string ValueOrEmpty(string? text)
        => string.IsNullOrWhiteSpace(text) ? "(vacío)" : Sanitize(text);

    private static string DescribeSendComponents(string? bodyText)
    {
        var variableCount = CountTemplateVariables(bodyText);
        return variableCount == 0
            ? "[]"
            : $"BODY_TEXT_PARAMETERS ({variableCount}; valores concretos no persistidos en CONV_MENSAJES)";
    }

    private static int CountTemplateVariables(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var max = 0;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"\{\{\s*(\d+)\s*\}\}"))
            if (int.TryParse(match.Groups[1].Value, out var index))
                max = Math.Max(max, index);
        return max;
    }

    private static async Task<MetaTemplateInfo?> FindMetaTemplateAsync(
        HttpClient httpClient,
        string baseUrl,
        string version,
        string accessToken,
        string wabaId,
        TenantTemplateInfo template,
        CancellationToken ct)
    {
        var fields = Uri.EscapeDataString("id,name,language,status,category,components");
        var uri = $"{baseUrl}/{version}/{Uri.EscapeDataString(wabaId)}/message_templates?fields={fields}&limit=100";
        if (!string.IsNullOrWhiteSpace(template.NombreMeta))
            uri += $"&name={Uri.EscapeDataString(template.NombreMeta)}";

        for (var page = 0; page < 5 && !string.IsNullOrWhiteSpace(uri); page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new MetaTemplateInfo(GraphHttpStatus: ((int)response.StatusCode).ToString(), Found: false);

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array)
            {
                MetaTemplateInfo? byName = null;
                foreach (var item in data.EnumerateArray())
                {
                    var current = MetaTemplateInfo.From(item, ((int)response.StatusCode).ToString());
                    if (!string.IsNullOrWhiteSpace(template.MetaTemplateId)
                        && string.Equals(current.Id, template.MetaTemplateId, StringComparison.Ordinal))
                        return current with { Found = true, FoundById = true };
                    if (string.Equals(current.Name, template.NombreMeta, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(current.Language, template.Idioma, StringComparison.OrdinalIgnoreCase))
                        byName = current with { Found = true, FoundByNameLanguage = true };
                }

                if (byName is not null)
                    return byName;
            }

            uri = TryReadNextPageUri(root);
        }

        return new MetaTemplateInfo(GraphHttpStatus: "200", Found: false);
    }

    private static string? TryReadNextPageUri(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("paging", out var paging)
            && paging.ValueKind == JsonValueKind.Object
            && paging.TryGetProperty("next", out var next)
            && next.ValueKind == JsonValueKind.String)
            return next.GetString();
        return null;
    }

    private static void WriteMetaTemplateComparison(TextWriter output, TenantTemplateInfo local, string queryWabaId, MetaTemplateInfo? meta)
    {
        meta ??= new MetaTemplateInfo(GraphHttpStatus: "N/A", Found: false);
        output.WriteLine($"META_TEMPLATE_GRAPH_HTTP = {meta.GraphHttpStatus}");
        output.WriteLine($"LOCAL_TEMPLATE_ID = {local.IdPlantilla}");
        output.WriteLine($"LOCAL_META_TEMPLATE_ID = {ValueOrEmpty(local.MetaTemplateId)}");
        output.WriteLine($"LOCAL_NAME = {ValueOrEmpty(local.NombreMeta)}");
        output.WriteLine($"LOCAL_LANGUAGE = {ValueOrEmpty(local.Idioma)}");
        output.WriteLine($"META_TEMPLATE_FOUND = {meta.Found}");
        output.WriteLine($"META_TEMPLATE_FOUND_BY_ID = {meta.FoundById}");
        output.WriteLine($"META_TEMPLATE_ID = {ValueOrEmpty(meta.Id)}");
        output.WriteLine($"META_TEMPLATE_NAME = {ValueOrEmpty(meta.Name)}");
        output.WriteLine($"META_TEMPLATE_LANGUAGE = {ValueOrEmpty(meta.Language)}");
        output.WriteLine($"META_TEMPLATE_STATUS = {ValueOrEmpty(meta.Status)}");
        output.WriteLine($"META_TEMPLATE_CATEGORY = {ValueOrEmpty(meta.Category)}");
        output.WriteLine($"ID_MATCH = {BoolMatch(local.MetaTemplateId, meta.Id)}");
        output.WriteLine($"NAME_MATCH = {BoolMatch(local.NombreMeta, meta.Name)}");
        output.WriteLine($"LANGUAGE_MATCH = {BoolMatch(local.Idioma, meta.Language)}");
        output.WriteLine($"LOCAL_STATUS = {ValueOrEmpty(local.EstadoMeta)}");
        output.WriteLine($"STATUS_MATCH = {BoolMatch(local.EstadoMeta, meta.Status)}");
        output.WriteLine($"LOCAL_WABA = {ValueOrEmpty(local.WabaId)}");
        output.WriteLine($"META_QUERY_WABA = {ValueOrEmpty(queryWabaId)}");
        output.WriteLine($"WABA_MATCH = {BoolMatch(local.WabaId, queryWabaId)}");
        output.WriteLine($"TEMPLATE_MATCH = {BuildTemplateMatch(local, queryWabaId, meta)}");
        output.WriteLine($"EVIDENCE = {BuildTemplateEvidence(local, queryWabaId, meta)}");
    }

    private static string BoolMatch(string? left, string? right)
        => (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase)).ToString();

    private static string BuildTemplateMatch(TenantTemplateInfo local, string queryWabaId, MetaTemplateInfo meta)
    {
        if (!meta.Found)
            return "NO (Meta no devolvió la plantilla en la WABA consultada)";
        var idOk = string.IsNullOrWhiteSpace(local.MetaTemplateId) || string.Equals(local.MetaTemplateId, meta.Id, StringComparison.Ordinal);
        var nameOk = string.Equals(local.NombreMeta, meta.Name, StringComparison.OrdinalIgnoreCase);
        var languageOk = string.Equals(local.Idioma, meta.Language, StringComparison.OrdinalIgnoreCase);
        var wabaOk = string.Equals(local.WabaId, queryWabaId, StringComparison.Ordinal);
        return (idOk && nameOk && languageOk && wabaOk).ToString();
    }

    private static string BuildTemplateEvidence(TenantTemplateInfo local, string queryWabaId, MetaTemplateInfo meta)
    {
        if (!meta.Found)
            return $"Meta no devolvió id/name/language consultados en WABA {queryWabaId}.";

        var parts = new List<string>();
        if (!string.Equals(local.MetaTemplateId, meta.Id, StringComparison.Ordinal))
            parts.Add($"id local={ValueOrEmpty(local.MetaTemplateId)} meta={ValueOrEmpty(meta.Id)}");
        if (!string.Equals(local.NombreMeta, meta.Name, StringComparison.OrdinalIgnoreCase))
            parts.Add($"name local={ValueOrEmpty(local.NombreMeta)} meta={ValueOrEmpty(meta.Name)}");
        if (!string.Equals(local.Idioma, meta.Language, StringComparison.OrdinalIgnoreCase))
            parts.Add($"language local={ValueOrEmpty(local.Idioma)} meta={ValueOrEmpty(meta.Language)}");
        if (!string.Equals(local.WabaId, queryWabaId, StringComparison.Ordinal))
            parts.Add($"waba local={ValueOrEmpty(local.WabaId)} query={ValueOrEmpty(queryWabaId)}");
        if (!string.Equals(local.EstadoMeta, meta.Status, StringComparison.OrdinalIgnoreCase))
            parts.Add($"status local={ValueOrEmpty(local.EstadoMeta)} meta={ValueOrEmpty(meta.Status)}");

        return parts.Count == 0
            ? "Local DB y Meta coinciden para id/name/language/status/WABA consultada."
            : string.Join("; ", parts);
    }

    private static void WriteBlockedFromOwnership(TextWriter output)
    {
        output.WriteLine("VAULT_CREDENTIAL = N/A (bloqueado)");
        WriteBlockedFromVault(output);
    }

    private static void WriteBlockedFromVault(TextWriter output)
    {
        output.WriteLine("ID_NUMERO = N/A (bloqueado)");
        output.WriteLine("NUMERO_ACTIVO = N/A (bloqueado)");
        output.WriteLine("ORIGEN/CLASIFICACION = N/A (bloqueado)");
        output.WriteLine("");
        output.WriteLine("=== OUTBOUND === (bloqueado, no se llegó a consultar)");
        output.WriteLine("=== WEBHOOK === (bloqueado, no se llegó a consultar)");
        output.WriteLine("=== GRAPH READ-ONLY ===");
        output.WriteLine("HTTP = N/A (bloqueado)");
        WriteGraphFields(output, null);
    }

    private static void WriteGraphFields(TextWriter output, JsonElement? root)
    {
        output.WriteLine($"platform_type = {GetField(root, "platform_type")}");
        output.WriteLine($"is_on_biz_app = {GetBoolField(root, "is_on_biz_app")}");
        output.WriteLine($"quality_rating = {GetField(root, "quality_rating")}");
    }

    private static string GetField(JsonElement? root, string propertyName)
        => root is { } element && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "NO DISPONIBLE")
            : "NO DISPONIBLE";

    private static string GetBoolField(JsonElement? root, string propertyName)
        => root is { } element && element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean().ToString()
            : "NO DISPONIBLE";

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static long? ReadLongOption(IReadOnlyList<string> args, string name)
        => long.TryParse(ReadOption(args, name), out var value) && value > 0 ? value : null;

    internal sealed record TenantBaseInfo(int IdBase, string Nombre, string DbServer, string DbName, string DbUser, string DbPassword);
    internal sealed record TenantNumeroInfo(int IdNumero, string Nombre, bool Activo);
    internal sealed record TenantOutboundMessageInfo(long IdMensaje, string? WhatsAppMessageId, string? EstadoEnvio, DateTime FechaHora, string? PayloadJson);
    internal sealed record TenantTemplateInfo(long IdPlantilla, string NombreMeta, string Idioma, string EstadoMeta, string MetaTemplateId, string WabaId, bool Activa, string CuerpoTexto);
    internal sealed record TenantWebhookLogInfo(DateTime FechaHoraRecepcion, bool? ProcesadoOk, string? PayloadJson, string? ErrorDescripcion, bool CorrelatedToPhoneNumberId);
    internal sealed record MetaTemplateInfo(string GraphHttpStatus, bool Found, string? Id = null, string? Name = null, string? Language = null, string? Status = null, string? Category = null, bool FoundById = false, bool FoundByNameLanguage = false)
    {
        public static MetaTemplateInfo From(JsonElement item, string graphHttpStatus)
            => new(
                graphHttpStatus,
                Found: true,
                Id: ReadString(item, "id"),
                Name: ReadString(item, "name"),
                Language: ReadString(item, "language"),
                Status: ReadString(item, "status"),
                Category: ReadString(item, "category"));

        private static string? ReadString(JsonElement item, string propertyName)
            => item.ValueKind == JsonValueKind.Object
               && item.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private sealed record TenantWebhookLogRow(string? PayloadJson, bool? ProcesadoOk, DateTime FechaHoraRecepcion, string? ErrorDescripcion)
    {
        public TenantWebhookLogInfo ToInfo(bool correlatedToPhoneNumberId)
            => new(FechaHoraRecepcion, ProcesadoOk, PayloadJson, ErrorDescripcion, correlatedToPhoneNumberId);
    }
}

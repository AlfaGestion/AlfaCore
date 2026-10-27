using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Clave pública del chat web embebible (canal WEBCHAT). En modo SaaS vive en
/// AlfaCentral.dbo.bases.WebChatSiteKey y es lo único con lo que un visitante anónimo puede
/// llegar a una base; en instalaciones de una sola base se guarda en TA_CONFIGURACION
/// (CONV_WEBCHAT_SITE_KEY).
///
/// La columna WebChatSiteKey se consulta aparte, nunca dentro del SELECT general de
/// CentralBasesService: si en una base central todavía no se corrió el ALTER TABLE, solo falla
/// el chat web y no el login ni el resto de los webhooks.
/// </summary>
public sealed class WebChatSiteKeyService(
    IConfiguration configuration,
    IAppModeService appMode,
    ISessionService sessionService,
    ICentralBasesService centralBases,
    IConversacionesConfigService configService,
    IConversacionesAuthorizationService authorizationService,
    IAppEventService appEvents) : IWebChatSiteKeyService
{
    private const int SqlInvalidColumn = 207;

    private string CentralConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public async Task<string?> GetSiteKeyAsync(CancellationToken ct = default)
    {
        if (!appMode.IsSaaSMode)
        {
            var config = await configService.GetWebChatConfigAsync(ct);
            return WebChatSiteKeys.IsValidFormat(config.SiteKey) ? config.SiteKey : null;
        }

        var idBase = RequireActiveBaseId();
        try
        {
            await using var cn = new SqlConnection(CentralConnectionString);
            return await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT WebChatSiteKey FROM dbo.bases WHERE id = @IdBase;",
                new { IdBase = idBase },
                cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number == SqlInvalidColumn)
        {
            throw MissingCentralColumn(ex);
        }
    }

    public async Task<string> EnsureSiteKeyAsync(CancellationToken ct = default)
    {
        if (!appMode.IsSaaSMode)
            return await configService.EnsureLocalWebChatSiteKeyAsync(ct);

        await authorizationService.EnsureCanManageAsync(ct);
        var idBase = RequireActiveBaseId();

        // Mismo esquema que CentralBasesService.EnsureWebhookTokenAsync: el UPDATE con
        // "WHERE WebChatSiteKey IS NULL" solo aplica una vez aunque lleguen dos pedidos juntos, y
        // se relee para devolver siempre la clave que efectivamente quedó grabada.
        const string sql = """
            UPDATE dbo.bases
            SET WebChatSiteKey = @SiteKey
            WHERE id = @IdBase
              AND WebChatSiteKey IS NULL;

            SELECT WebChatSiteKey FROM dbo.bases WHERE id = @IdBase;
            """;

        try
        {
            await using var cn = new SqlConnection(CentralConnectionString);
            var siteKey = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                sql,
                new { IdBase = idBase, SiteKey = WebChatSiteKeys.Generate() },
                cancellationToken: ct));

            if (string.IsNullOrWhiteSpace(siteKey))
                throw new InvalidOperationException($"No existe la base central {idBase}.");

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "EnsureWebChatSiteKey",
                "bases",
                idBase.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Clave del chat del sitio web consultada/generada.",
                null,
                ct);

            return siteKey.Trim();
        }
        catch (SqlException ex) when (ex.Number == SqlInvalidColumn)
        {
            throw MissingCentralColumn(ex);
        }
    }

    public async Task<int?> TryResolveTenantAsync(string siteKey, CancellationToken ct = default)
    {
        if (!WebChatSiteKeys.IsValidFormat(siteKey))
            return null;

        var normalized = siteKey.Trim();

        if (!appMode.IsSaaSMode)
        {
            // Una sola base: no hay a dónde enrutar, solo confirmar que la clave es la de esta base.
            var config = await configService.GetWebChatConfigAsync(ct);
            return string.Equals(config.SiteKey, normalized, StringComparison.Ordinal) ? 0 : null;
        }

        int? idBase;
        try
        {
            await using var cn = new SqlConnection(CentralConnectionString);
            idBase = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT TOP (1) id FROM dbo.bases WHERE WebChatSiteKey = @SiteKey;",
                new { SiteKey = normalized },
                cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number == SqlInvalidColumn)
        {
            await appEvents.LogErrorAsync("Conversaciones", "WebChatResolveTenant", ex,
                "La base central no tiene la columna WebChatSiteKey.", null, AppEventSeverity.Warning, ct);
            return null;
        }

        if (idBase is null)
            return null;

        var baseInfo = await centralBases.GetByIdAsync(idBase.Value, ct);
        if (baseInfo is null)
            return null;

        // Igual que Program.TryResolveWebhookTenantAsync: fija la base para el resto del request.
        sessionService.SetWebhookOverride(new SessionDto
        {
            BaseId = baseInfo.IdBase,
            Nombre = baseInfo.Nombre,
            Servidor = baseInfo.DbServer,
            BaseDatos = baseInfo.DbName,
            Usuario = baseInfo.DbUser,
            Password = baseInfo.DbPassword,
            TrustServerCertificate = true
        });

        return baseInfo.IdBase;
    }

    private int RequireActiveBaseId()
    {
        var idBase = sessionService.GetActiveSession()?.BaseId;
        if (idBase is null or 0)
            throw new InvalidOperationException("No hay una base activa para generar la clave del chat del sitio web.");
        return idBase.Value;
    }

    private static InvalidOperationException MissingCentralColumn(SqlException ex)
        => new(
            "La base central todavía no está preparada para el chat del sitio web (falta la columna dbo.bases.WebChatSiteKey). " +
            "Pedí a soporte que ejecute el script de AlfaCentral indicado en docs/modulos/conversaciones_webchat.md.",
            ex);
}

using System.Text.RegularExpressions;
using AlfaCore.Configuration;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Estado operativo de una integración WhatsApp ya activa (post-onboarding), independiente del
/// registro de onboarding en dbo.WhatsAppEmbeddedOnboarding (que es de un solo uso y expira). AlfaNet
/// opera como Technology Provider: el cliente final paga directamente a Meta, AlfaNet nunca absorbe
/// billing ni implementa Credit Sharing -- solo expone el problema para que el cliente lo resuelva en
/// Meta Business Manager.
///
/// La CLASIFICACIÓN del error (qué código, qué razón, si amerita este estado persistente) vive
/// exclusivamente en AlfaCore.Models.WhatsAppOutboundErrorClassifier -- este archivo solo persiste y
/// resuelve el resultado de esa clasificación, nunca vuelve a interpretar errors[].code por su cuenta.
/// </summary>
public sealed record WhatsAppIntegrationHealthStatus(
    int IdBase,
    string WabaId,
    string PhoneNumberId,
    string State,
    string? Reason,
    string ErrorCode,
    string DetailSummary,
    string? CtaUrl,
    DateTime? RequiredSinceUtc,
    DateTime? ResolvedAtUtc,
    DateTime ModifiedAtUtc);

public interface IWhatsAppIntegrationHealthStore
{
    /// <param name="eventTimestampUtc">
    /// Timestamp reportado por Meta para el evento que disparó el problema (no la hora local de
    /// procesamiento). Se usa como ancla para que solo un evento positivo posterior pueda resolverlo.
    /// </param>
    Task MarkActionRequiredAsync(int idBase, string wabaId, string phoneNumberId, string reason, string errorCode, string detailSummary, string? ctaUrl, string sourceMessageId, DateTime eventTimestampUtc, CancellationToken ct = default);

    /// <summary>
    /// Resuelve el estado ACTION_REQUIRED de (idBase, wabaId, phoneNumberId) únicamente si
    /// <paramref name="eventTimestampUtc"/> (timestamp de Meta del evento positivo, no la hora local)
    /// es posterior al RequiredSinceUtc almacenado. Un evento viejo/reordenado nunca limpia un fallo
    /// más nuevo. No afecta ningún mensaje individual (CONV_MENSAJES no se toca desde acá).
    /// </summary>
    Task ResolveIfSubsequentAsync(int idBase, string wabaId, string phoneNumberId, DateTime eventTimestampUtc, CancellationToken ct = default);

    Task<WhatsAppIntegrationHealthStatus?> GetAsync(int idBase, string wabaId, string phoneNumberId, CancellationToken ct = default);
}

public sealed class WhatsAppIntegrationHealthStore(IConfiguration configuration, IHostEnvironment? environment = null) : IWhatsAppIntegrationHealthStore
{
    private string ConnectionString => WhatsAppEmbeddedSignupConnection.Resolve(configuration, environment);

    public async Task MarkActionRequiredAsync(int idBase, string wabaId, string phoneNumberId, string reason, string errorCode, string detailSummary, string? ctaUrl, string sourceMessageId, DateTime eventTimestampUtc, CancellationToken ct = default)
    {
        if (idBase <= 0) throw new ArgumentOutOfRangeException(nameof(idBase));
        var normalizedWaba = (wabaId ?? string.Empty).Trim();
        var normalizedPhone = (phoneNumberId ?? string.Empty).Trim();
        if (normalizedWaba.Length == 0 || normalizedPhone.Length == 0)
            throw new ArgumentException("WabaId y PhoneNumberId son obligatorios para registrar un estado de integración.");

        // RequiredSinceUtc nunca retrocede: si un evento de falla llega desordenado (más viejo que el
        // ya registrado), se conserva el ancla más reciente para no debilitar la protección temporal
        // de ResolveIfSubsequentAsync.
        const string sql = """
            MERGE dbo.WhatsAppIntegrationHealth AS target
            USING (SELECT @IdBase AS IdBase, @WabaId AS WabaId, @PhoneNumberId AS PhoneNumberId) AS source
                ON target.IdBase = source.IdBase AND target.WabaId = source.WabaId AND target.PhoneNumberId = source.PhoneNumberId
            WHEN MATCHED THEN
                UPDATE SET State = N'ACTION_REQUIRED', Reason = @Reason, ErrorCode = @ErrorCode,
                    DetailSummary = @DetailSummary, CtaUrl = @CtaUrl, SourceMessageId = @SourceMessageId,
                    RequiredSinceUtc = CASE WHEN target.RequiredSinceUtc IS NULL OR @EventTimestampUtc > target.RequiredSinceUtc THEN @EventTimestampUtc ELSE target.RequiredSinceUtc END,
                    ResolvedAtUtc = NULL,
                    ModifiedAtUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (IdBase, WabaId, PhoneNumberId, State, Reason, ErrorCode, DetailSummary, CtaUrl, SourceMessageId, RequiredSinceUtc, ResolvedAtUtc, CreatedAtUtc, ModifiedAtUtc)
                VALUES (@IdBase, @WabaId, @PhoneNumberId, N'ACTION_REQUIRED', @Reason, @ErrorCode, @DetailSummary, @CtaUrl, @SourceMessageId, @EventTimestampUtc, NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            IdBase = idBase,
            WabaId = normalizedWaba,
            PhoneNumberId = normalizedPhone,
            Reason = reason,
            ErrorCode = errorCode,
            DetailSummary = detailSummary,
            CtaUrl = (object?)ctaUrl ?? DBNull.Value,
            SourceMessageId = sourceMessageId,
            EventTimestampUtc = eventTimestampUtc
        }, cancellationToken: ct));
    }

    public async Task ResolveIfSubsequentAsync(int idBase, string wabaId, string phoneNumberId, DateTime eventTimestampUtc, CancellationToken ct = default)
    {
        if (idBase <= 0) return;
        var normalizedWaba = (wabaId ?? string.Empty).Trim();
        var normalizedPhone = (phoneNumberId ?? string.Empty).Trim();
        if (normalizedWaba.Length == 0 || normalizedPhone.Length == 0)
            return;

        // El WHERE hace la comparación temporal de forma atómica: si RequiredSinceUtc es NULL (no
        // debería pasar con State=ACTION_REQUIRED, pero por robustez) se resuelve igual; si el evento
        // es más viejo o igual que RequiredSinceUtc, la fila no matchea y no se actualiza nada.
        const string sql = """
            UPDATE dbo.WhatsAppIntegrationHealth
            SET State = N'OK', ResolvedAtUtc = SYSUTCDATETIME(), ModifiedAtUtc = SYSUTCDATETIME()
            WHERE IdBase = @IdBase AND WabaId = @WabaId AND PhoneNumberId = @PhoneNumberId
              AND State = N'ACTION_REQUIRED'
              AND (RequiredSinceUtc IS NULL OR @EventTimestampUtc > RequiredSinceUtc);
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            IdBase = idBase,
            WabaId = normalizedWaba,
            PhoneNumberId = normalizedPhone,
            EventTimestampUtc = eventTimestampUtc
        }, cancellationToken: ct));
    }

    public async Task<WhatsAppIntegrationHealthStatus?> GetAsync(int idBase, string wabaId, string phoneNumberId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT IdBase, WabaId, PhoneNumberId, State, Reason, ErrorCode, DetailSummary, CtaUrl, RequiredSinceUtc, ResolvedAtUtc, ModifiedAtUtc
            FROM dbo.WhatsAppIntegrationHealth
            WHERE IdBase = @IdBase AND WabaId = @WabaId AND PhoneNumberId = @PhoneNumberId;
            """;
        await using var cn = new SqlConnection(ConnectionString);
        return await cn.QuerySingleOrDefaultAsync<WhatsAppIntegrationHealthStatus>(new CommandDefinition(sql, new
        {
            IdBase = idBase,
            WabaId = (wabaId ?? string.Empty).Trim(),
            PhoneNumberId = (phoneNumberId ?? string.Empty).Trim()
        }, cancellationToken: ct));
    }
}

/// <summary>
/// Evita renderizar como enlace clickeable cualquier URL que llegue dentro de un payload de Meta
/// (por ejemplo <c>error_data.details</c>). Solo se acepta HTTPS contra una allowlist estricta de
/// hosts de Meta/Facebook; cualquier otro esquema u host cae al CTA genérico del billing hub.
/// </summary>
public static class WhatsAppMetaCtaLinks
{
    private static readonly HashSet<string> TrustedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "business.facebook.com",
        "business.whatsapp.com",
        "www.facebook.com",
        "facebook.com",
    };

    public const string DefaultBillingHubUrl = "https://business.facebook.com/billing_hub/accounts";

    private static readonly Regex UrlPattern = new(@"https?://[^\s""'<>]+", RegexOptions.Compiled);

    /// <summary>
    /// Valida que <paramref name="candidateUrl"/> ya sea una URL completa y aislada (no texto libre).
    /// Devuelve null si no cumple el esquema HTTPS + allowlist de host -- nunca la URL original.
    /// </summary>
    public static string? ResolveBillingCtaUrl(string? candidateUrl)
        => Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            && TrustedHosts.Contains(uri.Host)
                ? uri.ToString()
                : null;

    /// <summary>
    /// Busca la primera URL dentro de texto libre proveniente de Meta (p. ej. <c>error_data.details</c>)
    /// y la valida. Nunca acepta esquemas u hosts fuera de la allowlist; el texto libre en sí nunca se
    /// usa como link, solo la subcadena que matchea una URL bien formada y confiable.
    /// </summary>
    public static string? ExtractSafeMetaCtaUrl(string? freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText))
            return null;
        var match = UrlPattern.Match(freeText);
        return match.Success ? ResolveBillingCtaUrl(match.Value) : null;
    }
}

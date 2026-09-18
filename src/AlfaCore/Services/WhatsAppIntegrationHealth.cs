using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Configuration;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Estado operativo de una integración WhatsApp ya activa (post-onboarding), independiente del
/// registro de onboarding en <see cref="WhatsAppEmbeddedOnboarding"/> (que es de un solo uso y
/// expira). AlfaNet opera como Technology Provider bajo CustomerPaysMeta: nunca resuelve un
/// bloqueo de facturación por el cliente, solo lo expone para que el cliente lo resuelva en Meta.
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
    DateTime ModifiedAtUtc);

public interface IWhatsAppIntegrationHealthStore
{
    Task MarkActionRequiredAsync(int idBase, string wabaId, string phoneNumberId, string reason, string errorCode, string detailSummary, string? ctaUrl, string sourceMessageId, CancellationToken ct = default);
    Task<WhatsAppIntegrationHealthStatus?> GetAsync(int idBase, string wabaId, string phoneNumberId, CancellationToken ct = default);
}

public sealed class WhatsAppIntegrationHealthStore(IConfiguration configuration, IHostEnvironment? environment = null) : IWhatsAppIntegrationHealthStore
{
    private string ConnectionString => WhatsAppEmbeddedSignupConnection.Resolve(configuration, environment);

    public async Task MarkActionRequiredAsync(int idBase, string wabaId, string phoneNumberId, string reason, string errorCode, string detailSummary, string? ctaUrl, string sourceMessageId, CancellationToken ct = default)
    {
        if (idBase <= 0) throw new ArgumentOutOfRangeException(nameof(idBase));
        var normalizedWaba = (wabaId ?? string.Empty).Trim();
        var normalizedPhone = (phoneNumberId ?? string.Empty).Trim();
        if (normalizedWaba.Length == 0 || normalizedPhone.Length == 0)
            throw new ArgumentException("WabaId y PhoneNumberId son obligatorios para registrar un estado de integración.");

        const string sql = """
            MERGE dbo.WhatsAppIntegrationHealth AS target
            USING (SELECT @IdBase AS IdBase, @WabaId AS WabaId, @PhoneNumberId AS PhoneNumberId) AS source
                ON target.IdBase = source.IdBase AND target.WabaId = source.WabaId AND target.PhoneNumberId = source.PhoneNumberId
            WHEN MATCHED THEN
                UPDATE SET State = N'ACTION_REQUIRED', Reason = @Reason, ErrorCode = @ErrorCode,
                    DetailSummary = @DetailSummary, CtaUrl = @CtaUrl, SourceMessageId = @SourceMessageId, ModifiedAtUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (IdBase, WabaId, PhoneNumberId, State, Reason, ErrorCode, DetailSummary, CtaUrl, SourceMessageId, CreatedAtUtc, ModifiedAtUtc)
                VALUES (@IdBase, @WabaId, @PhoneNumberId, N'ACTION_REQUIRED', @Reason, @ErrorCode, @DetailSummary, @CtaUrl, @SourceMessageId, SYSUTCDATETIME(), SYSUTCDATETIME());
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
            SourceMessageId = sourceMessageId
        }, cancellationToken: ct));
    }

    public async Task<WhatsAppIntegrationHealthStatus?> GetAsync(int idBase, string wabaId, string phoneNumberId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT IdBase, WabaId, PhoneNumberId, State, Reason, ErrorCode, DetailSummary, CtaUrl, ModifiedAtUtc
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
/// Clasifica errores de status callbacks de Meta que representan un problema de configuración de la
/// cuenta (no del mensaje/contacto puntual). Solo 131042 ("Business eligibility payment issue") se
/// trata como bloqueo de facturación; cualquier otro código de error deja el mensaje en ERROR_ENVIO
/// sin tocar el estado de la integración.
/// </summary>
public static class WhatsAppMetaErrorClassifier
{
    public const string PaymentSetupErrorCode = "131042";

    public static bool IsCustomerPaymentSetupRequired(string rawStatusJson)
    {
        if (string.IsNullOrWhiteSpace(rawStatusJson))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(rawStatusJson);
            if (!doc.RootElement.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var error in errors.EnumerateArray())
                if (error.TryGetProperty("code", out var code)
                    && string.Equals(code.ToString(), PaymentSetupErrorCode, StringComparison.Ordinal))
                    return true;
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
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

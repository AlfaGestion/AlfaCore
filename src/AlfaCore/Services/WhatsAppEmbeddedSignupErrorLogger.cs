using AlfaCore.Models;
using AlfaCore.Repositories;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class WhatsAppEmbeddedSignupErrorLogger(IAuxErrRepository auxErrRepository) : IWhatsAppEmbeddedSignupErrorLogger
{
    public async Task<string> LogAsync(Guid idOnboarding, int idBase, string step, string errorCode, string? wabaId, string? phoneNumberId, int retryCount, CancellationToken ct = default)
    {
        var incidentId = $"WAES-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..29].ToUpperInvariant();
        var safeCode = NormalizeCode(errorCode);
        var safeStep = NormalizeCode(step);
        var safeDetail = JsonSerializer.Serialize(new
        {
            IdOnboarding = idOnboarding,
            IdBase = idBase,
            Step = safeStep,
            ErrorCode = safeCode,
            IncidentId = incidentId,
            WabaId = NormalizeMetaId(wabaId),
            PhoneNumberId = NormalizeMetaId(phoneNumberId),
            RetryCount = Math.Max(0, retryCount)
        });

        await auxErrRepository.InsertAsync(new AuxErrEntry
        {
            Process = "WhatsAppEmbeddedSignup",
            ErrorCode = 0,
            Description = $"Embedded Signup falló en {safeStep}. Código {safeCode}. Incidente {incidentId}.",
            SqlDetail = safeDetail,
            Pc = Environment.MachineName,
            UserName = string.Empty
        }, ct);
        return incidentId;
    }

    private static string NormalizeCode(string? value)
    {
        var normalized = new string((value ?? string.Empty).Trim().ToUpperInvariant()
            .Where(static character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_').Take(80).ToArray());
        return normalized.Length == 0 ? "UNKNOWN" : normalized;
    }

    private static string NormalizeMetaId(string? value)
        => new((value ?? string.Empty).Where(static character => character is >= '0' and <= '9').Take(40).ToArray());
}

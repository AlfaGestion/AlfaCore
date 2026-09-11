using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Options;

namespace AlfaCore.Configuration;

/// <summary>
/// Modo one-shot <c>--reconcile-embedded-signup-status --base &lt;idBase&gt;</c>: invoca el mismo método
/// público que usa la UI para leer el estado de un onboarding (<c>GetLatestStatusForBaseAsync</c>),
/// ejerciendo así la reconciliación automática STARTED → EXPIRED (ver
/// WhatsAppEmbeddedSignupOrchestrator.ReconcileExpiredAsync) sin ningún UPDATE/DELETE manual.
///
/// No es una herramienta recurrente para "liberar" onboardings: la reconciliación ya ocurre sola en
/// GetStatusAsync/GetLatestStatusForBaseAsync/StartAsync durante el uso normal de la app. Este verbo
/// sólo sirve para verificar, de forma auditable y de una sola vez, que ese mismo código de producción
/// efectivamente resuelve un STARTED vencido puntual.
///
/// Reglas fijas:
///  - sólo lee/reconcilia (el único write posible es el UPDATE atómico guardado dentro de
///    ExpireStaleStartedAsync, que es la transición de dominio real, no un bypass);
///  - no arranca Kestrel ni hosted services (se corta en Program.Main antes de CreateBuilder);
///  - no imprime tokens, secretos ni PIN (WhatsAppEmbeddedStatusView no los expone).
/// </summary>
internal static class WhatsAppEmbeddedSignupStatusReconcileCommand
{
    public const string Verb = "--reconcile-embedded-signup-status";

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, Verb, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IConfiguration configuration, TextWriter output, CancellationToken ct)
    {
        var idBaseArg = ReadOption(args, "--base");
        if (!int.TryParse(idBaseArg, out var idBase) || idBase <= 0)
        {
            output.WriteLine("Uso: AlfaCore --reconcile-embedded-signup-status --base <idBase>");
            return 1;
        }

        var options = configuration.GetSection(WhatsAppEmbeddedSignupOptions.SectionName).Get<WhatsAppEmbeddedSignupOptions>() ?? new();
        var store = new WhatsAppEmbeddedSignupStore(configuration);
        var orchestrator = new WhatsAppEmbeddedSignupOrchestrator(
            store,
            new WhatsAppEmbeddedSignupStateProtector(),
            new UnusedOAuthClient(),
            new UnusedCredentialVault(),
            Microsoft.Extensions.Options.Options.Create(options));

        output.WriteLine("== reconcile-embedded-signup-status ==");
        output.WriteLine($"IdBase: {idBase}");

        var status = await orchestrator.GetLatestStatusForBaseAsync(idBase, ct);
        if (status is null)
        {
            output.WriteLine("Resultado: sin onboarding para esta base, o feature deshabilitada (Enabled=false).");
            return 0;
        }

        output.WriteLine($"IdOnboarding: {status.IdOnboarding}");
        output.WriteLine($"Status: {status.Status}");
        output.WriteLine($"Step: {status.Step}");
        output.WriteLine($"OnboardingMode: {status.OnboardingMode}");
        output.WriteLine($"IncidentId: {status.IncidentId}");
        return 0;
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private sealed class UnusedOAuthClient : IMetaOAuthClient
    {
        public Task<MetaTokenExchangeResult> ExchangeCodeAsync(string authorizationCode, WhatsAppVaultSecretContext vaultContext, CancellationToken ct = default)
            => throw new NotSupportedException("No usado por --reconcile-embedded-signup-status.");
        public Task<MetaTokenInspectionResult> InspectTokenAsync(WhatsAppCredentialReference tokenReference, CancellationToken ct = default)
            => throw new NotSupportedException("No usado por --reconcile-embedded-signup-status.");
    }

    private sealed class UnusedCredentialVault : IWhatsAppCredentialVault
    {
        public Task<WhatsAppCredentialReference> StoreAsync(WhatsAppVaultSecretContext context, ReadOnlyMemory<char> secret, CancellationToken ct = default)
            => throw new NotSupportedException("No usado por --reconcile-embedded-signup-status.");
        public Task<ReadOnlyMemory<char>> GetAsync(WhatsAppCredentialReference reference, CancellationToken ct = default)
            => throw new NotSupportedException("No usado por --reconcile-embedded-signup-status.");
        public Task RemoveAsync(WhatsAppCredentialReference reference, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

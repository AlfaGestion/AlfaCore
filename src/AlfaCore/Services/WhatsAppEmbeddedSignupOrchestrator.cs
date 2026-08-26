using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class WhatsAppEmbeddedSignupOrchestrator(
    IWhatsAppEmbeddedSignupStore store,
    IWhatsAppEmbeddedSignupStateProtector stateProtector,
    IMetaOAuthClient metaOAuthClient,
    IWhatsAppCredentialVault credentialVault,
    IOptions<WhatsAppEmbeddedSignupOptions> options) : IWhatsAppEmbeddedSignupOrchestrator
{
    private readonly WhatsAppEmbeddedSignupOptions _options = options.Value;

    public async Task<WhatsAppEmbeddedStartResult> StartAsync(WhatsAppEmbeddedStartRequest request, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("Embedded Signup todavía no está habilitado.");
        if (request.IdBase <= 0 || string.IsNullOrWhiteSpace(request.UsuarioIniciador))
            throw new ArgumentException("La base y el usuario iniciador son obligatorios.", nameof(request));

        var now = DateTime.UtcNow;
        var (state, hash) = stateProtector.Create();
        var item = new WhatsAppEmbeddedOnboardingDto
        {
            IdOnboarding = Guid.NewGuid(),
            IdBase = request.IdBase,
            IdCliente = request.IdCliente.Trim(),
            UsuarioIniciador = request.UsuarioIniciador.Trim(),
            CorrelationId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            StateHash = hash,
            Status = WhatsAppEmbeddedOnboardingStatus.Started,
            CurrentStep = "STARTED",
            StartedAtUtc = now,
            ModifiedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.OnboardingExpirationMinutes)
        };
        await store.CreateAsync(item, ct);
        return new(item.IdOnboarding, state, item.ExpiresAtUtc);
    }

    public async Task HandleAuthorizationCallbackAsync(WhatsAppEmbeddedAuthorizationCallback callback, CancellationToken ct = default)
    {
        if (!_options.Enabled) throw new InvalidOperationException("Embedded Signup todavía no está habilitado.");
        if (callback.IdOnboarding == Guid.Empty || callback.IdBase <= 0 || string.IsNullOrWhiteSpace(callback.Usuario))
            throw new UnauthorizedAccessException("La sesión de autorización no es válida.");
        if (string.IsNullOrWhiteSpace(callback.State) || string.IsNullOrWhiteSpace(callback.AuthorizationCode))
            throw new ArgumentException("La autorización de Meta está incompleta.", nameof(callback));

        var onboarding = await store.GetAsync(callback.IdOnboarding, ct)
            ?? throw new UnauthorizedAccessException("La sesión de autorización no existe.");
        if (onboarding.IdBase != callback.IdBase || !string.Equals(onboarding.UsuarioIniciador.Trim(), callback.Usuario.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("La sesión de autorización no pertenece a esta base o usuario.");
        if (!string.Equals(onboarding.StateHash, stateProtector.Hash(callback.State), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("El state de autorización no es válido.");

        var consumed = await store.ConsumeStateAsync(onboarding.StateHash, callback.IdBase, callback.Usuario.Trim(), DateTime.UtcNow, ct);
        if (consumed is null || consumed.IdOnboarding != callback.IdOnboarding)
            throw new UnauthorizedAccessException("La autorización venció o ya fue utilizada.");

        var context = new WhatsAppVaultSecretContext(
            callback.IdBase, callback.IdOnboarding, string.Empty, callback.WabaId.Trim(), callback.PhoneNumberId.Trim(),
            "META_EMBEDDED_SIGNUP_BUSINESS_AUTHORIZATION", null);
        var token = await metaOAuthClient.ExchangeCodeAsync(callback.AuthorizationCode, context, ct);
        try
        {
            await store.MarkAuthorizedAsync(callback.IdOnboarding, token.TokenReference.Value, string.Empty, ct);
        }
        catch
        {
            await credentialVault.RemoveAsync(token.TokenReference, ct);
            throw;
        }
    }

    public async Task HandleCancellationAsync(Guid idOnboarding, int idBase, string state, string usuario, CancellationToken ct = default)
    {
        if (!_options.Enabled) throw new InvalidOperationException("Embedded Signup todavía no está habilitado.");
        var item = await store.GetAsync(idOnboarding, ct) ?? throw new UnauthorizedAccessException("La sesión de autorización no existe.");
        if (item.IdBase != idBase || !string.Equals(item.UsuarioIniciador.Trim(), usuario.Trim(), StringComparison.OrdinalIgnoreCase) || !string.Equals(item.StateHash, stateProtector.Hash(state), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("La sesión de autorización no pertenece a esta base o usuario.");
        var consumed = await store.ConsumeStateAsync(item.StateHash, idBase, usuario.Trim(), DateTime.UtcNow, ct);
        if (consumed is null) return;
        await store.UpdateStatusAsync(idOnboarding, WhatsAppEmbeddedOnboardingStatus.Started, WhatsAppEmbeddedOnboardingStatus.Cancelled, "CANCELLED", ct);
    }

    public async Task<WhatsAppEmbeddedStatusView?> GetStatusAsync(Guid idOnboarding, CancellationToken ct = default)
    {
        var item = await store.GetAsync(idOnboarding, ct);
        return item is null ? null : WhatsAppEmbeddedSignupProgressMapper.Map(item);
    }

    public Task ProcessNextStepAsync(Guid idOnboarding, CancellationToken ct = default)
        => throw new NotSupportedException("El pipeline Meta real pertenece a ES-2.");

    public Task RetryAsync(WhatsAppEmbeddedRetryRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Los reintentos del pipeline Meta pertenecen a ES-2.");
}

public static class WhatsAppEmbeddedSignupProgressMapper
{
    private static readonly (string Key, string Label, WhatsAppEmbeddedOnboardingStatus Status)[] Steps =
    [
        ("authorized", "Cuenta autorizada", WhatsAppEmbeddedOnboardingStatus.Authorized),
        ("assets", "Cuenta de WhatsApp encontrada", WhatsAppEmbeddedOnboardingStatus.DiscoveringAssets),
        ("access", "Configurando permisos", WhatsAppEmbeddedOnboardingStatus.ConfiguringAccess),
        ("webhooks", "Configurando webhooks", WhatsAppEmbeddedOnboardingStatus.SubscribingWabas),
        ("numbers", "Importando números", WhatsAppEmbeddedOnboardingStatus.Importing)
    ];

    public static WhatsAppEmbeddedStatusView Map(WhatsAppEmbeddedOnboardingDto item)
    {
        var current = Array.FindIndex(Steps, x => x.Status == item.Status);
        var terminalReady = item.Status == WhatsAppEmbeddedOnboardingStatus.Ready;
        return new WhatsAppEmbeddedStatusView
        {
            IdOnboarding = item.IdOnboarding,
            Status = item.Status,
            Title = terminalReady ? "WhatsApp conectado" : "Configurando WhatsApp",
            Message = item.Status switch
            {
                WhatsAppEmbeddedOnboardingStatus.ActionRequired => "Necesitamos que completes una acción en tu cuenta Meta.",
                WhatsAppEmbeddedOnboardingStatus.FailedRetryable => "No pudimos completar la configuración. Podrás reintentar.",
                WhatsAppEmbeddedOnboardingStatus.FailedFinal => "No se pudo completar la configuración.",
                WhatsAppEmbeddedOnboardingStatus.Authorized => "Autorización recibida correctamente. La configuración automática continuará en la próxima etapa.",
                WhatsAppEmbeddedOnboardingStatus.Cancelled => "Conexión cancelada. Podés intentarlo nuevamente.",
                _ => string.Empty
            },
            IncidentId = item.IncidentId,
            Progress = Steps.Select((step, index) => new WhatsAppEmbeddedProgressItem(
                step.Key,
                step.Label,
                terminalReady || (current >= 0 && index < current) ? WhatsAppEmbeddedProgressState.Completed
                    : index == current ? WhatsAppEmbeddedProgressState.InProgress
                    : WhatsAppEmbeddedProgressState.Pending)).ToArray()
        };
    }
}

using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Extensions.Options;

namespace AlfaCore.Services;

public sealed class WhatsAppWabaRoutingProvider(ICentralBasesService centralBases, ISessionService sessionService,
    IConversacionesConfigService configService, IOptions<WhatsAppEmbeddedSignupOptions> embeddedSignupOptions) : IWhatsAppWabaRoutingProvider
{
    private readonly WhatsAppEmbeddedSignupOptions _options = embeddedSignupOptions.Value;

    public async Task<WhatsAppWabaRoutingConfiguration> GetAsync(int idBase, CancellationToken ct = default)
    {
        var centralBase = await centralBases.GetByIdAsync(idBase, ct)
            ?? throw new InvalidOperationException("La base del onboarding no existe en la configuración central.");
        sessionService.SetWebhookOverride(new SessionDto
        {
            Id = SessionDto.BuildGuidFromBaseId(idBase), BaseId = idBase, Nombre = centralBase.Nombre,
            Servidor = centralBase.DbServer, BaseDatos = centralBase.DbName, Usuario = centralBase.DbUser,
            Password = centralBase.DbPassword, TrustServerCertificate = true, Activa = true
        });
        var config = await configService.GetWhatsAppConfigAsync(ct);

        // Base pública HTTPS: la del tenant si la configuró; si no, el CallbackBaseUrl global de
        // Embedded Signup (mismo host tenantizado para todas las bases ES). Así una base nueva NO
        // necesita configurar su URL pública para que Embedded Signup arme el callback.
        if (string.IsNullOrWhiteSpace(config.PublicBaseUrl) && !string.IsNullOrWhiteSpace(_options.CallbackBaseUrl))
            config.PublicBaseUrl = _options.CallbackBaseUrl.Trim();
        if (string.IsNullOrWhiteSpace(config.PublicBaseUrl) || !Uri.TryCreate(config.PublicBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("La Base pública HTTPS de WhatsApp no es válida (ni la del tenant ni WhatsAppEmbeddedSignup:CallbackBaseUrl).");
        if (string.IsNullOrWhiteSpace(config.VerifyToken))
            throw new InvalidOperationException("El Verify Token de WhatsApp no está configurado (ni el del tenant ni el global WhatsApp:VerifyToken).");
        var token = string.IsNullOrWhiteSpace(centralBase.WebhookToken)
            ? await centralBases.EnsureWebhookTokenAsync(idBase, ct)
            : centralBase.WebhookToken;
        return new($"{config.GetWebhookUrl().TrimEnd('/')}/{token}", config.VerifyToken.Trim());
    }
}

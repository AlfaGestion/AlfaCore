using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class WhatsAppWabaRoutingProvider(ICentralBasesService centralBases, ISessionService sessionService,
    IConversacionesConfigService configService) : IWhatsAppWabaRoutingProvider
{
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
        if (string.IsNullOrWhiteSpace(config.PublicBaseUrl) || !Uri.TryCreate(config.PublicBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("La Base pública HTTPS de WhatsApp no es válida.");
        if (string.IsNullOrWhiteSpace(config.VerifyToken))
            throw new InvalidOperationException("El Verify Token de WhatsApp no está configurado.");
        var token = string.IsNullOrWhiteSpace(centralBase.WebhookToken)
            ? await centralBases.EnsureWebhookTokenAsync(idBase, ct)
            : centralBase.WebhookToken;
        return new($"{config.GetWebhookUrl().TrimEnd('/')}/{token}", config.VerifyToken.Trim());
    }
}

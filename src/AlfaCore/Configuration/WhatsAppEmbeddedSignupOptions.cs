namespace AlfaCore.Configuration;

public sealed class WhatsAppEmbeddedSignupOptions
{
    public const string SectionName = "WhatsAppEmbeddedSignup";

    public bool Enabled { get; set; }
    public bool WorkerEnabled { get; set; }
    public bool WebhookRoutingEnabled { get; set; }

    /// <summary>
    /// Si es <c>true</c>, cualquier base autenticada de AlfaCore puede INICIAR un onboarding de
    /// Embedded Signup sin figurar en <see cref="AllowedBaseIds"/>. Default seguro <c>false</c>:
    /// borrar la lista por accidente NO habilita las 141 bases. No afecta el runtime de assets ya
    /// onboardeados (eso lo decide <see cref="Enabled"/> + ownership central).
    /// </summary>
    public bool AllowAllTenants { get; set; }

    /// <summary>
    /// Lista blanca administrativa para iniciar nuevos onboardings cuando
    /// <see cref="AllowAllTenants"/> es <c>false</c>. No participa en la resolución de credencial de
    /// un asset ya onboardeado.
    /// </summary>
    public int[] AllowedBaseIds { get; set; } = [];
    public string AppId { get; set; } = string.Empty;
    public string BusinessPortfolioId { get; set; } = string.Empty;
    public string SystemUserId { get; set; } = string.Empty;
    public string EmbeddedSignupConfigId { get; set; } = string.Empty;
    public string GraphApiVersion { get; set; } = "v26.0";
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com";
    public bool UseApplicationCentralConnection { get; set; }
    public string CentralConnectionString { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string CallbackBaseUrl { get; set; } = string.Empty;
    public string DataProtectionKeysPath { get; set; } = string.Empty;

    /// <summary>
    /// Cómo se protege en reposo el key ring de Data Protection del vault.
    /// <see cref="WhatsAppDataProtectionKeyProtection.DpapiCurrentUser"/> es el valor histórico
    /// (compatible hacia atrás): sólo lo abre la cuenta Windows que generó las keys, en esa máquina.
    /// Para un proceso IIS público hay que usar <see cref="WhatsAppDataProtectionKeyProtection.DpapiLocalMachine"/>
    /// (key ring generado en ese mismo servidor) o <see cref="WhatsAppDataProtectionKeyProtection.Certificate"/>
    /// (key ring portable entre máquinas, protegido con un X.509).
    /// </summary>
    public WhatsAppDataProtectionKeyProtection DataProtectionKeyProtection { get; set; }
        = WhatsAppDataProtectionKeyProtection.DpapiCurrentUser;

    /// <summary>
    /// Huella (thumbprint) del certificado X.509 que protege el key ring cuando
    /// <see cref="DataProtectionKeyProtection"/> es <see cref="WhatsAppDataProtectionKeyProtection.Certificate"/>.
    /// Se busca en <c>LocalMachine\My</c> y luego en <c>CurrentUser\My</c>. Nunca es un secreto.
    /// </summary>
    public string DataProtectionCertificateThumbprint { get; set; } = string.Empty;
    public int OnboardingExpirationMinutes { get; set; } = 30;
    public int WorkerIntervalSeconds { get; set; } = 15;
    public int RetryInitialDelaySeconds { get; set; } = 30;
    public int RetryMaxDelaySeconds { get; set; } = 1800;
    public int MaxRetryCount { get; set; } = 8;
    public WhatsAppEmbeddedSignupCreditMode CreditMode { get; set; } = WhatsAppEmbeddedSignupCreditMode.CustomerPaysMeta;

    /// <summary>
    /// ¿Esta base puede INICIAR un nuevo onboarding de Embedded Signup? Elegibilidad administrativa
    /// únicamente. NO decide si un asset (WABA/phone) ya existente es ES o legacy: eso es
    /// <see cref="Enabled"/> + ownership central.
    /// </summary>
    public bool CanStartEmbeddedSignup(int idBase)
        => Enabled && idBase > 0 && (AllowAllTenants || AllowedBaseIds.Contains(idBase));

    public bool HasDataProtectionKeyRingConfiguration()
        => !string.IsNullOrWhiteSpace(DataProtectionKeysPath)
            && Path.IsPathRooted(DataProtectionKeysPath)
            && (DataProtectionKeyProtection != WhatsAppDataProtectionKeyProtection.Certificate
                || !string.IsNullOrWhiteSpace(DataProtectionCertificateThumbprint));

    public bool HasWebhookRuntimeConfiguration()
        // AllowedBaseIds YA NO es requisito: Enabled=true + AllowAllTenants=false + lista vacía es
        // válido y significa "runtime ES existente operativo, nuevos onboardings deshabilitados".
        => AllowedBaseIds.All(static id => id > 0)
            && AllowedBaseIds.Distinct().Count() == AllowedBaseIds.Length
            && (UseApplicationCentralConnection ^ !string.IsNullOrWhiteSpace(CentralConnectionString))
            && !string.IsNullOrWhiteSpace(AppSecret);

    public bool HasOnboardingGraphConfiguration()
        => !string.IsNullOrWhiteSpace(AppId)
            && !string.IsNullOrWhiteSpace(BusinessPortfolioId)
            && !string.IsNullOrWhiteSpace(SystemUserId)
            && !string.IsNullOrWhiteSpace(EmbeddedSignupConfigId)
            && !string.IsNullOrWhiteSpace(GraphApiVersion)
            && Uri.TryCreate(GraphBaseUrl, UriKind.Absolute, out var graphBaseUri)
            && graphBaseUri.Scheme == Uri.UriSchemeHttps;

    public bool HasWorkerConfiguration()
        => HasOnboardingGraphConfiguration()
            && HasDataProtectionKeyRingConfiguration()
            && OnboardingExpirationMinutes > 0
            && MaxRetryCount >= 0;

    public void EnsureOnboardingGraphConfiguration()
    {
        if (!HasOnboardingGraphConfiguration())
            throw new WhatsAppEmbeddedSignupOnboardingConfigurationException();
    }

    public bool IsValidStartupConfiguration()
    {
        if (!Enabled)
            return true;

        return HasWebhookRuntimeConfiguration()
            && (!WorkerEnabled || HasWorkerConfiguration());
    }
}

public sealed class WhatsAppEmbeddedSignupOnboardingConfigurationException()
    : InvalidOperationException("Este host no tiene configurada la capacidad de onboarding de WhatsApp Embedded Signup.");

public enum WhatsAppEmbeddedSignupCreditMode
{
    CustomerPaysMeta
}

public enum WhatsAppDataProtectionKeyProtection
{
    /// <summary>DPAPI ámbito usuario actual. Valor histórico; no portable entre cuentas/máquinas.</summary>
    DpapiCurrentUser = 0,
    /// <summary>DPAPI ámbito máquina local. Cualquier cuenta del servidor abre el key ring generado ahí.</summary>
    DpapiLocalMachine = 1,
    /// <summary>Key ring protegido con un certificado X.509 (portable entre máquinas).</summary>
    Certificate = 2
}

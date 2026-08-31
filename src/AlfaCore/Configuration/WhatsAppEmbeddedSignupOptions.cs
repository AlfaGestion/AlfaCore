namespace AlfaCore.Configuration;

public sealed class WhatsAppEmbeddedSignupOptions
{
    public const string SectionName = "WhatsAppEmbeddedSignup";

    public bool Enabled { get; set; }
    public bool WorkerEnabled { get; set; }
    public int[] AllowedBaseIds { get; set; } = [];
    public string AppId { get; set; } = string.Empty;
    public string BusinessPortfolioId { get; set; } = string.Empty;
    public string SystemUserId { get; set; } = string.Empty;
    public string EmbeddedSignupConfigId { get; set; } = string.Empty;
    public string GraphApiVersion { get; set; } = "v26.0";
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com";
    public string CentralConnectionString { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string CallbackBaseUrl { get; set; } = string.Empty;
    public string DataProtectionKeysPath { get; set; } = string.Empty;
    public int OnboardingExpirationMinutes { get; set; } = 30;
    public int WorkerIntervalSeconds { get; set; } = 15;
    public int RetryInitialDelaySeconds { get; set; } = 30;
    public int RetryMaxDelaySeconds { get; set; } = 1800;
    public int MaxRetryCount { get; set; } = 8;
    public WhatsAppEmbeddedSignupCreditMode CreditMode { get; set; } = WhatsAppEmbeddedSignupCreditMode.CustomerPaysMeta;

    public bool IsAllowedForBase(int idBase)
        => Enabled && idBase > 0 && AllowedBaseIds.Contains(idBase);
}

public enum WhatsAppEmbeddedSignupCreditMode
{
    CustomerPaysMeta
}

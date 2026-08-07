namespace AlfaCore.Models;

public sealed class PublicRegistrationRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PasswordConfirmacion { get; set; } = string.Empty;
    public string Cuit { get; set; } = string.Empty;
    public string Iva { get; set; } = string.Empty;
    public string RecaptchaToken { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
}

public sealed class PublicRegistrationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string IncidentId { get; init; } = string.Empty;
    public bool VerificationEmailSent { get; init; }
}

public sealed class PublicVerificationResult
{
    public bool Success { get; init; }
    public bool AccountVerified { get; init; }
    public bool ProvisioningCompleted { get; init; }
    public string Message { get; init; } = string.Empty;
    public string IncidentId { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
}

public sealed class PublicTrialResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class PublicProvisioningRequest
{
    public string IdCliente { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string Telefono { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Cuit { get; init; } = string.Empty;
    public string Iva { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public sealed class PublicProvisioningResult
{
    public bool Success { get; init; }
    public string DatabaseName { get; init; } = string.Empty;
    public string DatabaseUser { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

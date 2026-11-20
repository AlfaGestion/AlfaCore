namespace AlfaCore.Configuration;

/// <summary>
/// Configuración de la API Python que sigue operativa durante la migración.
/// La URL se configura por servidor; nunca se obtiene desde la solicitud del usuario.
/// </summary>
public sealed class LegacyPythonApiOptions
{
    public const string SectionName = "Api:PythonLegacy";

    // La API legacy sigue siendo necesaria para compatibilidad móvil durante la transición.
    // Una instalación puede desactivarla explícitamente con Api__PythonLegacy__Enabled=false.
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://alfacentral.ddns.net/";

    public int TimeoutSeconds { get; set; } = 120;

    public bool IsConfigured => Enabled && Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

namespace AlfaCore.Configuration;

/// <summary>
/// Configuración de la API Python que sigue operativa durante la migración.
/// La URL se configura por servidor; nunca se obtiene desde la solicitud del usuario.
/// </summary>
public sealed class LegacyPythonApiOptions
{
    public const string SectionName = "Api:PythonLegacy";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;

    public bool IsConfigured => Enabled && Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

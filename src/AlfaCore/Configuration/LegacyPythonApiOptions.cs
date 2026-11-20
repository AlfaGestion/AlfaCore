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

    public string BaseUrl { get; set; } = "http://alfanetac.ddns.net/";

    // Se prueban en orden. BaseUrl se conserva para instalaciones anteriores.
    public List<string> BaseUrls { get; set; } = [];

    public int TimeoutSeconds { get; set; } = 120;

    public bool IsConfigured => Enabled && GetBaseUrls().Any(IsHttpUrl);

    public IReadOnlyList<string> GetBaseUrls()
    {
        return BaseUrls
            .Append(BaseUrl)
            .Select(value => value?.Trim().TrimEnd('/') ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

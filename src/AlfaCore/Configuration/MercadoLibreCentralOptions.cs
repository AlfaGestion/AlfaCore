namespace AlfaCore.Configuration;

/// <summary>
/// Conexión guiada de Mercado Libre con la app central de ALFA NET (2026-11-11). Global del servidor:
/// cada cliente toca "Conectar Mercado Libre" y entra con SU cuenta de vendedor; la app es de ALFA NET.
/// El Client Secret va solo en el appsettings del servidor: nunca en el repo ni en las bases de clientes.
/// </summary>
public sealed class MercadoLibreCentralOptions
{
    public const string SectionName = "MercadoLibreCentral";

    public bool Enabled { get; set; }

    /// <summary>App ID / Client ID de la app de Mercado Libre de ALFA NET.</summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Sitio por defecto (MLA = Argentina).</summary>
    public string SiteId { get; set; } = "MLA";

    /// <summary>Pantalla de autorización del país de la cuenta vendedora.</summary>
    public string AuthorizationUrl { get; set; } = "https://auth.mercadolibre.com.ar/authorization";

    public string ApiBaseUrl { get; set; } = "https://api.mercadolibre.com";

    /// <summary>Vacío = el host desde el que se abrió la pantalla (tiene que estar cargado como Redirect URI en la app).</summary>
    public string CallbackBaseUrl { get; set; } = string.Empty;

    public bool Listo
        => Enabled && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public string ApiBase => string.IsNullOrWhiteSpace(ApiBaseUrl) ? "https://api.mercadolibre.com" : ApiBaseUrl.Trim().TrimEnd('/');
}

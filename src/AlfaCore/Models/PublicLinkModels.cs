namespace AlfaCore.Models;

/// <summary>
/// Valores permitidos para ALFA_CENTRAL.dbo.ALFA_PUBLIC_LINK.Tipo.
/// </summary>
public static class PublicLinkTipos
{
    public const string Catalogo = "CATALOGO";
    public const string Carrito = "CARRITO";
}

/// <summary>
/// Fila de ALFA_CENTRAL.dbo.ALFA_PUBLIC_LINK. El Token es la única credencial que autoriza el
/// acceso público — Slug es solamente presentación y nunca se usa para resolver IdWeb/IdBase.
/// </summary>
public sealed class PublicLinkDto
{
    public int IdPublicLink { get; init; }
    public string IdWeb { get; init; } = string.Empty;
    public int IdBase { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public int IdReferencia { get; init; }
    public string Token { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public bool Activo { get; init; }
    public DateTime FechaCreacion { get; init; }
    public DateTime? FechaVencimiento { get; init; }

    /// <summary>
    /// Segmento de ruta a publicar: "{slug}-{token}" o, si no hay slug, el token solo. La
    /// resolución nunca depende del slug — ver <see cref="Services.ICentralPublicLinkService.ResolveAsync"/>,
    /// que sólo mira los últimos caracteres del segmento (el token).
    /// </summary>
    public string RouteSegment => string.IsNullOrWhiteSpace(Slug) ? Token : $"{Slug}-{Token}";
}

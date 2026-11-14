namespace AlfaCore.Models;

/// <summary>
/// Valores permitidos para ALFA_CENTRAL.dbo.ALFA_PUBLIC_LINK.Tipo.
/// </summary>
public static class PublicLinkTipos
{
    public const string Catalogo = "CATALOGO";
    public const string Carrito = "CARRITO";
    // Código corto para compatibilidad con bases centrales antiguas donde Tipo es varchar(10).
    public const string Comprobante = "CPTE";
    // QR de una mesa del modo Salón del POS (IdReferencia = POS_MESA.ID de la base del cliente).
    public const string Mesa = "MESA";

    public static readonly string[] All = [Catalogo, Carrito, Comprobante, Mesa];

    /// <summary>CHECK de ALFA_PUBLIC_LINK.Tipo con todos los tipos vigentes (idempotente).</summary>
    public const string CheckConstraintScript = """
        IF EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name = N'CK_ALFA_PUBLIC_LINK_Tipo' AND parent_object_id = OBJECT_ID(N'dbo.ALFA_PUBLIC_LINK'))
            ALTER TABLE dbo.ALFA_PUBLIC_LINK DROP CONSTRAINT CK_ALFA_PUBLIC_LINK_Tipo;
        ALTER TABLE dbo.ALFA_PUBLIC_LINK
            ADD CONSTRAINT CK_ALFA_PUBLIC_LINK_Tipo CHECK ([Tipo] IN ('CARRITO', 'CATALOGO', 'CPTE', 'MESA'));
        """;
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

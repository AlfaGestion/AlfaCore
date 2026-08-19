namespace AlfaCore.Models;

public sealed class CatalogosModalidadOptionDto
{
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

public sealed class CatalogosListaPrecioDto
{
    public string IdLista { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Grupo { get; set; } = string.Empty;
    public string TipoLista { get; set; } = string.Empty;
    public bool Vigente { get; set; }
}

public sealed class CatalogosArticuloBusquedaFiltersDto
{
    public string Texto { get; set; } = string.Empty;
    public string IdLista { get; set; } = string.Empty;
    public string Origen { get; set; } = CatalogosArticuloOrigenKeys.ListaPrecio;
    public string? IdRubro { get; set; }
    public string? IdFamilia { get; set; }
    public string? IdTipo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? IdWeb { get; set; }

    // Artículos que ya pertenecen al catálogo/carrito que se está editando: se excluyen en la
    // consulta SQL (no se traen para después ocultarlos en el cliente).
    public IReadOnlyCollection<string> ExcludedIds { get; set; } = [];
}

public sealed class CatalogosArticuloBusquedaDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string DescripcionArticulo { get; set; } = string.Empty;
    public string Presentacion { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public string Rubro { get; set; } = string.Empty;
    public string Familia { get; set; } = string.Empty;
    public string ListaPrecio { get; set; } = string.Empty;
    public string NombreListaPrecio { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public decimal? PrecioOferta { get; set; }
}

// Opción simple (código/descripción) para los combos de clasificación del picker de artículos
// (Rubro, Familia, Marca). Reutilizable: no repite código/descripción como tipos anónimos.
public sealed class CatalogosClasificacionOpcionDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

public sealed class CatalogosArticuloCatalogoDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string DescripcionArticulo { get; set; } = string.Empty;
    public string Presentacion { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public string Rubro { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public decimal? PrecioOferta { get; set; }
}

public sealed class CatalogosViewSettingsDto
{
    public string AgruparPor { get; set; } = CatalogosViewGroupKeys.None;
    public List<CatalogosViewColumnDto> Columnas { get; set; } = [];
}

public sealed class CatalogosViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
}

public static class CatalogosViewColumnKeys
{
    public const string Id = "id";
    public const string Tipo = "tipo";
    public const string Nombre = "nombre";
    public const string Vigencia = "vigencia";
    public const string Estado = "estado";
}

public static class CatalogosViewGroupKeys
{
    public const string None = "none";
    public const string Tipo = "tipo";
    public const string Estado = "estado";
}

public static class CatalogosArticuloOrigenKeys
{
    public const string ListaPrecio = "lista";
    public const string Maestro = "maestro";
}

public sealed class CatalogosCatalogoItemDto
{
    public int IdInsert { get; set; }
    public DateTime? FechaCarga { get; set; }
    public DateTime? VigenciaDesde { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public string IdLista { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Grupo { get; set; } = string.Empty;
    public bool Finalizado { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string IdArticulo { get; set; } = string.Empty;
    public string DescripcionArticulo { get; set; } = string.Empty;
    public string Presentacion { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public decimal? Precio { get; set; }
    public decimal? PrecioOferta { get; set; }
    public string Rubro { get; set; } = string.Empty;
    public DateTime? OfertaHasta { get; set; }
}

public sealed class CatalogosCatalogoResumenDto
{
    public int IdInsert { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Vigencia { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public DateTime? VigenciaDesde { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public string IdLista { get; set; } = string.Empty;
    public string Grupo { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public DateTime? FechaCarga { get; set; }
    public int CantidadArticulos { get; set; }
    public bool Finalizado { get; set; }
    public bool Predeterminado { get; set; }
}

public sealed class CatalogosCatalogoDetalleDto
{
    public int IdInsert { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string IdLista { get; set; } = string.Empty;
    public string Grupo { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public DateTime? VigenciaDesde { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public bool Finalizado { get; set; }
    public bool HabilitarCarrito { get; set; }
    public bool Predeterminado { get; set; }
    public IReadOnlyList<CatalogosCatalogoItemDto> Articulos { get; set; } = [];
}

public sealed class CatalogosCatalogoSaveRequestDto
{
    public int? IdInsert { get; set; }
    public string Tipo { get; set; } = CatalogosViewGroupKeys.Tipo;
    public string Nombre { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public string IdLista { get; set; } = string.Empty;
    public DateTime? VigenciaDesde { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public bool HabilitarCarrito { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Pc { get; set; } = string.Empty;
    public IReadOnlyList<CatalogosArticuloCatalogoDto> Articulos { get; set; } = [];
}

public sealed class CatalogosCatalogoSaveResultDto
{
    public bool Persistido { get; set; }
    public bool Simulado { get; set; }
    public int IdInsert { get; set; }
    public string UrlPublica { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}

public sealed class CatalogosCatalogoAccessUrlsDto
{
    public string UrlPublica { get; set; } = string.Empty;
}

public sealed class CatalogosPublicIdentityDto
{
    public string NombreVisible { get; set; } = string.Empty;
    public string NombreFallback { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string LogoFormato { get; set; } = CatalogosPublicLogoFormatKeys.Auto;
    public bool TieneLogoPersonalizado { get; set; }
}

public static class CatalogosPublicLogoFormatKeys
{
    public const string Auto = "auto";
    public const string Square = "square";
    public const string Horizontal = "horizontal";
}

public sealed class CatalogosPublicLogoServeDto
{
    public string RutaCompleta { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/png";
    public string NombreArchivo { get; set; } = "logo.png";
}

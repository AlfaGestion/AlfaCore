namespace AlfaCore.Models;

// MVP del maestro de Artículos (Archivos > Maestros > Artículos). Alcance y decisiones de diseño en
// C:\Users\albert\.claude\plans\fluttering-drifting-moonbeam.md ("Módulo Archivos > Maestros >
// Artículos"). Mismo shape que CuentasComercialesModels.cs (patrón Clientes), reducido a los campos
// básicos confirmados contra V_MA_ARTICULOS/V_MA_PRECIOS de una base real: sin insumos/BOM, talles/
// colores, lotes/series, múltiples clases de precio ni imágenes -- eso queda para una fase 2 sobre el
// maestro legacy completo (Ma_art.frm).

public sealed class ArticuloFilters
{
    public string Texto { get; set; } = string.Empty;
    public string RubroCodigo { get; set; } = string.Empty;
    public string MarcaCodigo { get; set; } = string.Empty;
    public bool? Activo { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class ArticuloGridItemDto
{
    public string Codigo { get; set; } = string.Empty;
    public string CodigoBarra { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string RubroCodigo { get; set; } = string.Empty;
    public string RubroDescripcion { get; set; } = string.Empty;
    public string MarcaCodigo { get; set; } = string.Empty;
    public string MarcaDescripcion { get; set; } = string.Empty;
    public string UnidadCodigo { get; set; } = string.Empty;
    public string UnidadDescripcion { get; set; } = string.Empty;
    public decimal Costo { get; set; }
    public decimal Precio { get; set; }
    public decimal TasaIva { get; set; }
    public bool Exento { get; set; }
    public bool Pesable { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>V_MA_ARTICULOS.ModificoImagen='S' -- misma bandera que ya usa CatalogoPublico.razor
    /// para invalidar el caché local de ArticuloImagenFtpService cuando la imagen cambió.</summary>
    public bool ImagenModificada { get; set; }
}

public class ArticuloSaveRequest
{
    /// <summary>Código de artículo (IDARTICULO) -- PK real de la tabla, nvarchar(25), inmutable una
    /// vez creado. Se alinea con AlfaCore.Common.CodigoPk.Format (numérico a la derecha, alfanumérico
    /// a la izquierda) al grabar, igual que el resto de los códigos-PK de texto del sistema.</summary>
    public string CodigoOriginal { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Código de barras -- campo independiente del código de artículo, siempre editable
    /// (no es la PK).</summary>
    public string CodigoBarra { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string RubroCodigo { get; set; } = string.Empty;
    public string RubroDescripcion { get; set; } = string.Empty;
    public string MarcaCodigo { get; set; } = string.Empty;
    public string MarcaDescripcion { get; set; } = string.Empty;
    public string UnidadCodigo { get; set; } = string.Empty;
    public string UnidadDescripcion { get; set; } = string.Empty;
    public string CuentaProveedor { get; set; } = string.Empty;
    public string CodigoArtProveedor { get; set; } = string.Empty;
    public bool Pesable { get; set; }
    public decimal Costo { get; set; }
    public decimal Precio { get; set; }
    public decimal Utilidad { get; set; }
    public decimal TasaIva { get; set; }
    public bool Exento { get; set; }
}

public sealed class ArticuloDetailDto : ArticuloSaveRequest
{
    public bool Activo { get; set; } = true;
    public DateTime? FechaAlta { get; set; }

    /// <summary>Razón social del proveedor (vt_proveedores.RAZON_SOCIAL) resuelta solo para mostrar en
    /// el combo de búsqueda del editor -- no se persiste, se recalcula siempre en el servidor.</summary>
    public string CuentaProveedorNombre { get; set; } = string.Empty;
}

public sealed class ArticuloLookupOptionDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

public sealed class ArticuloLookupDataDto
{
    public List<ArticuloLookupOptionDto> Rubros { get; set; } = [];
    public List<ArticuloLookupOptionDto> Marcas { get; set; } = [];
    public List<ArticuloLookupOptionDto> Unidades { get; set; } = [];

    // Cfg relevante al cálculo Precio<->Costo<->Utilidad (ver CalcularUtlPrecio en FrmArtAlta.frm).
    public decimal TasaIvaDefault { get; set; }
    public bool PrecioIncluyeIva { get; set; }
    public bool ModoRetail { get; set; }
}

public sealed class ArticuloViewSettingsDto
{
    public string AgruparPor { get; set; } = ArticuloViewGroupKeys.None;
    public string Vista { get; set; } = ArticuloViewModeKeys.Listado;
    public List<ArticuloViewColumnDto> Columnas { get; set; } = [];
}

public static class ArticuloViewModeKeys
{
    public const string Listado = "listado";
    public const string Kanban = "kanban";
}

public static class ArticuloViewGroupKeys
{
    public const string None = "none";
    public const string Rubro = "rubro";
}

public sealed class ArticuloViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
}

public static class ArticuloViewColumnKeys
{
    public const string Codigo = "codigo";
    public const string CodigoBarra = "codigo-barra";
    public const string Descripcion = "descripcion";
    public const string Rubro = "rubro";
    public const string Marca = "marca";
    public const string Unidad = "unidad";
    public const string Costo = "costo";
    public const string Precio = "precio";
    public const string TasaIva = "tasa-iva";
}

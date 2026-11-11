namespace AlfaCore.Models;

// MVP del maestro de Artículos (Archivos > Maestros > Artículos). Alcance y decisiones de diseño en
// C:\Users\albert\.claude\plans\fluttering-drifting-moonbeam.md ("Módulo Archivos > Maestros >
// Artículos"). Mismo shape que CuentasComercialesModels.cs (patrón Clientes), reducido a los campos
// básicos confirmados contra V_MA_ARTICULOS/V_MA_PRECIOS de una base real: sin insumos/BOM, talles/
// colores, lotes/series ni imágenes -- eso queda para una fase 2 sobre el
// maestro legacy completo (Ma_art.frm).

public sealed class ArticuloFilters
{
    public string Texto { get; set; } = string.Empty;
    public string RubroCodigo { get; set; } = string.Empty;
    public string MarcaCodigo { get; set; } = string.Empty;
    public bool? Activo { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    /// <summary>Clave de columna (ver ArticuloViewColumnKeys) por la que se ordena el listado al hacer
    /// clic en un encabezado -- vacío usa el orden por defecto (activos primero, por descripción).</summary>
    public string SortBy { get; set; } = string.Empty;
    public bool SortDescending { get; set; }
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
    public decimal? PrecioOferta { get; set; }
    public DateTime? OfertaHasta { get; set; }
    public decimal Precio1 { get; set; }
    public decimal Precio2 { get; set; }
    public decimal Precio3 { get; set; }
    public decimal Precio4 { get; set; }
    public decimal Precio5 { get; set; }
    public decimal Precio6 { get; set; }
    public decimal Precio7 { get; set; }
    public decimal Precio8 { get; set; }
    public decimal Utilidad { get; set; }
    public decimal TasaIva { get; set; }
    public bool Exento { get; set; }

    /// <summary>Política de precios (V_TA_POLITICAPRECIOS.CODIGO); vacía = sin política. La aplica la base al grabar.</summary>
    public string PoliticaPrecios { get; set; } = string.Empty;

    /// <summary>Solapa "Listas": listas de ventas del artículo (null = no se tocan las listas).</summary>
    public List<ArticuloListaPrecioDto>? Listas { get; set; }
    /// <summary>Alta: agregarlo a todas las listas de ventas con los precios del artículo (las que no vengan en <see cref="Listas"/>).</summary>
    public bool IncluirEnTodasLasListas { get; set; }
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
    public int ClasePrecioVenta { get; set; } = 1;
    public List<string> NombresPrecios { get; set; } = [];
    /// <summary>Políticas de precios para el combo de la solapa Precios.</summary>
    public List<PoliticaPrecioDto> Politicas { get; set; } = [];
    /// <summary>Clase de oferta por fecha de las listas (CLASEPRECIOOFERTA; 0 = sin ofertas).</summary>
    public int ClasePrecioOferta { get; set; }
    /// <summary>OFERTAPORDESCUENTO=SI: los precios por cantidad de las listas (PRECIO9/10) son % de descuento.</summary>
    public bool OfertaPorDescuento { get; set; }
}

public sealed class ArticuloViewSettingsDto
{
    public string AgruparPor { get; set; } = ArticuloViewGroupKeys.None;
    public string Vista { get; set; } = ArticuloViewModeKeys.Listado;
    public List<ArticuloViewColumnDto> Columnas { get; set; } = [];
}

/// <summary>Acciones por lote del listado de articulos.</summary>
public static class ArticuloLoteAcciones
{
    public const string Aumento = "aumento";
    public const string Rubro = "rubro";
    public const string Marca = "marca";
    public const string Familia = "familia";
    public const string Utilidad = "utilidad";
}

/// <summary>Sobre que se aplica el aumento por porcentaje.</summary>
public static class ArticuloLoteAumentoDestino
{
    public const string CostoYPrecios = "ambos";
    public const string Precios = "precios";
    public const string Costo = "costo";
}

public sealed class ArticuloLoteRequest
{
    public List<string> Codigos { get; set; } = [];
    public string Accion { get; set; } = string.Empty;
    /// <summary>Aumento: porcentaje (negativo para bajar).</summary>
    public decimal Porcentaje { get; set; }
    /// <summary>Aumento: ver ArticuloLoteAumentoDestino.</summary>
    public string AplicarA { get; set; } = ArticuloLoteAumentoDestino.CostoYPrecios;
    /// <summary>Aumento/utilidad: redondear los precios a este paso (0 = sin redondeo, 2 decimales).</summary>
    public decimal Redondeo { get; set; }
    /// <summary>Rubro, marca o familia a asignar.</summary>
    public string Codigo { get; set; } = string.Empty;
    /// <summary>Utilidad % a asignar.</summary>
    public decimal Utilidad { get; set; }
}

public sealed class ArticuloLoteResultado
{
    public int Actualizados { get; set; }
    /// <summary>Articulos que no se pudieron cambiar del todo (ej. utilidad sin costo), con el motivo.</summary>
    public List<string> Observaciones { get; set; } = [];
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

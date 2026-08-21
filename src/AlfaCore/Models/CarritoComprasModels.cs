namespace AlfaCore.Models;

public static class CarritoComprasTipoKeys
{
    public const string Todos = "todos";
    public const string Catalogo = "catalogo";
    public const string General = "general";
}

public static class CarritoComprasEstadoKeys
{
    public const string Todos = "todos";
    public const string Activo = "activo";
    public const string Inactivo = "inactivo";
}

public static class CarritoComprasArticuloOrigenKeys
{
    public const string Maestro = "maestro";
    public const string Lista = "lista";
}

public static class CarritoComprasPrecioOrigenKeys
{
    public const string SegunCliente = "segun-cliente";
    public const string ListaFija = "lista-fija";
    public const string Maestro = "maestro";
}

public sealed class CarritoComprasFiltroDto
{
    public string? Texto { get; set; }
    public string Tipo { get; set; } = CarritoComprasTipoKeys.Todos;
    public string Estado { get; set; } = CarritoComprasEstadoKeys.Todos;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class CarritoComprasResumenDto
{
    public string Id { get; set; } = string.Empty;
    public int? IdCatalogo { get; set; }
    public int? IdCarrito { get; set; }
    public string TipoClave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Vigencia { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public int CantidadArticulos { get; set; }
    public string Precios { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public string UrlCarrito { get; set; } = string.Empty;
    public string UrlEdicion { get; set; } = string.Empty;
    public int OrdenTipo { get; set; }
}

public sealed class CarritoComprasGeneralArticuloDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string DescripcionArticulo { get; set; } = string.Empty;
    public string CodigoBarra { get; set; } = string.Empty;
    public string RutaImagen { get; set; } = string.Empty;
    public string Presentacion { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public string Rubro { get; set; } = string.Empty;
}

public sealed class CarritoComprasGeneralDetalleDto
{
    public int IdCarrito { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public string OrigenArticulos { get; set; } = CarritoComprasArticuloOrigenKeys.Maestro;
    public string? IdListaArticulos { get; set; }
    public string OrigenPrecios { get; set; } = CarritoComprasPrecioOrigenKeys.SegunCliente;
    public string? IdListaPrecios { get; set; }
    public int? ClasePrecio { get; set; }
    public IReadOnlyList<CarritoComprasGeneralArticuloDto> Articulos { get; set; } = [];
}

public sealed class CarritoComprasGeneralSaveRequestDto
{
    public int? IdCarrito { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public string OrigenArticulos { get; set; } = CarritoComprasArticuloOrigenKeys.Maestro;
    public string? IdListaArticulos { get; set; }
    public string OrigenPrecios { get; set; } = CarritoComprasPrecioOrigenKeys.SegunCliente;
    public string? IdListaPrecios { get; set; }
    public int? ClasePrecio { get; set; }
    public IReadOnlyList<CarritoComprasGeneralArticuloDto> Articulos { get; set; } = [];
    public string Usuario { get; set; } = string.Empty;
    public string Pc { get; set; } = string.Empty;
}

public sealed class CarritoComprasPrecioClienteDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public string NombreCliente { get; set; } = string.Empty;
    public string OrigenLista { get; set; } = string.Empty;
    public string OrigenClase { get; set; } = string.Empty;
    public string IdLista { get; set; } = string.Empty;
    public int ClasePrecio { get; set; } = 1;
    public string Origen { get; set; } = string.Empty;
}

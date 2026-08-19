namespace AlfaCore.Models;

public static class ListaPreciosAgruparPorKeys
{
    public const string Ninguno = "";
    public const string Familia = "familia";
    public const string Marca = "marca";
}

public sealed class ListaPreciosResolucionDto
{
    public string? IdLista { get; set; }
    public string? NombreLista { get; set; }
    public bool ListaDesdeCliente { get; set; }
    public bool UsaMaestro { get; set; }
    public int Clase { get; set; }
    public bool ClaseDesdeCliente { get; set; }
}

public sealed class ListaPreciosBusquedaFiltroDto
{
    public string CodigoCliente { get; set; } = string.Empty;
    public string? Texto { get; set; }
    public string AgruparPor { get; set; } = ListaPreciosAgruparPorKeys.Ninguno;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class ListaPreciosArticuloDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Presentacion { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public string Familia { get; set; } = string.Empty;
    public string Rubro { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public bool SinPrecio => Precio <= 0m;
}

public sealed class ListaPreciosResultadoDto
{
    public ListaPreciosResolucionDto Resolucion { get; set; } = new();
    public PagedResult<ListaPreciosArticuloDto> Pagina { get; set; } = new();
}

public sealed class ListaPreciosExportDto
{
    public ListaPreciosResolucionDto Resolucion { get; set; } = new();
    public IReadOnlyList<ListaPreciosArticuloDto> Articulos { get; set; } = [];
    public bool Truncado { get; set; }
}

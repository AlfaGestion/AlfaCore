namespace AlfaCore.Models;

// Listas de precios de ventas (V_MA_PRECIOSCAB + V_MA_PRECIOS), como el legacy V_MA_PRECIOS.exe @PARAMETROADICIONAL=V.
// La lista guarda una fila por artículo; ConIVA, ModificaPrecios y el redondeo (TA_CONFIGURACION DecimalesListaV{IdLista})
// son de la lista aunque el legacy los repita en cada fila. Los triggers de V_MA_PRECIOS registran el histórico y redondean.

/// <summary>Encabezado de una lista de ventas con sus datos de uso.</summary>
public sealed class ListaPrecioDto
{
    public string IdLista { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public DateTime? VigenciaDesde { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    /// <summary>Los precios de la lista incluyen IVA (V_MA_PRECIOS.ConIVA de sus filas).</summary>
    public bool ConIva { get; set; }
    /// <summary>Hay filas con y sin IVA: la lista quedó mezclada (se puede unificar al guardar).</summary>
    public bool ConIvaMezclado { get; set; }
    /// <summary>Decimales de redondeo (TA_CONFIGURACION DecimalesListaV{IdLista}; 2 por defecto).</summary>
    public int Decimales { get; set; } = 2;
    /// <summary>Clases que la lista copia del maestro de artículos (ModificaPrecios: 0=costo, 1..8=clases).</summary>
    public string ClasesDelMaestro { get; set; } = string.Empty;
    public int Articulos { get; set; }
    public int Clientes { get; set; }
}

public sealed class ListaPrecioGuardarRequest
{
    /// <summary>Vacío = alta (se propone el próximo código).</summary>
    public string IdListaOriginal { get; set; } = string.Empty;
    public string IdLista { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public DateTime? VigenciaDesde { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public bool ConIva { get; set; }
    public int Decimales { get; set; } = 2;
    /// <summary>Alta: copiar los artículos y precios de otra lista.</summary>
    public string? CopiarDeLista { get; set; }
}

public sealed class ListaPrecioFiltro
{
    public string IdLista { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public string RubroCodigo { get; set; } = string.Empty;
    public string MarcaCodigo { get; set; } = string.Empty;
    public string ProveedorCodigo { get; set; } = string.Empty;
    public string FamiliaCodigo { get; set; } = string.Empty;
    public bool SoloOfertas { get; set; }
    public bool IncluirSuspendidos { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

/// <summary>Fila de la lista: precios de la lista más datos del artículo para mostrar.</summary>
public sealed class ListaPrecioFilaDto
{
    public int Id { get; set; }
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string CodigoBarra { get; set; } = string.Empty;
    public string Rubro { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public decimal TasaIva { get; set; }
    public bool Exento { get; set; }
    public bool Suspendido { get; set; }
    public bool ConIva { get; set; }
    /// <summary>Costo del maestro (el de la fila puede estar desactualizado).</summary>
    public decimal Costo { get; set; }
    public decimal Precio1 { get; set; }
    public decimal Precio2 { get; set; }
    public decimal Precio3 { get; set; }
    public decimal Precio4 { get; set; }
    public decimal Precio5 { get; set; }
    public decimal Precio6 { get; set; }
    public decimal Precio7 { get; set; }
    public decimal Precio8 { get; set; }
    public decimal? Precio9 { get; set; }
    public decimal? Precio10 { get; set; }
    public decimal? CantidadOf2 { get; set; }
    public decimal? CantidadOf3 { get; set; }
    public DateTime? OfertaDesde { get; set; }
    public DateTime? OfertaHasta { get; set; }
    public string ClasesDelMaestro { get; set; } = string.Empty;

    public decimal Precio(int clase) => clase switch
    {
        1 => Precio1, 2 => Precio2, 3 => Precio3, 4 => Precio4, 5 => Precio5, 6 => Precio6, 7 => Precio7, 8 => Precio8,
        9 => Precio9 ?? 0, 10 => Precio10 ?? 0, _ => 0
    };

    public void SetPrecio(int clase, decimal valor)
    {
        switch (clase)
        {
            case 1: Precio1 = valor; break; case 2: Precio2 = valor; break; case 3: Precio3 = valor; break;
            case 4: Precio4 = valor; break; case 5: Precio5 = valor; break; case 6: Precio6 = valor; break;
            case 7: Precio7 = valor; break; case 8: Precio8 = valor; break;
            case 9: Precio9 = valor; break; case 10: Precio10 = valor; break;
        }
    }
}

public sealed class ListaPrecioPaginaDto
{
    public ListaPrecioDto Lista { get; set; } = new();
    public List<ListaPrecioFilaDto> Filas { get; set; } = [];
    public int Total { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

/// <summary>Datos de configuración para editar listas: nombres de clases, clase de venta y oferta, combos.</summary>
public sealed class ListaPrecioContextoDto
{
    public List<string> NombresClases { get; set; } = [];
    public int ClaseVenta { get; set; } = 4;
    public int ClaseOferta { get; set; }
    public bool Retail { get; set; }
    public bool OfertaPorDescuento { get; set; }
    public bool OfertaPrecioUnitario { get; set; }
    public decimal TasaIvaDefault { get; set; } = 21;
    public bool PuedeEditar { get; set; }
    public List<ArticuloLookupOptionDto> Rubros { get; set; } = [];
    public List<ArticuloLookupOptionDto> Marcas { get; set; } = [];
    public List<ArticuloLookupOptionDto> Familias { get; set; } = [];
    public List<ListaPrecioDto> Listas { get; set; } = [];
}

public static class ListaPrecioAgregarModo
{
    public const string Todos = "todos";
    public const string Rubro = "rubro";
    public const string Marca = "marca";
    public const string Proveedor = "proveedor";
    public const string Familia = "familia";
    public const string Articulos = "articulos";
    public const string OtraLista = "lista";
}

/// <summary>Agregar artículos en bloque (como "Insertar" del legacy). Los que ya están se saltean.</summary>
public sealed class ListaPrecioAgregarRequest
{
    public string IdLista { get; set; } = string.Empty;
    public string Modo { get; set; } = ListaPrecioAgregarModo.Rubro;
    /// <summary>Código de rubro, marca, proveedor, familia, lista o códigos de artículo separados por coma.</summary>
    public string Valor { get; set; } = string.Empty;
    /// <summary>OtraLista: copia los precios de esa lista; si no, los del maestro de artículos.</summary>
    public bool IncluirSuspendidos { get; set; }
}

/// <summary>Aumento por porcentaje (V_MA_AUMENTOGLOBAL): destino = origen × (1 + %/100), sobre lo filtrado.</summary>
public sealed class ListaPrecioAumentoRequest
{
    public ListaPrecioFiltro Filtro { get; set; } = new();
    /// <summary>0 = costo del maestro; 1..8 = clase de la lista.</summary>
    public int Origen { get; set; } = 4;
    /// <summary>Clases destino 1..8.</summary>
    public List<int> Destinos { get; set; } = [4];
    public decimal Porcentaje { get; set; }
    /// <summary>Redondear al múltiplo próximo (0 = solo a los decimales de la lista).</summary>
    public decimal RedondearA { get; set; }
}

/// <summary>Redondeo (pestaña "Redondeo" de V_MA_AUMENTOGLOBAL): sube al múltiplo próximo.</summary>
public sealed class ListaPrecioRedondeoRequest
{
    public ListaPrecioFiltro Filtro { get; set; } = new();
    public List<int> Clases { get; set; } = [4];
    public decimal Multiplo { get; set; } = 1;
}

public sealed class ListaPrecioOperacionResultado
{
    public int Filas { get; set; }
    public int Salteadas { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}

/// <summary>Una lista en la solapa "Listas" del maestro de artículos.</summary>
public sealed class ArticuloListaPrecioDto
{
    public string IdLista { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    /// <summary>El artículo está (o va a estar) en la lista.</summary>
    public bool Incluido { get; set; }
    /// <summary>Estaba en la lista al abrir el artículo.</summary>
    public bool Existia { get; set; }
    public bool ConIva { get; set; }
    public string ClasesDelMaestro { get; set; } = string.Empty;
    public decimal Precio1 { get; set; }
    public decimal Precio2 { get; set; }
    public decimal Precio3 { get; set; }
    public decimal Precio4 { get; set; }
    public decimal Precio5 { get; set; }
    public decimal Precio6 { get; set; }
    public decimal Precio7 { get; set; }
    public decimal Precio8 { get; set; }
    public decimal? Precio9 { get; set; }
    public decimal? Precio10 { get; set; }
    public decimal? CantidadOf2 { get; set; }
    public decimal? CantidadOf3 { get; set; }
    public DateTime? OfertaDesde { get; set; }
    public DateTime? OfertaHasta { get; set; }

    public decimal Precio(int clase) => clase switch
    {
        1 => Precio1, 2 => Precio2, 3 => Precio3, 4 => Precio4, 5 => Precio5, 6 => Precio6, 7 => Precio7, 8 => Precio8,
        9 => Precio9 ?? 0, 10 => Precio10 ?? 0, _ => 0
    };

    public void SetPrecio(int clase, decimal valor)
    {
        switch (clase)
        {
            case 1: Precio1 = valor; break; case 2: Precio2 = valor; break; case 3: Precio3 = valor; break;
            case 4: Precio4 = valor; break; case 5: Precio5 = valor; break; case 6: Precio6 = valor; break;
            case 7: Precio7 = valor; break; case 8: Precio8 = valor; break;
            case 9: Precio9 = valor; break; case 10: Precio10 = valor; break;
        }
    }
}

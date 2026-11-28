namespace AlfaCore.Models;

// Borrador de precios (D010508 «Borrador de Cambios de Precios» del legacy: EM_PLANILLAEDITDATOS @PARAMETROADICIONAL=BORRADOR
// + FrmBorradorPrecios.frm). Tabla compartida con Alfa Gestión: V_MA_PRECIOS_BORRADOR.
//   TipoLista B = maestro (V_MA_ARTICULOS), V = una lista de venta (V_MA_PRECIOS), G = grupo de listas (V_MA_PRECIOSCAB.GRUPO = Nombre),
//   M = alta de artículo (la escribe un trigger, no se aplica: no se muestra).

public sealed class PrecioBorradorFiltro
{
    public string Texto { get; set; } = string.Empty;
    /// <summary>Usuario que generó el cambio; vacío = todos.</summary>
    public string Usuario { get; set; } = string.Empty;
    /// <summary>B (maestro), V (listas) o vacío.</summary>
    public string Tipo { get; set; } = string.Empty;
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 100;
}

public sealed class PrecioBorradorPagina
{
    public List<PrecioBorradorFilaDto> Filas { get; set; } = [];
    public int Total { get; set; }
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 100;
    /// <summary>Ids de todas las filas del filtro (para "aplicar todo lo filtrado").</summary>
    public List<int> IdsFiltrados { get; set; } = [];
    public List<string> Usuarios { get; set; } = [];
    public int ClasePrecioVenta { get; set; } = 1;
    public string NombreClaseVenta { get; set; } = "Precio de venta";
    /// <summary>La base tiene la tabla V_MA_PRECIOS_BORRADOR.</summary>
    public bool Instalado { get; set; }
    /// <summary>Cfg PreciosArticulosPorBorrador=SI.</summary>
    public bool BorradorActivo { get; set; }
}

public sealed class PrecioBorradorFilaDto
{
    public int Id { get; set; }
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string IdLista { get; set; } = string.Empty;
    public string Lista { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    /// <summary>El destino ya no existe (el artículo salió de la lista o se borró): no se puede aplicar.</summary>
    public bool SinDestino { get; set; }
    public decimal CostoActual { get; set; }
    public decimal CostoNuevo { get; set; }
    public decimal PrecioActual { get; set; }
    public decimal PrecioNuevo { get; set; }
    /// <summary>Otras clases de precio que cambian ("P1 100 → 110").</summary>
    public List<string> OtrosCambios { get; set; } = [];

    public decimal? Variacion => PrecioActual == 0 ? null : Math.Round((PrecioNuevo - PrecioActual) / PrecioActual * 100m, 1);
}

public sealed class PrecioBorradorResultado
{
    public int Aplicadas { get; set; }
    public int Descartadas { get; set; }
    /// <summary>Productos recalculados en cascada porque usan como insumo un precio aplicado (fórmulas con ClasePr).</summary>
    public int ProductosRecalculados { get; set; }
    public List<string> Observaciones { get; set; } = [];
}

/// <summary>Reglas del borrador sin base de datos (para testear).</summary>
public static class PrecioBorradorCalculo
{
    /// <summary>
    /// Valor que se aplica: el del borrador si tiene uno; si está vacío o en 0 se conserva el actual. El legacy creaba filas
    /// con solo PRECIO4 y al aplicarlas dejaba en 0 los demás precios de la lista.
    /// </summary>
    public static decimal ValorAplicado(decimal? borrador, decimal actual)
        => borrador is { } b && b != 0 ? b : actual;
}

namespace AlfaCore.Models;

public abstract class GestionDateFilterBase
{
    protected GestionDateFilterBase()
    {
        var today = DateTime.Today;
        FechaDesde = new DateTime(today.Year, today.Month, 1);
        FechaHasta = today;
    }

    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
}

public sealed class VentasDashboardFilters : GestionDateFilterBase
{
    public string? Cliente { get; set; }
    public string? Usuario { get; set; }
    public List<string> UnidadesNegocio { get; set; } = [];
    public string? Deposito { get; set; }
    public string? TipoComprobante { get; set; }
    public bool ComparativoUnidadNegocio { get; set; }

    /// <summary>Agrupación para el tablero de ventas: "junto" (consolidado) o "separado" (una fila por unidad de negocio).</summary>
    public string Agrupacion { get; set; } = VentasAgrupacionKeys.Junto;
}

public static class VentasAgrupacionKeys
{
    public const string Junto = "junto";
    public const string Separado = "separado";
}

public sealed class UnidadNegocioOptionDto
{
    public string Codigo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
}

public sealed class VentasFilterOptionsDto
{
    public IReadOnlyList<string> Usuarios { get; init; } = [];
    public IReadOnlyList<UnidadNegocioOptionDto> UnidadesNegocio { get; init; } = [];
    public IReadOnlyList<string> Depositos { get; init; } = [];
    public IReadOnlyList<string> TiposComprobante { get; init; } = [];
}

public sealed class StockDashboardFilters : GestionDateFilterBase
{
    public string? ArticuloCodigo { get; set; }
    public string? ArticuloDescripcion { get; set; }
    public string? Rubro { get; set; }
    public string? Familia { get; set; }
    public string? Deposito { get; set; }
    public string? Sucursal { get; set; }
    public string? Estado { get; set; }
}

public sealed class StockFilterOptionsDto
{
    public IReadOnlyList<string> Rubros { get; init; } = [];
    public IReadOnlyList<string> Familias { get; init; } = [];
    public IReadOnlyList<string> Depositos { get; init; } = [];
    public IReadOnlyList<string> Sucursales { get; init; } = [];
    public IReadOnlyList<string> Estados { get; init; } = [];
}

public sealed class CajaBancosDashboardFilters : GestionDateFilterBase
{
    public string? Caja { get; set; }
    public string? BancoCuenta { get; set; }
    public string? Texto { get; set; }
}

public sealed class CajaBancosFilterOptionsDto
{
    public IReadOnlyList<string> Cajas { get; init; } = [];
    public IReadOnlyList<string> Bancos { get; init; } = [];
}

public sealed class ContabilidadDashboardFilters : GestionDateFilterBase
{
    public string? CuentaContable { get; set; }
    public string? Detalle { get; set; }
    public string? Usuario { get; set; }
    public string? Sucursal { get; set; }
    public string? Tipo { get; set; }
}

public sealed class ContabilidadFilterOptionsDto
{
    public IReadOnlyList<string> Usuarios { get; init; } = [];
    public IReadOnlyList<string> Sucursales { get; init; } = [];
    public IReadOnlyList<string> Tipos { get; init; } = [];
}

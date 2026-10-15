namespace AlfaCore.Models;

/// <summary>Borrador de factura nueva: nunca se persiste como pedido o remito auxiliar.</summary>
public sealed class CargaComprobantesDirecta
{
    public Guid Borrador { get; set; } = Guid.NewGuid();
    public string Tc { get; set; } = "FC";
    public int? FacturaAsociadaId { get; set; }
    public string Cliente { get; set; } = "";
    public string UnidadNegocio { get; set; } = "";
    public string Deposito { get; set; } = "";
    public string Observaciones { get; set; } = "";
    public List<CargaComprobantesDirectaLinea> Lineas { get; set; } = [];
}

public sealed class CargaComprobantesDirectaLinea
{
    public string Articulo { get; set; } = "";
    public decimal Cantidad { get; set; } = 1;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PrecioUnitarioNeto { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Descuento { get; set; }
}

public sealed record CargaComprobantesPoliticaPrecio(bool PuedeEditarPrecio, bool PuedeEditarDescuento,
    decimal MaximoDescuento, decimal? MaximoDescuentoLinea = null);

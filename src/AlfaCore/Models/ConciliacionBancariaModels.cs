using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.Models;

public sealed record ConciliacionArchivo(string Nombre, byte[] Contenido,DateTime? FechaReferencia=null);
public sealed record ConciliacionLectura(BankSource? Banco, IReadOnlyCollection<NormalizedMovementDto> Movimientos, string Mensaje)
{
    public bool RequiereFechaReferencia { get; init; }
    public DateTime? FechaReferenciaSugerida { get; init; }
}
public sealed record ConciliacionResultado(long ImportacionId, int Movimientos, int Duplicados, decimal Total);
public sealed class ConciliacionMovimientoFiltros
{
    public long? ImportacionId { get; init; }
    public string Clasificacion { get; init; } = "";
    public string Buscar { get; init; } = "";
    public string Cliente { get; init; } = "";
    public string Registracion { get; init; } = "";
    public string Conciliacion { get; init; } = "";
    public string OrdenPor { get; init; } = "fecha";
    public bool Descendente { get; init; } = true;
    public int Pagina { get; init; } = 1;
}
public sealed class ConciliacionCuenta
{
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Moneda { get; set; } = "";
}
public sealed class ConciliacionImportacion
{
    public long Id { get; set; }
    public string Banco { get; set; } = "";
    public string Archivo { get; set; } = "";
    public bool TieneOriginal { get; set; }
    public string Cuenta { get; set; } = "";
    public DateTime FechaUtc { get; set; }
    public string Usuario { get; set; } = "";
    public int Movimientos { get; set; }
    public decimal Total { get; set; }
    public string Estado { get; set; } = "";
}
public sealed class ConciliacionMovimiento
{
    public string? ReciboPosible { get; set; }
    public DateTime? FechaPosibleCobro { get; set; }
    public decimal? ImportePosibleCobro { get; set; }
    public string? AvisoPosibleDuplicado => ReciboPosible is not null && FechaPosibleCobro.HasValue && ImportePosibleCobro.HasValue
        ? $"Posible duplicado: {ReciboPosible} del {FechaPosibleCobro:dd/MM/yyyy}, $ {ImportePosibleCobro.Value.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("es-AR"))}. Diferencia con el banco: $ {(ImportePosibleCobro.Value-Importe).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("es-AR"))}. Revisá el recibo en Cuenta corriente antes de cobrar."
        : null;
    public string TipoMatch { get; set; }="";
    public long? ConciliacionId { get; set; }
    public long Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Banco { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Referencia { get; set; } = "";
    public decimal Importe { get; set; }
    public decimal? Saldo { get; set; }
    public string Categoria { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Propuesta { get; set; } = "";
    public decimal Confianza { get; set; }
    public string Observaciones { get; set; } = "";
    public string Estado { get; set; } = "";
}

public sealed class ConciliacionComprobanteInfo
{
    public string Moneda { get; set; }="";
    public int Id { get; set; }
    public string Tc { get; set; }="";
    public string Comprobante { get; set; }="";
    public string Nombre { get; set; }="";
    public string Cuit { get; set; }="";
    public string Unidad { get; set; }="";
    public DateTime? Fecha { get; set; }
    public DateTime? Vencimiento { get; set; }
    public decimal Importe { get; set; }
    public decimal? SaldoActual { get; set; }
    public string Comentarios { get; set; }="";
    public string Observaciones { get; set; }="";
    public string Conceptos { get; set; }="";
    public bool Propuesto { get; set; }
    public bool Anulada { get; set; }
    public string Clave => $"{Tc}|{Comprobante}";
    public string Display => $"{Tc} {Comprobante[..4]}-{Comprobante.Substring(4,8)} {Comprobante[^1]}";
}
public sealed class ConciliacionOrigenInfo
{
    public int FacturaId { get; set; }
    public string Tc { get; set; }="";
    public string Comprobante { get; set; }="";
    public DateTime? Fecha { get; set; }
    public string Comentarios { get; set; }="";
}
public sealed record ConciliacionContextoComprobantes(IReadOnlyList<ConciliacionComprobanteInfo> Comprobantes,
    IReadOnlyList<ConciliacionOrigenInfo> Origenes);

namespace AlfaCore.Models;

public sealed class CobranzaDocumento
{
    public int Id { get; set; }
    public string Cliente { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Unidad { get; set; } = "";
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public string Moneda { get; set; } = "";
    public decimal Saldo { get; set; }
    public string DetallePredeterminado => $"Cob. {Nombre.Trim()}";
    public string Display => $"{Tc} {Comprobante[..4]}-{Comprobante.Substring(4,8)} {Comprobante[^1]} · Saldo {Saldo.ToString("N2",System.Globalization.CultureInfo.GetCultureInfo("es-AR"))}";
}

public sealed class CobranzaMedio
{
    public string Cuenta { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Moneda { get; set; } = "";
}

public sealed class CobranzaTalonario
{
    public int Talonario { get; set; }
    public string Unidad { get; set; } = "";
    public int Sucursal { get; set; }
    public int Proximo { get; set; }
    public int Desde { get; set; }
    public int Hasta { get; set; }
    public DateTime? Vence { get; set; }
    public string Display => $"CB X · Sucursal {Sucursal:D4} · Talonario {Talonario}";
}

public sealed class CobranzaPreparacion
{
    public long? MovimientoId { get; set; }
    public DateTime Fecha { get; set; }
    public string CuentaBanco { get; set; } = "";
    public decimal ImporteBanco { get; set; }
    public IReadOnlyList<CobranzaDocumento> Documentos { get; set; } = [];
    public IReadOnlyList<CobranzaMedio> Medios { get; set; } = [];
    public IReadOnlyList<CobranzaTalonario> Talonarios { get; set; } = [];
    public IReadOnlyList<CobranzaRetencionSugerida> RetencionesSugeridas { get; set; } = [];
}

public sealed class CobranzaRetencionSugerida
{
    public string Cliente { get; set; } = "";
    public string Unidad { get; set; } = "";
    public string Cuenta { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public DateTime Fecha { get; set; }
}

public sealed record CobranzaPago(string Cuenta, decimal Importe, string Certificado = "");
public sealed record CobranzaSolicitud(Guid Operacion, int FacturaId, DateTime Fecha, int Talonario,
    IReadOnlyList<CobranzaPago> Pagos, long? MovimientoId = null, string? Detalle = null);
public sealed record CobranzaResultado(Guid Operacion, int CobranzaId, string Comprobante, int NumeroAsiento, decimal Importe);

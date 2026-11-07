namespace AlfaCore.Models;

/// <summary>Qué lleva el paquete mensual para el estudio contable (el «Datos para Contadores» D9832 del legacy).</summary>
public sealed class EstudioPaqueteSolicitud
{
    public int Anio { get; set; } = DateTime.Today.AddMonths(-1).Year;
    public int Mes { get; set; } = DateTime.Today.AddMonths(-1).Month;
    /// <summary>Vacío = todas las unidades de negocio.</summary>
    public string Unidad { get; set; } = string.Empty;
    public bool LibrosIva { get; set; } = true;
    public bool LibroIvaDigital { get; set; } = true;
    public bool Asientos { get; set; } = true;
    public bool Detalles { get; set; } = true;
    public bool FacturasVenta { get; set; } = true;
    /// <summary>true = solo A y M (las que el estudio suele pedir); false = todas.</summary>
    public bool FacturasVentaSoloA { get; set; } = true;
    public bool ComprobantesCompra { get; set; } = true;
}

public sealed record EstudioPaqueteOpciones(IReadOnlyList<CompraCargaOpcion> Unidades, int VentasA, int VentasTotal, int ComprasConArchivo);

public sealed record EstudioPaqueteAvance(string Paso, int Hechos, int Total);

/// <summary>Zip generado en el servidor: se baja por /api/estudio/paquete/{Token} (vence en unas horas).</summary>
public sealed record EstudioPaqueteResultado(string Token, string Nombre, long Bytes, int Archivos, IReadOnlyList<string> Avisos)
{
    public EstudioPaqueteRevision? Revision { get; init; }
}

public sealed class EstudioPaqueteRevision
{
    public DateTime Fecha { get; set; } = DateTime.Now;
    public IReadOnlyList<EstudioPaqueteControl> Controles { get; set; } = [];
    public IReadOnlyList<EstudioPaqueteObservacion> Observaciones { get; set; } = [];
}

public sealed record EstudioPaqueteControl(bool Compras, string Cuit, string Estado, string Mensaje, string Archivo = "", DateTime? FechaArchivo = null);

public sealed record EstudioPaqueteObservacion(string Libro, string Tipo, string Unidad, DateTime? Fecha, string Tc, string Letra,
    int PuntoVenta, long Numero, string Cuenta, string Cuit, string Nombre, string Detalle, decimal? TotalSistema = null, decimal? TotalArca = null);

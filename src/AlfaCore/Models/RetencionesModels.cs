namespace AlfaCore.Models;

public sealed class RetencionesFiltro
{
    public DateTime Desde { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime Hasta { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);
    /// <summary>SUFRIDAS, PRACTICADAS o TODAS.</summary>
    public string Alcance { get; set; } = "SUFRIDAS";
    public string Unidad { get; set; } = string.Empty;
}

/// <summary>Una línea de retención o percepción del asiento (cuenta con medio de pago RI, RB, RG o RO).</summary>
public sealed class RetencionFila
{
    public DateTime Fecha { get; set; }
    public DateTime FechaContable { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    /// <summary>Cobranza, Compra, Pago, Venta o Asiento.</summary>
    public string Origen { get; set; } = string.Empty;
    /// <summary>IVA, IIBB, Ganancias u Otros.</summary>
    public string Impuesto { get; set; } = string.Empty;
    /// <summary>Retención o Percepción.</summary>
    public string Clase { get; set; } = string.Empty;
    public bool Sufrida { get; set; }
    public string Cuenta { get; set; } = string.Empty;
    public string CuentaNombre { get; set; } = string.Empty;
    public string Contraparte { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Cuit { get; set; } = string.Empty;
    public string CondicionIva { get; set; } = string.Empty;
    /// <summary>Positivo = retenido/percibido; negativo = devuelto (NC).</summary>
    public decimal Importe { get; set; }
    public decimal? Base { get; set; }
    public string Certificado { get; set; } = string.Empty;
    /// <summary>Código SIFERE (901 CABA … 924 Tucumán); vacío si la cuenta no tiene provincia.</summary>
    public string Jurisdiccion { get; set; } = string.Empty;
    public string JurisdiccionNombre { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public string Comprobante => $"{Tc} {Sucursal}-{Numero} {Letra}".Trim();
}

public sealed record RetencionesTotal(string Impuesto, string Clase, bool Sufrida, int Cantidad, decimal Importe);

public sealed class RetencionesResultado
{
    public RetencionesFiltro Filtro { get; set; } = new();
    public IReadOnlyList<RetencionFila> Filas { get; set; } = [];
    public IReadOnlyList<RetencionesTotal> Totales { get; set; } = [];
    public IReadOnlyList<string> Avisos { get; set; } = [];
}

public sealed class RetencionesConfig
{
    /// <summary>Jurisdicción SIFERE para retenciones/percepciones de IIBB cuya cuenta no tiene provincia.</summary>
    public string JurisdiccionDefecto { get; set; } = string.Empty;
    /// <summary>Régimen F.2002 (Mis Aplicaciones Web) de retenciones de IVA sufridas.</summary>
    public string RegimenRetencionIva { get; set; } = string.Empty;
    /// <summary>Régimen F.2002 de percepciones de IVA sufridas.</summary>
    public string RegimenPercepcionIva { get; set; } = string.Empty;
    public IReadOnlyList<(string Codigo, string Nombre)> Jurisdicciones { get; set; } = [];
    public IReadOnlyList<CompraCargaOpcion> Unidades { get; set; } = [];
}

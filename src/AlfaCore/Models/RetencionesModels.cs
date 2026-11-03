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
    /// <summary>ARBA: presentación mensual (lote 0) o por quincena (lote 1 o 2), y código de actividad del nombre del archivo.</summary>
    public bool ArbaRetencionesMensual { get; set; } = true;
    public bool ArbaPercepcionesMensual { get; set; } = true;
    public string ArbaRetencionesActividad { get; set; } = string.Empty;
    public string ArbaPercepcionesActividad { get; set; } = string.Empty;
    /// <summary>SIRCAR: tipo de régimen (3 dígitos) y jurisdicción del agente (vacía = la provincia del comprobante).</summary>
    public string SircarRegimen { get; set; } = string.Empty;
    public string SircarJurisdiccion { get; set; } = string.Empty;
    /// <summary>SICORE: código de impuesto y régimen por cada retención de la tabla V_TA_RETENCIONES.</summary>
    public List<RetencionSicoreCodigo> Sicore { get; set; } = [];
    public IReadOnlyList<(string Codigo, string Nombre)> Jurisdicciones { get; set; } = [];
    public IReadOnlyList<CompraCargaOpcion> Unidades { get; set; } = [];
}

/// <summary>Códigos SICORE de una retención (claves RETGANCI_ / RETGANCR_ del legacy).</summary>
public sealed class RetencionSicoreCodigo
{
    public string IdRetencion { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Impuesto { get; set; } = string.Empty;
    public string Regimen { get; set; } = string.Empty;
}

/// <summary>Retención practicada en una orden de pago o pago (cabecera de V_MV_CPTE, columnas RETGAN_/RETIBR_).</summary>
public sealed class RetencionPago
{
    public string Tc { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Cuenta { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Domicilio { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string Provincia { get; set; } = string.Empty;
    public string CodJurisdiccion { get; set; } = string.Empty;
    public string CodigoPostal { get; set; } = string.Empty;
    public string Cuit { get; set; } = string.Empty;
    public string CondicionIva { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public decimal? ImporteSinIva { get; set; }
    public string IdRetencion { get; set; } = string.Empty;
    public decimal Base { get; set; }
    public decimal Retencion { get; set; }
    public decimal Alicuota { get; set; }
    public string Certificado { get; set; } = string.Empty;
    /// <summary>Alícuota de IVA del comprobante (percepción de IVA en ventas).</summary>
    public string AlicuotaIva { get; set; } = string.Empty;
}

/// <summary>Comprobante de venta con percepción practicada (libro de IVA ventas).</summary>
public sealed class PercepcionVenta
{
    public string Tc { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime FechaContable { get; set; }
    public string Cuit { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string CondicionIva { get; set; } = string.Empty;
    public string DocumentoTipo { get; set; } = string.Empty;
    public string SituacionIb { get; set; } = string.Empty;
    public bool Convenio { get; set; }
    public string CodCpteAfip { get; set; } = string.Empty;
    public decimal NetoGravado { get; set; }
    public decimal NetoNoGravado { get; set; }
    public decimal AlicuotaIva { get; set; }
    public decimal Iva { get; set; }
    public decimal PercepcionIva { get; set; }
    public decimal PercepcionIibb { get; set; }
    public decimal Total { get; set; }
    /// <summary>Base y alícuota de IIBB grabadas en el comprobante (RETIBR_), si las hay.</summary>
    public decimal? BaseIibb { get; set; }
    public decimal? AlicuotaIibb { get; set; }
    public decimal? ImporteSinIva { get; set; }
    public string Provincia { get; set; } = string.Empty;
    public string CodJurisdiccion { get; set; } = string.Empty;
}

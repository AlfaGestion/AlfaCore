namespace AlfaCore.Models;

/// <summary>Certificados de retención que se emiten desde un pago (PG) o una orden de pago (OPG), como la impresión diferida del VB6.</summary>
public static class CompraCertificado
{
    public const string Ganancias = "GAN";
    public const string Iva = "IVA";
    public const string IngresosBrutos = "IBR";
    public const string Suss = "SUSS";

    public static readonly IReadOnlyList<string> Todos = [Ganancias, Iva, IngresosBrutos, Suss];

    public static string Nombre(string codigo) => codigo switch
    {
        Ganancias => "Impuesto a las Ganancias",
        Iva => "IVA",
        IngresosBrutos => "Ingresos Brutos",
        Suss => "SUSS",
        _ => codigo
    };

    /// <summary>Prefijo de las columnas RET*_ de V_MV_CPTE (Importe, Nro, BaseImponible, ALICUOTA, IdRetencion).</summary>
    internal static string Prefijo(string codigo) => codigo switch
    {
        Ganancias => "RETGAN",
        Iva => "RETIVA",
        IngresosBrutos => "RETIBR",
        Suss => "RETSUSS",
        _ => throw new InvalidOperationException("Certificado de retención desconocido.")
    };
}

/// <summary>Un pago u orden de pago de proveedores, con lo retenido, para elegirlo y enviarlo por mail.</summary>
public sealed class CompraPagoFila
{
    public int Id { get; set; }
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string Cuenta { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Unidad { get; set; } = "";
    public decimal Importe { get; set; }
    public string Mail { get; set; } = "";
    public decimal RetGanancias { get; set; }
    public decimal RetIva { get; set; }
    public decimal RetIibb { get; set; }
    public decimal RetSuss { get; set; }
    /// <summary>Último envío del documento que muestra la grilla (el pago o, si se filtra por impuesto, su certificado).</summary>
    public string? UltimoEnvio { get; set; }

    public decimal Retencion(string certificado) => certificado switch
    {
        CompraCertificado.Ganancias => RetGanancias,
        CompraCertificado.Iva => RetIva,
        CompraCertificado.IngresosBrutos => RetIibb,
        CompraCertificado.Suss => RetSuss,
        _ => 0m
    };
}

/// <summary>Lo que se va a mandar: se completa con la ficha del proveedor y los textos configurados, y el usuario puede corregirlo.</summary>
public sealed class CompraMailBorrador
{
    public string Tc { get; set; } = "";
    public string Comprobante { get; set; } = "";
    /// <summary>Vacío = el comprobante en sí; GAN / IVA / IBR / SUSS = su certificado de retención.</summary>
    public string Certificado { get; set; } = "";
    public string Proveedor { get; set; } = "";
    public string Para { get; set; } = "";
    public string Asunto { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public string? MailPrueba { get; set; }
    public string? UltimoEnvio { get; set; }
    public string Archivo { get; set; } = "";
}

public sealed class CompraMailResultado
{
    public bool Enviado { get; set; }
    public string Para { get; set; } = "";
    public string Mensaje { get; set; } = "";
}

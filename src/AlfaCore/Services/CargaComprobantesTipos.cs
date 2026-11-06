namespace AlfaCore.Services;

/// <summary>
/// Tipos de la carga directa y sus efectos, como en V_mv_vta (GrabaComprobante):
/// FC/NC/ND electrónicos (CAE, libro de IVA y asiento); FP/NCFP proformas (asiento y stock, sin libro de IVA ni CAE);
/// PR/NP sin asiento ni stock; RM sin asiento, con stock solo si DescargaStockRemitos=SI.
/// </summary>
public static class CargaComprobantesTipos
{
    public static readonly IReadOnlyList<(string Tc, string Descripcion)> Todos =
    [
        ("FC", "Factura electrónica"), ("NC", "Nota de crédito electrónica"), ("ND", "Nota de débito electrónica"),
        ("FP", "Factura proforma"), ("NCFP", "Nota de crédito proforma"),
        ("PR", "Presupuesto"), ("NP", "Nota de pedido"), ("RM", "Remito")
    ];

    public static bool EsValido(string? tc) => Todos.Any(x => x.Tc == tc);
    public static string Descripcion(string? tc) => Todos.FirstOrDefault(x => x.Tc == tc).Descripcion ?? tc ?? "";
    public static bool EsElectronico(string? tc) => tc is "FC" or "NC" or "ND";
    public static bool EsProforma(string? tc) => tc is "FP" or "NCFP";
    /// <summary>Notas que acreditan una factura y conservan sus precios (NC sobre FC, NCFP sobre FP).</summary>
    public static bool EsCredito(string? tc) => tc is "NC" or "NCFP";
    /// <summary>Llevan una factura asociada obligatoria.</summary>
    public static bool RequiereAsociado(string? tc) => tc is "NC" or "ND" or "NCFP";
    /// <summary>Tipo de la factura que se asocia a la nota.</summary>
    public static string TcAsociado(string? tc) => tc == "NCFP" ? "FP" : "FC";
    /// <summary>PR, NP y RM no van a cuenta corriente ni generan asiento (V_mv_vta: GeneraAsientoVentas=False).</summary>
    public static bool GeneraAsiento(string? tc, bool proformaSinRegistracion)
        => EsElectronico(tc) || (EsProforma(tc) && !proformaSinRegistracion);
    /// <summary>Carga de precios y artículos como una factura (no hereda precios de otro comprobante).</summary>
    public static bool EsEditable(string? tc) => tc is "FC" or "FP" or "ND" or "PR" or "NP" or "RM";

    /// <summary>
    /// Letra de un comprobante no electrónico según las letras habilitadas en V_TA_CPTE (V_mv_vta.Obtener_Nro_Comprobante):
    /// RM/NP/FP usan la primera letra habilitada; NCFP/PR, la que corresponde al cliente si está habilitada
    /// (A pasa a M si solo está M), si no la primera. Sin letras habilitadas: X.
    /// </summary>
    public static string LetraNoFiscal(string tc, string letrasHabilitadas, bool requiereA)
    {
        var letras = new string((letrasHabilitadas ?? "").Trim().ToUpperInvariant().Where(char.IsAsciiLetter).ToArray());
        if (letras.Length == 0) return "X";
        if (tc is "RM" or "NP" or "FP") return letras[..1];
        var letra = requiereA ? "A" : "B";
        if (letras.Contains(letra)) return letra;
        if (letra == "A" && letras.Contains('M')) return "M";
        return letras[..1];
    }

    /// <summary>Letras con columnas de numeración en V_TA_CPTE ({letra}_ULTIMO_NRO / {letra}_SUC_DEFAULT).</summary>
    public static bool LetraConNumeracion(string? letra) => letra is "A" or "B" or "C" or "E" or "M" or "R" or "X";
}

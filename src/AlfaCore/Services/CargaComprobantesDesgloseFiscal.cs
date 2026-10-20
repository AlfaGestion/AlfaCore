using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Único cierre a centavos para cabecera, asiento, libro y solicitud electrónica.</summary>
public sealed record CargaComprobantesDesgloseFiscal(decimal NetoGravado, decimal Exento, decimal Total,
    IReadOnlyList<CargaComprobantesLiquidacionIva> Alicuotas)
{
    public decimal Iva => Alicuotas.Sum(x => x.Iva);
    public decimal Neto => NetoGravado + Exento;
    public static CargaComprobantesDesgloseFiscal Crear(CargaComprobantesLiquidacion liquidacion)
    {
        static decimal Red(decimal x) => decimal.Round(x, 2, MidpointRounding.ToEven);
        if (liquidacion.ImpuestosInternos != 0 || liquidacion.Alicuotas.Count > 3)
            throw new CargaComprobantesReglaException("El desglose fiscal requiere un circuito adicional de impuestos.");
        var grupos = liquidacion.Alicuotas.Select(x => new CargaComprobantesLiquidacionIva
            { Alicuota = x.Alicuota, Neto = Red(x.Neto), Iva = Red(x.Iva) }).ToList();
        foreach (var grupo in grupos) _ = ArcaCodigosAfip.ResolverIdAlicuotaIva(grupo.Alicuota);
        var exento = Red(liquidacion.NetoNoGravado);
        var total = Red(liquidacion.Total);
        var neto = grupos.Sum(x => x.Neto);
        var diferencia = total - neto - exento - grupos.Sum(x => x.Iva);
        if (total <= 0 || Math.Abs(diferencia) > .02m || (grupos.Count == 0 && diferencia != 0))
            throw new CargaComprobantesReglaException("El desglose de IVA y exentos no coincide con el total del comprobante.");
        if (diferencia != 0) grupos[0].Iva += diferencia;
        if (grupos.Any(x => x.Neto < 0 || x.Iva < 0) || exento < 0)
            throw new CargaComprobantesReglaException("El desglose fiscal contiene importes negativos no admitidos.");
        return new(neto, exento, total, grupos);
    }
}

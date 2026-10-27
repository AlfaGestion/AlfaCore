using AlfaCore.Models;

namespace AlfaCore.Tests;

/// <summary>
/// Modelo diagnóstico del paso clsTotales -> VtaTot con hasta una alícuota, exentos y sin ajustes globales.
/// Se mantiene fuera del motor comercial hasta contrastar el ejecutable original.
/// </summary>
internal static class CargaComprobantesCierreLegacyDiagnostico
{
    public static CierreLegacy? Evaluar(CargaComprobantesOrigen origen,
        CargaComprobantesLiquidacion calculo, CargaComprobantesContexto contexto, string letra, bool ajustarTotal)
    {
        if (letra is not ("A" or "B") || contexto.Decimales is < 0 or > 4 || calculo.Lineas.Count == 0 ||
            calculo.Alicuotas.Count > 1 || calculo.Lineas.Any(x => x.ImpuestosInternos != 0) ||
            origen.TieneAjustesGlobales || origen.TienePercepciones)
            return null;
        var ids = calculo.Lineas.Select(x => x.LineaId).ToHashSet();
        // El descuento por artículo ya está aplicado al neto de la línea (1129).
        // No es pDescuento/pDescuento1..3, que son ajustes globales (613-616).
        if (origen.Lineas.Any(x => ids.Contains(x.Id) && (x.ItcUnitario != 0 || x.AlicuotaIvaRecargo != 0)))
            return null;

        decimal Currency(decimal valor) => decimal.Round(valor, 4, MidpointRounding.ToEven);
        decimal Formato(decimal valor, int decimales) => decimal.Round(Currency(valor), decimales, MidpointRounding.ToEven);
        var decimalesEntrada = letra == "B" ? 4 : contexto.Decimales;
        // Tomar las líneas originales: el IVA global del calculador puede incluir ya un ajuste final.
        var netoAcumulado = calculo.Lineas.Where(x => !x.Exento).Sum(x => x.Neto);
        var noGravadoAcumulado = calculo.Lineas.Where(x => x.Exento).Sum(x => x.Neto);
        var ivaAcumulado = calculo.Lineas.Sum(x => x.Iva);
        var totalExacto = Formato(netoAcumulado + noGravadoAcumulado + ivaAcumulado, contexto.Decimales);
        // clsTotales 533-536 / 588-591 formatea ANTES de pasar al formulario.
        var netoEntrada = Formato(netoAcumulado, decimalesEntrada);
        var noGravadoEntrada = Formato(noGravadoAcumulado, decimalesEntrada);
        var tasa = calculo.Alicuotas.SingleOrDefault()?.Alicuota ?? 0;
        // VtaTot 3190-3193 activa recálculo inicial; 2912-2923 usa ese neto.
        var ivaRecalculado = Currency(netoEntrada * tasa / 100);
        var neto = Formato(netoEntrada, contexto.Decimales);
        var noGravado = Formato(noGravadoEntrada, contexto.Decimales);
        var iva = Formato(ivaRecalculado, contexto.Decimales);
        var totalComponentes = neto + noGravado + iva;
        var diferencia = Currency(totalExacto - totalComponentes);
        var ajuste = ajustarTotal && totalExacto > 0 && ivaRecalculado != 0 &&
            diferencia != 0 && Math.Abs(diferencia) <= .02m ? diferencia : 0;
        return new(letra, decimalesEntrada, netoAcumulado, noGravadoAcumulado, ivaAcumulado, netoEntrada,
            noGravadoEntrada, ivaRecalculado, neto, noGravado, iva + ajuste, totalComponentes, totalExacto, ajuste, totalComponentes + ajuste);
    }
}

internal sealed record CierreLegacy(string Letra, int DecimalesEntrada, decimal NetoAcumulado,
    decimal NetoNoGravadoAcumulado, decimal IvaAcumulado, decimal NetoEntradaTotales,
    decimal NetoNoGravadoEntradaTotales, decimal IvaRecalculado,
    decimal NetoGravado, decimal NetoNoGravado, decimal Iva, decimal TotalComponentes, decimal TotalExacto,
    decimal AjusteAplicado, decimal Total);

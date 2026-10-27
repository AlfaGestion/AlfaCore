using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Port acotado de clsTotales.AcumulaInsumos para FC con precios finales históricos.
/// Conserva asignaciones Currency de cuatro decimales y el ajuste lineal de IVA.
/// Los circuitos no cubiertos se informan explícitamente; no se completa con ceros.
/// </summary>
public static class CargaComprobantesCalculador
{
    public static IReadOnlyList<string> GetPendientes(CargaComprobantesOrigen origen,
        CargaComprobantesContexto contexto, IReadOnlyList<CargaComprobantesCantidad> cantidades)
    {
        var pendientes = new List<string>();
        if (!origen.DatosCalculoDisponibles) pendientes.Add("Faltan los datos fiscales históricos del origen.");
        if (!contexto.PreciosFinales || contexto.PreciosFinalesNeto)
            pendientes.Add("La liquidación incorporada requiere PreciosFinales=SI y PreciosFinalesNeto distinto de SI.");
        if (origen.Tc == "RM" && contexto.RevalorizaRemitos)
            pendientes.Add("El cliente revaloriza remitos; falta resolver las listas y precios vigentes.");
        if (origen.TieneAjustesGlobales) pendientes.Add("El documento tiene descuentos globales, recargos o anticipos pendientes de portar.");
        if (origen.TienePercepciones) pendientes.Add("La cuenta o el origen requieren liquidar percepciones.");
        if (origen.Tc == "FP" && !contexto.UsaFacturaProforma)
            pendientes.Add("La factura proforma no está habilitada en la configuración de esta base.");
        var ids = cantidades.Where(x => x.Cantidad > 0).Select(x => x.LineaId).ToHashSet();
        if (origen.Lineas.Any(x => ids.Contains(x.Id) && (x.ItcUnitario != 0 || x.AlicuotaIvaRecargo != 0)))
            pendientes.Add("Los renglones seleccionados incluyen ITC o IVA de recargo pendiente de portar.");
        return pendientes;
    }

    public static CargaComprobantesLiquidacion Calcular(CargaComprobantesOrigen origen,
        CargaComprobantesContexto contexto, IReadOnlyList<CargaComprobantesCantidad> cantidades)
    {
        // Comparte la validación de pertenencia, cantidades y alcance con la consulta.
        CargaComprobantesSimulador.ValidarSeleccion(origen, cantidades);
        var pendientes = GetPendientes(origen, contexto, cantidades);
        if (pendientes.Count != 0) throw new CargaComprobantesReglaException(string.Join(" ", pendientes));
        if (contexto.Decimales is < 0 or > 4)
            throw new CargaComprobantesReglaException("La precisión monetaria debe estar entre cero y cuatro decimales.");

        var porId = origen.Lineas.ToDictionary(x => x.Id);
        var result = new CargaComprobantesLiquidacion();
        var lineas = new List<CargaComprobantesLiquidacionLinea>();
        var alicuotas = new List<CargaComprobantesLiquidacionIva>();
        // Orden de la grilla VB6: también determina los slots de alícuotas.
        foreach (var item in cantidades.Where(x => x.Cantidad > 0).OrderBy(x => porId[x.LineaId].Secuencia).ThenBy(x => x.LineaId))
        {
            var origenLinea = porId[item.LineaId];
            if (origenLinea.Descuento > 100 || origenLinea.PrecioSinIva < 0 || origenLinea.Precio < 0 || origenLinea.ImpuestoInternoUnitario < 0)
                throw new CargaComprobantesReglaException("El renglón requiere un circuito de precios o descuentos especial.");
            var cantidad = Currency(item.Cantidad); // Utl.CValCur en AcumulaInsumos.
            if (cantidad <= 0)
                throw new CargaComprobantesReglaException("La cantidad seleccionada se reduce a cero con la precisión Currency del cálculo legacy.");
            // V_mv_vta:24809 aplica exención de artículos a FP cuando RobaIvaProforma=NO.
            // TotalizaLineaArticulos usa entonces AuxPrecioSinIVA, aunque la lista incluya IVA.
            var exento = origenLinea.Exento || origen.ExentoIvaArticulos ||
                (origen.Tc == "FP" && !contexto.RobaIvaProforma);
            var tasa = origenLinea.AlicuotaIva != 0 ? origenLinea.AlicuotaIva : contexto.AlicuotaGeneral;
            if (!exento && (tasa <= 0 || tasa > 100))
                throw new CargaComprobantesReglaException("Falta una alícuota de IVA válida para el renglón.");

            // ImporteMenosPorcDto retorna Currency antes de multiplicar cantidades.
            var precioNeto = Currency(origenLinea.PrecioSinIva * (1 - origenLinea.Descuento / 100));
            var neto = Currency(precioNeto * cantidad);
            // ImporteIVA retorna Double (clsTotales 324-331). La asignación Currency
            // sucede después de multiplicar cantidad (1144/1153), nunca por unidad.
            var iva = exento ? 0 : Currency(precioNeto * tasa / 100 * cantidad);
            var interno = Currency(origenLinea.ImpuestoInternoUnitario * cantidad);
            // IMPORTE guardado incluye internos con PreciosFinales; AcumulaInsumos los resta.
            var precioFinalSinInternos = Currency(origenLinea.Precio - origenLinea.ImpuestoInternoUnitario);
            var bruto = Currency(precioFinalSinInternos * cantidad);
            if (!exento && origenLinea.Descuento <= 0 && Math.Abs(Currency(neto + iva - bruto)) > 0.001m)
            {
                neto = Currency(bruto / (1 + tasa / 100));
                iva = Currency(bruto - neto);
            }
            var linea = new CargaComprobantesLiquidacionLinea
            {
                LineaId = item.LineaId, Cantidad = item.Cantidad, Neto = neto, Iva = iva,
                ImpuestosInternos = interno, Exento = exento, Alicuota = exento ? 0 : tasa,
                Total = Currency(neto + iva + interno)
            };
            lineas.Add(linea);
            if (exento) result.NetoNoGravado = Currency(result.NetoNoGravado + neto);
            else
            {
                result.NetoGravado = Currency(result.NetoGravado + neto);
                var grupo = alicuotas.FirstOrDefault(x => x.Alicuota == tasa);
                if (grupo is null)
                {
                    if (alicuotas.Count == 3)
                        throw new CargaComprobantesReglaException("Alfa Gestión admite hasta tres alícuotas en este circuito.");
                    grupo = new() { Alicuota = tasa }; alicuotas.Add(grupo);
                }
                grupo.Neto = Currency(grupo.Neto + neto);
                grupo.Iva = Currency(grupo.Iva + iva);
            }
            result.Iva = Currency(result.Iva + iva);
            result.ImpuestosInternos = Currency(result.ImpuestosInternos + interno);
        }
        // Totalizar/SoloTotalizar redondea el total acumulado, no cada impuesto de renglón.
        decimal Formato(decimal importe) => decimal.Round(Currency(importe), contexto.Decimales, MidpointRounding.ToEven);
        var totalExacto = Formato(result.NetoGravado + result.NetoNoGravado + result.Iva + result.ImpuestosInternos);
        // VtaTot suma campos monetarios ya formateados; conservar ambos pasos del legacy.
        result.Total = Formato(result.NetoGravado) + Formato(result.NetoNoGravado) +
            alicuotas.Sum(x => Formato(x.Iva)) + Formato(result.ImpuestosInternos);
        var diferencia = Currency(totalExacto - result.Total);
        // AjusteRedondeoTotal (02-10-2026): solo circuito simple, una alícuota, hasta dos centavos.
        if (contexto.AjustaRedondeoTotal && totalExacto > 0 && alicuotas.Count == 1 && result.Iva != 0 &&
            diferencia != 0 && Math.Abs(diferencia) <= .02m)
        {
            result.Iva = Formato(result.Iva) + diferencia;
            alicuotas[0].Iva = result.Iva;
            result.Total = totalExacto;
        }
        result.Lineas = lineas;
        result.Alicuotas = alicuotas;
        return result;
    }

    private static decimal Currency(decimal value) => decimal.Round(value, 4, MidpointRounding.ToEven);
}

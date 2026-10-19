using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Proyección proporcional de los importes históricos. No es el liquidador fiscal de emisión.</summary>
public static class CargaComprobantesSimulador
{
    public static CargaComprobantesSimulacion Simular(
        CargaComprobantesOrigen origen, CargaComprobantesContexto contexto,
        IReadOnlyList<CargaComprobantesCantidad> cantidades)
    {
        ValidarSeleccion(origen, cantidades);
        var lineas = origen.Lineas.ToDictionary(x => x.Id);
        var importe = cantidades.Where(x => x.Cantidad > 0).Sum(item =>
            decimal.Round(lineas[item.LineaId].TotalOriginal * item.Cantidad / lineas[item.LineaId].CantidadOriginal,
                contexto.Decimales, MidpointRounding.ToEven));
        var pendientes = CargaComprobantesCalculador.GetPendientes(origen, contexto, cantidades);
        var liquidacion = pendientes.Count == 0 ? CargaComprobantesCalculador.Calcular(origen, contexto, cantidades) : null;
        return new CargaComprobantesSimulacion
        {
            ImporteReferencia = importe,
            LineasSeleccionadas = cantidades.Count(x => x.Cantidad > 0),
            Liquidacion = liquidacion, PendientesCalculo = pendientes,
            EfectoStock = origen.TieneDevolucion
                ? "Revisar devolución previa antes de emitir"
                : origen.Tc == "RM" && contexto.DescargaStockRemitos && origen.TieneStock
                    ? "El remito ya movió stock; evitar una segunda descarga"
                    : "Prever salida de stock al emitir la factura",
            Avisos = ["Importe de referencia proporcional al detalle del origen. La liquidación incorporada usa precios históricos y un circuito acotado; falta certificar emisión, descuentos globales y percepciones.",
                "La simulación conserva el documento original y no reserva cantidades ni genera comprobantes."]
        };
    }

    internal static void ValidarSeleccion(CargaComprobantesOrigen origen, IReadOnlyList<CargaComprobantesCantidad> cantidades)
    {
        if (origen.TieneReservas || origen.TienePreparacion)
            throw new CargaComprobantesReglaException("Este documento pertenece a un circuito de reservas o preparación pendiente de incorporar.");
        if (origen.TieneOtrosDetalles)
            throw new CargaComprobantesReglaException("Este origen incluye servicios u otros conceptos. Su simulación completa se incorporará en otro incremento.");
        if (origen.Moneda.Length > 0 && origen.Moneda != "1")
            throw new CargaComprobantesReglaException("La simulación inicial admite documentos en moneda local.");
        if (cantidades.Count == 0 || cantidades.GroupBy(x => x.LineaId).Any(g => g.Count() > 1))
            throw new CargaComprobantesReglaException("Seleccioná renglones únicos para simular.");

        var lineas = origen.Lineas.ToDictionary(x => x.Id);
        var seleccionadas = 0;
        foreach (var item in cantidades)
        {
            if (!lineas.TryGetValue(item.LineaId, out var linea))
                throw new CargaComprobantesReglaException("Un renglón no pertenece al documento seleccionado. Volvé a cargar el origen.");
            if (item.Cantidad < 0 || item.Cantidad > linea.Pendiente)
                throw new CargaComprobantesReglaException($"Revisá la cantidad de {linea.Articulo}: debe estar entre cero y el pendiente actualizado.");
            if (item.Cantidad == 0) continue;
            if (linea.RenglonesArticulo > 1 || linea.CantidadOriginal <= 0)
                throw new CargaComprobantesReglaException("El artículo aparece en varios renglones o requiere tratar una devolución. Falta certificar la distribución de sus cantidades.");
            seleccionadas++;
        }
        if (seleccionadas == 0)
            throw new CargaComprobantesReglaException("Informá una cantidad mayor a cero en al menos un renglón.");

    }
}

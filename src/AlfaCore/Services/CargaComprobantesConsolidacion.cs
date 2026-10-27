using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Conserva cada origen y renglón; nunca fusiona cantidades del mismo artículo.</summary>
public static class CargaComprobantesConsolidacion
{
    public static int[] Identidades(int origenId, IReadOnlyList<CargaComprobantesCantidad> cantidades)
    {
        if (origenId <= 0 || cantidades.Count == 0 || cantidades.Any(x => x.OrigenId < 0))
            throw new CargaComprobantesReglaException("Seleccioná documentos y cantidades válidos.");
        var ids = cantidades.Select(x => x.OrigenId == 0 ? origenId : x.OrigenId).Distinct().Order().ToArray();
        if (!ids.Contains(origenId) || ids.Length > 30)
            throw new CargaComprobantesReglaException("Seleccioná hasta 30 orígenes, incluyendo el documento principal.");
        return ids;
    }

    public static CargaComprobantesOrigen Unir(IReadOnlyList<CargaComprobantesOrigen> origenes)
    {
        if (origenes.Count == 0) throw new CargaComprobantesReglaException("Seleccioná al menos un origen.");
        var primero = origenes[0];
        static object Contexto(CargaComprobantesOrigen o) => new
        { o.Tc, o.Cuenta, o.UnidadNegocio, o.Deposito, o.Lista, o.Moneda, o.ExentoIvaArticulos, o.TieneStock };
        if (origenes.Any(x => JsonSerializer.Serialize(Contexto(x)) != JsonSerializer.Serialize(Contexto(primero))) ||
            origenes.Select(x => x.Id).Distinct().Count() != origenes.Count ||
            origenes.SelectMany(x => x.Lineas).GroupBy(x => x.Id).Any(x => x.Count() > 1))
            throw new CargaComprobantesReglaException("Los orígenes deben ser distintos y compartir tipo, cliente, unidad, depósito, lista, moneda, exención y circuito de stock.");
        var unido = JsonSerializer.Deserialize<CargaComprobantesOrigen>(JsonSerializer.Serialize(primero))!;
        unido.Lineas = origenes.SelectMany(x => x.Lineas)
            .Select((x, i) => { var l = JsonSerializer.Deserialize<CargaComprobantesLineaOrigen>(JsonSerializer.Serialize(x))!; l.Secuencia = i + 1; return l; }).ToList();
        unido.TieneReservas = origenes.Any(x => x.TieneReservas);
        unido.TienePreparacion = origenes.Any(x => x.TienePreparacion);
        unido.TieneOtrosDetalles = origenes.Any(x => x.TieneOtrosDetalles);
        unido.TieneAjustesGlobales = origenes.Any(x => x.TieneAjustesGlobales);
        unido.TienePercepciones = origenes.Any(x => x.TienePercepciones);
        unido.TieneDevolucion = origenes.Any(x => x.TieneDevolucion);
        unido.DatosCalculoDisponibles = origenes.All(x => x.DatosCalculoDisponibles);
        unido.Importe = origenes.Sum(x => x.Importe);
        return unido;
    }

    public static CargaComprobantesPlanGrabacion UnirPlanes(List<CargaComprobantesPlanGrabacion> partes,
        CargaComprobantesContexto contexto, IReadOnlyList<CargaComprobantesCantidad> cantidades)
    {
        static string Contrato(CargaComprobantesPlanGrabacion p) => JsonSerializer.Serialize(new
        { p.Talonario, p.CondicionIva, p.CondicionVenta, p.Motivo, p.Caja, p.TcContable, p.Subdiario, p.Periodo,
          p.Letra, p.PuntoVenta, p.DescargaStock, p.MotivoStock, p.CuentaCliente, p.CuentaVentas, p.CuentaIva, p.Fecha, p.Vencimiento });
        if (partes.Count < 2 || partes.Any(x => x.Directa is not null || x.Origenes is not null || Contrato(x) != Contrato(partes[0])))
            throw new CargaComprobantesReglaException("Los orígenes tienen condiciones comerciales, contables o de stock incompatibles.");
        var plan = JsonSerializer.Deserialize<CargaComprobantesPlanGrabacion>(JsonSerializer.Serialize(partes[0]))!;
        plan.Origenes = partes;
        plan.Origen = Unir(partes.Select(x => x.Origen).ToList());
        plan.Cantidades = cantidades.OrderBy(x => x.LineaId).ToList();
        plan.Liquidacion = CargaComprobantesCalculador.Calcular(plan.Origen, contexto, cantidades);
        plan.ArtExentoANoGrav = plan.Liquidacion.NetoNoGravado != 0 && contexto.ArtExentoANoGrav;
        plan.Huella = "";
        plan.Huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan))));
        return plan;
    }
}

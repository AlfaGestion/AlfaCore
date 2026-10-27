using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CargaComprobantesCalculoTests
{
    private static CargaComprobantesContexto Contexto() => new() { PreciosFinales = true, AlicuotaGeneral = 21 };
    private static CargaComprobantesOrigen Origen(params CargaComprobantesLineaOrigen[] lineas) => new()
    { Tc = "RM", Moneda = "1", DatosCalculoDisponibles = true, Lineas = lineas };
    private static CargaComprobantesLineaOrigen Linea(int id = 1, decimal neto = 100, decimal precio = 121, decimal iva = 21) => new()
    { Id = id, Secuencia = id, CantidadOriginal = 10, Pendiente = 10, RenglonesArticulo = 1,
        PrecioSinIva = neto, Precio = precio, AlicuotaIva = iva, Articulo = id.ToString() };

    [Fact]
    public void ParcialCalculaDesdePrecioNoDesdeTotalFinalHistorico()
    {
        var l = Linea(); l.TotalOriginal = 9999;
        var result = CargaComprobantesCalculador.Calcular(Origen(l), Contexto(), [new() { LineaId = 1, Cantidad = 2 }]);
        Assert.Equal(200m, result.NetoGravado); Assert.Equal(42m, result.Iva); Assert.Equal(242m, result.Total);
        Assert.Equal(10m, l.Pendiente);
    }

    [Fact]
    public void DescuentoUnitarioConservaCurrencyAntesDeMultiplicar()
    {
        var l = Linea(neto: 1.0001m, precio: 1.21m); l.Descuento = 12.345m;
        var r = CargaComprobantesCalculador.Calcular(Origen(l), Contexto(), [new() { LineaId = 1, Cantidad = 3 }]);
        // Currency(1.0001*(1-.12345))=.8766; Currency(.8766*.21*3)=.5523.
        Assert.Equal(2.6298m, r.NetoGravado); Assert.Equal(.5523m, r.Iva); Assert.Equal(3.18m, r.Total);
    }

    [Fact]
    public void AjusteLinealPreservaPrecioFinalCuandoNetoMasIvaDifiere()
    {
        var l = Linea(neto: .83m, precio: 1m);
        var r = CargaComprobantesCalculador.Calcular(Origen(l), Contexto(), [new() { LineaId = 1, Cantidad = 3 }]);
        Assert.Equal(2.4793m, r.NetoGravado); Assert.Equal(.5207m, r.Iva); Assert.Equal(3m, r.Total);
    }

    [Fact]
    public void IvaUnitarioNoSeRedondeaAntesDeMultiplicarCantidad()
    {
        var l = Linea(neto: .0001m, precio: .0001m);
        l.Descuento = 1; // Evita el ajuste lineal: se verifica el IVA antes de ese paso.
        l.CantidadOriginal = l.Pendiente = 10000;
        var r = CargaComprobantesCalculador.Calcular(Origen(l), Contexto(), [new() { LineaId = 1, Cantidad = 10000 }]);
        Assert.Equal(1m, r.NetoGravado);
        Assert.Equal(.21m, r.Iva); // Redondear por unidad lo reduciría a cero.
        Assert.Equal(1.21m, r.Total);
    }

    [Fact]
    public void CantidadGrandeConservaAjusteLinealDelNetoDelOriginal()
    {
        var l = Linea(neto: 1980.3554m, precio: 2396.23m);
        l.CantidadOriginal = l.Pendiente = 200;
        var r = CargaComprobantesCalculador.Calcular(Origen(l), Contexto(), [new() { LineaId = 1, Cantidad = 200 }]);
        Assert.Equal(396071.0744m, r.NetoGravado);
        Assert.Equal(83174.9256m, r.Iva);
        Assert.Equal(479246m, r.Total);
    }

    [Fact]
    public void ExentoEInternosNoSeIncluyenEnBaseDeIva()
    {
        var l1 = Linea(); l1.ImpuestoInternoUnitario = 5; l1.Precio = 126;
        var l2 = Linea(2, 50, 50); l2.Exento = true;
        var r = CargaComprobantesCalculador.Calcular(Origen(l1, l2), Contexto(),
            [new() { LineaId = 1, Cantidad = 2 }, new() { LineaId = 2, Cantidad = 1 }]);
        Assert.Equal(200m, r.NetoGravado); Assert.Equal(50m, r.NetoNoGravado);
        Assert.Equal(42m, r.Iva); Assert.Equal(10m, r.ImpuestosInternos); Assert.Equal(302m, r.Total);
        Assert.Single(r.Alicuotas);
    }

    [Fact]
    public void AcumulaAntesDeRedondearElTotalYUsaRedondeoAlPar()
    {
        var a = Linea(1, .005m, .005m); a.Exento = true;
        var b = Linea(2, .005m, .005m); b.Exento = true;
        var o = Origen(a, b);
        Assert.Equal(0m, CargaComprobantesCalculador.Calcular(o, Contexto(), [new() { LineaId = 1, Cantidad = 1 }]).Total);
        Assert.Equal(.01m, CargaComprobantesCalculador.Calcular(o, Contexto(),
            [new() { LineaId = 1, Cantidad = 1 }, new() { LineaId = 2, Cantidad = 1 }]).Total);
    }

    [Fact]
    public void MantieneOrdenDeSecuenciaParaAlicuotasYRechazaLaCuarta()
    {
        var o = Origen(Linea(1), Linea(2, 100, 110.5m, 10.5m), Linea(3, 100, 127, 27), Linea(4, 100, 105, 5));
        var items = o.Lineas.Reverse().Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = 1 }).ToList();
        var r = CargaComprobantesCalculador.Calcular(o, Contexto(), items.Where(x => x.LineaId != 4).ToList());
        Assert.Equal(new[] { 21m, 10.5m, 27m }, r.Alicuotas.Select(x => x.Alicuota));
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesCalculador.Calcular(o, Contexto(), items));
    }

    [Fact]
    public void AjusteRedondeoTotalRespetaConfiguracionYSoloUnaAlicuota()
    {
        var o = Origen(Linea(neto: 100.005m, precio: 121.0061m));
        var items = new[] { new CargaComprobantesCantidad { LineaId = 1, Cantidad = 1 } };
        var contexto = Contexto();
        var sinAjuste = CargaComprobantesCalculador.Calcular(o, contexto, items);
        Assert.Equal(121m, sinAjuste.Total);
        contexto.AjustaRedondeoTotal = true;
        var conAjuste = CargaComprobantesCalculador.Calcular(o, contexto, items);
        Assert.Equal(121.01m, conAjuste.Total); Assert.Equal(21.01m, conAjuste.Iva);
        Assert.Equal(sinAjuste.NetoGravado, conAjuste.NetoGravado);
        Assert.Equal(conAjuste.Iva, conAjuste.Alicuotas.Single().Iva);
    }

    [Fact]
    public void CircuitosIncompletosNoProducenUnTotalFiscalAparente()
    {
        var o = Origen(Linea()); o.TienePercepciones = true; o.TieneAjustesGlobales = true;
        var items = new[] { new CargaComprobantesCantidad { LineaId = 1, Cantidad = 1 } };
        var r = CargaComprobantesSimulador.Simular(o, Contexto(), items);
        Assert.Null(r.Liquidacion); Assert.Equal(2, r.PendientesCalculo.Count);
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesCalculador.Calcular(o, Contexto(), items));
    }

    [Theory]
    [InlineData(true, 21, 121)]
    [InlineData(false, 0, 100)]
    public void ProformaSumaIvaSegunConfiguracionSinTomarElBrutoComoNeto(bool sumaIva, decimal iva, decimal total)
    {
        var origen = Origen(Linea()); origen.Tc = "FP";
        var contexto = Contexto(); contexto.UsaFacturaProforma = true; contexto.RobaIvaProforma = sumaIva;
        var result = CargaComprobantesCalculador.Calcular(origen, contexto, [new() { LineaId = 1, Cantidad = 1 }]);
        Assert.Equal(iva, result.Iva); Assert.Equal(total, result.Total);
        Assert.Equal(sumaIva ? 100m : 0m, result.NetoGravado);
        Assert.Equal(sumaIva ? 0m : 100m, result.NetoNoGravado);
        Assert.Equal(sumaIva ? 1 : 0, result.Alicuotas.Count);
        Assert.Equal(121, origen.Lineas[0].Precio); // La simulación no transforma el precio original.
    }

    [Fact]
    public void ProformaConDescuentoSinIvaConservaPrecioNetoYPrecisionCurrency()
    {
        var linea = Linea(neto: 1.0001m, precio: 1.21m); linea.Descuento = 12.345m;
        var origen = Origen(linea); origen.Tc = "FP";
        var contexto = Contexto(); contexto.UsaFacturaProforma = true;
        var result = CargaComprobantesCalculador.Calcular(origen, contexto, [new() { LineaId = 1, Cantidad = 3 }]);
        Assert.Equal(2.6298m, result.NetoNoGravado); Assert.Equal(2.63m, result.Total); Assert.Equal(0, result.Iva);
    }

    [Fact]
    public void HabilitarIvaEnProformaNoQuitaExencionDelArticulo()
    {
        var linea = Linea(); linea.Exento = true;
        var origen = Origen(linea); origen.Tc = "FP";
        var contexto = Contexto(); contexto.UsaFacturaProforma = true; contexto.RobaIvaProforma = true;
        var result = CargaComprobantesCalculador.Calcular(origen, contexto, [new() { LineaId = 1, Cantidad = 1 }]);
        Assert.Equal(100, result.Total); Assert.Equal(0, result.Iva);
    }

    [Fact]
    public void ProformaNoHabilitadaSeRechazaYSuConfiguracionNoCambiaLaFactura()
    {
        var origen = Origen(Linea()); origen.Tc = "FP";
        var contexto = Contexto();
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesCalculador.Calcular(origen, contexto,
            [new() { LineaId = 1, Cantidad = 1 }]));
        origen.Tc = "RM";
        Assert.Equal(121, CargaComprobantesCalculador.Calcular(origen, contexto,
            [new() { LineaId = 1, Cantidad = 1 }]).Total);
    }
}

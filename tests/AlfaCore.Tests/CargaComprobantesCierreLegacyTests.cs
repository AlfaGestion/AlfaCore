using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CargaComprobantesCierreLegacyTests
{
    private static (CargaComprobantesOrigen Origen, CargaComprobantesContexto Contexto,
        CargaComprobantesLiquidacion Calculo) Caso()
    {
        var contexto = new CargaComprobantesContexto
            { PreciosFinales = true, AlicuotaGeneral = 21, Decimales = 2, AjustaRedondeoTotal = true };
        var origen = new CargaComprobantesOrigen
        {
            Tc = "FC", Moneda = "1", DatosCalculoDisponibles = true,
            Lineas = [new() { Id = 1, Secuencia = 1, Articulo = "CASO", RenglonesArticulo = 1,
                CantidadOriginal = 2, Pendiente = 2, Precio = 12853.27m, PrecioSinIva = 10622.54m, AlicuotaIva = 21 }]
        };
        var calculo = CargaComprobantesCalculador.Calcular(origen, contexto, [new() { LineaId = 1, Cantidad = 2 }]);
        return (origen, contexto, calculo);
    }

    [Theory]
    [InlineData("A", false, 4461.46, 25706.53, 0)]
    [InlineData("A", true, 4461.47, 25706.54, .01)]
    [InlineData("B", false, 4461.47, 25706.54, 0)]
    [InlineData("B", true, 4461.47, 25706.54, 0)]
    public void PrimerCasoDistingueRedondeoEntradaRecalculoYAjusteFinal(string letra, bool ajuste,
        decimal iva, decimal total, decimal diferencia)
    {
        var (origen, contexto, calculo) = Caso();
        var cierre = CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, letra, ajuste)!;
        Assert.Equal(21245.0744m, cierre.NetoAcumulado);
        Assert.Equal(4461.4656m, cierre.IvaAcumulado);
        Assert.Equal(21245.07m, cierre.NetoGravado);
        Assert.Equal(letra == "B" ? 21245.0744m : 21245.07m, cierre.NetoEntradaTotales);
        Assert.Equal(iva, cierre.Iva); Assert.Equal(total, cierre.Total);
        Assert.Equal(diferencia, cierre.AjusteAplicado);
        // El diagnóstico no altera la liquidación ni las entradas del circuito comercial.
        Assert.Equal(25706.54m, calculo.Total); Assert.Equal(4461.4656m, calculo.Iva);
        Assert.Equal(10622.54m, origen.Lineas[0].PrecioSinIva);
    }

    [Fact]
    public void ElDiagnosticoNoFabricaResultadosDeCapacidadesNoModeladas()
    {
        var (origen, contexto, calculo) = Caso();
        Assert.Null(CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, "X", false));
        origen.TieneAjustesGlobales = true;
        Assert.Null(CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, "A", false));
        origen.TieneAjustesGlobales = false;
        calculo.Lineas[0].ImpuestosInternos = 1;
        Assert.Null(CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, "A", false));
    }

    [Fact]
    public void NoAjustaDiferenciasSuperioresADosCentavos()
    {
        var (origen, contexto, calculo) = Caso();
        calculo.Lineas[0].Iva += 1;
        var cierre = CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, "A", true)!;
        Assert.Equal(0m, cierre.AjusteAplicado);
        Assert.Equal(25706.53m, cierre.Total);
        Assert.Equal(25707.54m, cierre.TotalExacto);
    }

    [Theory]
    [InlineData("A", 8)]
    [InlineData("B", 8)]
    [InlineData("A", 9)]
    [InlineData("B", 9)]
    [InlineData("A", 15)]
    [InlineData("B", 15)]
    public void DescuentoPorRenglonNoSeVuelveAAplicarAlCerrar(string letra, decimal descuento)
    {
        var (origen, contexto, _) = Caso();
        var linea = origen.Lineas[0];
        linea.PrecioSinIva = 100; linea.Precio = 121; linea.Descuento = descuento;
        var calculo = CargaComprobantesCalculador.Calcular(origen, contexto, [new() { LineaId = 1, Cantidad = 2 }]);
        var esperadoNeto = (100 - descuento) * 2;
        var cierre = CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, letra, false)!;
        Assert.Equal(esperadoNeto, cierre.NetoGravado);
        Assert.Equal(esperadoNeto * .21m, cierre.Iva);
        Assert.Equal(esperadoNeto * 1.21m, cierre.Total);
        Assert.Equal(descuento, linea.Descuento);
        Assert.Equal(esperadoNeto, calculo.NetoGravado);
    }

    [Fact]
    public void MezclaLineasConYSinDescuentoYConservaCurrencyAntesDeCantidad()
    {
        var (origen, contexto, _) = Caso();
        origen.Lineas[0].PrecioSinIva = 1.0001m; origen.Lineas[0].Precio = 1.21m;
        origen.Lineas[0].Descuento = 12.345m;
        origen.Lineas = [origen.Lineas[0], new() { Id = 2, Secuencia = 2, Articulo = "OTRO", RenglonesArticulo = 1,
            CantidadOriginal = 1, Pendiente = 1, Precio = 121, PrecioSinIva = 100, AlicuotaIva = 21 }];
        origen.Lineas[0].CantidadOriginal = origen.Lineas[0].Pendiente = 3;
        var calculo = CargaComprobantesCalculador.Calcular(origen, contexto,
            [new() { LineaId = 1, Cantidad = 3 }, new() { LineaId = 2, Cantidad = 1 }]);
        var cierre = CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, "A", false)!;
        Assert.Equal(102.6298m, cierre.NetoAcumulado);
        Assert.Equal(102.63m, cierre.NetoGravado);
        Assert.Equal(21.55m, cierre.Iva);
        Assert.Equal(124.18m, cierre.Total);
        Assert.Equal(2, calculo.Lineas.Count);
    }

    [Theory]
    [InlineData("A", false)]
    [InlineData("B", false)]
    [InlineData("A", true)]
    [InlineData("B", true)]
    public void ExentosSeAcumulanSeparadosSinFabricarBaseNiIva(string letra, bool incluirGravado)
    {
        var (origen, contexto, _) = Caso();
        var exenta = origen.Lineas[0];
        exenta.PrecioSinIva = .005m; exenta.Precio = .005m; exenta.Exento = true;
        if (incluirGravado)
            origen.Lineas = [exenta, new() { Id = 2, Secuencia = 2, Articulo = "GRAVADO", RenglonesArticulo = 1,
                CantidadOriginal = 1, Pendiente = 1, Precio = 121, PrecioSinIva = 100, AlicuotaIva = 21 }];
        var cantidades = origen.Lineas.Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = x.CantidadOriginal }).ToArray();
        var calculo = CargaComprobantesCalculador.Calcular(origen, contexto, cantidades);
        var cierre = CargaComprobantesCierreLegacyDiagnostico.Evaluar(origen, calculo, contexto, letra, true)!;
        Assert.Equal(.01m, cierre.NetoNoGravado);
        Assert.Equal(incluirGravado ? 100m : 0m, cierre.NetoGravado);
        Assert.Equal(incluirGravado ? 21m : 0m, cierre.Iva);
        Assert.Equal(incluirGravado ? 121.01m : .01m, cierre.Total);
        Assert.Equal(0m, cierre.AjusteAplicado);
        Assert.True(calculo.Lineas[0].Exento);
    }
}

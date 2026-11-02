using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Otros conceptos (facturación automática) y factura C en el calculador y el desglose fiscal.</summary>
public sealed class CargaComprobantesConceptosTests
{
    private static CargaComprobantesContexto Contexto() => new()
    { PreciosFinales = true, PreciosFinalesNeto = false, AlicuotaGeneral = 21, Decimales = 2, AjustaRedondeoTotal = true };

    private static CargaComprobantesOrigen Origen(params (decimal Importe, bool NoGravado)[] conceptos) => new()
    {
        Tc = "DIRECTA", Moneda = "1", DatosCalculoDisponibles = true,
        Lineas = conceptos.Select((c, i) => new CargaComprobantesLineaOrigen
        {
            Id = i + 1, Secuencia = i + 1, EsConcepto = true, Descripcion = $"Concepto {i + 1}",
            CantidadOriginal = 1, Pendiente = 1, RenglonesArticulo = 1, PrecioSinIva = c.Importe,
            Precio = decimal.Round(c.Importe * (c.NoGravado ? 1 : 1.21m), 4), AlicuotaIva = c.NoGravado ? 0 : 21, Exento = c.NoGravado
        }).ToList()
    };

    private static List<CargaComprobantesCantidad> Todo(CargaComprobantesOrigen o)
        => o.Lineas.Select(l => new CargaComprobantesCantidad { LineaId = l.Id, Cantidad = 1 }).ToList();

    [Fact]
    public void FacturaC_NoDiscriminaIva_YElNetoGravadoEsElTotal()
    {
        // Caso real: abono 338.986,7025 + adicional en cuotas 169.493,35 (FC 0010-00000519 C).
        var origen = Origen((338986.7025m, false), (0m, false), (169493.35m, false));
        var liq = CargaComprobantesCalculador.Calcular(origen, Contexto(), Todo(origen), letraC: true);
        Assert.Equal(0, liq.Iva);
        Assert.Empty(liq.Alicuotas);
        Assert.Equal(508480.05m, liq.Total);
        var fiscal = CargaComprobantesDesgloseFiscal.Crear(liq);
        Assert.Equal(508480.05m, fiscal.NetoGravado);
        Assert.Equal(0, fiscal.Exento);
        Assert.Equal(fiscal.Total, fiscal.Neto);
    }

    [Fact]
    public void FacturaA_ConceptoGravaAlicuotaGeneral_SinAjustePorPrecioFinal()
    {
        var origen = Origen((162606.6826m, false));
        var liq = CargaComprobantesCalculador.Calcular(origen, Contexto(), Todo(origen));
        Assert.Equal(162606.6826m, liq.NetoGravado);
        Assert.Single(liq.Alicuotas);
        Assert.Equal(21, liq.Alicuotas[0].Alicuota);
        var fiscal = CargaComprobantesDesgloseFiscal.Crear(liq);
        Assert.Equal(fiscal.Total, fiscal.NetoGravado + fiscal.Iva);
        Assert.Equal(196754.09m, fiscal.Total);
    }

    [Fact]
    public void ConceptoNoGravado_VaComoExento()
    {
        var origen = Origen((1000m, false), (250m, true));
        var fiscal = CargaComprobantesDesgloseFiscal.Crear(CargaComprobantesCalculador.Calcular(origen, Contexto(), Todo(origen)));
        Assert.Equal(1000m, fiscal.NetoGravado);
        Assert.Equal(250m, fiscal.Exento);
        Assert.Equal(1460m, fiscal.Total);
    }
}

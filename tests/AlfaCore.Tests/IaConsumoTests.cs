using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Consumo y cobro de IA por créditos (2026-10-05).</summary>
public sealed class IaConsumoTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.0001, 1)]   // un uso mínimo cuenta como 1 crédito
    [InlineData(0.001, 1)]
    [InlineData(0.0015, 2)]
    [InlineData(1.25, 1250)]
    public void Creditos_RedondeaHaciaArriba(decimal costoUsd, long esperado)
        => Assert.Equal(esperado, IaConsumoService.Creditos(costoUsd, 0.001m));

    [Fact]
    public void Creditos_ConValorInvalidoNoCobra()
        => Assert.Equal(0, IaConsumoService.Creditos(5m, 0));

    [Fact]
    public void CalcularCargo_AbonoMasExcedentes()
    {
        var plan = new IaPlanCreditosDto { Precio = 10000m, CreditosIncluidos = 5000, PermiteExcedentes = true, PrecioExcedente = 2.5m };

        Assert.Equal((0L, 10000m), IaConsumoService.CalcularCargo(4000, plan));
        Assert.Equal((1500L, 13750m), IaConsumoService.CalcularCargo(6500, plan));
    }

    [Fact]
    public void CalcularCargo_SinExcedentesPermitidosSoloAbono()
    {
        var plan = new IaPlanCreditosDto { Precio = 10000m, CreditosIncluidos = 100, PermiteExcedentes = false, PrecioExcedente = 2.5m };

        Assert.Equal((900L, 10000m), IaConsumoService.CalcularCargo(1000, plan));
    }

    [Fact]
    public void CalcularCargo_SinPlanNoCobra()
        => Assert.Equal((0L, 0m), IaConsumoService.CalcularCargo(1000, null));

    [Fact]
    public void SumarCostosOpenAi_SumaTodosLosResultadosYDevuelveLaPaginaSiguiente()
    {
        const string json = """
            {"object":"page","data":[
              {"object":"bucket","results":[{"object":"organization.costs.result","amount":{"value":1.25,"currency":"usd"}}]},
              {"object":"bucket","results":[{"amount":{"value":0.75,"currency":"usd"}},{"amount":{"value":0.5,"currency":"usd"}}]},
              {"object":"bucket","results":[]}
            ],"has_more":true,"next_page":"page_abc"}
            """;

        var (total, siguiente) = IaConsumoService.SumarCostosOpenAi(json);

        Assert.Equal(2.5m, total);
        Assert.Equal("page_abc", siguiente);
        Assert.Null(IaConsumoService.SumarCostosOpenAi("""{"data":[],"has_more":false}""").Siguiente);
    }

    [Fact]
    public void RangoUtc_EsElMesCalendarioDeArgentina()
    {
        var (desde, hasta) = IaConsumoService.RangoUtc(2026, 10);

        Assert.Equal(DateTimeKind.Utc, desde.Kind);
        Assert.Equal(new DateTime(2026, 10, 1, 3, 0, 0), DateTime.SpecifyKind(desde, DateTimeKind.Unspecified));
        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0), DateTime.SpecifyKind(hasta, DateTimeKind.Unspecified));
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(80, 100, 80)]
    [InlineData(150, 100, 150)]
    [InlineData(10, 0, 0)]
    public void Porcentaje_DelPlan(long usados, int incluidos, int esperado)
        => Assert.Equal(esperado, ConversacionesConfiguracionAsistenteConsumo.Porcentaje(usados, incluidos));

    [Fact]
    public void NombresDeFuncion_SonLegibles()
    {
        Assert.Equal("Asistente (respuestas automáticas)", IaUsoFunciones.Nombre("bot"));
        Assert.Equal("Búsquedas en archivos", IaUsoFunciones.Nombre(IaUsoFunciones.ArchivosBusqueda));
        Assert.Equal("OTRA", IaUsoFunciones.Nombre("OTRA"));
    }
}

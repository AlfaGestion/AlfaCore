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
    public void CalcularCargo_AbonoMasExcedentesPorCadaMilCreditos()
    {
        // Plan Estándar aprobado: USD 35 + USD 3 cada 1.000 créditos extra.
        var plan = new IaPlanCreditosDto { Precio = 35m, CreditosIncluidos = 20000, PermiteExcedentes = true, PrecioExcedente = 3m };

        Assert.Equal((0L, 35m), IaConsumoService.CalcularCargo(18000, plan));
        Assert.Equal((1500L, 39.5m), IaConsumoService.CalcularCargo(21500, plan));
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

    [Theory]
    [InlineData(1000, 80, 0, false, false, 0)]
    [InlineData(1000, 80, 799, false, false, 79)]
    [InlineData(1000, 80, 800, false, true, 80)]
    [InlineData(1000, 80, 999, false, true, 99)]
    [InlineData(1000, 80, 1000, true, false, 100)]
    [InlineData(1000, 80, 1500, true, false, 150)]
    public void EvaluarTope_MarcaAvisoYAlcanzado(int tope, int aviso, long usados, bool alcanzado, bool enAviso, int porcentaje)
    {
        var estado = IaConsumoService.EvaluarTope("C1", tope, aviso, usados);

        Assert.Equal(alcanzado, estado.Alcanzado);
        Assert.Equal(enAviso, estado.EnAviso);
        Assert.Equal(porcentaje, estado.Porcentaje);
        Assert.Equal("C1", estado.IdCliente);
    }

    [Fact]
    public void Usd_MuestraCuatroDecimalesEnMontosChicos()
    {
        Assert.Equal("0,00", AlfaCore.Components.Pages.AdminConsumoIa.Usd(0m));
        Assert.Equal("0,0005", AlfaCore.Components.Pages.AdminConsumoIa.Usd(0.000454m));
        Assert.Equal("12,35", AlfaCore.Components.Pages.AdminConsumoIa.Usd(12.345m));
    }

    private static IaPlanCreditosDto Plan(int incluidos, bool excedentes)
        => new() { PlanNombre = "P", CreditosIncluidos = incluidos, PermiteExcedentes = excedentes, PrecioExcedente = 3m };

    [Fact]
    public void TopeAutomatico_SinExcedentesCortaEnIncluidos_ConExcedentesUsaElFactor()
    {
        Assert.Equal(3000, IaConsumoService.TopeAutomatico(Plan(3000, excedentes: false), 2m));
        Assert.Equal(40000, IaConsumoService.TopeAutomatico(Plan(20000, excedentes: true), 2m));
        Assert.Equal(30000, IaConsumoService.TopeAutomatico(Plan(20000, excedentes: true), 1.5m));
        Assert.Null(IaConsumoService.TopeAutomatico(Plan(20000, excedentes: true), 0m));
        Assert.Null(IaConsumoService.TopeAutomatico(Plan(0, excedentes: false), 2m));
        Assert.Null(IaConsumoService.TopeAutomatico(null, 2m));
    }

    [Fact]
    public void ResolverTope_ManualPrimaCeroEsSinTopeYSinoElDelPlan()
    {
        var inicial = Plan(3000, excedentes: false);

        var manual = IaConsumoService.ResolverTope("C1", (500, 90), inicial, 2m, 80, 400);
        Assert.Equal(500, manual!.TopeCreditos);
        Assert.Equal(IaTopeOrigenes.Manual, manual.Origen);
        Assert.False(manual.EnAviso);

        Assert.Null(IaConsumoService.ResolverTope("C1", (0, 80), inicial, 2m, 80, 99999));

        var delPlan = IaConsumoService.ResolverTope("C1", null, inicial, 2m, 80, 3000);
        Assert.Equal(3000, delPlan!.TopeCreditos);
        Assert.Equal(IaTopeOrigenes.Plan, delPlan.Origen);
        Assert.True(delPlan.Alcanzado);

        Assert.Null(IaConsumoService.ResolverTope("C1", null, null, 2m, 80, 99999));
    }

    [Fact]
    public void ConfigDesde_UsaValoresPorDefectoAnteClavesFaltantesOInvalidas()
    {
        var vacia = IaConsumoService.ConfigDesde(new Dictionary<string, string>());
        Assert.Equal(0.001m, vacia.UsdPorCredito);
        Assert.Equal(string.Empty, vacia.PlanDefaultCodigo);
        Assert.Equal(2m, vacia.TopeFactorExcedentes);
        Assert.Equal(80, vacia.AvisoPorcentaje);

        var cargada = IaConsumoService.ConfigDesde(new Dictionary<string, string>
        {
            ["USD_POR_CREDITO"] = "0.002",
            ["PLAN_DEFAULT_CODIGO"] = "IA_INICIAL",
            ["TOPE_FACTOR_EXCEDENTES"] = "1.5",
            ["AVISO_PORCENTAJE"] = "abc"
        });
        Assert.Equal(0.002m, cargada.UsdPorCredito);
        Assert.Equal("IA_INICIAL", cargada.PlanDefaultCodigo);
        Assert.Equal(1.5m, cargada.TopeFactorExcedentes);
        Assert.Equal(80, cargada.AvisoPorcentaje);
    }

    [Fact]
    public void PrecioTexto_DescribeAbonoYExcedente()
    {
        Assert.Equal("incluido, sin excedentes", AlfaCore.Components.Pages.ConversacionesConfiguracionAsistenteConsumo.PrecioTexto(
            new IaPlanOpcionDto { Precio = 0, Moneda = "USD", PermiteExcedentes = false }));
        Assert.Equal("USD 35,00 por mes + USD 3,00 cada 1.000 créditos extra", AlfaCore.Components.Pages.ConversacionesConfiguracionAsistenteConsumo.PrecioTexto(
            new IaPlanOpcionDto { Precio = 35, Moneda = "USD", PermiteExcedentes = true, PrecioExcedente = 3 }));
    }
}

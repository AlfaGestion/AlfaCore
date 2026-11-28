using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

public class ArticulosFormulaTests
{
    [Theory]
    [InlineData(1000, EquivalenciaSentido.Ninguna, 0, 1000)]
    [InlineData(1000, EquivalenciaSentido.Dividir, 1000, 1)]       // insumo en KG, renglón en GR (1 KG = 1000 GR)
    [InlineData(12, EquivalenciaSentido.Multiplicar, 12, 144)]     // renglón en caja (1 CJ = 12 UD)
    [InlineData(0, EquivalenciaSentido.Dividir, 1000, 0)]
    [InlineData(500, EquivalenciaSentido.Dividir, 0, 500)]         // coeficiente 0: no convierte (como el legacy)
    public void ValorEnUnidadConvierteComoRtStock(decimal valor, EquivalenciaSentido sentido, decimal coef, decimal esperado)
        => Assert.Equal(esperado, ArticuloFormulaCalculo.ValorEnUnidad(valor, sentido, coef));

    [Theory]
    [InlineData(30, 0, 1200, 600)]   // media hora a $1200 la hora
    [InlineData(1, 30, 3600, 90)]    // 1,5 minutos
    [InlineData(0, 0, 5000, 0)]
    public void CostoTareaEsHorasPorValorHora(decimal minutos, int segundos, decimal valorHora, decimal esperado)
        => Assert.Equal(esperado, ArticuloFormulaCalculo.CostoTarea(minutos, segundos, valorHora));

    [Fact]
    public void CostoTotalSumaInsumosYTareas()
    {
        var f = new ArticuloFormulaDto
        {
            Insumos =
            [
                new() { Codigo = "INS001", Cantidad = 2, CostoUnitario = 1800 },   // medallón
                new() { Codigo = "INS004", Cantidad = 0.18m, CostoUnitario = 2500 } // papas por kg
            ],
            Tareas = [new() { IdTarea = "T1", Minutos = 10, ValorHora = 1200 }]
        };
        Assert.Equal(3600 + 450 + 200, f.CostoInsumos);
    }

    [Fact]
    public void PorPorcentajeLosInsumosNoSumanCosto()
    {
        var f = new ArticuloFormulaDto
        {
            PorPorcentaje = true,
            Insumos = [new() { Codigo = "A", Cantidad = 60, CostoUnitario = 10 }, new() { Codigo = "B", Cantidad = 40, CostoUnitario = 10 }],
            Tareas = [new() { IdTarea = "T1", Minutos = 60, ValorHora = 100 }]
        };
        Assert.Equal(100, f.CostoInsumos);
        Assert.Empty(ArticuloFormulaCalculo.Validar(f));
    }

    [Fact]
    public void ValidarDetectaPorcentajesRepetidosYCantidades()
    {
        var f = new ArticuloFormulaDto
        {
            PorPorcentaje = true,
            Insumos = [new() { Codigo = "A", Cantidad = 60 }, new() { Codigo = "a", Cantidad = 30 }, new() { Codigo = "C", Cantidad = 0, ClasePr = 9 }],
            Tareas = [new() { IdTarea = "T1", Segundos = 75 }]
        };
        var errores = ArticuloFormulaCalculo.Validar(f);
        Assert.Contains(errores, e => e.Contains("repetido"));
        Assert.Contains(errores, e => e.Contains("sumar 100"));
        Assert.Contains(errores, e => e.Contains("mayor a 0"));
        Assert.Contains(errores, e => e.Contains("clase de precio"));
        Assert.Contains(errores, e => e.Contains("segundos"));
    }

    [Fact]
    public void BuscarCicloEncuentraCicloIndirecto()
    {
        // HAMB usa PAN; PAN usa MASA; MASA usaría HAMB -> ciclo.
        var aristas = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["PAN"] = ["MASA"],
            ["MASA"] = ["HAMB", "HARINA"],
            ["HARINA"] = []
        };
        var ciclo = ArticuloFormulaCalculo.BuscarCiclo("HAMB", ["PAN", "CHEDDAR"], a => aristas.GetValueOrDefault(a) ?? []);
        Assert.Equal(["HAMB", "PAN", "MASA", "HAMB"], ciclo);
    }

    [Fact]
    public void BuscarCicloSinCicloDevuelveNull()
    {
        var aristas = new Dictionary<string, string[]> { ["PAN"] = ["HARINA"], ["SALSA"] = ["HARINA"] };
        Assert.Null(ArticuloFormulaCalculo.BuscarCiclo("HAMB", ["PAN", "SALSA"], a => aristas.GetValueOrDefault(a) ?? []));
    }

    [Fact]
    public void BuscarCicloDetectaAutoreferencia()
        => Assert.NotNull(ArticuloFormulaCalculo.BuscarCiclo("HAMB", ["hamb"], _ => []));

    // Cadena: HARINA -> MASA -> PAN -> HAMB, y HAMB también usa CHEDDAR. COMBO usa HAMB y PAN.
    private static readonly Dictionary<string, string[]> Usan = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HARINA"] = ["MASA"],
        ["MASA"] = ["PAN"],
        ["PAN"] = ["HAMB", "COMBO"],
        ["HAMB"] = ["COMBO"],
        ["CHEDDAR"] = ["HAMB"]
    };

    [Fact]
    public void OrdenCascadaRecorreTodaLaCadenaUnaVezYEnOrden()
    {
        var (orden, ciclo) = ArticuloFormulaCalculo.OrdenCascada(["HARINA"], a => Usan.GetValueOrDefault(a) ?? [], _ => true);
        Assert.Equal(["MASA", "PAN", "HAMB", "COMBO"], orden);
        Assert.Empty(ciclo);
    }

    [Fact]
    public void OrdenCascadaSeCortaEnProductosSinCostoAutomatico()
    {
        var (orden, _) = ArticuloFormulaCalculo.OrdenCascada(["HARINA"], a => Usan.GetValueOrDefault(a) ?? [], a => a != "PAN");
        Assert.Equal(["MASA"], orden);
    }

    [Fact]
    public void OrdenCascadaConVariosOrigenesNoRepite()
    {
        var (orden, _) = ArticuloFormulaCalculo.OrdenCascada(["CHEDDAR", "PAN"], a => Usan.GetValueOrDefault(a) ?? [], _ => true);
        Assert.Equal(["HAMB", "COMBO"], orden);
    }

    [Fact]
    public void OrdenCascadaInformaCiclosSinColgarse()
    {
        var conCiclo = new Dictionary<string, string[]> { ["X"] = ["A"], ["A"] = ["B"], ["B"] = ["A", "C"] };
        var (orden, ciclo) = ArticuloFormulaCalculo.OrdenCascada(["X"], a => conCiclo.GetValueOrDefault(a) ?? [], _ => true);
        Assert.Empty(orden);
        Assert.Equal(["A", "B", "C"], ciclo);
    }

    [Theory]
    [InlineData(120.0, 100.0, 120.0)]   // el borrador trae precio: se aplica
    [InlineData(0.0, 100.0, 100.0)]     // 0 en el borrador (fila del legacy con solo PRECIO4): se conserva el actual
    [InlineData(null, 100.0, 100.0)]  // vacío: se conserva
    public void BorradorNoPisaPreciosConCero(double? borrador, double actual, double esperado)
        => Assert.Equal((decimal)esperado, PrecioBorradorCalculo.ValorAplicado(borrador is null ? null : (decimal)borrador, (decimal)actual));
}

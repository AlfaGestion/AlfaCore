using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

public sealed class PosIngresoRapidoTests
{
    [Theory]
    [InlineData("1,25", 1.25)]
    [InlineData("1.25", 1.25)]
    public void ParseAceptaComaYPunto(string texto, decimal esperado)
        => Assert.Equal(esperado, PosIngresoRapidoCalculador.TryParse(texto, out var valor) ? valor : -1m);

    [Fact]
    public void ImporteCalculaCantidadSinModificarPrecio()
    {
        var ok = PosIngresoRapidoCalculador.TryCalcularCantidad(
            new(PosIngresoRapidoModo.Importe, 2000m), 8000m, out var cantidad, out _);

        Assert.True(ok);
        Assert.Equal(0.25m, cantidad);
    }

    [Fact]
    public void CantidadFraccionariaSeAceptaAunquePesableSeaFalso()
    {
        var ok = PosIngresoRapidoCalculador.TryCalcularCantidad(
            new(PosIngresoRapidoModo.Cantidad, 0.25m), 100m, out var cantidad, out var error);

        Assert.True(ok, error);
        Assert.Equal(0.25m, cantidad);
    }

    [Fact]
    public void ImporteConPrecioCeroSeRechaza()
    {
        var ok = PosIngresoRapidoCalculador.TryCalcularCantidad(
            new(PosIngresoRapidoModo.Importe, 100m), 0m, out _, out var error);

        Assert.False(ok);
        Assert.Contains("precio unitario", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CantidadEnteraSeAcepta()
    {
        var ok = PosIngresoRapidoCalculador.TryCalcularCantidad(
            new(PosIngresoRapidoModo.Cantidad, 12m), 100m, out var cantidad, out _);

        Assert.True(ok);
        Assert.Equal(12m, cantidad);
    }

    [Theory]
    [InlineData(12, 600, 12)]
    [InlineData(6, 800, 6)]
    [InlineData(0.25, 8000, 0.25)]
    [InlineData(200, 4000, 0.05)]
    [InlineData(2000, 8000, 0.25)]
    public void CasosRealesDeIngresoRapidoCalculanLaCantidadEsperada(
        decimal valor,
        decimal precio,
        decimal esperado)
    {
        var modo = valor is 200m or 2000m
            ? PosIngresoRapidoModo.Importe
            : PosIngresoRapidoModo.Cantidad;

        var ok = PosIngresoRapidoCalculador.TryCalcularCantidad(
            new(modo, valor), precio, out var cantidad, out var error);

        Assert.True(ok, error);
        Assert.Equal(esperado, cantidad);
    }

    [Fact]
    public void ImporteDeArticuloPorKiloAceptaCantidadFraccionaria()
    {
        var ok = PosIngresoRapidoCalculador.TryCalcularCantidad(
            new(PosIngresoRapidoModo.Importe, 200m), 4000m, out var cantidad, out var error);

        Assert.True(ok, error);
        Assert.Equal(0.05m, cantidad);
    }
}

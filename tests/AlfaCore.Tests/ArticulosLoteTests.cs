using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public class ArticulosLoteTests
{
    [Theory]
    [InlineData(1000, 10, 0, 1100)]
    [InlineData(1000, -5, 0, 950)]
    [InlineData(3800, 7.5, 10, 4090)]   // 4085 -> redondea a 10
    [InlineData(3800, 7.5, 100, 4100)]
    [InlineData(123.45, 10, 0, 135.80)] // 135.795 -> 135.80
    [InlineData(0, 50, 0, 0)]          // una clase sin precio sigue en 0
    public void AumentarAplicaPorcentajeYRedondeo(decimal valor, decimal porcentaje, decimal paso, decimal esperado)
        => Assert.Equal(esperado, ArticuloLoteCalculo.Aumentar(valor, porcentaje, paso));

    [Theory]
    [InlineData(1000, 50, 0, 0, 1500)]       // maestro sin IVA
    [InlineData(1000, 50, 21, 0, 1815)]      // maestro con IVA 21
    [InlineData(1500, 150, 10.5, 10, 4140)]  // 4143.75 -> 4140
    public void PrecioDesdeUtilidadIgualQueElEditor(decimal costo, decimal utilidad, decimal iva, decimal paso, decimal esperado)
        => Assert.Equal(esperado, ArticuloLoteCalculo.PrecioDesdeUtilidad(costo, utilidad, iva, paso));
}

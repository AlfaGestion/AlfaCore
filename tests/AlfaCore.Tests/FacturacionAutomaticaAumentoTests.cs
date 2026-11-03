using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Cálculo del aumento/redondeo de importes de facturación automática.</summary>
public sealed class FacturacionAutomaticaAumentoTests
{
    private static decimal Calc(decimal importe, bool porc, decimal valor, decimal paso, bool conIva, decimal alic)
        => CuentasComercialesService.CalcularImporteAumento(importe, porc, valor, paso, conIva, alic);

    [Fact]
    public void PorcentajeSinRedondeoComoElVb6() => Assert.Equal(110000m, Calc(100000m, true, 10, 0, true, 21));

    [Fact]
    public void ReduccionConPorcentajeNegativo() => Assert.Equal(95000m, Calc(100000m, true, -5, 0, true, 21));

    [Fact]
    public void RedondeoDelTotalConIvaDejaElTotalRedondo()
    {
        var neto = Calc(162606.68m, true, 7.5m, 100, true, 21);
        Assert.Equal(0m, Math.Round(neto * 1.21m, 2) % 100);
    }

    [Theory]
    [InlineData(1234.37, 0.25, 1234.25)]
    [InlineData(1234.38, 0.25, 1234.50)]
    [InlineData(1234.49, 0.5, 1234.50)]
    [InlineData(1234.49, 1, 1234)]
    [InlineData(1235, 10, 1240)]
    [InlineData(1249.99, 100, 1200)]
    [InlineData(1500, 1000, 2000)]
    public void RedondeoDelNetoAPasos(decimal importe, decimal paso, decimal esperado)
        => Assert.Equal(esperado, Calc(importe, true, 0, paso, false, 21));

    [Fact]
    public void SinIvaElRedondeoConIvaRedondeaElNeto() => Assert.Equal(230800m, Calc(230818.81m, true, 0, 100, true, 0));

    [Fact]
    public void ImporteFijoReemplazaElValor() => Assert.Equal(150000m, Calc(99m, false, 150000, 0, true, 21));
}

/// <summary>Contra ALFANET2007_PRUEBAS: simula para todos y aplica a un cliente, después restaura sus importes.
/// Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class FacturacionAutomaticaAumentoPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task SimulaParaTodosYAplicaAUnClienteGuardandoElImporteAnterior()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var svc = new CuentasComercialesService(runtime.Configuration, runtime.Sessions, runtime.Events,
            DispatchProxy.Create<ICuentasComercialesValidator, CargaComprobantesUiTests.EventOnlyProxy>());

        var todos = await svc.AumentarFacturacionAutomaticaAsync(new FacturacionAutomaticaAumentoPedido { Porcentaje = true, Valor = 10, Redondeo = 100 }, aplicar: false);
        output.WriteLine($"Todos +10% redondeo 100: {todos.Renglones} renglones de {todos.Clientes} clientes · {todos.TotalAntes:N2} → {todos.TotalDespues:N2} · omitidos {todos.Omitidos}");
        foreach (var f in todos.Muestra.Take(5)) output.WriteLine($"  {f.Cuenta} {f.Detalle}: {f.Antes:N2} → {f.Despues:N4} (c/IVA {f.TotalConIvaDespues:N2}, alic {f.Alicuota})");
        Assert.False(todos.Aplicado);
        Assert.True(todos.Renglones > 0);
        Assert.All(todos.Muestra.Where(f => f.Alicuota > 0), f => Assert.Equal(0m, f.TotalConIvaDespues % 100));

        var cuenta = todos.Muestra[0].Cuenta;
        var antes = (await cn.QueryAsync<(int Id, decimal Importe, decimal? Old, DateTime? Fecha)>(
            "SELECT ID, Importe, ImporteOld, FechaCambio FROM dbo.MA_CUENTAS_AUTOCPTESDET WHERE Cuenta=@cuenta", new { cuenta })).ToList();
        try
        {
            var r = await svc.AumentarFacturacionAutomaticaAsync(new FacturacionAutomaticaAumentoPedido { Codigos = [cuenta], Porcentaje = true, Valor = -5, Redondeo = 1, RedondearConIva = false }, aplicar: true);
            output.WriteLine($"Aplicado -5% redondeo $1 a {cuenta}: {r.Renglones} renglones {r.TotalAntes:N2} → {r.TotalDespues:N2}");
            Assert.True(r.Aplicado);
            var despues = (await cn.QueryAsync<(int Id, decimal Importe, decimal? Old)>(
                "SELECT ID, Importe, ImporteOld FROM dbo.MA_CUENTAS_AUTOCPTESDET WHERE Cuenta=@cuenta AND Importe<>0", new { cuenta })).ToList();
            Assert.All(despues.Where(d => antes.Any(a => a.Id == d.Id && a.Importe != d.Importe)), d =>
            {
                Assert.Equal(antes.Single(a => a.Id == d.Id).Importe, d.Old);
                Assert.Equal(0m, d.Importe % 1);
            });
        }
        finally
        {
            foreach (var a in antes)
                await cn.ExecuteAsync("UPDATE dbo.MA_CUENTAS_AUTOCPTESDET SET Importe=@Importe, ImporteOld=@Old, FechaCambio=@Fecha WHERE ID=@Id",
                    new { a.Id, a.Importe, a.Old, a.Fecha });
        }
    }
}

using System.Reflection;
using AlfaCore.Components.Shared;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AlfaCore.Tests;

// Módulo Cobranzas: recibo CB sin movimiento bancario, con medios cargados por el usuario.
public sealed class CobranzaDirectaTests
{
    [Fact]
    public void LaCobranzaDirectaAdmiteMediosPorEfectivoYBancoSinMovimientoNiAjuste()
    {
        var directa=new CobranzaSolicitud(Guid.NewGuid(),1,new(2026,11,16),0,[new("111010001",600m),new("BANCO",568.20m)]);
        Assert.Null(directa.MovimientoId);
        Assert.Equal(1168.20m,CobranzasService.ValidarImportes(directa));
        // El ajuste de centavos requiere movimiento bancario: no existe en la cobranza directa.
        Assert.Throws<InvalidOperationException>(()=>CobranzasService.ValidarImportes(directa with{Ajuste=new("512",0.5m,"redondeo")}));
    }

    [Theory]
    [InlineData("1248",1248)]
    [InlineData("1248,5",1248.5)]
    [InlineData("1.248,50",1248.5)]
    [InlineData("1248.50",1248.5)]
    [InlineData("$ 72.000,00",72000)]
    public void ElImporteDelMedioAceptaFormatoArgentinoYConPunto(string texto,decimal esperado)
    {
        Assert.True(CobranzaRapidaDialog.TryImporte(texto,out var importe));
        Assert.Equal(esperado,importe);
        Assert.False(CobranzaRapidaDialog.TryImporte("abc",out _));
    }

    [Fact]
    public void ElPermisoDelModuloEsPropioYNoElDeConciliacion()
    {
        Assert.Equal("D6003",CobranzasService.MenuKey);
        Assert.NotEqual(ConciliacionBancariaService.MenuKey,CobranzasService.MenuKey);
    }

    [Fact]
    public async Task EditorDirectoPrecargaElPendienteYOfreceEfectivoSinPanelDeBanco()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime,CargaComprobantesUiTests.EventOnlyProxy>());
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        Task<string> Render(IReadOnlyList<int>? preseleccion)=>renderer.Dispatcher.InvokeAsync(async()=>
        {
            var prep=new CobranzaPreparacion {Fecha=new(2026,11,16),SaldoAl=new(2026,11,16),
                SaldosCliente=[new(){Cliente="112010854",Moneda="$",Saldo=1168200m}],
                Documentos=[new(){Id=1,Cliente="112010854",Nombre="DANLOVIC S. R. L.",Unidad="2",Tc="FC",Comprobante="000200003860A",Moneda="1",Saldo=1168200m}],
                Medios=[new(){Cuenta="111010001",Descripcion="Caja Efectivo Pesos.",Tipo="EF",Moneda="1"},
                    new(){Cuenta="111",Descripcion="BANCO ENSAYO",Tipo="DB",Moneda="1"}],
                Talonarios=[new(){Talonario=0,Unidad="2",Sucursal=1}],
                Cliente="112010854",NombreCliente="DANLOVIC S. R. L.",Unidad="2"};
            var root=await renderer.RenderComponentAsync<CobranzaRapidaDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Preparacion"]=prep,["Directo"]=true,["Preseleccion"]=preseleccion}));
            return System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
        });
        // Con el comprobante tildado en el listado, el medio acompaña su pendiente.
        var html=await Render([1]);
        Assert.Contains("2. Medios de pago",html);
        Assert.Contains("Medio seleccionado",html);
        Assert.Contains("Caja Efectivo Pesos.",html);
        Assert.Contains("1168200,00",html);          // importe precargado con el faltante
        Assert.DoesNotContain("Recibido en el banco",html);
        Assert.DoesNotContain("Agregar ajuste de centavos",html);
        Assert.Contains("A cuenta (anticipo)",html);
        // «Nueva cobranza» sin nada tildado: no se marca ninguna factura (puede ser un anticipo).
        var nueva=await Render(null);
        Assert.Contains("o ninguna si es un anticipo",nueva);
        Assert.Contains("0 factura(s)",nueva);
        Assert.DoesNotContain("1168200,00",nueva);
    }

    [Fact]
    public async Task EditorDirectoSinPendientesPermiteCobrarACuenta()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime,CargaComprobantesUiTests.EventOnlyProxy>());
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var prep=new CobranzaPreparacion {Fecha=new(2026,11,26),SaldoAl=new(2026,11,26),Documentos=[],
                Medios=[new(){Cuenta="111010001",Descripcion="Caja Efectivo Pesos.",Tipo="EF",Moneda="1"}],
                Talonarios=[new(){Talonario=0,Unidad="",Sucursal=1}],Cliente="112010087",NombreCliente="LACTEOS FERNYBELL S.R.L.",Unidad="1"};
            var root=await renderer.RenderComponentAsync<CobranzaRapidaDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Preparacion"]=prep,["Directo"]=true}));
            return System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
        });
        Assert.Contains("LACTEOS FERNYBELL S.R.L.",html);
        Assert.Contains("Cliente 112010087 · Unidad 1",html);
        Assert.Contains("no tiene comprobantes pendientes",html);
        Assert.Contains("A cuenta (anticipo)",html);
    }

    [Fact]
    public async Task EditorDirectoPreseleccionaLosComprobantesTildadosYMuestraFechaYConcepto()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(DispatchProxy.Create<Microsoft.JSInterop.IJSRuntime,CargaComprobantesUiTests.EventOnlyProxy>());
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var prep=new CobranzaPreparacion {Fecha=new(2026,11,16),SaldoAl=new(2026,11,16),
                Documentos=[
                    new(){Id=1,Cliente="112010854",Nombre="DANLOVIC S. R. L.",Unidad="2",Tc="FC",Comprobante="000200003860A",Moneda="1",Saldo=100m,Fecha=new(2026,10,1)},
                    new(){Id=2,Cliente="112010854",Nombre="DANLOVIC S. R. L.",Unidad="2",Tc="FC",Comprobante="000200003861A",Moneda="1",Saldo=250m,Fecha=new(2026,10,5)},
                    new(){Id=3,Cliente="112010854",Nombre="DANLOVIC S. R. L.",Unidad="2",Tc="FC",Comprobante="000200003862A",Moneda="1",Saldo=400m,Fecha=new(2026,10,9)}],
                Medios=[new(){Cuenta="111010001",Descripcion="Caja Efectivo Pesos.",Tipo="EF",Moneda="1"}],
                Talonarios=[new(){Talonario=0,Unidad="2",Sucursal=1}]};
            var root=await renderer.RenderComponentAsync<CobranzaRapidaDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Preparacion"]=prep,["Directo"]=true,["Preseleccion"]=new List<int>{2,3}}));
            return System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
        });
        Assert.Contains("2 factura(s)",html);          // tildadas 2 y 3; la 1 queda disponible para sumarla
        Assert.Contains("650,00",html);                  // 250 + 400
        Assert.Contains("0002-00003861 A",html);
        Assert.Contains("Fecha de cobranza",html);
        Assert.Contains("Concepto / observación",html);
        Assert.Contains("<details class=\"cobranza-editor__opciones\" open",html);
    }
}

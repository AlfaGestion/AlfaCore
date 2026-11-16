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
        var html=await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var prep=new CobranzaPreparacion {Fecha=new(2026,11,16),SaldoAl=new(2026,11,16),
                SaldosCliente=[new(){Cliente="112010854",Moneda="$",Saldo=1168200m}],
                Documentos=[new(){Id=1,Cliente="112010854",Nombre="DANLOVIC S. R. L.",Unidad="2",Tc="FC",Comprobante="000200003860A",Moneda="1",Saldo=1168200m}],
                Medios=[new(){Cuenta="111010001",Descripcion="Caja Efectivo Pesos.",Tipo="EF",Moneda="1"},
                    new(){Cuenta="111",Descripcion="BANCO ENSAYO",Tipo="DB",Moneda="1"}],
                Talonarios=[new(){Talonario=0,Unidad="2",Sucursal=1}]};
            var root=await renderer.RenderComponentAsync<CobranzaRapidaDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Preparacion"]=prep,["Directo"]=true}));
            return System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
        });
        Assert.Contains("2. Medios de pago",html);
        Assert.Contains("Agregar medio de pago",html);
        Assert.Contains("Caja Efectivo Pesos.",html);
        Assert.Contains("1168200.00",html);
        Assert.DoesNotContain("Recibido en el banco",html);
        Assert.DoesNotContain("Agregar ajuste de centavos",html);
    }
}

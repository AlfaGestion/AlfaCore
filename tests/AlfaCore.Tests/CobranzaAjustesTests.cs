using System.Reflection;
using System.Text.Json;
using AlfaCore.Components.Shared;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CobranzaAjustesTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    [Fact]
    public void ValidaAjustesDeAmbosSignosSinCambiarPagosYConservaContratoAnterior()
    {
        var solicitud=new CobranzaSolicitud(Guid.NewGuid(),1,new(2026,10,27),0,[new("BANCO",1000000)],52);
        Assert.DoesNotContain("Ajuste",JsonSerializer.Serialize(solicitud));
        Assert.Equal(1000000.39m,CobranzasService.ValidarImportes(solicitud with{Ajuste=new("GASTOS",.39m,"Redondeo") }));
        Assert.Equal(999999.61m,CobranzasService.ValidarImportes(solicitud with{Ajuste=new("GASTOS",-.39m,"Redondeo") }));
        foreach(var ajuste in new[]{new CobranzaAjuste("",.39m,"Motivo"),new("GASTOS",0,"Motivo"),new("GASTOS",100.01m,"Motivo"),new("GASTOS",.001m,"Motivo"),new("GASTOS",.39m,"")})
            Assert.Throws<InvalidOperationException>(()=>CobranzasService.ValidarImportes(solicitud with{Ajuste=ajuste}));
        Assert.Throws<InvalidOperationException>(()=>CobranzasService.ValidarImportes(solicitud with{MovimientoId=null,Ajuste=new("GASTOS",.39m,"Motivo")}));
        Assert.Throws<InvalidOperationException>(()=>CobranzasService.ValidarImportes(solicitud with{Ajuste=new("GASTOS",.39m,"Redondeo"),Aplicaciones=[new(1,1000000)]}));
    }

    [Theory]
    [InlineData("0.39")]
    [InlineData("-0.39")]
    public async Task AjusteEsExplicitoCompletaFacturaYExigeElegirCuenta(string texto)
    {
        var delta=decimal.Parse(texto,System.Globalization.CultureInfo.InvariantCulture);
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(DispatchProxy.Create<IJSRuntime,CargaComprobantesUiTests.EventOnlyProxy>());
        var activator=new Captura();services.AddSingleton<IComponentActivator>(activator);
        using var provider=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            CobranzaSolicitud? enviada=null;
            var prep=new CobranzaPreparacion{MovimientoId=52,Fecha=new(2026,10,27),CuentaBanco="BANCO",ImporteBanco=1000000,
                Documentos=[new(){Id=1,Cliente="CLIENTE",Nombre="Cliente de ensayo",Unidad="1",Tc="FC",Comprobante="000100000001A",Moneda="1",Saldo=1000000+delta}],
                Medios=[new(){Cuenta="BANCO",Descripcion="Banco",Tipo="DB",Moneda="1"}],Talonarios=[new(){Talonario=1,Unidad="1",Sucursal=1}],
                CuentasAjuste=[new("GASTOS","Diferencias por redondeo")],LimiteAjuste=1};
            var root=await renderer.RenderComponentAsync<CobranzaRapidaDialog>(ParameterView.FromDictionary(new Dictionary<string,object?>
            { ["Preparacion"]=prep,["OnConfirm"]=EventCallback.Factory.Create<CobranzaSolicitud>(new object(),(CobranzaSolicitud s)=>enviada=s)}));
            var dialog=activator.Dialog!;
            void Llamar(string nombre)=>typeof(CobranzaRapidaDialog).GetMethod(nombre,Flags)!.Invoke(dialog,null);
            Assert.False((bool)typeof(CobranzaRapidaDialog).GetField("_ajusteActivo",Flags)!.GetValue(dialog)!);
            Llamar("AgregarAjuste");
            await (Task)typeof(CobranzaRapidaDialog).GetMethod("Confirmar",Flags)!.Invoke(dialog,null)!;
            Assert.Null(enviada); // la primera vez no se inventa una cuenta
            typeof(CobranzaRapidaDialog).GetField("_cuentaAjuste",Flags)!.SetValue(dialog,"GASTOS");
            await (Task)typeof(CobranzaRapidaDialog).GetMethod("Confirmar",Flags)!.Invoke(dialog,null)!;
            Assert.NotNull(enviada);Assert.Equal(delta,enviada.Ajuste!.Importe);
            Assert.Equal("GASTOS",enviada.Ajuste.Cuenta);Assert.True(enviada.Ajuste.RecordarCuenta);
            Assert.Equal(1000000,Assert.Single(enviada.Pagos).Importe);
            Assert.Equal(1000000+delta,CobranzasService.ValidarImportes(enviada));
            Llamar("QuitarAjuste");
            Assert.False((bool)typeof(CobranzaRapidaDialog).GetField("_ajusteActivo",Flags)!.GetValue(dialog)!);
        });
    }
    private sealed class Captura:IComponentActivator
    {
        public CobranzaRapidaDialog? Dialog{get;private set;}
        public IComponent CreateInstance(Type type){var c=(IComponent)Activator.CreateInstance(type)!;if(c is CobranzaRapidaDialog d)Dialog=d;return c;}
    }
}

using System.Reflection;
using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasSeleccionUiTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private static ConciliacionTarjetas Pantalla(int cantidad=75)
    {
        var page=new ConciliacionTarjetas();
        typeof(ConciliacionTarjetas).GetProperty("PageHeader",Flags)!.SetValue(page,new PageHeaderService());
        Set(page,"_tab","conciliar");
        Set(page,"_comparacion",new TarjetasComparacion(new(){DetalleCompleto=true},
            Enumerable.Range(1,cantidad).Select(i=>new TarjetasDetalle{Id=i,Fecha=new(2026,4,1),Importe=10,Vinculo=i==2?9:null}).ToList(),
            Enumerable.Range(1,cantidad).Select(i=>new TarjetasMovimiento{Clave=i.ToString(),Fecha=new(2026,4,1),Importe=10,LegacyLiquidado=i==1,Vinculo=i==2?9:null,Alterado=i==3}).ToList(),1));
        return page;
    }
    private static void Set(object page,string field,object value)=>page.GetType().GetField(field,Flags)!.SetValue(page,value);
    private static T Field<T>(object page,string field)=>(T)page.GetType().GetField(field,Flags)!.GetValue(page)!;
    private static T Property<T>(object page,string property)=>(T)page.GetType().GetProperty(property,Flags)!.GetValue(page)!;
    private static void Call(object page,string method,params object[] args)=>page.GetType().GetMethod(method,Flags)!.Invoke(page,args);

    [Fact]
    public void SeleccionarTodoIncluyeOtrasPaginasYExcluyeConciliadosLegacyYAlterados()
    {
        var page=Pantalla();
        Call(page,"SeleccionarResumen",false,false);Call(page,"SeleccionarAlfa",false,false);
        Assert.Equal(74,Field<HashSet<long>>(page,"_detalles").Count);
        Assert.Equal(72,Field<HashSet<string>>(page,"_movimientos").Count);
        Assert.DoesNotContain(2L,Field<HashSet<long>>(page,"_detalles"));
        foreach(var key in new[]{"1","2","3"})Assert.DoesNotContain(key,Field<HashSet<string>>(page,"_movimientos"));
        Call(page,"SeleccionarResumen",false,true);
        Assert.Empty(Field<HashSet<long>>(page,"_detalles"));
        Assert.Equal(72,Field<HashSet<string>>(page,"_movimientos").Count);
        Call(page,"LimpiarLado",false);Assert.Empty(Field<HashSet<string>>(page,"_movimientos"));
    }
    [Fact]
    public void CasillaDelEncabezadoAfectaSoloLaPaginaYReflejaSeleccionParcial()
    {
        var page=Pantalla();
        Call(page,"CambiarPaginaResumen",true);
        Assert.Equal(49,Field<HashSet<long>>(page,"_detalles").Count);
        Assert.True(Property<bool?>(page,"EstadoPaginaResumen"));
        Field<HashSet<long>>(page,"_detalles").Remove(1);
        Assert.Null(Property<bool?>(page,"EstadoPaginaResumen"));
        Set(page,"_paginaResumen",2);Call(page,"CambiarPaginaResumen",true);
        Assert.Equal(73,Field<HashSet<long>>(page,"_detalles").Count);
        Call(page,"CambiarPaginaResumen",false);
        Assert.Equal(48,Field<HashSet<long>>(page,"_detalles").Count);
    }
    [Fact]
    public void VistaSeleccionadaYPaginacionConservanLaSeleccionAlCambiarTamano()
    {
        var page=Pantalla();Call(page,"SeleccionarResumen",false,false);
        Call(page,"VerSoloSeleccion",true);Set(page,"_paginaResumen",2);
        Call(page,"CambiarTamano","100");
        Assert.Equal(1,Field<int>(page,"_paginaResumen"));
        Assert.Equal(74,Property<IEnumerable<TarjetasDetalle>>(page,"PaginaResumen").Count());
        Assert.Equal(74,Field<HashSet<long>>(page,"_detalles").Count);
        Call(page,"LimpiarLado",true);
        Assert.Empty(Property<IEnumerable<TarjetasDetalle>>(page,"DetallesVisibles"));
        Assert.Equal(1,Property<int>(page,"PaginasResumen"));
    }
    [Fact]
    public void LimiteMasivoConservaLaSeleccionYPermiteTrabajarPorPagina()
    {
        var page=Pantalla(2002);Call(page,"CambiarPaginaResumen",true);
        var anteriores=Field<HashSet<long>>(page,"_detalles").ToHashSet();
        Call(page,"SeleccionarResumen",false,false);
        Assert.True(anteriores.SetEquals(Field<HashSet<long>>(page,"_detalles")));
        Assert.NotNull(Field<AppUiMessage>(page,"_feedback"));
    }
}

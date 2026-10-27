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
    [Fact]
    public void FiltrosLocalesSonIndependientesYNoModificanConsultaGeneral()
    {
        var page=Pantalla();
        var c=Field<TarjetasComparacion>(page,"_comparacion");
        c.Detalles[0].Fecha=new(2026,4,2,15,30,0);
        c.Movimientos[3].Fecha=new(2026,4,2,9,0,0);c.Movimientos[3].Sucursal="0002 ";
        c.Movimientos[4].Fecha=new(2026,4,2);c.Movimientos[4].Sucursal="0001";
        var general=Field<TarjetasBusqueda>(page,"_filtro");
        general.FechaDesde=new(2026,4,1);general.FechaHasta=new(2026,4,30);general.Sucursal="";
        Set(page,"_paginaResumen",2);Set(page,"_paginaAlfa",2);
        Call(page,"FiltrarFechaResumen","2026-04-02");
        Assert.Single(Property<IEnumerable<TarjetasDetalle>>(page,"DetallesVisibles"));
        Assert.Equal(75,Property<IEnumerable<TarjetasMovimiento>>(page,"MovimientosVisibles").Count());
        Assert.Equal(2,Field<int>(page,"_paginaAlfa"));
        Call(page,"FiltrarFechaAlfa","2026-04-02");Call(page,"FiltrarSucursalAlfa","0002");
        Assert.Equal("4",Assert.Single(Property<IEnumerable<TarjetasMovimiento>>(page,"MovimientosVisibles")).Clave);
        Assert.Equal(1,Field<int>(page,"_paginaAlfa"));
        Assert.Equal(new DateTime(2026,4,1),general.FechaDesde);Assert.Equal(new DateTime(2026,4,30),general.FechaHasta);Assert.Equal("",general.Sucursal);
        Assert.Equal(3,Property<IReadOnlyList<AlfaCore.Components.Shared.AlfaDesign.AlfaSelectOption>>(page,"FechasAlfa").Count);
        Call(page,"FiltrarSucursalAlfa","");
        Assert.Equal(2,Property<IEnumerable<TarjetasMovimiento>>(page,"MovimientosVisibles").Count());
    }
    [Fact]
    public void SeleccionMasivaRespetaFiltrosLocalesYConservaMarcasOcultas()
    {
        var page=Pantalla();var c=Field<TarjetasComparacion>(page,"_comparacion");
        c.Detalles[0].Fecha=new(2026,4,2);c.Movimientos[3].Sucursal="0002";
        Call(page,"FiltrarFechaResumen","2026-04-02");Call(page,"FiltrarSucursalAlfa","0002");
        Call(page,"SeleccionarResumen",false,false);Call(page,"SeleccionarAlfa",false,false);
        Assert.Equal(1L,Assert.Single(Field<HashSet<long>>(page,"_detalles")));
        Assert.Equal("4",Assert.Single(Field<HashSet<string>>(page,"_movimientos")));
        Call(page,"FiltrarFechaResumen","2026-04-01");Call(page,"FiltrarSucursalAlfa","__sin_sucursal__");
        Assert.Equal(1,Property<int>(page,"SeleccionOcultaResumen"));Assert.Equal(1,Property<int>(page,"SeleccionOcultaAlfa"));
        Assert.Equal(10m,Property<decimal>(page,"TotalResumen"));Assert.Equal(10m,Property<decimal>(page,"TotalAlfa"));
        Call(page,"SeleccionarResumen",false,true);Call(page,"SeleccionarAlfa",false,true);
        Assert.Equal(74,Field<HashSet<long>>(page,"_detalles").Count);Assert.Equal(72,Field<HashSet<string>>(page,"_movimientos").Count);
        Call(page,"LimpiarFiltrosLocales");
        Assert.Equal(0,Property<int>(page,"SeleccionOcultaResumen"));Assert.Equal(0,Property<int>(page,"SeleccionOcultaAlfa"));
    }
    [Fact]
    public void DeshacerDesdeFilaAbreConfirmacionDelVinculoCompletoSinCambiarSeleccion()
    {
        var page=Pantalla();Call(page,"SeleccionarResumen",true,false);
        var seleccion=Field<HashSet<long>>(page,"_detalles").ToHashSet();
        Call(page,"PedirDesconciliar",9L);
        Assert.True(Field<bool>(page,"_confirmar"));Assert.Equal(9L,Field<long?>(page,"_desconciliar"));
        Assert.Equal("",Field<string>(page,"_motivo"));
        Assert.True(seleccion.SetEquals(Field<HashSet<long>>(page,"_detalles")));
    }

    [Fact]
    public void EliminarImportacionExigeDialogoYPermiteCancelarSinModificarElLote()
    {
        var page=Pantalla();var importacion=new TarjetasImportacion{Id=7,Nombre="Prueba.pdf",Cuenta="TJ"};
        Call(page,"PedirEliminar",importacion);
        Assert.Same(importacion,Field<TarjetasImportacion>(page,"_eliminarImportacion"));
        Assert.Equal("",Field<string>(page,"_motivoEliminar"));
        Set(page,"_busy",true);Call(page,"CerrarEliminar");
        Assert.Same(importacion,Field<TarjetasImportacion>(page,"_eliminarImportacion"));
        Set(page,"_busy",false);Call(page,"CerrarEliminar");
        Assert.Null(Field<TarjetasImportacion?>(page,"_eliminarImportacion"));
        Assert.NotNull(Field<TarjetasComparacion>(page,"_comparacion"));
    }

}

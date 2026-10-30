using System.Reflection;
using System.Xml.Linq;
using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasDesconciliarTests
{
    private static TarjetasVinculoDeshacer Grupo(long id,int autorizados=2)=>new()
    {Id=id,Detalles=1,DetallesValidos=1,Movimientos=2,Autorizados=autorizados};
    private static TarjetasVistaDeshacer Vista(params TarjetasVinculoDeshacer[] grupos)=>new(5,"111030001",grupos);
    private static TarjetasDeshacerVarias Solicitud(params long[] ids)=>new(5,"111030001",ids,"Corrección revisada");

    [Fact]
    public void ClavesEstructuradasConservanIdentidadLegacyYEscapanXmlSinAceptarClavesInvalidas()
    {
        var xml=XDocument.Parse(ConciliacionTarjetasService.ClavesDeshacerXml([
            new(1,"111030001","18|3|24|&<|1|CB|0001|00000002|X"),
            new(2,"111030001","18|3|24||1"),
            new(3,"111030001","18|x|24||1|CB|0001|00000002|X"),
            new(4,"111030001","INEXISTENTE")]));
        var filas=xml.Root!.Elements("c").ToList();
        Assert.Equal("1",(string?)filas[0].Attribute("valida"));Assert.Equal("1",(string?)filas[0].Attribute("completa"));
        Assert.Equal("&<",(string?)filas[0].Attribute("tipo"));Assert.Equal("CB",(string?)filas[0].Attribute("tc"));
        Assert.Equal("1",(string?)filas[1].Attribute("valida"));Assert.Equal("0",(string?)filas[1].Attribute("completa"));
        Assert.All(filas.Skip(2),fila=>Assert.Equal("0",(string?)fila.Attribute("valida")));
    }

    [Fact]
    public void ValidaGruposCompletosSinDependerDeFiltrosDePantalla()
    {
        var grupos=ConciliacionTarjetasService.ValidarDesconciliaciones(Vista(Grupo(1),Grupo(2)),Solicitud(2,1));
        Assert.Equal(new long[]{1,2},grupos.Select(v=>v.Id));
        Assert.All(grupos,g=>Assert.Equal(2,g.Movimientos));
    }
    [Fact]
    public void RechazaTodoElLoteSiUnSoloGrupoNoEsAutorizado()
    {
        var ex=Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.ValidarDesconciliaciones(Vista(Grupo(1),Grupo(2,1)),Solicitud(1,2)));
        Assert.Contains("#2",ex.Message);Assert.Contains("No se deshizo ninguna",ex.Message);
    }
    [Fact]
    public void RechazaGrupoDeOtraImportacionAnuladoOConDetalleInconsistente()
    {
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.ValidarDesconciliaciones(Vista(Grupo(1)),Solicitud(2)));
        var grupo=Grupo(1);grupo.DetallesValidos=0;
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.ValidarDesconciliaciones(Vista(grupo),Solicitud(1)));
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.ValidarDesconciliaciones(Vista(Grupo(1)),Solicitud(1) with{Cuenta="OTRA"}));
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.ValidarDesconciliaciones(Vista(Grupo(1)),Solicitud(1) with{Importacion=9}));
    }
    [Fact]
    public void ExigeSeleccionUnicaMotivoYLimiteDeGrupos()
    {
        foreach(var solicitud in new[]{Solicitud(),Solicitud(1,1),Solicitud(0),Solicitud(1) with{Motivo=" "},Solicitud(1) with{Motivo=new string('x',501)},Solicitud(Enumerable.Range(1,1001).Select(i=>(long)i).ToArray())})
            Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.ValidarDesconciliaciones(Vista(Grupo(1)),solicitud));
    }
    [Fact]
    public void SeleccionMasivaIncluyeTodasLasPaginasYOmiteGruposSinPermiso()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var page=new ConciliacionTarjetas();
        void Set(string nombre,object valor)=>typeof(ConciliacionTarjetas).GetField(nombre,flags)!.SetValue(page,valor);
        void Call(string nombre,params object[] args)=>typeof(ConciliacionTarjetas).GetMethod(nombre,flags)!.Invoke(page,args);
        Set("_vistaDeshacer",Vista(Enumerable.Range(1,80).Select(i=>Grupo(i,i==7?0:2)).ToArray()));
        Call("SeleccionarTodasDeshacer");
        var ids=(HashSet<long>)typeof(ConciliacionTarjetas).GetField("_deshacerElegidos",flags)!.GetValue(page)!;
        Assert.Equal(79,ids.Count);Assert.Contains(80L,ids);Assert.DoesNotContain(7L,ids);
        Set("_aceptarDeshacerVarias",true);Call("ElegirDeshacer",80L,false);
        Assert.DoesNotContain(80L,ids);
        Assert.False((bool)typeof(ConciliacionTarjetas).GetField("_aceptarDeshacerVarias",flags)!.GetValue(page)!);
        Call("LimpiarDeshacer");Assert.Empty(ids);
    }
}

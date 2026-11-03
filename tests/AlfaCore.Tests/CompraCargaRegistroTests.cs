using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraCargaRegistroTests
{
    private static (CompraCargaBorrador D,CompraCargaProveedor P,CompraCargaContexto C) Datos()=>
        (new(){Proveedor="21101",Unidad="1",Motivo="99",Fecha=new(2026,8,25),FechaContable=new(2026,8,25),Vencimiento=new(2026,9,9),Sucursal="1",Numero="2",TotalDocumento=12.1m,
            Lineas=[new(){Descripcion="Servicio",Neto=10,Tasa=21}]},
         new(){Codigo="21101",Nombre="Proveedor",CondicionIva="1",SinOc=true},
         new([new(){Codigo="1"}],[new(){Codigo="99"}],[new(){Codigo="13101"},new(){Codigo="11203"}],[21,27,10.5m],
             [new(){Motivo="99",CondicionIva="1",Compra="13101",Iva="11203"}],"1","99",null,null,null)
         {Tipos=[new(){Codigo="FCC",Unidad="1",Letras="AC",Externo=true,DebeHaber="H"}],TasasFiscales=[21,27,10.5m,0]});
    [Fact]
    public void ConfirmacionValidaExigeLaRevisionExacta()
    {
        var(d,p,c)=Datos();var v=CompraCargaPlanificador.Preparar(d,p,c);var huella=CompraCargaService.HuellaRevision(d,p,c,v);
        CompraCargaService.ValidarRegistro(d,p,c,v,huella);
        d.Lineas[0].Cuenta="11203";var cambiado=CompraCargaPlanificador.Preparar(d,p,c);
        Assert.Throws<InvalidOperationException>(()=>CompraCargaService.ValidarRegistro(d,p,c,cambiado,huella));
    }
    [Fact]
    public void CambioDeImputacionConfiguradaInvalidaRevision()
    {
        var(d,p,c)=Datos();var v=CompraCargaPlanificador.Preparar(d,p,c);var h=CompraCargaService.HuellaRevision(d,p,c,v);
        c.Reglas[0].Compra="11203";
        Assert.Throws<InvalidOperationException>(()=>CompraCargaService.ValidarRegistro(d,p,c,CompraCargaPlanificador.Preparar(d,p,c),h));
    }
    [Fact]
    public void NoGrabaSinTotalDelDocumentoAunqueElAsientoBalancee()
    {
        var(d,p,c)=Datos();d.TotalDocumento=null;var v=CompraCargaPlanificador.Preparar(d,p,c);
        Assert.Throws<InvalidOperationException>(()=>CompraCargaService.ValidarRegistro(d,p,c,v,CompraCargaService.HuellaRevision(d,p,c,v)));
    }
    [Fact]
    public void NoOmiteElRequisitoDeOrdenDeCompra()
    {
        var(d,p,c)=Datos();p.SinOc=false;var v=CompraCargaPlanificador.Preparar(d,p,c);
        Assert.Throws<InvalidOperationException>(()=>CompraCargaService.ValidarRegistro(d,p,c,v,CompraCargaService.HuellaRevision(d,p,c,v)));
    }
    [Fact]
    public void IvaMixtoConservaPosicionesConfiguradasAunqueHayaUnaVacia()
    {
        var(d,p,c)=Datos();d.Lineas.Add(new(){Descripcion="Otro",Neto=10,Tasa=10.5m});d.TotalDocumento=23.15m;
        var v=CompraCargaPlanificador.Preparar(d,p,c);var f=CompraCargaFiscal.Crear(d,v,[21,0,10.5m,27]);
        Assert.Equal(new[]{21m,0,10.5m,0},f.Tasas);Assert.Equal(new[]{2.1m,0,1.05m,0},f.Importes);Assert.Equal(20,f.Gravado);
    }
    [Fact]
    public void ComprobanteCConservaBaseSinCreditoFiscal()
    {
        var(d,p,c)=Datos();d.Letra="C";d.Lineas[0].Tasa=0;d.TotalDocumento=10;p.CondicionIva="5";
        c.Reglas[0].CondicionIva="5";
        var v=CompraCargaPlanificador.Preparar(d,p,c);var f=CompraCargaFiscal.Crear(d,v,c.TasasFiscales);
        CompraCargaService.ValidarRegistro(d,p,c,v,CompraCargaService.HuellaRevision(d,p,c,v));
        Assert.Equal(10,f.Gravado);Assert.All(f.Importes,i=>Assert.Equal(0,i));Assert.Single(v.Asiento.Where(a=>a.CabeceraIva));
    }
}

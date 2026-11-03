using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraCargaTests
{
    private static CompraCargaContexto Contexto()=>new([new(){Codigo="1"}],[new(){Codigo="99"}],
        [new(){Codigo="13101",Descripcion="Compras varias"},new(){Codigo="11203",Descripcion="IVA crédito"},new(){Codigo="21209",Descripcion="Imputación particular"},new(){Codigo="11208"}],
        [21,10.5m,27],[new(){Motivo="99",CondicionIva="1",Compra="13101",Iva="11203",PercepcionIva="11208"}],"1","99",new(2025,11,3),new(2027,6,1),new(2000,1,1))
        {Tipos=[new(){Codigo="FCC",Unidad="1",Talonario=0,Letras="AC",Externo=true}]};
    private static CompraCargaProveedor Proveedor()=>new(){Codigo="21101",Nombre="Proveedor de prueba",CondicionIva="1",SinOc=true};
    private static CompraCargaBorrador Borrador()=>new(){Proveedor="21101",Unidad="1",Motivo="99",Fecha=new(2026,8,25),FechaContable=new(2026,8,25),Vencimiento=new(2026,9,9),Sucursal="1",Numero="2",Letra="A",TotalDocumento=12.10m,Lineas=[new(){Descripcion="Servicio de prueba",Neto=10,Tasa=21}]};
    [Fact]
    public void ReproduceFacturaDeReferenciaConUnaSolaCabeceraFiscal()
    {
        var d=Borrador();d.Lineas[0].Cuenta="21209";
        var r=CompraCargaPlanificador.Preparar(d,Proveedor(),Contexto());
        Assert.Empty(r.Errores);Assert.Equal("000100000002A",r.IdComprobante);Assert.Equal(12.10m,r.Total);
        Assert.Equal(3,r.Asiento.Count);Assert.Equal(12.10m,r.Asiento[0].Haber);Assert.Equal("21101",r.Asiento[0].Cuenta);
        Assert.Equal(10m,r.Asiento.Single(a=>a.Cuenta=="21209").Debe);Assert.Equal(2.10m,r.Asiento.Single(a=>a.Cuenta=="11203").Debe);
        Assert.Single(r.Asiento.Where(a=>a.CabeceraIva));Assert.Equal(0,r.DiferenciaAsiento);
    }
    [Fact]
    public void AplicaImputacionParticularAntesDelMotivoYNoAdmiteCuentasAjenas()
    {
        var p=Proveedor();p.Imputacion="21209";
        var r=CompraCargaPlanificador.Preparar(Borrador(),p,Contexto());
        Assert.Contains(r.Asiento,a=>a.Cuenta=="21209");Assert.Contains(r.Avisos,a=>a.Contains("imputación particular"));
        var d=Borrador();d.Lineas[0].Cuenta="999";
        Assert.Contains(CompraCargaPlanificador.Preparar(d,p,Contexto()).Errores,e=>e.Contains("cuenta contable"));
    }
    [Fact]
    public void ConservaIvaInformadoYNoAjustaElTotalParaCuadrar()
    {
        var d=Borrador();d.Lineas[0].IvaInformado=2.09m;
        var r=CompraCargaPlanificador.Preparar(d,Proveedor(),Contexto());
        Assert.Equal(2.09m,r.Iva);Assert.Equal(-.01m,r.DiferenciaDocumento);
        Assert.Contains(r.Errores,e=>e.Contains("no coincide"));Assert.Contains(r.Avisos,a=>a.Contains("se conserva IVA"));
    }
    [Fact]
    public void SumaVariasBasesYPercepcionSinDuplicarIva()
    {
        var d=Borrador();d.TotalDocumento=24.15m;d.PercepcionIva=1;
        d.Lineas.Add(new(){Descripcion="Otro servicio",Neto=10,Tasa=10.5m});
        var r=CompraCargaPlanificador.Preparar(d,Proveedor(),Contexto());
        Assert.Empty(r.Errores);Assert.Equal(3.15m,r.Iva);Assert.Equal(24.15m,r.Total);Assert.Equal(0,r.DiferenciaAsiento);
    }
    [Theory]
    [InlineData("C","Gravado",21,2.1)]
    [InlineData("A","Exento",21,2.1)]
    [InlineData("A","Gravado",22,2.2)]
    public void RechazaCombinacionesFiscalesInvalidas(string letra,string tratamiento,decimal tasa,decimal iva)
    {
        var d=Borrador();d.Letra=letra;d.Lineas[0].Tratamiento=tratamiento;d.Lineas[0].Tasa=tasa;d.Lineas[0].IvaInformado=iva;
        Assert.NotEmpty(CompraCargaPlanificador.Preparar(d,Proveedor(),Contexto()).Errores);
    }
    [Fact]
    public void ComprobanteCNoProponeCreditoFiscal()
    {
        var d=Borrador();d.Letra="C";d.Lineas[0].Tasa=0;d.TotalDocumento=10;
        var r=CompraCargaPlanificador.Preparar(d,Proveedor(),Contexto());
        Assert.Empty(r.Errores);Assert.Equal(0,r.Iva);Assert.Single(r.Asiento.Where(a=>a.CabeceraIva));
    }
    [Fact]
    public void ValidaUnidadFechasYCircuitoDocumental()
    {
        var d=Borrador();d.Unidad="2";d.Fecha=new(2020,1,1);d.ServiciosGastosConfirmados=false;
        var r=CompraCargaPlanificador.Preparar(d,Proveedor(),Contexto());
        Assert.Contains(r.Errores,e=>e.Contains("unidad"));Assert.Contains(r.Errores,e=>e.Contains("rango operativo"));Assert.Contains(r.Errores,e=>e.Contains("Confirmá"));
    }
    [Theory]
    [InlineData("12345","2")]
    [InlineData("1","0")]
    [InlineData("A","2")]
    public void IdentidadNoTruncaNumeros(string suc,string numero)
    {
        var d=Borrador();d.Sucursal=suc;d.Numero=numero;Assert.Throws<InvalidOperationException>(()=>CompraCargaPlanificador.Identidad(d));
    }
    [Fact]
    public void LecturaGuardadaConVariasTasasNoInventaBases()
    {
        var avisos=new List<string>();var ia=new InterfacesCompraIaResultadoDto{Estado="PROCESADO",JsonResultado="{}",TipoComprobante="FACTURA",Letra="A",PuntoVenta="1",Numero="2",NetoGravado=20,Iva21=2.1m,Iva105=1.05m,Total=23.15m,Fecha=new(2026,8,25)};
        var d=CompraCargaOrigenLectura.Convertir(ia,7,avisos);
        Assert.Single(d.Lineas);Assert.Equal(20,d.Lineas[0].Neto);Assert.Equal(0,d.Lineas[0].Tasa);Assert.Equal(3.15m,d.Lineas[0].IvaInformado);
        Assert.False(d.ServiciosGastosConfirmados);Assert.Contains(avisos,a=>a.Contains("varias alícuotas"));
    }
    [Fact]
    public void IdentificaLaNotaDeCreditoSinReinterpretarlaComoFactura()
    {
        var ia=new InterfacesCompraIaResultadoDto{Estado="PROCESADO",TipoComprobante="NC",JsonResultado="{}"};
        var origen=CompraCargaOrigenLectura.Convertir(ia,1,[]);
        Assert.Equal("NCC",origen.Tipo);
        Assert.Throws<InvalidOperationException>(()=>CompraCargaPlanificador.Identidad(origen));
        ia.TipoComprobante="FACTURA";ia.JsonResultado="";
        Assert.Throws<InvalidOperationException>(()=>CompraCargaOrigenLectura.Convertir(ia,1,[]));
    }
    [Theory]
    [InlineData("OC")]
    [InlineData("RMC")]
    [InlineData("NCC")]
    [InlineData("NDC")]
    [InlineData("PG")]
    public void UnTipoPendienteNuncaSeCalculaComoFactura(string tc)
    {
        var d=Borrador();d.Tipo=tc;Assert.Throws<InvalidOperationException>(()=>CompraCargaPlanificador.Preparar(d,Proveedor(),Contexto()));
    }
    [Fact]
    public void RequiereTipoLetraYTalonarioConfigurados()
    {
        var d=Borrador();d.Talonario=99;
        Assert.Contains(CompraCargaPlanificador.Preparar(d,Proveedor(),Contexto()).Errores,e=>e.Contains("talonario"));
        var c=Contexto() with{Tipos=[]};d.Talonario=0;
        Assert.Contains(CompraCargaPlanificador.Preparar(d,Proveedor(),c).Errores,e=>e.Contains("configurado"));
    }
    [Fact]
    public void LaConfiguracionDeLaUnidadPrevaleceSobreLaGlobal()
    {
        var c=Contexto() with{Tipos=[new(){Codigo="FCC",Letras="C",Unidad=""},new(){Codigo="FCC",Letras="A",Unidad="1"}]};
        Assert.Empty(CompraCargaPlanificador.Preparar(Borrador(),Proveedor(),c).Errores);
    }
}

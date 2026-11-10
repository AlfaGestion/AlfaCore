using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraCargaAmpliacionTests
{
    private static (CompraCargaBorrador D,CompraCargaProveedor P,CompraCargaContexto C) Datos(string tipo="FCC")
    {
        var d=new CompraCargaBorrador{Tipo=tipo,Proveedor="21101",Unidad="1",Motivo="99",Sucursal="1",Numero="2",Fecha=new(2026,8,25),FechaContable=new(2026,8,25),Vencimiento=new(2026,8,25),TotalDocumento=121,
            Lineas=[new(){Descripcion="Servicio",Neto=100,Tasa=21}]};
        var p=new CompraCargaProveedor{Codigo="21101",Nombre="Proveedor",SinOc=true,CondicionIva="1"};
        var c=new CompraCargaContexto([new(){Codigo="1"}],[new(){Codigo="99"}],[new(){Codigo="G"},new(){Codigo="I"},new(){Codigo="II"}],[21],
            [new(){Motivo="99",CondicionIva="1",Compra="G",Iva="I",ImpuestoInterno="II"}],"1","99",null,null,null)
            {Tipos=[new(){Codigo=tipo,Unidad="1",Letras="AC",Externo=true,DebeHaber=tipo=="NCC"?"D":"H"}],TasasFiscales=[21,0,0,0],CentrosCosto=[new(){Codigo="AC"},new(){Codigo="AP"}]};
        return(d,p,c);
    }
    [Theory]
    [InlineData("FCC",0,121)]
    [InlineData("NCC",121,0)]
    [InlineData("NDC",0,121)]
    public void ElCreditoInvierteElAsientoSinIngresarValoresNegativos(string tipo,decimal debe,decimal haber)
    {
        var(d,p,c)=Datos(tipo);var v=CompraCargaPlanificador.Preparar(d,p,c);
        Assert.Empty(v.Errores);Assert.Equal(debe,v.Asiento[0].Debe);Assert.Equal(haber,v.Asiento[0].Haber);Assert.Equal(0,v.DiferenciaAsiento);
        d.Lineas[0].Neto=-100;Assert.NotEmpty(CompraCargaPlanificador.Preparar(d,p,c).Errores);
    }
    [Fact]
    public void LosImpuestosInternosIntegranElTotalSinGenerarCreditoFiscal()
    {
        var(d,p,c)=Datos();d.ImpuestoInterno=7.5m;d.TotalDocumento=128.5m;var v=CompraCargaPlanificador.Preparar(d,p,c);
        Assert.Empty(v.Errores);Assert.Equal(21,v.Iva);Assert.Equal(7.5m,v.Asiento.Single(a=>a.Cuenta=="II").Debe);Assert.Equal(100,v.Neto);
    }
    [Fact]
    public void CentrosExigenCienPorCientoSinDuplicadosNiCentrosTitulo()
    {
        var(d,p,c)=Datos();c.Cuentas[0].RequiereCentroCosto=true;
        Assert.Contains(CompraCargaPlanificador.Preparar(d,p,c).Errores,e=>e.Contains("Distribuí"));
        d.CentrosCosto=[new("G","AC",60),new("G","AP",40)];Assert.Empty(CompraCargaPlanificador.Preparar(d,p,c).Errores);
        d.CentrosCosto=[new("G","AC",60),new("G","AC",40)];Assert.NotEmpty(CompraCargaPlanificador.Preparar(d,p,c).Errores);
        d.CentrosCosto=[new("G","AC",99)];Assert.NotEmpty(CompraCargaPlanificador.Preparar(d,p,c).Errores);
    }
    [Fact]
    public void UnaNotaNoPuedeAplicarMasQueSuTotalNiAplicarseDesdeUnaFactura()
    {
        var(d,p,c)=Datos("NCC");d.NotaFacturaId=1;d.NotaAplicar=122;Assert.NotEmpty(CompraCargaPlanificador.Preparar(d,p,c).Errores);
        d.NotaAplicar=100;Assert.Empty(CompraCargaPlanificador.Preparar(d,p,c).Errores);
        (d,p,c)=Datos();d.NotaFacturaId=1;d.NotaAplicar=100;Assert.NotEmpty(CompraCargaPlanificador.Preparar(d,p,c).Errores);
    }
    [Fact]
    public void CienCentrosNoGeneranUnResidualNegativoConImportesPequenos()
    {
        var importes=CompraCargaService.ImportesCentros(.50m,Enumerable.Repeat(1m,100).ToArray());
        Assert.Equal(.50m,importes.Sum());Assert.All(importes,i=>Assert.InRange(i,0,.01m));
    }
    [Theory]
    [InlineData(7.5,7.5,121,128.5,0)]
    [InlineData(-7.5,-7.5,-121,-128.5,0)]
    [InlineData(50,7.5,121,178.5,50)]
    [InlineData(50,0,121,171,50)]
    public void ElLibroNoDuplicaLosImpuestosInternosComoExento(decimal exento,decimal internos,decimal resto,decimal total,decimal esperado)
        =>Assert.Equal(esperado,LibroIvaService.ExentoCompras(exento,internos,resto,total));
    [Theory]
    [InlineData(129842.84, -0.02, 0, 158408.25, .10, 129842.82, 0)]   // FCC A 0011-00273780: centavo de redondeo en no gravado
    [InlineData(129842.84, -0.50, 0, 158408.25, .10, 129842.84, -0.50)] // ajuste grande: no se absorbe, queda marcado
    [InlineData(-1000, 0.02, 0, -1210, .10, -999.98, 0)]               // nota de crédito
    [InlineData(100, 0.02, 0, 121, .10, 100, 0.02)]                     // mismo signo que el total: no es ajuste
    public void ElRedondeoDeSignoOpuestoSeAbsorbeEnElNetoGravado(decimal neto,decimal noGravado,decimal exento,decimal total,decimal maximo,decimal netoEsperado,decimal noGravadoEsperado)
    {
        var r=LibroIvaService.AbsorberRedondeo(neto,noGravado,exento,total,maximo);
        Assert.Equal(netoEsperado,r.Neto);Assert.Equal(noGravadoEsperado,r.NoGravado);
    }
    [Theory]
    [InlineData("NCC")]
    [InlineData("NDC")]
    public void LasNotasNoRequierenLaMarcaFacturaSinOcDelProveedor(string tc)
    {
        var(d,p,c)=Datos(tc);p.SinOc=false;var v=CompraCargaPlanificador.Preparar(d,p,c);
        CompraCargaService.ValidarRegistro(d,p,c,v,CompraCargaService.HuellaRevision(d,p,c,v));
    }
}

using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasLtTests
{
    private static List<TarjetasLtAsignacion> Reglas()=>TarjetasLtPreparacion.Roles.Select(r=>new TarjetasLtAsignacion{Rol=r,Cuenta=r=="Banco"?"1110201":"42405"}).ToList();
    [Fact]
    public void ConservaImportesDelDocumentoYBalanceaSinRecalcularIva()
    {
        var l=new TarjetasLtLiquidacion{Numero="510208",Neto=33597.35m,Conceptos=[
            new(){Rol="Tarjeta",Importe=35246.16m},new(){Rol="Gastos",Importe=-634.43m},
            new(){Rol="IvaCredito",Importe=-133.23m},new(){Rol="Iibb",Importe=-881.15m}]};
        var b=TarjetasLtPreparacion.Preparar(l,"111030001",Reglas());
        Assert.Equal(0,b.Diferencia);Assert.Equal(35246.16m,b.Haber);Assert.Equal(35246.16m,b.Debe);
        Assert.Equal(133.23m,b.Lineas.Single(x=>x.Rol=="IvaCredito").Debe);
        Assert.Equal("111030001",b.Lineas.Single(x=>x.Rol=="Tarjeta").Cuenta);
    }
    [Fact]
    public void NoInventaAjustesYConservaReversiones()
    {
        var l=new TarjetasLtLiquidacion{Neto=90,Conceptos=[new(){Rol="Tarjeta",Importe=100},new(){Rol="Gastos",Importe=-15},new(){Rol="Gastos",Importe=4}]};
        var b=TarjetasLtPreparacion.Preparar(l,"11103",[]);
        Assert.Equal(1,b.Diferencia);Assert.Equal(4,b.Lineas.Count);
        Assert.Equal(4,b.Lineas[2].Haber);Assert.Contains(b.Avisos,x=>x.Contains("no balancea"));
        Assert.Contains(b.Avisos,x=>x.Contains("sin cuenta"));
    }
    [Fact]
    public void ResumenMensualNoSePresentaComoLiquidacionIndividual()
    {
        var b=TarjetasLtPreparacion.Preparar(new(){EsResumenMensual=true,Neto=90,Conceptos=[new(){Rol="Tarjeta",Importe=100},new(){Rol="SinClasificar",Importe=-10}]},"11103",Reglas());
        Assert.Contains(b.Avisos,x=>x.Contains("no representa una LT individual"));
        Assert.Contains(b.Avisos,x=>x.Contains("sin clasificar"));
    }
    [Fact]
    public void SugerenciasUnivocasYTiposDeCuentaSeValidan()
    {
        TarjetasLtCuenta[] catalogo=[new(){Codigo="1",Descripcion="Gastos Tarjetas de Crédito"},new(){Codigo="2",Descripcion="Gastos Tarjetas de Credito"},new(){Codigo="3",Descripcion="Banco",Medio="DB"},new(){Codigo="4",Descripcion="I.V.A. Crédito Fiscal"}];
        var reglas=TarjetasLtPreparacion.Sugerir(catalogo).ToList();
        Assert.Equal("",reglas.Single(r=>r.Rol=="Gastos").Cuenta);
        Assert.Equal("4",reglas.Single(r=>r.Rol=="IvaCredito").Cuenta);
        reglas.Single(r=>r.Rol=="Banco").Cuenta="3";TarjetasLtPreparacion.Validar(reglas,catalogo);
        reglas.Single(r=>r.Rol=="Gastos").Cuenta="3";
        Assert.Throws<InvalidOperationException>(()=>TarjetasLtPreparacion.Validar(reglas,catalogo));
    }
}

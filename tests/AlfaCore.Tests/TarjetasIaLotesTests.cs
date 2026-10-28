using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasIaLotesTests
{
    private static readonly DateTime Inicio=new(2025,8,10);
    private static TarjetasComparacion Datos(int detalles,int movimientos)=>new(new(){DetalleCompleto=true,Cuenta="TJ"},
        Enumerable.Range(1,detalles).Select(i=>new TarjetasDetalle{Id=i,Fecha=Inicio.AddDays(i%30),Importe=100}).ToList(),
        Enumerable.Range(1,movimientos).Select(i=>new TarjetasMovimiento{Clave=i.ToString(),Huella=i.ToString(),Fecha=Inicio.AddDays(i%30),Importe=100}).ToList(),1);

    [Fact]
    public void PeriodoGrandeSeDivideSinRecortarCandidatosDentroDelMargen()
    {
        var datos=Datos(845,3278);var lotes=TarjetasIaLotes.Preparar(datos,3,2000);
        Assert.True(lotes.Count>1);
        Assert.Equal(845,lotes.SelectMany(l=>l.Detalles).Select(d=>d.Id).Distinct().Count());
        Assert.Equal(845,lotes.Sum(l=>l.Detalles.Count));
        foreach(var lote in lotes)
        {
            Assert.InRange(lote.Detalles.Count,1,2000);Assert.InRange(lote.Movimientos.Count,1,2000);
            var desde=lote.Detalles.Min(d=>d.Fecha).AddDays(-3);var hasta=lote.Detalles.Max(d=>d.Fecha).AddDays(3);
            Assert.True(datos.Movimientos.Where(m=>m.Fecha>=desde&&m.Fecha<=hasta).Select(m=>m.Clave).ToHashSet().SetEquals(lote.Movimientos.Select(m=>m.Clave)));
        }
        Assert.Equal(3278,lotes.SelectMany(l=>l.Movimientos).Select(m=>m.Clave).Distinct().Count());
    }
    [Fact]
    public void DiaDemasiadoGrandeRechazaPlanCompletoConFechaYAccionConcreta()
    {
        var datos=Datos(1,51);
        foreach(var m in datos.Movimientos)m.Fecha=datos.Detalles[0].Fecha;
        var error=Assert.Throws<InvalidOperationException>(()=>TarjetasIaLotes.Preparar(datos,3,50));
        Assert.Contains("11/08/2025",error.Message);Assert.Contains("sucursal",error.Message);Assert.Contains("No se envió",error.Message);
    }
    [Fact]
    public void MapeaIndicesDeCadaLoteYDescartaGruposQueCompitenEntreLotes()
    {
        var original=Datos(2,2);
        var lotes=new[]{original with{Detalles=[original.Detalles[0]],Movimientos=[original.Movimientos[0]]},
            original with{Detalles=[original.Detalles[1]],Movimientos=[original.Movimientos[1]]}};
        var filtro=new TarjetasFiltro(1,Inicio,Inicio.AddDays(30));
        const string json="""{"grupos":[{"banco":[0],"alfa":[0],"confianza":"Alta","motivo":"Par de ensayo"}],"orientaciones":[]}""";
        var resultados=lotes.Select(l=>TarjetasIaPropuestas.Validar(l,TarjetasIaPropuestas.Contexto(l,filtro,3,50),new("ensayo",json))).ToList();
        var unido=TarjetasIaLotes.Unir(lotes,resultados,original,3);
        Assert.Equal(2,unido.Propuestas.Count);Assert.Equal("2",unido.Propuestas[1].Movimientos[0].Clave);Assert.Equal(2,unido.LotesAnalizados);
        var conflicto=resultados[1] with{Propuestas=[resultados[1].Propuestas[0] with{Movimientos=[original.Movimientos[0]]}]};
        var descartado=TarjetasIaLotes.Unir(lotes,[resultados[0],conflicto],original,3);
        Assert.Empty(descartado.Propuestas);Assert.Equal(2,descartado.Descartadas);
        var repetido=TarjetasIaLotes.Unir(lotes,[resultados[0],resultados[0]],original,3);
        Assert.Single(repetido.Propuestas);
    }
    [Fact]
    public void InformaRegistrosFueraDeVentanasYExcluyeConciliadosLegacyYAlterados()
    {
        var datos=Datos(2,55);
        foreach(var m in datos.Movimientos)m.Fecha=Inicio.AddDays(80);
        datos.Movimientos[0].Fecha=datos.Detalles[0].Fecha;
        datos.Movimientos[1].LegacyLiquidado=true;datos.Movimientos[2].Alterado=true;datos.Movimientos[3].Vinculo=9;
        datos.Detalles[1].Fecha=Inicio.AddDays(40);
        var lotes=TarjetasIaLotes.Preparar(datos,0,50);
        var r=TarjetasIaLotes.Unir(lotes,[],datos,0);
        Assert.Equal(1,r.LotesAnalizados);Assert.Equal(1,r.DetallesAnalizados);Assert.Equal(1,r.MovimientosAnalizados);
        Assert.Equal(1,r.DetallesSinCandidatosPorFecha);Assert.Equal(51,r.MovimientosSinCandidatosPorFecha);
    }
}

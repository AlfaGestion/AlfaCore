using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasIaTests
{
    private static TarjetasComparacion Datos()=>new(new(){Cuenta="TJ-DEMO",DetalleCompleto=true},
        [new(){Id=10,Fecha=new(2026,4,1),Importe=150,Nivel="Lote",Cantidad=2,Lote="0011",Liquidacion="123"}],
        [new(){Clave="A",Huella="HA",Fecha=new(2026,4,1),Importe=100,Cupon="123"},
         new(){Clave="B",Huella="HB",Fecha=new(2026,4,2),Importe=50.22m,Cupon="124"},
         new(){Clave="LEGACY",Fecha=new(2026,4,1),Importe=150,LegacyLiquidado=true},
         new(){Clave="ALTERADO",Fecha=new(2026,4,1),Importe=150,Alterado=true}],1);
    private static readonly TarjetasFiltro Filtro=new(1,new(2026,4,1),new(2026,4,30));
    private const string Json="""{"grupos":[{"banco":[0],"alfa":[0,1],"confianza":"Media","motivo":"Dos ventas por el bruto del lote; revisar la identidad"}],"orientaciones":[]}""";

    [Fact]
    public void ContextoSoloIncluyePendientesValidosSinAdjuntarOriginal()
    {
        var c=TarjetasIaPropuestas.Contexto(Datos(),Filtro,3,50);
        Assert.True(c.EsTarjetas);Assert.Equal(2,c.Alfa.Count);Assert.Single(c.Banco);
        Assert.Contains("lote 0011",c.Banco[0].Referencia);Assert.Contains("cantidad 2",c.Banco[0].Detalle);
        var r=TarjetasIaPropuestas.Validar(Datos(),c,new("ensayo",Json));
        var p=Assert.Single(r.Propuestas);
        Assert.Equal(-0.22m,p.Diferencia);Assert.Equal(new[]{"A","B"},p.Movimientos.Select(m=>m.Clave));
        Assert.Equal("HB",p.Movimientos[1].Huella);
    }
    [Fact]
    public void DescartaIndicesInventadosReutilizadosSignosFechasYFueraDeTolerancia()
    {
        var datos=Datos();var c=TarjetasIaPropuestas.Contexto(datos,Filtro,3,50);
        foreach(var json in new[]{Json.Replace("[0,1]","[0,99]"),Json.Replace("[0,1]","[0,0]"),Json.Replace("[0,1]","[0]")})
            Assert.Empty(TarjetasIaPropuestas.Validar(datos,c,new("ensayo",json)).Propuestas);
        Assert.Empty(TarjetasIaPropuestas.Validar(datos,c with{Criterios=new(0,1)},new("ensayo",Json)).Propuestas);
        Assert.Empty(TarjetasIaPropuestas.Validar(datos,c with{Criterios=new(3,0.1m)},new("ensayo",Json)).Propuestas);
        c.Alfa[1].Importe=-50.22m;
        Assert.Empty(TarjetasIaPropuestas.Validar(datos,c,new("ensayo",Json)).Propuestas);
        Assert.Throws<InvalidOperationException>(()=>TarjetasIaPropuestas.Validar(datos,c,new("ensayo","respuesta ilegible")));
    }
    [Fact]
    public void DescartaTodosLosGruposQueCompitenPorElMismoMovimiento()
    {
        var datos=Datos();var c=TarjetasIaPropuestas.Contexto(datos,Filtro,3,50);
        var salida=JsonSerializer.Deserialize<ConciliacionIaSalida>(Json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        salida.Grupos.Add(salida.Grupos[0]);
        var r=TarjetasIaPropuestas.Validar(datos,c,new("ensayo",JsonSerializer.Serialize(salida)));
        Assert.Empty(r.Propuestas);Assert.Equal(2,r.Descartadas);
    }
    [Fact]
    public void CuponesIdentificablesDiferentesNoSeVinculanAunqueCoincidanLosImportes()
    {
        var datos=Datos();datos.Detalles[0].Nivel="Cupón";datos.Detalles[0].Cupon="00999";
        var c=TarjetasIaPropuestas.Contexto(datos,Filtro,3,50);
        Assert.Empty(TarjetasIaPropuestas.Validar(datos,c,new("ensayo",Json)).Propuestas);
        datos.Detalles[0].Cupon="00123";
        Assert.Empty(TarjetasIaPropuestas.Validar(datos,c,new("ensayo",Json)).Propuestas);
        datos.Movimientos[1].Cupon="000123";
        Assert.Single(TarjetasIaPropuestas.Validar(datos,c,new("ensayo",Json)).Propuestas);
    }
    [Fact]
    public void SuperarElMaximoNoEnviaUnaMuestraParcial()
    {
        var datos=Datos();
        var grande=datos with{Detalles=Enumerable.Range(1,51).Select(i=>new TarjetasDetalle{Id=i}).ToList()};
        Assert.Throws<InvalidOperationException>(()=>TarjetasIaPropuestas.Contexto(grande,Filtro,3,50));
    }
}

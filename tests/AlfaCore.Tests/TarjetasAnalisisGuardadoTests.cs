using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasAnalisisGuardadoTests
{
    private static (TarjetasAnalisisGuardado Guardado,TarjetasComparacion Datos) Caso()
    {
        var datos=TarjetasAgrupacionesTests.CasoLotesGrandes();
        var resultado=new TarjetasIaResultado("Local",7,133,0,3,TarjetasAgrupaciones.Buscar(datos,new()).Propuestas){ResumenBusqueda="Evidencia original"};
        return(new(1,"TJ",new(1,new(2026,9,25),new(2026,10,2),"0001"),new(){MaxMovimientosGrupo=300},resultado),datos);
    }
    [Fact]
    public void SerializacionConservaCriteriosAlcanceHuellasYEvidencia()
    {
        var (original,datos)=Caso();
        var copia=JsonSerializer.Deserialize<TarjetasAnalisisGuardado>(JsonSerializer.Serialize(original))!;
        Assert.Equal(original.Filtro,copia.Filtro);Assert.Equal(300,copia.Criterios.MaxMovimientosGrupo);
        var resultado=ConciliacionTarjetasService.RevalidarAnalisis(copia,datos,out var omitidas);
        Assert.Equal(0,omitidas);Assert.Equal(4,resultado.Propuestas.Count);
        Assert.Equal("Evidencia original",resultado.ResumenBusqueda);
        Assert.Equal(4130191.29m,resultado.Propuestas.Sum(p=>p.Detalles.Sum(d=>d.Importe)));
        Assert.All(resultado.Propuestas,p=>Assert.All(p.Movimientos,m=>Assert.Same(datos.Movimientos.Single(a=>a.Clave==m.Clave),m)));
    }
    [Theory]
    [InlineData("huella")]
    [InlineData("conciliado")]
    [InlineData("legacy")]
    [InlineData("alterado")]
    [InlineData("sinPermiso")]
    [InlineData("detalle")]
    public void OmiteElGrupoEnteroYConservaLosOtrosCuandoUnMiembroNoEstaVigente(string cambio)
    {
        var (guardado,datos)=Caso();
        // Deserializar para simular dos consultas independientes, sin compartir objetos mutables.
        datos=JsonSerializer.Deserialize<TarjetasComparacion>(JsonSerializer.Serialize(datos))!;
        var propuesta=guardado.Resultado.Propuestas[0];var clave=propuesta.Movimientos[0].Clave;
        var m=datos.Movimientos.Single(a=>a.Clave==clave);
        switch(cambio)
        {
            case "huella":m.Huella="CAMBIO";break;
            case "conciliado":m.Vinculo=1;break;
            case "legacy":m.LegacyLiquidado=true;break;
            case "alterado":m.Alterado=true;break;
            case "sinPermiso":datos=datos with{Movimientos=datos.Movimientos.Where(a=>a.Clave!=clave).ToList()};break;
            case "detalle":datos.Detalles.Single(d=>d.Id==propuesta.Detalles[0].Id).Cantidad++;break;
        }
        var resultado=ConciliacionTarjetasService.RevalidarAnalisis(guardado,datos,out var omitidas);
        Assert.Equal(1,omitidas);Assert.Equal(3,resultado.Propuestas.Count);
        Assert.DoesNotContain(resultado.Propuestas,p=>p.Movimientos.Any(a=>a.Clave==clave));
    }
    [Fact]
    public void CuentaCorregidaNoRecuperaPropuestasDeOtraCuenta()
    {
        var (guardado,datos)=Caso();datos.Importacion.Cuenta="OTRA";
        Assert.Throws<InvalidOperationException>(()=>ConciliacionTarjetasService.RevalidarAnalisis(guardado,datos,out _));
    }
    [Fact]
    public void ToleraAnalisisSinCoincidenciasYRespetaLaToleranciaActual()
    {
        var (guardado,datos)=Caso();
        var vacio=guardado with{Resultado=guardado.Resultado with{Propuestas=[]}};
        Assert.Empty(ConciliacionTarjetasService.RevalidarAnalisis(vacio,datos,out var omitidas).Propuestas);Assert.Equal(0,omitidas);
        var copia=JsonSerializer.Deserialize<TarjetasAnalisisGuardado>(JsonSerializer.Serialize(guardado))!;
        copia.Resultado.Propuestas[0].Detalles[0].Importe+=0.5m;
        datos.Detalles.Single(d=>d.Id==copia.Resultado.Propuestas[0].Detalles[0].Id).Importe+=0.5m;
        var resultado=ConciliacionTarjetasService.RevalidarAnalisis(copia,datos with{Tolerancia=0.1m},out omitidas);
        Assert.Equal(1,omitidas);Assert.Equal(3,resultado.Propuestas.Count);
    }
}

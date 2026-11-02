using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasAutomaticasTests
{
    internal static TarjetasComparacion CasoResidual()
    {
        var ds=new List<TarjetasDetalle>();var ms=new List<TarjetasMovimiento>();long id=0;
        void Grupo(string lote,int pdfDia,int alfaDia,string suc,int n,int[] cantidades,decimal[] valores,int[] excluir,decimal dif=0)
        {
            var elegidos=valores.Where((_,i)=>!excluir.Contains(i)).ToArray();var posicion=0;
            foreach(var cantidad in cantidades)
            {
                var importe=elegidos.Skip(posicion).Take(cantidad).Sum();if(posicion==0)importe+=dif;
                ds.Add(new(){Id=++id,Lote=lote,Nivel="Lote",Fecha=new(2026,9,pdfDia),Cantidad=cantidad,Importe=importe});posicion+=cantidad;
            }
            for(var i=0;i<n;i++)ms.Add(new(){Clave=$"{lote}-{i}",Huella=$"H-{lote}-{i}",Sucursal=suc,Fecha=new(2026,9,alfaDia),Importe=valores[i]});
        }
        static decimal[] Valores(int n,decimal factor)=>Enumerable.Range(0,n).Select(i=>(decimal)Math.Pow(2,i)*factor).ToArray();
        Grupo("0370",4,1,"0003",23,[15,4],Valores(23,100),[3,8,13,20],-.01m);
        Grupo("0928",4,1,"0001",16,[7],Valores(16,3.17m),[0,1,2,3,4,5,6,7,8]);
        foreach(var d in ds)d.Fecha=new(2026,8,31);
        foreach(var m in ms)m.Fecha=new(2026,8,28);
        Grupo("0379",7,5,"0003",25,[24],Valores(25,100),[5]);
        Grupo("0387",14,12,"0003",19,[12,6],Valores(19,100),[7]);
        var inicio=ms.Count;Grupo("0395",21,19,"0003",51,[41,10],Enumerable.Repeat(1m,51).ToArray(),[]);
        foreach(var m in ms.Skip(inicio+32))m.Fecha=new(2026,9,20);
        var valores=Valores(30,100).Concat([4965.50m,4965.50m]).ToArray();
        Grupo("0386",14,11,"0003",32,[25,6],valores,[31]);
        Grupo("0391",17,16,"0003",16,[16],Valores(15,10).Concat([4965.50m]).ToArray(),[]);
        ms.Last().Vinculo=13;
        return new(new(){Id=1,Cuenta="TJ",DetalleCompleto=true},ds,ms,1);
    }
    [Fact]
    public void ResuelveLotesCompletosComplementosYFinDeSemanaSinElegirEntreImportesRepetidos()
    {
        var datos=CasoResidual();var resultado=TarjetasBusquedaAutomatica.Buscar(datos,new(){Dias=4});
        Assert.Equal(5,resultado.Propuestas.Count);Assert.Equal(8,resultado.Propuestas.Sum(p=>p.Detalles.Count));
        Assert.Equal(119,resultado.Propuestas.Sum(p=>p.Movimientos.Count));
        Assert.Contains(resultado.Propuestas,p=>p.Movimientos.Select(m=>m.Fecha.Date).Distinct().Count()==2&&p.Movimientos.Count==51);
        Assert.Contains(resultado.Propuestas,p=>p.Diferencia==-.01m);
        Assert.Contains(resultado.Diagnosticos,d=>d.Tipo=="Ambiguo"&&d.Titulo.Contains("0386"));
        var bloqueados=TarjetasBusquedaAutomatica.DetectarYaConciliados(datos,new(){Dias=4},resultado.Propuestas.SelectMany(p=>p.Detalles).Select(d=>d.Id).ToHashSet());
        Assert.Contains(bloqueados,d=>d.Titulo.Contains("0391")&&d.Vinculos.Contains(13));
        Assert.All(resultado.Propuestas,p=>Assert.Single(p.Movimientos.Select(m=>m.Sucursal).Distinct()));
    }
    [Fact]
    public void RespetaLimitesCancelacionYNoMezclaSucursalesNiFechasFueraDeMargen()
    {
        var datos=CasoResidual();var c=new TarjetasCriteriosAnalisis{Dias=4,MaxDiasGrupo=1,MaxIntentosAutomaticos=100};
        var r=TarjetasBusquedaAutomatica.Buscar(datos,c);Assert.InRange(r.Intentos,0,100);
        Assert.DoesNotContain(r.Propuestas,p=>p.Movimientos.Select(m=>m.Fecha.Date).Distinct().Count()>1);
        Assert.True(r.AlcancesOmitidos>0);
        using var cancelar=new CancellationTokenSource();cancelar.Cancel();
        Assert.Throws<OperationCanceledException>(()=>TarjetasBusquedaAutomatica.Buscar(datos,c,cancelar.Token));
        Assert.Throws<InvalidOperationException>(()=>TarjetasBusquedaAutomatica.Buscar(datos,c with{MaxDiasGrupo=8}));
    }
    [Fact]
    public void NoAceptaQueElModeloElijaUnoDeDosCobrosIgualesSinNumeroDeCupon()
    {
        var a=new TarjetasMovimiento{Clave="A",Fecha=new(2026,9,11),Importe=4965.50m,Cupon="1"};
        var b=new TarjetasMovimiento{Clave="B",Fecha=a.Fecha,Importe=a.Importe,Cupon="1"};
        var d=new TarjetasDetalle{Id=1,Fecha=a.Fecha,Importe=a.Importe,Nivel="Cupón",Cupon="6490"};
        var p=new TarjetasIaPropuesta("Alta","Respuesta del modelo",[d],[a]);
        var datos=new TarjetasComparacion(new(),[d],[a,b],1);
        Assert.True(TarjetasBusquedaAutomatica.HayAlternativaIndistinguible(p,datos,new()));
        a.Cupon="6490";Assert.False(TarjetasBusquedaAutomatica.HayAlternativaIndistinguible(p,datos,new()));
    }
    [Fact]
    public void ElFallbackDeIaNoPuedeMezclarSucursalesNiExcederDiasOCantidades()
    {
        var datos=CasoResidual();var c=new TarjetasCriteriosAnalisis{Dias=4};
        var propuesta=TarjetasBusquedaAutomatica.Buscar(datos,c).Propuestas.First();
        Assert.True(TarjetasBusquedaAutomatica.CumpleLimites(propuesta,c));
        var copia=propuesta.Movimientos.Select(m=>new TarjetasMovimiento{Fecha=m.Fecha,Sucursal=m.Sucursal,Importe=m.Importe}).ToList();
        copia[0].Sucursal="OTRA";Assert.False(TarjetasBusquedaAutomatica.CumpleLimites(propuesta with{Movimientos=copia},c));
        copia[0].Sucursal=copia[1].Sucursal;copia[0].Fecha=copia[1].Fecha.AddDays(8);
        Assert.False(TarjetasBusquedaAutomatica.CumpleLimites(propuesta with{Movimientos=copia},c));
        Assert.False(TarjetasBusquedaAutomatica.CumpleLimites(propuesta with{Movimientos=propuesta.Movimientos.Skip(1).ToList()},c));
    }
    [Fact]
    public void PlanDeIaSoloEligeAlcancesYElServidorCompruebaCantidadesEImportes()
    {
        var datos=CasoResidual();var c=new TarjetasCriteriosAnalisis{Dias=4};
        var plan=TarjetasPlanIa.Preparar(datos,new(1,new(2026,9,1),new(2026,9,25)),c);
        Assert.True(plan.Contexto.EsPlanTarjetas);
        var objetivo=plan.Objetivos.Single(o=>o.Detalles[0].Lote=="0395");
        var alcance=plan.Alcances.Single(a=>a.Sucursal=="0003"&&a.Desde==new DateTime(2026,9,19)&&a.Hasta==new DateTime(2026,9,20));
        var json=$"{{\"grupos\":[{{\"banco\":[{objetivo.Id}],\"alfa\":[{alcance.Id}],\"confianza\":\"Alta\",\"motivo\":\"Revisar fin de semana\"}}],\"orientaciones\":[]}}";
        var seleccion=TarjetasPlanIa.Validar(plan,json,c);Assert.Single(seleccion);
        var r=TarjetasBusquedaAutomatica.Buscar(datos,c,plan:seleccion);var p=Assert.Single(r.Propuestas);Assert.Equal(51,p.Movimientos.Count);Assert.Equal(0,p.Diferencia);
        Assert.Empty(TarjetasPlanIa.Validar(plan,"{\"grupos\":[{\"banco\":[9999],\"alfa\":[0]}],\"orientaciones\":[]}",c));
        Assert.Throws<InvalidOperationException>(()=>TarjetasPlanIa.Validar(plan,"{\"grupos\":[]}",c));
        Assert.Empty(TarjetasBusquedaAutomatica.Buscar(datos,c,plan:new HashSet<(int,int)>()).Propuestas);
        var ambiguo=plan.Objetivos.Single(o=>o.Detalles[0].Lote=="0386");
        var diaAmbiguo=plan.Alcances.Single(a=>a.Sucursal=="0003"&&a.Desde==new DateTime(2026,9,11)&&a.Hasta==a.Desde);
        var noElegir=TarjetasBusquedaAutomatica.Buscar(datos,c,plan:new HashSet<(int,int)>{(ambiguo.Id,diaAmbiguo.Id)});
        Assert.Empty(noElegir.Propuestas);Assert.Contains(noElegir.Diagnosticos,d=>d.Tipo=="Ambiguo");
    }
}

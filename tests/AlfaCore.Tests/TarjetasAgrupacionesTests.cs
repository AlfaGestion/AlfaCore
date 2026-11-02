using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasAgrupacionesTests
{
    internal static TarjetasComparacion CasoLotesGrandes()
    {
        var ds=new List<TarjetasDetalle>();var ms=new List<TarjetasMovimiento>();long id=0;
        void Grupo(int dia,string lote,(int Cantidad,decimal Total)[] conceptos)
        {
            foreach(var c in conceptos)ds.Add(new(){Id=++id,Fecha=new(2026,9,dia==28?29:28),Lote=lote,Nivel="Lote",Cantidad=c.Cantidad,Importe=c.Total});
            var cantidad=conceptos.Sum(c=>c.Cantidad);var total=conceptos.Sum(c=>c.Total);
            for(var j=0;j<cantidad;j++)ms.Add(new(){Clave=$"{lote}-{j}",Huella=$"H-{lote}-{j}",Fecha=new(2026,9,dia),Sucursal="0001",Importe=j==cantidad-1?total-(cantidad-1):1});
        }
        Grupo(25,"0001",[(22,611990.03m),(10,433922.27m)]);
        Grupo(26,"0002",[(35,988999.93m),(8,227018.35m)]);
        Grupo(27,"0003",[(30,1053290.99m)]);
        Grupo(28,"0004",[(21,618313.58m),(7,196656.14m)]);
        return new(new(){Id=1,Cuenta="TJ",DetalleCompleto=true},ds,ms,1);
    }
    [Fact]
    public void ResuelveSieteConceptosEnCuatroGruposDe133MovimientosYPermiteConfirmarMasDeVeinte()
    {
        var datos=CasoLotesGrandes();var resultado=TarjetasAgrupaciones.Buscar(datos,new());
        Assert.False(resultado.Limitada);Assert.Equal(4,resultado.Propuestas.Count);
        Assert.Equal(133,resultado.Propuestas.Sum(p=>p.Movimientos.Count));Assert.Equal(7,resultado.Propuestas.Sum(p=>p.Detalles.Count));
        Assert.Equal(4130191.29m,resultado.Propuestas.Sum(p=>p.Detalles.Sum(d=>d.Importe)));
        Assert.All(resultado.Propuestas,p=>{Assert.Equal("Agrupación local",p.Origen);Assert.Equal(0,p.Diferencia);Assert.Single(p.Movimientos.Select(m=>m.Sucursal).Distinct());});
        var filtro=new TarjetasFiltro(1,new(2026,9,25),new(2026,10,2));
        var solicitud=new TarjetasConfirmarVarias(filtro,resultado.Propuestas.Select(p=>new TarjetasIaSeleccion(p.Detalles.Select(d=>d.Id).ToList(),p.Movimientos.Select(m=>new TarjetasSeleccion(m.Clave,m.Huella)).ToList(),p.Motivo)).ToList(),false,"");
        Assert.Equal(4,ConciliacionTarjetasService.PrepararConfirmaciones(datos,solicitud).Count);
    }
    [Fact]
    public void ProgresoInformaFasesEIntentosSinCambiarLasPropuestas()
    {
        var avances=new List<(string Actividad,int Intentos)>();
        var resultado=TarjetasAgrupaciones.Buscar(CasoLotesGrandes(),new(),progreso:(a,i)=>avances.Add((a,i)));
        Assert.Equal(4,resultado.Propuestas.Count);
        Assert.Contains(avances,a=>a.Actividad.Contains("combinaciones"));
        Assert.Contains(avances,a=>a.Actividad.Contains("totales diarios"));
        Assert.Contains(avances,a=>a.Actividad.Contains("subconjuntos"));
        Assert.Equal(avances.Select(a=>a.Intentos).OrderBy(i=>i),avances.Select(a=>a.Intentos));
        Assert.All(avances,a=>Assert.InRange(a.Intentos,0,resultado.Intentos));
    }
    [Fact]
    public void MargenYMaximoRestringenLaBusquedaSinForzarCoincidencias()
    {
        var datos=CasoLotesGrandes();
        var cercano=TarjetasAgrupaciones.Buscar(datos,new(){Dias=2,BuscarSubconjuntos=false});
        Assert.Equal(3,cercano.Propuestas.Count);Assert.DoesNotContain(cercano.Propuestas,p=>p.Movimientos.Any(m=>m.Fecha.Day==25));
        var corto=TarjetasAgrupaciones.Buscar(datos,new(){MaxMovimientosGrupo=20,BuscarSubconjuntos=false});
        Assert.Empty(corto.Propuestas);Assert.Equal(4,corto.DiasExcedidos);
    }
    private static TarjetasComparacion Pequeño(decimal[] importes,decimal objetivo=70,int cantidad=2)
        =>new(new(){Id=1,DetalleCompleto=true},[new(){Id=1,Fecha=new(2026,9,28),Nivel="Lote",Lote="001",Importe=objetivo,Cantidad=cantidad}],
            importes.Select((v,n)=>new TarjetasMovimiento{Clave=n.ToString(),Fecha=new(2026,9,27),Sucursal="0001",Importe=v}).ToList(),0);
    [Fact]
    public void PruebaSubconjuntosConCantidadYRechazaAlternativasDelMismoImporte()
    {
        var unico=TarjetasAgrupaciones.Buscar(Pequeño([10,20,30,50]),new());
        var p=Assert.Single(unico.Propuestas);Assert.Equal(new[]{20m,50m},p.Movimientos.Select(m=>m.Importe));Assert.Equal("Media",p.Confianza);
        var ambiguo=TarjetasAgrupaciones.Buscar(Pequeño([20,50,20]),new());
        Assert.Empty(ambiguo.Propuestas);Assert.True(ambiguo.Ambiguas>=2);
        Assert.Empty(TarjetasAgrupaciones.Buscar(Pequeño([10,20,30,50]),new(){BuscarSubconjuntos=false}).Propuestas);
    }
    [Fact]
    public void NoEligeEntreDosSucursalesConTotalesIgualesNiMezclaSusMovimientos()
    {
        var datos=Pequeño([20,50]);
        datos=datos with{Movimientos=datos.Movimientos.Concat([new(){Clave="X",Fecha=new(2026,9,27),Sucursal="0003",Importe=30},new(){Clave="Y",Fecha=new(2026,9,27),Sucursal="0003",Importe=40}]).ToList()};
        var resultado=TarjetasAgrupaciones.Buscar(datos,new());Assert.Empty(resultado.Propuestas);Assert.Equal(2,resultado.Ambiguas);
        Assert.Empty(TarjetasAgrupaciones.Buscar(Pequeño([20,50]) with{Movimientos=[new(){Clave="A",Sucursal="0001",Fecha=new(2026,9,27),Importe=20},new(){Clave="B",Sucursal="0003",Fecha=new(2026,9,27),Importe=50}]},new()).Propuestas);
    }
    [Fact]
    public void LimiteDeIntentosNoSePresentaComoBusquedaAgotadaYCancela()
    {
        var datos=Pequeño(Enumerable.Range(1,25).Select(n=>(decimal)n).ToArray(),50.01m,5);
        var resultado=TarjetasAgrupaciones.Buscar(datos,new(){MaxIntentos=100});
        Assert.True(resultado.Limitada);Assert.Equal(100,resultado.Intentos);Assert.Empty(resultado.Propuestas);
        using var cancelado=new CancellationTokenSource();cancelado.Cancel();
        Assert.Throws<OperationCanceledException>(()=>TarjetasAgrupaciones.Buscar(datos,new(),cancelado.Token));
    }
    [Fact]
    public void NoReutilizaConciliadosLegacyOModificadosNiAgrupaCuponesIdentificadosComoLotes()
    {
        foreach(var tipo in new[]{"vinculado","legacy","alterado"})
        {
            var datos=Pequeño([20,50]);var m=datos.Movimientos[0];m.Vinculo=tipo=="vinculado"?9:null;m.LegacyLiquidado=tipo=="legacy";m.Alterado=tipo=="alterado";
            Assert.Empty(TarjetasAgrupaciones.Buscar(datos,new()).Propuestas);
        }
        var cupon=Pequeño([20,50]);cupon.Detalles[0].Nivel="Cupón";
        Assert.Empty(TarjetasAgrupaciones.Buscar(cupon,new()).Propuestas);
        Assert.Throws<InvalidOperationException>(()=>TarjetasAgrupaciones.Buscar(cupon,new(){MaxMovimientosGrupo=501}));
    }
}

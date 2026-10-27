using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasDeteccionCuentasTests
{
    private static readonly DateTime Fecha=new(2026,4,1);
    private static readonly IReadOnlyList<ConciliacionCuenta> Cuentas=[new(){Codigo="A",Descripcion="VISA",Moneda="1"},new(){Codigo="B",Descripcion="OTRA VISA",Moneda="1"}];
    private static TarjetasDetalle D(decimal importe,string cupon="",int cantidad=1)=>new(){Fecha=Fecha,Importe=importe,Cupon=cupon,Cantidad=cantidad};
    private static TarjetasMovimiento M(string cuenta,decimal importe,string cupon="",int dia=0)=>new(){Cuenta=cuenta,Fecha=Fecha.AddDays(dia),Importe=importe,Cupon=cupon,Sucursal="0002"};
    private static TarjetasLectura Lectura(params TarjetasDetalle[] detalles)=>new(){Tarjeta="VISA",Detalles=detalles.ToList()};
    private static IReadOnlyList<TarjetasCuentaCandidata> Evaluar(TarjetasLectura l,params TarjetasMovimiento[] movimientos)=>TarjetasDeteccionCuentas.Evaluar(l,Cuentas,movimientos,3,1);
    private static void Sugerir(TarjetasLectura l,IReadOnlyList<TarjetasCuentaCandidata> candidatas,string recordada="",bool completo=true)=>
        TarjetasDeteccionCuentas.Sugerir(l,Cuentas,candidatas,recordada,"0001",Fecha,Fecha,3,completo);

    [Fact]
    public void IdentificaPorDatosAunqueElNombreDeTarjetaSeaAmbiguoYNoReusaFilas()
    {
        var l=Lectura(D(100,"00045"),D(200));
        var candidatas=Evaluar(l,M("A",100,"45",-2),M("A",200),M("B",900));
        var a=Assert.Single(candidatas);Assert.Equal("A",a.Cuenta);Assert.Equal(1,a.PorCupon);Assert.Equal(1,a.PorFechaImporte);Assert.Equal(300,a.ImporteCoincidente);
        Sugerir(l,candidatas);Assert.Equal("A",l.CuentaSugerida);Assert.Equal("0002",l.SucursalSugerida);
        Assert.Equal(2,l.Deteccion!.CuentasAnalizadas);Assert.Equal(Fecha,l.Deteccion.Desde);
    }
    [Fact]
    public void EmpatesEntreCuentasNoSeResuelvenPorNombreNiPorOrden()
    {
        var l=Lectura(D(100));var candidatas=Evaluar(l,M("A",100),M("B",100));
        Assert.Equal(2,candidatas.Count);Sugerir(l,candidatas);Assert.Equal("",l.CuentaSugerida);
    }
    [Fact]
    public void CuponesContradictoriosGenericosYDuplicadosNoDanIdentidadFalsa()
    {
        Assert.Empty(Evaluar(Lectura(D(100,"55")),M("A",100,"66")));
        Assert.Empty(Evaluar(Lectura(D(100,"1")),M("A",100,"1",-1)));
        Assert.Empty(Evaluar(Lectura(D(100)),M("A",100),M("A",100)));
        Assert.Empty(Evaluar(Lectura(D(100),D(100)),M("A",100)));
        Assert.Empty(Evaluar(Lectura(D(100,"44")),M("A",100,"44",-4)));
    }
    [Fact]
    public void LotesRequierenCantidadYBrutoCompatiblesYNoAutoseleccionanCuenta()
    {
        var l=Lectura(D(300,"",2));var candidatas=Evaluar(l,M("A",100),M("A",200));
        Assert.Equal(1,Assert.Single(candidatas).LotesCompatibles);
        Assert.Equal(0,candidatas[0].Coincidencias);Sugerir(l,candidatas);Assert.Equal("",l.CuentaSugerida);
        Assert.Empty(Evaluar(l,M("A",300)));
        Assert.Empty(Evaluar(l,M("A",100),M("A",202)));
        Assert.Empty(Evaluar(Lectura(D(400,"",2),D(-100)),M("A",100),M("A",200)));
    }
    [Fact]
    public void AsociacionContradichaPorDatosSeDejaParaRevision()
    {
        var l=Lectura(D(100));var candidatas=Evaluar(l,M("B",100));
        Sugerir(l,candidatas,"A");Assert.Equal("",l.CuentaSugerida);Assert.Contains("recordada",l.Deteccion!.Mensaje);
        Sugerir(l,candidatas,"B");Assert.Equal("B",l.CuentaSugerida);Assert.Equal("0001",l.SucursalSugerida);
    }
    [Fact]
    public void ConsultaTruncadaNoEmiteSugerenciaNiSiquieraConAsociacion()
    {
        var l=Lectura(D(100));Sugerir(l,[],"A",false);Assert.Equal("",l.CuentaSugerida);Assert.Contains("muestra parcial",l.Deteccion!.Mensaje);
    }
    [Fact]
    public void AsociacionInvalidaNoSePrecargaYNombreSinEvidenciaSeExplica()
    {
        var l=Lectura(D(100));Sugerir(l,[],"NO-TJ");Assert.Equal("",l.CuentaSugerida);Assert.Single(l.Avisos);
        l.Tarjeta="OTRA VISA";Sugerir(l,[]);Assert.Equal("B",l.CuentaSugerida);Assert.Contains("únicamente por el nombre",l.Deteccion!.Mensaje);
    }
}

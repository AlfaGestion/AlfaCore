using System.Reflection;
using System.Text;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace AlfaCore.Tests;

public sealed class LectoresServidorTests:IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"alfacore-servidor-"+Guid.NewGuid().ToString("N"));
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
    }
    private LectoresServidorService Servicio(bool admin=true,bool autenticado=true,bool fallaAuditoria=false)
        =>new(Proxy<IWebHostEnvironment>((m,_)=>m.Name=="get_ContentRootPath"?_root:null),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch
            {
                "get_IsAuthenticated"=>autenticado,"get_CurrentUser"=>new AppUserSessionInfo{SuperAdmin=admin},_=>null
            }),
            Proxy<IAppEventService>((m,_)=>m.Name switch
            {
                "LogErrorAsync"=>Task.FromResult("ENSAYO"),
                "LogAuditAsync" when fallaAuditoria=>Task.FromException<string>(new IOException("Auditoría no disponible")),
                "LogAuditAsync"=>Task.FromResult("ENSAYO"),_=>null
            }));
    private async Task<LectoresServidorConfiguracion> ConfigurarArchivo(string carpeta="python")
    {
        var dir=Path.Combine(_root,carpeta);Directory.CreateDirectory(dir);
        var exe=Path.Combine(dir,"python.exe");await File.WriteAllTextAsync(exe,"Ensayo: no ejecutable real",new UTF8Encoding(false));
        return new(){PythonExe=exe};
    }

    [Theory]
    [InlineData("",@"C:\anterior\python.exe",@"C:\anterior\python.exe")]
    [InlineData(@"C:\Python314\python.exe",@"C:\anterior\python.exe",@"C:\Python314\python.exe")]
    [InlineData(@"C:\Python314\python.exe","",@"C:\Python314\python.exe")]
    [InlineData("","","python")]
    public void PrioridadDeInstalacionConCompatibilidadAnterior(string servidor,string anterior,string esperado)
    {
        var comun=LectoresServidorService.ResolverPython(servidor,anterior);Assert.Equal(esperado,comun);
        Assert.Equal(esperado,TarjetasPdfLector.ResolverPython("python",comun));
        Assert.Equal(@"C:\propio\python.exe",TarjetasPdfLector.ResolverPython(@"C:\propio\python.exe",comun));
    }

    [Fact]
    public async Task GuardadoEsCompartidoEntreSesionesSinModificarConfiguracionDeLasBases()
    {
        var config=await ConfigurarArchivo();var admin=Servicio();var operador=Servicio(admin:false);
        Assert.Equal(@"C:\viejo\python.exe",operador.Python(@"C:\viejo\python.exe"));
        await admin.GuardarAsync(config);
        Assert.Equal(config.PythonExe,operador.Python(@"C:\viejo\python.exe"));
        Assert.Equal(config.PythonExe,Servicio().Python(null));
        Assert.Equal(config,await admin.ObtenerAsync());
        var bytes=await File.ReadAllBytesAsync(Path.Combine(_root,"App_Data","servidor","lectores.json"));
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [Theory]
    [InlineData(false,true)]
    [InlineData(true,false)]
    public async Task OperadorOSesionNoAutenticadaNoPuedeLeerAdministracionGuardarNiEjecutar(bool admin,bool autenticado)
    {
        var config=await ConfigurarArchivo();var servicio=Servicio(admin,autenticado);
        await Assert.ThrowsAsync<AppUserFacingException>(()=>servicio.GuardarAsync(config));
        await Assert.ThrowsAsync<AppUserFacingException>(()=>servicio.ObtenerAsync());
        await Assert.ThrowsAsync<AppUserFacingException>(()=>servicio.ProbarAsync(config));
        Assert.False(File.Exists(Path.Combine(_root,"App_Data","servidor","lectores.json")));
    }

    [Fact]
    public async Task ErrorDeAuditoriaRestauraConfiguracionAnteriorYNoDejaTemporales()
    {
        var primero=await ConfigurarArchivo("primero");await Servicio().GuardarAsync(primero);
        var segundo=await ConfigurarArchivo("segundo");
        await Assert.ThrowsAsync<AppUserFacingException>(()=>Servicio(fallaAuditoria:true).GuardarAsync(segundo));
        Assert.Equal(primero,Servicio().Leer());
        Assert.Single(Directory.GetFiles(Path.Combine(_root,"App_Data","servidor")));
    }

    [Fact]
    public async Task RechazaRutaRelativaArgumentosYFalloAlArrancarSeRegistra()
    {
        var servicio=Servicio();
        await Assert.ThrowsAsync<AppUserFacingException>(()=>servicio.GuardarAsync(new(){PythonExe="python"}));
        await Assert.ThrowsAsync<AppUserFacingException>(()=>servicio.GuardarAsync(new(){PythonExe="python.exe -c algo"}));
        var config=await ConfigurarArchivo();
        await Assert.ThrowsAsync<AppUserFacingException>(()=>servicio.ProbarAsync(config));
    }

    [Fact]
    public async Task SinAuditoriaNoSeCreaPrimeraConfiguracion()
    {
        var config=await ConfigurarArchivo();
        await Assert.ThrowsAsync<AppUserFacingException>(()=>Servicio(fallaAuditoria:true).GuardarAsync(config));
        Assert.False(File.Exists(Path.Combine(_root,"App_Data","servidor","lectores.json")));
    }

    [PythonProcesoFact]
    public async Task PruebaRealCompruebaPythonYPypdfSinGuardarConfiguracion()
    {
        var salida=await Servicio().ProbarAsync(new(){PythonExe=Environment.GetEnvironmentVariable("TARJETAS_TEST_PYTHON")!});
        Assert.Contains("Python",salida);Assert.Contains("pypdf disponible",salida);
        Assert.False(File.Exists(Path.Combine(_root,"App_Data","servidor","lectores.json")));
    }

    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}

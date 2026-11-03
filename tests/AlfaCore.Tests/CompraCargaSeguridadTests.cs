using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraCargaSeguridadTests
{
    [Theory]
    [InlineData(false,true,10)]
    [InlineData(true,true,20)]
    public async Task NoAbreConexionSinSesionAutorizadaOConOtraBase(bool autenticado,bool permiso,int baseEsperada)
    {
        var conexiones=0;var sesion=new SessionDto{BaseId=10};using var runtime=new CargaComprobantesArcaRuntime();
        var service=Crear(sesion,autenticado,permiso,()=>conexiones++,runtime.Events);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ContextoAsync(baseEsperada));
        Assert.Equal(0,conexiones);
    }
    [Fact]
    public async Task PermisoDenegadoImpideConsultarSql()
    {
        var conexiones=0;using var runtime=new CargaComprobantesArcaRuntime();
        var service=Crear(new(){BaseId=10},true,false,()=>conexiones++,runtime.Events);
        await Assert.ThrowsAsync<AppUserFacingException>(()=>service.ContextoAsync(10));
        Assert.Equal(0,conexiones);
    }
    private static CompraCargaService Crear(SessionDto sesion,bool autenticado,bool permiso,Action conexion,IAppEventService eventos)
    {
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> f) where T:class
        {var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=f;return p;}
        return new(Proxy<ISessionService>((m,_)=>{
            if(m.Name=="GetActiveSession")return sesion;if(m.Name=="GetConnectionString"){conexion();throw new InvalidOperationException("No debe abrir SQL");}return null;
        }),Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>autenticado,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Sistemas",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(permiso)),eventos,null!);
    }
}

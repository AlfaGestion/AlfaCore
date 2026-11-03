using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ProveedorAltaRapidaTests
{
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var proxy=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler=handler;return proxy;
    }
    private sealed class Contexto:IDisposable
    {
        public SessionDto Sesion=new(){BaseId=10};
        public readonly CargaComprobantesArcaRuntime Runtime=new();
        public readonly List<string> Permisos=[];
        public bool Permitir=true;
        public ISessionService Sesiones=>Proxy<ISessionService>((m,_)=>m.Name=="GetActiveSession"?Sesion:throw new InvalidOperationException("No debe abrir SQL"));
        public IAppUserSessionService Usuario=>Proxy<IAppUserSessionService>((m,_)=>m.Name switch
        {"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Administrador",_=>null});
        public IPermissionService Autorizacion=>Proxy<IPermissionService>((_,a)=>{Permisos.Add((string)a![0]!);return Task.FromResult(Permitir);});
        public ProveedorAltaRapidaService Servicio(Func<MethodInfo,object?[]?,object?> cuentas)=>new(Sesiones,Usuario,Autorizacion,Proxy<ICuentasComercialesService>(cuentas),null!,Runtime.Events);
        public void Dispose()=>Runtime.Dispose();
    }
    [Fact]
    public async Task AltaReutilizaMaestroProveedorConSesionEsperadaYPermisos()
    {
        using var c=new Contexto();var solicitud=new CuentaComercialSaveRequest{NumeroDocumento="30-12345678-1",MotivoCompraCodigo="99",IvaCodigo="1",CondicionComercialCodigo="1"};
        var servicio=c.Servicio((m,a)=>{
            Assert.Equal("CrearProveedorEnSesionAsync",m.Name);Assert.Same(solicitud,a![0]);Assert.Equal(c.Sesion.Id,a[1]);return Task.FromResult("211010999");
        });
        Assert.Equal("211010999",await servicio.CrearAsync(c.Sesion.Id,10,solicitud));
        Assert.Equal(new[]{ProveedorAltaRapidaService.PermisoProveedor,CompraCargaService.MenuKey},c.Permisos);
    }
    [Fact]
    public async Task PermisoDenegadoNoInvocaMaestro()
    {
        using var c=new Contexto{Permitir=false};var servicio=c.Servicio((_,_)=>throw new Exception("No debe invocarse"));
        await Assert.ThrowsAsync<AppUserFacingException>(()=>servicio.CatalogoAsync(c.Sesion.Id,10));
    }
    [Fact]
    public async Task AltaNoAsumeCondicionesSiElOperadorNoLasSelecciono()
    {
        using var c=new Contexto();var servicio=c.Servicio((_,_)=>throw new Exception("No debe guardar"));
        await Assert.ThrowsAsync<AppUserFacingException>(()=>servicio.CrearAsync(c.Sesion.Id,10,new(){NumeroDocumento="30123456781"}));
    }
    [Fact]
    public async Task CambioDeBaseDescartaResultadoDeCatalogo()
    {
        using var c=new Contexto();var original=c.Sesion.Id;
        var servicio=c.Servicio((_,_)=>{c.Sesion=new(){BaseId=20};return Task.FromResult(new CuentaComercialLookupDataDto());});
        await Assert.ThrowsAsync<OperationCanceledException>(()=>servicio.CatalogoAsync(original,10));
    }
    [Fact]
    public async Task DuplicadosIncluyeInactivosYNormalizaDocumento()
    {
        using var c=new Contexto();var llamadas=0;
        var servicio=c.Servicio((m,a)=>{
            Assert.Equal("SearchAsync",m.Name);Assert.Equal(CuentaComercialTipo.Proveedor,a![0]);
            var filtro=Assert.IsType<CuentaComercialFilters>(a[1]);Assert.Null(filtro.Activo);llamadas++;
            return Task.FromResult(new PagedResult<CuentaComercialGridItemDto>{Items=[new(){Codigo="211010001",NumeroDocumento="30-12345678-1",Activo=false},new(){Codigo="OTRO",NumeroDocumento="20123456789"}]});
        });
        var existentes=await servicio.DuplicadosAsync(c.Sesion.Id,10,"30123456781");
        Assert.Equal(2,llamadas);Assert.Single(existentes);Assert.False(existentes[0].Activo);
    }
    [Fact]
    public async Task ServicioComunRechazaCodigoManualAntesDeAbrirSql()
    {
        using var c=new Contexto();
        var servicio=new CuentasComercialesService(new ConfigurationBuilder().Build(),c.Sesiones,c.Runtime.Events,null!,c.Usuario,c.Autorizacion);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.CrearProveedorEnSesionAsync(new(){Codigo="211010001"},c.Sesion.Id));
    }
    [Fact]
    public void EdicionDeFichaConservaMotivoDelProveedor()
    {
        var request=CuentaComercialFormulario.DesdeDetalle(new(){MotivoCompraCodigo="99",FacturaCompraSinOc=true},CuentaComercialTipo.Proveedor);
        Assert.Equal("99",request.MotivoCompraCodigo);
        Assert.True(request.FacturaCompraSinOc);
    }
}

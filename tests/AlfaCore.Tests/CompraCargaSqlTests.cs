using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraCargaSqlTests
{
    [CompraCargaSqlFact]
    public async Task PreparaReferenciaLegacyYDetectaDuplicadoSinModificarLaBase()
    {
        var conexion=Environment.GetEnvironmentVariable("COMPRA_CARGA_TEST_SQL")!;
        var b=new SqlConnectionStringBuilder(conexion);
        Assert.Equal("ALFANET",b.InitialCatalog,StringComparer.OrdinalIgnoreCase);
        Assert.Equal("SERVER-ALFAVB6",b.DataSource,StringComparer.OrdinalIgnoreCase);
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> f) where T:class
        {var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=f;return p;}
        var sesion=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var servicio=new CompraCargaService(
            Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>conexion,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Administrador",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        await using var cn=new SqlConnection(conexion);await cn.OpenAsync();
        const string foto="SELECT CONVERT(varchar(20),(SELECT COUNT(*) FROM dbo.C_MV_CPTE))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM dbo.MV_ASIENTOS))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM dbo.V_MV_STOCK))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM dbo.IA_COMPRAS_CAB))";
        var antes=await cn.ExecuteScalarAsync<string>(foto);
        var catalogo=await servicio.ContextoAsync();Assert.Contains(21m,catalogo.Tasas);
        Assert.Contains(catalogo.Tipos,t=>t.Codigo=="OC"&&!t.VistaPreviaDisponible);
        Assert.Contains(catalogo.Tipos,t=>t.Codigo=="RMC"&&!t.VistaPreviaDisponible);
        Assert.Contains(catalogo.Tipos,t=>t.Codigo=="FCC"&&t.Externo&&t.Letras.Contains('A'));
        var proveedor=await servicio.ProveedorAsync("211010002");Assert.Equal("211010002",proveedor.Codigo);
        var d=new CompraCargaBorrador{Proveedor=proveedor.Codigo,Unidad="1",Motivo="99",Letra="A",Sucursal="1",Numero="2",Fecha=new(2026,8,25),FechaContable=new(2026,8,25),Vencimiento=new(2026,9,9),TotalDocumento=12.10m,
            Lineas=[new(){Descripcion="Concepto de referencia",Neto=10,Tasa=21,Cuenta="21209"}]};
        var vista=await servicio.PrepararAsync(d);
        Assert.Equal(12.10m,vista.Total);Assert.Equal(2.10m,vista.Iva);Assert.Equal(0,vista.DiferenciaAsiento);
        Assert.Contains(vista.Asiento,a=>a.Cuenta=="11203"&&a.Debe==2.10m);
        Assert.Contains(vista.Errores,e=>e.Contains("ya existe"));Assert.Contains(vista.Avisos,a=>a.Contains("renglones contables"));
        Assert.Equal(antes,await cn.ExecuteScalarAsync<string>(foto));
    }
}
public sealed class CompraCargaSqlFactAttribute:FactAttribute
{
    public CompraCargaSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPRA_CARGA_TEST_SQL")))Skip="Relevamiento autorizado de ALFANET/SERVER-ALFAVB6. Solo SELECT; no registra compras ni modifica configuración.";}
}

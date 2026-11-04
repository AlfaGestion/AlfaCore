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
        Assert.Contains(catalogo.Tipos,t=>t.Codigo=="OC"&&t.EsComercial&&t.VistaPreviaDisponible);
        Assert.Contains(catalogo.Tipos,t=>t.Codigo=="RMC"&&t.EsComercial&&t.VistaPreviaDisponible);
        Assert.Contains(catalogo.Tipos,t=>t.Codigo=="FCC"&&t.Externo&&t.Letras.Contains('A'));
        var proveedor=await servicio.ProveedorAsync("211010002");Assert.Equal("211010002",proveedor.Codigo);
        Assert.NotEmpty(await servicio.BuscarProveedoresAsync(""));
        Assert.Contains(await servicio.BuscarProveedoresAsync(proveedor.Codigo),p=>p.Codigo==proveedor.Codigo);
        Assert.Contains(await servicio.BuscarProveedoresAsync(proveedor.Nombre),p=>p.Codigo==proveedor.Codigo);
        var cuit=new string(proveedor.Documento.Where(char.IsAsciiDigit).ToArray());
        if(cuit.Length>=6)Assert.Contains(await servicio.BuscarProveedoresAsync(cuit),p=>p.Codigo==proveedor.Codigo);
        Assert.Empty(await servicio.BuscarProveedoresAsync("%_NO_ES_UN_PROVEEDOR_["));
        // Ranking: lo que empieza igual que lo escrito va antes que lo que solo lo contiene; varias palabras en cualquier orden.
        var prefijo=proveedor.Nombre[..3];var porPrefijo=await servicio.BuscarProveedoresAsync(prefijo);
        Assert.StartsWith(prefijo,porPrefijo[0].Nombre,StringComparison.OrdinalIgnoreCase);
        var palabras=proveedor.Nombre.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(palabras.Length>1)Assert.Contains(await servicio.BuscarProveedoresAsync(string.Join(' ',palabras.Reverse())),p=>p.Codigo==proveedor.Codigo);
        var articulos=await servicio.BuscarArticulosAsync("1","1","1");Assert.Contains(articulos,a=>a.Codigo=="1");
        // Como el legacy: un artículo gravado sin alícuota en el maestro usa la general (PIVA).
        Assert.All(articulos.Where(x=>!x.Exento),x=>Assert.True(x.Tasa>0));
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

using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CompraCargaRegistroSqlTests
{
    [CompraCargaRegistroSqlFact]
    public async Task RegistraAYCConLibroYReintentoSinDuplicarYRevierteElEnsayo()
    {
        var cadena=Environment.GetEnvironmentVariable("COMPRA_REGISTRO_TEST_SQL")!;var config=new SqlConnectionStringBuilder(cadena);
        Assert.Equal("ALFANET",config.InitialCatalog,StringComparer.OrdinalIgnoreCase);Assert.Equal("SERVER-ALFAVB6",config.DataSource,StringComparer.OrdinalIgnoreCase);
        static T Proxy<T>(Func<MethodInfo,object?[]?,object?> f) where T:class
        {var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=f;return p;}
        var sesion=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var servicio=new CompraCargaService(Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>cadena,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"Administrador",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        var solicitudes=new List<CompraCargaRegistroSolicitud>();
        foreach(var(letra,proveedor) in new[]{("A","211010002"),("C","211010174")})
        {
            var d=new CompraCargaBorrador{Proveedor=proveedor,Unidad="1",Motivo="99",Letra=letra,Sucursal="9999",Numero=letra=="A"?"90000001":"90000002",
                Fecha=new(2026,8,25),FechaContable=new(2026,8,26),Vencimiento=new(2026,9,9),TotalDocumento=letra=="A"?12.1m:10m,
                Lineas=[new(){Descripcion="Ensayo transaccional de servicio",Neto=10,Tasa=letra=="A"?21:0,Cuenta="21209"}]};
            var vista=await servicio.PrepararAsync(d);Assert.Empty(vista.Errores);
            solicitudes.Add(new(Guid.NewGuid(),sesion.Id,d,vista.Huella));
        }
        await using var cn=new SqlConnection(cadena);await cn.OpenAsync();
        const string foto="SELECT CONVERT(varchar(20),(SELECT COUNT(*) FROM C_MV_CPTE))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM MV_ASIENTOS))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM V_MV_STOCK))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM MV_APLICACION))+':'+CONVERT(varchar(20),(SELECT COUNT(*) FROM V_MV_CPTEACCIONES))";
        var antes=await cn.ExecuteScalarAsync<string>(foto);
        using(var tx=cn.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
                var script=File.ReadAllText(Path.Combine(root,"src/AlfaCore/App_Data/updates/2026-11-03-003__compras_registro_operaciones.sql"));
                await cn.ExecuteAsync(script,transaction:tx);
                foreach(var solicitud in solicitudes)
                {
                    var r=await servicio.RegistrarEnTransaccionAsync(cn,tx,solicitud);
                    Assert.Equal(r,await servicio.RegistrarEnTransaccionAsync(cn,tx,solicitud));
                    Assert.Equal(1,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM C_MV_CPTE WHERE ID=@CompraId;",r,tx));
                    var a=(await cn.QueryAsync<dynamic>("SELECT * FROM MV_ASIENTOS WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro",r,tx)).ToList();
                    Assert.Equal(solicitud.Borrador.Letra=="A"?3:2,a.Count);
                    Assert.Single(a.Where(x=>(int)x.SECUENCIA==1&&(string)x.LIVA_TIPO=="COMPRAS"));
                    var fiscal=a.Single(x=>(int)x.SECUENCIA==1);Assert.Equal(r.Total,(decimal)fiscal.LIVA_TOTAL);
                    Assert.Equal(10m,(decimal)fiscal.LIVA_ImpNetoGrav);Assert.Equal(solicitud.Borrador.Letra=="A"?2.1m:0m,(decimal)fiscal.LIVA_ImpIVA);
                    Assert.Equal(0m,a.Sum(x=>(string)((IDictionary<string,object>)x)["DEBE-HABER"]=="D"?(decimal)x.IMPORTE:-(decimal)x.IMPORTE));
                    Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE SUCURSAL='9999' AND NUMERO=@numero AND (DATALENGTH(TC)<>6 OR DATALENGTH(CUENTA)<>DATALENGTH(LTRIM(RTRIM(CUENTA))))",new{numero=r.Comprobante.Substring(4,8)},tx));
                    await Assert.ThrowsAsync<InvalidOperationException>(()=>servicio.RegistrarEnTransaccionAsync(cn,tx,solicitud with{Operacion=Guid.NewGuid()}));
                }
            }
            finally {tx.Rollback();}
        }
        Assert.Equal(antes,await cn.ExecuteScalarAsync<string>(foto));
    }
}
public sealed class CompraCargaRegistroSqlFactAttribute:FactAttribute
{
    public CompraCargaRegistroSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPRA_REGISTRO_TEST_SQL")))Skip="Ensayo autorizado en ALFANET/SERVER-ALFAVB6 con ROLLBACK obligatorio; no habilitar en producción.";}
}

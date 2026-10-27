using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TarjetasCambioCuentaTests
{
    [Fact]
    public void HuellasConservanContratoHistoricoYMultiplicidadAlCambiarCuenta()
    {
        var d=new TarjetasDetalle{Id=1,Fecha=new(2026,4,1),Liquidacion="123",Lote="10",Cupon="7",Terminal="9",Importe=100};
        static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
        var lectura=new TarjetasLectura{Banco="Nación",Comercio="1234"};var cuenta="111";
        var identidad=Hash(JsonSerializer.Serialize(new{cuenta=cuenta.Trim(),lectura.Banco,lectura.Comercio,d.Fecha,d.Liquidacion,d.Lote,d.Cupon,d.Terminal,d.Importe}));
        var viejas=ConciliacionTarjetasService.HuellasDetalles(cuenta,lectura.Banco,lectura.Comercio,[d,d]);
        Assert.Equal(Hash(identidad+"|1"),viejas[0].Huella);Assert.Equal(Hash(identidad+"|2"),viejas[1].Huella);
        var nuevas=ConciliacionTarjetasService.HuellasDetalles("222",lectura.Banco,lectura.Comercio,[d,d]);
        Assert.NotEqual(viejas[0].Huella,nuevas[0].Huella);Assert.NotEqual(nuevas[0].Huella,nuevas[1].Huella);
    }

    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> handler) where T:class
    {
        var p=DispatchProxy.Create<T,CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)p).Handler=handler;return p;
    }
    [CambioCuentaSqlFact]
    public async Task CorrigeCuentaSinDuplicarConOriginalAuditoriaAsociacionYRollback()
    {
        var connection=Environment.GetEnvironmentVariable("TARJETAS_CUENTA_TEST_SQL")!;
        Assert.Equal("ELBOLSON2011_PRUEBAS",new SqlConnectionStringBuilder(connection).InitialCatalog);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
        var sesion=new SessionDto();using var runtime=new CargaComprobantesArcaRuntime();
        var service=new ConciliacionTarjetasService(Proxy<ISessionService>((m,_)=>m.Name switch{"GetActiveSession"=>sesion,"GetConnectionString"=>connection,_=>null}),
            Proxy<IAppUserSessionService>((m,_)=>m.Name switch{"get_IsAuthenticated"=>true,"IsAuthorizedForSession"=>true,"GetCurrentUserName"=>"SISTEMAS",_=>null}),
            Proxy<IPermissionService>((_,_)=>Task.FromResult(true)),runtime.Events,null!);
        var tag="ENSAYO_CUENTA_"+Guid.NewGuid().ToString("N");
        var cuentas=(await cn.QueryAsync<string>("SELECT TOP(2) LTRIM(RTRIM(CODIGO)) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='TJ' AND LTRIM(RTRIM(MONEDA))='1' AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 ORDER BY CODIGO",transaction:tx)).ToArray();
        Assert.Equal(2,cuentas.Length);var a=cuentas[0];var b=cuentas[1];
        var original=Encoding.UTF8.GetBytes("%PDF-ENSAYO-"+tag);var hash=Convert.ToHexString(SHA256.HashData(original));long id=0;
        async Task<long> Importacion(string cuenta,string hashArchivo)=>await cn.ExecuteScalarAsync<long>("""
            INSERT dbo.TJ_Importacion(Nombre,Original,Hash,Banco,Comercio,Tarjeta,Cuenta,Sucursal,Desde,Hasta,Bruto,Neto,DetalleCompleto,Avisos,Usuario)
            OUTPUT INSERTED.Id VALUES(@tag,@original,@hashArchivo,'Demo',@tag,'VISA',@cuenta,'0001','20260401','20260401',200,190,1,'','SISTEMAS');
            """,new{tag,original,hashArchivo,cuenta},tx);
        try
        {
            id=await Importacion(a,hash);
            var ds=new[]{new TarjetasDetalle{Fecha=new(2026,4,1),Liquidacion="1",Lote="2",Importe=100.00m},new TarjetasDetalle{Fecha=new(2026,4,1),Liquidacion="1",Lote="2",Importe=100.00m}};
            var iniciales=ConciliacionTarjetasService.HuellasDetalles(a,"Demo",tag,ds);
            for(var n=0;n<ds.Length;n++)ds[n].Id=await cn.ExecuteScalarAsync<long>("""
                INSERT dbo.TJ_Detalle(ImportacionId,Fecha,Liquidacion,Lote,Cupon,Terminal,Nivel,Cantidad,Importe,Huella)
                OUTPUT INSERTED.Id VALUES(@id,'20260401','1','2','','','Lote',1,100,@huella);
                """,new{id,huella=iniciales[n].Huella},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CambiarCuentaEnTransaccionAsync(cn,tx,id,a,"NO_EXISTE","Corrección de prueba",false,default));
            await service.CambiarCuentaEnTransaccionAsync(cn,tx,id,a,b,"Corrección de prueba",true,default);
            var actualizado=await cn.QuerySingleAsync<(string Cuenta,byte[] Original,string Avisos)>("SELECT Cuenta,Original,Avisos FROM dbo.TJ_Importacion WHERE Id=@id",new{id},tx);
            Assert.Equal(b,actualizado.Cuenta);Assert.Equal(original,actualizado.Original);Assert.Contains($"{a} → {b}",actualizado.Avisos);Assert.Contains("SISTEMAS",actualizado.Avisos);
            var esperadas=ConciliacionTarjetasService.HuellasDetalles(b,"Demo",tag,ds);
            Assert.Equal(esperadas.Select(x=>x.Huella),(await cn.QueryAsync<string>("SELECT Huella FROM dbo.TJ_Detalle WHERE ImportacionId=@id ORDER BY Id",new{id},tx)));
            var asociaciones=await cn.ExecuteScalarAsync<string>("SELECT CONVERT(nvarchar(max),ValorAux) FROM dbo.TA_CONFIGURACION WHERE CLAVE='TARJETAS-ASOCIACIONES'",transaction:tx);
            using(var json=JsonDocument.Parse(asociaciones!))Assert.Contains(json.RootElement.EnumerateArray(),x=>x.GetProperty("Comercio").GetString()==tag&&x.GetProperty("Cuenta").GetString()==b);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CambiarCuentaEnTransaccionAsync(cn,tx,id,a,a,"Solicitud vieja",false,default));
            var repetida=await Importacion(a,hash);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CambiarCuentaEnTransaccionAsync(cn,tx,id,b,a,"PDF repetido",false,default));
            await cn.ExecuteAsync("DELETE dbo.TJ_Importacion WHERE Id=@repetida",new{repetida},tx);
            var solapada=await Importacion(a,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tag))));
            await cn.ExecuteAsync("INSERT dbo.TJ_Detalle(ImportacionId,Fecha,Liquidacion,Lote,Cupon,Terminal,Nivel,Cantidad,Importe,Huella) VALUES(@solapada,'20260401','1','2','','','Lote',1,100,@huella)",new{solapada,huella=iniciales[0].Huella},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CambiarCuentaEnTransaccionAsync(cn,tx,id,b,a,"Operaciones repetidas",false,default));
            await cn.ExecuteAsync("INSERT dbo.TJ_Vinculo(ImportacionId,Activo,ImporteResumen,ImporteAlfa,Motivo,Usuario) VALUES(@id,0,100,100,'Ensayo anulado','SISTEMAS')",new{id},tx);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CambiarCuentaEnTransaccionAsync(cn,tx,id,b,a,"Con historial",false,default));
            Assert.Equal(b,await cn.ExecuteScalarAsync<string>("SELECT Cuenta FROM dbo.TJ_Importacion WHERE Id=@id",new{id},tx));
        }
        finally{tx.Rollback();}
        Assert.Equal(0,await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TJ_Importacion WHERE Id=@id",new{id}));
    }
}
public sealed class CambioCuentaSqlFactAttribute:FactAttribute
{
    public CambioCuentaSqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TARJETAS_CUENTA_TEST_SQL")))Skip="Requiere conexión autorizada a ELBOLSON2011_PRUEBAS.";}
}

using System.Data;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    private static string IdentidadDetalle(string cuenta,string banco,string comercio,TarjetasDetalle d)
        =>Hash(JsonSerializer.Serialize(new{cuenta=cuenta.Trim(),Banco=banco,Comercio=comercio,d.Fecha,d.Liquidacion,d.Lote,d.Cupon,d.Terminal,d.Importe}));

    internal static IReadOnlyList<(long Id,string Huella)> HuellasDetalles(string cuenta,string banco,string comercio,IEnumerable<TarjetasDetalle> detalles)
    {
        var repeticiones=new Dictionary<string,int>();var resultado=new List<(long,string)>();
        foreach(var d in detalles)
        {
            var identidad=IdentidadDetalle(cuenta,banco,comercio,d);
            repeticiones.TryGetValue(identidad,out var ordinal);repeticiones[identidad]=++ordinal;
            resultado.Add((d.Id,Hash(identidad+"|"+ordinal)));
        }
        return resultado;
    }

    public Task<bool> CambiarCuentaAsync(long id,string cuentaAnterior,string cuentaNueva,string motivo,bool recordar,CancellationToken ct=default)
        =>Ejecutar("CambiarCuenta",async(c,t)=>
        {
            using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            await CambiarCuentaEnTransaccionAsync(c,tx,id,cuentaAnterior,cuentaNueva,motivo,recordar,t);
            tx.Commit();return true;
        },ct);

    internal async Task CambiarCuentaEnTransaccionAsync(SqlConnection c,SqlTransaction tx,long id,string cuentaAnterior,string cuentaNueva,string motivo,bool recordar,CancellationToken ct)
    {
        var session=sessions.GetActiveSession()!.Id;
        cuentaNueva=cuentaNueva.Trim();cuentaAnterior=cuentaAnterior.Trim();motivo=motivo.Trim();
        if(cuentaNueva.Length is <1 or >15||cuentaAnterior.Length is <1 or >15||motivo.Length is <3 or >200)
            throw new InvalidOperationException("Seleccioná la cuenta correcta y escribí un motivo de entre 3 y 200 caracteres.");
        var admiteAnulacion=await TieneAnulacionAsync(c,ct,tx);
        var origen=await c.QuerySingleOrDefaultAsync<CuentaOrigen>(Cmd("SELECT Cuenta,Hash,Banco,Comercio,Tarjeta,Sucursal"+(admiteAnulacion?",Anulada":"")+" FROM dbo.TJ_Importacion WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id",new{id},ct,tx))
            ??throw new InvalidOperationException("La importación no está disponible.");
        if(origen.Anulada)throw new InvalidOperationException("La importación está anulada. Volvé a importar el documento para elegir otra cuenta.");
        if(origen.Cuenta.Trim()!=cuentaAnterior)throw new InvalidOperationException("La cuenta cambió en otra sesión. Actualizá la importación antes de corregirla.");
        if(cuentaNueva==cuentaAnterior)throw new InvalidOperationException("Elegí una cuenta diferente de la actual.");
        if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.TJ_Vinculo WITH(UPDLOCK,HOLDLOCK) WHERE ImportacionId=@id",new{id},ct,tx))>0)
            throw new InvalidOperationException("La importación tiene historial de conciliación. No se puede cambiar su cuenta, aunque los vínculos estén anulados.");
        if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO))=@cuentaNueva AND LTRIM(RTRIM(MEDIODEPAGO))='TJ' AND LTRIM(RTRIM(MONEDA))='1' AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0",new{cuentaNueva},ct,tx))!=1)
            throw new InvalidOperationException("Elegí una cuenta de tarjetas TJ activa, imputable y en pesos.");
        if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.TJ_Importacion WITH(UPDLOCK,HOLDLOCK) WHERE Hash=@Hash AND Cuenta=@cuentaNueva AND Id<>@id"+(admiteAnulacion?" AND Anulada=0":""),new{origen.Hash,cuentaNueva,id},ct,tx))>0)
            throw new InvalidOperationException("Este PDF ya está importado en la cuenta elegida. Abrí esa importación; no se duplicaron sus movimientos.");
        var detalles=(await c.QueryAsync<TarjetasDetalle>(Cmd("SELECT Id,Fecha,Liquidacion,Lote,Cupon,Terminal,Importe FROM dbo.TJ_Detalle WITH(UPDLOCK,HOLDLOCK) WHERE ImportacionId=@id ORDER BY Id",new{id},ct,tx))).AsList();
        var viejas=HuellasDetalles(cuentaAnterior,origen.Banco,origen.Comercio,detalles);
        var guardadas=(await c.QueryAsync<(long Id,string Huella)>(Cmd("SELECT Id,Huella FROM dbo.TJ_Detalle WHERE ImportacionId=@id",new{id},ct,tx))).ToDictionary(x=>x.Id,x=>x.Huella);
        if(detalles.Count==0||viejas.Any(x=>!string.Equals(guardadas[x.Id],x.Huella,StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Los datos de la importación cambiaron. Revisá su integridad antes de corregir la cuenta.");
        var nuevas=HuellasDetalles(cuentaNueva,origen.Banco,origen.Comercio,detalles);
        foreach(var lote in nuevas.Chunk(500))
            if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.TJ_Detalle WITH(UPDLOCK,HOLDLOCK) WHERE Huella IN @huellas AND ImportacionId<>@id"+(admiteAnulacion?" AND ImportacionActiva=1":""),new{huellas=lote.Select(x=>x.Huella).ToArray(),id},ct,tx))>0)
                throw new InvalidOperationException("Hay operaciones ya importadas en la cuenta elegida. No se cambió la cuenta ni se duplicaron movimientos.");
        foreach(var detalle in nuevas)
            await c.ExecuteAsync(Cmd("UPDATE dbo.TJ_Detalle SET Huella=@Huella WHERE Id=@Id AND ImportacionId=@importacion",new{detalle.Id,detalle.Huella,importacion=id},ct,tx));
        var auditoria=$"Cambio de cuenta {cuentaAnterior} → {cuentaNueva}. {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC · {users.GetCurrentUserName()}: {motivo}";
        await c.ExecuteAsync(Cmd("UPDATE dbo.TJ_Importacion SET Cuenta=@cuentaNueva,Avisos=CONCAT(Avisos,CHAR(10),@auditoria) WHERE Id=@id",new{cuentaNueva,auditoria,id},ct,tx));
        if(recordar)
        {
            var reglas=JsonSerializer.Deserialize<List<Asociacion>>(await Config(c,"TARJETAS-ASOCIACIONES",ct,tx))??[];
            reglas.RemoveAll(x=>x.Banco==origen.Banco&&x.Comercio==origen.Comercio&&x.Tarjeta==origen.Tarjeta);
            reglas.Add(new(origen.Banco,origen.Comercio,origen.Tarjeta,cuentaNueva,origen.Sucursal));
            if(await c.ExecuteAsync(Cmd("UPDATE dbo.TA_CONFIGURACION SET VALOR='',ValorAux=@json,FechaHora_Modificacion=GETDATE() WHERE CLAVE='TARJETAS-ASOCIACIONES'",new{json=JsonSerializer.Serialize(reglas)},ct,tx))!=1)
                throw new InvalidOperationException("Falta la configuración de asociaciones de tarjetas. Aplicá las actualizaciones antes de recordar la cuenta.");
        }
        AntesDeGrabar(session,ct);
    }
    private sealed class CuentaOrigen
    {
        public bool Anulada {get;set;}
        public string Cuenta {get;set;}="";
        public string Hash {get;set;}="";
        public string Banco {get;set;}="";
        public string Comercio {get;set;}="";
        public string Tarjeta {get;set;}="";
        public string Sucursal {get;set;}="";
    }
}

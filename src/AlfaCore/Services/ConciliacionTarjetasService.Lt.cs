using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    private const string LtConfigClave="TARJETAS-LT-CUENTAS";
    private sealed record LtRegla(string Banco,string Comercio,string Tarjeta,string CuentaTarjeta,List<TarjetasLtAsignacion> Cuentas);
    private static Task<IEnumerable<TarjetasLtCuenta>> LtCuentas(SqlConnection c,CancellationToken t,SqlTransaction? tx=null)=>c.QueryAsync<TarjetasLtCuenta>(Cmd("""
        SELECT LTRIM(RTRIM(c.CODIGO)) Codigo,c.DESCRIPCION Descripcion,LTRIM(RTRIM(ISNULL(c.MEDIODEPAGO,''))) Medio
        FROM dbo.MA_CUENTAS c
        WHERE ISNULL(c.TITULO,0)=0 AND ISNULL(c.Dada_De_Baja,0)=0 AND LTRIM(RTRIM(c.MONEDA))='1'
          AND LTRIM(RTRIM(ISNULL(c.MEDIODEPAGO,''))) IN ('','DB','RI','RB','RG','RO')
          AND NOT EXISTS(SELECT 1 FROM dbo.VT_CLIENTES x WHERE x.CODIGO=c.CODIGO)
          AND NOT EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES x WHERE x.CODIGO=c.CODIGO)
        ORDER BY c.DESCRIPCION,c.CODIGO
        """,null,t,tx));
    public Task<TarjetasLtVista> PrepararLtAsync(long id,CancellationToken ct=default)=>Ejecutar("PrepararLT",async(c,t)=>
    {
        var i=await ObtenerLtImportacion(c,id,t);
        var original=await c.QuerySingleAsync<Original>(Cmd("SELECT Nombre,Original Contenido,Hash FROM dbo.TJ_Importacion WHERE Id=@id",new{id},t));
        if(Convert.ToHexString(SHA256.HashData(original.Contenido))!=original.Hash)throw new InvalidOperationException("El PDF original no coincide con su huella. Solicitá revisión a soporte.");
        var lectura=await lector.LeerAsync(new(original.Nombre,original.Contenido),await PythonAsync(c,t),t);
        var cuentas=(await LtCuentas(c,t)).AsList();
        var reglas=JsonSerializer.Deserialize<List<LtRegla>>(await ConfigLt(c,t))??[];
        var guardada=reglas.SingleOrDefault(r=>r.Banco==i.Banco&&r.Comercio==i.Comercio&&r.Tarjeta==i.Tarjeta&&r.CuentaTarjeta==i.Cuenta);
        var asignaciones=(guardada?.Cuentas??TarjetasLtPreparacion.Sugerir(cuentas).ToList())
            .Select(r=>new TarjetasLtAsignacion{Rol=r.Rol,Cuenta=cuentas.Any(x=>x.Codigo==r.Cuenta&&TarjetasLtPreparacion.Elegible(x,r.Rol))?r.Cuenta:""}).ToList();
        var avisos=new List<string>();
        if(lectura.LiquidacionesLt.Count==0)avisos.Add("Este formato todavía no ofrece un desglose económico verificable para LT. Conservamos la conciliación; revisá el PDF original.");
        if(!lectura.DetalleCompleto)avisos.Add("El detalle de operaciones no coincide con el bruto del archivo; revisá el original.");
        if(lectura.LiquidacionesLt.GroupBy(l=>(l.Numero,l.Fecha,l.EsResumenMensual)).Any(g=>g.Count()>1))
            avisos.Add("Hay bloques económicos con el mismo número y fecha. Revisá el PDF: no se unificaron ni descartaron automáticamente.");
        if(lectura.LiquidacionesLt.Sum(l=>l.Conceptos.Where(x=>x.Rol=="Tarjeta").Sum(x=>x.Importe))!=lectura.Bruto||lectura.LiquidacionesLt.Sum(l=>l.Neto)!=lectura.Neto)
            avisos.Add("Los bloques económicos extraídos no cubren exactamente el bruto y el neto del resumen. No se completaron importes faltantes.");
        return new TarjetasLtVista(i.Id,i.Banco,i.Comercio,i.Tarjeta,i.Cuenta,lectura.LiquidacionesLt,cuentas,asignaciones,avisos);
    },ct);
    private static async Task<TarjetasImportacion> ObtenerLtImportacion(SqlConnection c,long id,CancellationToken t,SqlTransaction? tx=null)
    {
        var i=await c.QuerySingleOrDefaultAsync<TarjetasImportacion>(Cmd("SELECT * FROM dbo.TJ_Importacion WHERE Id=@id",new{id},t,tx))??throw new InvalidOperationException("No se encontró la importación.");
        if(i.Anulada)throw new InvalidOperationException("La importación está anulada.");
        return i;
    }
    private static async Task<string> ConfigLt(SqlConnection c,CancellationToken t,SqlTransaction? tx=null)=>
        await c.ExecuteScalarAsync<string?>(Cmd("SELECT CONVERT(nvarchar(max),ValorAux) FROM dbo.TA_CONFIGURACION WHERE CLAVE=@clave",new{clave=LtConfigClave},t,tx))??"[]";
    public Task<bool> GuardarLtCuentasAsync(long id,IReadOnlyList<TarjetasLtAsignacion> cuentas,CancellationToken ct=default)=>Ejecutar("GuardarCuentasLT",async(c,t)=>
    {
        var session=sessions.GetActiveSession()!.Id;
        using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        var i=await ObtenerLtImportacion(c,id,t,tx);
        var catalogo=(await LtCuentas(c,t,tx)).AsList();TarjetasLtPreparacion.Validar(cuentas,catalogo);
        var reglas=JsonSerializer.Deserialize<List<LtRegla>>(await ConfigLt(c,t,tx))??[];
        reglas.RemoveAll(r=>r.Banco==i.Banco&&r.Comercio==i.Comercio&&r.Tarjeta==i.Tarjeta&&r.CuentaTarjeta==i.Cuenta);
        reglas.Add(new(i.Banco,i.Comercio,i.Tarjeta,i.Cuenta,cuentas.Select(r=>new TarjetasLtAsignacion{Rol=r.Rol,Cuenta=r.Cuenta.Trim()}).ToList()));
        await c.ExecuteAsync(Cmd("""
            IF EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WITH(UPDLOCK,HOLDLOCK) WHERE CLAVE=@clave)
                UPDATE dbo.TA_CONFIGURACION SET VALOR='JSON',ValorAux=@json,FechaHora_Modificacion=GETDATE() WHERE CLAVE=@clave;
            ELSE INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,ValorAux,DESCRIPCION)
                VALUES('CAJA',@clave,'JSON',@json,N'Cuentas para vista previa LT de tarjetas');
            """,new{clave=LtConfigClave,json=JsonSerializer.Serialize(reglas)},t,tx));
        AntesDeGrabar(session,t);tx.Commit();return true;
    },ct);
}

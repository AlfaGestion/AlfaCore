using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService(ISessionService sessions,IAppUserSessionService users,
    IPermissionService permissions,IAppEventService events,TarjetasPdfLector lector,IConciliacionIaModelo? iaModelo=null)
{
    public const string MenuKey="D75-TARJETAS-IA";
    private async Task<T> Ejecutar<T>(string accion,Func<SqlConnection,CancellationToken,Task<T>> work,CancellationToken ct)
    {
        if(!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var session)) throw new InvalidOperationException("Iniciá sesión en la base donde querés conciliar.");
        void Sesion(){if(!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var actual)||actual!=session) throw new OperationCanceledException("La base activa cambió.");}
        try
        {
            if(!await permissions.HasAccessAsync(MenuKey,ct)) throw new InvalidOperationException("No tenés permiso para conciliar tarjetas.");
            Sesion();await using var cn=new SqlConnection(sessions.GetConnectionString());await cn.OpenAsync(ct);
            var result=await work(cn,ct);ct.ThrowIfCancellationRequested();Sesion();return result;
        }
        catch(OperationCanceledException){throw;}
        catch(AppUserFacingException){throw;}
        catch(Exception ex)
        {
            var message=ex is InvalidOperationException ? ex.Message : "No se pudo completar la conciliación de tarjetas. Conservamos la selección para reintentar.";
            var code=await events.LogErrorAsync("ConciliacionTarjetas",accion,ex,message,
                data:ex.InnerException is not null?new{Diagnostico=ex.InnerException.Message}:null,ct:ct);
            throw new AppUserFacingException(message,code,ex);
        }
    }
    private static CommandDefinition Cmd(string sql,object? args,CancellationToken ct,SqlTransaction? tx=null)=>new(sql,args,tx,commandTimeout:60,cancellationToken:ct);
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private void AntesDeGrabar(Guid session,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var actual)||actual!=session) throw new OperationCanceledException();
    }
    private static async Task<string> Config(SqlConnection cn,string clave,CancellationToken ct,SqlTransaction? tx=null)
        => await cn.ExecuteScalarAsync<string?>(Cmd("SELECT COALESCE(NULLIF(RTRIM(VALOR),''),CONVERT(nvarchar(max),ValorAux)) FROM dbo.TA_CONFIGURACION WHERE CLAVE=@clave",new{clave},ct,tx)) ?? "";
    private static async Task<string> PythonAsync(SqlConnection cn,CancellationToken ct)
        => TarjetasPdfLector.ResolverPython(await Config(cn,"TARJETAS-PYTHON",ct),
            await Config(cn,"InterfacesIaComprasPythonExe",ct));
    public Task<IReadOnlyList<ConciliacionCuenta>> CuentasAsync(CancellationToken ct=default)=>Ejecutar<IReadOnlyList<ConciliacionCuenta>>("Cuentas",async(c,t)=>
        (await c.QueryAsync<ConciliacionCuenta>(Cmd(CuentasTarjetasSql,null,t))).AsList(),ct);
    public Task<TarjetasLectura> LeerAsync(ConciliacionArchivo archivo,CancellationToken ct=default)=>Ejecutar("LeerPDF",async(c,t)=>
    {
        var lectura=await lector.LeerAsync(archivo,await PythonAsync(c,t),t);
        await DetectarCuenta(c,lectura,t);
        return lectura;
    },ct);
    public Task<long> ImportarAsync(ConciliacionArchivo archivo,string cuenta,string sucursal,bool recordar,CancellationToken ct=default)=>Ejecutar("Importar",async(c,t)=>
    {
        // Se relee el original; nunca se confía en totales manipulables desde la UI.
        var lectura=await lector.LeerAsync(archivo,await PythonAsync(c,t),t);
        if(sucursal.Length>10)throw new InvalidOperationException("La sucursal admite hasta diez caracteres.");
        var session=sessions.GetActiveSession()!.Id;
        using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        var ok=await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO))=@cuenta AND LTRIM(RTRIM(MEDIODEPAGO))='TJ' AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 AND LTRIM(RTRIM(MONEDA))='1'",new{cuenta=cuenta.Trim()},t,tx));
        if(ok!=1)throw new InvalidOperationException("Elegí una cuenta activa de tarjetas (TJ) en pesos.");
        var hash=Convert.ToHexString(SHA256.HashData(archivo.Contenido));
        var admiteAnulacion=await TieneAnulacionAsync(c,t,tx);
        if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.TJ_Importacion WITH(UPDLOCK,HOLDLOCK) WHERE Hash=@hash AND Cuenta=@cuenta"+(admiteAnulacion?" AND Anulada=0":""),new{hash,cuenta=cuenta.Trim()},t,tx))>0)
            throw new InvalidOperationException("Este documento ya fue importado para la cuenta. Abrilo desde Importaciones.");
        var id=await c.ExecuteScalarAsync<long>(Cmd("""
            INSERT dbo.TJ_Importacion(Nombre,Original,Hash,Banco,Comercio,Tarjeta,Cuenta,Sucursal,Desde,Hasta,Bruto,Neto,DetalleCompleto,Avisos,Usuario)
            OUTPUT INSERTED.Id VALUES(@nombre,@original,@hash,@Banco,@Comercio,@Tarjeta,@cuenta,@sucursal,@desde,@hasta,@Bruto,@Neto,@DetalleCompleto,@avisos,@usuario)
            """,new{nombre=Path.GetFileName(archivo.Nombre),original=archivo.Contenido,hash,lectura.Banco,lectura.Comercio,lectura.Tarjeta,cuenta=cuenta.Trim(),sucursal=sucursal.Trim(),desde=lectura.Detalles.Min(x=>x.Fecha),hasta=lectura.Detalles.Max(x=>x.Fecha),lectura.Bruto,lectura.Neto,lectura.DetalleCompleto,avisos=string.Join(" ",lectura.Avisos),usuario=users.GetCurrentUserName()},t,tx));
        var ocurrencias=new Dictionary<string,int>();
        foreach(var d in lectura.Detalles)
        {
            var identidad=IdentidadDetalle(cuenta,lectura.Banco,lectura.Comercio,d);
            ocurrencias.TryGetValue(identidad,out var ordinal);ocurrencias[identidad]=++ordinal;
            var huella=Hash(identidad+"|"+ordinal);
            if(await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.TJ_Detalle WITH(UPDLOCK,HOLDLOCK) WHERE Huella=@huella"+(admiteAnulacion?" AND ImportacionActiva=1":""),new{huella},t,tx))>0)
                throw new InvalidOperationException("El documento contiene operaciones ya importadas. No se duplicó ninguna: revisá las importaciones existentes.");
            await c.ExecuteAsync(Cmd("INSERT dbo.TJ_Detalle(ImportacionId,Fecha,FechaPago,Liquidacion,Lote,Cupon,Terminal,Nivel,Cantidad,Importe,Huella) VALUES(@id,@Fecha,@FechaPago,@Liquidacion,@Lote,@Cupon,@Terminal,@Nivel,@Cantidad,@Importe,@huella)",new{id,d.Fecha,d.FechaPago,d.Liquidacion,d.Lote,d.Cupon,d.Terminal,d.Nivel,d.Cantidad,d.Importe,huella},t,tx));
        }
        if(recordar)
        {
            var reglas=JsonSerializer.Deserialize<List<Asociacion>>(await Config(c,"TARJETAS-ASOCIACIONES",t,tx))??[];
            reglas.RemoveAll(x=>x.Banco==lectura.Banco&&x.Comercio==lectura.Comercio&&x.Tarjeta==lectura.Tarjeta);
            reglas.Add(new(lectura.Banco,lectura.Comercio,lectura.Tarjeta,cuenta.Trim(),sucursal.Trim()));
            await c.ExecuteAsync(Cmd("UPDATE dbo.TA_CONFIGURACION SET VALOR='',ValorAux=@json,FechaHora_Modificacion=GETDATE() WHERE CLAVE='TARJETAS-ASOCIACIONES'",new{json=JsonSerializer.Serialize(reglas)},t,tx));
        }
        AntesDeGrabar(session,t);tx.Commit();return id;
    },ct);
    public Task<IReadOnlyList<TarjetasImportacion>> ImportacionesAsync(CancellationToken ct=default)=>ImportacionesAsync(false,ct);
    public Task<IReadOnlyList<TarjetasImportacion>> ImportacionesAsync(bool incluirAnuladas,CancellationToken ct=default)=>Ejecutar<IReadOnlyList<TarjetasImportacion>>("Importaciones",async(c,t)=>
    {
        var admiteAnulacion=await TieneAnulacionAsync(c,t);
        return (await c.QueryAsync<TarjetasImportacion>(Cmd("SELECT TOP(500) i.Id,i.Nombre,i.Banco,i.Comercio,i.Tarjeta,i.Cuenta,i.Sucursal,i.Desde,i.Hasta,i.Bruto,i.Neto,i.DetalleCompleto,i.Avisos,"
            +(admiteAnulacion?"i.Anulada,":"")
            +"(SELECT COUNT(*) FROM dbo.TJ_Detalle d WHERE d.ImportacionId=i.Id AND NOT EXISTS(SELECT 1 FROM dbo.TJ_VinculoDetalle v WHERE v.DetalleId=d.Id AND v.Activo=1)) Pendientes FROM dbo.TJ_Importacion i"
            +(admiteAnulacion&&!incluirAnuladas?" WHERE i.Anulada=0":"")+" ORDER BY i.Id DESC",null,t))).AsList();
    },ct);
    public Task<ConciliacionArchivo> OriginalAsync(long id,CancellationToken ct=default)=>Ejecutar("Original",async(c,t)=>
    {
        var row=await c.QuerySingleOrDefaultAsync<Original>(Cmd("SELECT Nombre,Original Contenido,Hash FROM dbo.TJ_Importacion WHERE Id=@id",new{id},t))??throw new InvalidOperationException("No se encontró la importación.");
        if(Convert.ToHexString(SHA256.HashData(row.Contenido))!=row.Hash)throw new InvalidOperationException("El original no coincide con su huella. Solicitá revisión a soporte.");
        return new ConciliacionArchivo(row.Nombre,row.Contenido);
    },ct);
    public Task<TarjetasComparacion> CompararAsync(TarjetasFiltro filtro,CancellationToken ct=default)=>Ejecutar("Comparar",(c,t)=>Comparar(c,filtro,t),ct);
    private async Task<TarjetasComparacion> Comparar(SqlConnection c,TarjetasFiltro f,CancellationToken ct,SqlTransaction? tx=null)
    {
        if(f.Desde.Date>f.Hasta.Date||f.Sucursal.Length>10||f.Buscar.Length>200||f.Estado is not("Todos" or "Pendientes" or "Conciliados"))throw new InvalidOperationException("Revisá las fechas y los filtros.");
        var i=await c.QuerySingleOrDefaultAsync<TarjetasImportacion>(Cmd("SELECT * FROM dbo.TJ_Importacion WHERE Id=@Importacion",f,ct,tx))??throw new InvalidOperationException("No se encontró la importación.");
        if(i.Anulada)throw new InvalidOperationException("La importación está anulada. Volvé a importar el documento para conciliarlo.");
        var detalles=(await c.QueryAsync<TarjetasDetalle>(Cmd("SELECT d.*,v.VinculoId Vinculo FROM dbo.TJ_Detalle d LEFT JOIN dbo.TJ_VinculoDetalle v ON v.DetalleId=d.Id AND v.Activo=1 WHERE d.ImportacionId=@Importacion ORDER BY d.Fecha,d.Id",f,ct,tx))).AsList();
        var movimientos=(await c.QueryAsync<TarjetasMovimiento>(Cmd(MovimientosAlfaSql,new{cuenta=i.Cuenta,desde=f.Desde.Date,hasta=f.Hasta.Date,sucursal=f.Sucursal.Trim(),usuario=users.GetCurrentUserName()},ct,tx))).AsList();
        if(movimientos.Count>10000)throw new InvalidOperationException("Acotá el período: hay más de 10.000 operaciones para revisar.");
        if(movimientos.Any(m=>string.IsNullOrWhiteSpace(m.Cupon)||m.Cupon.Trim()=="1"))
            i.Avisos+=" Hay movimientos de Alfa sin número de cupón identificable. Revisá las coincidencias por fecha, presentación e importe; no prueban identidad individual.";
        if(movimientos.Any(m=>m.Presentacion<=0))
            i.Avisos+=" Hay cobranzas de Ventas sin número de presentación. Se incluyen por su cuenta de tarjeta; revisá fecha, cupón e importe contra la liquidación.";
        var vinculos=(await c.QueryAsync<(string Clave,string Huella,long Vinculo)>(Cmd("SELECT Clave,Huella,VinculoId Vinculo FROM dbo.TJ_VinculoAlfa WHERE Cuenta=@Cuenta AND Activo=1",new{i.Cuenta},ct,tx))).ToDictionary(x=>x.Clave);
        // Compatibilidad con vínculos previos: la huella desambigua la clave antigua incompleta.
        foreach(var grupo in movimientos.GroupBy(x=>x.ClaveAnterior))
        foreach(var m in grupo)
        {
            if(vinculos.TryGetValue(m.Clave,out var v)
                ||(vinculos.TryGetValue(m.ClaveAnterior,out v)&&(m.Huella==v.Huella||!grupo.Any(x=>x.Huella==v.Huella))))
            {m.Vinculo=v.Vinculo;m.Alterado=m.Huella!=v.Huella;}
        }
        if(!decimal.TryParse(await Config(c,"TARJETAS-TOLERANCIA",ct,tx),NumberStyles.Number,CultureInfo.InvariantCulture,out var tolerancia)||tolerancia<0||tolerancia>100)throw new InvalidOperationException("Revisá TARJETAS-TOLERANCIA (0 a 100, con punto decimal).");
        bool Estado(long? v)=>f.Estado=="Todos"||(f.Estado=="Pendientes" ? v is null : v is not null);
        return new(i,detalles.Where(d=>Estado(d.Vinculo)&&(f.Buscar.Length==0||$"{d.Lote} {d.Cupon} {d.Liquidacion} {d.Importe}".Contains(f.Buscar,StringComparison.OrdinalIgnoreCase))).ToList(),
            movimientos.Where(m=>(f.Estado=="Todos"||(f.Estado=="Pendientes" ? m.Vinculo is null&&!m.LegacyLiquidado : m.Vinculo is not null||m.LegacyLiquidado))&&(f.Buscar.Length==0||$"{m.Detalle} {m.Comprobante} {m.Importe} {m.Presentacion}".Contains(f.Buscar,StringComparison.OrdinalIgnoreCase))).ToList(),tolerancia);
    }
    public Task<long> ConfirmarAsync(TarjetasConfirmar solicitud,CancellationToken ct=default)=>Ejecutar("Confirmar",async(c,t)=>
    {
        var session=sessions.GetActiveSession()!.Id;using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        var actual=await Comparar(c,solicitud.Filtro with{Estado="Todos",Buscar=""},t,tx);
        var grupo=ResolverConfirmacion(actual,solicitud);
        var id=await InsertarVinculo(c,tx,actual,grupo,solicitud.Motivo,t);
        AntesDeGrabar(session,t);tx.Commit();return id;
    },ct);
    public static void ValidarDiferencia(decimal resumen,decimal alfa,decimal tolerancia,string motivo)
    {
        if(Math.Abs(resumen-alfa)>tolerancia)throw new InvalidOperationException("La diferencia supera la tolerancia configurada.");
        if(resumen!=alfa&&string.IsNullOrWhiteSpace(motivo))throw new InvalidOperationException("Escribí el motivo para aceptar la diferencia sin cambiar importes.");
    }
    private sealed record Asociacion(string Banco,string Comercio,string Tarjeta,string Cuenta,string Sucursal);
    private sealed class Original {public string Nombre{get;set;}="";public byte[] Contenido{get;set;}=[];public string Hash{get;set;}="";}
}

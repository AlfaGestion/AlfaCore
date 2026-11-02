using System.Security.Cryptography;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class TarjetasRecepcionService(ISessionService sessions,IAppUserSessionService users,
    IPermissionService permissions,IAppEventService events,IConfiguration config,TarjetasPdfLector lector,
    IConciliacionIaModelo modelo,LectoresServidorService lectores)
{
    public const string Tipo="LIQUIDACION_TARJETAS";
    public Task VerificarAccesoAsync(CancellationToken ct=default)=>Ejecutar("VerificarAcceso",(_,_,_)=>Task.FromResult(true),ct);
    private static CommandDefinition Cmd(string sql,object? args,CancellationToken ct,SqlTransaction? tx=null)
        =>new(sql,args,tx,commandTimeout:60,cancellationToken:ct);
    private static Task<int> Disponible(SqlConnection c,CancellationToken ct)
        =>c.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN OBJECT_ID(N'dbo.TJ_Recepcion',N'U') IS NULL THEN 0 ELSE 1 END",null,ct));
    private async Task<T> Ejecutar<T>(string accion,Func<SqlConnection,CancellationToken,Action,Task<T>> work,CancellationToken ct)
    {
        try
        {
            if(!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var sesion))
                throw new InvalidOperationException("Ingresá con un usuario autorizado para conciliar tarjetas en esta base.");
            var autor=users.GetCurrentUserName();var sistema=users.CurrentUser?.SystemCode;
            void Validar()
            {
                if(!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var actual)||actual!=sesion
                    ||users.GetCurrentUserName()!=autor||users.CurrentUser?.SystemCode!=sistema)throw new OperationCanceledException("La base o el usuario cambió.");
            }
            if(!await permissions.HasAccessAsync(ConciliacionTarjetasService.MenuKey,ct))throw new InvalidOperationException("No tenés permiso para conciliar tarjetas.");
            Validar();
            await using var c=new SqlConnection(sessions.GetConnectionString());await c.OpenAsync(ct);
            var valor=await work(c,ct,Validar);
            Validar();return valor;
        }
        catch(OperationCanceledException){throw;}
        catch(AppUserFacingException){throw;}
        catch(Exception ex){var id=await events.LogErrorAsync("TarjetasRecepcion",accion,ex,"No se pudo completar la recepción de tarjetas.",ct:ct);throw new AppUserFacingException(ex is InvalidOperationException?ex.Message:"No se pudo completar la recepción de tarjetas.",id,ex);}
    }
    public Task EncolarAsync(long documento,ConciliacionArchivo archivo,CancellationToken ct=default,Guid? sesionEsperada=null)=>Ejecutar("Encolar",async(c,t,validar)=>
    {
        if(sesionEsperada.HasValue&&sessions.GetActiveSession()?.Id!=sesionEsperada)throw new OperationCanceledException();
        if(await Disponible(c,t)==0)throw new InvalidOperationException("Ejecutá las actualizaciones de base para habilitar la recepción de tarjetas.");
        ValidarArchivo(archivo);
        var hash=Convert.ToHexString(SHA256.HashData(archivo.Contenido));
        var usuario=users.GetCurrentUserName().Trim();var sistema=users.CurrentUser?.SystemCode?.Trim()??"";
        if(usuario.Length==0||sistema.Length==0)throw new InvalidOperationException("Volvé a iniciar sesión con el usuario del sistema.");
        using var tx=c.BeginTransaction(System.Data.IsolationLevel.Serializable);
        var valido=await c.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM dbo.INT_COMPROBANTE_RECIBIDO d
            JOIN dbo.INT_TIPO_DOCUMENTO t ON t.IdTipoDocumento=d.IdTipoDocumento
            JOIN dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO a ON a.IdComprobanteRecibido=d.IdComprobanteRecibido AND ISNULL(a.Eliminado,0)=0
            WHERE d.IdComprobanteRecibido=@documento AND ISNULL(d.Eliminado,0)=0 AND t.Codigo=@tipo
              AND a.ContentHash=@hash
              AND (SELECT COUNT(*) FROM dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO x WHERE x.IdComprobanteRecibido=d.IdComprobanteRecibido AND ISNULL(x.Eliminado,0)=0)=1
            """,new{documento,tipo=Tipo,hash},t,tx));
        if(valido!=1)throw new InvalidOperationException("El PDF no corresponde al original de una recepción de liquidación de tarjetas activa.");
        var anterior=await c.QuerySingleOrDefaultAsync<TarjetasRecepcionTrabajo>(Cmd("SELECT Id,Usuario,Hash FROM dbo.TJ_Recepcion WITH(UPDLOCK,HOLDLOCK) WHERE DocumentoId=@documento",new{documento},t,tx));
        if(anterior is not null)
        {
            if(anterior.Hash!=hash)throw new InvalidOperationException("Esta recepción ya se preparó con otro PDF. Creá una recepción nueva para el archivo correcto.");
            if(!anterior.Usuario.Equals(usuario,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Otro usuario ya preparó esta recepción. Debe revisarla con su usuario.");
            validar();tx.Commit();return true;
        }
        await c.ExecuteAsync(Cmd("INSERT dbo.TJ_Recepcion(DocumentoId,Usuario,Sistema,Nombre,Original,Hash) VALUES(@documento,@usuario,@sistema,@nombre,@original,@hash)",new{documento,usuario,sistema,nombre=Path.GetFileName(archivo.Nombre),original=archivo.Contenido,hash},t,tx));
        await events.WriteAuditAsync(new(){Module="TarjetasRecepcion",EntityType="RecepcionDocumental",RecordId=documento.ToString(),Operation="Queue",Message="Liquidación encolada para lectura y propuestas; sin confirmar conciliaciones."},c,tx,t);
        validar();tx.Commit();return true;
    },ct);
    internal static void ValidarArchivo(ConciliacionArchivo archivo)
    {
        if(!Path.GetExtension(archivo.Nombre).Equals(".pdf",StringComparison.OrdinalIgnoreCase)||archivo.Contenido.Length is <5 or >20971520
            ||!archivo.Contenido.AsSpan(0,5).SequenceEqual("%PDF-"u8))throw new InvalidOperationException("Adjuntá un único PDF de liquidación por recepción, de hasta 20 MB.");
    }
    public Task<IReadOnlyList<TarjetasRecepcionTrabajo>> ConsultarAsync(IReadOnlyList<long> documentos,CancellationToken ct=default)
        =>Ejecutar<IReadOnlyList<TarjetasRecepcionTrabajo>>("Consultar",async(c,t,validar)=>
        {
            if(documentos.Count==0||await Disponible(c,t)==0)return [];
            var ids=documentos.Where(i=>i>0).Distinct().Take(500).ToArray();
            return (await c.QueryAsync<TarjetasRecepcionTrabajo>(Cmd("SELECT Id,DocumentoId,Usuario,Estado,Mensaje,Cuenta,Sucursal,ImportacionId,AnalisisId,ActualizadoUtc,Candidatas,Intentos FROM dbo.TJ_Recepcion WHERE DocumentoId IN @ids AND Usuario=@usuario",new{ids,usuario=users.GetCurrentUserName().Trim()},t))).AsList();
        },ct);
    public Task ContinuarAsync(long documento,string cuenta,string sucursal,bool reintentar,CancellationToken ct=default,Guid? sesionEsperada=null)=>Ejecutar("Continuar",async(c,t,validar)=>
    {
        if(sesionEsperada.HasValue&&sessions.GetActiveSession()?.Id!=sesionEsperada)throw new OperationCanceledException();
        if(sucursal.Length>10)throw new InvalidOperationException("La sucursal admite hasta diez caracteres.");
        using var tx=c.BeginTransaction();
        var actual=await c.QuerySingleOrDefaultAsync<TarjetasRecepcionTrabajo>(Cmd("SELECT Id,Estado,Cuenta,ImportacionId FROM dbo.TJ_Recepcion WITH(UPDLOCK,HOLDLOCK) WHERE DocumentoId=@documento AND Usuario=@usuario",new{documento,usuario=users.GetCurrentUserName().Trim()},t,tx))??throw new InvalidOperationException("No hay una recepción disponible para tu usuario.");
        if(actual.Estado is not ("CUENTA" or "ERROR"))throw new InvalidOperationException("La recepción ya está procesándose o terminó. Actualizá su estado.");
        cuenta=actual.ImportacionId.HasValue?actual.Cuenta:cuenta.Trim();
        if(cuenta.Length>0&&await c.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO))=@cuenta AND LTRIM(RTRIM(MEDIODEPAGO))='TJ' AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 AND LTRIM(RTRIM(MONEDA))='1'",new{cuenta},t,tx))!=1)
            throw new InvalidOperationException("Elegí una cuenta de tarjetas activa, imputable y en pesos.");
        if(actual.Estado=="CUENTA"&&cuenta.Length==0)throw new InvalidOperationException("Elegí la cuenta para continuar.");
        if(actual.Estado=="ERROR"&&!reintentar)throw new InvalidOperationException("Usá Reintentar para volver a procesar la recepción.");
        var n=await c.ExecuteAsync(Cmd("UPDATE dbo.TJ_Recepcion SET Cuenta=@cuenta,Sucursal=@sucursal,Estado='PENDIENTE',Intentos=0,Lease=NULL,HastaUtc=NULL,ProximoUtc=SYSUTCDATETIME(),ActualizadoUtc=SYSUTCDATETIME(),Mensaje=N'En cola para continuar.' WHERE Id=@Id AND Estado=@Estado AND Usuario=@usuario",new{actual.Id,actual.Estado,cuenta,sucursal=sucursal.Trim(),usuario=users.GetCurrentUserName().Trim()},t,tx));
        if(n!=1)throw new InvalidOperationException("El estado cambió. Actualizá la recepción.");
        await events.WriteAuditAsync(new(){Module="TarjetasRecepcion",EntityType="RecepcionDocumental",RecordId=documento.ToString(),Operation=reintentar?"Retry":"SelectAccount",Message="Trabajo encolado para continuar; cuenta "+cuenta+", sucursal "+sucursal.Trim()+"."},c,tx,t);
        validar();tx.Commit();return true;
    },ct);
}

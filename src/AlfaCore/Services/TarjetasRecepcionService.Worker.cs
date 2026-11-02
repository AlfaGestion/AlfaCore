using System.Security.Cryptography;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class TarjetasRecepcionService
{
    internal const string ReclamarSql="""
            ;WITH siguiente AS(
                SELECT TOP(1) * FROM dbo.TJ_Recepcion WITH(UPDLOCK,READPAST,ROWLOCK)
                WHERE Intentos<3 AND ((Estado='PENDIENTE' AND ProximoUtc<=SYSUTCDATETIME())
                    OR (Estado IN('LEYENDO','ANALIZANDO') AND HastaUtc<SYSUTCDATETIME())) ORDER BY Id
            ) UPDATE siguiente SET Estado='LEYENDO',Lease=@lease,HastaUtc=DATEADD(minute,@duracion,SYSUTCDATETIME()),
                Intentos=Intentos+1,ActualizadoUtc=SYSUTCDATETIME(),Mensaje=N'Leyendo liquidación…' OUTPUT inserted.*;
            """;
    internal async Task<int> ProcesarSiguienteAsync(CancellationToken ct)
    {
        // Solo lo invoca el hosted service dentro de una base expresamente habilitada en la central.
        var conexion=new SqlConnectionStringBuilder(sessions.GetConnectionString()){ConnectTimeout=5};
        await using var c=new SqlConnection(conexion.ConnectionString);await c.OpenAsync(ct);
        if(await Disponible(c,ct)==0)return 0;
        var habilitado=await c.ExecuteScalarAsync<string?>(Cmd("SELECT VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE='TARJETAS-DOCUMENTAL-HABILITADO'",null,ct));
        if(!string.Equals(habilitado?.Trim(),"SI",StringComparison.OrdinalIgnoreCase))return 0;
        var minutosTexto=await c.ExecuteScalarAsync<string?>(Cmd("SELECT VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE='TARJETAS-DOCUMENTAL-MINUTOS'",null,ct));
        var minutos=int.TryParse(minutosTexto,out var n)&&n is >=1 and <=120?n:20;
        await c.ExecuteAsync(Cmd("UPDATE dbo.TJ_Recepcion SET Estado='ERROR',Lease=NULL,Mensaje=N'El proceso se interrumpió tres veces. Revisá la configuración y usá Reintentar.',ActualizadoUtc=SYSUTCDATETIME() WHERE Estado IN('LEYENDO','ANALIZANDO') AND HastaUtc<SYSUTCDATETIME() AND Intentos>=3",null,ct));
        var lease=Guid.NewGuid();
        var trabajo=await c.QuerySingleOrDefaultAsync<TarjetasRecepcionTrabajo>(Cmd(ReclamarSql,new{lease,duracion=minutos+5},ct));
        if(trabajo is null)return 0;
        using var plazo=CancellationTokenSource.CreateLinkedTokenSource(ct);plazo.CancelAfter(TimeSpan.FromMinutes(minutos));
        try
        {
            var servicio=await CrearServicioWorker(c,trabajo,plazo.Token);
            await Vigente(c,trabajo,plazo.Token);
            long importacion;
            if(trabajo.ImportacionId.HasValue)importacion=trabajo.ImportacionId.Value;
            else
            {
                var archivo=new ConciliacionArchivo(trabajo.Nombre,trabajo.Original);ValidarArchivo(archivo);
                if(Convert.ToHexString(SHA256.HashData(archivo.Contenido))!=trabajo.Hash)throw new InvalidOperationException("El original guardado no coincide con su huella.");
                var lectura=await servicio.LeerAsync(archivo,plazo.Token);
                await Vigente(c,trabajo,plazo.Token);
                if(trabajo.Cuenta.Length==0)trabajo.Cuenta=lectura.CuentaSugerida;
                if(trabajo.Sucursal.Length==0)trabajo.Sucursal=lectura.SucursalSugerida;
                if(trabajo.Cuenta.Length==0)
                {
                    await Actualizar(c,trabajo,"CUENTA","Elegí la cuenta contable para continuar el análisis.",plazo.Token,
                        JsonSerializer.Serialize(lectura.Deteccion?.Candidatas??[]));
                    return 1;
                }
                // Si se cortó después de importar, o el operador ya había importado el mismo original,
                // se reutiliza SOLO su hash exacto y la cuenta elegida. No se fusionan PDFs por importe.
                importacion=await c.ExecuteScalarAsync<long?>(Cmd("SELECT TOP(1) Id FROM dbo.TJ_Importacion WHERE Hash=@Hash AND Cuenta=@Cuenta AND Anulada=0 ORDER BY Id DESC",trabajo,plazo.Token))??0;
                if(importacion==0)
                {
                    try{importacion=await servicio.ImportarAsync(archivo,trabajo.Cuenta,trabajo.Sucursal,false,plazo.Token);}
                    catch(AppUserFacingException)
                    {
                        importacion=await c.ExecuteScalarAsync<long?>(Cmd("SELECT TOP(1) Id FROM dbo.TJ_Importacion WHERE Hash=@Hash AND Cuenta=@Cuenta AND Anulada=0 ORDER BY Id DESC",trabajo,plazo.Token))??0;
                        if(importacion==0)throw;
                    }
                }
                trabajo.ImportacionId=importacion;
            }
            await Actualizar(c,trabajo,"ANALIZANDO","Preparando movimientos y criterios de análisis…",plazo.Token);
            var origen=await c.QuerySingleAsync<TarjetasImportacion>(Cmd("SELECT Id,Desde,Hasta,Cuenta,Anulada FROM dbo.TJ_Importacion WHERE Id=@id",new{id=importacion},plazo.Token));
            if(origen.Anulada||origen.Cuenta.Trim()!=trabajo.Cuenta.Trim())throw new InvalidOperationException("La liquidación importada se anuló o cambió de cuenta. Revisala desde Conciliación.");
            var criterios=await servicio.CriteriosIaAsync(plazo.Token);
            var filtro=new TarjetasFiltro(importacion,origen.Desde.AddDays(-criterios.Dias),origen.Hasta.AddDays(criterios.Dias),trabajo.Sucursal);
            var resultado=await servicio.AnalizarIaAsync(filtro,plazo.Token,criterios:criterios,avance:async a=>
            {
                await Vigente(c,trabajo,plazo.Token);
                await Actualizar(c,trabajo,"ANALIZANDO",a.Titulo+" · "+a.Actividad+(a.TotalLotes>0?$" Lote {a.LoteActual} de {a.TotalLotes}.":""),plazo.Token);
            });
            trabajo.AnalisisId=resultado.AnalisisId;
            await Actualizar(c,trabajo,"LISTO",$"Análisis guardado: {resultado.Propuestas.Count} propuestas para revisar. No se confirmó ninguna conciliación.",plazo.Token);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested)
        {
            using var cierre=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await c.ExecuteAsync(Cmd("UPDATE dbo.TJ_Recepcion SET Estado='PENDIENTE',Intentos=Intentos-1,Lease=NULL,HastaUtc=NULL,ProximoUtc=SYSUTCDATETIME(),ActualizadoUtc=SYSUTCDATETIME(),Mensaje=N'El servidor se detuvo. Se continuará al reiniciar.' WHERE Id=@Id AND Lease=@Lease AND Estado IN('LEYENDO','ANALIZANDO')",trabajo,cierre.Token));
            throw;
        }
        catch(Exception ex)
        {
            // Incluso en cancelación por parada se libera el trabajo con un token independiente.
            using var cierre=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var codigo=ex is AppUserFacingException conocido?conocido.ErrorCode:await events.LogErrorAsync("TarjetasRecepcion","Worker",ex,"No se completó el análisis documental de tarjetas.",ct:cierre.Token);
            var reintentar=trabajo.Intentos<3;
            await c.ExecuteAsync(Cmd("""
                UPDATE dbo.TJ_Recepcion SET Estado=@estado,Mensaje=@mensaje,Lease=NULL,HastaUtc=NULL,
                    ProximoUtc=DATEADD(minute,5,SYSUTCDATETIME()),ActualizadoUtc=SYSUTCDATETIME()
                WHERE Id=@Id AND Lease=@Lease AND Estado IN('LEYENDO','ANALIZANDO')
                """,new{trabajo.Id,trabajo.Lease,estado=reintentar?"PENDIENTE":"ERROR",
                    mensaje=(reintentar?"No se completó. Se reintentará en cinco minutos.":"No se pudo completar. Revisá la configuración y usá Reintentar.")+" Código: "+codigo},cierre.Token));
            if(ct.IsCancellationRequested)throw new OperationCanceledException(ct);
        }
        return 1;
    }
    private static async Task Vigente(SqlConnection c,TarjetasRecepcionTrabajo trabajo,CancellationToken ct)
    {
        var n=await c.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM dbo.TJ_Recepcion q
            JOIN dbo.INT_COMPROBANTE_RECIBIDO d ON d.IdComprobanteRecibido=q.DocumentoId
            JOIN dbo.INT_TIPO_DOCUMENTO t ON t.IdTipoDocumento=d.IdTipoDocumento
            WHERE q.Id=@Id AND q.Lease=@Lease AND q.HastaUtc>SYSUTCDATETIME()
              AND q.Estado IN('LEYENDO','ANALIZANDO') AND ISNULL(d.Eliminado,0)=0 AND t.Codigo=@tipo
              AND EXISTS(SELECT 1 FROM dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO a WHERE a.IdComprobanteRecibido=d.IdComprobanteRecibido AND ISNULL(a.Eliminado,0)=0 AND a.ContentHash=q.Hash)
              AND (SELECT COUNT(*) FROM dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO a WHERE a.IdComprobanteRecibido=d.IdComprobanteRecibido AND ISNULL(a.Eliminado,0)=0)=1
            """,new{trabajo.Id,trabajo.Lease,tipo=Tipo},ct));
        if(n!=1)throw new InvalidOperationException("La recepción cambió o el trabajo ya no está vigente.");
    }
    private static async Task Actualizar(SqlConnection c,TarjetasRecepcionTrabajo trabajo,string estado,string mensaje,CancellationToken ct,string? candidatas=null)
    {
        if(mensaje.Length>1000)mensaje=mensaje[..1000];
        var n=await c.ExecuteAsync(Cmd("""
            UPDATE dbo.TJ_Recepcion SET Estado=@estado,Mensaje=@mensaje,Cuenta=@Cuenta,Sucursal=@Sucursal,
                ImportacionId=@ImportacionId,AnalisisId=@AnalisisId,Candidatas=COALESCE(@candidatas,Candidatas),ActualizadoUtc=SYSUTCDATETIME(),
                Lease=CASE WHEN @estado IN('CUENTA','LISTO') THEN NULL ELSE Lease END
            WHERE Id=@Id AND Lease=@Lease AND Estado IN('LEYENDO','ANALIZANDO') AND HastaUtc>SYSUTCDATETIME()
            """,new{trabajo.Id,trabajo.Lease,trabajo.Cuenta,trabajo.Sucursal,trabajo.ImportacionId,trabajo.AnalisisId,estado,mensaje,candidatas},ct));
        if(n!=1)throw new InvalidOperationException("El trabajo ya no está vigente.");
    }
    private async Task<ConciliacionTarjetasService> CrearServicioWorker(SqlConnection c,TarjetasRecepcionTrabajo trabajo,CancellationToken ct)
    {
        var activo=await c.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN COL_LENGTH(N'dbo.TA_USUARIOS',N'Activo') IS NULL THEN 0 ELSE 1 END",null,ct));
        var usuario=await c.QuerySingleOrDefaultAsync<AppUserSessionInfo>(Cmd("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM dbo.TA_USUARIOS WHERE LTRIM(RTRIM(NOMBRE))=@Usuario AND LTRIM(RTRIM(SISTEMA))=@Sistema AND ISNULL(EsGrupo,0)=0"+(activo==1?" AND ISNULL(Activo,1)=1":""),trabajo,ct))
            ??throw new InvalidOperationException("El usuario que encoló la recepción no está disponible en esta base.");
        var identidad=new TarjetasUsuarioWorker(usuario,sessions.GetActiveSession()!.Id);
        var permisos=new PermissionService(config,sessions,identidad,events);
        if(!await permisos.HasAccessAsync(ConciliacionTarjetasService.MenuKey,ct))throw new InvalidOperationException("El usuario ya no tiene permiso para analizar tarjetas.");
        return new(sessions,identidad,permisos,events,lector,modelo,lectores);
    }
}

using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    private sealed record CompraOrigenBloqueado(long Documento,int Lectura,int EstadoAnterior,int EstadoRegistrado);
    private sealed class OrigenDocumentoCompra
    {
        public int IdEstado{get;set;}public string Codigo{get;set;}="";public string Tipo{get;set;}="";public bool Eliminado{get;set;}
    }
    private sealed class OrigenLecturaCompra
    {
        public int Id{get;set;}public string Estado{get;set;}="";public string Json{get;set;}="";
    }
    private static async Task<CompraOrigenBloqueado> BloquearOrigenAsync(SqlConnection cn,SqlTransaction tx,CompraCargaBorrador d,CancellationToken ct)
    {
        var doc=await cn.QuerySingleOrDefaultAsync<OrigenDocumentoCompra>(Cmd("""
            SELECT c.IdEstado,e.Codigo,t.Codigo Tipo,c.Eliminado FROM dbo.INT_COMPROBANTE_RECIBIDO c WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.INT_ESTADO e ON e.IdEstado=c.IdEstado JOIN dbo.INT_TIPO_DOCUMENTO t ON t.IdTipoDocumento=c.IdTipoDocumento
            WHERE c.IdComprobanteRecibido=@id;
            """,new{id=d.DocumentoRecibido},ct,tx));
        if(doc is null||doc.Eliminado||doc.Tipo!="COMPROBANTE_COMPRA"||doc.Codigo is "ANULADO" or "RECHAZADO" or "REGISTRADO")
            throw new InvalidOperationException("El documento recibido ya no está disponible para registrar esta compra.");
        var lectura=await cn.QueryFirstOrDefaultAsync<OrigenLecturaCompra>(Cmd("""
            SELECT TOP(1) ID,Estado,JsonResultado Json FROM dbo.IA_Compras_CAB WITH(UPDLOCK,HOLDLOCK)
            WHERE IdComprobanteRecibido=@id ORDER BY FechaHora_Proceso DESC,ID DESC;
            """,new{id=d.DocumentoRecibido},ct,tx));
        if(lectura is null||lectura.Id!=d.LecturaCompraId||lectura.Estado is not ("PROCESADO" or "SIN_PROVEEDOR")||
            string.IsNullOrWhiteSpace(lectura.Json)||CompraCargaOrigenLectura.Huella(lectura.Json)!=d.LecturaCompraHuella)
            throw new InvalidOperationException("La lectura del documento cambió o ya fue utilizada. Volvé a abrirlo desde Recepción documental.");
        var estado=await cn.QuerySingleOrDefaultAsync<int?>(Cmd("SELECT IdEstado FROM dbo.INT_ESTADO WHERE Codigo='REGISTRADO' AND Activo=1 AND PermiteEdicion=0 AND EsFinal=1;",null,ct,tx));
        if(estado is null)throw new InvalidOperationException("Aplicá la actualización de Recepción documental para registrar compras.");
        return new(d.DocumentoRecibido!.Value,lectura.Id,doc.IdEstado,estado.Value);
    }
    private async Task MarcarOrigenRegistradoAsync(SqlConnection cn,SqlTransaction tx,CompraOrigenBloqueado origen,int compra,string comprobante,string tipo,CancellationToken ct)
    {
        var confirmado=await cn.ExecuteScalarAsync<int>(Cmd("""
            UPDATE dbo.IA_Compras_CAB SET Estado='APROBADO',FechaHora_Modificacion=GETDATE(),Usuario_Proceso=@usuario
            WHERE ID=@Lectura AND IdComprobanteRecibido=@Documento AND Estado IN ('PROCESADO','SIN_PROVEEDOR');
            IF @@ROWCOUNT<>1 BEGIN RAISERROR(N'La lectura cambió. No se confirmó la compra.',16,1); RETURN; END;
            UPDATE dbo.INT_COMPROBANTE_RECIBIDO SET IdEstado=@EstadoRegistrado,FechaHoraEstado=GETDATE(),FechaHora_Modificacion=GETDATE(),
                UsuarioModificacion=@usuario,PcModificacion=HOST_NAME() WHERE IdComprobanteRecibido=@Documento AND IdEstado=@EstadoAnterior AND Eliminado=0;
            IF @@ROWCOUNT<>1 BEGIN RAISERROR(N'El documento cambió. No se confirmó la compra.',16,1); RETURN; END;
            INSERT dbo.INT_COMPROBANTE_RECIBIDO_HIST(IdComprobanteRecibido,Usuario,Pc,Accion,IdEstadoAnterior,IdEstadoNuevo,Observacion,DataJson)
            VALUES(@Documento,@usuario,HOST_NAME(),'CAMBIO_ESTADO',@EstadoAnterior,@EstadoRegistrado,@nota,@json);
            SELECT 1;
            """,new{origen.Documento,origen.Lectura,origen.EstadoRegistrado,origen.EstadoAnterior,usuario=usuarios.GetCurrentUserName(),
                nota=$"Compra {tipo} {comprobante} registrada desde AlfaCore.",json=System.Text.Json.JsonSerializer.Serialize(new{Proceso="COMPRA_REGISTRADA",CompraId=compra,Comprobante=comprobante})},ct,tx));
        if(confirmado!=1)throw new InvalidOperationException("No se pudo completar el vínculo documental. No se confirmó la compra.");
    }
}

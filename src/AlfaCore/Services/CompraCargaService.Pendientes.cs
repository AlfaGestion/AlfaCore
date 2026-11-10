using AlfaCore.Models;
using Dapper;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    private const string PermisoRecepcion="D016004";

    /// <summary>Como SQL_PendientesIACompras del legacy: documentos de compra sin confirmar, los más nuevos primero.</summary>
    public Task<IReadOnlyList<CompraPendienteRecepcion>> PendientesRecepcionAsync(int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar<IReadOnlyList<CompraPendienteRecepcion>>(baseEsperada,"PendientesRecepcion",async(c,t)=>
        {
            if(!await permisos.HasAccessAsync(PermisoRecepcion,t))throw new InvalidOperationException("No tenés permiso para consultar Recepción documental.");
            return (await c.QueryAsync<CompraPendienteRecepcion>(Cmd("""
                SELECT TOP(200) d.IdComprobanteRecibido Id,d.FechaHora_Grabacion Fecha,ISNULL(d.UsuarioAlta,'') Usuario,
                  ISNULL(ia.Proveedor_Nombre,'') Proveedor,ISNULL(ia.TipoComprobante,'') Tipo,
                  LTRIM(RTRIM(ISNULL(ia.Letra,'')+' '+ISNULL(ia.PuntoVenta,'')+'-'+ISNULL(ia.Numero,''))) Comprobante,
                  ISNULL((SELECT TOP(1) a.NombreOriginal FROM dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO a WHERE a.IdComprobanteRecibido=d.IdComprobanteRecibido AND a.Eliminado=0 ORDER BY a.Orden),'') Archivo,
                  CASE WHEN ia.Estado IN ('PROCESADO','SIN_PROVEEDOR') THEN 'Leído' WHEN ia.Estado IN ('PENDIENTE_LECTURA','PROCESANDO_LECTURA') THEN 'Leyendo'
                       WHEN ia.Estado='ERROR_LECTURA' THEN 'Error de lectura' WHEN ia.Estado IS NULL THEN 'Sin leer' ELSE ISNULL(ia.Estado,'') END Estado,
                  ia.Total,CONVERT(bit,CASE WHEN ia.Estado IN ('PROCESADO','SIN_PROVEEDOR') THEN 1 ELSE 0 END) TieneLectura
                FROM dbo.INT_COMPROBANTE_RECIBIDO d
                JOIN dbo.INT_ESTADO e ON e.IdEstado=d.IdEstado
                JOIN dbo.INT_TIPO_DOCUMENTO tp ON tp.IdTipoDocumento=d.IdTipoDocumento AND tp.Codigo='COMPROBANTE_COMPRA'
                OUTER APPLY(SELECT TOP(1) cab.Estado,cab.Proveedor_Nombre,cab.TipoComprobante,cab.Letra,cab.PuntoVenta,cab.Numero,cab.Total
                            FROM dbo.IA_Compras_CAB cab WHERE cab.IdComprobanteRecibido=d.IdComprobanteRecibido
                            ORDER BY cab.FechaHora_Proceso DESC,cab.ID DESC) ia
                WHERE ISNULL(d.Eliminado,0)=0 AND e.Codigo NOT IN ('REGISTRADO','ANULADO','RECHAZADO')
                  AND ISNULL(ia.Estado,'') NOT IN ('APROBADO','ELIMINADO','RECHAZADO','CANCELADO')
                ORDER BY d.FechaHora_Grabacion DESC,d.IdComprobanteRecibido DESC;
                """,null,t))).AsList();
        },ct);

    /// <summary>
    /// Sube archivos a Recepción documental y los lee con IA en el momento (sin esperar la cola), así la carga se completa sola.
    /// Si un archivo ya fue subido antes, reutiliza ese documento en lugar de duplicarlo.
    /// </summary>
    public Task<IReadOnlyList<CompraLecturaArchivo>> LeerArchivosAsync(IReadOnlyList<InterfacesCrearAdjuntoRequest> archivos,Func<string,Task>? progreso,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar<IReadOnlyList<CompraLecturaArchivo>>(baseEsperada,"LeerArchivos",async(c,t)=>
        {
            if(archivos.Count is 0 or >20)throw new InvalidOperationException("Elegí entre 1 y 20 archivos.");
            if(!await permisos.HasAccessAsync(PermisoRecepcion,t))throw new InvalidOperationException("No tenés permiso para cargar documentos en Recepción documental.");
            var tipo=(await documentos.GetDocumentTypesAsync(baseEsperada,t)).FirstOrDefault(x=>x.Codigo=="COMPROBANTE_COMPRA")
                ??throw new InvalidOperationException("Recepción documental no tiene el tipo «Comprobante de compra».");
            var usuario=usuarios.GetCurrentUserName();var pc=Environment.MachineName;var resultado=new List<CompraLecturaArchivo>();
            var repetidos=(await documentos.FindFilesByHashAsync(archivos.Select(a=>a.ContentHash).Where(h=>h.Length>0).ToList(),baseEsperada,t))
                .GroupBy(x=>x.ContentHash,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First().IdComprobanteRecibido,StringComparer.OrdinalIgnoreCase);
            var nuevos=archivos.Where(a=>a.ContentHash.Length==0||!repetidos.ContainsKey(a.ContentHash)).ToList();
            var documentosLeer=new List<(long Id,string Archivos,bool Repetido)>();
            foreach(var a in archivos.Where(a=>a.ContentHash.Length>0&&repetidos.ContainsKey(a.ContentHash)))
                if(documentosLeer.All(d=>d.Id!=repetidos[a.ContentHash]))documentosLeer.Add((repetidos[a.ContentHash],a.NombreArchivo,true));
            if(nuevos.Count>0)
            {
                if(progreso is not null)await progreso("Subiendo a Recepción documental…");
                var lote=await documentos.CreateLoteAsync(new(){IdTipoDocumento=tipo.IdTipoDocumento,UsuarioAccion=usuario,PcAccion=pc,Archivos=nuevos},baseEsperada,t);
                foreach(var g in lote.Comprobantes)documentosLeer.Add((g.IdComprobanteRecibido,g.ArchivosOrigen,false));
            }
            var n=0;
            foreach(var (id,nombre,repetido) in documentosLeer)
            {
                t.ThrowIfCancellationRequested();n++;
                if(progreso is not null)await progreso($"Leyendo con IA {n} de {documentosLeer.Count}: {nombre}…");
                try
                {
                    var lectura=await documentos.DetectCompraAsync(new(){IdComprobanteRecibido=id,UsuarioAccion=usuario,PcAccion=pc},t);
                    var leido=lectura.Estado is "PROCESADO" or "SIN_PROVEEDOR";
                    resultado.Add(new(id,nombre,leido,leido?(repetido?"Ya estaba cargado en Recepción documental; se reutilizó.":""):
                        (lectura.LectorError.Length>0?lectura.LectorError:"La IA no pudo leer el documento.")));
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){resultado.Add(new(id,nombre,false,ex is InvalidOperationException||ex is AppUserFacingException?ex.Message:"No se pudo leer el documento."));}
            }
            return resultado;
        },ct);
}

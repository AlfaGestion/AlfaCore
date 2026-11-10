using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace AlfaCore.Components.Pages;

// «Desde Recepción»: elegir un documento ya leído por la IA o subir archivos nuevos y completar la carga solos (como Personalizar del legacy).
public partial class ComprasCarga
{
    private const int MaximoArchivosLectura=20;
    private bool _mostrarRecepcion,_leyendoArchivos;
    private string _progresoLectura="";
    private int _versionSelectorArchivos;
    private IReadOnlyList<CompraPendienteRecepcion> _pendientes=[];
    private IReadOnlyList<CompraLecturaArchivo> _resultadosLectura=[];
    private bool PuedeRecepcion=>!EdicionBloqueada&&_contexto is not null&&_borrador.CompraOrigenId is null;

    private Task AbrirRecepcion()=>Operar(async ct=>
    {
        _resultadosLectura=[];_progresoLectura="";
        _pendientes=await Servicio.PendientesRecepcionAsync(_baseCargada,ct);
        _importadasPendientes=await Servicio.ImportadasPendientesAsync(_baseCargada,ct);_mostrarRecepcion=true;
    });
    private void CerrarRecepcion(){if(!_leyendoArchivos)_mostrarRecepcion=false;}

    private Task UsarPendiente(CompraPendienteRecepcion p)=>Operar(async ct=>
    {
        if(!p.TieneLectura){_feedback=AppUiMessage.Warning("Documento sin lectura","Ese documento todavía no fue leído por la IA. Esperá a que termine o procesalo desde Recepción documental.");return;}
        await AplicarDocumentoAsync(p.Id,ct);_mostrarRecepcion=false;
    });

    private async Task ArchivosElegidos(InputFileChangeEventArgs e)
    {
        if(_busy||_leyendoArchivos||_disposed)return;
        var archivos=e.GetMultipleFiles(MaximoArchivosLectura);
        if(archivos.Count==0)return;
        var subidos=new List<InterfacesCrearAdjuntoRequest>();
        await Operar(async ct=>
        {
            _leyendoArchivos=true;_resultadosLectura=[];_progresoLectura="Preparando archivos…";
            try
            {
                var maximo=(await Configuracion.GetUploadSettingsAsync(_baseCargada,ct)).TamanoMaximoBytes;
                foreach(var f in archivos)
                {
                    await using var origen=f.OpenReadStream(maximo,ct);var ms=new MemoryStream();await origen.CopyToAsync(ms,ct);var bytes=ms.ToArray();ms.Position=0;
                    subidos.Add(new(){NombreArchivo=f.Name,MimeType=f.ContentType,TamanoBytes=f.Size,Contenido=ms,
                        ContentHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()});
                }
                _resultadosLectura=await Servicio.LeerArchivosAsync(subidos,async m=>{_progresoLectura=m;await InvokeAsync(StateHasChanged);},_baseCargada,ct);
                _pendientes=await Servicio.PendientesRecepcionAsync(_baseCargada,ct);
                var leidos=_resultadosLectura.Where(r=>r.Leido).ToList();
                // Un solo documento leído: se completa la carga sin pasos intermedios. Con varios, queda elegir cuál cargar primero.
                if(leidos.Count==1&&_resultadosLectura.Count==1){await AplicarDocumentoAsync(leidos[0].Documento,ct);_mostrarRecepcion=false;}
            }
            // El selector se recrea recién acá: si se reiniciara antes, el navegador ya no tendría los archivos para leerlos.
            finally{foreach(var s in subidos)await s.Contenido.DisposeAsync();_leyendoArchivos=false;_progresoLectura="";_versionSelectorArchivos++;}
        });
    }

    /// <summary>Reemplaza lo que hay en pantalla por el documento leído: proveedor, cabecera, importes y lectura original para cotejar.</summary>
    private async Task AplicarDocumentoAsync(long id,CancellationToken ct)
    {
        var ctx=_contexto??throw new InvalidOperationException("La pantalla todavía no está lista.");
        _origen=null;_vista=null;_proveedor=null;_filas.Clear();_registrada=null;_confirmarRegistro=false;_operacion=Guid.NewGuid();_borradorId=Guid.NewGuid();_borradorVersion=0;
        _impuestoInterno="0";_notaAplicar="0";_facturaNota=null;_buscarFacturaNota="";_identidades.Clear();_percIva=_percIibb=_percGan="0";_totalDocumento="";_totalManual=false;_buscarProveedor="";
        _borrador=new(){Unidad=ctx.Unidad,Motivo=ctx.Motivo};
        await CargarOrigenAsync(id,ct);
        if(_filas.Count==0)Agregar();
        PrepararTipoYDeposito(true);
        _feedback=AppUiMessage.Info("Datos completados desde el documento","Revisá proveedor, comprobante, importes y artículos contra el original antes de registrar.");
    }
}

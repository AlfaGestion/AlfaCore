using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using AlfaCore.Services;

namespace AlfaCore.Components.Pages;

// Comprobantes importados de ARCA que esperan su comprobante físico: se avisan, se pueden completar y se compara en vivo contra lo informado por ARCA.
public partial class ComprasCarga
{
    private IReadOnlyList<CompraImportadaPendiente> _importadasPendientes=[];
    private CompraExistente? _importadaArca,_existenteAviso;

    private decimal? IvaCarga=>_vista?.Bases.Sum(b=>b.Iva);
    private decimal? NetoGravadoCarga=>_vista?.Bases.Where(b=>b.Tratamiento=="Gravado").Sum(b=>b.Neto);
    private bool CoincideConArca(decimal arca,decimal? carga,decimal tolerancia)=>carga is {} c&&Math.Abs(arca-c)<=tolerancia;

    /// <summary>Guarda el aviso del comprobante ya cargado que acompaña a un origen (importado de ARCA o registrado por otra vía).</summary>
    private void AplicarExistente(CompraExistente? existente)
    {
        _importadaArca=null;_existenteAviso=null;
        if(existente is not {Anulada:false})return;
        if(existente.ImportadaArca&&_borrador.ReemplazaImportadaId==existente.Id)_importadaArca=existente;else _existenteAviso=existente;
    }

    private Task UsarImportada(CompraImportadaPendiente p)=>Operar(async ct=>
    {
        var d=await Servicio.PrepararDesdeImportadaAsync(p.Id,_baseCargada,ct);
        var ctx=_contexto??throw new InvalidOperationException("La pantalla todavía no está lista.");
        _origen=null;_vista=null;_filas.Clear();_registrada=null;_confirmarRegistro=false;_operacion=Guid.NewGuid();_borradorId=Guid.NewGuid();_borradorVersion=0;_renglonesPendientes=[];_candidatoElegido.Clear();
        _impuestoInterno="0";_notaAplicar="0";_facturaNota=null;_buscarFacturaNota="";_identidades.Clear();_percIva=_percIibb=_percGan="0";_identidades.Clear();
        _borrador=d;_borrador.Deposito=ctx.Depositos.FirstOrDefault()?.Codigo??"";
        _proveedor=await Servicio.ProveedorAsync(d.Proveedor,_baseCargada,ct);_buscarProveedor=_proveedor.Nombre;
        _totalDocumento=d.TotalDocumento is {} total?NumeroTexto(total):"";_totalManual=_totalDocumento.Length>0;
        PrepararTipoYDeposito(true);
        if(_proveedor.Motivo.Length>0)_borrador.Motivo=_proveedor.Motivo;
        AplicarExistente(await Servicio.CompraExistenteAsync(d.Proveedor,d.Tipo,d.Letra,d.Sucursal,d.Numero,_baseCargada,ct));
        _mostrarRecepcion=false;
        _feedback=AppUiMessage.Info("Importación de ARCA lista para completar","Cargá los artículos del comprobante (o leelo con IA desde «Desde Recepción»). Los importes tienen que coincidir con ARCA para poder registrar.");
    });

    /// <summary>Cambia sucursal, número o letra y vuelve a evaluar el aviso de comprobante ya cargado, para que no quede el de la identificación anterior.</summary>
    private void CambiarIdentificacion(Action cambio)
    {
        if(EdicionBloqueada)return;
        _borrador.ReemplazaImportadaId=null;
        _importadaArca=null;_existenteAviso=null;_feedback=null;
        Cambiar(cambio);
        _ = Operar(async ct=>
        {
            if(_proveedor is null||_borrador.Tipo is not("FCC" or "NCC" or "NDC"))return;
            var existente=await Servicio.CompraExistenteAsync(_borrador.Proveedor,_borrador.Tipo,_borrador.Letra,_borrador.Sucursal,_borrador.Numero,_baseCargada,ct);
            if(existente is {Anulada:false,ImportadaArca:true} arca)_borrador.ReemplazaImportadaId=arca.Id;
            AplicarExistente(existente);
        });
    }

    /// <summary>Aviso previo al registro: si ese comprobante ya está cargado se informa; si es una importación de ARCA se ofrece completarla.</summary>
    private async Task<bool> VerificarComprobanteExistenteAsync(CancellationToken ct)
    {
        if(_borrador.ReemplazaImportadaId is not null||_proveedor is null||_borrador.Tipo is not("FCC" or "NCC" or "NDC"))return true;
        var existente=await Servicio.CompraExistenteAsync(_borrador.Proveedor,_borrador.Tipo,_borrador.Letra,_borrador.Sucursal,_borrador.Numero,_baseCargada,ct);
        if(existente is not {Anulada:false} ex)return true;
        if(ex.ImportadaArca)
        {
            _borrador.ReemplazaImportadaId=ex.Id;AplicarExistente(ex);
            _feedback=AppUiMessage.Warning("Este comprobante ya fue importado de ARCA",$"Cargado el {ex.Fecha:dd/MM/yyyy} por {Dinero(ex.Total)}. Al registrar se reemplaza por esta carga con su detalle: revisá que los importes coincidan con ARCA y volvé a registrar.");
            return false;
        }
        throw new InvalidOperationException($"Ese comprobante ya está registrado ({ex.Tipo} {ex.Comprobante}, {ex.Fecha:dd/MM/yyyy}, {Dinero(ex.Total)}, usuario {ex.Usuario}). No se puede cargar de nuevo.");
    }
}

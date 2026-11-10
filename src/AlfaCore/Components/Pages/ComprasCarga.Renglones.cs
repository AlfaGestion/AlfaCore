using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using AlfaCore.Services;

namespace AlfaCore.Components.Pages;

// Renglones leídos por la IA que no se pudieron emparejar solos: el operador elige un artículo, lo da de alta, lo pasa a concepto o lo descarta.
public partial class ComprasCarga
{
    private List<CompraRenglonResuelto> _renglonesPendientes=[];
    private CompraRenglonResuelto? _renglonBuscando,_renglonAlta;
    private CompraAltaPropuesta? _altaPropuesta;
    private ArticuloLookupDataDto? _altaOpciones;
    private bool _mostrarAlta,_confirmarAltaTodo;
    private readonly Dictionary<CompraRenglonResuelto,string> _candidatoElegido=[];
    private bool RenglonesBloqueados=>EdicionBloqueada||_proveedor is null;
    private string RenglonesPendientesTexto=>_renglonesPendientes.Count==1?"1 renglón sin artículo":$"{_renglonesPendientes.Count} renglones sin artículo";

    // Como Cambiar, pero también dentro de Operar (con la pantalla ocupada): los flujos de esta solapa cambian filas mientras esperan al servidor.
    private void Aplicar(Action cambio){if(_registrada is not null)return;cambio();SincronizarTotal();_operacion=Guid.NewGuid();Header();ProgramarVista();}

    private static IReadOnlyList<AlfaSelectOption> OpcionesAlta(List<ArticuloLookupOptionDto>? opciones)
        => new[]{new AlfaSelectOption("","Seleccionar")}.Concat((opciones??[]).Select(o=>new AlfaSelectOption(o.Codigo,$"{o.Codigo} · {o.Descripcion}"))).ToList();

    private FilaCompra FilaDeLinea(CompraCargaLinea l)=>new(){Descripcion=l.Descripcion,Cuenta=l.Cuenta,Tratamiento=l.Tratamiento,Articulo=l.Articulo,ArticuloHuella=l.ArticuloHuella,UnidadArticulo=l.UnidadArticulo,
        Cantidad=l.Cantidad,CostoUnitario=l.CostoUnitario,Neto=NumeroTexto(l.Neto),Tasa=NumeroTexto(l.Tasa),Iva=l.IvaInformado is {} iva?NumeroTexto(iva):"",
        CodigoProveedor=l.CodigoProveedor,UnidadesPorBulto=l.UnidadesPorBulto,Nota=l.Nota};

    /// <summary>Pasa un renglón ya resuelto (con artículo) a las líneas de la compra.</summary>
    private void IncorporarRenglon(CompraRenglonResuelto r)
    {
        if(r.Articulo is not {} a)return;
        Aplicar(()=>{
            _filas.RemoveAll(f=>f.Articulo.Length==0&&f.Descripcion.Length==0&&f.Neto.Length==0&&f.Cuenta.Length==0);
            _filas.Add(FilaDeLinea(CompraCargaService.LineaDeRenglon(r,a,_borrador.Letra)));
            _renglonesPendientes.Remove(r);_candidatoElegido.Remove(r);
        });
    }

    private Task ElegirCandidato(CompraRenglonResuelto r)=>Operar(async ct=>
    {
        if(!_candidatoElegido.TryGetValue(r,out var codigo)||codigo.Length==0){_feedback=AppUiMessage.Info("Elegí un artículo","Seleccioná uno de los candidatos o usá Buscar.");return;}
        await AsignarYIncorporarAsync(r,codigo,ct);
    });
    private async Task AsignarYIncorporarAsync(CompraRenglonResuelto r,string codigoArticulo,CancellationToken ct)
    {
        var asignado=await Servicio.AsignarArticuloAsync(r,codigoArticulo,_borrador.Proveedor,_baseCargada,ct);
        if(asignado.Cantidad<=0||asignado.Costo<=0){_feedback=AppUiMessage.Warning("Faltan datos del renglón","El renglón no informa cantidad o costo utilizables; cargalo a mano en la grilla de artículos.");asignado.Articulo=null;return;}
        IncorporarRenglon(asignado);
    }

    private Task BuscarParaRenglon(CompraRenglonResuelto r)=>Operar(async ct=>
    {
        _renglonBuscando=r;_busquedaArticulos?.Cancel();_buscandoArticulos=false;_textoArticulos=CompraArticuloTexto.PalabraClave(r.Descripcion);_cantidadModal=null;_mostrarArticulos=true;
        _articulosEncontrados=await Servicio.BuscarArticulosAsync(_textoArticulos,_borrador.Unidad,_borrador.Deposito,_baseCargada,ct,_proveedor?.Codigo??"");_articuloActivo=0;await FocoArticulo("-buscar-articulo");
    });

    private void RenglonAConcepto(CompraRenglonResuelto r)=>Cambiar(()=>
    {
        var neto=r.Total is >0?r.Total.Value:CompraCargaPlanificador.Moneda(r.CantidadLeida*r.CostoLeido);
        var tasa=r.IvaLeido is >0?r.IvaLeido.Value:_contexto?.Tasas.FirstOrDefault()??0;
        _filas.RemoveAll(f=>f.Articulo.Length==0&&f.Descripcion.Length==0&&f.Neto.Length==0&&f.Cuenta.Length==0);
        _filas.Add(new(){Descripcion=r.Descripcion.Length>200?r.Descripcion[..200]:r.Descripcion,Tratamiento="Gravado",Neto=NumeroTexto(neto),Tasa=NumeroTexto(_borrador.Letra=="C"?0:tasa),CodigoProveedor=r.CodigoProveedor});
        _renglonesPendientes.Remove(r);_candidatoElegido.Remove(r);
    });
    private void DescartarRenglon(CompraRenglonResuelto r)=>Cambiar(()=>{_renglonesPendientes.Remove(r);_candidatoElegido.Remove(r);});

    private Task AbrirAlta(CompraRenglonResuelto r)=>Operar(async ct=>
    {
        _altaOpciones??=await Servicio.AltaOpcionesAsync(_baseCargada,ct);
        _altaPropuesta=await Servicio.PropuestaAltaAsync(r,_borrador.Proveedor,_baseCargada,ct);_renglonAlta=r;_mostrarAlta=true;
    });
    private void CerrarAlta(){if(_busy)return;_mostrarAlta=false;_renglonAlta=null;_altaPropuesta=null;}
    private Task ConfirmarAlta()=>Operar(async ct=>
    {
        if(_altaPropuesta is not {} p||_renglonAlta is not {} r)return;
        var articulo=await Servicio.AltaArticuloAsync(p,_borrador.Proveedor,_baseCargada,ct);
        _mostrarAlta=false;_renglonAlta=null;_altaPropuesta=null;
        await AsignarYIncorporarAsync(r,articulo.Codigo,ct);
        _feedback=AppUiMessage.Success("Artículo dado de alta",$"Se creó el artículo {articulo.Codigo}. Completá el resto de sus datos desde el maestro de artículos.");
    });

    /// <summary>"Alta todo lo nuevo" del legacy: una sola confirmación y se dan de alta todos los renglones sin artículo con los datos propuestos.</summary>
    private Task AltaTodoNuevo()=>Operar(async ct=>
    {
        _altaOpciones??=await Servicio.AltaOpcionesAsync(_baseCargada,ct);
        var errores=new List<string>();var creados=0;
        foreach(var r in _renglonesPendientes.Where(x=>x.Descripcion.Length>0).ToList())
        {
            try
            {
                var propuesta=await Servicio.PropuestaAltaAsync(r,_borrador.Proveedor,_baseCargada,ct);
                var articulo=await Servicio.AltaArticuloAsync(propuesta,_borrador.Proveedor,_baseCargada,ct);
                await AsignarYIncorporarAsync(r,articulo.Codigo,ct);creados++;
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex){errores.Add($"{r.Descripcion}: {(ex is AppUserFacingException||ex is InvalidOperationException?ex.Message:"no se pudo dar de alta")}");}
        }
        _confirmarAltaTodo=false;
        _feedback=errores.Count==0?AppUiMessage.Success("Altas completas",$"Se dieron de alta {creados} artículos. Completá el resto de sus datos desde el maestro de artículos.")
            :AppUiMessage.Warning("Algunas altas fallaron",$"Se crearon {creados}. "+string.Join(" · ",errores.Take(3)));
    });
}

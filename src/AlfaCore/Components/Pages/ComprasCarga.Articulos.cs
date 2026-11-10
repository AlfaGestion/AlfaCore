using System.Globalization;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AlfaCore.Components.Pages;

public partial class ComprasCarga
{
    private readonly string _editorId="compra-editor-"+Guid.NewGuid().ToString("N");
    private IJSObjectReference? _teclado;
    private DotNetObjectReference<ComprasCarga>? _referenciaJs;
    private bool _tecladoIniciando,_mostrarArticulos,_mostrarProveedores,_buscandoArticulos;
    private string _nuevoCodigo="",_nuevaCantidad="1",_nuevoCosto="",_textoArticulos="",_textoProveedores="";
    private CompraArticulo? _nuevoArticulo;private int _articuloActivo;private decimal? _cantidadModal;
    private IReadOnlyList<CompraArticulo> _articulosEncontrados=[];
    private IReadOnlyList<CompraCargaProveedor> _proveedoresEncontrados=[];
    private CancellationTokenSource? _busquedaArticulos;
    private bool NuevaLineaBloqueada=>EdicionBloqueada||_proveedor is null||!PuedeVista||_borrador.CompraOrigenId is not null||_filas.Count>=100;
    private IEnumerable<FilaCompra> FilasArticulos=>_filas.Where(f=>f.Articulo.Length>0);
    private IEnumerable<FilaCompra> FilasConceptos=>_filas.Where(f=>f.Articulo.Length==0);
    private static decimal? NumeroGrilla(string? texto)
    {
        var valor=(texto??"").Trim().Replace(',','.');
        return decimal.TryParse(valor,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)?n:null;
    }
    private static string CantidadTexto(FilaCompra f)=>f.CantidadTexto??NumeroTexto(f.Cantidad);
    private static string CostoTexto(FilaCompra f)=>f.CostoTexto??NumeroTexto(f.CostoUnitario??(f.Cantidad>0&&NumeroGrilla(f.Neto) is {} neto?decimal.Round(neto/f.Cantidad,4):0));
    private decimal NetoFila(FilaCompra f)
    {
        if(f.Articulo.Length==0){var neto=NumeroGrilla(f.Neto)??0;return neto is >=0 and <=100000000000m?neto:0;}
        var cantidad=NumeroGrilla(CantidadTexto(f))??0;var costo=NumeroGrilla(CostoTexto(f))??0;
        return cantidad is >=0 and <=9999999&&costo is >=0 and <=999999999?cantidad*costo:0;
    }
    private decimal IvaFila(FilaCompra f)=>NumeroGrilla(f.Iva)??((_borrador.Letra=="A"||EsComercial&&_proveedor?.CondicionIva!="5")&&f.Tratamiento=="Gravado"?CompraCargaPlanificador.Moneda(CompraCargaPlanificador.Moneda(NetoFila(f))*(NumeroGrilla(f.Tasa)??0)/100):0);
    private decimal TotalEstimado=>_filas.Sum(f=>CompraCargaPlanificador.Moneda(NetoFila(f))+IvaFila(f))+(NumeroGrilla(_percIva)??0)+(NumeroGrilla(_percIibb)??0)+(NumeroGrilla(_percGan)??0)+(NumeroGrilla(_impuestoInterno)??0);
    private async Task IniciarTeclado()
    {
        if(_tecladoIniciando||_disposed)return;_tecladoIniciando=true;
        try{_teclado=await Js.InvokeAsync<IJSObjectReference>("import","./js/carga-compras.js");_referenciaJs=DotNetObjectReference.Create(this);await _teclado.InvokeVoidAsync("init",_editorId,_referenciaJs);}
        catch(JSDisconnectedException){}catch(InvalidOperationException){}
    }
    private async Task FocoArticulo(string campo){try{await Js.InvokeVoidAsync("cargaDirecta.focus",_editorId+campo);}catch(JSDisconnectedException){}catch(InvalidOperationException){}}
    [JSInvokable] public Task BuscarArticuloDesdeTeclado()=>InvokeAsync(AbrirArticulos);
    [JSInvokable] public Task BuscarProveedorDesdeTeclado()=>InvokeAsync(AbrirProveedores);
    private Task CodigoArticuloKeyDown(KeyboardEventArgs e)=>e.Key=="Enter"?ResolverCodigoArticulo():Task.CompletedTask;
    private Task CantidadArticuloKeyDown(KeyboardEventArgs e)=>e.Key=="Enter"?FocoArticulo("-costo"):Task.CompletedTask;
    private Task CostoArticuloKeyDown(KeyboardEventArgs e)=>e.Key=="Enter"?AgregarArticulo():Task.CompletedTask;
    private Task ResolverCodigoArticulo()=>Operar(async ct=>
    {
        if(_proveedor is null||_borrador.CompraOrigenId is not null)return;
        // Como en ventas: "cantidad*código" (también con lector de código de barras) lo agrega directo.
        var (cantidad,codigo)=SepararCantidad(_nuevoCodigo);
        if(codigo.Length==0)return;
        var exacto=await Servicio.ResolverCodigoArticuloAsync(codigo,_borrador.Unidad,_borrador.Deposito,_proveedor.Codigo,_baseCargada,ct);
        if(exacto is not null)
        {
            await ElegirArticulo(exacto);
            if(cantidad is {} c&&_nuevoArticulo is not null){_nuevaCantidad=NumeroTexto(c);await FocoArticulo("-costo");}
            return;
        }
        var lista=await Servicio.BuscarArticulosAsync(codigo,_borrador.Unidad,_borrador.Deposito,_baseCargada,ct,_proveedor.Codigo);
        if(lista.Count==0){_feedback=AppUiMessage.Warning("Artículo no encontrado",$"No hay artículos para \"{codigo}\" (código, código de barras ni código del proveedor). Probá con F5 para buscar por descripción.");await FocoArticulo("-codigo");return;}
        _articulosEncontrados=lista;_articuloActivo=0;_textoArticulos=codigo;_mostrarArticulos=true;
    });
    // "3*7791234567890" o "1,5*826": lo anterior al asterisco es la cantidad.
    private static (decimal? Cantidad,string Codigo) SepararCantidad(string? entrada)
    {
        var raw=(entrada??"").Trim();var i=raw.IndexOf('*');
        return i>0&&i<raw.Length-1&&NumeroGrilla(raw[..i]) is {} c&&c>0?(c,raw[(i+1)..].Trim()):(null,raw);
    }
    private Task AbrirArticulos()=>Operar(async ct=>
    {
        if(_proveedor is null){_feedback=AppUiMessage.Info("Elegí el proveedor","Buscá y seleccioná el proveedor antes de cargar artículos.");return;}
        if(_borrador.CompraOrigenId is not null)return;
        _busquedaArticulos?.Cancel();_buscandoArticulos=false;_textoArticulos="";_cantidadModal=null;_mostrarArticulos=true;
        _articulosEncontrados=await Servicio.BuscarArticulosAsync("",_borrador.Unidad,_borrador.Deposito,_baseCargada,ct,_proveedor.Codigo);_articuloActivo=0;await FocoArticulo("-buscar-articulo");
    });
    private async Task BuscarArticulosInput(ChangeEventArgs e)
    {
        _textoArticulos=e.Value?.ToString()??"";_cantidadModal=SepararCantidad(_textoArticulos).Cantidad;_busquedaArticulos?.Cancel();_busquedaArticulos?.Dispose();
        var cts=_busquedaArticulos=CancellationTokenSource.CreateLinkedTokenSource(_vida.Token);_buscandoArticulos=true;
        try{await Task.Delay(220,cts.Token);var lista=await Servicio.BuscarArticulosAsync(SepararCantidad(_textoArticulos).Codigo,_borrador.Unidad,_borrador.Deposito,_baseCargada,cts.Token,_proveedor?.Codigo??"");if(!cts.IsCancellationRequested){_articulosEncontrados=lista;_articuloActivo=0;}}
        catch(OperationCanceledException){}catch(Exception ex){if(!cts.IsCancellationRequested)_feedback=UiOps.BuildMessage(ex,"Buscar artículo");}
        finally{if(!cts.IsCancellationRequested)_buscandoArticulos=false;}
    }
    private async Task ArticuloModalKeyDown(KeyboardEventArgs e)
    {
        if(e.Key=="Escape"){CerrarArticulos();return;}
        if(_busy||_buscandoArticulos||_articulosEncontrados.Count==0)return;
        if(e.Key=="ArrowDown")_articuloActivo=Math.Min(_articuloActivo+1,_articulosEncontrados.Count-1);
        else if(e.Key=="ArrowUp")_articuloActivo=Math.Max(_articuloActivo-1,0);
        else if(e.Key=="Enter"){await ElegirArticulo(_articulosEncontrados[_articuloActivo]);return;}
        else return;
        try{await Js.InvokeVoidAsync("cargaDirecta.scrollIntoView",_editorId+"-resultado-"+_articuloActivo);}catch(JSDisconnectedException){}
    }
    private async Task ElegirArticulo(CompraArticulo a)
    {
        if(_renglonBuscando is {} renglon)
        {
            // Búsqueda abierta desde un renglón leído por la IA sin artículo: el elegido se asigna a ese renglón, no a una línea nueva.
            _renglonBuscando=null;_mostrarArticulos=false;_busquedaArticulos?.Cancel();
            await Operar(ct=>AsignarYIncorporarAsync(renglon,a.Codigo,ct));return;
        }
        if(a.Especial||a.Unidad.Length==0){_feedback=AppUiMessage.Warning("Artículo con tratamiento especial","Requiere series, lotes, equivalencias u otros impuestos. Revisalo en su circuito específico.");return;}
        _nuevoArticulo=a;_nuevoCodigo=a.Codigo;_nuevaCantidad=_cantidadModal is {} cm?NumeroTexto(cm):"1";_cantidadModal=null;_nuevoCosto=NumeroTexto(a.Costo);_mostrarArticulos=false;_busquedaArticulos?.Cancel();_feedback=null;
        await FocoArticulo("-cantidad");
    }
    private Task AgregarArticulo()
    {
        if(NuevaLineaBloqueada||_nuevoArticulo is not {} a)return Task.CompletedTask;
        var cantidad=NumeroGrilla(_nuevaCantidad);var costo=NumeroGrilla(_nuevoCosto);
        if(cantidad is not >0||costo is not >0||cantidad>9999999||costo>999999999||decimal.Round(cantidad.Value,4)!=cantidad||decimal.Round(costo.Value,4)!=costo)
        {_feedback=AppUiMessage.Warning("Revisar cantidad y costo","Ingresá valores positivos con hasta cuatro decimales.");return Task.CompletedTask;}
        Cambiar(()=>{
            _filas.RemoveAll(f=>f.Articulo.Length==0&&f.Descripcion.Length==0&&f.Neto.Length==0&&f.Cuenta.Length==0);
            _filas.Add(new(){Articulo=a.Codigo,ArticuloHuella=a.Huella,UnidadArticulo=a.Unidad,Cantidad=cantidad.Value,CostoUnitario=costo.Value,
                CantidadTexto=NumeroTexto(cantidad.Value),CostoTexto=NumeroTexto(costo.Value),Descripcion=a.Descripcion,Cuenta=a.Cuenta,
                Neto=NumeroTexto(CompraCargaPlanificador.Moneda(cantidad.Value*costo.Value)),Tratamiento=a.Exento?"Exento":"Gravado",
                Tasa=NumeroTexto(a.Exento||_borrador.Letra=="C"?0:a.Tasa)});
            _nuevoArticulo=null;_nuevoCodigo="";_nuevaCantidad="1";_nuevoCosto="";
        });return FocoArticulo("-codigo");
    }
    private Task AbrirProveedores()=>Operar(async ct=>{_textoProveedores="";_proveedoresEncontrados=await Servicio.BuscarProveedoresAsync("",_baseCargada,ct);_mostrarProveedores=true;await FocoArticulo("-proveedor-busqueda");});
    private void CerrarArticulos(){_renglonBuscando=null;_mostrarArticulos=false;_busquedaArticulos?.Cancel();_buscandoArticulos=false;}
    private Task ProveedorModalKeyDown(KeyboardEventArgs e)=>e.Key=="Enter"?BuscarProveedoresModal():Task.CompletedTask;
    private Task BuscarProveedoresModal()=>Operar(async ct=>{_proveedoresEncontrados=await Servicio.BuscarProveedoresAsync(_textoProveedores,_baseCargada,ct);});
    private Task ElegirProveedorModal(CompraCargaProveedor p){_mostrarProveedores=false;return ProveedorChanged(p);}
    private async Task CerrarTeclado()
    {
        try{if(_teclado is not null){await _teclado.InvokeVoidAsync("dispose",_editorId);await _teclado.DisposeAsync();}}
        catch(JSDisconnectedException){}catch(InvalidOperationException){}
        finally{_referenciaJs?.Dispose();}
    }
}

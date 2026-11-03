using System.Globalization;
using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace AlfaCore.Components.Pages;

public partial class ComprasCarga
{
    [SupplyParameterFromQuery(Name="documento")] public long? Documento{get;set;}
    private readonly CancellationTokenSource _vida=new();
    private bool _busy,_disposed;private int? _baseCargada;private Guid? _sesion;
    private bool _mostrarAltaProveedor,_puedeAltaProveedor;
    private bool _confirmarRegistro;private Guid _operacion=Guid.NewGuid();private CompraCargaRegistrada? _registrada;
    private bool EdicionBloqueada=>_busy||_registrada is not null;
    private bool PuedeRegistrar=>!EdicionBloqueada&&_vista is {Errores.Count:0}&&_borrador.TotalDocumento is not null;
    private readonly Dictionary<string,(string Letra,string Sucursal,string Numero)> _identidades=[];
    private CompraCargaContexto? _contexto;private CompraCargaProveedor? _proveedor;private CompraCargaOrigen? _origen;
    private CompraCargaBorrador _borrador=new();private CompraCargaVista? _vista;private AppUiMessage? _feedback;
    private PageHeaderConfig? _header;private string _buscarProveedor="",_percIva="0",_percIibb="0",_percGan="0",_totalDocumento="";
    private readonly List<FilaCompra> _filas=[];
    private sealed class FilaCompra {public string Descripcion="",Cuenta="",Tratamiento="Gravado",Neto="",Tasa="0",Iva="";}
    private bool Directo=>QueryHelpers.ParseQuery(new Uri(Nav.Uri).Query).TryGetValue("directo",out var v)&&v=="1";
    private IEnumerable<CompraCargaTipo> TiposVisibles=>(_contexto?.Tipos??[]).Where(x=>x.Unidad.Length==0||x.Unidad==_borrador.Unidad)
        .GroupBy(x=>(x.Codigo,x.Talonario)).Select(g=>g.FirstOrDefault(x=>x.Unidad==_borrador.Unidad)??g.First());
    private CompraCargaTipo? TipoActual=>TiposVisibles.FirstOrDefault(x=>x.Codigo==_borrador.Tipo&&x.Talonario==_borrador.Talonario);
    private bool PuedeVista=>TipoActual?.VistaPreviaDisponible==true&&(_borrador.Letra is "A" or "C")&&TipoActual.Letras.Contains(_borrador.Letra,StringComparison.Ordinal);
    private IReadOnlyList<AlfaSelectOption> TiposComprobantes=>new[]{new AlfaSelectOption("","Seleccionar comprobante")}.Concat(TiposVisibles.Select(t=>new AlfaSelectOption(t.Clave,$"{t.Codigo} · {t.Descripcion} · Tal. {t.Talonario}"))).ToList();
    private IReadOnlyList<AlfaSelectOption> Letras=>(TipoActual?.Letras??"").Where(char.IsAsciiLetter).Distinct().Select(l=>new AlfaSelectOption(l.ToString(),l.ToString())).ToList();
    private static readonly IReadOnlyList<AlfaSelectOption> Tratamientos=[new("Gravado","Gravado"),new("NoGravado","No gravado"),new("Exento","Exento")];
    private static IReadOnlyList<AlfaSelectOption> Opciones(IReadOnlyList<CompraCargaOpcion> opciones)=>new[]{new AlfaSelectOption("","Seleccionar")}.Concat(opciones.Select(o=>new AlfaSelectOption(o.Codigo,$"{o.Codigo} · {o.Descripcion}"))).ToList();
    private IReadOnlyList<AlfaSelectOption> Cuentas=>new[]{new AlfaSelectOption("","Automática: proveedor / motivo")}.Concat((_contexto?.Cuentas??[]).Select(o=>new AlfaSelectOption(o.Codigo,$"{o.Codigo} · {o.Descripcion}"))).ToList();
    private IReadOnlyList<AlfaSelectOption> Tasas=>new[]{0m}.Concat(_contexto?.Tasas??[]).Select(t=>new AlfaSelectOption(NumeroTexto(t),$"{t:N2} %")).ToList();
    private InterfacesAdjuntoDto? AdjuntoVista=>_origen?.Documento.Adjuntos.FirstOrDefault(a=>!a.Eliminado&&a.Extension.ToLowerInvariant() is ".pdf" or ".jpg" or ".jpeg" or ".png");
    private static string NumeroTexto(decimal n)=>n.ToString(CultureInfo.InvariantCulture);
    private static string Dinero(decimal n)=>n.ToString("N2",CultureInfo.GetCultureInfo("es-AR"));
    private static string FechaTexto(DateTime f)=>f.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    protected override void OnInitialized(){Sesiones.SessionChanged+=CambioSesion;Usuarios.StateChanged+=CambioSesion;Header();}
    protected override async Task OnParametersSetAsync()=>await Operar(async ct=>
    {
        _contexto=null;_origen=null;_vista=null;_proveedor=null;_filas.Clear();_feedback=null;_registrada=null;_confirmarRegistro=false;_operacion=Guid.NewGuid();
        var tenant=await TenantGuard.ResolveAsync(idbase,idweb,ct);
        if(!tenant.IsValid)throw new InvalidOperationException("No se pudo resolver la base de esta URL.");
        _baseCargada=idbase??Sesiones.GetActiveSession()?.BaseId;_sesion=Sesiones.GetActiveSession()?.Id;
        _contexto=await Servicio.ContextoAsync(_baseCargada,ct);
        _borrador=new(){Unidad=_contexto.Unidad,Motivo=_contexto.Motivo};_identidades.Clear();
        _puedeAltaProveedor=await Permisos.HasAccessAsync(ProveedorAltaRapidaService.PermisoProveedor,ct);
        _percIva=_percIibb=_percGan="0";_totalDocumento="";_buscarProveedor="";
        if(Documento is {} id)
        {
            _origen=await Servicio.OrigenAsync(id,_baseCargada,ct);_borrador=_origen.Borrador;_borrador.Unidad=_contexto.Unidad;_borrador.Motivo=_contexto.Motivo;
            foreach(var l in _borrador.Lineas)_filas.Add(new(){Descripcion=l.Descripcion,Cuenta=l.Cuenta,Tratamiento=l.Tratamiento,Neto=NumeroTexto(l.Neto),Tasa=NumeroTexto(l.Tasa),Iva=l.IvaInformado is {} iva?NumeroTexto(iva):""});
            _percIva=NumeroTexto(_borrador.PercepcionIva);_percIibb=NumeroTexto(_borrador.PercepcionIibb);_percGan=NumeroTexto(_borrador.PercepcionGanancias);_totalDocumento=_borrador.TotalDocumento is {} total?NumeroTexto(total):"";
            if(_borrador.Proveedor.Length>0)
                try{_proveedor=await Servicio.ProveedorAsync(_borrador.Proveedor,_baseCargada,ct);_buscarProveedor=_proveedor.Nombre;if(_proveedor.Motivo.Length>0)_borrador.Motivo=_proveedor.Motivo;}
                catch(AppUserFacingException ex){_feedback=UiOps.BuildMessage(ex,"Proveedor de la lectura");_borrador.Proveedor="";}
        }
        if(_filas.Count==0)Agregar();
        if(TipoActual is null)
        {
            var preferido=TiposVisibles.FirstOrDefault(t=>t.Codigo==_borrador.Tipo)??(Documento is null?TiposVisibles.FirstOrDefault(t=>t.Codigo=="FCC")??TiposVisibles.FirstOrDefault():null);
            if(preferido is not null){_borrador.Tipo=preferido.Codigo;_borrador.Talonario=preferido.Talonario;}
        }
        if(TipoActual is {} tipoInicial)AplicarTipo(tipoInicial);
    });
    protected override void OnAfterRender(bool firstRender){if(firstRender)Header();}
    private Task<IReadOnlyList<CompraCargaProveedor>> BuscarProveedores(string texto,CancellationToken ct)=>Servicio.BuscarProveedoresAsync(texto,_baseCargada,ct);
    private Task ProveedorChanged(CompraCargaProveedor? p)
    {
        Cambiar(()=>AsignarProveedor(p));return Task.CompletedTask;
    }
    private void AsignarProveedor(CompraCargaProveedor? p){_proveedor=p;_borrador.Proveedor=p?.Codigo??"";_borrador.Motivo=p?.Motivo.Length>0?p.Motivo:_contexto?.Motivo??"";_vista=null;}
    private Task ProveedorCreado(string codigo)=>Operar(async ct=>{
        var proveedor=await Servicio.ProveedorAsync(codigo,_baseCargada,ct);AsignarProveedor(proveedor);_buscarProveedor=proveedor.Nombre;
        _mostrarAltaProveedor=false;_feedback=AppUiMessage.Success("Proveedor seleccionado","Se conservan los conceptos, importes y datos de la compra. La compra no se registró.");
    });
    private void CerrarAltaProveedor(){if(!_busy)_mostrarAltaProveedor=false;}
    private void CambiarUnidad(string unidad)=>Cambiar(()=>{
        GuardarIdentidad();_borrador.Unidad=unidad;
        var tipo=TipoActual??TiposVisibles.FirstOrDefault(t=>t.Codigo==_borrador.Tipo)??TiposVisibles.FirstOrDefault();
        if(tipo is not null)AplicarTipo(tipo);
    });
    private void CambiarTipo(string clave)=>Cambiar(()=>{
        GuardarIdentidad();var tipo=TiposVisibles.FirstOrDefault(t=>t.Clave==clave);
        if(tipo is null){_borrador.Tipo="";_vista=null;return;}AplicarTipo(tipo);
    });
    private void GuardarIdentidad(){if(TipoActual is {} tipo)_identidades[tipo.Clave]=(_borrador.Letra,_borrador.Sucursal,_borrador.Numero);}
    private void AplicarTipo(CompraCargaTipo tipo)
    {
        _borrador.Tipo=tipo.Codigo;_borrador.Talonario=tipo.Talonario;
        if(_identidades.TryGetValue(tipo.Clave,out var id)){_borrador.Letra=id.Letra;_borrador.Sucursal=id.Sucursal;_borrador.Numero=id.Numero;}
        else if(_borrador.Letra.Length!=1||!tipo.Letras.Contains(_borrador.Letra,StringComparison.Ordinal))
        {
            var letra=tipo.Letras.FirstOrDefault(char.IsAsciiLetter);_borrador.Letra=letra=='\0'?"":letra.ToString();
        }
    }
    private void Cambiar(Action cambio){if(EdicionBloqueada)return;cambio();_vista=null;_operacion=Guid.NewGuid();Header();}
    private void Agregar(){if(_registrada is not null||_filas.Count>=100)return;_filas.Add(new(){Tasa=NumeroTexto(_contexto?.Tasas.FirstOrDefault()??0)});_vista=null;}
    private void Quitar(FilaCompra fila)=>Cambiar(()=>_filas.Remove(fila));
    private void CambiarTratamiento(FilaCompra fila,string valor)=>Cambiar(()=>{fila.Tratamiento=valor;if(valor!="Gravado")fila.Tasa="0";});
    private void CambiarFecha(string? v,int tipo)=>Cambiar(()=>{
        if(!DateTime.TryParseExact(v,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var f))f=DateTime.MinValue;
        if(tipo==0)_borrador.Fecha=f;else if(tipo==1)_borrador.FechaContable=f;else _borrador.Vencimiento=f;
    });
    private static decimal Numero(string v,string campo)
    {
        if(!decimal.TryParse(v,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n))throw new InvalidOperationException($"Completá un importe válido en {campo}.");
        return n;
    }
    private Task Preparar()=>Operar(async ct=>{
        if(!PuedeVista)throw new InvalidOperationException("El circuito seleccionado todavía no admite vista previa contable en esta pantalla.");
        _vista=null;
        _borrador.Lineas=_filas.Select((f,i)=>new CompraCargaLinea{Descripcion=f.Descripcion,Cuenta=f.Cuenta,Tratamiento=f.Tratamiento,Neto=Numero(f.Neto,$"neto del concepto {i+1}"),Tasa=Numero(f.Tasa,"tasa IVA"),IvaInformado=f.Iva.Length>0?Numero(f.Iva,"IVA informado"):null}).ToList();
        _borrador.PercepcionIva=Numero(_percIva,"percepción IVA");_borrador.PercepcionIibb=Numero(_percIibb,"percepción IIBB");_borrador.PercepcionGanancias=Numero(_percGan,"percepción Ganancias");
        _borrador.TotalDocumento=_totalDocumento.Length>0?Numero(_totalDocumento,"total del documento"):null;
        _vista=await Servicio.PrepararAsync(_borrador,_baseCargada,ct);
    });
    private Task RegistrarCompra()=>Operar(async ct=>{
        if(_vista is null||_sesion is null||_registrada is not null)return;
        _registrada=await Servicio.RegistrarAsync(new(_operacion,_sesion.Value,_borrador,_vista.Huella),_baseCargada,ct);
        _confirmarRegistro=false;
        _feedback=AppUiMessage.Success("Compra registrada",$"FCC {_registrada.Comprobante} · Asiento {_registrada.Asiento} · Total {Dinero(_registrada.Total)}. Se guardaron comprobante, asiento y datos fiscales.");
    });
    private void CerrarRegistro(){if(!_busy)_confirmarRegistro=false;}
    private async Task Operar(Func<CancellationToken,Task> accion)
    {
        if(_busy||_disposed||_vida.IsCancellationRequested)return;_busy=true;Header();
        try{await InvokeAsync(StateHasChanged);await accion(_vida.Token);}
        catch(OperationCanceledException) when(_vida.IsCancellationRequested){}
        catch(Exception ex)
        {
            if(!_vida.IsCancellationRequested)
            {
                if(ex is not AppUserFacingException){var mensaje=ex is InvalidOperationException?ex.Message:"No se pudo preparar la compra.";var codigo=await Eventos.LogErrorAsync("CargaCompras","Pantalla",ex,mensaje);ex=new AppUserFacingException(mensaje,codigo,ex);}
                _feedback=UiOps.BuildMessage(ex,"Preparar compra");
            }
        }
        finally{_busy=false;if(!_disposed){Header();await InvokeAsync(StateHasChanged);}}
    }
    private string UrlAdjunto(long id)
    {
        var url=$"/api/interfaces/adjuntos/{_baseCargada}/{id}";return string.IsNullOrWhiteSpace(Usuarios.CurrentToken)?url:QueryHelpers.AddQueryString(url,"token",Usuarios.CurrentToken);
    }
    private void Header()
    {
        var acciones=new List<PageHeaderAction>{new(){Key="registrar",Label="Registrar compra",Icon="bi-save",Disabled=!PuedeRegistrar,OnClick=()=>{_confirmarRegistro=true;return InvokeAsync(StateHasChanged);}},new(){Key="preparar",Label=_busy?"Procesando…":"Revisar asiento",Icon="bi-journal-check",Busy=_busy,Disabled=EdicionBloqueada||!PuedeVista,OnClick=Preparar},new(){Key="concepto",Label="Agregar concepto",Icon="bi-plus",Disabled=EdicionBloqueada||!PuedeVista||_filas.Count>=100,OnClick=()=>{Agregar();return InvokeAsync(StateHasChanged);}}};
        if(_registrada is not null)acciones.Add(new(){Key="nueva",Label="Nueva compra",Icon="bi-plus",Disabled=_busy,OnClick=()=>{Nav.NavigateTo(RouteContext.BuildRoute("/compras/carga")+(Directo?"?directo=1":""),true);return Task.CompletedTask;}});
        if(_puedeAltaProveedor&&_contexto is not null)acciones.Add(new(){Key="proveedor-nuevo",Label="Nuevo proveedor",Icon="bi-person-plus",Disabled=EdicionBloqueada,OnClick=()=>{_mostrarAltaProveedor=true;return InvokeAsync(StateHasChanged);}});
        if(!Directo&&_origen is {} o)acciones.Add(new(){Key="origen",Label="Recepción documental",Icon="bi-file-earmark",Disabled=_busy,OnClick=()=>{Nav.NavigateTo(RouteContext.BuildRoute($"/interfaces/{o.Documento.IdComprobanteRecibido}"));return Task.CompletedTask;}});
        PageHeader.Set(_header=new(){ModuleKey="compras",ShellMode=PageHeaderShellMode.AlfaDesignPilot,Title="Preparar compra",ResponsiveToolbar=true,TopNavigationItems=Directo?[new(){Key="carga-compras",Label="Preparar compra",Active=true}]:ModuleTopNavPresets.BuildCompras(RouteContext,Nav,"carga-compras"),Actions=acciones});
    }
    private void CambioSesion()
    {
        if(_sesion is null)return;
        if(Sesiones.GetActiveSession()?.Id==_sesion&&TenantDataAccessGuard.IsActiveSessionAuthorized(Sesiones,Usuarios))return;
        _vida.Cancel();_mostrarAltaProveedor=false;_confirmarRegistro=false;_registrada=null;_contexto=null;_origen=null;_vista=null;_proveedor=null;_filas.Clear();_feedback=AppUiMessage.Warning("La sesión cambió","Volvé a abrir Preparar compra en la base activa.");_ = InvokeAsync(StateHasChanged);
    }
    public void Dispose(){_disposed=true;Sesiones.SessionChanged-=CambioSesion;Usuarios.StateChanged-=CambioSesion;_vida.Cancel();_vida.Dispose();if(ReferenceEquals(PageHeader.Current,_header))PageHeader.Clear();}
}

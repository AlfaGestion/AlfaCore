using System.Globalization;
using System.Text.Json;
using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace AlfaCore.Components.Pages;

public partial class ComprasCarga
{
    [SupplyParameterFromQuery(Name="documento")] public long? Documento{get;set;}
    // Desde «Ver comprobantes»: facturar una OC/remito (origen) o copiar una compra para cargar otra.
    [SupplyParameterFromQuery(Name="origen")] public int? OrigenMercaderia{get;set;}
    [SupplyParameterFromQuery(Name="copiar")] public int? CopiarCompraId{get;set;}
    private readonly CancellationTokenSource _vida=new();
    private bool _busy,_disposed;private int? _baseCargada;private Guid? _sesion;
    private bool _mostrarAltaProveedor,_puedeAltaProveedor,_mostrarConsulta,_mostrarBorradores;
    private Guid _borradorId=Guid.NewGuid();private int _borradorVersion;private IReadOnlyList<CompraBorradorGuardado> _borradores=[];
    private CompraCargaService.FacturaNota? _facturaNota;private string _buscarFacturaNota="",_notaAplicar="0",_impuestoInterno="0";
    private string _cuentaCc="",_centroCc="",_porcentajeCc="100";
    private bool _confirmarRegistro;private Guid _operacion=Guid.NewGuid();private CompraCargaRegistrada? _registrada;
    private bool EdicionBloqueada=>_busy||_registrada is not null;
    private bool PuedeRegistrar=>!EdicionBloqueada&&PuedeVista&&_proveedor is not null&&_filas.Count>0;
    // El total se calcula solo; pasa a manual cuando el operador lo edita para cotejarlo con el comprobante del proveedor.
    private bool _totalManual;private CancellationTokenSource? _recalculo;
    private readonly Dictionary<string,(string Letra,string Sucursal,string Numero)> _identidades=[];
    private CompraCargaContexto? _contexto;private CompraCargaProveedor? _proveedor;private CompraCargaOrigen? _origen;
    private CompraCargaBorrador _borrador=new();private CompraCargaVista? _vista;private AppUiMessage? _feedback;
    private PageHeaderConfig? _header;private string _buscarProveedor="",_percIva="0",_percIibb="0",_percGan="0",_totalDocumento="";
    private readonly List<FilaCompra> _filas=[];
    public sealed class FilaCompra {public string Descripcion{get;set;}="";public string Cuenta{get;set;}="";public string Tratamiento{get;set;}="Gravado";public string Neto{get;set;}="";public string Tasa{get;set;}="0";public string Iva{get;set;}="";public string Articulo{get;set;}="";public string UnidadArticulo{get;set;}="";public decimal Cantidad{get;set;}public decimal? CostoUnitario{get;set;}public string ArticuloHuella{get;set;}="";public string? CantidadTexto{get;set;}public string? CostoTexto{get;set;}}
    public sealed record EdicionCompra(CompraCargaBorrador Borrador,List<FilaCompra> Filas,string PercIva,string PercIibb,string PercGan,string Total,string ImpuestoInterno="0",string NotaAplicar="0");
    private bool Directo=>QueryHelpers.ParseQuery(new Uri(Nav.Uri).Query).TryGetValue("directo",out var v)&&v=="1";
    private IEnumerable<CompraCargaTipo> TiposVisibles=>(_contexto?.Tipos??[]).Where(x=>x.Unidad.Length==0||x.Unidad==_borrador.Unidad)
        .GroupBy(x=>(x.Codigo,x.Talonario)).Select(g=>g.FirstOrDefault(x=>x.Unidad==_borrador.Unidad)??g.First());
    private CompraCargaTipo? TipoActual=>TiposVisibles.FirstOrDefault(x=>x.Codigo==_borrador.Tipo&&x.Talonario==_borrador.Talonario);
    private string TituloRegistro=>_borrador.Tipo switch{"OC"=>"Registrar orden de compra","RMC"=>"Registrar remito de compra",_=>"Registrar factura de compra"};
    private bool EsComercial=>TipoActual?.EsComercial==true;
    private bool PuedeVista=>TipoActual?.VistaPreviaDisponible==true&&_borrador.Letra.Length==1&&(EsComercial||_borrador.Letra is "A" or "C")&&TipoActual.Letras.Contains(_borrador.Letra,StringComparison.Ordinal);
    private IReadOnlyList<AlfaSelectOption> TiposComprobantes=>new[]{new AlfaSelectOption("","Seleccionar comprobante")}.Concat(TiposVisibles.Select(t=>new AlfaSelectOption(t.Clave,$"{t.Codigo} · {t.Descripcion} · Tal. {t.Talonario}"))).ToList();
    private IReadOnlyList<AlfaSelectOption> Letras=>(TipoActual?.Letras??"").Where(char.IsAsciiLetter).Distinct().Select(l=>new AlfaSelectOption(l.ToString(),l.ToString())).ToList();
    private static readonly IReadOnlyList<AlfaSelectOption> Tratamientos=[new("Gravado","Gravado"),new("NoGravado","No gravado"),new("Exento","Exento")];
    private static IReadOnlyList<AlfaSelectOption> Opciones(IReadOnlyList<CompraCargaOpcion> opciones)=>new[]{new AlfaSelectOption("","Seleccionar")}.Concat(opciones.Select(o=>new AlfaSelectOption(o.Codigo,$"{o.Codigo} · {o.Descripcion}"))).ToList();
    private IReadOnlyList<AlfaSelectOption> Cuentas=>new[]{new AlfaSelectOption("","Automática: artículo / proveedor / motivo")}.Concat((_contexto?.Cuentas??[]).Select(o=>new AlfaSelectOption(o.Codigo,$"{o.Codigo} · {o.Descripcion}"))).ToList();
    private IReadOnlyList<AlfaSelectOption> Tasas=>new[]{0m}.Concat(_contexto?.Tasas??[]).Select(t=>new AlfaSelectOption(NumeroTexto(t),$"{t:N2} %")).ToList();
    private InterfacesAdjuntoDto? AdjuntoVista=>_origen?.Documento.Adjuntos.FirstOrDefault(a=>!a.Eliminado&&a.Extension.ToLowerInvariant() is ".pdf" or ".jpg" or ".jpeg" or ".png");
    private static string NumeroTexto(decimal n)=>n.ToString(CultureInfo.InvariantCulture);
    private static string Dinero(decimal n)=>n.ToString("N2",CultureInfo.GetCultureInfo("es-AR"));
    private static string FechaTexto(DateTime f)=>f.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    protected override void OnInitialized(){Sesiones.SessionChanged+=CambioSesion;Usuarios.StateChanged+=CambioSesion;Header();}
    private string? _rutaCargada;private bool _directoCargado;
    private string ParametrosActuales=>$"{idweb}|{idbase}|{Documento}|{OrigenMercaderia}|{CopiarCompraId}|{Sesiones.GetActiveSession()?.Id}|{Usuarios.GetCurrentUserName()}";
    protected override async Task OnParametersSetAsync()
    {
        if(_contexto is not null&&_rutaCargada==ParametrosActuales){if(_directoCargado!=Directo){_directoCargado=Directo;Header();}return;}
        await Operar(async ct=>
    {
        _contexto=null;_origen=null;_vista=null;_proveedor=null;_filas.Clear();_feedback=null;_registrada=null;_confirmarRegistro=false;_operacion=Guid.NewGuid();_borradorId=Guid.NewGuid();_borradorVersion=0;_impuestoInterno="0";_notaAplicar="0";_facturaNota=null;_buscarFacturaNota="";
        var tenant=await TenantGuard.ResolveAsync(idbase,idweb,ct);
        if(!tenant.IsValid)throw new InvalidOperationException("No se pudo resolver la base de esta URL.");
        _baseCargada=idbase??Sesiones.GetActiveSession()?.BaseId;_sesion=Sesiones.GetActiveSession()?.Id;
        _contexto=await Servicio.ContextoAsync(_baseCargada,ct);
        _borrador=new(){Unidad=_contexto.Unidad,Motivo=_contexto.Motivo};_identidades.Clear();
        _puedeAltaProveedor=await Permisos.HasAccessAsync(ProveedorAltaRapidaService.PermisoProveedor,ct);
        _percIva=_percIibb=_percGan="0";_totalDocumento="";_totalManual=false;_buscarProveedor="";
        if(Documento is {} id)
        {
            _origen=await Servicio.OrigenAsync(id,_baseCargada,ct);_borrador=_origen.Borrador;_borrador.Unidad=_contexto.Unidad;_borrador.Motivo=_contexto.Motivo;
            foreach(var l in _borrador.Lineas)_filas.Add(new(){Descripcion=l.Descripcion,Cuenta=l.Cuenta,Tratamiento=l.Tratamiento,Articulo=l.Articulo,ArticuloHuella=l.ArticuloHuella,UnidadArticulo=l.UnidadArticulo,Cantidad=l.Cantidad,CostoUnitario=l.CostoUnitario,Neto=NumeroTexto(l.Neto),Tasa=NumeroTexto(l.Tasa),Iva=l.IvaInformado is {} iva?NumeroTexto(iva):""});
            _impuestoInterno=NumeroTexto(_borrador.ImpuestoInterno);_percIva=NumeroTexto(_borrador.PercepcionIva);_percIibb=NumeroTexto(_borrador.PercepcionIibb);_percGan=NumeroTexto(_borrador.PercepcionGanancias);_totalDocumento=_borrador.TotalDocumento is {} total?NumeroTexto(total):"";_totalManual=_totalDocumento.Length>0;
            if(_borrador.Proveedor.Length>0)
                try{_proveedor=await Servicio.ProveedorAsync(_borrador.Proveedor,_baseCargada,ct);_buscarProveedor=_proveedor.Nombre;if(_proveedor.Motivo.Length>0)_borrador.Motivo=_proveedor.Motivo;}
                catch(AppUserFacingException ex){_feedback=UiOps.BuildMessage(ex,"Proveedor de la lectura");_borrador.Proveedor="";}
        }
        if(_filas.Count==0&&Documento is not null)Agregar();
        if(TipoActual is null)
        {
            var preferido=TiposVisibles.FirstOrDefault(t=>t.Codigo==_borrador.Tipo)??(Documento is null?TiposVisibles.FirstOrDefault(t=>t.Codigo=="FCC")??TiposVisibles.FirstOrDefault():null);
            if(preferido is not null){_borrador.Tipo=preferido.Codigo;_borrador.Talonario=preferido.Talonario;}
        }
        if(TipoActual is {} tipoInicial)AplicarTipo(tipoInicial);
        if(_borrador.Deposito.Length==0)_borrador.Deposito=_contexto.Depositos.FirstOrDefault()?.Codigo??"";
        if(OrigenMercaderia is {} origenId)await AplicarBorradorAsync(await Servicio.PrepararDesdeOrigenAsync(origenId,_baseCargada,ct),ct);
        else if(CopiarCompraId is {} copiarId)await AplicarBorradorAsync(await Servicio.CopiarCompraAsync(copiarId,_baseCargada,ct),ct);
        _rutaCargada=ParametrosActuales;_directoCargado=Directo;
    });
    }
    protected override void OnAfterRender(bool firstRender){if(firstRender){Header();_=IniciarTeclado();}}
    private Task<IReadOnlyList<CompraCargaProveedor>> BuscarProveedores(string texto,CancellationToken ct)=>Servicio.BuscarProveedoresAsync(texto,_baseCargada,ct);
    private Task ProveedorChanged(CompraCargaProveedor? p)
    {
        if(p is null&&_proveedor is null)return Task.CompletedTask;
        Cambiar(()=>{AsignarProveedor(p);if(p is not null){_buscarProveedor=$"{p.Codigo} · {p.Nombre}";if(!EsComercial&&TipoActual?.Letras.Contains(p.CondicionIva=="5"?"C":"A")==true)_borrador.Letra=p.CondicionIva=="5"?"C":"A";}});return p is null?Task.CompletedTask:FocoArticulo("-codigo");
    }
    private void AsignarProveedor(CompraCargaProveedor? p){_facturaNota=null;_borrador.NotaFacturaId=null;_notaAplicar="0";_proveedor=p;_borrador.Proveedor=p?.Codigo??"";_borrador.Motivo=p?.Motivo.Length>0?p.Motivo:_contexto?.Motivo??"";_vista=null;}
    private Task ProveedorCreado(string codigo)=>Operar(async ct=>{
        var proveedor=await Servicio.ProveedorAsync(codigo,_baseCargada,ct);AsignarProveedor(proveedor);_buscarProveedor=proveedor.Nombre;
        _mostrarAltaProveedor=false;_feedback=AppUiMessage.Success("Proveedor seleccionado","Se conservan los conceptos, importes y datos de la compra. La compra no se registró.");
    });
    private void CerrarAltaProveedor(){if(!_busy)_mostrarAltaProveedor=false;}
    private void CambiarUnidad(string unidad)=>Cambiar(()=>{
        GuardarIdentidad();_borrador.Unidad=unidad;_facturaNota=null;_borrador.NotaFacturaId=null;_notaAplicar="0";
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
        if(_borrador.Tipo!=tipo.Codigo){_facturaNota=null;_borrador.NotaFacturaId=null;_notaAplicar="0";}
        _borrador.Tipo=tipo.Codigo;_borrador.Talonario=tipo.Talonario;
        if(_identidades.TryGetValue(tipo.Clave,out var id)){_borrador.Letra=id.Letra;_borrador.Sucursal=id.Sucursal;_borrador.Numero=id.Numero;}
        else if(_borrador.Letra.Length!=1||!tipo.Letras.Contains(_borrador.Letra,StringComparison.Ordinal))
        {
            var letra=tipo.Codigo=="RMC"&&tipo.Letras.Contains('R')?'R':tipo.Letras.FirstOrDefault(char.IsAsciiLetter);_borrador.Letra=letra=='\0'?"":letra.ToString();
        }
    }
    private void Cambiar(Action cambio){if(EdicionBloqueada)return;cambio();SincronizarTotal();_operacion=Guid.NewGuid();Header();ProgramarVista();}
    private void SincronizarTotal(){if(!_totalManual)_totalDocumento=_filas.Count==0?"":NumeroTexto(CompraCargaPlanificador.Moneda(TotalEstimado));}
    // Recalcula en segundo plano el asiento y los totales fiscales, sin pedir un paso previo de "revisar".
    private void ProgramarVista()
    {
        _recalculo?.Cancel();
        if(_disposed||_registrada is not null||!PuedeVista||_proveedor is null||_filas.Count==0)return;
        var cts=_recalculo=CancellationTokenSource.CreateLinkedTokenSource(_vida.Token);
        _=RecalcularAsync(cts);
    }
    private async Task RecalcularAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(600,cts.Token);
            await InvokeAsync(async()=>
            {
                if(cts.IsCancellationRequested||_busy||_disposed)return;
                try
                {
                    ArmarBorrador();var vista=await Servicio.PrepararAsync(_borrador,_baseCargada,cts.Token,silencioso:true);
                    if(!cts.IsCancellationRequested){_vista=vista;StateHasChanged();}
                }
                catch(OperationCanceledException){}
                catch(Exception){if(!cts.IsCancellationRequested){_vista=null;StateHasChanged();}} // datos incompletos: se informan al registrar
            });
        }
        catch(OperationCanceledException){}
    }
    private void Agregar(){if(_registrada is not null||_filas.Count>=100||_borrador.CompraOrigenId is not null)return;_filas.Add(new(){Tasa=NumeroTexto(_contexto?.Tasas.FirstOrDefault()??0)});_vista=null;}
    private void Quitar(FilaCompra fila)=>Cambiar(()=>_filas.Remove(fila));
    private void CambiarTratamiento(FilaCompra fila,string valor)=>Cambiar(()=>{fila.Tratamiento=valor;if(valor!="Gravado")fila.Tasa="0";});
    private static string RellenarNumero(string? texto,int ancho)
    {
        var digitos=new string((texto??"").Where(char.IsAsciiDigit).ToArray());
        return digitos.Length==0?"":digitos.Length>=ancho?digitos[..ancho]:digitos.PadLeft(ancho,'0');
    }
    private void CambiarLetra(string? texto)
    {
        var letra=(texto??"").Trim().ToUpperInvariant();
        if(letra.Length==1&&TipoActual?.Letras.Contains(letra,StringComparison.Ordinal)==true){Cambiar(()=>_borrador.Letra=letra);return;}
        _feedback=AppUiMessage.Warning("Letra no habilitada",$"Para este comprobante se admiten: {TipoActual?.Letras}.");
    }
    private void CambiarFecha(string? v,int tipo)=>Cambiar(()=>{
        if(!DateTime.TryParseExact(v,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var f))f=DateTime.MinValue;
        if(tipo==0)_borrador.Fecha=f;else if(tipo==1)_borrador.FechaContable=f;else _borrador.Vencimiento=f;
    });
    private static decimal Numero(string v,string campo)
    {
        if(!decimal.TryParse(v.Trim().Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n))throw new InvalidOperationException($"Completá un importe válido en {campo}.");
        return n;
    }
    private void ArmarBorrador()
    {
        SincronizarTotal();
        _borrador.Lineas=_filas.Select((f,i)=>new CompraCargaLinea{Descripcion=f.Descripcion,Cuenta=f.Cuenta,Tratamiento=f.Tratamiento,Articulo=f.Articulo,ArticuloHuella=f.ArticuloHuella,
            UnidadArticulo=f.UnidadArticulo,Cantidad=f.Articulo.Length>0?Numero(CantidadTexto(f),"cantidad del artículo"):0,CostoUnitario=f.Articulo.Length>0?Numero(CostoTexto(f),"costo unitario"):(decimal?)null,
            Neto=f.Articulo.Length>0?CompraCargaPlanificador.Moneda(Numero(CantidadTexto(f),"cantidad")*Numero(CostoTexto(f),"costo")):Numero(f.Neto,$"neto del concepto {i+1}"),Tasa=Numero(f.Tasa,"tasa IVA"),IvaInformado=f.Iva.Length>0?Numero(f.Iva,"IVA informado"):null}).ToList();
        _borrador.PercepcionIva=Numero(_percIva,"percepción IVA");_borrador.PercepcionIibb=Numero(_percIibb,"percepción IIBB");_borrador.PercepcionGanancias=Numero(_percGan,"percepción Ganancias");
        _borrador.TotalDocumento=_totalDocumento.Length>0?Numero(_totalDocumento,"total del documento"):null;
        _borrador.ImpuestoInterno=Numero(_impuestoInterno,"impuestos internos");_borrador.NotaAplicar=Numero(_notaAplicar,"aplicación de la nota");
    }
    // Un solo paso: valida y calcula con los datos actuales; si está todo bien abre la confirmación.
    private Task IniciarRegistro()=>Operar(async ct=>{
        _recalculo?.Cancel();
        if(!PuedeVista)throw new InvalidOperationException("El circuito seleccionado todavía no admite carga en esta pantalla.");
        if(_filas.Count==0)throw new InvalidOperationException("Cargá al menos un artículo o concepto.");
        ArmarBorrador();_vista=await Servicio.PrepararAsync(_borrador,_baseCargada,ct);
        if(_vista.Errores.Count>0){_feedback=AppUiMessage.Warning("Revisá la compra antes de registrar",string.Join(" ",_vista.Errores));return;}
        _confirmarRegistro=true;
    });
    private Task RegistrarCompra()=>Operar(async ct=>{
        if(_vista is null||_sesion is null||_registrada is not null)return;
        _registrada=await Servicio.RegistrarAsync(new(_operacion,_sesion.Value,_borrador,_vista.Huella),_baseCargada,ct);
        _confirmarRegistro=false;
        _feedback=EsComercial?AppUiMessage.Success(_borrador.Tipo=="OC"?"Orden de compra registrada":"Remito registrado",$"{_borrador.Tipo} {_registrada.Comprobante} · Total {Dinero(_registrada.Total)}. No genera asiento ni libro de IVA."+(_borrador.Tipo=="RMC"?" El stock ingresó.":" Quedó aprobada y pendiente de recepción o facturación."))
            :AppUiMessage.Success("Compra registrada",$"{_borrador.Tipo} {_registrada.Comprobante} · Asiento {_registrada.Asiento} · Total {Dinero(_registrada.Total)}. Se guardaron comprobante, asiento y datos fiscales.");
    });
    private Task<IReadOnlyList<CompraCargaService.FacturaNota>> BuscarFacturasNota(string texto,CancellationToken ct)
        =>Servicio.FacturasParaNotaAsync(_borrador.Proveedor,_borrador.Unidad,texto,_baseCargada,ct);
    private Task SeleccionarFacturaNota(CompraCargaService.FacturaNota? factura){Cambiar(()=>{_facturaNota=factura;_borrador.NotaFacturaId=factura?.Id;_notaAplicar=factura is null?"0":_totalDocumento;});return Task.CompletedTask;}
    private Task GuardarBorrador()=>Operar(async ct=>{
        var json=JsonSerializer.Serialize(new EdicionCompra(_borrador,_filas,_percIva,_percIibb,_percGan,_totalDocumento,_impuestoInterno,_notaAplicar));
        var nombre=$"{_borrador.Tipo} {_borrador.Sucursal}-{_borrador.Numero} · {_proveedor?.Nombre??_borrador.Proveedor}";
        _borradorVersion=await Servicio.GuardarBorradorAsync(_borradorId,_borradorVersion,nombre.Length>160?nombre[..160]:nombre,json,_baseCargada,ct);
        _feedback=AppUiMessage.Success("Borrador guardado","Podés cerrar la pantalla y recuperarlo desde Borradores con este usuario y base. No se registró contabilidad.");
    });
    private Task VerBorradores()=>Operar(async ct=>{_borradores=await Servicio.BorradoresAsync(_baseCargada,ct);_mostrarBorradores=true;});
    private Task Recuperar(CompraBorradorGuardado b)=>Operar(async ct=>{
        var edicion=JsonSerializer.Deserialize<EdicionCompra>(b.Json)??throw new InvalidOperationException("No se pudo recuperar el borrador.");
        var proveedor=edicion.Borrador.Proveedor.Length>0?await Servicio.ProveedorAsync(edicion.Borrador.Proveedor,_baseCargada,ct):null;
        var origen=edicion.Borrador.DocumentoRecibido is {} doc?await Servicio.OrigenAsync(doc,_baseCargada,ct):null;
        _borrador=edicion.Borrador;_borradorId=b.Id;_borradorVersion=b.Version;_filas.Clear();_filas.AddRange(edicion.Filas);
        _percIva=edicion.PercIva;_percIibb=edicion.PercIibb;_percGan=edicion.PercGan;_totalDocumento=edicion.Total;_totalManual=edicion.Total.Length>0;_impuestoInterno=edicion.ImpuestoInterno;_notaAplicar=edicion.NotaAplicar;_facturaNota=null;_buscarFacturaNota=edicion.Borrador.NotaFacturaId is {} asociado?$"Factura asociada #{asociado}":"";
        _origen=origen;_proveedor=proveedor;_buscarProveedor=proveedor?.Nombre??"";_vista=null;_operacion=Guid.NewGuid();_identidades.Clear();
        _mostrarBorradores=false;_feedback=AppUiMessage.Info("Borrador recuperado","Revisá proveedor, configuración, importes y asiento antes de registrar.");
    });
    private Task CopiarCompra(CompraCargaBorrador d)=>Operar(ct=>AplicarBorradorAsync(d,ct));
    private async Task AplicarBorradorAsync(CompraCargaBorrador d,CancellationToken ct)
    {
        var proveedor=await Servicio.ProveedorAsync(d.Proveedor,_baseCargada,ct);
        _borrador=d;_borradorId=Guid.NewGuid();_borradorVersion=0;_registrada=null;_origen=null;_proveedor=proveedor;_buscarProveedor=proveedor.Nombre;
        _filas.Clear();_filas.AddRange(d.Lineas.Select(l=>new FilaCompra{Descripcion=l.Descripcion,Cuenta=l.Cuenta,Tratamiento=l.Tratamiento,Articulo=l.Articulo,ArticuloHuella=l.ArticuloHuella,UnidadArticulo=l.UnidadArticulo,Cantidad=l.Cantidad,CostoUnitario=l.CostoUnitario,Neto=NumeroTexto(l.Neto),Tasa=NumeroTexto(l.Tasa),Iva=l.IvaInformado is {} iva?NumeroTexto(iva):""}));
        _percIva=NumeroTexto(d.PercepcionIva);_percIibb=NumeroTexto(d.PercepcionIibb);_percGan=NumeroTexto(d.PercepcionGanancias);_totalDocumento=d.TotalDocumento is {} total?NumeroTexto(total):"";_totalManual=d.CompraOrigenId is null&&_totalDocumento.Length>0;_impuestoInterno=NumeroTexto(d.ImpuestoInterno);_notaAplicar=NumeroTexto(d.NotaAplicar);_facturaNota=null;_buscarFacturaNota="";
        _vista=null;_operacion=Guid.NewGuid();_identidades.Clear();_mostrarConsulta=false;_feedback=d.CompraOrigenId is not null?AppUiMessage.Info("Origen preparado","Se cargaron las cantidades pendientes. Podés reducirlas o quitar artículos para facturar parcialmente y completar la identificación de la factura."):AppUiMessage.Info("Copia preparada","Ingresá la identificación del nuevo comprobante. No se modificó la compra original.");
    }
    private void AgregarCentro()=>Cambiar(()=>{
        if(!decimal.TryParse(_porcentajeCc,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var porcentaje)){_feedback=AppUiMessage.Warning("Revisar porcentaje","Ingresá un porcentaje válido.");return;}
        if(_cuentaCc.Length==0||_centroCc.Length==0||porcentaje<=0||porcentaje>100){_feedback=AppUiMessage.Warning("Revisar distribución","Elegí cuenta, centro y un porcentaje mayor a cero y hasta 100.");return;}
        _borrador.CentrosCosto.Add(new(_cuentaCc,_centroCc,porcentaje));
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
                _feedback=UiOps.BuildMessage(ex,"Carga de comprobantes de compras");
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
        var acciones=new List<PageHeaderAction>
        {
            new(){Key="registrar",Label="Registrar compra",Icon="bi-save",Style=PageHeaderActionStyle.Primary,Priority=PageHeaderActionPriority.Essential,Disabled=!PuedeRegistrar,OnClick=IniciarRegistro},
            new(){Key="buscar-proveedor",Label="Buscar proveedor (F2)",Icon="bi-search",Priority=PageHeaderActionPriority.Overflow,Disabled=EdicionBloqueada,OnClick=AbrirProveedores},
            new(){Key="buscar-articulo",Label="Buscar artículo (F5)",Icon="bi-box",Priority=PageHeaderActionPriority.Overflow,Disabled=NuevaLineaBloqueada,OnClick=AbrirArticulos},
            new(){Key="guardar-borrador",Label="Guardar borrador",Icon="bi-save2",Priority=PageHeaderActionPriority.Overflow,Disabled=EdicionBloqueada,OnClick=GuardarBorrador},
            new(){Key="borradores",Label="Borradores",Icon="bi-folder",Priority=PageHeaderActionPriority.Overflow,Disabled=EdicionBloqueada,OnClick=VerBorradores}
        };
        if(_registrada is not null)acciones.Add(new(){Key="nueva",Label="Nueva compra",Icon="bi-plus",Disabled=_busy,OnClick=()=>{Nav.NavigateTo(RouteContext.BuildRoute("/compras/carga")+(Directo?"?directo=1":""),true);return Task.CompletedTask;}});
        if(_puedeAltaProveedor&&_contexto is not null)acciones.Add(new(){Key="proveedor-nuevo",Label="Nuevo proveedor",Icon="bi-person-plus",Disabled=EdicionBloqueada,OnClick=()=>{_mostrarAltaProveedor=true;return InvokeAsync(StateHasChanged);}});
        if(!Directo&&_origen is {} o)acciones.Add(new(){Key="origen",Label="Recepción documental",Icon="bi-file-earmark",Disabled=_busy,OnClick=()=>{Nav.NavigateTo(RouteContext.BuildRoute($"/interfaces/{o.Documento.IdComprobanteRecibido}"));return Task.CompletedTask;}});
        PageHeader.Set(_header=new(){ModuleKey="compras",ShellMode=PageHeaderShellMode.AlfaDesignPilot,Title="Carga de comprobantes de compras",Breadcrumb=["Compras","Carga de comprobantes de compras"],ShowHistoryNavigation=false,ResponsiveToolbar=true,
            // Solo las solapas propias, como Ventas: cargar y ver comprobantes.
            TopNavigationItems=[new(){Key="carga",Label="Carga de comprobantes",Active=true},new(){Key="visor",Label="Ver comprobantes",OnClick=()=>{Nav.NavigateTo(RouteContext.BuildRoute("/compras/carga/visor")+(Directo?"?directo=1":""));return Task.CompletedTask;}},new(){Key="pagos",Label="Pagos y retenciones",OnClick=()=>{Nav.NavigateTo(RouteContext.BuildRoute("/compras/carga/pagos")+(Directo?"?directo=1":""));return Task.CompletedTask;}}],Actions=acciones});
    }
    private void CambioSesion()
    {
        if(_sesion is null)return;
        if(Sesiones.GetActiveSession()?.Id==_sesion&&TenantDataAccessGuard.IsActiveSessionAuthorized(Sesiones,Usuarios))return;
        _vida.Cancel();_busquedaArticulos?.Cancel();_mostrarArticulos=false;_mostrarProveedores=false;_articulosEncontrados=[];_proveedoresEncontrados=[];_nuevoArticulo=null;_mostrarAltaProveedor=false;_mostrarConsulta=false;_mostrarBorradores=false;_borradores=[];_confirmarRegistro=false;_registrada=null;_contexto=null;_origen=null;_vista=null;_proveedor=null;_filas.Clear();_feedback=AppUiMessage.Warning("La sesión cambió","Volvé a abrir Carga de comprobantes en la base activa.");_ = InvokeAsync(StateHasChanged);
    }
    public void Dispose(){_disposed=true;_busquedaArticulos?.Cancel();_busquedaArticulos?.Dispose();_=CerrarTeclado();Sesiones.SessionChanged-=CambioSesion;Usuarios.StateChanged-=CambioSesion;_vida.Cancel();_vida.Dispose();if(ReferenceEquals(PageHeader.Current,_header))PageHeader.Clear();}
}

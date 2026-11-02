using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using AlfaCore.Services;

namespace AlfaCore.Components.Pages;

public partial class ConciliacionTarjetas
{
    private bool _mostrarLt;
    private TarjetasLtVista? _lt;
    private List<TarjetasLtAsignacion> _ltCuentas=[];
    private string _ltIndice="0";
    private TarjetasLtLiquidacion? LtElegida=>_lt is not null&&int.TryParse(_ltIndice,out var n)&&n>=0&&n<_lt.Liquidaciones.Count?_lt.Liquidaciones[n]:null;
    private IReadOnlyList<AlfaSelectOption> LtOpciones=>_lt?.Liquidaciones.Select((l,n)=>new AlfaSelectOption(n.ToString(),$"{(l.EsResumenMensual?"Resumen mensual":"Liquidación")} {l.Numero} · {l.Fecha:dd/MM/yyyy} · Neto {Dinero(l.Neto)}")).ToList()??[];
    private IReadOnlyList<AlfaSelectOption> LtOpcionesCuenta(string rol)=>new[]{new AlfaSelectOption("","Seleccionar cuenta")}
        .Concat((_lt?.Cuentas??[]).Where(c=>TarjetasLtPreparacion.Elegible(c,rol)).Select(c=>new AlfaSelectOption(c.Codigo,$"{c.Codigo} · {c.Descripcion}"))).ToList();
    private static string LtNombreRol(string rol)=>rol switch{"Banco"=>"Banco / neto a acreditar","Gastos"=>"Aranceles y gastos","IvaCredito"=>"IVA crédito fiscal","PercepcionIva"=>"Percepciones / retenciones IVA","Iibb"=>"Retenciones Ingresos Brutos","Ganancias"=>"Retenciones Ganancias",_=>rol};
    private Task AbrirLt()=>Operar(async ct=>{
        if(_comparacion is null)return;
        _lt=null;_mostrarLt=true;_ltIndice="0";
        await InvokeAsync(StateHasChanged);
        _lt=await Servicio.PrepararLtAsync(_comparacion.Importacion.Id,ct);
        _ltCuentas=_lt.Asignaciones.Select(r=>new TarjetasLtAsignacion{Rol=r.Rol,Cuenta=r.Cuenta}).ToList();
    },AppUiMessage.InProgress("Preparando vista previa LT…","Leyendo los conceptos del PDF original y las cuentas de la base activa."),"PrepararLT");
    private void CerrarLt(){if(!_busy){_mostrarLt=false;_lt=null;_ltCuentas=[];}}
    private Task GuardarLtCuentas()=>Operar(async ct=>{
        if(_lt is null)return;
        await Servicio.GuardarLtCuentasAsync(_lt.Importacion,_ltCuentas,ct);
        _feedback=AppUiMessage.Success("Cuentas LT guardadas","Se usarán para este banco, comercio, tarjeta y cuenta en la base activa. No se registró ningún asiento.");
    },AppUiMessage.InProgress("Guardando cuentas LT…","Conservando la relación de conceptos con el plan de cuentas."),"GuardarCuentasLT");
}

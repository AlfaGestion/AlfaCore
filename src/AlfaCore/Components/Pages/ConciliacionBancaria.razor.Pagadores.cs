using AlfaCore.Models;

namespace AlfaCore.Components.Pages;

public partial class ConciliacionBancaria
{
    private ConciliacionMovimiento? _buscarPagadorMovimiento;
    private Task UsarPagador(CobranzaPagadorGrupo grupo)=>Operar(async ct=>
    {
        if(_buscarPagadorMovimiento is not {} row)return;
        var actuales=await Cobranzas.BuscarPagadoresAsync(row.Id,grupo.Cliente,ct);
        if(actuales.Importe!=row.Importe)throw new InvalidOperationException("El importe bancario cambió. Actualizá la revisión antes de asignar el cliente.");
        var vigente=actuales.Clientes.SingleOrDefault(g=>g.Cliente==grupo.Cliente&&g.Unidad==grupo.Unidad)
            ??throw new InvalidOperationException("El cliente ya no tiene pendientes autorizados en esta unidad. Actualizá la búsqueda.");
        await Servicio.AsignarClienteAsync(row.Id,grupo.Cliente,false,null,ct);
        var prep=await Cobranzas.PrepararConciliacionAsync(row.Id,ct);
        var ids=vigente.Coincidentes.Count>0?vigente.Coincidentes:vigente.Documentos.Take(1).Select(d=>d.Id).ToArray();
        var elegidos=prep.Documentos.Where(d=>d.Cliente==grupo.Cliente&&d.Unidad==grupo.Unidad&&ids.Contains(d.Id)).ToList();
        prep.AplicacionesSugeridas=elegidos.Select(d=>new CobranzaAplicacion(d.Id,d.Saldo)).ToList();
        var primero=elegidos.FirstOrDefault();
        _comprobantePreferido=primero is null?null:$"{primero.Tc}|{primero.Comprobante}";
        _buscarPagadorMovimiento=null;_seleccion=null;_detalle=[];_errorCobranza=null;_feedback=null;_cobranza=prep;
    });
}

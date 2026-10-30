using AlfaCore.Models;

namespace AlfaCore.Components.Pages;

public partial class ConciliacionTarjetas
{
    private readonly HashSet<int> _iaElegidas=[];
    private bool _confirmarIaVarias,_aceptarDiferenciasIa;
    private string _motivoIaVarias="";
    private IReadOnlyList<TarjetasIaPropuesta> PropuestasIaElegidas=>_ia?.Propuestas.Where((_,i)=>_iaElegidas.Contains(i)).ToList()??[];
    private bool HayDiferenciasIa=>PropuestasIaElegidas.Any(p=>p.Diferencia!=0);
    private bool PuedeConfirmarVariasIa=>PropuestasIaElegidas.Count>0&&(!HayDiferenciasIa||(_aceptarDiferenciasIa&&!string.IsNullOrWhiteSpace(_motivoIaVarias)));
    private void MarcarIa(int indice,bool? valor){if(_busy||_ia is null||indice<0||indice>=_ia.Propuestas.Count)return;if(valor==true)_iaElegidas.Add(indice);else _iaElegidas.Remove(indice);}
    private void SeleccionarIa(bool soloExactas)
    {
        if(_busy||_ia is null)return;_iaElegidas.Clear();
        for(var i=0;i<_ia.Propuestas.Count;i++)if(!soloExactas||_ia.Propuestas[i].Diferencia==0)_iaElegidas.Add(i);
    }
    private void LimpiarIa(){_iaElegidas.Clear();_confirmarIaVarias=false;_aceptarDiferenciasIa=false;_motivoIaVarias="";}
    private void PedirConfirmarVariasIa()
    {
        if(_busy||PropuestasIaElegidas.Count==0)return;
        _feedback=null;_aceptarDiferenciasIa=false;_motivoIaVarias="";_mostrarIa=false;_confirmarIaVarias=true;
    }
    private void VolverAPropuestasIa(){if(_busy)return;_confirmarIaVarias=false;_mostrarIa=true;}
    private Task GuardarVariasIa()=>Operar(async ct=>
    {
        if(!PuedeConfirmarVariasIa||_aplicado is null)return;
        var seleccion=PropuestasIaElegidas.Select(p=>
        {
            var evidencia=p.Confianza+": "+p.Motivo;if(evidencia.Length>500)evidencia=evidencia[..500];
            return new TarjetasIaSeleccion(p.Detalles.Select(d=>d.Id).ToList(),p.Movimientos.Select(m=>new TarjetasSeleccion(m.Clave,m.Huella)).ToList(),evidencia);
        }).ToList();
        var ids=await Servicio.ConfirmarVariasAsync(new(_aplicado,seleccion,_aceptarDiferenciasIa,_motivoIaVarias),ct);
        _confirmarIaVarias=false;_mostrarIa=false;
        await CompararCore(ct);_importaciones=await Servicio.ImportacionesAsync(ct);
        _feedback=AppUiMessage.Success("Propuestas confirmadas",$"Se guardaron {ids.Count} conciliaciones independientes con su evidencia y motivo. Los importes originales se conservaron.");
    },AppUiMessage.InProgress("Confirmando propuestas…","Verificando todas las propuestas seleccionadas antes de guardar sus conciliaciones. Esperá hasta que termine."));
}

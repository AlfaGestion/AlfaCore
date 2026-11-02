using System.Threading.Channels;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    private static TarjetasBusquedaAgrupada BuscarSegunModo(TarjetasComparacion datos,TarjetasCriteriosAnalisis c,CancellationToken ct,Action<string,int>? progreso=null)
    {
        if(!c.Automatico)return TarjetasAgrupaciones.Buscar(datos,c,ct,progreso);
        var auto=TarjetasBusquedaAutomatica.Buscar(datos,c,ct,progreso);
        return new(auto.Propuestas,auto.Intentos,auto.AlcancesOmitidos>0,auto.Diagnosticos.Count(d=>d.Tipo=="Ambiguo"),0){Diagnosticos=auto.Diagnosticos};
    }
    private static async Task<TarjetasBusquedaAgrupada> BuscarConProgreso(TarjetasComparacion datos,TarjetasCriteriosAnalisis criterios,
        Func<TarjetasProgresoAnalisis,Task>? avance,CancellationToken ct)
    {
        if(avance is null)return await Task.Run(()=>BuscarSegunModo(datos,criterios,ct),ct);
        // Coalescer avisos rápidos sin bloquear el motor ni acumular eventos de UI después de terminar.
        var canal=Channel.CreateBounded<TarjetasProgresoAnalisis>(new BoundedChannelOptions(1)
        {SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.DropOldest});
        using var cancelacion=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var trabajo=Task.Run(()=>
        {
            try{return BuscarSegunModo(datos,criterios,cancelacion.Token,
                (actividad,intentos)=>canal.Writer.TryWrite(new(2,actividad,intentos)));}
            finally{canal.Writer.TryComplete();}
        },CancellationToken.None);
        try
        {
            await foreach(var actual in canal.Reader.ReadAllAsync(ct))await avance(actual);
            return await trabajo;
        }
        catch
        {
            cancelacion.Cancel();try{await trabajo;}catch{/* Preservar el error/cancelación original. */}
            throw;
        }
    }
}

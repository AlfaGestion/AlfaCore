using System.Diagnostics;
using AlfaCore.Models;

namespace AlfaCore.Components.Pages;

public partial class ConciliacionTarjetas
{
    private TarjetasProgresoAnalisis? _avanceAnalisis;
    private readonly Stopwatch _relojAnalisis=new();
    private string TiempoAnalisis=>$"{(int)_relojAnalisis.Elapsed.TotalMinutes:00}:{_relojAnalisis.Elapsed.Seconds:00}";
    private static readonly string[] EtapasAnalisis=["Movimientos","Agrupaciones","IA","Resultados"];

    private Task MostrarAvanceAnalisis(TarjetasProgresoAnalisis avance)=>InvokeAsync(()=>
    {
        if(_disposed||_lifetime.IsCancellationRequested)return;
        _avanceAnalisis=avance;
        var detalle=avance.Actividad;
        if(avance.Intentos>0)detalle+=$" · {avance.Intentos:N0} intentos utilizados.";
        if(avance.TotalLotes>0)detalle+=$" Lote {avance.LoteActual} de {avance.TotalLotes}; {avance.LotesCompletados} completados; quedan {avance.LotesPendientes} por finalizar.";
        _progreso=AppUiMessage.InProgress(avance.Titulo,detalle);StateHasChanged();
    });

    private async Task ConRelojAnalisis(Func<Task> trabajo,CancellationToken ct)
    {
        _relojAnalisis.Restart();
        using var detener=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reloj=ActualizarRelojAnalisis(detener.Token);
        try
        {
            await MostrarAvanceAnalisis(new(1,"Consultando movimientos y permisos de la base activa…"));
            await trabajo();
        }
        finally
        {
            detener.Cancel();try{await reloj;}catch(OperationCanceledException){}
            _relojAnalisis.Stop();_avanceAnalisis=null;
        }
    }

    private async Task ActualizarRelojAnalisis(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
        while(await timer.WaitForNextTickAsync(ct))
            await InvokeAsync(()=>{if(!_disposed&&!ct.IsCancellationRequested)StateHasChanged();});
    }
}

using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;
using Microsoft.JSInterop;

namespace AlfaCore.Components.Pages;

public partial class ConciliacionTarjetas
{
    private bool _mostrarExportar;
    private string _alcanceExportar="Actual";
    private static readonly IReadOnlyList<AlfaSelectOption> AlcancesExcel=
        [new("Actual","Comparación actual"),new("Pendientes","Solo pendientes"),new("Conciliados","Solo conciliados")];
    private Task PedirExportar()
    {
        if(_busy||_aplicado is null||_comparacion is null)return Task.CompletedTask;
        _alcanceExportar="Actual";_feedback=null;_mostrarExportar=true;return InvokeAsync(StateHasChanged);
    }
    private void CerrarExportar(){if(!_busy)_mostrarExportar=false;}
    private Task ExportarExcel()=>Operar(async ct=>
    {
        if(_aplicado is null||_comparacion is null)return;
        if(!Enum.TryParse<TarjetasExportacionAlcance>(_alcanceExportar,out var alcance))throw new InvalidOperationException("Elegí el alcance de la exportación.");
        var archivo=await Servicio.ExportarExcelAsync(new(_aplicado,alcance,_fechaResumenLocal,_fechaAlfaLocal,_sucursalAlfaLocal,
            _soloSeleccionados,_detalles.ToList(),_movimientos.ToList()),ct);
        ct.ThrowIfCancellationRequested();
        await Js.InvokeVoidAsync("alfaCoreInterfacesUpload.downloadBlob",ct,Convert.ToBase64String(archivo.Contenido),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",archivo.Nombre);
        _mostrarExportar=false;
        _feedback=AppUiMessage.Success("Excel descargado","Se exportaron Resumen, Liquidacion, Alfa y Conciliaciones, con todas las páginas del alcance elegido.");
    },AppUiMessage.InProgress("Preparando Excel…","Consultando los datos autorizados y generando las cuatro hojas. Esperá a que termine la descarga."),"exportar");
}

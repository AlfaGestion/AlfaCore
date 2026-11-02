using AlfaCore.Models;

namespace AlfaCore.Components.Pages;

public partial class ConciliacionTarjetas
{
    private TarjetasAnalisisReferencia? _analisisGuardado;
    private Task RecuperarAnalisis()=>Operar(RecuperarAnalisisCore,AppUiMessage.InProgress("Recuperando análisis…","Verificando las propuestas guardadas contra los movimientos y permisos actuales. No se vuelve a ejecutar la IA."));
    private async Task RecuperarAnalisisCore(CancellationToken ct)
    {
        if(_comparacion is null||_analisisGuardado is null)return;
        var recuperado=await Servicio.RecuperarAnalisisAsync(_comparacion.Importacion.Id,_analisisGuardado.Id,ct);
        _analisisGuardado=recuperado.Referencia;_comparacion=recuperado.Comparacion;_aplicado=recuperado.Filtro;
        _filtro.FechaDesde=_aplicado.Desde;_filtro.FechaHasta=_aplicado.Hasta;
        _filtro.Sucursal=_aplicado.Sucursal;_filtro.Buscar=_aplicado.Buscar;_filtro.Estado="Pendientes";
        _criteriosElegidos=recuperado.Criterios;CargarFormularioCriterios(recuperado.Criterios);
        LimpiarFiltrosLocales();LimpiarSeleccion();LimpiarIa();_paginaResumen=_paginaAlfa=1;
        _ia=recuperado.Resultado;_mostrarIa=true;
        _feedback=AppUiMessage.Info("Análisis recuperado",
            $"{_ia.Propuestas.Count} propuestas disponibles. Se omitieron {recuperado.Omitidas} propuestas porque sus movimientos cambiaron, ya se conciliaron o no están disponibles con tus permisos actuales. No se volvió a consultar la IA; los conteos del análisis corresponden a la ejecución original.");
    }
}

using AlfaCore.Models;
using System.Globalization;

namespace AlfaCore.Components.Pages;

public partial class ConciliacionTarjetas
{
    private bool _mostrarCriteriosIa,_subconjuntosIa=true,_consultarModeloIa=true,_automaticoIa=true;
    private TarjetasCriteriosAnalisis? _criteriosElegidos;
    private string _diasGrupoIa="3",_intentosAutoIa="1000000";
    private string _diasIa="3",_filasIa="2000",_grupoIa="200",_lotesIa="4",_intentosIa="100000";
    private Task AsistirIa()=>Operar(async ct=>
    {
        if(_comparacion is null)return;
        CargarFormularioCriterios(_criteriosElegidos??await Servicio.CriteriosIaAsync(ct));_mostrarCriteriosIa=true;
    },AppUiMessage.InProgress("Preparando criterios…","Leyendo los límites de búsqueda de esta base."),"criterios");
    private void CargarFormularioCriterios(TarjetasCriteriosAnalisis c)
    {
        _automaticoIa=c.Automatico;_diasGrupoIa=c.MaxDiasGrupo.ToString(CultureInfo.InvariantCulture);_intentosAutoIa=c.MaxIntentosAutomaticos.ToString(CultureInfo.InvariantCulture);
        _diasIa=c.Dias.ToString(CultureInfo.InvariantCulture);_filasIa=c.MaxFilasIa.ToString(CultureInfo.InvariantCulture);
        _grupoIa=c.MaxMovimientosGrupo.ToString(CultureInfo.InvariantCulture);_lotesIa=c.MaxLotesCombinados.ToString(CultureInfo.InvariantCulture);
        _intentosIa=c.MaxIntentos.ToString(CultureInfo.InvariantCulture);_subconjuntosIa=c.BuscarSubconjuntos;_consultarModeloIa=c.ConsultarIa;
    }
    private static int EnteroFormulario(string valor)=>int.TryParse(valor,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:
        throw new InvalidOperationException("Completá los límites con números enteros válidos.");
    private TarjetasCriteriosAnalisis LeerFormularioCriterios()
    {
        var c=new TarjetasCriteriosAnalisis{Automatico=_automaticoIa,MaxDiasGrupo=EnteroFormulario(_diasGrupoIa),MaxIntentosAutomaticos=EnteroFormulario(_intentosAutoIa),Dias=EnteroFormulario(_diasIa),MaxFilasIa=EnteroFormulario(_filasIa),
            MaxMovimientosGrupo=EnteroFormulario(_grupoIa),MaxLotesCombinados=EnteroFormulario(_lotesIa),MaxIntentos=EnteroFormulario(_intentosIa),
            BuscarSubconjuntos=_subconjuntosIa,ConsultarIa=_consultarModeloIa};c.Validar();return c;
    }
    private void CerrarCriteriosIa(){if(!_busy)_mostrarCriteriosIa=false;}
    private Task ValoresBaseIa()=>Operar(async ct=>{var c=await Servicio.CriteriosIaAsync(ct);CargarFormularioCriterios(c);_criteriosElegidos=null;});
    private Task PeriodoPdfIa()=>Operar(async ct=>
    {
        if(_comparacion is null)return;var c=LeerFormularioCriterios();
        _filtro.FechaDesde=_comparacion.Importacion.Desde.AddDays(-c.Dias);_filtro.FechaHasta=_comparacion.Importacion.Hasta.AddDays(c.Dias);
        await CompararCore(ct);
    },AppUiMessage.InProgress("Actualizando período…","Incluyendo las fechas de operación del margen elegido."));
}

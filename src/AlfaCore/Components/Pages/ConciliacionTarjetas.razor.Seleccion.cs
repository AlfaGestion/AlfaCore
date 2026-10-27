using AlfaCore.Models;
using AlfaCore.Components.Shared.AlfaDesign;

namespace AlfaCore.Components.Pages;

public partial class ConciliacionTarjetas
{
    private bool _soloSeleccionados;
    private int _tamanoPagina=50;
    private static readonly IReadOnlyList<AlfaSelectOption> TamanosPagina=[new("50","50"),new("100","100")];
    private static bool Seleccionable(TarjetasMovimiento m)=>m.Vinculo is null&&!m.LegacyLiquidado&&!m.Alterado;
    private IEnumerable<TarjetasDetalle> DetallesVisibles=>DetallesOrdenados.Where(d=>!_soloSeleccionados||_detalles.Contains(d.Id));
    private IEnumerable<TarjetasMovimiento> MovimientosVisibles=>MovimientosOrdenados.Where(m=>!_soloSeleccionados||_movimientos.Contains(m.Clave));
    private IEnumerable<TarjetasDetalle> PaginaResumen=>DetallesVisibles.Skip((_paginaResumen-1)*_tamanoPagina).Take(_tamanoPagina);
    private IEnumerable<TarjetasMovimiento> PaginaAlfa=>MovimientosVisibles.Skip((_paginaAlfa-1)*_tamanoPagina).Take(_tamanoPagina);
    private int PaginasResumen=>Math.Max(1,(DetallesVisibles.Count()+_tamanoPagina-1)/_tamanoPagina);
    private int PaginasAlfa=>Math.Max(1,(MovimientosVisibles.Count()+_tamanoPagina-1)/_tamanoPagina);
    private bool DentroTolerancia=>_comparacion is {} c&&Math.Abs(TotalResumen-TotalAlfa)<=c.Tolerancia;
    private bool PuedeConciliar=>_comparacion?.Importacion.DetalleCompleto==true&&_detalles.Count>0&&_movimientos.Count>0&&DentroTolerancia;
    private bool? EstadoPaginaResumen=>EstadoSeleccion(PaginaResumen.Where(d=>d.Vinculo is null).Select(d=>_detalles.Contains(d.Id)));
    private bool? EstadoPaginaAlfa=>EstadoSeleccion(PaginaAlfa.Where(Seleccionable).Select(m=>_movimientos.Contains(m.Clave)));
    private static bool? EstadoSeleccion(IEnumerable<bool> seleccion)
    {
        var valores=seleccion.ToList();return valores.Count==0||valores.All(x=>!x)?false:valores.All(x=>x)?true:null;
    }
    private void AjustarPaginas(){_paginaResumen=Math.Min(_paginaResumen,PaginasResumen);_paginaAlfa=Math.Min(_paginaAlfa,PaginasAlfa);}
    private void VerSoloSeleccion(bool? valor){if(_busy)return;_soloSeleccionados=valor==true;_paginaResumen=_paginaAlfa=1;}
    private void CambiarTamano(string? valor){if(_busy)return;_tamanoPagina=valor=="100"?100:50;_paginaResumen=_paginaAlfa=1;}
    private void LimpiarLado(bool resumen)
    {
        if(_busy)return;
        if(resumen)_detalles.Clear();else _movimientos.Clear();
        AjustarPaginas();Header();
    }
    private void SeleccionarResumen(bool pagina,bool invertir)
    {
        if(_busy||_comparacion is null)return;
        var candidatos=(pagina?PaginaResumen:_comparacion.Detalles).Where(d=>d.Vinculo is null).Select(d=>d.Id).ToList();
        CambiarSeleccion(_detalles,candidatos,invertir);AjustarPaginas();Header();
    }
    private void SeleccionarAlfa(bool pagina,bool invertir)
    {
        if(_busy||_comparacion is null)return;
        var candidatos=(pagina?PaginaAlfa:_comparacion.Movimientos).Where(Seleccionable).Select(m=>m.Clave).ToList();
        CambiarSeleccion(_movimientos,candidatos,invertir);AjustarPaginas();Header();
    }
    private void CambiarSeleccion<T>(HashSet<T> seleccion,IReadOnlyList<T> candidatos,bool invertir)
    {
        var nuevos=new HashSet<T>(seleccion);
        foreach(var id in candidatos){if(!invertir||!nuevos.Remove(id))nuevos.Add(id);}
        if(nuevos.Count>2000){_feedback=AppUiMessage.Info("Selección demasiado grande","Podés seleccionar hasta 2.000 registros por lado. Usá la casilla del encabezado para elegir por página. Se conservó la selección anterior.");return;}
        seleccion.Clear();seleccion.UnionWith(nuevos);
    }
    private void CambiarPaginaResumen(bool seleccionar)
    {
        if(_busy)return;
        if(seleccionar)SeleccionarResumen(true,false);
        else {foreach(var d in PaginaResumen.ToList())_detalles.Remove(d.Id);AjustarPaginas();Header();}
    }
    private void CambiarPaginaAlfa(bool seleccionar)
    {
        if(_busy)return;
        if(seleccionar)SeleccionarAlfa(true,false);
        else {foreach(var m in PaginaAlfa.ToList())_movimientos.Remove(m.Clave);AjustarPaginas();Header();}
    }
}

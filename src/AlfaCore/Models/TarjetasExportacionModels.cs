namespace AlfaCore.Models;

public enum TarjetasExportacionAlcance { Actual, Pendientes, Conciliados }
public sealed record TarjetasExportar(TarjetasFiltro Filtro,TarjetasExportacionAlcance Alcance=TarjetasExportacionAlcance.Actual,
    string FechaLiquidacion="",string FechaAlfa="",string SucursalAlfa="",bool SoloSeleccionados=false,
    IReadOnlyList<long>? DetallesSeleccionados=null,IReadOnlyList<string>? MovimientosSeleccionados=null);

public sealed class TarjetasVinculoExportacion
{
    public long Id {get;set;}
    public long ImportacionId {get;set;}
    public string Usuario {get;set;}="";
    public DateTime FechaUtc {get;set;}
    public string Motivo {get;set;}="";
    public decimal ImporteResumen {get;set;}
    public decimal ImporteAlfa {get;set;}
    public int CantidadDetalles {get;set;}
    public int CantidadMovimientos {get;set;}
}
public sealed record TarjetasDatosExportacion(TarjetasComparacion Comparacion,TarjetasExportar Solicitud,
    IReadOnlyList<TarjetasVinculoExportacion> Vinculos,string Base,string Usuario,DateTime GeneradoUtc);

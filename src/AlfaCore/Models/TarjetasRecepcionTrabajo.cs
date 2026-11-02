namespace AlfaCore.Models;

public sealed class TarjetasRecepcionTrabajo
{
    public long Id {get;set;}
    public long DocumentoId {get;set;}
    public string Usuario {get;set;}="";
    public string Sistema {get;set;}="";
    public string Nombre {get;set;}="";
    public byte[] Original {get;set;}=[];
    public string Hash {get;set;}="";
    public string Cuenta {get;set;}="";
    public string Sucursal {get;set;}="";
    public string Estado {get;set;}="";
    public string Mensaje {get;set;}="";
    public string Candidatas {get;set;}="";
    public int Intentos {get;set;}
    public long? ImportacionId {get;set;}
    public long? AnalisisId {get;set;}
    public Guid? Lease {get;set;}
    public DateTime ActualizadoUtc {get;set;}
    public bool EnProceso=>Estado is "LEYENDO" or "ANALIZANDO";
}

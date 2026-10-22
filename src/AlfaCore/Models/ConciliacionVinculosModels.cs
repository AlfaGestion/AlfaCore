namespace AlfaCore.Models;

public sealed record ConciliacionCompararFiltro(string Cuenta,DateTime Desde,DateTime Hasta,
    string Estado="Pendientes",string Buscar="",int PaginaBanco=1,int PaginaAlfa=1,string Sentido="Todos",decimal? ImporteDesde=null,decimal? ImporteHasta=null,
    string OrdenBanco="Fecha",bool DescBanco=true,string OrdenAlfa="Fecha",bool DescAlfa=true);
public sealed class ConciliacionFilaBanco
{
    public long Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Detalle { get; set; }="";
    public string Referencia { get; set; }="";
    public decimal Importe { get; set; }
    public string Huella { get; set; }="";
    public long? ConciliacionId { get; set; }
    public string Registracion { get; set; }="";
    public bool Alterado { get; set; }
}
public sealed class ConciliacionFilaAlfa
{
    public string Periodo { get; set; }="";
    public byte Mes { get; set; }
    public int Asiento { get; set; }
    public string TipoRegistro { get; set; }="";
    public int Secuencia { get; set; }
    public DateTime Fecha { get; set; }
    public string Detalle { get; set; }="";
    public string Comprobante { get; set; }="";
    public string Referencia { get; set; }="";
    public string Unidad { get; set; }="";
    public string Moneda { get; set; }="";
    public decimal Importe { get; set; }
    public string Huella { get; set; }="";
    public long? ConciliacionId { get; set; }
    public bool Legacy { get; set; }
    public bool Alterado { get; set; }
    public long MovimientoId { get; set; }
    public int Candidatos { get; set; }
    public ConciliacionAsientoClave Clave => new(Periodo,Mes,Asiento,TipoRegistro,Secuencia);
}
public sealed class ConciliacionCompararResumen
{
    public int Banco { get; set; }
    public int Alfa { get; set; }
    public decimal TotalBanco { get; set; }
    public decimal TotalAlfa { get; set; }
    public int Dias { get; set; }
}
public sealed record ConciliacionPropuesta(ConciliacionFilaBanco Banco,ConciliacionFilaAlfa Alfa);
public sealed record ConciliacionSugerencia(long MovimientoId,ConciliacionFilaAlfa Asiento,int Candidatos);
public sealed record ConciliacionComparacion(ConciliacionCompararResumen Resumen,
    IReadOnlyList<ConciliacionFilaBanco> Banco,IReadOnlyList<ConciliacionFilaAlfa> Alfa,
    IReadOnlyList<ConciliacionSugerencia> Sugerencias)
{
    public IReadOnlyList<ConciliacionPropuesta> Propuestas { get; init; }=[];
}
public sealed record ConciliacionBancoSeleccion(long Id,string Huella);
public sealed record ConciliacionAlfaSeleccion(ConciliacionAsientoClave Clave,string Huella);
public sealed record ConciliacionVincularSolicitud(Guid Operacion,ConciliacionCompararFiltro Filtro,
    IReadOnlyList<ConciliacionBancoSeleccion> Banco,IReadOnlyList<ConciliacionAlfaSeleccion> Alfa);
public sealed class ConciliacionVinculoCabecera
{
    public long Id { get; set; }
    public string Cuenta { get; set; }="";
    public decimal Importe { get; set; }
    public bool Activa { get; set; }
    public string Usuario { get; set; }="";
    public DateTime FechaUtc { get; set; }
    public string? Motivo { get; set; }
    public string? UsuarioAnulacion { get; set; }
    public DateTime? FechaAnulacionUtc { get; set; }
}
public sealed record ConciliacionVinculoDetalle(ConciliacionVinculoCabecera Cabecera,
    IReadOnlyList<ConciliacionFilaBanco> Banco,IReadOnlyList<ConciliacionFilaAlfa> Alfa);

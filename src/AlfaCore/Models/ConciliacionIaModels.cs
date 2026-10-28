using System.Text.Json.Serialization;

namespace AlfaCore.Models;

public sealed record ConciliacionIaPropuesta(Guid Id,string Origen,string Confianza,string Motivo,
    IReadOnlyList<ConciliacionFilaBanco> Banco,IReadOnlyList<ConciliacionFilaAlfa> Alfa,
    ConciliacionVincularSolicitud Solicitud)
{
    public decimal Diferencia=>Banco.Sum(b=>b.Importe)-Alfa.Sum(a=>a.Importe);
}
public sealed record ConciliacionIaOrientacion(ConciliacionFilaBanco Banco,string Categoria,string Cuenta,string Motivo);
public sealed record ConciliacionIaResultado(Guid Id,string Modelo,int BancosAnalizados,int AsientosAnalizados,
    int Descartadas,IReadOnlyList<ConciliacionIaPropuesta> Propuestas,IReadOnlyList<ConciliacionIaOrientacion> Orientaciones)
{
    public ConciliacionCriterios Criterios { get; init; }=new();
}
public sealed record ConciliacionIaContexto(ConciliacionCompararFiltro Filtro,ConciliacionCriterios Criterios,
    IReadOnlyList<ConciliacionFilaBanco> Banco,IReadOnlyList<ConciliacionFilaAlfa> Alfa,
    IReadOnlyList<ConciliacionAsientoCuenta> Cuentas)
{
    public bool EsTarjetas { get; init; }
}
public sealed record ConciliacionIaRespuesta(string Modelo,string Json);

// Los índices de la respuesta se resuelven exclusivamente contra el contexto autorizado.
public sealed class ConciliacionIaSalida
{
    [JsonRequired] public List<ConciliacionIaGrupo> Grupos { get; set; }=[];
    [JsonRequired] public List<ConciliacionIaConsejo> Orientaciones { get; set; }=[];
}
public sealed class ConciliacionIaGrupo
{
    public int[] Banco { get; set; }=[];
    public int[] Alfa { get; set; }=[];
    public string Confianza { get; set; }="";
    public string Motivo { get; set; }="";
}
public sealed class ConciliacionIaConsejo
{
    public int Banco { get; set; }
    public string Categoria { get; set; }="";
    public string Cuenta { get; set; }="";
    public string Motivo { get; set; }="";
}

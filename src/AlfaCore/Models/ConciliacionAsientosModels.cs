namespace AlfaCore.Models;

public sealed class ConciliacionAsientoRegla
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string Nombre { get; set; }="";
    public string Categoria { get; set; }="Gasto";
    public string CuentaBanco { get; set; }="";
    public string Texto { get; set; }="";
    public string Contrapartida { get; set; }="";
    public string Detalle { get; set; }="";
}
public sealed class ConciliacionAsientoConfig
{
    public string TipoRegistro { get; set; }="";
    public List<ConciliacionAsientoRegla> Reglas { get; set; }=[];
}
public sealed record ConciliacionAsientoCuenta(string Codigo,string Descripcion);
public sealed record ConciliacionAsientoLinea(string Cuenta,decimal Importe);
public sealed record ConciliacionAsientoSolicitud(Guid Operacion,long MovimientoId,string Huella,DateTime Fecha,
    string Unidad,int Talonario,string Detalle,IReadOnlyList<ConciliacionAsientoLinea> Contrapartidas,Guid? ReglaId=null,bool ConfirmarNuevaRegistracion=false);
public sealed class ConciliacionAsientoResultado
{
    public Guid Operacion { get; set; }
    public string Comprobante { get; set; }="";
    public string Periodo { get; set; }="";
    public byte Mes { get; set; }
    public int NumeroAsiento { get; set; }
    public string TipoRegistro { get; set; }="";
    public long ConciliacionId { get; set; }
}
public sealed record ConciliacionAsientoCatalogo(ConciliacionAsientoConfig Config,
    IReadOnlyList<ConciliacionAsientoCuenta> Cuentas,IReadOnlyList<ConciliacionCuenta> Bancos,
    IReadOnlyList<ConciliacionAsientoCuenta> Unidades,IReadOnlyList<ConciliacionAsientoCuenta> Subdiarios,
    IReadOnlyList<CobranzaTalonario> Talonarios,string UnidadInicial)
{
    public IReadOnlyList<ConciliacionAsientoRegla> SugerenciasIniciales { get; init; }=[];
}
public sealed record ConciliacionAsientoPreparacion(ConciliacionFilaBanco Movimiento,string CuentaBanco,string Categoria,
    ConciliacionAsientoCatalogo Catalogo,Guid? ReglaSugerida,IReadOnlyList<ConciliacionFilaAlfa> PosiblesDuplicados);

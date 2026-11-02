namespace AlfaCore.Models;

public sealed class TarjetasLtConcepto
{
    public string Concepto {get;set;}="";
    public string Rol {get;set;}="";
    // Signo económico del documento: ventas +, descuentos -.
    public decimal Importe {get;set;}
}
public sealed class TarjetasLtLiquidacion
{
    public string Numero {get;set;}="";
    public DateTime Fecha {get;set;}
    public decimal Neto {get;set;}
    public bool EsResumenMensual {get;set;}
    public List<TarjetasLtConcepto> Conceptos {get;set;}=[];
}
public sealed class TarjetasLtCuenta
{
    public string Codigo {get;set;}="";
    public string Descripcion {get;set;}="";
    public string Medio {get;set;}="";
}
public sealed class TarjetasLtAsignacion
{
    public string Rol {get;set;}="";
    public string Cuenta {get;set;}="";
}
public sealed record TarjetasLtLinea(string Concepto,string Rol,string Cuenta,decimal Debe,decimal Haber);
public sealed record TarjetasLtBorrador(IReadOnlyList<TarjetasLtLinea> Lineas,IReadOnlyList<string> Avisos)
{
    public decimal Debe=>Lineas.Sum(l=>l.Debe);
    public decimal Haber=>Lineas.Sum(l=>l.Haber);
    public decimal Diferencia=>Debe-Haber;
}
public sealed record TarjetasLtVista(long Importacion,string Banco,string Comercio,string Tarjeta,string CuentaTarjeta,
    IReadOnlyList<TarjetasLtLiquidacion> Liquidaciones,IReadOnlyList<TarjetasLtCuenta> Cuentas,
    IReadOnlyList<TarjetasLtAsignacion> Asignaciones,IReadOnlyList<string> Avisos);

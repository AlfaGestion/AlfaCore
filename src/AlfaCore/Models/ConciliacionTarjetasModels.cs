namespace AlfaCore.Models;

public sealed class TarjetasLectura
{
    public string Banco {get;set;}="";
    public string Comercio {get;set;}="";
    public string Tarjeta {get;set;}="";
    public decimal Bruto {get;set;}
    public decimal Neto {get;set;}
    public decimal TotalDetalle {get;set;}
    public bool DetalleCompleto {get;set;}
    public List<TarjetasDetalle> Detalles {get;set;}=[];
    public List<string> Avisos {get;set;}=[];
    public string CuentaSugerida {get;set;}="";
    public string SucursalSugerida {get;set;}="";
    public TarjetasDeteccionCuenta? Deteccion {get;set;}
}
public sealed record TarjetasCuentaCandidata(string Cuenta,string Descripcion,int PorCupon,int PorFechaImporte,
    int LotesCompatibles,decimal ImporteCoincidente,string Sucursal,int MovimientosConsultados)
{
    public int Coincidencias=>PorCupon+PorFechaImporte;
}
public sealed record TarjetasDeteccionCuenta(DateTime Desde,DateTime Hasta,int Dias,int CuentasAnalizadas,
    string Mensaje,IReadOnlyList<TarjetasCuentaCandidata> Candidatas);
public sealed class TarjetasDetalle
{
    public long Id {get;set;}
    public DateTime Fecha {get;set;}
    public DateTime? FechaPago {get;set;}
    public string Liquidacion {get;set;}="";
    public string Lote {get;set;}="";
    public string Cupon {get;set;}="";
    public string Terminal {get;set;}="";
    public string Nivel {get;set;}="";
    public int Cantidad {get;set;}
    public decimal Importe {get;set;}
    public long? Vinculo {get;set;}
}
public sealed class TarjetasImportacion
{
    public long Id {get;set;}
    public string Nombre {get;set;}="";
    public string Banco {get;set;}="";
    public string Comercio {get;set;}="";
    public string Tarjeta {get;set;}="";
    public string Cuenta {get;set;}="";
    public string Sucursal {get;set;}="";
    public DateTime Desde {get;set;}
    public DateTime Hasta {get;set;}
    public decimal Bruto {get;set;}
    public decimal Neto {get;set;}
    public bool DetalleCompleto {get;set;}
    public int Pendientes {get;set;}
    public string Avisos {get;set;}="";
}
public sealed class TarjetasMovimiento
{
    public string Cuenta {get;set;}="";
    public string Clave {get;set;}="";
    public string ClaveAnterior {get;set;}="";
    public string Huella {get;set;}="";
    public DateTime Fecha {get;set;}
    public string Comprobante {get;set;}="";
    public string Sucursal {get;set;}="";
    public string Detalle {get;set;}="";
    public string Cupon {get;set;}="";
    public double Presentacion {get;set;}
    public decimal Importe {get;set;}
    public long? Vinculo {get;set;}
    public bool Alterado {get;set;}
    public bool LegacyLiquidado {get;set;}
}
public sealed record TarjetasComparacion(TarjetasImportacion Importacion,IReadOnlyList<TarjetasDetalle> Detalles,IReadOnlyList<TarjetasMovimiento> Movimientos,decimal Tolerancia);
public sealed record TarjetasFiltro(long Importacion,DateTime Desde,DateTime Hasta,string Sucursal="",string Estado="Pendientes",string Buscar="");
public sealed record TarjetasSeleccion(string Clave,string Huella);
public sealed class TarjetasBusqueda : GestionDateFilterBase
{
    public string Sucursal {get;set;}="";
    public string Estado {get;set;}="Pendientes";
    public string Buscar {get;set;}="";
}
public sealed record TarjetasConfirmar(TarjetasFiltro Filtro,IReadOnlyList<long> Detalles,IReadOnlyList<TarjetasSeleccion> Movimientos,string Motivo="");
public sealed record TarjetasIaSeleccion(IReadOnlyList<long> Detalles,IReadOnlyList<TarjetasSeleccion> Movimientos,string Evidencia);
public sealed record TarjetasConfirmarVarias(TarjetasFiltro Filtro,IReadOnlyList<TarjetasIaSeleccion> Propuestas,bool AceptarDiferencias,string Motivo);
public sealed record TarjetasIaPropuesta(string Confianza,string Motivo,IReadOnlyList<TarjetasDetalle> Detalles,IReadOnlyList<TarjetasMovimiento> Movimientos)
{
    public decimal Diferencia=>Detalles.Sum(x=>x.Importe)-Movimientos.Sum(x=>x.Importe);
}
public sealed record TarjetasIaResultado(string Modelo,int DetallesAnalizados,int MovimientosAnalizados,int Descartadas,int Dias,IReadOnlyList<TarjetasIaPropuesta> Propuestas)
{
    public int LotesAnalizados {get;init;}=1;
    public int DetallesSinCandidatosPorFecha {get;init;}
    public int MovimientosSinCandidatosPorFecha {get;init;}
}

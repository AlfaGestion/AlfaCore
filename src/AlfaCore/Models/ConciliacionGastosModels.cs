namespace AlfaCore.Models;

/// <summary>Cuentas y reglas del asiento mensual de gastos e impuestos bancarios. Se guarda en TA_CONFIGURACION (CONCILIACION-GASTOS-BANCARIOS), separada de Alfa Gestión.</summary>
public sealed class ConciliacionGastosConfig
{
    /// <summary>Cuenta contable por concepto (claves de <c>ConciliacionGastosBancarios.Claves</c>).</summary>
    public Dictionary<string,string> Cuentas { get; set; }=[];
    public decimal AlicuotaIva { get; set; }=21m;
    public string Detalle { get; set; }="GASTOS E IMPUESTOS BANCARIOS";
    /// <summary>Textos que identifican cada concepto en la descripción del extracto. Vacío = valores iniciales del sistema.</summary>
    public Dictionary<string,List<string>> Patrones { get; set; }=[];
    public string Cuenta(string clave)=>Cuentas.TryGetValue(clave,out var c) ? c.Trim() : "";
}
public sealed record ConciliacionGastosConcepto(string Clave,string Titulo,int Cantidad,decimal Importe);
public sealed record ConciliacionGastosMovimiento(long Id,string Huella,string Clave,decimal Importe);
public sealed record ConciliacionGastosCalculo(IReadOnlyList<ConciliacionGastosConcepto> Conceptos,decimal Total,
    IReadOnlyList<ConciliacionGastosMovimiento> Movimientos,IReadOnlyList<string> Errores,IReadOnlyList<string> Avisos,string Huella)
{
    public decimal Importe(string clave)=>Conceptos.FirstOrDefault(c=>c.Clave==clave)?.Importe ?? 0m;
}
public sealed record ConciliacionGastosPreparacion(long ImportacionId,string CuentaBanco,string CuentaBancoNombre,string CuitBanco,string CondIvaBanco,
    DateTime Desde,DateTime Hasta,DateTime FechaSugerida,ConciliacionGastosCalculo Calculo,ConciliacionGastosConfig Config,
    bool ConfigDesdeModelo,ConciliacionAsientoCatalogo Catalogo,string Tc="CC");
public sealed record ConciliacionGastosSolicitud(Guid Operacion,long ImportacionId,string Huella,DateTime Fecha,string Unidad,int Talonario,
    string Detalle,IReadOnlyDictionary<string,string> Cuentas);

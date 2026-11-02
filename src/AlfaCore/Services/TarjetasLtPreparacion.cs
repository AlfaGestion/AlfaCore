using System.Globalization;
using System.Text;
using AlfaCore.Models;

namespace AlfaCore.Services;

// Borrador puro: nunca reserva numeración, graba movimientos ni genera crédito fiscal.
internal static class TarjetasLtPreparacion
{
    internal static readonly string[] Roles=["Banco","Gastos","IvaCredito","PercepcionIva","Iibb","Ganancias"];
    internal static IReadOnlyList<TarjetasLtAsignacion> Sugerir(IReadOnlyList<TarjetasLtCuenta> cuentas)
    {
        var nombres=new Dictionary<string,string[]>{
            ["Gastos"]=["GASTOS TARJETAS DE CREDITO","GASTOS TARJETAS CREDITO"],
            ["IvaCredito"]=["IVA CREDITO FISCAL","I V A CREDITO FISCAL"],
            ["PercepcionIva"]=["RETENCIONES PERCEPCIONES DE IVA","RETENCION PERCEPCION DE IVA"],
            ["Iibb"]=["RETENCION PERCEPCION INGRESOS BRUTOS","RETENCIONES PERCEPCIONES INGRESOS BRUTOS"],
            ["Ganancias"]=["RETENCIONES GANANCIAS","RETENCION GANANCIAS"]};
        return Roles.Select(rol=>{
            var candidatas=cuentas.Where(c=>Elegible(c,rol)&&nombres.TryGetValue(rol,out var ns)&&ns.Contains(Normalizar(c.Descripcion))).ToList();
            return new TarjetasLtAsignacion{Rol=rol,Cuenta=candidatas.Count==1?candidatas[0].Codigo:""};
        }).ToList();
    }
    private static string Normalizar(string s)=>string.Join(' ',new string(s.Normalize(NormalizationForm.FormD)
        .Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)
        .Select(c=>char.IsLetterOrDigit(c)?char.ToUpperInvariant(c):' ').ToArray()).Split(' ',StringSplitOptions.RemoveEmptyEntries));
    internal static bool Elegible(TarjetasLtCuenta c,string rol)=>rol=="Banco"?c.Medio=="DB":c.Medio is "" or "RI" or "RB" or "RG" or "RO";
    internal static void Validar(IReadOnlyList<TarjetasLtAsignacion> reglas,IReadOnlyList<TarjetasLtCuenta> cuentas)
    {
        if(reglas.Count!=Roles.Length||reglas.Select(r=>r.Rol).Distinct().Count()!=Roles.Length||reglas.Any(r=>!Roles.Contains(r.Rol)||r.Cuenta.Length>30||
            (r.Cuenta.Trim().Length>0&&!cuentas.Any(c=>c.Codigo==r.Cuenta.Trim()&&Elegible(c,r.Rol)))))
            throw new InvalidOperationException("Revisá las cuentas de LT: deben estar activas, ser imputables, en pesos y corresponder al tipo de concepto.");
    }
    internal static TarjetasLtBorrador Preparar(TarjetasLtLiquidacion l,string tarjeta,IReadOnlyList<TarjetasLtAsignacion> reglas)
    {
        var avisos=new List<string>();var lineas=new List<TarjetasLtLinea>();
        string Cuenta(string rol)=>rol=="Tarjeta"?tarjeta:reglas.FirstOrDefault(r=>r.Rol==rol)?.Cuenta.Trim()??"";
        foreach(var c in l.Conceptos.Where(c=>c.Importe!=0))
            lineas.Add(new(c.Concepto,c.Rol,Cuenta(c.Rol),Math.Max(-c.Importe,0),Math.Max(c.Importe,0)));
        lineas.Add(new("Neto a acreditar","Banco",Cuenta("Banco"),Math.Max(l.Neto,0),Math.Max(-l.Neto,0)));
        if(l.EsResumenMensual)avisos.Add("Es un resumen fiscal mensual compartido por varias liquidaciones. No se distribuyeron gastos ni impuestos: este cuadro no representa una LT individual. Debe definirse cómo registrar el resumen sin duplicar los registros diarios.");
        if(l.Conceptos.Count==0||!l.Conceptos.Any(c=>c.Rol=="Tarjeta"))avisos.Add("Falta el bruto explícito de la liquidación.");
        if(l.Conceptos.Any(c=>c.Rol=="SinClasificar"))avisos.Add("Hay conceptos sin clasificar. Deben revisarse contra el PDF.");
        if(lineas.Any(x=>x.Cuenta.Length==0))avisos.Add("Hay conceptos sin cuenta. Elegí sus cuentas antes de la futura registración.");
        if(lineas.Sum(x=>x.Debe-x.Haber)!=0)avisos.Add("El desglose no balancea con el neto del documento. No se agregó ninguna cuenta ni importe de ajuste.");
        avisos.Add("Vista previa: no se comprobó duplicidad contra LT manuales ni se definieron numeración, unidad y datos fiscales. No se registran LT, asientos ni libro de IVA.");
        return new(lineas,avisos);
    }
}

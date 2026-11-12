using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Cálculo del asiento mensual de gastos e impuestos bancarios a partir de los movimientos importados del extracto.
/// Los totales coinciden con el resumen impositivo del banco (IVA, percepción, SIRCREB, impuesto a los débitos y créditos):
/// se suman los renglones del propio extracto, por lo que sirve para cualquier banco y formato de importación.
/// </summary>
internal static class ConciliacionGastosBancarios
{
    public const string Gastos="Gastos",IvaCredito="IvaCredito",IvaPercepcion="IvaPercepcion",Iibb="Iibb",Ganancias="Ganancias",Idc="Idc";
    /// <summary>Orden del asiento (Debe) y de la evaluación de patrones: lo más específico primero.</summary>
    public static readonly IReadOnlyList<string> Claves=[Gastos,IvaCredito,IvaPercepcion,Iibb,Ganancias,Idc];
    private static readonly string[] OrdenClasificacion=[IvaPercepcion,IvaCredito,Iibb,Ganancias,Idc,Gastos];
    public static readonly IReadOnlyDictionary<string,string> Titulos=new Dictionary<string,string>
    {
        [Gastos]="Comisiones y gastos bancarios",[IvaCredito]="IVA crédito fiscal",[IvaPercepcion]="IVA percepción",
        [Iibb]="Retención de Ingresos Brutos (SIRCREB)",[Ganancias]="Retención de Ganancias",[Idc]="Impuesto a los débitos y créditos"
    };
    /// <summary>Valores iniciales; un texto terminado en * coincide como prefijo de palabra.</summary>
    public static readonly IReadOnlyDictionary<string,string[]> PatronesIniciales=new Dictionary<string,string[]>
    {
        [IvaPercepcion]=["IVA PERCEPCION","PERCEPCION IVA","PERC IVA","PERCEP IVA","RG 2408"],
        [IvaCredito]=["IVA","I V A"],
        [Iibb]=["SIRCREB","RET IIBB","IIBB","ING BRUTOS","INGRESOS BRUTOS"],
        [Ganancias]=["RET GANANCIAS","RETENCION GANANCIAS","IMP GANANCIAS","RET GAN"],
        [Idc]=["IMP DB CR","DB CR BANCARIOS","25413","IMPUESTO AL CHEQUE","IMP DEBITOS Y CREDITOS","IMPUESTO DEBITOS Y CREDITOS","IMP CREDITOS Y DEBITOS","IMP CRED Y DEB"],
        [Gastos]=["COMIS*","MANTENIM*","EMISION ECHEQ","GASTO*"]
    };
    private static readonly HashSet<string> EstadosRegistrados=new(["Cobrado","Pagado","Registrado"],StringComparer.OrdinalIgnoreCase);

    public static string Normalizar(string texto)
    {
        var d=texto.Normalize(NormalizationForm.FormD);
        var sb=new StringBuilder(d.Length);
        var espacio=true;
        foreach(var ch in d)
        {
            if(CharUnicodeInfo.GetUnicodeCategory(ch)==UnicodeCategory.NonSpacingMark)continue;
            if(char.IsLetterOrDigit(ch)){sb.Append(char.ToUpperInvariant(ch));espacio=false;}
            else if(!espacio){sb.Append(' ');espacio=true;}
        }
        return sb.ToString().Trim();
    }

    private static bool Coincide(string textoNormalizado,string patron)
    {
        var prefijo=patron.EndsWith('*');
        var nucleo=Normalizar(patron.TrimEnd('*'));
        if(nucleo.Length==0)return false;
        return (" "+textoNormalizado+" ").Contains(" "+nucleo+(prefijo ? "" : " "),StringComparison.Ordinal);
    }

    /// <summary>Concepto al que pertenece la descripción, o null si no es un gasto/impuesto bancario (transferencias, cheques, tarjetas).</summary>
    public static string? Clasificar(string descripcion,ConciliacionGastosConfig config)
    {
        var texto=Normalizar(descripcion);
        foreach(var clave in OrdenClasificacion)
        {
            IReadOnlyList<string> patrones=config.Patrones.TryGetValue(clave,out var propios)&&propios.Count>0 ? propios : PatronesIniciales[clave];
            if(patrones.Any(p=>Coincide(texto,p)))return clave;
        }
        return null;
    }

    public static ConciliacionGastosCalculo Calcular(IEnumerable<ConciliacionFilaBanco> movimientos,ConciliacionGastosConfig config,decimal tolerancia)
    {
        var errores=new List<string>();var avisos=new List<string>();var incluidos=new List<ConciliacionGastosMovimiento>();
        int conciliados=0,devoluciones=0;decimal devuelto=0;
        foreach(var m in movimientos.OrderBy(m=>m.Id))
        {
            var clave=Clasificar(m.Detalle,config);
            if(clave is null)continue;
            if(m.ConciliacionId.HasValue||EstadosRegistrados.Contains(m.Registracion)){conciliados++;continue;}
            // Una devolución (crédito) descuenta del concepto: el importe neto es el que informa el resumen del banco.
            if(m.Importe>0){devoluciones++;devuelto+=m.Importe;}
            incluidos.Add(new(m.Id,m.Huella,clave,-m.Importe));
        }
        if(conciliados>0)avisos.Add($"{conciliados} movimientos de gastos ya estaban conciliados o registrados y no se incluyen.");
        if(devoluciones>0)avisos.Add($"{devoluciones} devoluciones por {Dinero(devuelto)} se descontaron de sus conceptos.");
        var conceptos=Claves.Select(c=>new ConciliacionGastosConcepto(c,Titulos[c],incluidos.Count(i=>i.Clave==c),incluidos.Where(i=>i.Clave==c).Sum(i=>i.Importe))).ToList();
        var total=conceptos.Sum(c=>c.Importe);
        var neto=conceptos.First(c=>c.Clave==Gastos).Importe;var iva=conceptos.First(c=>c.Clave==IvaCredito).Importe;
        if(total<=0)errores.Add("No hay gastos ni impuestos bancarios pendientes en este extracto.");
        else
        {
            var esperado=decimal.Round(neto*config.AlicuotaIva/100m,2,MidpointRounding.AwayFromZero);
            if(Math.Abs(iva-esperado)>tolerancia)
                errores.Add($"El IVA del extracto ({Dinero(iva)}) no corresponde al {config.AlicuotaIva:0.##}% de los gastos ({Dinero(neto)}; esperado {Dinero(esperado)}). Revisá qué movimientos se clasificaron como gasto.");
        }
        var huella=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",incluidos.Select(i=>$"{i.Id}:{i.Huella}:{i.Clave}"))))).ToLowerInvariant();
        return new(conceptos,total,incluidos,errores,avisos,huella);
    }

    /// <summary>Campos del Libro de IVA Compras que van en el renglón del banco, igual que el asiento de liquidación de Alfa Gestión.</summary>
    public sealed record LibroIva(decimal NetoGravado,decimal NetoNoGravado,decimal Alicuota,decimal Iva,decimal PercepcionIva,decimal PercepcionIibb,decimal PercepcionGanancias,decimal Total);

    public static LibroIva ArmarLibroIva(ConciliacionGastosCalculo c,ConciliacionGastosConfig config)=>
        new(c.Importe(Gastos),c.Importe(Idc),c.Importe(IvaCredito)>0 ? config.AlicuotaIva : 0m,c.Importe(IvaCredito),
            c.Importe(IvaPercepcion),c.Importe(Iibb),c.Importe(Ganancias),c.Total);

    /// <summary>Renglones del Debe en orden contable; los conceptos sin importe no generan renglón.</summary>
    public static IReadOnlyList<ConciliacionAsientoLinea> LineasDebe(ConciliacionGastosCalculo c,IReadOnlyDictionary<string,string> cuentas)
    {
        var lineas=new List<ConciliacionAsientoLinea>();
        foreach(var clave in Claves)
        {
            var importe=c.Importe(clave);
            if(importe<=0)continue;
            if(!cuentas.TryGetValue(clave,out var cuenta)||string.IsNullOrWhiteSpace(cuenta))
                throw new InvalidOperationException($"Falta la cuenta contable de «{Titulos[clave]}».");
            lineas.Add(new(cuenta.Trim(),importe));
        }
        return lineas;
    }

    public sealed record ModeloFila(string Cuenta,string Descripcion,string DebeHaber,decimal Alicuota);

    /// <summary>Propone la cuenta de cada concepto a partir de las filas de un asiento modelo, por su descripción. Solo se usa la primera vez.</summary>
    public static Dictionary<string,string> SugerirCuentas(IEnumerable<ModeloFila> filas)
    {
        var resultado=new Dictionary<string,string>();
        foreach(var f in filas.Where(f=>f.DebeHaber.Trim()=="D"))
        {
            var d=Normalizar(f.Descripcion);var cuenta=f.Cuenta.Trim();
            var iva=Palabra(d,"IVA")||Palabra(d,"I V A");
            string? clave=null;
            if(iva&&(d.Contains("RETENC")||d.Contains("PERCEP")))clave=IvaPercepcion;
            else if(iva)clave=IvaCredito;
            else if(d.Contains("INGRESOS BRUTOS")||d.Contains("ING BRUTOS")||Palabra(d,"IIBB"))clave=Iibb;
            else if(d.Contains("GANANCIA"))clave=Ganancias;
            else if(Palabra(d,"IDC")||d.Contains("DEB CRED")||d.Contains("25413")||(d.Contains("DEBITO")&&d.Contains("CREDITO")))clave=Idc;
            else if(d.Contains("GASTO")||d.Contains("COMISION")||f.Alicuota>0)clave=Gastos;
            if(clave is not null&&!resultado.ContainsKey(clave))resultado[clave]=cuenta;
        }
        return resultado;
    }

    private static bool Palabra(string normalizado,string palabra)=>(" "+normalizado+" ").Contains(" "+palabra+" ",StringComparison.Ordinal);

    private static string Dinero(decimal v)=>v.ToString("N2",CultureInfo.GetCultureInfo("es-AR"));
}

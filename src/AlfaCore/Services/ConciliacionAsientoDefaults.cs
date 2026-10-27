using System.Globalization;
using System.Text;
using AlfaCore.Models;

namespace AlfaCore.Services;

// Trabaja únicamente sobre las cuentas elegibles del catálogo de la base activa.
// Las propuestas son un borrador: no participan del matching hasta guardarlas.
internal static class ConciliacionAsientoDefaults
{
    internal static IReadOnlyList<ConciliacionAsientoRegla> Proponer(IReadOnlyList<ConciliacionAsientoCuenta> cuentas)
    {
        var reglas=new List<ConciliacionAsientoRegla>();
        string? Cuenta(params string[] nombres)
        {
            var opciones=cuentas.Where(c=>nombres.Contains(Normalizar(c.Descripcion)))
                .Select(c=>c.Codigo.Trim()).Distinct().ToList();
            return opciones.Count==1 ? opciones[0] : null;
        }
        void Agregar(string? cuenta,string nombre,string categoria,string texto="")
        {
            if(cuenta is null)return;
            reglas.Add(new(){Nombre=nombre,Categoria=categoria,Texto=texto,Contrapartida=cuenta});
        }

        var gastos=Cuenta("GASTOS BANCARIOS","COMISIONES BANCARIAS","GASTOS Y COMISIONES BANCARIAS","COMISIONES Y GASTOS BANCARIOS");
        Agregar(gastos,"Comisiones bancarias","Comision");
        Agregar(gastos,"Comisiones del banco (sin tilde)","Gasto","comision");
        Agregar(gastos,"Comisiones del banco","Gasto","comisión");
        Agregar(gastos,"Mantenimiento de cuenta bancaria","Gasto","mantenimiento de cuenta");

        var intereses=Cuenta("GASTOS FINANCIEROS INTERESES","INTERESES PAGADOS","INTERESES BANCARIOS PAGADOS");
        Agregar(intereses,"Intereses y descubierto bancario","Gasto","interes");
        Agregar(intereses,"Intereses bancarios (con tilde)","Gasto","interés");
        Agregar(Cuenta("INTERESES GANADOS","INTERESES BANCARIOS GANADOS"),"Intereses ganados","IngresoFinanciero");

        var iibb=Cuenta("RETENCION BANCARIA DE IIBB","RETENCIONES BANCARIAS DE IIBB","RETENCIONES BANCARIAS INGRESOS BRUTOS");
        foreach(var categoria in new[]{"Gasto","Impuesto"})
        {
            Agregar(iibb,$"SIRCREB ({categoria})",categoria,"sircreb");
            Agregar(iibb,$"Retención bancaria IIBB ({categoria})",categoria,"iibb");
        }
        // Son imputaciones contables de conceptos explícitos del extracto, no altas en el libro de IVA.
        var ivaBanco=Cuenta("I V A DEBITOS COMISIONES BANCOS","IVA DEBITOS COMISIONES BANCOS","IVA COMISIONES BANCARIAS","IVA CREDITO FISCAL BANCARIO");
        var ley=Cuenta("R LEY 25 413","IMPUESTO LEY 25 413","IMPUESTO LEY 25413","IMPUESTO A LOS DEBITOS Y CREDITOS BANCARIOS");
        foreach(var categoria in new[]{"Gasto","Impuesto"})
        {
            Agregar(ivaBanco,$"IVA bancario base ({categoria})",categoria,"I.V.A. BASE");
            Agregar(ivaBanco,$"IVA básico bancario ({categoria})",categoria,"DEBITO FISCAL IVA BASICO");
            Agregar(ley,$"Ley 25.413 ({categoria})",categoria,"25.413");
            Agregar(ley,$"Ley 25413 ({categoria})",categoria,"25413");
        }
        return reglas;
    }

    private static string Normalizar(string texto)
    {
        var resultado=new StringBuilder();
        foreach(var c in texto.Normalize(NormalizationForm.FormD))
        {
            if(CharUnicodeInfo.GetUnicodeCategory(c)==UnicodeCategory.NonSpacingMark)continue;
            resultado.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }
        return string.Join(' ',resultado.ToString().Split(' ',StringSplitOptions.RemoveEmptyEntries));
    }
}

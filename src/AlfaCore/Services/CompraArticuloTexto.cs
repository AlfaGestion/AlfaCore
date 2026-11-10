using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

/// <summary>
/// Normalización de textos para emparejar los renglones leídos por la IA con el maestro de artículos.
/// Criterios tomados de modBusquedaAutomaticaArticulos.bas del legacy (V_MV_CPRA): mismas stopwords, mismo largo mínimo de palabra
/// y mismo umbral de coincidencia; a diferencia del legacy, no distingue tildes ni la Ñ.
/// </summary>
internal static partial class CompraArticuloTexto
{
    private static readonly HashSet<string> Vacias=new(StringComparer.Ordinal)
    {
        "DE","DEL","LA","LAS","EL","LOS","Y","CON","SIN","POR","PARA","UN","UNA","UNO","ART","ARTICULO","PRODUCTO","ITEM","VARIOS","GENERICO","PROMO","PACK","UNID","UND"
    };
    private const string Separadores=".,;:-/\\()+%\t";

    internal static string SinAcentos(string? texto)
    {
        var d=(texto??"").Normalize(NormalizationForm.FormD);var sb=new StringBuilder(d.Length);
        foreach(var c in d)if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
    /// <summary>Mayúsculas, sin tildes, signos como separadores y espacios colapsados.</summary>
    internal static string Limpiar(string? texto)
    {
        var sb=new StringBuilder();
        foreach(var c in SinAcentos(texto).Trim().ToUpperInvariant())sb.Append(Separadores.Contains(c)?' ':c);
        return EspaciosMultiples().Replace(sb.ToString()," ").Trim();
    }
    /// <summary>Código comparable: mayúsculas y sin espacios, guiones, puntos, barras ni guion bajo (igual que el REPLACE del lado SQL).</summary>
    internal static string NormalizarCodigo(string? texto)
    {
        var sb=new StringBuilder();
        foreach(var c in (texto??"").Trim().ToUpperInvariant())if(c is not(' ' or '-' or '.' or '/' or '\\' or '_'))sb.Append(c);
        return sb.ToString();
    }
    internal static string QuitarCeros(string codigo){var s=codigo.TrimStart('0');return s.Length==0&&codigo.Length>0?"0":s;}
    internal static bool SoloDigitos(string? t)=>!string.IsNullOrEmpty(t)&&t.All(char.IsAsciiDigit);
    internal static bool PalabraValida(string p)=>p.Length>=3&&p.Any(c=>c is >='A' and <='Z')&&!Vacias.Contains(p);
    /// <summary>Palabras significativas de la descripción, sin repetir y en el orden en que aparecen.</summary>
    internal static IReadOnlyList<string> Palabras(string? descripcion)
        =>Limpiar(descripcion).Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(PalabraValida).Distinct(StringComparer.Ordinal).ToList();
    internal static string PalabraClave(string? descripcion)=>Palabras(descripcion).FirstOrDefault()??"";
    /// <summary>Mínimo de palabras que deben estar presentes: el 50 % redondeado hacia arriba, al menos una.</summary>
    internal static int Umbral(int cantidadPalabras)=>Math.Max(1,(int)Math.Ceiling(cantidadPalabras*0.5));
    /// <summary>Para armar un LIKE literal: escapa los comodines con la barra invertida (usar <c>ESCAPE '\'</c>).</summary>
    internal static string LikeLiteral(string t)=>t.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_").Replace("[","\\[");

    /// <summary>"24 X 6", "x12" al final o "(24)": unidades por bulto que informa el propio texto, como CompletaDetalle del legacy.</summary>
    internal static (decimal Unidades,string Origen)? BultoDesdeDescripcion(string? descripcion)
    {
        var texto=descripcion??"";
        var m=NxM().Match(texto);
        if(m.Success&&int.TryParse(m.Groups[1].Value,out var a)&&int.TryParse(m.Groups[2].Value,out var b)&&a>0&&b>0&&(a>1||b>1))
        {
            // Valores razonables: si uno es chico (<=24) y el otro grande, el bulto es el chico; si ambos son chicos, el de la izquierda; si ambos grandes, el de la derecha.
            var v=a<=24&&b>24?a:b<=24&&a>24?b:a<=24&&b<=24?a:b;
            if(v>1)return (v,"Descripción (N x M)");
        }
        var p=Parentesis().Match(texto);
        if(p.Success&&int.TryParse(p.Groups[1].Value,out var n)&&n is >=2 and <=999)return (n,"Descripción (entre paréntesis)");
        var x=XFinal().Match(texto);
        if(x.Success&&int.TryParse(x.Groups[1].Value,out var k)&&k is >=2 and <=200)return (k,"Descripción (x N)");
        return null;
    }
    [GeneratedRegex(@"(?<![A-Za-z0-9])[xX]\s*(\d{1,3})\s*$")] private static partial Regex XFinal();
    [GeneratedRegex(@"(?<!\d)(\d{1,4})\s*[xX]\s*(\d{1,4})(?!\d)")] private static partial Regex NxM();
    [GeneratedRegex(@"\((\d{1,3})\)?\s*$")] private static partial Regex Parentesis();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex EspaciosMultiples();
}

using System.Text.RegularExpressions;

namespace AlfaCore.ConciliacionBancaria.Application.Services;

public static class ContraparteBancaria
{
    public static string NormalizarCuit(string? valor) => new((valor ?? "").Where(c=>c is >= '0' and <= '9').ToArray());
    internal static string PatronCuit(string valor) => "%"+string.Join("%",NormalizarCuit(valor).ToCharArray())+"%";
    public static (string? Cuit,string Nombre) Extraer(string descripcion)
    {
        var texto=Regex.Replace(descripcion.Replace(@"\t"," "),@"\s+"," ").Trim();
        var cuit=Regex.Match(texto,@"(?<!\d)\d{11}(?!\d)");
        var nombre=texto;
        if(cuit.Success)
        {
            var antes=Limpiar(texto[..cuit.Index]);
            nombre=antes.Any(char.IsLetter) ? antes : texto[(cuit.Index+cuit.Length)..];
        }
        return (cuit.Success ? cuit.Value : null,Limpiar(nombre));
    }

    private static string Limpiar(string texto)
    {
        texto=Regex.Replace(texto,@"^(?:pago a proveedores recibido|transferencia recibida|transf recibida cvu dif titular|transf recibida|cr\.tran\.)\s*", "",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant).Trim();
        texto=Regex.Replace(texto,@"^de\s+","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return texto.Split('/')[0].Trim(' ','.','-');
    }
}

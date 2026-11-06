namespace AlfaCore.Common;

/// <summary>Importes en letras para comprobantes: «Un Mil Quinientos Pesos Con 00/100».</summary>
public static class NumeroALetras
{
    private static readonly string[] Unidades = ["", "Un", "Dos", "Tres", "Cuatro", "Cinco", "Seis", "Siete", "Ocho", "Nueve", "Diez", "Once", "Doce", "Trece", "Catorce", "Quince",
        "Dieciseis", "Diecisiete", "Dieciocho", "Diecinueve", "Veinte", "Veintiun", "Veintidos", "Veintitres", "Veinticuatro", "Veinticinco", "Veintiseis", "Veintisiete", "Veintiocho", "Veintinueve"];
    private static readonly string[] Decenas = ["", "", "", "Treinta", "Cuarenta", "Cincuenta", "Sesenta", "Setenta", "Ochenta", "Noventa"];
    private static readonly string[] Centenas = ["", "Ciento", "Doscientos", "Trescientos", "Cuatrocientos", "Quinientos", "Seiscientos", "Setecientos", "Ochocientos", "Novecientos"];

    private static string Menor1000(int n)
    {
        if (n == 100) return "Cien";
        var partes = new List<string>();
        if (n >= 100) { partes.Add(Centenas[n / 100]); n %= 100; }
        if (n >= 30) { var d = Decenas[n / 10]; n %= 10; partes.Add(n > 0 ? d + " Y " + Unidades[n] : d); }
        else if (n > 0) partes.Add(Unidades[n]);
        return string.Join(" ", partes);
    }

    private static string Entero(long n)
    {
        if (n == 0) return "Cero";
        var partes = new List<string>();
        var billones = n / 1_000_000_000_000;
        n %= 1_000_000_000_000;
        if (billones > 0) partes.Add(billones == 1 ? "Un Billon" : Menor1000((int)billones) + " Billones");
        var millones = n / 1_000_000; n %= 1_000_000;
        if (millones > 0) partes.Add(millones == 1 ? "Un Millon" : Menor1000((int)millones) + " Millones");
        var miles = n / 1000; n %= 1000;
        if (miles > 0) partes.Add(miles == 1 ? "Un Mil" : Menor1000((int)miles) + " Mil");
        if (n > 0) partes.Add(Menor1000((int)n));
        return string.Join(" ", partes);
    }

    public static string Pesos(decimal importe)
    {
        importe = decimal.Round(Math.Abs(importe), 2, MidpointRounding.AwayFromZero);
        var entero = (long)decimal.Truncate(importe);
        var centavos = (int)((importe - entero) * 100);
        return $"{Entero(entero)} {(entero == 1 ? "Peso" : "Pesos")} Con {centavos:00}/100";
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

/// <summary>Valores de un artículo que puede usar una fórmula de política de precios.</summary>
public sealed record PoliticaPreciosVariables(decimal Costo, decimal[] Precios, decimal TasaIva, decimal Kilos, decimal M3, decimal Impuestos)
{
    public decimal Precio(int clase) => clase is >= 1 and <= 8 ? Precios[clase - 1] : 0;
}

/// <summary>
/// Fórmulas de políticas de precios (V_TA_POLITICAPRECIOS.FCOSTO / FCLASE1..8), idénticas al procedimiento NW_OBTIENEVALOR
/// que usan los triggers FMT_V_MA_ARTICULOS y FMT_V_MA_PRECIOS: una variable inicial y operaciones de izquierda a derecha,
/// sin precedencia ni paréntesis. Ej.: &lt;COSTO&gt; %+ 30 * 1.21.
/// </summary>
public static partial class PoliticasPreciosFormula
{
    /// <summary>Variables que entiende la base (en NW_OBTIENEVALOR &lt;M3&gt; solo funciona después de un operador).</summary>
    public static readonly IReadOnlyList<string> Variables = ["COSTO", "CLASE1", "CLASE2", "CLASE3", "CLASE4", "CLASE5", "CLASE6", "CLASE7", "CLASE8", "IVA", "KILOS", "M3", "IMPUESTOS"];

    private static readonly string[] Operadores = ["%+", "%-", "+", "-", "*", "/", "\\"];

    /// <summary>Fórmula que deja el valor como está (la que muestra el legacy cuando no hay fórmula).</summary>
    public static string Identidad(int clase) => clase == 0 ? "<COSTO>" : $"<CLASE{clase}>";

    /// <summary>Saca espacios, pasa la coma decimal a punto (la base toma la coma como separador de miles) y las variables a mayúsculas.</summary>
    public static string Normalizar(string? formula)
    {
        var f = (formula ?? "").Replace(" ", "").Replace("\t", "").Replace(',', '.');
        return VariableRegex().Replace(f, m => m.Value.ToUpperInvariant());
    }

    /// <summary>Null si la fórmula es válida; si no, qué corregir.</summary>
    public static string? Validar(string? formula)
    {
        var f = Normalizar(formula);
        if (f.Length == 0) return "Falta la fórmula.";
        if (f.Length > 100) return "La fórmula admite hasta 100 caracteres.";
        var m = InicioRegex().Match(f);
        if (!m.Success) return "La fórmula tiene que empezar con una variable entre < >, por ejemplo <COSTO>.";
        var inicial = m.Groups[1].Value;
        if (!Variables.Contains(inicial)) return $"La variable <{inicial}> no existe. Usá {string.Join(" ", Variables.Select(v => $"<{v}>"))}.";
        if (inicial == "M3") return "<M3> no puede ser la variable inicial (la base no la reconoce ahí): ponela después de un operador.";
        var resto = f[m.Length..];
        if (resto.Length > 50) return "Después de la variable inicial la fórmula admite hasta 50 caracteres (límite de la base).";
        var i = 0;
        while (i < resto.Length)
        {
            var op = Operadores.FirstOrDefault(o => string.CompareOrdinal(resto, i, o, 0, o.Length) == 0);
            if (op is null) return $"Se esperaba un operador (+ - * / \\ %+ %-) en «{resto[i..]}».";
            i += op.Length;
            var operando = OperandoRegex().Match(resto, i);
            if (!operando.Success || operando.Index != i)
                return i >= resto.Length ? $"Falta el valor después de «{op}»."
                    : $"Después de «{op}» va un número (con punto decimal, sin signo) o una variable entre < >: revisá «{resto[i..]}».";
            var valor = operando.Value;
            if (valor.StartsWith('<'))
            {
                var nombre = valor[1..^1];
                if (!Variables.Contains(nombre)) return $"La variable <{nombre}> no existe.";
            }
            else if ((op is "/" or "\\") && decimal.Parse(valor, CultureInfo.InvariantCulture) == 0)
                return "No se puede dividir por cero.";
            else if (valor is "0" or "0.0" or "0.00" && op is "*")
                return "Multiplicar por 0 no tiene efecto en la base (devuelve el valor sin cambios).";
            i += valor.Length;
        }
        return null;
    }

    /// <summary>Evalúa como NW_OBTIENEVALOR (valores money: 4 decimales; las variables después de un operador entran con 2).</summary>
    public static decimal Evaluar(string? formula, PoliticaPreciosVariables v)
    {
        var f = (formula ?? "").Replace(" ", "");
        var desde = f.IndexOf('<');
        var hasta = f.IndexOf('>');
        if (desde < 0 || hasta < 0 || hasta < desde) return 0;
        decimal? baseValor = Variable(f[(desde + 1)..hasta].ToUpperInvariant(), v, inicial: true);
        if (baseValor is not { } pbase) throw new InvalidOperationException($"La variable {f[desde..(hasta + 1)]} no existe.");
        if (pbase == 0) return 0;
        var resto = f[(hasta + 1)..];
        if (resto.Length > 50) resto = resto[..50];
        decimal nvalor = 0;
        while (resto.Length > 0)
        {
            var p = PrimerOperador(resto);
            if (p < 0) return nvalor;
            string op;
            if (resto[p] == '%') { op = resto.Substring(p, 2); resto = resto[(p + 2)..]; }
            else { op = resto[p].ToString(); resto = resto[(p + 1)..]; }
            if (resto.Contains('>'))
            {
                var cierre = resto.IndexOf('>', Math.Min(2, resto.Length));
                if (resto.StartsWith('<') && cierre > 0)
                {
                    var token = resto[..(cierre + 1)];
                    if (Variable(token[1..^1].ToUpperInvariant(), v, inicial: false) is { } valorToken)
                        resto = ReemplazarTodas(resto, token, Texto(valorToken));
                }
            }
            var q = PrimerOperador(resto);
            decimal valor;
            if (q < 0)
            {
                if (resto == "0") return pbase; // la base devuelve el valor sin aplicar la operación
                valor = Money(resto);
                q = resto.Length - 1;
            }
            else valor = Money(resto[..q]);
            resto = resto[q..];
            nvalor = op switch
            {
                "+" => pbase + valor,
                "-" => pbase - valor,
                "*" => Redondear(pbase * valor),
                "/" => valor == 0 ? throw new DivideByZeroException("La fórmula divide por cero.") : Truncar(pbase / valor),
                "\\" => valor == 0 ? throw new DivideByZeroException("La fórmula divide por cero.") : decimal.Truncate(pbase / valor),
                "%+" => pbase + Truncar(Redondear(pbase * valor) / 100),
                "%-" => pbase - Truncar(Redondear(pbase * valor) / 100),
                _ => nvalor
            };
            pbase = nvalor;
        }
        return pbase;
    }

    private static int PrimerOperador(string s)
    {
        var minimo = -1;
        foreach (var c in new[] { "+", "-", "*", "/", "\\", "%-", "%+" })
        {
            var i = s.IndexOf(c, StringComparison.Ordinal);
            if (i >= 0 && (minimo < 0 || i < minimo)) minimo = i;
        }
        return minimo;
    }

    private static decimal? Variable(string nombre, PoliticaPreciosVariables v, bool inicial) => nombre switch
    {
        "COSTO" => v.Costo,
        "CLASE1" => v.Precio(1), "CLASE2" => v.Precio(2), "CLASE3" => v.Precio(3), "CLASE4" => v.Precio(4),
        "CLASE5" => v.Precio(5), "CLASE6" => v.Precio(6), "CLASE7" => v.Precio(7), "CLASE8" => v.Precio(8),
        "IVA" => v.TasaIva,
        "KILOS" => v.Kilos,
        "M3" => inicial ? null : v.M3,
        "IMPUESTOS" => v.Impuestos,
        _ => null
    };

    // CONVERT(NVARCHAR, money): 2 decimales, punto, sin miles.
    private static string Texto(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    // CONVERT(MONEY, texto): vacío = 0; la coma es separador de miles.
    private static decimal Money(string texto)
    {
        var t = texto.Replace(",", "");
        if (t.Length == 0) return 0;
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? Redondear(d)
            : throw new FormatException($"«{texto}» no es un número.");
    }

    private static decimal Redondear(decimal x) => decimal.Round(x, 4, MidpointRounding.AwayFromZero);

    // money / money en SQL Server trunca a 4 decimales.
    private static decimal Truncar(decimal x) => decimal.Truncate(x * 10000m) / 10000m;

    private static string ReemplazarTodas(string s, string token, string valor)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (true)
        {
            var j = s.IndexOf(token, i, StringComparison.OrdinalIgnoreCase);
            if (j < 0) { sb.Append(s, i, s.Length - i); break; }
            sb.Append(s, i, j - i).Append(valor);
            i = j + token.Length;
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"<[A-Za-z0-9]+>")]
    private static partial Regex VariableRegex();

    [GeneratedRegex(@"^<([A-Z0-9]+)>")]
    private static partial Regex InicioRegex();

    [GeneratedRegex(@"<[A-Z0-9]+>|\d+(\.\d+)?")]
    private static partial Regex OperandoRegex();
}

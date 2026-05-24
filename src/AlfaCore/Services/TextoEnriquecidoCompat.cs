using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public static class TextoEnriquecidoCompat
{
    private static readonly Regex HtmlRegex = new(@"<\s*[a-zA-Z][^>]*>", RegexOptions.Compiled);
    private static readonly Regex ControlWordRegex = new(@"\\([a-zA-Z]+)(-?\d+)?\s?", RegexOptions.Compiled);

    public static string NormalizarParaEditor(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        var normalized = valor.Trim();
        if (EsHtml(normalized))
            return normalized;

        if (EsRtf(normalized))
            return ConvertirRtfBasicoAHtml(normalized);

        return ConvertirTextoPlanoAHtml(normalized);
    }

    public static string NormalizarComoTextoPlano(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        var normalized = valor.Trim();
        if (EsRtf(normalized))
            return LimpiarEspaciado(ExtraerTextoRtf(normalized));

        if (EsHtml(normalized))
            return LimpiarEspaciado(ExtraerTextoHtml(normalized));

        return normalized;
    }

    public static bool EsRtf(string? valor)
        => !string.IsNullOrWhiteSpace(valor)
           && valor.TrimStart().StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase);

    public static bool EsHtml(string? valor)
        => !string.IsNullOrWhiteSpace(valor)
           && HtmlRegex.IsMatch(valor);

    private static string ConvertirRtfBasicoAHtml(string rtf)
    {
        var plainText = ExtraerTextoRtf(rtf);
        return ConvertirTextoPlanoAHtml(plainText);
    }

    private static string ExtraerTextoRtf(string rtf)
    {
        var latin1 = Encoding.Latin1;
        var output = new StringBuilder(rtf.Length);
        var skipStack = new Stack<bool>();
        var destinationStack = new Stack<bool>();
        var skipping = false;
        var skippingDestination = false;

        for (var i = 0; i < rtf.Length; i++)
        {
            var ch = rtf[i];
            switch (ch)
            {
                case '{':
                    skipStack.Push(skipping);
                    destinationStack.Push(skippingDestination);
                    break;

                case '}':
                    if (skipStack.Count > 0)
                        skipping = skipStack.Pop();
                    if (destinationStack.Count > 0)
                        skippingDestination = destinationStack.Pop();
                    break;

                case '\\':
                    if (i + 1 >= rtf.Length)
                        break;

                    var next = rtf[i + 1];
                    if (next is '\\' or '{' or '}')
                    {
                        if (!skipping && !skippingDestination)
                            output.Append(next);
                        i++;
                        break;
                    }

                    if (next == '\'')
                    {
                        if (i + 3 < rtf.Length)
                        {
                            var hex = rtf.Substring(i + 2, 2);
                            if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) && !skipping && !skippingDestination)
                                output.Append(latin1.GetString([value]));
                            i += 3;
                        }
                        break;
                    }

                    if (next == '*')
                    {
                        skippingDestination = true;
                        i++;
                        break;
                    }

                    var match = ControlWordRegex.Match(rtf, i);
                    if (!match.Success || match.Index != i)
                        break;

                    var word = match.Groups[1].Value;
                    var argument = match.Groups[2].Success ? match.Groups[2].Value : string.Empty;

                    if (!skippingDestination)
                        skippingDestination = EsDestinoIgnorable(word);

                    if (!skipping && !skippingDestination)
                        AplicarControlWord(output, word, argument);

                    i = match.Index + match.Length - 1;
                    if (!skipping && !skippingDestination && word.Equals("u", StringComparison.OrdinalIgnoreCase) && match.Groups[2].Success)
                    {
                        if (int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unicodeValue))
                        {
                            if (unicodeValue < 0)
                                unicodeValue += 65536;
                            output.Append(char.ConvertFromUtf32(unicodeValue));
                        }

                        if (i + 1 < rtf.Length && rtf[i + 1] == '?')
                            i++;
                    }
                    break;

                case '\r':
                case '\n':
                    break;

                default:
                    if (!skipping && !skippingDestination)
                        output.Append(ch);
                    break;
            }
        }

        return output.ToString();
    }

    private static bool EsDestinoIgnorable(string controlWord)
        => controlWord.Equals("fonttbl", StringComparison.OrdinalIgnoreCase)
           || controlWord.Equals("colortbl", StringComparison.OrdinalIgnoreCase)
           || controlWord.Equals("stylesheet", StringComparison.OrdinalIgnoreCase)
           || controlWord.Equals("info", StringComparison.OrdinalIgnoreCase)
           || controlWord.Equals("pict", StringComparison.OrdinalIgnoreCase)
           || controlWord.Equals("object", StringComparison.OrdinalIgnoreCase)
           || controlWord.Equals("header", StringComparison.OrdinalIgnoreCase)
           || controlWord.Equals("footer", StringComparison.OrdinalIgnoreCase)
           || controlWord.Equals("fldinst", StringComparison.OrdinalIgnoreCase)
           || controlWord.Equals("datafield", StringComparison.OrdinalIgnoreCase);

    private static void AplicarControlWord(StringBuilder output, string word, string argument)
    {
        switch (word.ToLowerInvariant())
        {
            case "par":
                output.AppendLine();
                break;
            case "line":
                output.AppendLine();
                break;
            case "tab":
                output.Append('\t');
                break;
            case "emdash":
                output.Append('—');
                break;
            case "endash":
                output.Append('–');
                break;
            case "bullet":
                output.Append("• ");
                break;
            case "lquote":
            case "rquote":
                output.Append('\'');
                break;
            case "ldblquote":
            case "rdblquote":
                output.Append('"');
                break;
            case "u":
                break;
            default:
                if (word.Equals("uc", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("cf", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("f", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("fs", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("lang", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("viewkind", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("viewscale", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("paperw", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("paperh", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("margl", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("margr", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("margt", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("margb", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("deff", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("plain", StringComparison.OrdinalIgnoreCase))
                    break;
                if (word.Equals("b", StringComparison.OrdinalIgnoreCase) && argument == "0")
                    break;
                if (word.Equals("i", StringComparison.OrdinalIgnoreCase) && argument == "0")
                    break;
                break;
        }
    }

    private static string ConvertirTextoPlanoAHtml(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var normalized = texto
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        var paragraphs = normalized
            .Split(["\n\n"], StringSplitOptions.None)
            .Select(block => block.Trim('\n'))
            .Where(block => block.Length > 0)
            .Select(block => $"<p>{WebUtility.HtmlEncode(block).Replace("\n", "<br>", StringComparison.Ordinal)}</p>");

        return string.Join(Environment.NewLine, paragraphs);
    }

    private static string ExtraerTextoHtml(string html)
    {
        var normalized = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase);

        normalized = Regex.Replace(normalized, "<[^>]+>", string.Empty, RegexOptions.Singleline);
        return WebUtility.HtmlDecode(normalized);
    }

    private static string LimpiarEspaciado(string texto)
        => Regex.Replace(
                texto
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Trim(),
                @"\n{3,}",
                "\n\n")
            .Trim();
}

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

/// <summary>
/// Limpia el HTML de un informe antes de mostrarlo en la página pública o guardarlo: deja solo etiquetas de texto,
/// listas y tablas, sin atributos salvo href seguros y las clases de los banners del editor de tickets.
/// Script, style, iframe y similares se descartan con su contenido. El resto de etiquetas desconocidas se quitan
/// dejando su texto.
/// </summary>
public static partial class InformeHtmlSanitizer
{
    private static readonly HashSet<string> Permitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "b", "em", "i", "u", "s", "small", "code", "pre", "blockquote", "hr",
        "h1", "h2", "h3", "h4", "ul", "ol", "li", "div", "span", "a",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption"
    };

    private static readonly HashSet<string> Vacias = new(StringComparer.OrdinalIgnoreCase) { "br", "hr" };

    /// <summary>Etiquetas que se descartan junto con todo su contenido.</summary>
    private static readonly HashSet<string> Peligrosas = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "noscript", "template", "svg", "math", "textarea", "select", "head", "title"
    };

    private static readonly HashSet<string> ClasesPermitidas = new(StringComparer.Ordinal)
    {
        "ticket-editor-banner", "ticket-editor-banner__body", "ticket-editor-banner__icon",
        "ticket-editor-banner--info", "ticket-editor-banner--success", "ticket-editor-banner--warning", "ticket-editor-banner--danger",
        "num"
    };

    public static string Limpiar(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var entrada = Comentarios().Replace(html, string.Empty);
        foreach (var tag in Peligrosas)
            entrada = Regex.Replace(entrada, $@"<{tag}\b[\s\S]*?(</{tag}\s*>|$)", string.Empty, RegexOptions.IgnoreCase);

        var salida = new StringBuilder(entrada.Length);
        var abiertas = new Stack<string>();
        var pos = 0;
        foreach (Match m in Etiqueta().Matches(entrada))
        {
            salida.Append(Texto(entrada[pos..m.Index]));
            pos = m.Index + m.Length;
            var cierre = m.Groups["cierre"].Success;
            var nombre = m.Groups["nombre"].Value.ToLowerInvariant();
            if (!Permitidas.Contains(nombre)) continue;
            if (cierre)
            {
                if (!abiertas.Contains(nombre)) continue;
                while (abiertas.Count > 0)
                {
                    var t = abiertas.Pop();
                    salida.Append("</").Append(t).Append('>');
                    if (t == nombre) break;
                }
                continue;
            }
            salida.Append('<').Append(nombre).Append(Atributos(nombre, m.Groups["attrs"].Value)).Append('>');
            if (!Vacias.Contains(nombre)) abiertas.Push(nombre);
        }
        salida.Append(Texto(entrada[pos..]));
        while (abiertas.Count > 0) salida.Append("</").Append(abiertas.Pop()).Append('>');
        return salida.ToString().Trim();
    }

    /// <summary>Texto plano del informe, para pasarle a la IA o para un resumen.</summary>
    public static string ATexto(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var t = Regex.Replace(html, @"<(br|/p|/li|/h\d|/tr|/div)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<li\b[^>]*>", "- ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<[^>]+>", " ");
        t = WebUtility.HtmlDecode(t);
        t = Regex.Replace(t, @"[ \t]+", " ");
        t = Regex.Replace(t, @" *\n *", "\n");
        return Regex.Replace(t, @"\n{3,}", "\n\n").Trim();
    }

    /// <summary>Texto con saltos de línea (descripciones viejas sin HTML) a párrafos.</summary>
    public static string TextoAHtml(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var bloques = Regex.Split(texto.Replace("\r\n", "\n").Trim(), @"\n\s*\n");
        return string.Concat(bloques.Select(b => "<p>" + Codificar(b.Trim()).Replace("\n", "<br>") + "</p>"));
    }

    /// <summary>Escapa solo lo necesario (deja tildes y eñes tal cual, a diferencia de WebUtility.HtmlEncode).</summary>
    public static string Codificar(string? texto)
        => (texto ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public static bool PareceHtml(string? texto) => !string.IsNullOrEmpty(texto) && EtiquetaConocida().IsMatch(texto);

    private static string Texto(string segmento)
        => segmento.Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Atributos(string etiqueta, string crudo)
    {
        if (string.IsNullOrWhiteSpace(crudo)) return string.Empty;
        var sb = new StringBuilder();
        foreach (Match a in Atributo().Matches(crudo))
        {
            var nombre = a.Groups["n"].Value.ToLowerInvariant();
            var valor = WebUtility.HtmlDecode(a.Groups["v1"].Success ? a.Groups["v1"].Value : a.Groups["v2"].Success ? a.Groups["v2"].Value : a.Groups["v3"].Value).Trim();
            if (etiqueta == "a" && nombre == "href" && UrlSegura(valor))
                sb.Append(" href=\"").Append(WebUtility.HtmlEncode(valor)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\"");
            else if (nombre == "class")
            {
                var clases = valor.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(c => ClasesPermitidas.Contains(c) || c.StartsWith("bi-", StringComparison.Ordinal) || c == "bi").ToList();
                if (clases.Count > 0) sb.Append(" class=\"").Append(WebUtility.HtmlEncode(string.Join(' ', clases))).Append('"');
            }
            else if ((etiqueta is "td" or "th") && nombre is "colspan" or "rowspan" && int.TryParse(valor, out var n) && n is > 0 and < 50)
                sb.Append(' ').Append(nombre).Append("=\"").Append(n).Append('"');
        }
        return sb.ToString();
    }

    private static bool UrlSegura(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeMailto);

    [GeneratedRegex(@"<!--[\s\S]*?(-->|$)")]
    private static partial Regex Comentarios();

    [GeneratedRegex(@"<(?<cierre>/)?(?<nombre>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:\s+[^>]*?)?)\s*/?>")]
    private static partial Regex Etiqueta();

    [GeneratedRegex(@"(?<n>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*(?:=\s*(?:""(?<v1>[^""]*)""|'(?<v2>[^']*)'|(?<v3>[^\s""'>]+)))?")]
    private static partial Regex Atributo();

    [GeneratedRegex(@"<(p|br|div|h[1-6]|ul|ol|li|strong|em|b|i|table|span|a)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex EtiquetaConocida();
}

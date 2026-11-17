using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Reglas puras del reclamo de saldos (sin base de datos): elección del tramo, reemplazo de variables y
/// armado del detalle de deuda para email (HTML) y WhatsApp (una línea). Variables del mensaje, como en el
/// legacy (&lt;NOMBRE&gt;, &lt;DETALLE&gt;, &lt;FORMADEPAGO&gt;) más &lt;SALDO&gt;, &lt;VENCIDO&gt;, &lt;CANTIDAD&gt;, &lt;DIAS&gt;,
/// &lt;FECHA&gt; y &lt;FECHAANTIGUA&gt;.
/// </summary>
public static class ReclamoSaldosPlantilla
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>Variables disponibles (para mostrarlas en la pantalla de configuración).</summary>
    public static IReadOnlyList<(string Token, string Descripcion)> Variables { get; } =
    [
        ("<NOMBRE>", "Razón social del cliente."),
        ("<DETALLE>", "Tabla con cada comprobante pendiente (email) o resumen en una línea (WhatsApp)."),
        ("<FORMADEPAGO>", "Forma de pago configurada para la unidad de negocio del cliente."),
        ("<SALDO>", "Saldo total de la cuenta corriente."),
        ("<VENCIDO>", "Importe de los comprobantes ya vencidos."),
        ("<CANTIDAD>", "Cantidad de comprobantes pendientes."),
        ("<DIAS>", "Días de atraso del comprobante vencido más antiguo."),
        ("<FECHA>", "Fecha de hoy."),
        ("<FECHAANTIGUA>", "Fecha del comprobante pendiente más antiguo."),
        ("<TARJETAS>", "Email: cuatro tarjetas con saldo, vencido, comprobantes y días de atraso."),
        ("<PORTAL>", "Email, opcional: botón que abre la cuenta del cliente en el Portal Cliente sin pedir usuario ni clave. Solo se genera al enviar de verdad.")
    ];

    private const string Tinta = "#101828", Gris = "#667085", Borde = "#d0d5dd", Rojo = "#b42318";

    /// <summary>
    /// Cuatro tarjetas (tabla con estilos en línea, apta para clientes de correo) con lo esencial de la deuda.
    /// Los colores son fijos: el correo no hereda el tema de la aplicación.
    /// </summary>
    public static string TarjetasHtml(ReclamoCliente c)
    {
        var tarjetas = new (string Titulo, string Valor, string Color)[]
        {
            ("Saldo total", Dinero(c.Saldo), Tinta),
            ("Vencido", Dinero(c.Vencido), c.Vencido > 0 ? Rojo : Tinta),
            ("Comprobantes", c.CantidadComprobantes.ToString(Ar), Tinta),
            ("Días de atraso", c.DiasAtraso > 0 ? c.DiasAtraso.ToString(Ar) : "Sin atraso", c.DiasAtraso > 0 ? Rojo : "#067647")
        };
        var sb = new StringBuilder("<table role=\"presentation\" cellspacing=\"6\" cellpadding=\"0\" style=\"border-collapse:separate;font-family:Arial,sans-serif\"><tr>");
        foreach (var (titulo, valor, color) in tarjetas)
            sb.Append($"<td style=\"background:#f9fafb;border:1px solid {Borde};border-radius:8px;padding:10px 16px;min-width:120px\">")
              .Append($"<div style=\"font-size:11px;color:{Gris};text-transform:uppercase;letter-spacing:.04em\">{WebUtility.HtmlEncode(titulo)}</div>")
              .Append($"<div style=\"font-size:18px;font-weight:bold;color:{color};margin-top:2px\">{WebUtility.HtmlEncode(valor)}</div></td>");
        return sb.Append("</tr></table>").ToString();
    }

    /// <summary>Botón que lleva al portal. <paramref name="vence"/> se muestra para que el cliente sepa hasta cuándo sirve.</summary>
    public static string PortalHtml(string url, DateTime? vence)
        => $"<p style=\"margin:8px 0\"><a href=\"{WebUtility.HtmlEncode(url)}\" style=\"display:inline-block;background:#1d4ed8;color:#ffffff;padding:10px 18px;" +
           "border-radius:6px;text-decoration:none;font-weight:bold;font-family:Arial,sans-serif;font-size:14px\">Ver mi cuenta en el portal</a>" +
           (vence is { } v ? $"<br><span style=\"font-size:12px;color:{Gris};font-family:Arial,sans-serif\">Enlace personal, no lo compartas. Vence el {v:dd/MM/yyyy}.</span>" : string.Empty) + "</p>";

    /// <summary>Documento completo para mostrar el cuerpo en un iframe aislado de los estilos (y del tema oscuro) de la aplicación.</summary>
    public static string DocumentoVistaPrevia(string cuerpoHtml)
        => $"<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:0;padding:16px;background:#ffffff;color:{Tinta};font-family:Arial,sans-serif\">{cuerpoHtml}</body></html>";

    public static string Dinero(decimal v) => "$ " + v.ToString("N2", Ar);

    /// <summary>Tramo 1-3 según los días de atraso: hasta N1 → 1, hasta N2 → 2, más → 3.</summary>
    public static int ElegirTramo(int diasAtraso, int diasHasta1, int diasHasta2)
        => diasAtraso <= diasHasta1 ? 1 : diasAtraso <= diasHasta2 ? 2 : 3;

    /// <summary>Reemplaza variables en un texto plano (asunto o WhatsApp). Sin HTML.</summary>
    public static string ReemplazarTexto(string plantilla, IReadOnlyDictionary<string, string> valores)
    {
        var s = plantilla ?? string.Empty;
        foreach (var (token, valor) in valores)
            s = Regex.Replace(s, Regex.Escape(token), _ => valor, RegexOptions.IgnoreCase);
        return s;
    }

    /// <summary>
    /// Convierte el mensaje escrito por el usuario (texto plano con variables) en HTML seguro: escapa todo,
    /// respeta los saltos de línea y reemplaza cada variable por su valor ya escapado (&lt;DETALLE&gt; entra como HTML).
    /// </summary>
    public static string ReemplazarHtml(string plantilla, IReadOnlyDictionary<string, string> valoresHtml)
    {
        var s = WebUtility.HtmlEncode(plantilla ?? string.Empty);
        foreach (var (token, valorHtml) in valoresHtml)
            s = Regex.Replace(s, Regex.Escape(WebUtility.HtmlEncode(token)), _ => valorHtml, RegexOptions.IgnoreCase);
        return s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");
    }

    public static string NumeroComprobante(ReclamoComprobante c)
        => $"{c.Tc} {c.Numero}".Trim();

    /// <summary>Tabla HTML de comprobantes pendientes; resalta vencidos y avisa si el saldo difiere de la suma.</summary>
    public static string DetalleHtml(IReadOnlyList<ReclamoComprobante> cptes, ReclamoCliente cliente, IReadOnlyDictionary<string, string>? nombresUnidad = null)
    {
        if (cptes.Count == 0) return "<p>No hay comprobantes pendientes de pago.</p>";
        var variasUnidades = cptes.Select(c => c.Unidad).Distinct().Count() > 1;
        var sb = new StringBuilder();
        const string th = "padding:6px 10px;border:1px solid #d0d5dd;background:#f2f4f7;text-align:{0};font-size:13px";
        const string td = "padding:6px 10px;border:1px solid #d0d5dd;text-align:{0};font-size:13px{1}";
        sb.Append("<table style=\"border-collapse:collapse;font-family:Arial,sans-serif\" cellspacing=\"0\"><thead><tr>");
        if (variasUnidades) sb.Append($"<th style=\"{string.Format(th, "left")}\">Unidad</th>");
        sb.Append($"<th style=\"{string.Format(th, "left")}\">Comprobante</th><th style=\"{string.Format(th, "left")}\">Fecha</th>")
          .Append($"<th style=\"{string.Format(th, "left")}\">Vencimiento</th><th style=\"{string.Format(th, "right")}\">Días de atraso</th>")
          .Append($"<th style=\"{string.Format(th, "right")}\">Importe</th><th style=\"{string.Format(th, "right")}\">Pendiente</th></tr></thead><tbody>");
        foreach (var c in cptes)
        {
            var rojo = c.DiasVencido > 0 ? ";color:#b42318;font-weight:bold" : string.Empty;
            sb.Append("<tr>");
            if (variasUnidades)
            {
                var u = nombresUnidad is not null && nombresUnidad.TryGetValue(c.Unidad, out var n) && n.Length > 0 ? n : c.Unidad;
                sb.Append($"<td style=\"{string.Format(td, "left", "")}\">{WebUtility.HtmlEncode(u)}</td>");
            }
            sb.Append($"<td style=\"{string.Format(td, "left", "")}\">{WebUtility.HtmlEncode(NumeroComprobante(c))}</td>")
              .Append($"<td style=\"{string.Format(td, "left", "")}\">{c.Fecha:dd/MM/yyyy}</td>")
              .Append($"<td style=\"{string.Format(td, "left", rojo)}\">{(c.Vencimiento is { } v ? v.ToString("dd/MM/yyyy") : "—")}</td>")
              .Append($"<td style=\"{string.Format(td, "right", rojo)}\">{(c.DiasVencido > 0 ? c.DiasVencido.ToString(Ar) : "—")}</td>")
              .Append($"<td style=\"{string.Format(td, "right", "")}\">{c.Importe.ToString("N2", Ar)}</td>")
              .Append($"<td style=\"{string.Format(td, "right", "")}\"><strong>{c.Saldo.ToString("N2", Ar)}</strong></td></tr>");
        }
        var cols = variasUnidades ? 6 : 5;
        sb.Append($"<tr><td colspan=\"{cols}\" style=\"{string.Format(td, "right", ";font-weight:bold")}\">Total pendiente</td>")
          .Append($"<td style=\"{string.Format(td, "right", ";font-weight:bold")}\">{cliente.SumaComprobantes.ToString("N2", Ar)}</td></tr>");
        if (cliente.Vencido > 0)
            sb.Append($"<tr><td colspan=\"{cols}\" style=\"{string.Format(td, "right", ";color:#b42318;font-weight:bold")}\">De los cuales vencidos</td>")
              .Append($"<td style=\"{string.Format(td, "right", ";color:#b42318;font-weight:bold")}\">{cliente.Vencido.ToString("N2", Ar)}</td></tr>");
        sb.Append("</tbody></table>");
        if (cliente.SaldoDifiere)
            sb.Append($"<p style=\"font-size:12px;color:#667085\">El saldo de su cuenta corriente es {WebUtility.HtmlEncode(Dinero(cliente.Saldo))}: " +
                      "incluye notas de crédito o pagos a cuenta que todavía no están aplicados a estos comprobantes.</p>");
        return sb.ToString();
    }

    /// <summary>Resumen en una sola línea, apto para una variable de plantilla de WhatsApp (sin saltos de línea ni tabs).</summary>
    public static string DetalleUnaLinea(IReadOnlyList<ReclamoComprobante> cptes, ReclamoCliente cliente, int maxComprobantes = 4)
    {
        if (cptes.Count == 0) return "sin comprobantes pendientes";
        var sb = new StringBuilder($"Saldo {Dinero(cliente.Saldo)} en {cptes.Count} comprobante(s)");
        if (cliente.Vencido > 0) sb.Append($", vencido {Dinero(cliente.Vencido)}");
        sb.Append(": ");
        sb.Append(string.Join(" | ", cptes.Take(maxComprobantes).Select(c =>
            $"{NumeroComprobante(c)} {Dinero(c.Saldo)}" + (c.Vencimiento is { } v ? $" (vto {v:dd/MM})" : string.Empty))));
        if (cptes.Count > maxComprobantes) sb.Append($" y {cptes.Count - maxComprobantes} más");
        return LimpiarParaWhatsApp(sb.ToString());
    }

    /// <summary>Meta rechaza variables con saltos de línea, tabs o más de 4 espacios seguidos, y limita el largo.</summary>
    public static string LimpiarParaWhatsApp(string texto, int max = 900)
    {
        var s = Regex.Replace(texto ?? string.Empty, @"[\r\n\t]+", " ");
        s = Regex.Replace(s, @" {2,}", " ").Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    /// <summary>Teléfono válido para WhatsApp (≥ 8 dígitos y no todo ceros) o vacío.</summary>
    public static string NormalizarTelefono(string? valor)
    {
        var t = (valor ?? string.Empty).Trim();
        var digitos = new string(t.Where(char.IsDigit).ToArray());
        if (digitos.Length < 8 || digitos.All(c => c == '0')) return string.Empty;
        return (t.StartsWith('+') ? "+" : string.Empty) + digitos;
    }

    /// <summary>Cantidad de variables {{N}} del cuerpo de una plantilla de WhatsApp.</summary>
    public static int ContarVariablesPlantilla(string cuerpo)
        => Regex.Matches(cuerpo ?? string.Empty, @"\{\{(\d+)\}\}").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();

    /// <summary>Cuerpo de la plantilla con las variables ya reemplazadas (vista previa).</summary>
    public static string RellenarPlantilla(string cuerpo, IReadOnlyList<string> valores)
        => Regex.Replace(cuerpo ?? string.Empty, @"\{\{(\d+)\}\}", m =>
        {
            var i = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
            return i >= 0 && i < valores.Count ? valores[i] : m.Value;
        });
}

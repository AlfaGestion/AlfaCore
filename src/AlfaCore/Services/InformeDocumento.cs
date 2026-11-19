using System.Globalization;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Documento HTML completo de un informe al cliente, para el PDF (Playwright) y como base del mail.
/// Mismo aspecto que la página pública /{idweb}/informe/{token}, pensado para A4.
/// </summary>
public static class InformeDocumento
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    public static string Html(TicketInformePublicoDto i)
    {
        string E(string? s) => InformeHtmlSanitizer.Codificar(s);
        var fecha = (i.FechaHoraModificacion ?? i.FechaHoraPublicacion ?? DateTime.Now).ToString("dd/MM/yyyy", Ar);
        var cliente = string.IsNullOrWhiteSpace(i.Cliente) ? "" : $"<div><dt>Cliente</dt><dd>{E(i.Cliente)}</dd></div>";
        return $$"""
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8"><title>{{E(i.Titulo)}}</title>
            <style>
              @page { size: A4; margin: 16mm 14mm 18mm; }
              body { margin: 0; color: #1d2430; font: 10.5pt/1.55 "Segoe UI", Arial, sans-serif; }
              .marca { font-size: 8pt; letter-spacing: .08em; text-transform: uppercase; color: #1f5fa8; font-weight: 600; }
              h1 { font-size: 18pt; line-height: 1.25; margin: 4px 0 8px; }
              dl { display: flex; flex-wrap: wrap; gap: 4px 24px; margin: 0 0 14px; padding-bottom: 12px; border-bottom: 1px solid #dfe3ea; }
              dt { font-size: 8pt; color: #5b6574; } dd { margin: 0; font-weight: 600; }
              h2 { font-size: 13pt; margin: 16px 0 6px; } h3 { font-size: 11.5pt; margin: 12px 0 4px; }
              p { margin: 0 0 8px; } ul, ol { margin: 0 0 8px; padding-left: 20px; } li { margin: 2px 0; }
              table { width: 100%; border-collapse: collapse; font-size: 9pt; margin: 6px 0 10px; page-break-inside: auto; }
              tr { page-break-inside: avoid; }
              th, td { border-bottom: 1px solid #dfe3ea; padding: 5px 6px; text-align: left; vertical-align: top; }
              th { color: #5b6574; font-size: 7.5pt; text-transform: uppercase; letter-spacing: .04em; }
              .num { text-align: right; white-space: nowrap; }
              blockquote { border-left: 3px solid #1f5fa8; margin: 0 0 8px; padding: 2px 10px; color: #3b4555; }
              hr { border: 0; border-top: 1px solid #dfe3ea; margin: 12px 0; }
              .ticket-editor-banner { padding: 8px 12px; border-radius: 6px; background: #e8f0fb; margin: 0 0 10px; page-break-inside: avoid; }
              .ticket-editor-banner--success { background: #e7f4ec; } .ticket-editor-banner--warning { background: #fdf1dd; } .ticket-editor-banner--danger { background: #fbe7e7; }
              .ticket-editor-banner__icon { display: none; }
              .ticket-editor-banner p:last-child { margin: 0; }
              .pie { margin-top: 18px; padding-top: 8px; border-top: 1px solid #dfe3ea; color: #5b6574; font-size: 8.5pt; }
            </style></head>
            <body>
              <div class="marca">Alfa Gestión · Soporte</div>
              <h1>{{E(i.Titulo)}}</h1>
              <dl>{{cliente}}<div><dt>Ticket</dt><dd>#{{i.NumeroTicket}} · {{E(i.TituloTicket)}}</dd></div><div><dt>Fecha</dt><dd>{{fecha}}</dd></div></dl>
              {{InformeHtmlSanitizer.Limpiar(i.Html)}}
              <div class="pie">Alfa Gestión · Soporte. Ante cualquier duda sobre este informe, respondanos por el canal habitual.</div>
            </body></html>
            """;
    }

    /// <summary>Cuerpo del mail: el mensaje de quien envía, un botón al informe y una aclaración del adjunto.</summary>
    public static string CuerpoMail(string mensaje, string link, string titulo, bool conPdf)
    {
        var parrafos = InformeHtmlSanitizer.TextoAHtml(mensaje);
        var adjunto = conPdf ? "<p style=\"color:#5b6574;font-size:13px\">También va adjunto en PDF.</p>" : "";
        return $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;font-size:15px;line-height:1.55;color:#1d2430;max-width:620px">
              {parrafos}
              <p style="margin:22px 0"><a href="{InformeHtmlSanitizer.Codificar(link)}" style="background:#1f5fa8;color:#fff;text-decoration:none;padding:11px 18px;border-radius:6px;display:inline-block">Ver el informe: {InformeHtmlSanitizer.Codificar(titulo)}</a></p>
              {adjunto}
              <p style="color:#5b6574;font-size:13px">Alfa Gestión · Soporte</p>
            </div>
            """;
    }

    /// <summary>Número para wa.me: solo dígitos; los de 10 dígitos de Argentina se completan con 549.</summary>
    public static string TelefonoWhatsApp(string? telefono)
    {
        var d = new string((telefono ?? string.Empty).Where(char.IsDigit).ToArray());
        if (d.StartsWith("00", StringComparison.Ordinal)) d = d[2..];
        if (d.StartsWith('0')) d = d[1..];
        if (d.Length == 10) d = "549" + d;
        else if (d.Length == 12 && d.StartsWith("54", StringComparison.Ordinal) && d[2] != '9') d = "549" + d[2..];
        return d;
    }

    public static string LinkWhatsApp(string? telefono, string texto)
    {
        var numero = TelefonoWhatsApp(telefono);
        return $"https://wa.me/{numero}?text={Uri.EscapeDataString(texto ?? string.Empty)}";
    }
}

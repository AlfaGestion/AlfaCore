using System.Globalization;
using System.Net;
using System.Text;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class DocumentRenderer : IDocumentRenderer
{
    private static readonly CultureInfo CulturaAr = CultureInfo.GetCultureInfo("es-AR");

    public string RenderCotizacion(DocumentTemplateDefinition template, CotizacionDocumentData data, string? cssCustom = null, string? themeKey = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);
        var paper = template.Paper;
        var (coverWidthMm, coverHeightMm) = PageDimensionsMm(paper);
        var theme = DocumentThemePresets.Resolve(themeKey);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Cotización ")
            .Append(E(data.Comprobante.Numero)).Append("</title><style>")
            .Append("@page{size:").Append(paper.Size).Append(' ').Append(paper.Orientation.ToLowerInvariant())
            .Append(";margin:").Append(Mm(paper.MarginTopMm)).Append(' ').Append(Mm(paper.MarginRightMm)).Append(' ')
            .Append(Mm(paper.MarginBottomMm)).Append(' ').Append(Mm(paper.MarginLeftMm)).Append(";}*")
            .Append("{box-sizing:border-box}body{font-family:Arial,sans-serif;color:").Append(theme.ColorTexto).Append(";font-size:10pt;margin:0}.doc{width:100%}")
            .Append(".header{border-bottom:2px solid ").Append(theme.ColorSecundario).Append(";padding-bottom:5mm;margin-bottom:5mm;display:flex;gap:8mm;align-items:flex-start}")
            .Append(".header--split{border:1px solid #d5dde5;border-bottom:2px solid ").Append(theme.ColorSecundario).Append(";border-radius:2mm;padding:5mm 6mm}")
            .Append("@page cover-page{margin:0}.cover-page{page:cover-page;break-after:page;width:").Append(Mm(coverWidthMm)).Append(";height:").Append(Mm(coverHeightMm)).Append(";overflow:hidden}.cover-page img{width:100%;height:100%;object-fit:cover;display:block}")
            .Append(".logo{max-height:35mm;object-fit:contain}.company{flex:1}.company h1{font-size:18pt;margin:0 0 2mm}.doc-meta{text-align:right;min-width:45mm}")
            .Append(".header--split .doc-meta{text-align:right}.header--split .logo-side{display:flex;align-items:center}")
            .Append(".card{border:1px solid #d5dde5;background:").Append(theme.ColorFondoSuave).Append(";padding:4mm;margin:0 0 5mm}")
            .Append(".items{width:100%;border-collapse:collapse;margin:4mm 0}.items th{background:").Append(theme.ColorPrimario).Append(";color:white;padding:2.5mm;text-align:left}")
            .Append(".items td{padding:2.3mm;border-bottom:1px solid #dce3e9;vertical-align:top}.right{text-align:right}.totals{margin-left:auto;width:65mm;margin-top:5mm}")
            .Append(".totals td{padding:1.4mm 0}.total-final{font-size:14pt;font-weight:700;border-top:2px solid ").Append(theme.ColorPrimario).Append("}")
            .Append(".muted{color:#596579;font-size:9pt}.proposal{margin-top:6mm}.proposal img{max-width:100%}.section-title{font-size:11pt;font-weight:700;margin:5mm 0 2mm;color:").Append(theme.ColorPrimario).Append("}")
            .Append(".signature{margin-top:14mm}.signature img{max-height:22mm;object-fit:contain;display:block;margin:3mm 0}")
            .Append(SafeCss(cssCustom)).Append("</style></head><body><main class=\"doc\">");

        var hasCompanyBlock = template.Blocks.Any(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.Empresa, StringComparison.OrdinalIgnoreCase));
        var logoBlock = template.Blocks.FirstOrDefault(x => x.Type.Equals(TiposBloqueDocumento.Logo, StringComparison.OrdinalIgnoreCase));
        var combinedLogo = logoBlock is { Visible: true, CombineWithCompany: true } ? logoBlock : null;
        foreach (var block in template.Blocks.Where(x => x.Visible))
            AppendBlock(sb, block, data, hasCompanyBlock, combinedLogo, theme);

        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static void AppendBlock(StringBuilder sb, DocumentBlockDefinition block, CotizacionDocumentData data, bool hasCompanyBlock, DocumentBlockDefinition? combinedLogo, DocumentThemePreset theme)
    {
        var fields = block.VisibleFields;
        var style = FontStyle(block.FontSizePt);
        switch (block.Type.ToUpperInvariant())
        {
            case "PORTADA":
                // Requiere las dos cosas: que la plantilla tenga portada configurada Y que ESTA
                // versión puntual la quiera incluir (COT_VERSION.IncluyePortada).
                if (data.IncluyePortada && data.PortadaBytes is { Length: > 0 })
                    sb.Append("<div class=\"cover-page\"><img src=\"data:image/png;base64,").Append(Convert.ToBase64String(data.PortadaBytes)).Append("\" alt=\"Portada\"></div>");
                break;
            case "LOGO":
                // Si está combinado con Empresa, esta misma imagen se dibuja adentro de esa
                // cabecera -- evita duplicar el logo en dos lugares del documento.
                if (block.CombineWithCompany) break;
                if (data.Empresa.Logo is { Length: > 0 })
                    sb.Append("<div style=\"text-align:").Append(Align(block.Align)).Append(";margin-bottom:4mm\"><img class=\"logo\" style=\"max-width:").Append(block.Width is > 0 ? block.Width.Value.ToString("0", CultureInfo.InvariantCulture) : "120").Append("px\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(data.Empresa.Logo)).Append("\" alt=\"Logo\"></div>");
                break;
            case "EMPRESA":
                var logoOnRight = combinedLogo is not null && Align(combinedLogo.Align) == "right";
                sb.Append("<section class=\"header").Append(combinedLogo is not null ? " header--split" : string.Empty).Append('"').Append(style).Append('>');
                if (combinedLogo is not null && !logoOnRight) AppendLogoSide(sb, combinedLogo, data.Empresa.Logo);
                sb.Append("<div class=\"company\">");
                if (Shows(fields, "Nombre")) sb.Append("<h1>").Append(E(data.Empresa.Nombre)).Append("</h1>");
                var empresaValues = new List<string>();
                if (Shows(fields, "Cuit")) AddIf(empresaValues, data.Empresa.Cuit);
                if (Shows(fields, "Domicilio")) AddIf(empresaValues, data.Empresa.Domicilio);
                if (Shows(fields, "Telefono")) AddIf(empresaValues, data.Empresa.Telefono);
                if (Shows(fields, "Email")) AddIf(empresaValues, data.Empresa.Email);
                AppendMuted(sb, empresaValues);
                sb.Append("</div>");
                AppendDocumentMeta(sb, data.Comprobante, hasCompanyBlock ? fields : null);
                if (combinedLogo is not null && logoOnRight) AppendLogoSide(sb, combinedLogo, data.Empresa.Logo);
                sb.Append("</section>");
                break;
            case "COMPROBANTE":
                // Ya forma parte del encabezado para evitar duplicar información en el formato estándar.
                if (!hasCompanyBlock) AppendDocumentMeta(sb, data.Comprobante, fields);
                break;
            case "CLIENTE":
                sb.Append("<section class=\"card\"").Append(style).Append("><strong>Cliente</strong><br>");
                if (Shows(fields, "RazonSocial")) sb.Append("<b>").Append(E(data.Cliente.RazonSocial)).Append("</b>");
                if (Shows(fields, "Codigo") && !string.IsNullOrWhiteSpace(data.Cliente.Codigo)) sb.Append(" <span class=\"muted\">(").Append(E(data.Cliente.Codigo)).Append(")</span>");
                var clienteValues = new List<string>();
                if (Shows(fields, "Cuit")) AddIf(clienteValues, data.Cliente.Cuit);
                if (Shows(fields, "Domicilio")) AddIf(clienteValues, data.Cliente.Domicilio);
                if (Shows(fields, "Telefono")) AddIf(clienteValues, data.Cliente.Telefono);
                if (Shows(fields, "Email")) AddIf(clienteValues, data.Cliente.Email);
                AppendMuted(sb, clienteValues);
                sb.Append("</section>");
                break;
            case "ITEMS": AppendItems(sb, block, data.Items); break;
            case "TOTALES": AppendTotals(sb, data.Totales, data.Comprobante.Moneda, fields, style); break;
            case "PROPUESTA":
                if (!string.IsNullOrWhiteSpace(data.PropuestaHtml))
                    sb.Append("<section class=\"proposal\"").Append(style).Append('>').Append(SafeInnerHtml(data.PropuestaHtml)).Append("</section>");
                break;
            case "FIRMA":
                sb.Append("<section class=\"signature\">Atentamente,");
                if (data.FirmaBytes is { Length: > 0 })
                    sb.Append("<img src=\"data:image/png;base64,").Append(Convert.ToBase64String(data.FirmaBytes)).Append("\" alt=\"Firma\">");
                if (!string.IsNullOrWhiteSpace(data.FirmanteNombre))
                    sb.Append("<strong>").Append(E(data.FirmanteNombre)).Append("</strong>");
                sb.Append("</section>");
                break;
        }
    }

    private static void AppendLogoSide(StringBuilder sb, DocumentBlockDefinition logoBlock, byte[]? logo)
    {
        if (logo is not { Length: > 0 }) return;
        sb.Append("<div class=\"logo-side\"><img class=\"logo\" style=\"max-width:").Append(logoBlock.Width is > 0 ? logoBlock.Width.Value.ToString("0", CultureInfo.InvariantCulture) : "120").Append("px\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(logo)).Append("\" alt=\"Logo\"></div>");
    }

    private static void AppendDocumentMeta(StringBuilder sb, ComprobanteDocumentData document, List<string>? fields)
    {
        sb.Append("<div class=\"doc-meta\">");
        if (Shows(fields, "Numero")) sb.Append("<b>COTIZACIÓN ").Append(E(document.Numero)).Append("</b><br>");
        if (Shows(fields, "Fecha")) sb.Append("<span class=\"muted\">Fecha: ").Append(document.Fecha.ToString("dd/MM/yyyy", CulturaAr)).Append("</span>");
        if (Shows(fields, "Vencimiento") && document.FechaVencimiento is { } due) sb.Append("<br><span class=\"muted\">Vence: ").Append(due.ToString("dd/MM/yyyy", CulturaAr)).Append("</span>");
        if (Shows(fields, "Moneda") && !string.IsNullOrWhiteSpace(document.Moneda)) sb.Append("<br><span class=\"muted\">Moneda: ").Append(E(document.Moneda)).Append("</span>");
        sb.Append("</div>");
    }

    private static void AppendMuted(StringBuilder sb, List<string> values)
    {
        if (values.Count > 0) sb.Append("<div class=\"muted\">").Append(string.Join("<br>", values.Select(E))).Append("</div>");
    }

    private static void AddIf(List<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
    }

    private static void AppendItems(StringBuilder sb, DocumentBlockDefinition block, IReadOnlyList<CotizacionDocumentItemData> items)
    {
        var columns = block.Columns.Where(x => x.Visible).ToList();
        if (columns.Count == 0) return;
        sb.Append("<table class=\"items\"").Append(FontStyle(block.FontSizePt)).Append("><colgroup>");
        foreach (var column in columns) sb.Append("<col style=\"width:").Append(column.WidthPercent.ToString("0.##", CultureInfo.InvariantCulture)).Append("%\">");
        sb.Append("</colgroup><thead><tr>");
        foreach (var column in columns) sb.Append("<th class=\"").Append(AlignClass(column.Align)).Append("\">").Append(E(column.Title)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var item in items)
        {
            sb.Append("<tr>");
            foreach (var column in columns) sb.Append("<td class=\"").Append(AlignClass(column.Align)).Append("\">").Append(ItemValue(item, column.Field)).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendTotals(StringBuilder sb, TotalesDocumentData totals, string currency, List<string>? fields, string style)
    {
        sb.Append("<table class=\"totals\"").Append(style).Append("><tbody>");
        if (Shows(fields, "Neto")) sb.Append("<tr><td>Neto</td><td class=\"right\">").Append(Money(totals.Neto, currency)).Append("</td></tr>");
        if (Shows(fields, "Descuento") && totals.Descuento != 0) sb.Append("<tr><td>Descuento</td><td class=\"right\">-").Append(Money(totals.Descuento, currency)).Append("</td></tr>");
        if (Shows(fields, "Impuestos") && totals.Impuestos != 0) sb.Append("<tr><td>Impuestos</td><td class=\"right\">").Append(Money(totals.Impuestos, currency)).Append("</td></tr>");
        if (Shows(fields, "Total")) sb.Append("<tr class=\"total-final\"><td>Total</td><td class=\"right\">").Append(Money(totals.Total, currency)).Append("</td></tr>");
        sb.Append("</tbody></table>");
    }

    private static string ItemValue(CotizacionDocumentItemData item, string field) => field.ToUpperInvariant() switch
    {
        "CODIGO" => E(item.Codigo), "DESCRIPCION" => E(item.Descripcion), "CANTIDAD" => item.ImpactaTotal ? item.Cantidad.ToString("N2", CulturaAr) : string.Empty,
        "PRECIO" => item.ImpactaTotal ? item.Precio.ToString("N2", CulturaAr) : string.Empty,
        "DESCUENTO" => item.ImpactaTotal ? item.Descuento.ToString("N2", CulturaAr) : string.Empty,
        "TOTAL" => item.ImpactaTotal ? item.Total.ToString("N2", CulturaAr) : string.Empty, _ => string.Empty
    };

    /// <summary>True cuando el campo no está restringido (VisibleFields null = "todos") o está
    /// explícitamente incluido -- backward-compatible con plantillas guardadas antes de este campo.</summary>
    private static bool Shows(List<string>? fields, string field)
        => fields is null || fields.Contains(field, StringComparer.OrdinalIgnoreCase);

    private static string FontStyle(decimal? fontSizePt)
        => fontSizePt is > 0 ? $" style=\"font-size:{fontSizePt.Value.ToString("0.#", CultureInfo.InvariantCulture)}pt\"" : string.Empty;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Align(string? value) => value?.ToLowerInvariant() is "center" or "right" ? value.ToLowerInvariant() : "left";
    private static string AlignClass(string? value) => Align(value) == "right" ? "right" : string.Empty;
    private static string Mm(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "mm";

    private static (decimal Width, decimal Height) PageDimensionsMm(DocumentPaperDefinition paper)
    {
        var (w, h) = paper.Size.Equals("A5", StringComparison.OrdinalIgnoreCase) ? (148m, 210m) : (210m, 297m);
        return paper.Orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase) ? (h, w) : (w, h);
    }
    private static string Money(decimal value, string currency) => E(value.ToString("N2", CulturaAr) + (string.IsNullOrWhiteSpace(currency) ? string.Empty : " " + currency));
    private static string SafeCss(string? css) => string.IsNullOrWhiteSpace(css) ? string.Empty : css.Replace("</style", string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>El HTML de Propuesta ya pasó por el editor de bloques (tickets-rich-editor.js), que
    /// sanea tags/atributos al guardar -- acá solo se corta cualquier intento de cerrar prematuramente
    /// el contenedor, igual criterio que SafeCss.</summary>
    private static string SafeInnerHtml(string html) => html.Replace("</main", string.Empty, StringComparison.OrdinalIgnoreCase);
}

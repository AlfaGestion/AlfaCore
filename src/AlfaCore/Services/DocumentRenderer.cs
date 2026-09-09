using System.Globalization;
using System.Net;
using System.Text;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class DocumentRenderer : IDocumentRenderer
{
    private static readonly CultureInfo CulturaAr = CultureInfo.GetCultureInfo("es-AR");

    public string RenderCotizacion(DocumentTemplateDefinition template, CotizacionDocumentData data, string? cssCustom = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);
        var paper = template.Paper;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Cotización ")
            .Append(E(data.Comprobante.Numero)).Append("</title><style>")
            .Append("@page{size:").Append(paper.Size).Append(' ').Append(paper.Orientation.ToLowerInvariant())
            .Append(";margin:").Append(Mm(paper.MarginTopMm)).Append(' ').Append(Mm(paper.MarginRightMm)).Append(' ')
            .Append(Mm(paper.MarginBottomMm)).Append(' ').Append(Mm(paper.MarginLeftMm)).Append(";}*")
            .Append("{box-sizing:border-box}body{font-family:Arial,sans-serif;color:#172033;font-size:10pt;margin:0}.doc{width:100%}.header{border-bottom:2px solid #168da0;padding-bottom:5mm;margin-bottom:5mm;display:flex;gap:8mm;align-items:flex-start}.logo{max-height:35mm;object-fit:contain}.company{flex:1}.company h1{font-size:18pt;margin:0 0 2mm}.doc-meta{text-align:right;min-width:45mm}.card{border:1px solid #d5dde5;background:#f8fafc;padding:4mm;margin:0 0 5mm}.items{width:100%;border-collapse:collapse;margin:4mm 0}.items th{background:#123a63;color:white;padding:2.5mm;text-align:left}.items td{padding:2.3mm;border-bottom:1px solid #dce3e9;vertical-align:top}.right{text-align:right}.totals{margin-left:auto;width:65mm;margin-top:5mm}.totals td{padding:1.4mm 0}.total-final{font-size:14pt;font-weight:700;border-top:2px solid #123a63}.muted{color:#596579;font-size:9pt}.observations{margin-top:6mm;white-space:pre-line}.section-title{font-size:11pt;font-weight:700;margin:5mm 0 2mm}")
            .Append(SafeCss(cssCustom)).Append("</style></head><body><main class=\"doc\">");

        var hasCompanyBlock = template.Blocks.Any(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.Empresa, StringComparison.OrdinalIgnoreCase));
        foreach (var block in template.Blocks.Where(x => x.Visible))
            AppendBlock(sb, block, data, hasCompanyBlock);

        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static void AppendBlock(StringBuilder sb, DocumentBlockDefinition block, CotizacionDocumentData data, bool hasCompanyBlock)
    {
        switch (block.Type.ToUpperInvariant())
        {
            case "LOGO":
                if (data.Empresa.Logo is { Length: > 0 })
                    sb.Append("<div style=\"text-align:").Append(Align(block.Align)).Append(";margin-bottom:4mm\"><img class=\"logo\" style=\"max-width:").Append(block.Width is > 0 ? block.Width.Value.ToString("0", CultureInfo.InvariantCulture) : "120").Append("px\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(data.Empresa.Logo)).Append("\" alt=\"Logo\"></div>");
                break;
            case "EMPRESA":
                sb.Append("<section class=\"header\"><div class=\"company\"><h1>").Append(E(data.Empresa.Nombre)).Append("</h1>");
                AppendContact(sb, data.Empresa.Cuit, data.Empresa.Domicilio, data.Empresa.Telefono, data.Empresa.Email);
                sb.Append("</div>");
                AppendDocumentMeta(sb, data.Comprobante);
                sb.Append("</section>");
                break;
            case "COMPROBANTE":
                // Ya forma parte del encabezado para evitar duplicar información en el formato estándar.
                if (!hasCompanyBlock) AppendDocumentMeta(sb, data.Comprobante);
                break;
            case "CLIENTE":
                sb.Append("<section class=\"card\"><strong>Cliente</strong><br><b>").Append(E(data.Cliente.RazonSocial)).Append("</b>");
                if (!string.IsNullOrWhiteSpace(data.Cliente.Codigo)) sb.Append(" <span class=\"muted\">(").Append(E(data.Cliente.Codigo)).Append(")</span>");
                AppendContact(sb, data.Cliente.Cuit, data.Cliente.Domicilio, data.Cliente.Telefono, data.Cliente.Email);
                sb.Append("</section>");
                break;
            case "ITEMS": AppendItems(sb, block, data.Items); break;
            case "TOTALES": AppendTotals(sb, data.Totales, data.Comprobante.Moneda); break;
            case "OBSERVACIONES":
                if (!string.IsNullOrWhiteSpace(data.Observaciones)) sb.Append("<section class=\"observations\"><strong>Observaciones</strong><br>").Append(E(data.Observaciones)).Append("</section>");
                break;
        }
    }

    private static void AppendDocumentMeta(StringBuilder sb, ComprobanteDocumentData document)
    {
        sb.Append("<div class=\"doc-meta\"><b>COTIZACIÓN ").Append(E(document.Numero)).Append("</b><br><span class=\"muted\">Fecha: ").Append(document.Fecha.ToString("dd/MM/yyyy", CulturaAr)).Append("</span>");
        if (document.FechaVencimiento is { } due) sb.Append("<br><span class=\"muted\">Vence: ").Append(due.ToString("dd/MM/yyyy", CulturaAr)).Append("</span>");
        if (!string.IsNullOrWhiteSpace(document.Moneda)) sb.Append("<br><span class=\"muted\">Moneda: ").Append(E(document.Moneda)).Append("</span>");
        sb.Append("</div>");
    }

    private static void AppendContact(StringBuilder sb, string cuit, string address, string phone, string email)
    {
        var values = new[] { cuit, address, phone, email }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(E).ToArray();
        if (values.Length > 0) sb.Append("<div class=\"muted\">").Append(string.Join("<br>", values)).Append("</div>");
    }

    private static void AppendItems(StringBuilder sb, DocumentBlockDefinition block, IReadOnlyList<CotizacionDocumentItemData> items)
    {
        var columns = block.Columns.Where(x => x.Visible).ToList();
        if (columns.Count == 0) return;
        sb.Append("<table class=\"items\"><colgroup>");
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

    private static void AppendTotals(StringBuilder sb, TotalesDocumentData totals, string currency)
    {
        sb.Append("<table class=\"totals\"><tbody><tr><td>Neto</td><td class=\"right\">").Append(Money(totals.Neto, currency)).Append("</td></tr>");
        if (totals.Descuento != 0) sb.Append("<tr><td>Descuento</td><td class=\"right\">-").Append(Money(totals.Descuento, currency)).Append("</td></tr>");
        if (totals.Impuestos != 0) sb.Append("<tr><td>Impuestos</td><td class=\"right\">").Append(Money(totals.Impuestos, currency)).Append("</td></tr>");
        sb.Append("<tr class=\"total-final\"><td>Total</td><td class=\"right\">").Append(Money(totals.Total, currency)).Append("</td></tr></tbody></table>");
    }

    private static string ItemValue(CotizacionDocumentItemData item, string field) => field.ToUpperInvariant() switch
    {
        "CODIGO" => E(item.Codigo), "DESCRIPCION" => E(item.Descripcion), "CANTIDAD" => item.ImpactaTotal ? item.Cantidad.ToString("N2", CulturaAr) : string.Empty,
        "PRECIO" => item.ImpactaTotal ? item.Precio.ToString("N2", CulturaAr) : string.Empty,
        "DESCUENTO" => item.ImpactaTotal ? item.Descuento.ToString("N2", CulturaAr) : string.Empty,
        "TOTAL" => item.ImpactaTotal ? item.Total.ToString("N2", CulturaAr) : string.Empty, _ => string.Empty
    };
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Align(string? value) => value?.ToLowerInvariant() is "center" or "right" ? value.ToLowerInvariant() : "left";
    private static string AlignClass(string? value) => Align(value) == "right" ? "right" : string.Empty;
    private static string Mm(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "mm";
    private static string Money(decimal value, string currency) => E(value.ToString("N2", CulturaAr) + (string.IsNullOrWhiteSpace(currency) ? string.Empty : " " + currency));
    private static string SafeCss(string? css) => string.IsNullOrWhiteSpace(css) ? string.Empty : css.Replace("</style", string.Empty, StringComparison.OrdinalIgnoreCase);
}

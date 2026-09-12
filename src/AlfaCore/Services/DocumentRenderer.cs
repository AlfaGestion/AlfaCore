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
        var theme = DocumentThemePresets.Resolve(themeKey);
        var sb = new StringBuilder();
        var hasFooter = template.Blocks.Any(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.Pie, StringComparison.OrdinalIgnoreCase));
        // Márgenes reales de página, no padding: el padding de un contenedor que se fragmenta en
        // varias hojas impresas SOLO se aplica en el primer/último fragmento (no antes de cada
        // salto de página intermedio) -- por eso la versión anterior de este fix dejaba el texto
        // pegado al borde justo en los saltos de página del medio del documento. El margen real de
        // @page sí se repite en cada hoja. Se usan dos páginas CSS con nombre: "cover" (margen 0,
        // para que la portada ocupe la hoja completa) y "content" (el margen de papel configurado,
        // más espacio extra abajo si hay pie de página) -- nunca una @page sin nombre, para no
        // depender de cómo Chromium propaga el nombre de página entre hermanos sin "page" propio.
        AppendDocumentHead(sb, "Cotización " + data.Comprobante.Numero, paper, theme, hasFooter, cssCustom, string.Empty);

        var hasCompanyBlock = template.Blocks.Any(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.Empresa, StringComparison.OrdinalIgnoreCase));
        var logoBlock = template.Blocks.FirstOrDefault(x => x.Type.Equals(TiposBloqueDocumento.Logo, StringComparison.OrdinalIgnoreCase));
        var combinedLogo = logoBlock is { Visible: true, CombineWithCompany: true } ? logoBlock : null;

        var coverBlock = template.Blocks.FirstOrDefault(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.Portada, StringComparison.OrdinalIgnoreCase));
        if (coverBlock is not null)
            AppendBlock(sb, coverBlock, data, hasCompanyBlock, combinedLogo, theme);

        sb.Append("<div class=\"doc-content\">");
        foreach (var block in template.Blocks.Where(x => x.Visible && !x.Type.Equals(TiposBloqueDocumento.Portada, StringComparison.OrdinalIgnoreCase)))
            AppendBlock(sb, block, data, hasCompanyBlock, combinedLogo, theme);
        sb.Append("</div>");

        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    public string RenderFactura(DocumentTemplateDefinition template, FacturaDocumentData data, string? cssCustom = null, string? themeKey = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);
        var paper = template.Paper;
        var theme = DocumentThemePresets.Resolve(themeKey);
        var sb = new StringBuilder();
        var hasFooter = template.Blocks.Any(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.Pie, StringComparison.OrdinalIgnoreCase));
        var titulo = $"Factura {data.Comprobante.Letra} {data.Comprobante.PuntoVenta}-{data.Comprobante.Numero}";
        AppendDocumentHead(sb, titulo, paper, theme, hasFooter, cssCustom, FacturaExtraCss(theme));

        var hasCompanyBlock = template.Blocks.Any(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.Empresa, StringComparison.OrdinalIgnoreCase));
        var logoBlock = template.Blocks.FirstOrDefault(x => x.Type.Equals(TiposBloqueDocumento.Logo, StringComparison.OrdinalIgnoreCase));
        var combinedLogo = logoBlock is { Visible: true, CombineWithCompany: true } ? logoBlock : null;
        var recuadroBlock = template.Blocks.FirstOrDefault(x => x.Visible && x.Type.Equals(TiposBloqueDocumento.RecuadroTipo, StringComparison.OrdinalIgnoreCase));

        if (data.Cae is { Resultado.Length: > 0 } cae && !string.Equals(cae.Resultado, "A", StringComparison.OrdinalIgnoreCase))
            sb.Append("<div class=\"afip-warning\">Comprobante con observaciones de AFIP")
              .Append(string.IsNullOrWhiteSpace(cae.Motivo) ? string.Empty : ": " + E(cae.Motivo))
              .Append("</div>");

        sb.Append("<div class=\"doc-content\">");
        foreach (var block in template.Blocks.Where(x => x.Visible))
            AppendBlockFactura(sb, block, data, hasCompanyBlock, combinedLogo, recuadroBlock);
        sb.Append("</div>");

        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    /// <summary>CSS base compartida entre Cotización y Factura (@page, header, card, items, totals).
    /// Cada Render* agrega su propio título y CSS extra (extraCss) antes de cssCustom.</summary>
    private static void AppendDocumentHead(StringBuilder sb, string titulo, DocumentPaperDefinition paper, DocumentThemePreset theme, bool hasFooter, string? cssCustom, string extraCss)
    {
        var (coverWidthMm, coverHeightMm) = PageDimensionsMm(paper);
        var bottomContentMm = hasFooter ? Math.Max(paper.MarginBottomMm, 14m) : paper.MarginBottomMm;
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>")
            .Append(E(titulo)).Append("</title><style>")
            .Append("@page cover{size:").Append(paper.Size).Append(' ').Append(paper.Orientation.ToLowerInvariant()).Append(";margin:0}")
            .Append("@page content{size:").Append(paper.Size).Append(' ').Append(paper.Orientation.ToLowerInvariant()).Append(";margin:")
            .Append(Mm(paper.MarginTopMm)).Append(' ').Append(Mm(paper.MarginRightMm)).Append(' ').Append(Mm(bottomContentMm)).Append(' ').Append(Mm(paper.MarginLeftMm)).Append('}')
            .Append('*').Append("{box-sizing:border-box}html,body{margin:0;padding:0}body{font-family:Arial,sans-serif;color:").Append(theme.ColorTexto).Append(";font-size:10pt}.doc{width:100%}")
            .Append(".doc-content{page:content}")
            .Append(".header{border-bottom:2px solid ").Append(theme.ColorSecundario).Append(";padding-bottom:5mm;margin-bottom:5mm;display:flex;gap:8mm;align-items:flex-start}")
            .Append(".header--split{border:1px solid #d5dde5;border-bottom:2px solid ").Append(theme.ColorSecundario).Append(";border-radius:2mm;padding:5mm 6mm}")
            .Append(".cover-page{page:cover;break-after:page;width:").Append(Mm(coverWidthMm)).Append(";height:").Append(Mm(coverHeightMm)).Append(";overflow:hidden}.cover-page img{width:100%;height:100%;object-fit:cover;display:block}")
            .Append(".logo{max-height:35mm;object-fit:contain}.company{flex:1}.company h1{font-size:18pt;margin:0 0 2mm}.doc-meta{text-align:right;min-width:45mm}")
            .Append(".header--split .doc-meta{text-align:right}.header--split .logo-side{display:flex;align-items:center}")
            .Append(".card{border:1px solid #d5dde5;background:").Append(theme.ColorFondoSuave).Append(";padding:4mm;margin:0 0 5mm}")
            .Append(".items{width:100%;border-collapse:collapse;margin:4mm 0}.items th{background:").Append(theme.ColorPrimario).Append(";color:white;padding:2.5mm;text-align:left}")
            .Append(".items td{padding:2.3mm;border-bottom:1px solid #dce3e9;vertical-align:top}.right{text-align:right}.totals{margin-left:auto;width:65mm;margin-top:5mm}")
            .Append(".totals td{padding:1.4mm 0}.total-final{font-size:14pt;font-weight:700;border-top:2px solid ").Append(theme.ColorPrimario).Append("}")
            .Append(".muted{color:#596579;font-size:9pt}.proposal{margin-top:6mm}.proposal img{max-width:100%}.section-title{font-size:11pt;font-weight:700;margin:5mm 0 2mm;color:").Append(theme.ColorPrimario).Append("}")
            // La imagen va en un <div> propio, NUNCA <img style="display:block"> directamente: es
            // un bug real de Chromium confirmado con pruebas aisladas -- un <img> con display:block
            // dentro de un contenedor con página CSS con nombre (page:content, ver .doc-content
            // arriba) que tiene al menos un hermano ANTERIOR se manda entero a la hoja siguiente en
            // blanco, sin importar cuánto espacio sobre en la hoja actual (reproducido con 0mm, 10mm,
            // 100mm y 200mm de contenido previo: siempre salta). Envolviendo la imagen en un div de
            // bloque y dejando el <img> en su display inline por defecto, el bug no se dispara.
            .Append(".signature{margin-top:10mm;break-inside:avoid}.signature-image{margin:3mm 0}.signature-image img{height:20mm;width:55mm;object-fit:contain;object-position:left center}")
            .Append(extraCss)
            .Append(SafeCss(cssCustom)).Append("</style></head><body><main class=\"doc\">");
    }

    private static string FacturaExtraCss(DocumentThemePreset theme)
        => ".afip-warning{background:#fef3c7;color:#92400e;border:1px solid #f59e0b;border-radius:2mm;padding:2.5mm 4mm;margin-bottom:4mm;font-size:9pt;font-weight:700}"
         + ".afip-box{border:1.5pt solid #172033;text-align:center;width:22mm;margin:0 4mm}"
         + ".afip-box .letra{font-size:22pt;font-weight:700;line-height:1.1;padding:1mm 0}"
         + ".afip-box .codigo{border-top:1pt solid #172033;font-size:8pt;padding:0.8mm 0}"
         + ".header--afip{align-items:flex-start}"
         + ".cae-box{border:1px solid #d5dde5;background:" + theme.ColorFondoSuave + ";padding:3mm 4mm;margin-top:4mm;font-size:9pt}"
         + ".qr-box{margin-top:3mm}.qr-box img{width:28mm;height:28mm}"
         + ".totals td.iva-label{color:#596579}";

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
                    sb.Append("<div class=\"signature-image\"><img src=\"data:image/png;base64,").Append(Convert.ToBase64String(data.FirmaBytes)).Append("\" alt=\"Firma\"></div>");
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

    // ---- Factura A/B/C -----------------------------------------------------------------------

    private static void AppendBlockFactura(StringBuilder sb, DocumentBlockDefinition block, FacturaDocumentData data, bool hasCompanyBlock, DocumentBlockDefinition? combinedLogo, DocumentBlockDefinition? recuadroBlock)
    {
        var fields = block.VisibleFields;
        var style = FontStyle(block.FontSizePt);
        switch (block.Type.ToUpperInvariant())
        {
            case "RECUADROTIPO":
                // Se dibuja adentro de EMPRESA (ver más abajo), junto al resto del encabezado --
                // igual criterio que el Logo combinado de Cotización.
                break;
            case "LOGO":
                if (block.CombineWithCompany) break;
                if (data.Empresa.Logo is { Length: > 0 })
                    sb.Append("<div style=\"text-align:").Append(Align(block.Align)).Append(";margin-bottom:4mm\"><img class=\"logo\" style=\"max-width:").Append(block.Width is > 0 ? block.Width.Value.ToString("0", CultureInfo.InvariantCulture) : "120").Append("px\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(data.Empresa.Logo)).Append("\" alt=\"Logo\"></div>");
                break;
            case "EMPRESA":
                var logoOnRight = combinedLogo is not null && Align(combinedLogo.Align) == "right";
                sb.Append("<section class=\"header header--afip").Append(combinedLogo is not null ? " header--split" : string.Empty).Append('"').Append(style).Append('>');
                if (combinedLogo is not null && !logoOnRight) AppendLogoSide(sb, combinedLogo, data.Empresa.Logo);
                sb.Append("<div class=\"company\">");
                if (Shows(fields, "Nombre")) sb.Append("<h1>").Append(E(data.Empresa.Nombre)).Append("</h1>");
                var empresaValues = new List<string>();
                if (Shows(fields, "Cuit") && !string.IsNullOrWhiteSpace(data.Empresa.Cuit)) empresaValues.Add("CUIT: " + data.Empresa.Cuit);
                if (Shows(fields, "Domicilio")) AddIf(empresaValues, data.Empresa.Domicilio);
                if (Shows(fields, "Telefono")) AddIf(empresaValues, data.Empresa.Telefono);
                if (!string.IsNullOrWhiteSpace(data.CondicionIvaEmisor)) AddIf(empresaValues, data.CondicionIvaEmisor);
                if (!string.IsNullOrWhiteSpace(data.IngresosBrutosEmisor)) empresaValues.Add("Ingresos Brutos: " + data.IngresosBrutosEmisor);
                if (data.InicioActividadesEmisor is { } inicio) empresaValues.Add("Inicio de Actividades: " + inicio.ToString("dd/MM/yyyy", CulturaAr));
                AppendMuted(sb, empresaValues);
                sb.Append("</div>");
                if (recuadroBlock is not null) AppendRecuadroTipo(sb, recuadroBlock, data.Comprobante);
                AppendDocumentMetaFactura(sb, data.Comprobante, hasCompanyBlock ? fields : null);
                if (combinedLogo is not null && logoOnRight) AppendLogoSide(sb, combinedLogo, data.Empresa.Logo);
                sb.Append("</section>");
                break;
            case "COMPROBANTE":
                if (!hasCompanyBlock) AppendDocumentMetaFactura(sb, data.Comprobante, fields);
                break;
            case "CLIENTE":
                sb.Append("<section class=\"card\"").Append(style).Append("><strong>Cliente</strong><br>");
                if (Shows(fields, "RazonSocial")) sb.Append("<b>").Append(E(data.Cliente.RazonSocial)).Append("</b>");
                if (Shows(fields, "Codigo") && !string.IsNullOrWhiteSpace(data.Cliente.Codigo)) sb.Append(" <span class=\"muted\">(").Append(E(data.Cliente.Codigo)).Append(")</span>");
                var clienteValues = new List<string>();
                if (Shows(fields, "Cuit") && !string.IsNullOrWhiteSpace(data.Cliente.DocumentoNumero))
                    clienteValues.Add((string.IsNullOrWhiteSpace(data.Cliente.DocumentoTipoDescripcion) ? "Documento" : data.Cliente.DocumentoTipoDescripcion) + ": " + data.Cliente.DocumentoNumero);
                if (Shows(fields, "CondicionIva")) AddIf(clienteValues, data.Cliente.CondicionIvaDescripcion);
                if (Shows(fields, "Domicilio")) AddIf(clienteValues, string.Join(", ", new[] { data.Cliente.Domicilio, data.Cliente.Localidad }.Where(x => !string.IsNullOrWhiteSpace(x))));
                if (Shows(fields, "Telefono")) AddIf(clienteValues, data.Cliente.Telefono);
                if (Shows(fields, "Email")) AddIf(clienteValues, data.Cliente.Email);
                AppendMuted(sb, clienteValues);
                sb.Append("</section>");
                break;
            case "ITEMS": AppendItemsFactura(sb, block, data.Items); break;
            case "TOTALES": AppendTotalsFactura(sb, data.Totales, data.Comprobante.Letra, fields, style); break;
            case "CAE": AppendCaeBlock(sb, data.Cae, style); break;
            case "QRAFIP": AppendQrAfipBlock(sb, data.QrBytes, block.Align); break;
        }
    }

    private static void AppendRecuadroTipo(StringBuilder sb, DocumentBlockDefinition block, FacturaComprobanteDocumentData comprobante)
    {
        var fields = block.VisibleFields;
        sb.Append("<div class=\"afip-box\">");
        if (Shows(fields, "Letra")) sb.Append("<div class=\"letra\">").Append(E(comprobante.Letra)).Append("</div>");
        if (Shows(fields, "CodigoAfip") && !string.IsNullOrWhiteSpace(comprobante.CodigoAfip)) sb.Append("<div class=\"codigo\">COD. ").Append(E(comprobante.CodigoAfip)).Append("</div>");
        sb.Append("</div>");
    }

    private static void AppendDocumentMetaFactura(StringBuilder sb, FacturaComprobanteDocumentData comprobante, List<string>? fields)
    {
        sb.Append("<div class=\"doc-meta\">");
        sb.Append("<b>FACTURA ").Append(E(comprobante.PuntoVenta)).Append('-').Append(E(comprobante.Numero)).Append("</b><br>");
        sb.Append("<span class=\"muted\">Fecha: ").Append(comprobante.Fecha.ToString("dd/MM/yyyy", CulturaAr)).Append("</span>");
        if (!string.IsNullOrWhiteSpace(comprobante.CondicionVenta)) sb.Append("<br><span class=\"muted\">Cond. venta: ").Append(E(comprobante.CondicionVenta)).Append("</span>");
        sb.Append("</div>");
    }

    private static void AppendItemsFactura(StringBuilder sb, DocumentBlockDefinition block, IReadOnlyList<FacturaDocumentItemData> items)
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
            foreach (var column in columns) sb.Append("<td class=\"").Append(AlignClass(column.Align)).Append("\">").Append(ItemValueFactura(item, column.Field)).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static string ItemValueFactura(FacturaDocumentItemData item, string field) => field.ToUpperInvariant() switch
    {
        "CODIGO" => E(item.Codigo),
        "DESCRIPCION" => E(item.Descripcion),
        "UNIDAD" => E(item.Unidad),
        "CANTIDAD" => item.Cantidad.ToString("N2", CulturaAr),
        "PRECIO" => item.PrecioUnitario.ToString("N2", CulturaAr),
        "DESCUENTO" => item.Bonificacion != 0 ? item.Bonificacion.ToString("N2", CulturaAr) : string.Empty,
        "IVA" => item.AlicuotaIva != 0 ? item.AlicuotaIva.ToString("0.##", CulturaAr) + "%" : string.Empty,
        "TOTAL" => item.Subtotal.ToString("N2", CulturaAr),
        _ => string.Empty
    };

    /// <summary>A discrimina IVA por alícuota en el cuerpo; B/C muestran el total con IVA incluido
    /// sin desglosar (el dato viaja igual en Totales.LineasIva, simplemente no se imprime acá).
    /// Descuentos y percepciones se muestran siempre que existan, sea cual sea la letra.</summary>
    private static void AppendTotalsFactura(StringBuilder sb, FacturaTotalesDocumentData totals, string letra, List<string>? fields, string style)
    {
        var mostrarIva = string.Equals(letra, "A", StringComparison.OrdinalIgnoreCase);
        sb.Append("<table class=\"totals\"").Append(style).Append("><tbody>");
        if (Shows(fields, "Neto"))
        {
            var neto = totals.NetoGravado + totals.NetoNoGravado + totals.ImporteExento;
            sb.Append("<tr><td>Subtotal</td><td class=\"right\">").Append(Money(neto, totals.Moneda)).Append("</td></tr>");
        }
        if (Shows(fields, "Descuento"))
            foreach (var descuento in totals.LineasDescuento.Where(x => x.Importe != 0))
                sb.Append("<tr><td class=\"iva-label\">Descuento ").Append(descuento.Porcentaje.ToString("0.##", CulturaAr)).Append("%</td><td class=\"right\">-").Append(Money(descuento.Importe, totals.Moneda)).Append("</td></tr>");
        if (mostrarIva && Shows(fields, "Impuestos"))
        {
            foreach (var iva in totals.LineasIva.Where(x => x.Importe != 0))
                sb.Append("<tr><td class=\"iva-label\">IVA ").Append(iva.Alicuota.ToString("0.##", CulturaAr)).Append("%</td><td class=\"right\">").Append(Money(iva.Importe, totals.Moneda)).Append("</td></tr>");
            if (totals.ImporteImpuestosInternos != 0)
                sb.Append("<tr><td class=\"iva-label\">Impuestos internos</td><td class=\"right\">").Append(Money(totals.ImporteImpuestosInternos, totals.Moneda)).Append("</td></tr>");
        }
        if (Shows(fields, "Impuestos"))
            foreach (var percepcion in totals.LineasPercepcion.Where(x => x.Importe != 0))
                sb.Append("<tr><td class=\"iva-label\">").Append(E(percepcion.Descripcion)).Append("</td><td class=\"right\">").Append(Money(percepcion.Importe, totals.Moneda)).Append("</td></tr>");
        if (Shows(fields, "Total")) sb.Append("<tr class=\"total-final\"><td>Total</td><td class=\"right\">").Append(Money(totals.Total, totals.Moneda)).Append("</td></tr>");
        sb.Append("</tbody></table>");
    }

    private static void AppendCaeBlock(StringBuilder sb, FacturaCaeDocumentData? cae, string style)
    {
        if (cae is null || string.IsNullOrWhiteSpace(cae.Cae)) return;
        sb.Append("<div class=\"cae-box\"").Append(style).Append('>');
        sb.Append("<b>CAE:</b> ").Append(E(cae.Cae));
        sb.Append(" &nbsp; <b>Vto. CAE:</b> ").Append(cae.VencimientoCae.ToString("dd/MM/yyyy", CulturaAr));
        sb.Append("</div>");
    }

    private static void AppendQrAfipBlock(StringBuilder sb, byte[]? qrBytes, string align)
    {
        if (qrBytes is not { Length: > 0 }) return;
        sb.Append("<div class=\"qr-box\" style=\"text-align:").Append(Align(align)).Append("\"><img src=\"data:image/png;base64,").Append(Convert.ToBase64String(qrBytes)).Append("\" alt=\"QR AFIP\"></div>");
    }
}

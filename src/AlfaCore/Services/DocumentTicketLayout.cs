using System.Globalization;
using System.Net;
using AlfaCore.Models;

namespace AlfaCore.Services;

internal static class DocumentTicketLayout
{
    public static string Apply(string html, DocumentTemplateDefinition template, string empresa)
    {
        var paper = template.Paper;
        static string Mm(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "mm";
        var css = """
            <meta name="alfa-paper" content="Ticket80"><style>
            @page ticket { size:80mm 300mm; margin:0 }
            html,body{margin:0;padding:0}.doc{page:ticket;width:80mm;display:flow-root;font-size:9pt;overflow-wrap:anywhere}
            .doc-content{page:auto;width:100%}.cover-page{display:none}
            .header,.header--split{display:flex;flex-direction:column;gap:2mm;padding:2mm 0;margin:0 0 3mm;border:0;border-bottom:1px solid #222;border-radius:0}
            .company{width:100%;text-align:center}.company h1{font-size:13pt}.muted{font-size:8pt}
            .doc-meta{min-width:0;width:100%;text-align:center!important}.logo-side{align-self:center;max-width:100%}.logo{max-width:100%!important;max-height:20mm}
            .afip-box{align-self:center;margin:0}.afip-box .letra{font-size:16pt}
            .card{padding:2mm 0;border:0;border-bottom:1px dashed #444;background:white;margin:0 0 3mm}
            .items{display:block;width:100%;margin:2mm 0;font-size:9pt}.items colgroup,.items thead{display:none}
            .items tbody{display:block}.items tr{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:1mm 2mm;padding:2mm 0}.doc-lines .items tr{border-bottom:1px dashed #444}
            .items td{display:block;min-width:0;padding:0;border:0;font-size:inherit}
            .items td:before{content:attr(data-label);display:block;font-size:7pt;color:#444}
            .items td[data-field="Descripcion"],.items td[data-field="Total"]{grid-column:1/-1}
            .items td[data-field="Descripcion"]{font-weight:600}.items td[data-field="Descripcion"]:before{display:none}
            .totals{width:100%;margin:3mm 0 0;font-size:9pt}.totals td{font-size:inherit}.total-final td{font-size:12pt}
            .transparencia-fiscal{margin-top:3mm;padding:2mm 0 2.5mm;font-size:8pt}.transparencia-fiscal__row{gap:2mm}.transparencia-fiscal__row span:first-child{min-width:0}.transparencia-fiscal__row span:last-child{white-space:nowrap}
            .ticket-extras{margin-top:3mm;padding:2mm 0;font-size:8pt}.ticket-extras__row{margin:.8mm 0}.ticket-legend{margin:4mm 0 0;font-size:8pt}.cae-barcode{font-size:7pt;letter-spacing:.04em}.cae-status{font-size:8pt}
            .document-closing{margin:0;display:flow-root}.cae-box{padding:2mm 0;border:0;border-top:1px dashed #444;font-size:8pt}
            .qr-box{text-align:center}.qr-box img{max-width:100%;height:auto!important}
            .signature{margin-top:4mm}.signature-image img{max-width:100%}.ticket-footer{font-size:8pt;text-align:center;margin-top:3mm}
            </style>
            """;
        css += "<style>.doc{padding:" + Mm(paper.MarginTopMm) + " " + Mm(paper.MarginRightMm) + " "
            + Mm(paper.MarginBottomMm) + " " + Mm(paper.MarginLeftMm) + "}</style>";
        var pie = template.Blocks.FirstOrDefault(b => b.Visible && b.Type.Equals(TiposBloqueDocumento.Pie, StringComparison.OrdinalIgnoreCase));
        if (pie is not null)
        {
            bool Show(string field) => pie.VisibleFields is null || pie.VisibleFields.Contains(field, StringComparer.OrdinalIgnoreCase);
            var footer = "<div class=\"ticket-footer\">"
                + (Show("NombreEmpresa") ? WebUtility.HtmlEncode(empresa) : "")
                + (Show("NumeroPagina") ? "<br>Página 1" : "") + "</div>";
            html = html.Replace("</main>", footer + "</main>", StringComparison.Ordinal);
        }
        return html.Replace("</head>", css + "</head>", StringComparison.Ordinal);
    }
}

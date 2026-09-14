using System.Globalization;
using AlfaCore.Models;

namespace AlfaCore.Services;

internal static class DocumentPagination
{
    private static readonly string Script = ReadScript();

    // El navegador mide fuentes y filas reales. El mismo código integrado al HTML se ejecuta
    // en el iframe y en Chromium antes de exportar: no hay un segundo diseño para PDF.
    public static string Apply(string html, DocumentPaperDefinition paper, bool hasFooter)
    {
        var width = paper.Size == "A5" ? 148m : 210m;
        var height = paper.Size == "A5" ? 210m : 297m;
        if (paper.Orientation == "Landscape") (width, height) = (height, width);
        width -= paper.MarginLeftMm + paper.MarginRightMm;
        height -= paper.MarginTopMm + (hasFooter ? Math.Max(paper.MarginBottomMm, 14) : paper.MarginBottomMm);
        static string Mm(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "mm";
        var css = "<style>.doc-content{width:" + Mm(width) + ";margin:0 auto}"
            + ".document-sheet{height:" + Mm(height - 0.3m) + ";display:flex;flex-direction:column;break-after:page;page:content}"
            + ".document-sheet:last-child{break-after:auto}.document-sheet-body{display:flow-root;flex:0 0 auto}"
            + ".document-closing{display:flow-root;margin-top:auto;flex:0 0 auto}.document-closing .totals{break-inside:avoid}"
            + ".document-sheet .items{table-layout:fixed;overflow-wrap:anywhere}"
            + "@media screen{.document-sheet{outline:1px solid #d5dde5;margin-bottom:12px}}"
            + "</style>";
        return html.Replace("</head>", css + "</head>", StringComparison.Ordinal)
            .Replace("</body>", "<script>" + Script + "</script></body>", StringComparison.Ordinal);
    }

    private static string ReadScript()
    {
        using var stream = typeof(DocumentPagination).Assembly.GetManifestResourceStream("AlfaCore.DocumentPagination.js")
            ?? throw new InvalidOperationException("No se encontró el paginador de documentos.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

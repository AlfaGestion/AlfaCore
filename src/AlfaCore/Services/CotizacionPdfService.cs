using System.Globalization;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AlfaCore.Services;

// PDF profesional de una cotización (documento comercial, no captura de pantalla), armado con
// QuestPDF igual que el resto de los PDF de la aplicación (ver ListaPreciosClientePdfService).
public sealed class CotizacionPdfService : ICotizacionPdfService
{
    private static readonly CultureInfo CulturaAr = CultureInfo.GetCultureInfo("es-AR");

    public byte[] GenerarPdf(
        CotizacionVersionDetailDto detail,
        string nombreEmpresa,
        byte[]? logoBytes = null,
        byte[]? portadaBytes = null,
        byte[]? firmaBytes = null,
        string? firmanteNombre = null)
    {
        var document = Document.Create(doc =>
        {
            if (detail.IncluyePortada && portadaBytes is { Length: > 0 })
            {
                doc.Page(cover =>
                {
                    cover.Size(PageSizes.A4);
                    cover.Margin(0);
                    cover.Content().Image(portadaBytes).FitArea();
                });
            }

            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(header => ComposeHeader(header, detail, nombreEmpresa, logoBytes));

                page.Content().PaddingTop(14).Column(column =>
                {
                    column.Spacing(12);

                    if (!string.IsNullOrWhiteSpace(detail.EmpresaProspecto) || !string.IsNullOrWhiteSpace(detail.ContactoNombre))
                        column.Item().Element(e => ComposeCliente(e, detail));

                    var propuestaTexto = StripHtml(detail.CuerpoPropuesta);
                    if (!string.IsNullOrWhiteSpace(propuestaTexto))
                        column.Item().Text(propuestaTexto).FontSize(9).LineHeight(1.35f);

                    var sinSeccion = detail.Lineas.Where(l => l.IdSeccion is null).OrderBy(l => l.Orden).ToList();
                    if (sinSeccion.Count > 0)
                        column.Item().Element(e => ComposeTabla(e, sinSeccion));

                    foreach (var seccion in detail.Secciones.OrderBy(s => s.Orden))
                    {
                        var lineas = detail.Lineas.Where(l => l.IdSeccion == seccion.IdSeccion).OrderBy(l => l.Orden).ToList();
                        if (lineas.Count == 0)
                            continue;
                        column.Item().Element(e => ComposeSeccion(e, seccion, lineas));
                    }

                    column.Item().Element(e => ComposeTotales(e, detail));

                    if (!string.IsNullOrWhiteSpace(detail.Observaciones))
                        column.Item().PaddingTop(4).Text(detail.Observaciones).FontSize(8).FontColor(Colors.Grey.Darken1);

                    if (firmaBytes is { Length: > 0 } || !string.IsNullOrWhiteSpace(firmanteNombre))
                        column.Item().Element(e => ComposeFirma(e, firmaBytes, firmanteNombre));
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(nombreEmpresa).FontSize(7).FontColor(Colors.Grey.Medium);
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Medium));
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, CotizacionVersionDetailDto detail, string nombreEmpresa, byte[]? logoBytes)
    {
        container.PaddingBottom(10).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Row(row =>
        {
            if (logoBytes is { Length: > 0 })
            {
                row.ConstantItem(84).Height(84).Image(logoBytes).FitArea();
                row.ConstantItem(16);
            }

            row.RelativeItem().Column(col =>
            {
                col.Item().Text(nombreEmpresa).FontSize(20).Bold();
                col.Item().PaddingTop(2).Text($"Cotización {detail.CodigoVisible} · v{detail.NumeroVersion}").FontSize(13).SemiBold().FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(3).Text(EstadoLabel(detail.EstadoVersion)).FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(150).AlignRight().Column(col =>
            {
                col.Item().AlignRight().Text($"Fecha: {detail.Fecha:dd/MM/yyyy}").FontSize(8).FontColor(Colors.Grey.Darken1);
                if (detail.FechaVencimiento is { } venc)
                    col.Item().AlignRight().Text($"Vence: {venc:dd/MM/yyyy}").FontSize(8).FontColor(Colors.Grey.Darken1);
                if (!string.IsNullOrWhiteSpace(detail.CodigoMoneda))
                    col.Item().AlignRight().Text($"Moneda: {detail.CodigoMoneda}").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private static void ComposeCliente(IContainer container, CotizacionVersionDetailDto detail)
    {
        container.Background(Colors.Grey.Lighten4).Padding(8).Column(col =>
        {
            col.Spacing(2);
            if (!string.IsNullOrWhiteSpace(detail.EmpresaProspecto))
                col.Item().Text(detail.EmpresaProspecto).FontSize(11).Bold();
            if (!string.IsNullOrWhiteSpace(detail.DocumentoFiscal))
                col.Item().Text($"CUIT/Documento: {detail.DocumentoFiscal}").FontSize(8).FontColor(Colors.Grey.Darken1);
            if (!string.IsNullOrWhiteSpace(detail.ContactoNombre) || !string.IsNullOrWhiteSpace(detail.ContactoEmail))
            {
                var contacto = string.Join(" · ", new[] { detail.ContactoNombre, detail.ContactoEmail, detail.ContactoTelefono }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                col.Item().Text(contacto).FontSize(8).FontColor(Colors.Grey.Darken1);
            }
        });
    }

    private static void ComposeSeccion(IContainer container, CotizacionSeccionDto seccion, List<CotizacionLineaDto> lineas)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Background(Colors.Grey.Lighten3).Padding(6).Text(seccion.Titulo).FontSize(10).Bold();
            col.Item().Element(e => ComposeTabla(e, lineas));
        });
    }

    private static void ComposeTabla(IContainer container, List<CotizacionLineaDto> lineas)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(5);
                columns.ConstantColumn(50);
                columns.ConstantColumn(70);
                columns.ConstantColumn(70);
            });

            table.Header(header =>
            {
                header.Cell().Element(EncabezadoCelda).Text("Descripción");
                header.Cell().Element(EncabezadoCelda).AlignRight().Text("Cant.");
                header.Cell().Element(EncabezadoCelda).AlignRight().Text("Precio");
                header.Cell().Element(EncabezadoCelda).AlignRight().Text("Subtotal");

                static IContainer EncabezadoCelda(IContainer c)
                    => c.DefaultTextStyle(x => x.FontSize(8).Bold().FontColor(Colors.White))
                        .Background(Colors.Blue.Darken2).PaddingVertical(4).PaddingHorizontal(4);
            });

            foreach (var linea in lineas)
            {
                var esInformativa = !linea.ImpactaTotal;
                table.Cell().Element(CeldaBase).Text(linea.Descripcion).FontSize(8).Italic(esInformativa);
                table.Cell().Element(CeldaBase).AlignRight().Text(esInformativa ? "" : linea.Cantidad.ToString("0.##", CulturaAr)).FontSize(8);
                table.Cell().Element(CeldaBase).AlignRight().Text(esInformativa ? "" : linea.PrecioUnitario.ToString("N2", CulturaAr)).FontSize(8);
                table.Cell().Element(CeldaBase).AlignRight().Text(esInformativa ? "" : linea.Subtotal.ToString("N2", CulturaAr)).FontSize(8).Bold();
            }

            static IContainer CeldaBase(IContainer c)
                => c.PaddingVertical(4).PaddingHorizontal(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3);
        });
    }

    private static void ComposeFirma(IContainer container, byte[]? firmaBytes, string? firmanteNombre)
    {
        container.PaddingTop(18).Column(col =>
        {
            col.Spacing(2);
            col.Item().Text("Atentamente,").FontSize(9);
            if (firmaBytes is { Length: > 0 })
                col.Item().PaddingTop(6).Width(140).Height(60).Image(firmaBytes).FitArea();
            if (!string.IsNullOrWhiteSpace(firmanteNombre))
                col.Item().Text(firmanteNombre).FontSize(9).Bold();
        });
    }

    private static void ComposeTotales(IContainer container, CotizacionVersionDetailDto detail)
    {
        container.AlignRight().Column(col =>
        {
            col.Spacing(2);
            col.Item().AlignRight().Text($"Subtotal: {detail.Subtotal.ToString("N2", CulturaAr)}").FontSize(9);
            if (detail.DescuentoGeneralPorcentaje > 0)
                col.Item().AlignRight().Text($"Descuento ({detail.DescuentoGeneralPorcentaje.ToString("0.##", CulturaAr)}%): -{detail.TotalDescuento.ToString("N2", CulturaAr)}").FontSize(9);
            col.Item().AlignRight().PaddingTop(2).Text($"Total: {detail.Total.ToString("N2", CulturaAr)} {detail.CodigoMoneda}").FontSize(13).Bold();
        });
    }

    private static string EstadoLabel(string estado) => estado switch
    {
        CotizacionEstados.Borrador => "Borrador",
        CotizacionEstados.Enviada => "Enviada",
        CotizacionEstados.Aceptada => "Aceptada",
        CotizacionEstados.Rechazada => "Rechazada",
        CotizacionEstados.Vencida => "Vencida",
        CotizacionEstados.Anulada => "Anulada",
        _ => estado
    };

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var sinTags = Regex.Replace(html, "<(br|/p|/div|/li)\\s*/?>", "\n", RegexOptions.IgnoreCase);
        sinTags = Regex.Replace(sinTags, "<[^>]+>", string.Empty);
        sinTags = System.Net.WebUtility.HtmlDecode(sinTags);
        return Regex.Replace(sinTags, "\n{3,}", "\n\n").Trim();
    }
}

using System.Globalization;
using AlfaCore.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AlfaCore.Services;

public interface IListaPreciosClientePdfService
{
    byte[] GenerarPdf(
        IReadOnlyList<ListaPreciosArticuloDto> articulos,
        ListaPreciosResolucionDto resolucion,
        byte[]? logoBytes,
        string nombreEmpresa,
        string nombreCliente,
        string agruparPor,
        bool truncado = false);
}

// PDF profesional de "lista de precios" para el Portal Cliente: logo, empresa, cliente, fecha,
// lista/clase usada y artículos con precio. No es una captura de pantalla: se arma con QuestPDF
// igual que el resto de los PDF comerciales de la aplicación (ver CatalogoPublicoPdfService).
public sealed class ListaPreciosClientePdfService : IListaPreciosClientePdfService
{
    private static readonly CultureInfo CulturaAr = CultureInfo.GetCultureInfo("es-AR");

    public byte[] GenerarPdf(
        IReadOnlyList<ListaPreciosArticuloDto> articulos,
        ListaPreciosResolucionDto resolucion,
        byte[]? logoBytes,
        string nombreEmpresa,
        string nombreCliente,
        string agruparPor,
        bool truncado = false)
    {
        var generadoEl = DateTime.Now;
        var listaTexto = resolucion.UsaMaestro
            ? "Precio de maestro (sin lista)"
            : (string.IsNullOrWhiteSpace(resolucion.NombreLista) ? resolucion.IdLista : resolucion.NombreLista) ?? string.Empty;

        var grupos = AgruparArticulos(articulos, agruparPor);

        var document = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(header =>
                    ComposeHeader(header, logoBytes, nombreEmpresa, nombreCliente, listaTexto, resolucion.Clase, generadoEl));

                page.Content().PaddingTop(12).Column(column =>
                {
                    column.Spacing(14);

                    if (articulos.Count == 0)
                    {
                        column.Item().AlignCenter().PaddingTop(40)
                            .Text("No encontramos artículos para esta búsqueda.")
                            .FontSize(11).FontColor(Colors.Grey.Darken1);
                    }

                    if (truncado)
                    {
                        column.Item().Background(Colors.Amber.Lighten3).Padding(6)
                            .Text($"Se alcanzó el máximo de {articulos.Count:N0} artículos exportables de una vez. Refiná la búsqueda o los filtros para exportar el resto.")
                            .FontSize(8).Bold().FontColor(Colors.Amber.Darken3);
                    }

                    foreach (var grupo in grupos)
                        column.Item().Element(e => ComposeGrupo(e, grupo));
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

    private sealed record GrupoPdf(string? Titulo, List<ListaPreciosArticuloDto> Articulos);

    private static List<GrupoPdf> AgruparArticulos(IReadOnlyList<ListaPreciosArticuloDto> articulos, string agruparPor)
    {
        if (agruparPor is ListaPreciosAgruparPorKeys.Familia)
        {
            return articulos
                .GroupBy(a => string.IsNullOrWhiteSpace(a.Familia) ? "Sin familia" : a.Familia.Trim())
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new GrupoPdf(g.Key.ToUpperInvariant(), g.ToList()))
                .ToList();
        }

        if (agruparPor is ListaPreciosAgruparPorKeys.Marca)
        {
            return articulos
                .GroupBy(a => string.IsNullOrWhiteSpace(a.Marca) ? "Sin marca" : a.Marca.Trim())
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new GrupoPdf(g.Key.ToUpperInvariant(), g.ToList()))
                .ToList();
        }

        return [new GrupoPdf(null, articulos.ToList())];
    }

    private static void ComposeHeader(
        IContainer container,
        byte[]? logoBytes,
        string nombreEmpresa,
        string nombreCliente,
        string listaTexto,
        int clase,
        DateTime generadoEl)
    {
        container.PaddingBottom(10).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Row(row =>
        {
            if (logoBytes is not null)
            {
                row.ConstantItem(56).Height(56).Image(logoBytes).FitArea();
                row.ConstantItem(12);
            }

            row.RelativeItem().Column(col =>
            {
                col.Item().Text(nombreEmpresa).FontSize(15).Bold();
                col.Item().Text("Lista de precios").FontSize(11).SemiBold().FontColor(Colors.Grey.Darken2);

                if (!string.IsNullOrWhiteSpace(nombreCliente))
                    col.Item().PaddingTop(2).Text($"Cliente: {nombreCliente}").FontSize(8).FontColor(Colors.Grey.Darken1);

                col.Item().PaddingTop(2).Text($"Lista: {listaTexto} · Clase: {clase}").FontSize(8).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(110).AlignRight().Column(col =>
            {
                col.Item().AlignRight().Text("Uso exclusivo del cliente").FontSize(7).FontColor(Colors.Grey.Medium);
                col.Item().AlignRight().Text($"Generado {generadoEl:dd/MM/yyyy HH:mm}").FontSize(7).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static void ComposeGrupo(IContainer container, GrupoPdf grupo)
    {
        container.Column(col =>
        {
            col.Spacing(6);

            if (!string.IsNullOrWhiteSpace(grupo.Titulo))
                col.Item().Background(Colors.Grey.Lighten3).Padding(6).Text(grupo.Titulo).FontSize(11).Bold();

            col.Item().Element(e => ComposeTabla(e, grupo.Articulos));
        });
    }

    private static void ComposeTabla(IContainer container, List<ListaPreciosArticuloDto> articulos)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(60);
                columns.RelativeColumn(4);
                columns.RelativeColumn(2);
                columns.RelativeColumn(2);
                columns.ConstantColumn(80);
            });

            table.Header(header =>
            {
                header.Cell().Element(EncabezadoCelda).Text("Código");
                header.Cell().Element(EncabezadoCelda).Text("Descripción");
                header.Cell().Element(EncabezadoCelda).Text("Marca");
                header.Cell().Element(EncabezadoCelda).Text("Rubro");
                header.Cell().Element(EncabezadoCelda).AlignRight().Text("Precio");

                static IContainer EncabezadoCelda(IContainer c)
                    => c.DefaultTextStyle(x => x.FontSize(8).Bold().FontColor(Colors.White))
                        .Background(Colors.Blue.Darken2).PaddingVertical(4).PaddingHorizontal(4);
            });

            foreach (var item in articulos)
            {
                table.Cell().Element(CeldaBase).Text(item.IdArticulo).FontSize(8);
                table.Cell().Element(CeldaBase).Element(e => ComposeDescripcionCelda(e, item));
                table.Cell().Element(CeldaBase).Text(item.Marca).FontSize(8);
                table.Cell().Element(CeldaBase).Text(item.Rubro).FontSize(8);
                table.Cell().Element(CeldaBase).AlignRight().Text(item.SinPrecio ? "Consultar" : FormatMoney(item.Precio))
                    .FontSize(8).Bold();
            }

            static IContainer CeldaBase(IContainer c)
                => c.PaddingVertical(4).PaddingHorizontal(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3);
        });
    }

    private static void ComposeDescripcionCelda(IContainer container, ListaPreciosArticuloDto item)
    {
        container.Column(col =>
        {
            col.Item().Text(item.Descripcion).FontSize(8).SemiBold();
            if (!string.IsNullOrWhiteSpace(item.Presentacion))
                col.Item().Text(item.Presentacion).FontSize(7).FontColor(Colors.Grey.Darken1);
        });
    }

    private static string FormatMoney(decimal value)
        => value.ToString("C2", CulturaAr);
}

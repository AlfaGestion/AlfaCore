using System.Globalization;
using AlfaCore.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AlfaCore.Services;

public interface ICatalogoPublicoPdfService
{
    Task<byte[]> GenerarPdfAsync(
        CatalogosCatalogoDetalleDto catalogo,
        CatalogosPublicIdentityDto branding,
        string? idWeb,
        string? ftpCodigoCta,
        int? idBase,
        bool esCuadricula,
        CancellationToken ct = default);
}

public sealed class CatalogoPublicoPdfService(
    IInterfacesCatalogosService catalogosSvc,
    IArticuloImagenFtpService imagenSvc) : ICatalogoPublicoPdfService
{
    private static readonly CultureInfo CulturaAr = CultureInfo.GetCultureInfo("es-AR");

    private sealed record GrupoMarcaPdf(string Marca, List<CatalogosCatalogoItemDto> Articulos);
    private sealed record GrupoRubroPdf(string Rubro, List<GrupoMarcaPdf> Marcas);

    public async Task<byte[]> GenerarPdfAsync(
        CatalogosCatalogoDetalleDto catalogo,
        CatalogosPublicIdentityDto branding,
        string? idWeb,
        string? ftpCodigoCta,
        int? idBase,
        bool esCuadricula,
        CancellationToken ct = default)
    {
        var logoBytes = await CargarLogoAsync(idWeb, ct);
        var imageMap = await CargarImagenesAsync(catalogo.Articulos, ftpCodigoCta, idBase, ct);
        var grupos = AgruparPorRubroYMarca(catalogo.Articulos);
        var generadoEl = DateTime.Now;

        var nombreEmpresa = string.IsNullOrWhiteSpace(branding.NombreVisible)
            ? (string.IsNullOrWhiteSpace(branding.NombreFallback) ? "Catálogos" : branding.NombreFallback.Trim())
            : branding.NombreVisible.Trim();

        var nombreCatalogo = string.IsNullOrWhiteSpace(catalogo.Nombre) ? "Catálogo público" : catalogo.Nombre.Trim();
        var descripcion = catalogo.Observaciones?.Trim() ?? string.Empty;
        var vigenciaTexto = BuildVigenciaTexto(catalogo);

        var document = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(header =>
                    ComposeHeader(header, logoBytes, nombreEmpresa, nombreCatalogo, descripcion, vigenciaTexto, generadoEl));

                page.Content().PaddingTop(12).Column(column =>
                {
                    column.Spacing(16);

                    if (grupos.Count == 0)
                    {
                        column.Item().AlignCenter().PaddingTop(40)
                            .Text("Este catálogo todavía no tiene artículos publicados.")
                            .FontSize(11).FontColor(Colors.Grey.Darken1);
                    }

                    foreach (var grupoRubro in grupos)
                    {
                        column.Item().Element(e => ComposeRubroSection(e, grupoRubro, imageMap, esCuadricula));
                    }
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

    private static void ComposeHeader(
        IContainer container,
        byte[]? logoBytes,
        string nombreEmpresa,
        string nombreCatalogo,
        string descripcion,
        string? vigenciaTexto,
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
                col.Item().Text(nombreCatalogo).FontSize(11).SemiBold().FontColor(Colors.Grey.Darken2);

                if (!string.IsNullOrWhiteSpace(descripcion))
                    col.Item().PaddingTop(2).Text(descripcion).FontSize(8).FontColor(Colors.Grey.Darken1);

                if (!string.IsNullOrWhiteSpace(vigenciaTexto))
                    col.Item().PaddingTop(2).Text(vigenciaTexto).FontSize(8).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(110).AlignRight().Column(col =>
            {
                col.Item().AlignRight().Text("Catálogo comercial").FontSize(7).FontColor(Colors.Grey.Medium);
                col.Item().AlignRight().Text($"Generado {generadoEl:dd/MM/yyyy}").FontSize(7).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static void ComposeRubroSection(IContainer container, GrupoRubroPdf grupo, IReadOnlyDictionary<string, byte[]?> imageMap, bool esCuadricula)
    {
        container.Column(col =>
        {
            col.Spacing(10);
            col.Item().Background(Colors.Grey.Lighten3).Padding(6).Text(grupo.Rubro).FontSize(12).Bold();

            foreach (var grupoMarca in grupo.Marcas)
            {
                col.Item().Column(marcaCol =>
                {
                    marcaCol.Spacing(4);
                    marcaCol.Item().Text(grupoMarca.Marca).FontSize(10).SemiBold().FontColor(Colors.Grey.Darken2);

                    if (esCuadricula)
                        marcaCol.Item().Element(e => ComposeArticulosGrid(e, grupoMarca.Articulos, imageMap));
                    else
                        marcaCol.Item().Element(e => ComposeArticulosTable(e, grupoMarca.Articulos, imageMap));
                });
            }
        });
    }

    private const int ArticulosPorFilaGrid = 3;

    private static void ComposeArticulosGrid(IContainer container, List<CatalogosCatalogoItemDto> articulos, IReadOnlyDictionary<string, byte[]?> imageMap)
    {
        container.Column(column =>
        {
            column.Spacing(8);

            for (var i = 0; i < articulos.Count; i += ArticulosPorFilaGrid)
            {
                var fila = articulos.Skip(i).Take(ArticulosPorFilaGrid).ToList();

                column.Item().Row(row =>
                {
                    foreach (var item in fila)
                        row.RelativeItem().Element(e => ComposeTarjetaArticulo(e, item, imageMap));

                    for (var j = fila.Count; j < ArticulosPorFilaGrid; j++)
                        row.RelativeItem();
                });
            }
        });
    }

    private static void ComposeTarjetaArticulo(IContainer container, CatalogosCatalogoItemDto item, IReadOnlyDictionary<string, byte[]?> imageMap)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten3).Padding(7).Column(col =>
        {
            col.Spacing(4);
            col.Item().Height(112).Element(e => ComposeImagenCelda(e, item, imageMap, 112));
            col.Item().Text(item.DescripcionArticulo).FontSize(8).Bold();
            col.Item().Text($"Cód. {item.IdArticulo}").FontSize(7).FontColor(Colors.Grey.Medium);
            col.Item().Element(e => ComposePrecioCelda(e, item, alignRight: false, tamanioBase: 13f));
        });
    }

    private static void ComposeArticulosTable(IContainer container, List<CatalogosCatalogoItemDto> articulos, IReadOnlyDictionary<string, byte[]?> imageMap)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(54);
                columns.RelativeColumn(4);
                columns.RelativeColumn(2);
            });

            foreach (var item in articulos)
            {
                table.Cell().Element(CeldaBase).Element(e => ComposeImagenCelda(e, item, imageMap, 48));
                table.Cell().Element(CeldaBase).Element(e => ComposeDescripcionCelda(e, item));
                table.Cell().Element(CeldaBase).Element(e => ComposePrecioCelda(e, item, alignRight: true));
            }
        });

        static IContainer CeldaBase(IContainer c)
            => c.PaddingVertical(4).PaddingHorizontal(3).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3);
    }

    private static void ComposeImagenCelda(IContainer container, CatalogosCatalogoItemDto item, IReadOnlyDictionary<string, byte[]?> imageMap, float size)
    {
        var tieneImagen = imageMap.TryGetValue(item.IdArticulo, out var bytes) && bytes is not null;

        if (tieneImagen)
        {
            container.Width(size).Height(size).Image(bytes!).FitArea();
            return;
        }

        container.Width(size).Height(size).Background(Colors.Grey.Lighten3).AlignCenter().AlignMiddle()
            .Text(Iniciales(item.DescripcionArticulo)).FontSize(8).Bold().FontColor(Colors.Grey.Darken2);
    }

    private static void ComposeDescripcionCelda(IContainer container, CatalogosCatalogoItemDto item)
    {
        container.Column(col =>
        {
            col.Item().Text(text =>
            {
                text.Span($"{item.IdArticulo} — ").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                text.Span(item.DescripcionArticulo).FontSize(9).Bold();
            });

            if (!string.IsNullOrWhiteSpace(item.Presentacion))
                col.Item().Text(item.Presentacion).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
        });
    }

    private static void ComposePrecioCelda(IContainer container, CatalogosCatalogoItemDto item, bool alignRight, float tamanioBase = 10f)
    {
        var hasOffer = HasValidOffer(item);

        container.Column(col =>
        {
            var precioItem = alignRight ? col.Item().AlignRight() : col.Item();
            precioItem.Text(text =>
            {
                var span = text.Span(FormatMoney(item.Precio));
                if (hasOffer)
                {
                    span.FontSize(tamanioBase - 2).FontColor(Colors.Grey.Medium).Strikethrough();
                }
                else
                {
                    span.FontSize(tamanioBase).Bold();
                }
            });

            if (hasOffer)
            {
                var ofertaItem = alignRight ? col.Item().AlignRight() : col.Item();
                ofertaItem.Text($"Oferta: {FormatMoney(item.PrecioOferta)}")
                    .FontSize(tamanioBase).Bold().FontColor(Colors.Blue.Darken2);
            }
        });
    }

    private static bool HasValidOffer(CatalogosCatalogoItemDto item)
        => item.PrecioOferta.HasValue
           && item.PrecioOferta.Value > 0m
           && item.PrecioOferta.Value < item.Precio;

    private static string FormatMoney(decimal? value)
        => value.HasValue ? value.Value.ToString("C2", CulturaAr) : "—";

    private static string Iniciales(string descripcion)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
            return "CA";

        var parts = descripcion
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(x => char.ToUpperInvariant(x[0]));

        var result = string.Concat(parts);
        return string.IsNullOrWhiteSpace(result) ? "CA" : result;
    }

    private static string? BuildVigenciaTexto(CatalogosCatalogoDetalleDto catalogo)
    {
        if (!catalogo.VigenciaDesde.HasValue && !catalogo.VigenciaHasta.HasValue)
            return null;

        var desde = catalogo.VigenciaDesde?.ToString("dd/MM/yyyy") ?? "sin fecha";
        var hasta = catalogo.VigenciaHasta?.ToString("dd/MM/yyyy") ?? "sin fecha";
        return $"Vigencia: {desde} - {hasta}";
    }

    private static List<GrupoRubroPdf> AgruparPorRubroYMarca(IReadOnlyList<CatalogosCatalogoItemDto> articulos)
        => articulos
            .GroupBy(a => string.IsNullOrWhiteSpace(a.Rubro) ? "SIN RUBRO" : a.Rubro.Trim().ToUpperInvariant())
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GrupoRubroPdf(
                g.Key,
                g.GroupBy(a => string.IsNullOrWhiteSpace(a.Marca) ? "VARIOS" : a.Marca.Trim().ToUpperInvariant())
                 .OrderBy(mg => mg.Key, StringComparer.OrdinalIgnoreCase)
                 .Select(mg => new GrupoMarcaPdf(mg.Key, mg.ToList()))
                 .ToList()))
            .ToList();

    private async Task<byte[]?> CargarLogoAsync(string? idWeb, CancellationToken ct)
    {
        try
        {
            var logo = await catalogosSvc.GetPublicLogoForServeAsync(idWeb, ct);
            if (logo is null || !File.Exists(logo.RutaCompleta))
                return null;

            return await File.ReadAllBytesAsync(logo.RutaCompleta, ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, byte[]?>> CargarImagenesAsync(
        IReadOnlyList<CatalogosCatalogoItemDto> articulos,
        string? ftpCodigoCta,
        int? idBase,
        CancellationToken ct)
    {
        var resultado = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(ftpCodigoCta) || articulos.Count == 0)
            return resultado;

        using var throttle = new SemaphoreSlim(6);

        var tareas = articulos.Select(async item =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var imagen = await imagenSvc.ObtenerImagenAsync(ftpCodigoCta, idBase, item.IdArticulo, thumbnail: false, ct: ct)
                             ?? await imagenSvc.ObtenerImagenAsync(ftpCodigoCta, idBase, item.IdArticulo, thumbnail: true, ct: ct);
                if (imagen is null || !File.Exists(imagen.RutaCompleta))
                    return (item.IdArticulo, Bytes: (byte[]?)null);

                var bytes = await File.ReadAllBytesAsync(imagen.RutaCompleta, ct);
                return (item.IdArticulo, Bytes: (byte[]?)bytes);
            }
            catch
            {
                return (item.IdArticulo, Bytes: (byte[]?)null);
            }
            finally
            {
                throttle.Release();
            }
        });

        var resultados = await Task.WhenAll(tareas);
        foreach (var (idArticulo, bytes) in resultados)
            resultado[idArticulo] = bytes;

        return resultado;
    }
}

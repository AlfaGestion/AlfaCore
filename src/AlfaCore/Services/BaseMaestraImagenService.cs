using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed partial class BaseMaestraImagenService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents,
    IHttpClientFactory httpClientFactory,
    IArticuloImagenFtpService articuloImagenFtpService,
    IPuntoVentaService puntoVentaService,
    IWebHostEnvironment environment,
    ILogger<BaseMaestraImagenService> logger) : IBaseMaestraImagenService
{
    private const string ModuleName = "BaseMaestraImagen";
    private const string FriendlyLookupMessage = "No se pudieron consultar las imágenes en Base Maestra.";
    private const string FriendlyAssignMessage = "No se pudieron asignar las imágenes seleccionadas.";
    private const string PreviewRoute = "/api/base-maestra/imagen-preview";
    private const string DefaultCacheDirectory = "App_Data/cache/base-maestra-imagenes";
    private const string ConfigGroup = "BASEMAESTRA";
    private const int ExternalLookupTimeoutSeconds = 12;
    private const string LegacyProductInfoUrl = "http://149.50.128.177:5712/api/v1";
    private const string LegacyProductInfoApiKey = "NTphbGJlcnRvZmF2aW9hbnR1bmV6QGdtYWlsLmNvbTpNSUlEWkRDQ0FreWdBd0lCQWdJSVBMdG1YZjRCOGtVd0RRWUpLb1pJaHZjTkFRRUZCUUF3UmpFb01DWUdBMVVFQXd3ZlFVWkpVQ0JRY205a2RXTmphVzl1SUVOdmJY";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, Task<BaseMaestraProductInfoDto?>> _productInfoCache = new(StringComparer.OrdinalIgnoreCase);

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public string BuildPreviewUrl(string imageUrl, string? idClienteFtp, int? idBase)
    {
        var sourceUrl = (imageUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return string.Empty;

        var url = $"{PreviewRoute}?src={Uri.EscapeDataString(sourceUrl)}";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(idClienteFtp))
            query.Add($"idCliente={Uri.EscapeDataString(idClienteFtp.Trim())}");
        if (idBase is > 0)
            query.Add($"idBase={idBase.Value}");

        return query.Count == 0 ? url : $"{url}?{string.Join("&", query)}";
    }

    public string BuildPreviewUrlFromCodigo(string codigo, string? idClienteFtp, int? idBase)
    {
        var codigoNormalizado = (codigo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(codigoNormalizado))
            return string.Empty;

        var url = $"{PreviewRoute}/{Uri.EscapeDataString(codigoNormalizado)}";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(idClienteFtp))
            query.Add($"idCliente={Uri.EscapeDataString(idClienteFtp.Trim())}");
        if (idBase is > 0)
            query.Add($"idBase={idBase.Value}");

        return query.Count == 0 ? url : $"{url}?{string.Join("&", query)}";
    }

    public Task<BaseMaestraImagenArticuloDto> ConsultarArticuloAsync(BaseMaestraImagenOrigenDto articulo, string? idClienteFtp, int? idBase, CancellationToken ct = default, bool forceRefresh = false)
        => ExecuteLoggedAsync(ModuleName, "ConsultarArticulo", async token =>
        {
            ArgumentNullException.ThrowIfNull(articulo);

            var idArticulo = (articulo.IdArticulo ?? string.Empty).Trim();
            var codigoBarra = (articulo.CodigoBarra ?? string.Empty).Trim();
            var hasCurrentImage = await HasCurrentImageAsync(idArticulo, idClienteFtp, idBase, token);

            var result = new BaseMaestraImagenArticuloDto
            {
                IdArticulo = idArticulo,
                DescripcionArticulo = (articulo.DescripcionArticulo ?? string.Empty).Trim(),
                CodigoBarra = codigoBarra,
                RutaImagen = (articulo.RutaImagen ?? string.Empty).Trim(),
                CodigoConsulta = codigoBarra,
                TieneImagenActual = hasCurrentImage,
                PuedeSeleccionarse = !hasCurrentImage
            };

            if (string.IsNullOrWhiteSpace(codigoBarra))
            {
                result.Estado = "Sin código de barras";
                result.PuedeSeleccionarse = false;
                return result;
            }

            var info = await TryGetProductInfoAsync(codigoBarra, token, forceRefresh);
            var candidate = await ResolveBestCandidateAsync(idArticulo, codigoBarra, articulo.DescripcionArticulo ?? string.Empty, info, token);

            if (candidate is null)
            {
                result.Estado = "Sin imagen encontrada";
                result.PuedeSeleccionarse = false;
                return result;
            }

            ApplyCandidate(result, candidate, hasCurrentImage, idClienteFtp, idBase);
            return result;
        }, FriendlyLookupMessage, ct);

    public Task<ArticuloImagenArchivoDto?> ObtenerPreviewAsync(string imageUrl, string? idClienteFtp, int? idBase, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ObtenerPreview", async token =>
        {
            var sourceUrl = (imageUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourceUrl))
                return null;

            var cacheRoot = await GetPreviewCacheRootAsync(token);
            Directory.CreateDirectory(cacheRoot);
            var fileName = BuildPreviewFileNameFromUrl(sourceUrl);
            var cachedFile = Path.Combine(cacheRoot, fileName);
            var cachedExisting = FindCachedPreviewFile(cachedFile);
            if (cachedExisting is not null)
                return new ArticuloImagenArchivoDto { RutaCompleta = cachedExisting, MimeType = InferMimeTypeFromPath(cachedExisting) };

            var bytes = await DownloadImageAsync(sourceUrl, token);
            if (bytes is null || bytes.Length == 0)
                return null;

            var mimeType = DetectMimeType(bytes, sourceUrl);
            var ext = MimeTypeToExtension(mimeType);
            cachedFile = Path.ChangeExtension(cachedFile, ext);
            await File.WriteAllBytesAsync(cachedFile, bytes, token);
            return new ArticuloImagenArchivoDto { RutaCompleta = cachedFile, MimeType = mimeType };
        }, FriendlyLookupMessage, ct);

    public Task<ArticuloImagenArchivoDto?> ObtenerPreviewDesdeCodigoAsync(string codigo, string? idClienteFtp, int? idBase, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ObtenerPreviewDesdeCodigo", async token =>
        {
            var codigoNormalizado = (codigo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigoNormalizado))
                return null;

            var cacheRoot = await GetPreviewCacheRootAsync(token);
            Directory.CreateDirectory(cacheRoot);
            var cachedBase = Path.Combine(cacheRoot, SafeFileSegment(codigoNormalizado) + ".jpg");
            var cachedExisting = FindCachedPreviewFile(cachedBase);
            if (cachedExisting is not null)
                return new ArticuloImagenArchivoDto { RutaCompleta = cachedExisting, MimeType = InferMimeTypeFromPath(cachedExisting) };

            var bytes = await DownloadProductImageAsync(codigoNormalizado, token);
            if (bytes is null || bytes.Length == 0)
                return null;

            var mimeType = DetectMimeType(bytes, "image/jpeg");
            var ext = MimeTypeToExtension(mimeType);
            var cachedFile = Path.ChangeExtension(cachedBase, ext);
            await File.WriteAllBytesAsync(cachedFile, bytes, token);
            return new ArticuloImagenArchivoDto { RutaCompleta = cachedFile, MimeType = mimeType };
        }, FriendlyLookupMessage, ct);

    public Task<BaseMaestraImagenResultadoDto> AsignarImagenesAsync(
        IReadOnlyList<BaseMaestraImagenArticuloDto> articulos,
        string? idClienteFtp,
        int? idBase,
        Action<int, int, string>? progressReporter = null,
        CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AsignarImagenes", async token =>
        {
            if (articulos is null || articulos.Count == 0)
                return new BaseMaestraImagenResultadoDto();

            var resultado = new BaseMaestraImagenResultadoDto
            {
                Total = articulos.Count
            };

            var oficiales = articulos
                .Where(a => a.Seleccionado
                            && a.ImagenEncontrada
                            && !string.IsNullOrWhiteSpace(a.ImageUrl)
                            && (!a.TieneImagenActual || a.PermiteReemplazo))
                .ToList();

            resultado.Seleccionadas = oficiales.Count;
            if (oficiales.Count == 0)
                return resultado;

            var carpetaBase = await ResolveRutaImagenesAsync(token);
            if (string.IsNullOrWhiteSpace(carpetaBase))
                throw new InvalidOperationException("No se pudo resolver la carpeta oficial de imágenes (RutaImagenes).");

            Directory.CreateDirectory(carpetaBase);
            var thumbsDir = Path.Combine(carpetaBase, "thumbs4");
            Directory.CreateDirectory(thumbsDir);

            for (var index = 0; index < oficiales.Count; index++)
            {
                var articulo = oficiales[index];
                progressReporter?.Invoke(index + 1, oficiales.Count, $"Asignando imagen {index + 1} de {oficiales.Count}...");
                try
                {
                    if (await HasCurrentImageAsync(articulo.IdArticulo, idClienteFtp, idBase, token) && !articulo.PermiteReemplazo)
                    {
                        resultado.OmitidasPorExistir++;
                        continue;
                    }

                    byte[]? bytes;
                    var imageUrl = NormalizeUrl(articulo.ImageUrl);
                    if (string.Equals(articulo.Fuente, "Base Maestra", StringComparison.OrdinalIgnoreCase))
                    {
                        bytes = await DownloadProductImageAsync(articulo.CodigoConsulta, token);
                        if ((bytes is null || bytes.Length == 0) && !string.IsNullOrWhiteSpace(imageUrl))
                            bytes = await DownloadImageAsync(imageUrl, token);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(imageUrl))
                        {
                            resultado.SinImagen++;
                            continue;
                        }

                        bytes = await DownloadImageAsync(imageUrl, token);
                    }

                    if (bytes is null || bytes.Length == 0)
                    {
                        resultado.ConError++;
                        continue;
                    }

                    var extension = NormalizeExtensionFromUrl(imageUrl, articulo.Extension, articulo.MimeType);
                    var officialFileName = BuildOfficialFileName(articulo.IdArticulo, extension);
                    var destination = Path.Combine(carpetaBase, officialFileName);
                    await File.WriteAllBytesAsync(destination, bytes, token);

                    var thumbDestination = Path.Combine(thumbsDir, officialFileName);
                    await File.WriteAllBytesAsync(thumbDestination, bytes, token);

                    if (!string.IsNullOrWhiteSpace(idClienteFtp))
                    {
                        await using var stream = new MemoryStream(bytes, writable: false);
                        await articuloImagenFtpService.SubirImagenAsync(idClienteFtp, idBase, articulo.IdArticulo, extension, stream, thumbnail: false, token);
                        stream.Position = 0;
                        await articuloImagenFtpService.SubirImagenAsync(idClienteFtp, idBase, articulo.IdArticulo, extension, stream, thumbnail: true, token);
                    }

                    await MarkArticleImageModifiedAsync(articulo.IdArticulo, token);
                    resultado.Asignadas++;
                }
                catch (Exception ex)
                {
                    resultado.ConError++;
                    logger.LogWarning(ex, "No se pudo asignar la imagen del articulo {Articulo}", articulo.IdArticulo);
                }
            }

            progressReporter?.Invoke(oficiales.Count, oficiales.Count, $"Asignación finalizada: {resultado.Asignadas} imágenes asignadas.");

            return resultado;
        }, FriendlyAssignMessage, ct);

    private async Task MarkArticleImageModifiedAsync(string idArticulo, CancellationToken ct)
    {
        var articuloId = (idArticulo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(articuloId))
            throw new InvalidOperationException("No se pudo marcar la actualización de imagen porque el artículo no tiene ID.");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        if (!await TableExistsAsync(cn, "V_MA_ARTICULOS", ct))
            throw new InvalidOperationException("La base activa no tiene V_MA_ARTICULOS. No se pudo marcar ModificoImagen.");

        const string sql = """
            UPDATE dbo.V_MA_ARTICULOS
            SET ModificoImagen = @Valor
            WHERE LTRIM(RTRIM(IDARTICULO)) = @IdArticulo;
            """;

        var affected = await cn.ExecuteAsync(new CommandDefinition(
            sql,
            new { Valor = "S", IdArticulo = articuloId },
            cancellationToken: ct));

        if (affected == 0)
            throw new InvalidOperationException($"No se encontró el artículo {articuloId} para marcar ModificoImagen.");
    }

    private async Task<bool> HasCurrentImageAsync(string idArticulo, string? idClienteFtp, int? idBase, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idArticulo))
            return false;

        var local = await puntoVentaService.GetArticleImageForServeAsync(idArticulo, ct);
        if (local is not null && File.Exists(local.RutaCompleta))
            return true;

        if (string.IsNullOrWhiteSpace(idClienteFtp))
            return false;

        var ftp = await articuloImagenFtpService.ObtenerImagenAsync(idClienteFtp, idBase, idArticulo, thumbnail: true, ct);
        return ftp is not null && File.Exists(ftp.RutaCompleta);
    }

    private async Task<BaseMaestraProductInfoDto?> TryGetProductInfoAsync(string codigo, CancellationToken ct, bool forceRefresh = false)
    {
        try
        {
            var key = (codigo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
                return null;

            if (forceRefresh)
                _productInfoCache.TryRemove(key, out _);

            var task = _productInfoCache.GetOrAdd(key, _ => GetProductInfoAsync(key, ct));
            return await task;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo consultar Base Maestra para el codigo {Codigo}.", codigo);
            return null;
        }
    }

    private async Task<BaseMaestraProductInfoDto?> GetProductInfoAsync(string codigo, CancellationToken ct)
    {
        var (url, apiKey) = await ResolveServiceConfigAsync(ct);
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Falta la configuración WS_PRODUCTINFO_URL / WS_PRODUCTINFO_API_KEY en la base activa.");

        var endpoint = $"{url.TrimEnd('/')}/ProductInfo/{Uri.EscapeDataString(codigo)}?ApiKey={Uri.EscapeDataString(apiKey)}";
        using var client = httpClientFactory.CreateClient(nameof(BaseMaestraImagenService));
        using var response = await client.GetAsync(endpoint, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return null;

            return new BaseMaestraProductInfoDto
            {
                Description = ReadString(data, "description"),
                ImageLinkToDownload = ReadString(data, "imageLinkToDownload"),
                ImageName = ReadString(data, "imageName"),
                DescriptionBrand = ReadString(data, "descriptionBrand"),
                DescriptionUnitOfMeasurement = ReadString(data, "descriptionUnitOfMeasurement"),
                Prices = ReadPriceList(data)
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "JSON inválido recibido desde Base Maestra.");
            return null;
        }
    }

    private async Task<byte[]?> DownloadProductImageAsync(string codigo, CancellationToken ct)
    {
        var (url, apiKey) = await ResolveServiceConfigAsync(ct);
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Falta la configuración WS_PRODUCTINFO_URL / WS_PRODUCTINFO_API_KEY en la base activa.");

        var endpoint = $"{url.TrimEnd('/')}/ProductImage/{Uri.EscapeDataString(codigo)}?ApiKey={Uri.EscapeDataString(apiKey)}&format=JPG";
        using var client = httpClientFactory.CreateClient(nameof(BaseMaestraImagenService));
        using var response = await client.GetAsync(endpoint, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<byte[]?> DownloadImageAsync(string imageUrl, CancellationToken ct)
    {
        var sourceUrl = NormalizeUrl(imageUrl);
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return null;

        using var client = CreateHttpClient();
        using var response = await client.GetAsync(sourceUrl, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0)
            return null;

        if (IsLikelyImage(bytes, response.Content.Headers.ContentType?.MediaType))
            return bytes;

        var contentType = response.Content.Headers.ContentType?.MediaType?.Trim();
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return null;

        return bytes;
    }

    private HttpClient CreateHttpClient()
    {
        var client = httpClientFactory.CreateClient(nameof(BaseMaestraImagenService));
        client.Timeout = TimeSpan.FromSeconds(ExternalLookupTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AlfaCore/1.0");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es-AR,es;q=0.9,en;q=0.8");
        return client;
    }

    private async Task<ImageCandidateDto?> ResolveBestCandidateAsync(string idArticulo, string codigoBarra, string descripcionArticulo, BaseMaestraProductInfoDto? info, CancellationToken ct)
    {
        if (info is not null && !string.IsNullOrWhiteSpace(info.ImageLinkToDownload))
        {
            var baseImageUrl = NormalizeUrl(info.ImageLinkToDownload);
            if (!string.IsNullOrWhiteSpace(baseImageUrl) && await IsValidImageUrlAsync(baseImageUrl, ct))
            {
                return new ImageCandidateDto
                {
                    Fuente = "Base Maestra",
                    Confianza = "Alta",
                    ImageUrl = baseImageUrl,
                    ImageName = SafeImageName(info.ImageName, codigoBarra, baseImageUrl),
                    MimeType = GuessMimeType(baseImageUrl, "image/jpeg"),
                    Extension = MimeTypeToExtension(GuessMimeType(baseImageUrl, "image/jpeg")),
                    Seleccionada = true
                };
            }
        }

        var commerceCandidates = new List<ImageCandidateDto>();
        if (info?.Prices is { Count: > 0 })
        {
            foreach (var price in info.Prices)
            {
                if (string.IsNullOrWhiteSpace(price.UrlProduct))
                    continue;

                var sourceName = NormalizeCommerceSource(price.Commerce, price.UrlProduct);
                var pageCandidate = await InspectPageForImageAsync(price.UrlProduct, sourceName, codigoBarra, descripcionArticulo, info, ct);
                if (pageCandidate is not null)
                    commerceCandidates.Add(pageCandidate);
            }
        }

        var bestCommerce = PickBestCandidate(commerceCandidates);
        if (bestCommerce is not null)
            return bestCommerce;

        var googleCandidates = await SearchGoogleCandidatesAsync(codigoBarra, descripcionArticulo, info, ct);
        var bestGoogle = PickBestCandidate(googleCandidates);
        return bestGoogle;
    }

    private void ApplyCandidate(BaseMaestraImagenArticuloDto result, ImageCandidateDto candidate, bool hasCurrentImage, string? idClienteFtp, int? idBase)
    {
        result.ImagenEncontrada = true;
        result.ImageName = candidate.ImageName;
        result.ImageUrl = candidate.ImageUrl;
        result.MimeType = candidate.MimeType;
        result.Extension = candidate.Extension;
        result.Fuente = candidate.Fuente;
        result.Confianza = candidate.Confianza;
        result.Estado = hasCurrentImage ? "Ya tiene imagen" : candidate.Confianza == "Alta" ? "Imagen encontrada" : "Revisar";
        result.PreviewUrl = string.Equals(candidate.Fuente, "Base Maestra", StringComparison.OrdinalIgnoreCase)
            ? BuildPreviewUrlFromCodigo(result.CodigoConsulta, idClienteFtp, idBase)
            : BuildPreviewUrl(candidate.ImageUrl, idClienteFtp, idBase);
        result.PuedeSeleccionarse = !hasCurrentImage;
        result.Seleccionado = !hasCurrentImage && candidate.Seleccionada && string.Equals(candidate.Confianza, "Alta", StringComparison.OrdinalIgnoreCase);
        if (!result.Seleccionado && !hasCurrentImage && string.Equals(candidate.Confianza, "Media", StringComparison.OrdinalIgnoreCase))
            result.PuedeSeleccionarse = true;
    }

    private static ImageCandidateDto? PickBestCandidate(List<ImageCandidateDto> candidates)
    {
        if (candidates.Count == 0)
            return null;

        return candidates
            .OrderByDescending(c => ConfidenceWeight(c.Confianza))
            .ThenByDescending(c => SourceWeight(c.Fuente))
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.ImageUrl));
    }

    private async Task<ImageCandidateDto?> InspectPageForImageAsync(string pageUrl, string sourceName, string codigoBarra, string descripcionArticulo, BaseMaestraProductInfoDto? info, CancellationToken ct)
    {
        var normalizedPageUrl = NormalizeUrl(pageUrl);
        if (string.IsNullOrWhiteSpace(normalizedPageUrl))
            return null;

        var html = await DownloadTextAsync(normalizedPageUrl, ct);
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var imageUrl = ExtractBestImageUrl(html, normalizedPageUrl);
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        if (!await IsValidImageUrlAsync(imageUrl, ct))
            return null;

        var exactEan = ContainsExactEan(html, codigoBarra);
        var confidence = AssessConfidence(exactEan, descripcionArticulo, info);
        return new ImageCandidateDto
        {
            Fuente = sourceName,
            Confianza = confidence,
            ImageUrl = imageUrl,
            ImageName = SafeImageName(Path.GetFileNameWithoutExtension(new Uri(imageUrl).AbsolutePath), codigoBarra, imageUrl),
            MimeType = GuessMimeType(imageUrl, string.Empty),
            Extension = MimeTypeToExtension(GuessMimeType(imageUrl, string.Empty)),
            PageUrl = normalizedPageUrl,
            Seleccionada = string.Equals(confidence, "Alta", StringComparison.OrdinalIgnoreCase)
        };
    }

    private async Task<List<ImageCandidateDto>> SearchGoogleCandidatesAsync(string codigoBarra, string descripcionArticulo, BaseMaestraProductInfoDto? info, CancellationToken ct)
    {
        var barcodeCandidates = await SearchGoogleAsync(BuildGoogleBarcodeQuery(codigoBarra), "Google", codigoBarra, descripcionArticulo, info, ct);
        if (barcodeCandidates.Count > 0)
            return barcodeCandidates;

        return await SearchGoogleAsync(BuildGoogleDescriptionQuery(codigoBarra, descripcionArticulo, info), "Google", codigoBarra, descripcionArticulo, info, ct);
    }

    private async Task<List<ImageCandidateDto>> SearchGoogleAsync(string query, string sourceName, string codigoBarra, string descripcionArticulo, BaseMaestraProductInfoDto? info, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var searchUrl = $"https://www.google.com/search?hl=es&num=8&q={Uri.EscapeDataString(query)}";
        var html = await DownloadTextAsync(searchUrl, ct);
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var resultUrls = ExtractGoogleResultUrls(html).Take(5).ToList();
        var candidates = new List<ImageCandidateDto>();
        foreach (var url in resultUrls)
        {
            var candidate = await InspectPageForImageAsync(url, sourceName, codigoBarra, descripcionArticulo, info, ct);
            if (candidate is not null)
                candidates.Add(candidate);
        }

        return candidates;
    }

    private static IEnumerable<string> ExtractGoogleResultUrls(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            yield break;

        var matches = GoogleResultUrlRegex().Matches(html);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches)
        {
            var raw = match.Groups["url"].Value;
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var decoded = Uri.UnescapeDataString(raw);
            if (decoded.StartsWith("http", StringComparison.OrdinalIgnoreCase) && seen.Add(decoded))
                yield return decoded;
        }
    }

    private async Task<string> DownloadTextAsync(string url, CancellationToken ct)
    {
        using var client = CreateHttpClient();
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<bool> IsValidImageUrlAsync(string url, CancellationToken ct)
    {
        var bytes = await DownloadImageAsync(url, ct);
        return bytes is not null && bytes.Length > 0;
    }

    private static string ExtractBestImageUrl(string html, string pageUrl)
    {
        var candidates = new[]
        {
            ExtractMetaContent(html, "og:image"),
            ExtractMetaContent(html, "twitter:image"),
            ExtractJsonLdImage(html)
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => NormalizeUrl(value!, pageUrl))
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        return candidates.FirstOrDefault() ?? string.Empty;
    }

    private static string ExtractMetaContent(string html, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var pattern = $@"<meta[^>]+(?:property|name)=(['""]){Regex.Escape(propertyName)}\1[^>]+content=(['""])(?<value>.*?)\2";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value.Trim()) : string.Empty;
    }

    private static string ExtractJsonLdImage(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var scripts = Regex.Matches(html, @"<script[^>]+type=(['""])application/ld\+json\1[^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        foreach (Match match in scripts)
        {
            var json = WebUtility.HtmlDecode(match.Groups["json"].Value.Trim());
            if (string.IsNullOrWhiteSpace(json))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var image = FindImageInJsonLd(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(image))
                    return image;
            }
            catch (JsonException)
            {
            }
        }

        return string.Empty;
    }

    private static string FindImageInJsonLd(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryReadJsonImage(element, out var image))
                return image;

            foreach (var prop in element.EnumerateObject())
            {
                var nested = FindImageInJsonLd(prop.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindImageInJsonLd(item);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return string.Empty;
    }

    private static bool TryReadJsonImage(JsonElement element, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (element.TryGetProperty("image", out var imageProp))
        {
            if (imageProp.ValueKind == JsonValueKind.String)
            {
                imageUrl = imageProp.GetString()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(imageUrl);
            }

            if (imageProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in imageProp.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        imageUrl = item.GetString()?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(imageUrl))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool ContainsExactEan(string html, string codigoBarra)
        => !string.IsNullOrWhiteSpace(codigoBarra)
           && Regex.IsMatch(html ?? string.Empty, $@"(?<!\d){Regex.Escape(codigoBarra.Trim())}(?!\d)", RegexOptions.CultureInvariant);

    private static string AssessConfidence(bool exactEan, string descripcionArticulo, BaseMaestraProductInfoDto? info)
    {
        if (exactEan)
            return "Alta";

        var score = 0;
        var text = NormalizeSearchText($"{descripcionArticulo} {info?.Description} {info?.DescriptionBrand} {info?.DescriptionUnitOfMeasurement}");
        foreach (var token in TokenizeSearchText(descripcionArticulo))
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                score++;
        }

        if (info is not null && !string.IsNullOrWhiteSpace(info.DescriptionBrand))
        {
            var brand = NormalizeSearchText(info.DescriptionBrand);
            if (!string.IsNullOrWhiteSpace(brand) && text.Contains(brand, StringComparison.OrdinalIgnoreCase))
                score += 2;
        }

        return score >= 4 ? "Media" : "Revisar";
    }

    private static string BuildGoogleBarcodeQuery(string codigoBarra)
    {
        var barcode = (codigoBarra ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(barcode) ? string.Empty : $"\"{barcode}\"";
    }

    private static string BuildGoogleDescriptionQuery(string codigoBarra, string descripcionArticulo, BaseMaestraProductInfoDto? info)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(codigoBarra))
            parts.Add(codigoBarra.Trim());
        if (!string.IsNullOrWhiteSpace(descripcionArticulo))
            parts.Add(descripcionArticulo.Trim());
        if (!string.IsNullOrWhiteSpace(info?.DescriptionBrand))
            parts.Add(info.DescriptionBrand.Trim());

        return string.Join(" ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeSearchText(string value)
    {
        var text = WebUtility.HtmlDecode((value ?? string.Empty).Normalize(NormalizationForm.FormD));
        text = Regex.Replace(text, @"\p{Mn}+", string.Empty);
        text = Regex.Replace(text, @"\s+", " ").Trim().ToLowerInvariant();
        return text;
    }

    private static IEnumerable<string> TokenizeSearchText(string value)
        => NormalizeSearchText(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3 && !StopWords.Contains(token));

    private static string NormalizeCommerceSource(string commerce, string url)
    {
        var trimmed = (commerce ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
            return trimmed;

        try
        {
            var host = new Uri(url).Host.ToLowerInvariant();
            return host switch
            {
                var h when h.Contains("jumbo") => "Jumbo",
                var h when h.Contains("carrefour") => "Carrefour",
                var h when h.Contains("changomas") => "ChangoMás",
                var h when h.Contains("disco") => "Disco",
                _ => "Comercio"
            };
        }
        catch
        {
            return "Comercio";
        }
    }

    private static int ConfidenceWeight(string? confidence)
        => confidence?.Trim().ToLowerInvariant() switch
        {
            "alta" => 3,
            "media" => 2,
            "revisar" => 1,
            _ => 0
        };

    private static int SourceWeight(string? source)
        => source?.Trim().ToLowerInvariant() switch
        {
            "base maestra" => 4,
            "jumbo" => 3,
            "carrefour" => 3,
            "changomás" => 3,
            "chango más" => 3,
            "disco" => 3,
            "google" => 1,
            "web" => 1,
            _ => 2
        };

    private static string SafeImageName(string? imageName, string codigoBarra, string imageUrl)
    {
        var candidate = string.IsNullOrWhiteSpace(imageName)
            ? Path.GetFileNameWithoutExtension(new Uri(imageUrl).AbsolutePath)
            : Path.GetFileNameWithoutExtension(imageName.Trim());

        if (string.IsNullOrWhiteSpace(candidate))
            candidate = codigoBarra;

        return SafeFileSegment(candidate);
    }

    private static string BuildPreviewFileNameFromUrl(string imageUrl)
    {
        try
        {
            var uri = new Uri(imageUrl);
            var fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(fileName))
                return SafeFileSegment(fileName) + ".jpg";
        }
        catch
        {
        }

        return SafeFileSegment(imageUrl) + ".jpg";
    }

    private static string? FindCachedPreviewFile(string cachedFile)
    {
        var baseName = Path.GetFileNameWithoutExtension(cachedFile);
        var directory = Path.GetDirectoryName(cachedFile);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" })
        {
            var candidate = Path.Combine(directory, baseName + ext);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string InferMimeTypeFromPath(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };

    private static string NormalizeUrl(string? url, string? baseUrl = null)
    {
        var trimmed = (url ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            var combined = new Uri(baseUri, trimmed);
            return combined.ToString();
        }

        return trimmed;
    }

    private static bool IsLikelyImage(byte[] bytes, string? mediaType)
    {
        if (bytes.Length < 4)
            return false;

        if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        return HasPrefix(bytes, [0xFF, 0xD8, 0xFF])
               || HasPrefix(bytes, [0x89, 0x50, 0x4E, 0x47])
               || HasPrefix(bytes, [0x47, 0x49, 0x46, 0x38])
               || HasPrefix(bytes, [0x52, 0x49, 0x46, 0x46]);
    }

    private static string GuessMimeType(string url, string fallback)
    {
        var extension = Path.GetExtension(url).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => string.IsNullOrWhiteSpace(fallback) ? "image/jpeg" : fallback
        };
    }

    private static string MimeTypeToExtension(string mimeType)
        => mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };

    private static string DetectMimeType(byte[] bytes, string imageUrl)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            var fromUrl = GuessMimeType(imageUrl, string.Empty);
            if (!string.IsNullOrWhiteSpace(fromUrl))
                return fromUrl;
        }

        return HasPrefix(bytes, [0xFF, 0xD8, 0xFF]) ? "image/jpeg"
             : HasPrefix(bytes, [0x89, 0x50, 0x4E, 0x47]) ? "image/png"
             : HasPrefix(bytes, [0x47, 0x49, 0x46, 0x38]) ? "image/gif"
             : HasPrefix(bytes, [0x42, 0x4D]) ? "image/bmp"
             : "application/octet-stream";
    }

    private static bool HasPrefix(byte[] bytes, ReadOnlySpan<byte> prefix)
    {
        if (bytes.Length < prefix.Length)
            return false;

        return bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "para", "por", "del", "las", "los", "una", "uno", "que", "y", "de", "el", "la", "al", "en", "lt", "ml", "gr", "kg", "pack", "paq", "unidad", "u"
    };

    [GeneratedRegex(@"(?:/url\?q=|/url\?url=)(?<url>[^&""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoogleResultUrlRegex();

    private async Task<(string Url, string ApiKey)> ResolveServiceConfigAsync(CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var hasConfigTable = await TableExistsAsync(cn, "TA_CONFIGURACION", ct);
        var detailColumn = hasConfigTable ? await ResolveConfigDetailColumnAsync(cn, ct) : "VALOR_AUX";

        var url = configuration["WS_PRODUCTINFO_URL"]?.Trim() ?? string.Empty;
        var apiKey = configuration["WS_PRODUCTINFO_API_KEY"]?.Trim() ?? string.Empty;
        var cacheDirectory = configuration["PRODUCTINFO_CACHE_IMAGE_DIRECTORY"]?.Trim() ?? string.Empty;

        if (hasConfigTable)
        {
            url = await EnsureConfigValueAsync(cn, detailColumn, "WS_PRODUCTINFO_URL", url, LegacyProductInfoUrl, ct);
            apiKey = await EnsureConfigValueAsync(cn, detailColumn, "WS_PRODUCTINFO_API_KEY", apiKey, LegacyProductInfoApiKey, ct);
            cacheDirectory = await EnsureConfigValueAsync(cn, detailColumn, "PRODUCTINFO_CACHE_IMAGE_DIRECTORY", cacheDirectory, DefaultCacheDirectory, ct);
        }
        else
        {
            url = !string.IsNullOrWhiteSpace(url) ? url : LegacyProductInfoUrl;
            apiKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : LegacyProductInfoApiKey;
        }

        if (string.IsNullOrWhiteSpace(cacheDirectory))
            cacheDirectory = DefaultCacheDirectory;

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Falta la configuración WS_PRODUCTINFO_URL / WS_PRODUCTINFO_API_KEY en la base activa.");

        return (url, apiKey);
    }

    private async Task<string> ResolveRutaImagenesAsync(CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        if (!await TableExistsAsync(cn, "TA_CONFIGURACION", ct))
            return string.Empty;

        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var row = await cn.QuerySingleOrDefaultAsync<(string Valor, string ValorAux)>(new CommandDefinition(
            $"""
            SELECT TOP (1)
                ISNULL(VALOR, '') AS Valor,
                ISNULL({detailColumn}, '') AS ValorAux
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'RUTAIMAGENES';
            """,
            cancellationToken: ct));

        var raw = string.IsNullOrWhiteSpace(row.Valor) ? row.ValorAux : row.Valor;
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Trim();
        if (Path.IsPathRooted(trimmed))
            return trimmed;

        return Path.Combine(environment.ContentRootPath, trimmed.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private async Task<string> GetPreviewCacheRootAsync(CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var cachePath = configuration["PRODUCTINFO_CACHE_IMAGE_DIRECTORY"]?.Trim() ?? string.Empty;
        if (await TableExistsAsync(cn, "TA_CONFIGURACION", ct))
        {
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
            cachePath = await EnsureConfigValueAsync(cn, detailColumn, "PRODUCTINFO_CACHE_IMAGE_DIRECTORY", cachePath, DefaultCacheDirectory, ct);
        }

        if (string.IsNullOrWhiteSpace(cachePath))
            cachePath = DefaultCacheDirectory;

        var trimmed = cachePath.Trim();
        if (Path.IsPathRooted(trimmed))
            return trimmed;

        return Path.Combine(environment.ContentRootPath, trimmed.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string BuildPreviewFileName(string codigoConsulta, string imageName)
    {
        var baseName = string.IsNullOrWhiteSpace(imageName)
            ? codigoConsulta.Trim()
            : Path.GetFileNameWithoutExtension(imageName.Trim());

        return SafeFileSegment(baseName) + ".jpg";
    }

    private static string BuildOfficialFileName(string idArticulo, string extension)
        => SafeFileSegment(idArticulo) + NormalizeExtension(extension);

    private static string SafeFileSegment(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return "imagen";

        var builder = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                builder.Append(ch);
        }

        return builder.Length == 0 ? "imagen" : builder.ToString();
    }

    private static string NormalizeExtensionFromUrl(string imageUrl, string fallbackExtension, string fallbackMimeType)
    {
        var fromUrl = string.Empty;
        try
        {
            fromUrl = Path.GetExtension(new Uri((imageUrl ?? string.Empty).Trim()).AbsolutePath);
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(fromUrl))
            return NormalizeExtension(fromUrl);

        if (!string.IsNullOrWhiteSpace(fallbackExtension))
            return NormalizeExtension(fallbackExtension);

        return NormalizeExtension(MimeTypeToExtension(fallbackMimeType));
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = (extension ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmed))
            return ".jpg";

        if (!trimmed.StartsWith('.'))
            trimmed = "." + trimmed;

        return trimmed is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp"
            ? (trimmed == ".jpeg" ? ".jpg" : trimmed)
            : ".jpg";
    }

    private static string ResolveCodigoBusqueda(string codigoBarra)
        => (codigoBarra ?? string.Empty).Trim();

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) ? prop.GetString()?.Trim() ?? string.Empty : string.Empty;

    private static List<BaseMaestraCommercePriceDto> ReadPriceList(JsonElement data)
    {
        var prices = new List<BaseMaestraCommercePriceDto>();
        if (!data.TryGetProperty("prices", out var pricesElement) || pricesElement.ValueKind != JsonValueKind.Array)
            return prices;

        foreach (var item in pricesElement.EnumerateArray())
        {
            prices.Add(new BaseMaestraCommercePriceDto
            {
                Commerce = ReadString(item, "commerce"),
                UrlProduct = ReadString(item, "url"),
                CurrentPrice = ReadDecimal(item, "currentPrice"),
                IsAvailable = ReadBool(item, "isAvailable"),
                AvailableQuantity = ReadInt64(item, "availableQuantity"),
                PriceDate = ReadString(item, "priceDate")
            });
        }

        return prices;
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var value))
            return value;

        if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var value))
            return value;

        if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.True)
            return true;
        if (prop.ValueKind == JsonValueKind.False)
            return false;

        if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out var parsed))
            return parsed;

        return false;
    }

    private async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE
                WHEN COL_LENGTH(N'dbo.TA_CONFIGURACION', N'VALOR_AUX') IS NOT NULL THEN N'VALOR_AUX'
                WHEN COL_LENGTH(N'dbo.TA_CONFIGURACION', N'VALORAUX') IS NOT NULL THEN N'VALORAUX'
                ELSE N'VALOR_AUX'
            END;
            """;

        var column = await cn.ExecuteScalarAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
        return string.IsNullOrWhiteSpace(column) ? "VALOR_AUX" : column;
    }

    private async Task<string> EnsureConfigValueAsync(
        SqlConnection cn,
        string detailColumn,
        string key,
        string configuredValue,
        string fallbackValue,
        CancellationToken ct)
    {
        var currentValue = await TryReadConfigValueAsync(cn, detailColumn, key, ct);
        if (!string.IsNullOrWhiteSpace(currentValue))
            return currentValue;

        var resolved = !string.IsNullOrWhiteSpace(configuredValue) ? configuredValue.Trim() : fallbackValue.Trim();
        if (string.IsNullOrWhiteSpace(resolved))
            return string.Empty;

        await SaveConfigValueAsync(cn, detailColumn, key, resolved, ct);
        logger.LogWarning("BaseMaestraImagenService: se repobló {Clave} en TA_CONFIGURACION con un valor por defecto.", key);
        return resolved;
    }

    private async Task<string> TryReadConfigValueAsync(SqlConnection cn, string detailColumn, string key, CancellationToken ct)
    {
        const string sqlTemplate = """
            SELECT TOP (1)
                ISNULL(VALOR, '') AS Valor,
                ISNULL({DETAIL_COLUMN}, '') AS ValorAux
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """;

        var sql = sqlTemplate.Replace("{DETAIL_COLUMN}", detailColumn, StringComparison.Ordinal);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Clave", key.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return string.Empty;

        var value = rd.IsDBNull(0) ? string.Empty : rd.GetString(0);
        var auxValue = rd.IsDBNull(1) ? string.Empty : rd.GetString(1);
        return !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();
    }

    private async Task SaveConfigValueAsync(
        SqlConnection cn,
        string detailColumn,
        string key,
        string value,
        CancellationToken ct)
    {
        var stored = SplitStoredValue(value);
        var sql = $"""
            UPDATE dbo.TA_CONFIGURACION
            SET
                VALOR = @Valor,
                {detailColumn} = @ValorAux,
                GRUPO = @Grupo
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO)
                VALUES (@Clave, @Valor, @ValorAux, @Grupo);
            END;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@ClaveNormalizada", key.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Clave", key.Trim());
        cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
        cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
        cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (normalized.Length > 150)
            return (string.Empty, normalized);

        return (normalized, string.Empty);
    }

    private static object DbNullable(string value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
        => await ObjectExistsAsync(cn, tableName, "U", ct);

    private static async Task<bool> ObjectExistsAsync(SqlConnection cn, string objectName, string? objectType, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE
                WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NOT NULL THEN 1
                WHEN @ObjectType IS NULL AND OBJECT_ID(@ObjectName) IS NOT NULL THEN 1
                ELSE 0
            END;
            """;

        var exists = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { ObjectName = $"dbo.{objectName}", ObjectType = objectType },
            cancellationToken: ct));

        return exists == 1;
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string friendlyMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (AppUserFacingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                module,
                action,
                ex,
                friendlyMessage,
                new
                {
                    Usuario = appUserSession.GetCurrentUserName(Environment.UserName),
                    SesionSql = sessionService.GetActiveSession()?.Nombre
                },
                AppEventSeverity.Warning,
                ct);

            throw new AppUserFacingException($"{friendlyMessage} Código: {incidentId}", incidentId, ex);
        }
    }

    private sealed class BaseMaestraProductInfoDto
    {
        public string Description { get; init; } = string.Empty;
        public string ImageLinkToDownload { get; init; } = string.Empty;
        public string ImageName { get; init; } = string.Empty;
        public string DescriptionBrand { get; init; } = string.Empty;
        public string DescriptionUnitOfMeasurement { get; init; } = string.Empty;
        public List<BaseMaestraCommercePriceDto> Prices { get; init; } = [];
    }

    private sealed class BaseMaestraCommercePriceDto
    {
        public string Commerce { get; init; } = string.Empty;
        public string UrlProduct { get; init; } = string.Empty;
        public decimal? CurrentPrice { get; init; }
        public bool IsAvailable { get; init; }
        public long? AvailableQuantity { get; init; }
        public string PriceDate { get; init; } = string.Empty;
    }

    private sealed class ImageCandidateDto
    {
        public string Fuente { get; init; } = string.Empty;
        public string Confianza { get; init; } = string.Empty;
        public string ImageUrl { get; init; } = string.Empty;
        public string ImageName { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public string Extension { get; init; } = string.Empty;
        public string PageUrl { get; init; } = string.Empty;
        public bool Seleccionada { get; init; }
    }
}

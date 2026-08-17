using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace AlfaCore.Services;

public interface IArticuloImagenFtpService
{
    Task<ArticuloImagenArchivoDto?> ObtenerImagenAsync(string idCliente, int? idBase, string idArticulo, bool thumbnail = false, CancellationToken ct = default);
}

public sealed class ArticuloImagenArchivoDto
{
    public string RutaCompleta { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/jpeg";
}

public sealed class ArticuloImagenFtpService(IHostEnvironment environment, ILogger<ArticuloImagenFtpService> logger) : IArticuloImagenFtpService
{
    private const string FtpHost = "alfanet.ddns.net";
    private const string FtpUsuario = "ftpalfa";
    private const string FtpClave = "24681012";
    private static readonly string[] ExtensionesSoportadas = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];
    private static readonly TimeSpan CacheMissTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, DateTime> MissCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ArticuloImagenArchivoDto?> ObtenerImagenAsync(string idCliente, int? idBase, string idArticulo, bool thumbnail = false, CancellationToken ct = default)
    {
        var cliente = NormalizeSegment(idCliente);
        var articulo = NormalizeSegment(idArticulo);
        if (string.IsNullOrWhiteSpace(cliente) || string.IsNullOrWhiteSpace(articulo))
            return null;

        var baseSegment = idBase is > 0 ? idBase.Value.ToString() : null;
        var carpeta = thumbnail ? "thumbs4" : "imagenes";
        var remoteDir = baseSegment is null ? $"{cliente}/{carpeta}" : $"{cliente}/{baseSegment}/{carpeta}";
        var cacheDir = baseSegment is null
            ? Path.Combine(environment.ContentRootPath, "App_Data", "cache", "imagenes-articulos", cliente, carpeta)
            : Path.Combine(environment.ContentRootPath, "App_Data", "cache", "imagenes-articulos", cliente, baseSegment, carpeta);

        foreach (var ext in ExtensionesSoportadas)
        {
            var cachedPath = Path.Combine(cacheDir, articulo + ext);
            if (File.Exists(cachedPath))
                return new ArticuloImagenArchivoDto { RutaCompleta = cachedPath, MimeType = MimeTypeFor(ext) };
        }

        var missKey = $"{remoteDir}/{articulo}";
        if (MissCache.TryGetValue(missKey, out var missedAt) && DateTime.UtcNow - missedAt < CacheMissTtl)
            return null;

        Directory.CreateDirectory(cacheDir);

        foreach (var ext in ExtensionesSoportadas)
        {
            var bytes = await TryDownloadAsync(remoteDir, articulo + ext, ct);
            if (bytes is null)
                continue;

            var destino = Path.Combine(cacheDir, articulo + ext);
            await File.WriteAllBytesAsync(destino, bytes, ct);
            MissCache.TryRemove(missKey, out _);
            return new ArticuloImagenArchivoDto { RutaCompleta = destino, MimeType = MimeTypeFor(ext) };
        }

        MissCache[missKey] = DateTime.UtcNow;
        return null;
    }

    private async Task<byte[]?> TryDownloadAsync(string remoteDir, string nombreArchivo, CancellationToken ct)
    {
        try
        {
            var uri = new Uri($"ftp://{FtpHost}/Clientes/{remoteDir}/{nombreArchivo}");
#pragma warning disable SYSLIB0014, CS0618
            var request = (FtpWebRequest)WebRequest.Create(uri);
            request.Method = WebRequestMethods.Ftp.DownloadFile;
            request.Credentials = new NetworkCredential(FtpUsuario, FtpClave);
            request.UsePassive = true;
            request.UseBinary = true;

            using var response = (FtpWebResponse)await request.GetResponseAsync();
#pragma warning restore SYSLIB0014, CS0618
            await using var responseStream = response.GetResponseStream();
            using var ms = new MemoryStream();
            await responseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (WebException)
        {
            // Archivo inexistente en el FTP para esta extension: es un caso esperado, no un error.
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo descargar del FTP la imagen de articulo {Archivo}", nombreArchivo);
            return null;
        }
    }

    private static string NormalizeSegment(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var builder = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
                builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string MimeTypeFor(string ext) => ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };
}

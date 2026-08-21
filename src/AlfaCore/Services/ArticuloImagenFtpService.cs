using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace AlfaCore.Services;

public interface IArticuloImagenFtpService
{
    Task<ArticuloImagenArchivoDto?> ObtenerImagenAsync(string idCliente, int? idBase, string idArticulo, bool thumbnail = false, CancellationToken ct = default);
    Task<bool> SubirImagenAsync(string idCliente, int? idBase, string idArticulo, string extension, Stream contenido, bool thumbnail = false, CancellationToken ct = default);
    Task EliminarImagenAsync(string idCliente, int? idBase, string idArticulo, bool thumbnail = false, CancellationToken ct = default);
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
    private static readonly string[] ExtensionesSoportadas = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"];
    private static readonly TimeSpan CacheMissTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, DateTime> MissCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DirectoryListingTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, (DateTime CachedAt, HashSet<string> Archivos)> DirectoryListingCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ArticuloImagenArchivoDto?> ObtenerImagenAsync(string idCliente, int? idBase, string idArticulo, bool thumbnail = false, CancellationToken ct = default)
    {
        var cliente = NormalizeSegment(idCliente);
        var articulo = NormalizeSegment(idArticulo);
        if (string.IsNullOrWhiteSpace(cliente) || string.IsNullOrWhiteSpace(articulo))
            return null;

        var baseSegment = idBase is > 0 ? idBase.Value.ToString() : null;
        var rootCliente = baseSegment is null ? cliente : $"{cliente}/{baseSegment}";
        var carpetasCandidatas = thumbnail
            ? new[] { "thumbs4", "imagenes" }
            : new[] { "imagenes", "thumbs4" };

        foreach (var carpeta in carpetasCandidatas)
        {
            var remoteDir = baseSegment is null ? $"{cliente}/{carpeta}" : $"{cliente}/{baseSegment}/{carpeta}";
            var relativoCache = baseSegment is null ? Path.Combine(cliente, carpeta) : Path.Combine(cliente, baseSegment, carpeta);
            var cacheDir = Path.Combine(environment.ContentRootPath, "App_Data", "cache", "imagenes-articulos", relativoCache);
            // Algunas instalaciones (ej. sitios IIS) no le dan permiso de escritura al identity del
            // sitio sobre App_Data/cache. Se usa una carpeta temporal como respaldo para no romper el
            // servicio de imágenes por eso: se sigue sirviendo (sin cachear en App_Data) en vez de fallar.
            var cacheDirRespaldo = Path.Combine(Path.GetTempPath(), "alfacore-imagenes-articulos", relativoCache);

            foreach (var dirCandidatoCache in new[] { cacheDir, cacheDirRespaldo })
            {
                foreach (var ext in ExtensionesSoportadas)
                {
                    var cachedPath = Path.Combine(dirCandidatoCache, articulo + ext);
                    if (File.Exists(cachedPath))
                        return new ArticuloImagenArchivoDto { RutaCompleta = cachedPath, MimeType = MimeTypeFor(ext) };
                }
            }

            var missKey = $"{remoteDir}/{articulo}";
            if (MissCache.TryGetValue(missKey, out var missedAt) && DateTime.UtcNow - missedAt < CacheMissTtl)
                continue;

            var remoteDirsCandidatos = thumbnail
                ? new[]
                {
                    baseSegment is null ? $"{cliente}/imagenes/thumbs4" : $"{cliente}/{baseSegment}/imagenes/thumbs4",
                    remoteDir,
                    baseSegment is null ? $"{cliente}/imagenes" : $"{cliente}/{baseSegment}/imagenes"
                }
                : new[]
                {
                    remoteDir,
                    baseSegment is null ? $"{cliente}/thumbs4" : $"{cliente}/{baseSegment}/thumbs4"
                };

            foreach (var dirCandidato in remoteDirsCandidatos)
            {
                // Listar la carpeta una vez (y compartir esa lista entre todos los artículos que piden
                // imagen de esa misma carpeta) evita tener que probar cada extensión con una descarga
                // FTP completa por artículo — con catálogos grandes eso era muy lento, sobre todo para
                // los artículos que no tienen imagen (antes probaban las 6 extensiones igual, una por una).
                var listado = await GetDirectoryListingAsync(dirCandidato, ct);
                var extensionesAProbar = listado is not null
                    ? ExtensionesSoportadas.Where(ext => listado.Contains(articulo + ext))
                    : ExtensionesSoportadas;

                foreach (var ext in extensionesAProbar)
                {
                    var bytes = await TryDownloadAsync(dirCandidato, articulo + ext, ct);
                    if (bytes is null)
                        continue;

                    var destino = await GuardarEnCacheAsync(cacheDir, cacheDirRespaldo, articulo + ext, bytes, ct);
                    MissCache.TryRemove(missKey, out _);
                    return new ArticuloImagenArchivoDto { RutaCompleta = destino, MimeType = MimeTypeFor(ext) };
                }
            }
        }

        MissCache[$"{rootCliente}/imagenes/{articulo}"] = DateTime.UtcNow;
        return null;
    }

    private async Task<string> GuardarEnCacheAsync(string cacheDir, string cacheDirRespaldo, string nombreArchivo, byte[] bytes, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            var destino = Path.Combine(cacheDir, nombreArchivo);
            await File.WriteAllBytesAsync(destino, bytes, ct);
            return destino;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(ex, "No se pudo escribir la cache de imagenes en {Carpeta}; se usa una carpeta temporal.", cacheDir);
            Directory.CreateDirectory(cacheDirRespaldo);
            var destinoRespaldo = Path.Combine(cacheDirRespaldo, nombreArchivo);
            await File.WriteAllBytesAsync(destinoRespaldo, bytes, ct);
            return destinoRespaldo;
        }
    }

    public async Task<bool> SubirImagenAsync(string idCliente, int? idBase, string idArticulo, string extension, Stream contenido, bool thumbnail = false, CancellationToken ct = default)
    {
        var cliente = NormalizeSegment(idCliente);
        var articulo = NormalizeSegment(idArticulo);
        var ext = NormalizeExtension(extension);
        if (string.IsNullOrWhiteSpace(cliente) || string.IsNullOrWhiteSpace(articulo) || string.IsNullOrWhiteSpace(ext))
            return false;

        var baseSegment = idBase is > 0 ? idBase.Value.ToString() : null;
        var carpeta = thumbnail ? "thumbs4" : "imagenes";
        var remoteDir = baseSegment is null ? $"{cliente}/{carpeta}" : $"{cliente}/{baseSegment}/{carpeta}";

        using var ms = new MemoryStream();
        await contenido.CopyToAsync(ms, ct);
        var subido = await TryUploadAsync(remoteDir, articulo + ext, ms.ToArray(), ct);
        if (!subido)
            return false;

        LimpiarCacheLocal(cliente, baseSegment, carpeta, articulo);
        return true;
    }

    public async Task EliminarImagenAsync(string idCliente, int? idBase, string idArticulo, bool thumbnail = false, CancellationToken ct = default)
    {
        var cliente = NormalizeSegment(idCliente);
        var articulo = NormalizeSegment(idArticulo);
        if (string.IsNullOrWhiteSpace(cliente) || string.IsNullOrWhiteSpace(articulo))
            return;

        var baseSegment = idBase is > 0 ? idBase.Value.ToString() : null;
        var carpeta = thumbnail ? "thumbs4" : "imagenes";
        var remoteDir = baseSegment is null ? $"{cliente}/{carpeta}" : $"{cliente}/{baseSegment}/{carpeta}";

        foreach (var ext in ExtensionesSoportadas)
            await TryDeleteAsync(remoteDir, articulo + ext, ct);

        LimpiarCacheLocal(cliente, baseSegment, carpeta, articulo);
    }

    private void LimpiarCacheLocal(string cliente, string? baseSegment, string carpeta, string articulo)
    {
        var relativoCache = baseSegment is null ? Path.Combine(cliente, carpeta) : Path.Combine(cliente, baseSegment, carpeta);
        var cacheDir = Path.Combine(environment.ContentRootPath, "App_Data", "cache", "imagenes-articulos", relativoCache);
        var cacheDirRespaldo = Path.Combine(Path.GetTempPath(), "alfacore-imagenes-articulos", relativoCache);

        foreach (var dir in new[] { cacheDir, cacheDirRespaldo })
        {
            foreach (var ext in ExtensionesSoportadas)
            {
                var cachedPath = Path.Combine(dir, articulo + ext);
                if (File.Exists(cachedPath))
                {
                    try { File.Delete(cachedPath); } catch { }
                }
            }
        }

        var remoteDir = baseSegment is null ? $"{cliente}/{carpeta}" : $"{cliente}/{baseSegment}/{carpeta}";
        MissCache.TryRemove($"{remoteDir}/{articulo}", out _);
        DirectoryListingCache.TryRemove(remoteDir, out _);
    }

    private async Task<bool> TryUploadAsync(string remoteDir, string nombreArchivo, byte[] bytes, CancellationToken ct)
    {
        try
        {
            await EnsureRemoteDirectoryAsync(remoteDir, ct);

            var uri = new Uri($"ftp://{FtpHost}/Clientes/{remoteDir}/{nombreArchivo}");
#pragma warning disable SYSLIB0014, CS0618
            var request = (FtpWebRequest)WebRequest.Create(uri);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential(FtpUsuario, FtpClave);
            request.UsePassive = true;
            request.UseBinary = true;
            request.ContentLength = bytes.Length;

            await using (var requestStream = await request.GetRequestStreamAsync())
            {
                await requestStream.WriteAsync(bytes, ct);
            }

            using var response = (FtpWebResponse)await request.GetResponseAsync();
#pragma warning restore SYSLIB0014, CS0618
            return response.StatusCode is FtpStatusCode.ClosingData or FtpStatusCode.FileActionOK;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo subir al FTP el archivo {Archivo}", nombreArchivo);
            return false;
        }
    }

    private async Task EnsureRemoteDirectoryAsync(string remoteDir, CancellationToken ct)
    {
        var segmentos = remoteDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var acumulado = "Clientes";
        foreach (var segmento in segmentos)
        {
            acumulado = $"{acumulado}/{segmento}";
            await TryCreateDirectoryAsync(acumulado, ct);
        }
    }

    private async Task TryCreateDirectoryAsync(string remotePath, CancellationToken ct)
    {
        try
        {
            var uri = new Uri($"ftp://{FtpHost}/{remotePath}");
#pragma warning disable SYSLIB0014, CS0618
            var request = (FtpWebRequest)WebRequest.Create(uri);
            request.Method = WebRequestMethods.Ftp.MakeDirectory;
            request.Credentials = new NetworkCredential(FtpUsuario, FtpClave);
            request.UsePassive = true;

            using var response = (FtpWebResponse)await request.GetResponseAsync();
#pragma warning restore SYSLIB0014, CS0618
        }
        catch (WebException)
        {
            // La carpeta ya existe (caso normal) o no se pudo crear: se intenta igual la subida
            // y, si el problema era real, va a fallar ahí con un error más específico.
        }
    }

    private async Task TryDeleteAsync(string remoteDir, string nombreArchivo, CancellationToken ct)
    {
        try
        {
            var uri = new Uri($"ftp://{FtpHost}/Clientes/{remoteDir}/{nombreArchivo}");
#pragma warning disable SYSLIB0014, CS0618
            var request = (FtpWebRequest)WebRequest.Create(uri);
            request.Method = WebRequestMethods.Ftp.DeleteFile;
            request.Credentials = new NetworkCredential(FtpUsuario, FtpClave);
            request.UsePassive = true;

            using var response = (FtpWebResponse)await request.GetResponseAsync();
#pragma warning restore SYSLIB0014, CS0618
        }
        catch (WebException)
        {
            // Archivo inexistente para esta extension: caso esperado, no es un error.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo borrar del FTP el archivo {Archivo}", nombreArchivo);
        }
    }

    private static string NormalizeExtension(string? extension)
    {
        var trimmed = (extension ?? string.Empty).Trim().ToLowerInvariant();
        if (trimmed.Length == 0)
            return string.Empty;
        if (!trimmed.StartsWith('.'))
            trimmed = "." + trimmed;
        return Array.IndexOf(ExtensionesSoportadas, trimmed) >= 0 ? trimmed : string.Empty;
    }

    private async Task<HashSet<string>?> GetDirectoryListingAsync(string remoteDir, CancellationToken ct)
    {
        if (DirectoryListingCache.TryGetValue(remoteDir, out var cached) && DateTime.UtcNow - cached.CachedAt < DirectoryListingTtl)
            return cached.Archivos;

        try
        {
            var uri = new Uri($"ftp://{FtpHost}/Clientes/{remoteDir}/");
#pragma warning disable SYSLIB0014, CS0618
            var request = (FtpWebRequest)WebRequest.Create(uri);
            request.Method = WebRequestMethods.Ftp.ListDirectory;
            request.Credentials = new NetworkCredential(FtpUsuario, FtpClave);
            request.UsePassive = true;

            using var response = (FtpWebResponse)await request.GetResponseAsync();
            await using var responseStream = response.GetResponseStream();
#pragma warning restore SYSLIB0014, CS0618
            using var reader = new StreamReader(responseStream);
            var contenido = await reader.ReadToEndAsync(ct);
            var archivos = new HashSet<string>(
                contenido.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()),
                StringComparer.OrdinalIgnoreCase);

            DirectoryListingCache[remoteDir] = (DateTime.UtcNow, archivos);
            return archivos;
        }
        catch (Exception ex)
        {
            // Si no se puede listar (servidor que no lo permite, corte puntual, etc.) se vuelve al
            // modo anterior de probar cada extensión por descarga directa, sin romper el servicio.
            logger.LogWarning(ex, "No se pudo listar la carpeta FTP {Carpeta}; se prueba cada extensión por descarga.", remoteDir);
            return null;
        }
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
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
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
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };
}

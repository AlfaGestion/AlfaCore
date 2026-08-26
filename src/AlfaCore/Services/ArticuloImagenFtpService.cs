using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace AlfaCore.Services;

public interface IArticuloImagenFtpService
{
    /// <param name="forzarRecarga">
    /// True cuando el llamador ya sabe que la imagen remota cambió (ej. V_MA_ARTICULOS.ModificoImagen)
    /// y no hay que confiar en nada de lo que esté cacheado localmente — se borra cualquier copia
    /// existente (imagenes y thumbs4, todas las extensiones) y se vuelve a descargar del FTP.
    /// </param>
    Task<ArticuloImagenArchivoDto?> ObtenerImagenAsync(string idCliente, int? idBase, string idArticulo, bool thumbnail = false, bool forzarRecarga = false, CancellationToken ct = default);
    Task<bool> SubirImagenAsync(string idCliente, int? idBase, string idArticulo, string extension, Stream contenido, bool thumbnail = false, CancellationToken ct = default);
    Task EliminarImagenAsync(string idCliente, int? idBase, string idArticulo, bool thumbnail = false, CancellationToken ct = default);

    /// <summary>
    /// Descarga/cachea en paralelo (con concurrencia acotada) las imágenes de varios artículos —
    /// pensado para "precalentar" el caché local apenas se conoce la lista de artículos de un
    /// catálogo, antes de que el browser empiece a pedir cada &lt;img&gt; una por una. Best-effort:
    /// un artículo que falla no interrumpe a los demás.
    /// </summary>
    /// <param name="idArticulosForzados">
    /// Subconjunto de <paramref name="idArticulos"/> cuya imagen cambió (ModificoImagen) y debe
    /// redescargarse salteando cualquier caché local, igual que <see cref="ObtenerImagenAsync"/>
    /// con <c>forzarRecarga: true</c>.
    /// </param>
    Task PrecalentarAsync(string idCliente, int? idBase, IEnumerable<string> idArticulos, bool thumbnail = false, IReadOnlySet<string>? idArticulosForzados = null, CancellationToken ct = default);
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

    // Los .bmp no llevan compresión: un thumbnail generado mal (proceso externo que lo trunca o
    // lo deja casi vacío) queda muy por debajo de lo que pesa cualquier imagen real, aunque sea
    // chica — a diferencia de jpg/png/webp, donde un archivo liviano es normal para un thumb
    // legítimo. Por eso el piso de "tamaño sospechoso" sólo se aplica a .bmp.
    private const long TamanioMinimoBmpBytes = 4096;

    // Un hit de caché positivo antes vivía para siempre: si alguien reemplazaba la imagen a mano
    // en el FTP (sin pasar por SubirImagenAsync, el único camino que hoy invalida la caché), el
    // catálogo seguía sirviendo la vieja indefinidamente — había que borrar el archivo cacheado a
    // mano en el servidor para que se corrigiera. Con este tope, pasado ese tiempo se intenta
    // refrescar contra el FTP; si el FTP no responde, se sigue sirviendo la copia vieja en vez de
    // dejar al visitante sin imagen (ver ObtenerImagenAsync).
    private static readonly TimeSpan CacheHitMaxAge = TimeSpan.FromHours(12);
    private static readonly TimeSpan CacheMissTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, DateTime> MissCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DirectoryListingTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, (DateTime CachedAt, HashSet<string> Archivos)> DirectoryListingCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ArticuloImagenArchivoDto?> ObtenerImagenAsync(string idCliente, int? idBase, string idArticulo, bool thumbnail = false, bool forzarRecarga = false, CancellationToken ct = default)
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

        if (forzarRecarga)
        {
            // El caller ya sabe (V_MA_ARTICULOS.ModificoImagen) que la imagen remota cambió: se
            // borra cualquier copia cacheada en ambas carpetas antes de mirar el caché, así el
            // resto del método siempre termina yendo a buscar la versión nueva al FTP.
            LimpiarCacheLocal(cliente, baseSegment, "imagenes", articulo);
            LimpiarCacheLocal(cliente, baseSegment, "thumbs4", articulo);
        }

        // Si el único hit de caché que aparece está vencido (ver CacheHitMaxAge), se intenta
        // refrescar contra el FTP más abajo en vez de servirlo directo — pero se lo guarda acá
        // como red de seguridad: si el refresh falla (FTP caído, timeout), se sirve esta copia
        // vieja antes que dejar al visitante sin imagen.
        ArticuloImagenArchivoDto? fallbackVencido = null;

        // Distingue "el FTP realmente no tiene esta imagen" (550, seguro cachear como miss por 10
        // minutos) de "hubo un problema de conexión/timeout" (nada seguro de asumir). Antes ambos
        // casos se trataban igual: si 100+ artículos pedían su imagen a la vez apenas se abría un
        // catálogo por primera vez y el FTP rechazaba algunas conexiones por la ráfaga, esas
        // imágenes quedaban "no encontradas" en caché 10 minutos aunque el FTP las tuviera —de ahí
        // que hiciera falta recargar la página (y esperar) para que se sirvieran bien.
        var huboFalloTransitorio = false;

        foreach (var carpeta in carpetasCandidatas)
        {
            var remoteDir = baseSegment is null ? $"{cliente}/{carpeta}" : $"{cliente}/{baseSegment}/{carpeta}";
            var relativoCache = baseSegment is null ? Path.Combine(cliente, carpeta) : Path.Combine(cliente, baseSegment, carpeta);
            var cacheDir = Path.Combine(environment.ContentRootPath, "App_Data", "cache", "imagenes-articulos", relativoCache);
            // Algunas instalaciones (ej. sitios IIS) no le dan permiso de escritura al identity del
            // sitio sobre App_Data/cache. Se usa una carpeta temporal como respaldo para no romper el
            // servicio de imágenes por eso: se sigue sirviendo (sin cachear en App_Data) en vez de fallar.
            var cacheDirRespaldo = Path.Combine(Path.GetTempPath(), "alfacore-imagenes-articulos", relativoCache);

            var huboHitVencido = false;

            foreach (var dirCandidatoCache in new[] { cacheDir, cacheDirRespaldo })
            {
                foreach (var ext in ExtensionesSoportadas)
                {
                    var cachedPath = Path.Combine(dirCandidatoCache, articulo + ext);
                    if (!File.Exists(cachedPath))
                        continue;

                    var tamanio = new FileInfo(cachedPath).Length;
                    if (!EsTamanioPlausible(ext, tamanio))
                    {
                        logger.LogWarning(
                            "Imagen cacheada {Ruta} descartada por tamaño sospechoso ({Bytes} bytes); se prueba otra fuente.",
                            cachedPath, tamanio);
                        TryDeleteArchivoLocal(cachedPath);
                        continue;
                    }

                    var antiguedad = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedPath);
                    if (antiguedad > CacheHitMaxAge)
                    {
                        fallbackVencido ??= new ArticuloImagenArchivoDto { RutaCompleta = cachedPath, MimeType = MimeTypeFor(ext) };
                        huboHitVencido = true;
                        continue;
                    }

                    return new ArticuloImagenArchivoDto { RutaCompleta = cachedPath, MimeType = MimeTypeFor(ext) };
                }
            }

            var missKey = $"{remoteDir}/{articulo}";

            // Un hit vencido siempre amerita reintentar contra el FTP, aunque haya un miss reciente
            // cacheado para esta carpeta (ese miss era de antes de que existiera el archivo local).
            if (huboHitVencido)
                MissCache.TryRemove(missKey, out _);
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
                if (listado is null)
                {
                    // No se pudo ni listar la carpeta: no hay forma de saber si el artículo
                    // realmente no tiene imagen ahí, así que no se asume que falta.
                    huboFalloTransitorio = true;
                }

                var extensionesAProbar = listado is not null
                    ? ExtensionesSoportadas.Where(ext => listado.Contains(articulo + ext))
                    : ExtensionesSoportadas;

                foreach (var ext in extensionesAProbar)
                {
                    var (bytes, transitorio) = await TryDownloadAsync(dirCandidato, articulo + ext, ct);
                    if (transitorio)
                        huboFalloTransitorio = true;

                    if (bytes is null)
                        continue;

                    if (!EsTamanioPlausible(ext, bytes.LongLength))
                    {
                        logger.LogWarning(
                            "Imagen descargada {Carpeta}/{Archivo} descartada por tamaño sospechoso ({Bytes} bytes); se prueba otra fuente.",
                            dirCandidato, articulo + ext, bytes.LongLength);
                        continue;
                    }

                    var destino = await GuardarEnCacheAsync(cacheDir, cacheDirRespaldo, articulo + ext, bytes, ct);
                    MissCache.TryRemove(missKey, out _);
                    return new ArticuloImagenArchivoDto { RutaCompleta = destino, MimeType = MimeTypeFor(ext) };
                }
            }
        }

        if (fallbackVencido is not null)
        {
            logger.LogDebug(
                "No se pudo refrescar {Ruta} contra el FTP; se sigue sirviendo la copia cacheada (vencida).",
                fallbackVencido.RutaCompleta);
            return fallbackVencido;
        }

        if (!huboFalloTransitorio)
            MissCache[$"{rootCliente}/imagenes/{articulo}"] = DateTime.UtcNow;

        return null;
    }

    // Precalentar de a muchos a la vez, pero con un tope: el FTP remoto es un servicio compartido
    // y abrir decenas de conexiones simultáneas (una por artículo) puede ser peor que ayudar. Ahora
    // que el caller espera este precalentado (con un timeout duro) antes de mostrar el catálogo,
    // conviene ir más rápido que cuando era sólo en segundo plano — de ahí 16 en vez de 8.
    private const int PrecalentamientoConcurrencia = 16;

    public async Task PrecalentarAsync(string idCliente, int? idBase, IEnumerable<string> idArticulos, bool thumbnail = false, IReadOnlySet<string>? idArticulosForzados = null, CancellationToken ct = default)
    {
        var codigos = idArticulos
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (codigos.Length == 0)
            return;

        using var semaforo = new SemaphoreSlim(PrecalentamientoConcurrencia);

        async Task PrecalentarUnoAsync(string articulo)
        {
            await semaforo.WaitAsync(ct);
            try
            {
                var forzar = idArticulosForzados?.Contains(articulo) == true;
                await ObtenerImagenAsync(idCliente, idBase, articulo, thumbnail, forzar, ct);
            }
            catch (Exception ex)
            {
                // Best-effort: si un artículo falla, no debe frenar a los demás ni al catálogo en
                // sí — la imagen igual se va a intentar servir (y loguear si vuelve a fallar)
                // cuando el browser la pida.
                logger.LogDebug(ex, "Precalentado de imagen falló para {Articulo}; se sigue con el resto.", articulo);
            }
            finally
            {
                semaforo.Release();
            }
        }

        await Task.WhenAll(codigos.Select(PrecalentarUnoAsync));
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

    /// <summary>
    /// Transitorio=true significa "no se pudo confirmar si el archivo existe o no" (timeout, conexión
    /// rechazada, etc.) — el caller no debe cachearlo como "no existe" en ese caso. Transitorio=false
    /// con Bytes=null es el 550 "File not found" real de la FTP: eso sí es seguro cachear como miss.
    /// </summary>
    private async Task<(byte[]? Bytes, bool Transitorio)> TryDownloadAsync(string remoteDir, string nombreArchivo, CancellationToken ct)
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
            return (ms.ToArray(), false);
        }
        catch (WebException ex) when (EsArchivoInexistente(ex))
        {
            // Archivo inexistente en el FTP para esta extension: es un caso esperado, no un error.
            return (null, false);
        }
        catch (Exception ex)
        {
            // Timeout, conexión rechazada por demasiadas simultáneas, corte de red, etc. — no hay
            // forma de saber si la imagen existe o no, así que esto NO debe cachearse como "miss".
            logger.LogWarning(ex, "No se pudo descargar del FTP la imagen de articulo {Archivo} (fallo transitorio)", nombreArchivo);
            return (null, true);
        }
    }

    private static bool EsArchivoInexistente(WebException ex)
        => ex.Response is FtpWebResponse ftpResponse
           && ftpResponse.StatusCode is FtpStatusCode.ActionNotTakenFileUnavailable or FtpStatusCode.ActionNotTakenFilenameNotAllowed;

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

    private static bool EsTamanioPlausible(string ext, long tamanioBytes)
        => !string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase) || tamanioBytes >= TamanioMinimoBmpBytes;

    private static void TryDeleteArchivoLocal(string path)
    {
        try { File.Delete(path); }
        catch { }
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

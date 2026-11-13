using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

/// <summary>
/// Guarda los archivos de Conversaciones por contenido: un archivo por SHA-256 dentro de la carpeta de la base
/// (<c>_contenido/ab/abcdef….pdf</c>). El mismo PDF enviado o recibido N veces ocupa disco una sola vez; antes
/// cada mensaje grababa una copia con un Guid nuevo y un catálogo de 36 MB llegó a sumar 16 GB en 6 días
/// (2026-11-13). Cada adjunto sigue teniendo su fila en CONV_ADJUNTOS con su RutaLocal: varias filas pueden
/// apuntar al mismo archivo, por eso estos archivos no se borran nunca desde el flujo de un mensaje.
/// </summary>
internal static partial class ConversacionesAdjuntosAlmacen
{
    internal const string CarpetaContenido = "_contenido";

    internal static async Task<string> GuardarAsync(string raizBase, byte[] contenido, string nombreOExtension, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raizBase);
        ArgumentNullException.ThrowIfNull(contenido);

        var hash = Convert.ToHexString(SHA256.HashData(contenido)).ToLowerInvariant();
        var carpeta = Path.Combine(raizBase, CarpetaContenido, hash[..2]);
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, hash + Extension(nombreOExtension));
        if (ExisteCompleto(ruta, contenido.LongLength))
            return ruta;

        // Se escribe en un temporal y se renombra: un lector (o un segundo envío del mismo archivo en paralelo)
        // nunca ve un archivo a medio escribir.
        var temporal = $"{ruta}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporal, contenido, ct);
            File.Move(temporal, ruta, overwrite: true);
        }
        catch (IOException) when (ExisteCompleto(ruta, contenido.LongLength))
        {
            // Otro proceso lo grabó primero (o lo tiene abierto para servirlo): mismo hash, mismo contenido.
        }
        finally
        {
            if (File.Exists(temporal))
                File.Delete(temporal);
        }

        return ruta;
    }

    private static bool ExisteCompleto(string ruta, long largo)
    {
        var info = new FileInfo(ruta);
        return info.Exists && info.Length == largo;
    }

    // La extensión viene del nombre que manda el cliente o Meta: solo letras y números, para que no se cuele
    // nada raro en la ruta.
    private static string Extension(string nombreOExtension)
    {
        var ext = Path.GetExtension(nombreOExtension ?? string.Empty);
        if (string.IsNullOrEmpty(ext) && (nombreOExtension ?? string.Empty).StartsWith('.'))
            ext = nombreOExtension!;
        ext = ext.TrimStart('.').ToLowerInvariant();
        return ext.Length is > 0 and <= 10 && ExtensionValida().IsMatch(ext) ? "." + ext : string.Empty;
    }

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex ExtensionValida();
}

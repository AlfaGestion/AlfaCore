using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AlfaCore.Models;

/// <summary>
/// Clave pública que identifica la base en el snippet del widget (<c>data-site="AC-..."</c>). Es
/// visible para cualquiera en el HTML del sitio: por eso es distinta del WebhookToken de la base y
/// solo habilita los endpoints del chat web.
/// </summary>
public static partial class WebChatSiteKeys
{
    public static string Generate()
        => "AC-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Filtra basura antes de consultar la base central por la clave.</summary>
    public static bool IsValidFormat(string? siteKey)
        => !string.IsNullOrWhiteSpace(siteKey) && SiteKeyRegex().IsMatch(siteKey.Trim());

    [GeneratedRegex("^AC-[a-f0-9]{32}$")]
    private static partial Regex SiteKeyRegex();
}

/// <summary>
/// Mensaje que manda el widget de chat web embebido en un sitio externo (POST
/// /api/webchat/{siteKey}/mensajes). Llega de un visitante anónimo: todo se normaliza y acota con
/// <see cref="TryNormalize"/> antes de tocar la base.
/// </summary>
public sealed partial class WebChatMensajeEntranteRequest
{
    public const int MaxTexto = 2000;
    public const int MaxNombre = 80;
    public const int MaxPaginaUrl = 1000;
    public const int MaxContexto = 200;

    /// <summary>Id aleatorio que genera el widget y persiste en localStorage del navegador del visitante.</summary>
    public string VisitorId { get; set; } = string.Empty;

    /// <summary>Id del mensaje generado por el widget: hace idempotente el reintento de un POST.</summary>
    public string? ClientMessageId { get; set; }

    public string Texto { get; set; } = string.Empty;
    public string? Nombre { get; set; }
    public string? PaginaUrl { get; set; }
    public string? Contexto { get; set; }

    public static bool IsValidVisitorId(string? visitorId)
        => !string.IsNullOrWhiteSpace(visitorId) && VisitorIdRegex().IsMatch(visitorId.Trim());

    /// <summary>
    /// Valida y recorta los campos. Devuelve false (con un motivo genérico, apto para responder al
    /// visitante) si falta algo obligatorio o el formato no es válido.
    /// </summary>
    public bool TryNormalize(out string error)
    {
        error = string.Empty;
        VisitorId = (VisitorId ?? string.Empty).Trim();
        if (!IsValidVisitorId(VisitorId))
        {
            error = "Identificador de visitante inválido.";
            return false;
        }

        Texto = (Texto ?? string.Empty).Trim();
        if (Texto.Length == 0)
        {
            error = "El mensaje está vacío.";
            return false;
        }

        if (Texto.Length > MaxTexto)
            Texto = Texto[..MaxTexto];

        var clientMessageId = (ClientMessageId ?? string.Empty).Trim();
        ClientMessageId = ClientIdRegex().IsMatch(clientMessageId)
            ? clientMessageId
            : Guid.NewGuid().ToString("N");

        Nombre = Clip(Nombre, MaxNombre);
        Contexto = Clip(Contexto, MaxContexto);
        PaginaUrl = Clip(PaginaUrl, MaxPaginaUrl);
        if (PaginaUrl is not null
            && (!Uri.TryCreate(PaginaUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            PaginaUrl = null;
        }

        return true;
    }

    private static string? Clip(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return null;
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex VisitorIdRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ClientIdRegex();
}

public sealed class WebChatMensajeEntranteResultDto
{
    public long IdMensaje { get; init; }
    public bool Creado { get; init; }
}

/// <summary>
/// Mensaje que ve el visitante en el widget. Nunca incluye notas internas ni datos del agente
/// (usuario, técnico): solo texto, fecha y de qué lado de la charla está.
/// </summary>
public sealed class WebChatMensajeDto
{
    public long Id { get; init; }
    public string Texto { get; init; } = string.Empty;
    public DateTime FechaHora { get; init; }
    public bool EsVisitante { get; init; }

    /// <summary>Ids de las imágenes del mensaje: el widget las pide a /api/webchat/{clave}/adjuntos/{id}.</summary>
    public IReadOnlyList<long> Imagenes { get; init; } = [];

    /// <summary>Ids de los audios del mensaje (mismo endpoint que las imágenes).</summary>
    public IReadOnlyList<long> Audios { get; init; } = [];
}

public sealed class WebChatMensajesResponseDto
{
    public IReadOnlyList<WebChatMensajeDto> Mensajes { get; init; } = [];

    /// <summary>Último IdMensaje devuelto: el widget lo manda como <c>since</c> en el próximo polling.</summary>
    public long Cursor { get; init; }
}

/// <summary>Datos públicos de presentación del widget (sin nada sensible).</summary>
public sealed class WebChatWidgetConfigDto
{
    public string Titulo { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public string MensajeBienvenida { get; init; } = string.Empty;

    /// <summary>El visitante puede mandar imágenes (botón de clip y Ctrl+V en el widget).</summary>
    public bool PermiteImagenes { get; init; }

    /// <summary>El visitante puede mandar notas de voz (botón de micrófono en el widget).</summary>
    public bool PermiteAudios { get; init; }
}

/// <summary>
/// Archivo que manda el visitante (imagen o nota de voz). Viaja en base64 dentro de un JSON, como el
/// resto del widget. El tipo real se decide por el contenido (ver <see cref="WebChatImagenes"/> y
/// <see cref="WebChatAudios"/>), nunca por el nombre ni por lo que diga el navegador.
/// </summary>
public abstract class WebChatArchivoEntranteRequest
{
    public string VisitorId { get; set; } = string.Empty;
    public string? ClientMessageId { get; set; }
    public string? Nombre { get; set; }
    public string? PaginaUrl { get; set; }
    public string? Contexto { get; set; }

    /// <summary>Texto opcional que acompaña el archivo.</summary>
    public string? Texto { get; set; }

    public string? NombreArchivo { get; set; }

    /// <summary>Marcador del mensaje sin texto ("[image]", "[audio]"), el mismo que usan los de WhatsApp.</summary>
    protected abstract string MarcadorSinTexto { get; }

    /// <summary>
    /// Mensaje equivalente para el circuito de siempre. Sin texto va el marcador: el asistente lo
    /// reconoce como imagen o audio sin texto (mira la imagen o transcribe el audio).
    /// </summary>
    public WebChatMensajeEntranteRequest ToMensajeRequest()
        => new()
        {
            VisitorId = VisitorId,
            ClientMessageId = ClientMessageId,
            Nombre = Nombre,
            PaginaUrl = PaginaUrl,
            Contexto = Contexto,
            Texto = string.IsNullOrWhiteSpace(Texto) ? MarcadorSinTexto : Texto
        };
}

/// <summary>Imagen del visitante (POST /api/webchat/{siteKey}/imagenes).</summary>
public sealed class WebChatImagenEntranteRequest : WebChatArchivoEntranteRequest
{
    public string ImagenBase64 { get; set; } = string.Empty;
    protected override string MarcadorSinTexto => WebChatImagenes.MarcadorSinTexto;
}

/// <summary>Nota de voz del visitante (POST /api/webchat/{siteKey}/audios).</summary>
public sealed class WebChatAudioEntranteRequest : WebChatArchivoEntranteRequest
{
    public string AudioBase64 { get; set; } = string.Empty;
    protected override string MarcadorSinTexto => WebChatAudios.MarcadorSinTexto;
}

/// <summary>Archivo ya validado, listo para grabar. TipoArchivo: IMAGE o AUDIO.</summary>
public sealed record WebChatArchivoEntrante(byte[] Bytes, string MimeType, string NombreArchivo, string TipoArchivo);

/// <summary>Imagen o audio de una conversación del chat web, para mostrarlo en el widget.</summary>
public sealed record WebChatAdjuntoDto(byte[] Bytes, string MimeType);

/// <summary>Reglas de las imágenes del chat web: formatos aceptados y tamaño máximo.</summary>
public static class WebChatImagenes
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const string MarcadorSinTexto = "[image]";

    /// <summary>
    /// Decide el tipo por los primeros bytes (JPEG, PNG, GIF, WEBP). Cualquier otra cosa (SVG, HTML,
    /// ejecutables renombrados) se rechaza: el archivo después se muestra en el navegador.
    /// </summary>
    public static bool TryDetectar(ReadOnlySpan<byte> bytes, out string mimeType, out string extension)
    {
        mimeType = string.Empty;
        extension = string.Empty;
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            (mimeType, extension) = ("image/jpeg", ".jpg");
        else if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            (mimeType, extension) = ("image/png", ".png");
        else if (bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8')
            (mimeType, extension) = ("image/gif", ".gif");
        else if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
                 && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            (mimeType, extension) = ("image/webp", ".webp");
        return mimeType.Length > 0;
    }

    /// <summary>
    /// Decodifica el base64 del widget (acepta el prefijo "data:...;base64,") y valida tipo y tamaño.
    /// El error es apto para mostrarle al visitante.
    /// </summary>
    public static bool TryLeer(string? base64, string? nombreArchivo, out WebChatArchivoEntrante? imagen, out string error)
        => WebChatArchivos.TryLeer(base64, nombreArchivo, MaxBytes, TryDetectar, "IMAGE", "imagen",
            "Formato no admitido: se aceptan JPG, PNG, GIF y WEBP.", out imagen, out error);
}

/// <summary>Reglas de las notas de voz del chat web.</summary>
public static class WebChatAudios
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const string MarcadorSinTexto = "[audio]";

    /// <summary>
    /// Formatos que graban los navegadores (WebM/Opus en Chrome y Edge, MP4/AAC en Safari, OGG en
    /// Firefox) y los comunes de archivo (MP3, WAV). Se decide por los primeros bytes.
    /// </summary>
    public static bool TryDetectar(ReadOnlySpan<byte> bytes, out string mimeType, out string extension)
    {
        mimeType = string.Empty;
        extension = string.Empty;
        if (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
            (mimeType, extension) = ("audio/webm", ".webm");
        else if (bytes.Length >= 4 && bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S')
            (mimeType, extension) = ("audio/ogg", ".ogg");
        else if (bytes.Length >= 12 && bytes[4] == 'f' && bytes[5] == 't' && bytes[6] == 'y' && bytes[7] == 'p')
            (mimeType, extension) = ("audio/mp4", ".m4a");
        else if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
                 && bytes[8] == 'W' && bytes[9] == 'A' && bytes[10] == 'V' && bytes[11] == 'E')
            (mimeType, extension) = ("audio/wav", ".wav");
        else if ((bytes.Length >= 3 && bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3')
                 || (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0 && bytes[1] != 0xD8))
            (mimeType, extension) = ("audio/mpeg", ".mp3");
        return mimeType.Length > 0;
    }

    public static bool TryLeer(string? base64, string? nombreArchivo, out WebChatArchivoEntrante? audio, out string error)
        => WebChatArchivos.TryLeer(base64, nombreArchivo, MaxBytes, TryDetectar, "AUDIO", "audio",
            "Formato de audio no admitido.", out audio, out error);
}

/// <summary>Lectura común de los archivos que manda el widget en base64.</summary>
internal static class WebChatArchivos
{
    internal delegate bool Detector(ReadOnlySpan<byte> bytes, out string mimeType, out string extension);

    internal static bool TryLeer(
        string? base64,
        string? nombreArchivo,
        int maxBytes,
        Detector detectar,
        string tipoArchivo,
        string nombreDefault,
        string errorFormato,
        out WebChatArchivoEntrante? archivo,
        out string error)
    {
        archivo = null;
        error = "No se pudo leer el archivo.";
        var errorTamano = $"El archivo es muy grande (máximo {maxBytes / (1024 * 1024)} MB).";
        var datos = (base64 ?? string.Empty).Trim();
        var coma = datos.IndexOf(',');
        if (datos.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma > 0)
            datos = datos[(coma + 1)..];
        if (datos.Length == 0)
            return false;
        if (datos.Length > (maxBytes / 3 + 2) * 4)
        {
            error = errorTamano;
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(datos);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length == 0)
            return false;
        if (bytes.Length > maxBytes)
        {
            error = errorTamano;
            return false;
        }
        if (!detectar(bytes, out var mime, out var extension))
        {
            error = errorFormato;
            return false;
        }

        archivo = new WebChatArchivoEntrante(bytes, mime, NombreSeguro(nombreArchivo, extension, nombreDefault), tipoArchivo);
        error = string.Empty;
        return true;
    }

    private static string NombreSeguro(string? nombre, string extension, string nombreDefault)
    {
        var baseName = Path.GetFileNameWithoutExtension((nombre ?? string.Empty).Trim());
        baseName = System.Text.RegularExpressions.Regex.Replace(baseName, "[^A-Za-z0-9_-]", "_").Trim('_');
        if (baseName.Length == 0)
            baseName = nombreDefault;
        if (baseName.Length > 60)
            baseName = baseName[..60];
        return baseName + extension;
    }
}

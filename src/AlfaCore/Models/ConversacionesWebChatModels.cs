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
}

/// <summary>
/// Imagen que manda el visitante (POST /api/webchat/{siteKey}/imagenes). Viaja en base64 dentro de
/// un JSON, como el resto del widget. El tipo real se decide por el contenido (ver <see cref="WebChatImagenes"/>),
/// nunca por el nombre ni por lo que diga el navegador.
/// </summary>
public sealed class WebChatImagenEntranteRequest
{
    public string VisitorId { get; set; } = string.Empty;
    public string? ClientMessageId { get; set; }
    public string? Nombre { get; set; }
    public string? PaginaUrl { get; set; }
    public string? Contexto { get; set; }

    /// <summary>Texto opcional que acompaña la imagen.</summary>
    public string? Texto { get; set; }

    public string ImagenBase64 { get; set; } = string.Empty;
    public string? NombreArchivo { get; set; }

    /// <summary>
    /// Mensaje equivalente para el circuito de siempre. Sin texto va "[image]", el mismo marcador
    /// que usan los mensajes de imagen de WhatsApp (el asistente lo reconoce como imagen sin texto).
    /// </summary>
    public WebChatMensajeEntranteRequest ToMensajeRequest()
        => new()
        {
            VisitorId = VisitorId,
            ClientMessageId = ClientMessageId,
            Nombre = Nombre,
            PaginaUrl = PaginaUrl,
            Contexto = Contexto,
            Texto = string.IsNullOrWhiteSpace(Texto) ? WebChatImagenes.MarcadorSinTexto : Texto
        };
}

/// <summary>Imagen ya validada, lista para grabar.</summary>
public sealed record WebChatImagenEntrante(byte[] Bytes, string MimeType, string NombreArchivo);

/// <summary>Imagen de una conversación del chat web, para mostrarla en el widget.</summary>
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
    public static bool TryLeer(string? base64, string? nombreArchivo, out WebChatImagenEntrante? imagen, out string error)
    {
        imagen = null;
        error = "No se pudo leer la imagen.";
        var datos = (base64 ?? string.Empty).Trim();
        var coma = datos.IndexOf(',');
        if (datos.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma > 0)
            datos = datos[(coma + 1)..];
        if (datos.Length == 0)
            return false;
        if (datos.Length > (MaxBytes / 3 + 2) * 4)
        {
            error = "La imagen es muy grande (máximo 5 MB).";
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
        if (bytes.Length > MaxBytes)
        {
            error = "La imagen es muy grande (máximo 5 MB).";
            return false;
        }
        if (!TryDetectar(bytes, out var mime, out var extension))
        {
            error = "Formato no admitido: se aceptan JPG, PNG, GIF y WEBP.";
            return false;
        }

        imagen = new WebChatImagenEntrante(bytes, mime, NombreSeguro(nombreArchivo, extension));
        error = string.Empty;
        return true;
    }

    private static string NombreSeguro(string? nombre, string extension)
    {
        var baseName = Path.GetFileNameWithoutExtension((nombre ?? string.Empty).Trim());
        baseName = System.Text.RegularExpressions.Regex.Replace(baseName, "[^A-Za-z0-9_-]", "_").Trim('_');
        if (baseName.Length == 0)
            baseName = "imagen";
        if (baseName.Length > 60)
            baseName = baseName[..60];
        return baseName + extension;
    }
}

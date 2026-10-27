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
}

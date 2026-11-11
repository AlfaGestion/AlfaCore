namespace AlfaCore.Models;

/// <summary>Canales con conexión guiada por una app central de ALFA NET. Cada uno se conecta por separado.</summary>
public static class MetaCanalSocial
{
    public const string Instagram = "INSTAGRAM";
    public const string Facebook = "FACEBOOK";
    /// <summary>No es de Meta, pero usa la misma tabla central, el mismo flujo y la misma tarjeta de conexión.</summary>
    public const string MercadoLibre = "MERCADOLIBRE";

    public static string? Normalizar(string? canal)
        => (canal ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "INSTAGRAM" => Instagram,
            "FACEBOOK" => Facebook,
            "MERCADOLIBRE" => MercadoLibre,
            _ => null
        };
}

/// <summary>Fila de ALFA_CENTRAL.dbo.CONV_CANALES_SOCIALES.</summary>
public sealed class MetaCanalConexionDto
{
    public int IdConexion { get; set; }
    public int IdBase { get; set; }
    public string Canal { get; set; } = string.Empty;
    public string CuentaId { get; set; } = string.Empty;
    public string? CuentaIdAlterno { get; set; }
    public string? CuentaNombre { get; set; }
    public string? CuentaUsuario { get; set; }
    public string? FotoUrl { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime? TokenVenceUtc { get; set; }
    public DateTime? FechaRenovacionUtc { get; set; }
    public string? UltimoError { get; set; }
    public string? ConectadoPor { get; set; }
    public DateTime FechaConexion { get; set; }

    /// <summary>Nombre para mostrar: @usuario si hay, si no el nombre.</summary>
    public string Titulo
        => !string.IsNullOrWhiteSpace(CuentaUsuario)
            ? (Canal == MetaCanalSocial.Facebook ? CuentaNombre ?? CuentaUsuario.Trim() : (Canal == MetaCanalSocial.Instagram ? "@" : string.Empty) + CuentaUsuario.Trim())
            : CuentaNombre ?? CuentaId;
}

/// <summary>Cuenta de Instagram que devolvió Meta al volver del inicio de sesión (todavía no grabada).</summary>
public sealed class MetaInstagramCuentaAutorizada
{
    /// <summary>user_id: id de la cuenta profesional (el que usa el webhook y el envío).</summary>
    public string CuentaId { get; init; } = string.Empty;
    /// <summary>id con alcance de app.</summary>
    public string CuentaIdAlterno { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string FotoUrl { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public DateTime? TokenVenceUtc { get; init; }
}

/// <summary>Página de Facebook que la persona administra (con su token de página, que no vence).</summary>
public sealed class MetaFacebookPaginaAutorizada
{
    public string PaginaId { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public string FotoUrl { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    /// <summary>Si la persona puede leer y contestar mensajes de la página (tarea MESSAGING o MODERATE).</summary>
    public bool PuedeMensajes { get; init; } = true;
}

/// <summary>Cuenta vendedora de Mercado Libre que autorizó la app (todavía no grabada).</summary>
public sealed class MercadoLibreCuentaAutorizada
{
    public string SellerId { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string SiteId { get; init; } = string.Empty;
    public string FotoUrl { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime? TokenVenceUtc { get; init; }
}

public enum MetaConexionPendienteEstado { EsperandoMeta, Lista, Error }

/// <summary>
/// Conexión en curso: se crea al tocar "Conectar", Meta vuelve al callback con el código y la pantalla
/// la confirma. Vive solo en memoria del servidor unos minutos (los tokens nunca salen al navegador).
/// </summary>
public sealed class MetaConexionPendiente
{
    public string Id { get; init; } = string.Empty;
    public string Canal { get; init; } = string.Empty;
    public int IdBase { get; init; }
    public string Usuario { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string ReturnPath { get; init; } = string.Empty;
    public DateTime CreadaUtc { get; init; } = DateTime.UtcNow;
    public MetaConexionPendienteEstado Estado { get; set; } = MetaConexionPendienteEstado.EsperandoMeta;
    public string Error { get; set; } = string.Empty;
    public MetaInstagramCuentaAutorizada? Instagram { get; set; }
    public IReadOnlyList<MetaFacebookPaginaAutorizada> PaginasFacebook { get; set; } = [];
    public MercadoLibreCuentaAutorizada? MercadoLibre { get; set; }
}

/// <summary>Lo que necesita la pantalla de cada canal para mostrar la conexión guiada.</summary>
public sealed class MetaCanalConexionVista
{
    public bool Disponible { get; init; }
    public string MotivoNoDisponible { get; init; } = string.Empty;
    public MetaCanalConexionDto? Conexion { get; init; }
    public MetaConexionPendiente? Pendiente { get; init; }
}

public sealed class MetaCanalSocialException(string message, Exception? inner = null) : Exception(message, inner);

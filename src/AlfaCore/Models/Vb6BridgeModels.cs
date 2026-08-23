namespace AlfaCore.Models;

public sealed class Vb6AuthTicketRequest
{
    public string Servidor { get; init; } = string.Empty;
    public string BaseDatos { get; init; } = string.Empty;
    public string UsuarioSql { get; init; } = string.Empty;
    public string PasswordSql { get; init; } = string.Empty;
    public string UsuarioSistema { get; init; } = string.Empty;
    public string PasswordSistema { get; init; } = string.Empty;
    public string Modulo { get; init; } = string.Empty;
    public string? NombreSesion { get; init; }

    /// <summary>
    /// Id de la fila en ALFA_CENTRAL.dbo.bases que identifica a este cliente/base. Solo hace falta
    /// cuando AlfaCore corre en modo SaaS: ahí no se puede inferir el cliente comparando el
    /// "servidor" que manda el VB6 contra <c>bases.dbserver</c>, porque ese dato es el nombre/IP
    /// que ve el cliente en su LAN, mientras que en el catálogo central suele estar la IP de
    /// WireGuard (o puede repetirse un nombre de base genérico entre clientes distintos). Lo
    /// configura una sola vez quien da de alta el equipo, vía Cfg("ALFACORE_IDBASE").
    /// </summary>
    public string? IdBaseCentral { get; init; }

    /// <summary>
    /// Cfg NW("LICENCIAPRINCIPAL") del equipo — el mismo número de serie que ya usa el sistema de
    /// licencias (sp_ActivaLicencia). En modo SaaS se usa para: (a) resolver el IdBase automáticamente
    /// cuando todavía no está cargado (ver <see cref="Vb6ResolverIdBaseRequest"/>), y (b) blindar el
    /// IdBase ya cacheado — si alguien lo cambió a mano o vino de un backup restaurado en otro equipo,
    /// que apunte a una base de un cliente distinto se detecta cruzando contra esta licencia.
    /// </summary>
    public string? LicenciaPrincipal { get; init; }
}

/// <summary>
/// Pedido para resolver a qué fila de ALFA_CENTRAL.dbo.bases corresponde este equipo, sin que
/// nadie tenga que tipear ni adivinar un IdBase. Ver <see cref="Vb6BridgeService.ResolverIdBaseAsync"/>.
/// </summary>
public sealed class Vb6ResolverIdBaseRequest
{
    public string LicenciaPrincipal { get; init; } = string.Empty;
    public string BaseDatos { get; init; } = string.Empty;
    public string UsuarioSql { get; init; } = string.Empty;
    public string PasswordSql { get; init; } = string.Empty;
}

public sealed class Vb6ConsumeTicketResult
{
    public string SqlSessionId { get; init; } = string.Empty;
    public string UserToken { get; init; } = string.Empty;
    public string RedirectUrl { get; init; } = string.Empty;
}

/// <summary>
/// Identidad de una instalación VB6 ya validada contra ALFA_CENTRAL — ver
/// <see cref="Vb6BridgeService.ValidateInstallationAsync"/>. IdWeb siempre viene resuelto acá
/// (nunca del request del VB6): es lo que permite que un endpoint de integración confíe en el
/// IdWeb sin tener que recibirlo, validarlo y potencialmente equivocarse.
/// </summary>
public sealed class Vb6InstallationDto
{
    public int IdBase { get; init; }
    public string IdCliente { get; init; } = string.Empty;
    public string IdWeb { get; init; } = string.Empty;
}

public sealed record Vb6BridgeTicketRecord
{
    public string Ticket { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public string Servidor { get; init; } = string.Empty;
    public string BaseDatos { get; init; } = string.Empty;
    public string UsuarioSql { get; init; } = string.Empty;
    public string PasswordSql { get; init; } = string.Empty;
    public string UsuarioSistema { get; init; } = string.Empty;
    public string PasswordSistema { get; init; } = string.Empty;
    public string Modulo { get; init; } = string.Empty;
    public string? NombreSesion { get; init; }

    // Solo se completan cuando AlfaCore corre en modo SaaS (ver Vb6BridgeService.CreateTicketAsync).
    // IdCliente vacío/null == ticket "local": ConsumeTicketAsync sigue el camino legacy de siempre.
    public string? IdCliente { get; init; }
    public string? IdWeb { get; init; }
    public string? RazonSocial { get; init; }
    public bool SuperAdmin { get; init; }
    public int IdBase { get; init; }
}

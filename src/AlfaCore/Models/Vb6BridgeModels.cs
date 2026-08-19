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
}

public sealed class Vb6ConsumeTicketResult
{
    public string SqlSessionId { get; init; } = string.Empty;
    public string UserToken { get; init; } = string.Empty;
    public string RedirectUrl { get; init; } = string.Empty;
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

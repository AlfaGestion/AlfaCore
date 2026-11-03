namespace AlfaCore.Models;

/// <summary>Acceso de un estudio contable a una base (ALFA_CENTRAL.ESTUDIO_ACCESOS).</summary>
public sealed class EstudioAcceso
{
    public int IdAcceso { get; set; }
    public int IdBase { get; set; }
    public string EmailInvitado { get; set; } = string.Empty;
    public string UsuarioBase { get; set; } = string.Empty;
    /// <summary>COMPLETO (por defecto) o CONSULTA.</summary>
    public string Perfil { get; set; } = "CONSULTA";
    /// <summary>PENDIENTE, ACTIVO o REVOCADO.</summary>
    public string Estado { get; set; } = "PENDIENTE";
    public string IdClienteEstudio { get; set; } = string.Empty;
    public string NombreEstudio { get; set; } = string.Empty;
    public string LoginEstudio { get; set; } = string.Empty;
    public string InvitadoPor { get; set; } = string.Empty;
    public DateTime FechaInvitacion { get; set; }
    public DateTime? FechaAceptacion { get; set; }
    public DateTime? TokenVence { get; set; }
    public DateTime? FechaRevocacion { get; set; }
}

public sealed class EstudioInvitarSolicitud
{
    public string Email { get; set; } = string.Empty;
    /// <summary>Nombre del usuario en la base (TA_USUARIOS); vacío = se arma con el email.</summary>
    public string Usuario { get; set; } = string.Empty;
    public string Perfil { get; set; } = "COMPLETO";
}

/// <summary>El enlace se muestra una sola vez: en la base queda solo su hash.</summary>
public sealed record EstudioInvitacionCreada(EstudioAcceso Acceso, string Enlace);

/// <summary>Lo que ve quien abre el enlace de invitación.</summary>
public sealed record EstudioInvitacionInfo(string Empresa, string BaseNombre, string EmailInvitado, string Perfil, string Estado, bool Vencida);

public sealed class EstudioRegistroSolicitud
{
    public string NombreEstudio { get; set; } = string.Empty;
    public string Cuit { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Una empresa en el portal del estudio, con el estado del mes.</summary>
public sealed class EstudioEmpresa
{
    public int IdAcceso { get; set; }
    public int IdBase { get; set; }
    public string Empresa { get; set; } = string.Empty;
    public string BaseNombre { get; set; } = string.Empty;
    public string IdWebEmpresa { get; set; } = string.Empty;
    public string Perfil { get; set; } = string.Empty;
    public string UsuarioBase { get; set; } = string.Empty;
    public EstudioEstadoMes? Estado { get; set; }
    public string? Error { get; set; }
}

public sealed class EstudioEstadoMes
{
    public DateTime Mes { get; set; }
    /// <summary>Fechas de cierre de ventas y compras (null = sin cerrar nunca).</summary>
    public DateTime? CierreVentas { get; set; }
    public DateTime? CierreCompras { get; set; }
    public bool VentasCerradas => CierreVentas >= Mes.AddMonths(1).AddDays(-1);
    public bool ComprasCerradas => CierreCompras >= Mes.AddMonths(1).AddDays(-1);
    public int Ventas { get; set; }
    public int Compras { get; set; }
    /// <summary>Último control automático con ARCA (texto grabado por la descarga automática).</summary>
    public string ControlArcaRecibidos { get; set; } = string.Empty;
    public string ControlArcaEmitidos { get; set; } = string.Empty;
    public DateTime? UltimoPaquete { get; set; }
}

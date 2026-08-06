namespace AlfaCore.Models;

public sealed class UsuariosFilters
{
    public string Texto      { get; set; } = string.Empty;
    public bool?  Activo     { get; set; } = true;
    public bool?  EsGrupo    { get; set; }
    public int    PageNumber { get; set; } = 1;
    public int    PageSize   { get; set; } = 50;
}

public sealed class UsuarioGridItemDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EsGrupo { get; set; }
    public bool CambiarProximoInicio { get; set; }
    public bool Activo { get; set; } = true;
    public bool TieneFoto { get; set; }
    public DateTime? FechaHoraGrabacion { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
}

public sealed class UsuarioDetailDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EsGrupo { get; set; }
    public bool CambiarProximoInicio { get; set; }
    public bool Activo { get; set; } = true;
    public string ContrasenaDecodificada { get; set; } = string.Empty;
    public bool TieneFoto { get; set; }
    public string FotoCacheToken { get; set; } = string.Empty;
    public DateTime? FechaHoraGrabacion { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    /// <summary>Tiene una fila activa (Baja=0) en dbo.V_TA_Tecnicos asociada a este usuario.</summary>
    public bool EsTecnico { get; set; }
}

public sealed class UsuarioSaveRequest
{
    public string NombreOriginal { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EsGrupo { get; set; }
    public bool CambiarProximoInicio { get; set; }
    public string Contrasena { get; set; } = string.Empty;
    public byte[]? FotoContenido { get; set; }
    public string FotoNombreOriginal { get; set; } = string.Empty;
    public string FotoMimeType { get; set; } = string.Empty;
    public bool QuitarFoto { get; set; }
    /// <summary>
    /// Si está tildado, se da de alta (o reactiva) automáticamente una fila en
    /// dbo.V_TA_Tecnicos asociada a este usuario — para poder asignarle conversaciones, usar el
    /// chat interno, etc. Si se destilda, la fila existente se da de baja lógica (no se borra:
    /// puede tener tickets/mensajes atados).
    /// </summary>
    public bool EsTecnico { get; set; }
}

public sealed class UsuarioPhotoServeDto
{
    public string RutaCompleta { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/jpeg";
    public string NombreArchivo { get; set; } = string.Empty;
}

public sealed class UsuariosViewSettingsDto
{
    public string AgruparPor { get; set; } = UsuariosViewGroupKeys.None;
    public List<UsuarioViewColumnDto> Columnas { get; set; } = [];
}

public sealed class UsuarioViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
}

public static class UsuariosViewColumnKeys
{
    public const string Nombre = "nombre";
    public const string Email = "email";
    public const string Tipo = "tipo";
    public const string CambiarClave = "cambiar-clave";
    public const string Activo = "activo";
    public const string Alta = "alta";
    public const string Modificacion = "modificacion";

    public static readonly IReadOnlyList<string> All =
    [
        Nombre,
        Email,
        Tipo,
        CambiarClave,
        Activo,
        Alta,
        Modificacion
    ];
}

public static class UsuariosViewGroupKeys
{
    public const string None = "none";
    public const string Tipo = "tipo";
    public const string Activo = "activo";
    public const string CambiarClave = "cambiar-clave";
}

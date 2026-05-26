namespace AlfaCore.Models;

public sealed class TecnicosFilters
{
    public string Texto      { get; set; } = string.Empty;
    public bool?  Activo     { get; set; } = true;
    public int    PageNumber { get; set; } = 1;
    public int    PageSize   { get; set; } = 50;
}

public sealed class TecnicoGridItemDto
{
    public string   IdTecnico       { get; set; } = string.Empty;
    public string   Nombre          { get; set; } = string.Empty;
    public string   Cargo           { get; set; } = string.Empty;
    public string   Localidad       { get; set; } = string.Empty;
    public string   Telefono        { get; set; } = string.Empty;
    public decimal? CostoHora       { get; set; }
    public bool     OcultarCliente  { get; set; }
    public string   UsuarioAsociado { get; set; } = string.Empty;
    public bool     Baja            { get; set; }
}

public sealed class TecnicoDetailDto
{
    public string   IdTecnico       { get; set; } = string.Empty;
    public string   Nombre          { get; set; } = string.Empty;
    public string   Cargo           { get; set; } = string.Empty;
    public string   Domicilio       { get; set; } = string.Empty;
    public string   Localidad       { get; set; } = string.Empty;
    public string   IdProvincia     { get; set; } = string.Empty;
    public string   Telefono        { get; set; } = string.Empty;
    public decimal? CostoHora       { get; set; }
    public string   UsuarioAsociado { get; set; } = string.Empty;
    public bool     OcultarCliente  { get; set; }
    public bool     Baja            { get; set; }
}

public sealed class TecnicoSaveRequest
{
    public string   IdTecnicoOriginal { get; set; } = string.Empty;
    public string   IdTecnico         { get; set; } = string.Empty;
    public string   Nombre            { get; set; } = string.Empty;
    public string   Cargo             { get; set; } = string.Empty;
    public string   Domicilio         { get; set; } = string.Empty;
    public string   Localidad         { get; set; } = string.Empty;
    public string   IdProvincia       { get; set; } = string.Empty;
    public string   Telefono          { get; set; } = string.Empty;
    public decimal? CostoHora         { get; set; }
    public string   UsuarioAsociado   { get; set; } = string.Empty;
    public bool     OcultarCliente    { get; set; }
}

public sealed class EstadoItemDto
{
    public string Codigo      { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

public sealed class TecnicosViewSettingsDto
{
    public string                   AgruparPor { get; set; } = TecnicosViewGroupKeys.None;
    public List<TecnicoViewColumnDto> Columnas { get; set; } = [];
}

public sealed class TecnicoViewColumnDto
{
    public string Key     { get; set; } = string.Empty;
    public string Label   { get; set; } = string.Empty;
    public bool   Visible { get; set; }
    public int    Order   { get; set; }
}

public static class TecnicosViewColumnKeys
{
    public const string IdTecnico      = "id-tecnico";
    public const string Nombre         = "nombre";
    public const string Cargo          = "cargo";
    public const string Localidad      = "localidad";
    public const string Telefono       = "telefono";
    public const string CostoHora      = "costo-hora";
    public const string OcultarCliente = "ocultar-cliente";
    public const string UsuarioAsociado = "usuario-asociado";
    public const string Activo         = "activo";

    public static readonly IReadOnlyList<string> All =
    [
        IdTecnico,
        Nombre,
        Cargo,
        Localidad,
        Telefono,
        CostoHora,
        OcultarCliente,
        UsuarioAsociado,
        Activo
    ];
}

public static class TecnicosViewGroupKeys
{
    public const string None           = "none";
    public const string Cargo          = "cargo";
    public const string OcultarCliente = "ocultar-cliente";
    public const string Activo         = "activo";
}

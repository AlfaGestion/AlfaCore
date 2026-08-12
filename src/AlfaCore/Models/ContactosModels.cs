namespace AlfaCore.Models;

public sealed class ContactosFilters
{
    public string Texto      { get; set; } = string.Empty;
    public bool?  Activo     { get; set; } = true;
    public bool?  TieneEmail { get; set; }
    public bool?  TieneCelular { get; set; }
    public bool?  TieneWhatsApp { get; set; }
    public string ProvinciaCodigo { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public List<SearchRuleDto> Reglas { get; set; } = [];
    public string OrdenarPor { get; set; } = ContactosViewColumnKeys.Nombre;
    public bool OrdenDescendente { get; set; }
    public int    PageNumber { get; set; } = 1;
    public int    PageSize   { get; set; } = 50;
}

public sealed class SearchRuleDto
{
    public string Campo { get; set; } = string.Empty;
    public string Operador { get; set; } = "contains";
    public string Valor { get; set; } = string.Empty;
}

public sealed class ContactoGridItemDto
{
    public int Id { get; set; }
    public string NombreApellido { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string ProvinciaCodigo { get; set; } = string.Empty;
    public string ProvinciaDescripcion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public long? IdConversacionWhatsApp { get; set; }
}

public sealed class ContactoNavigationContextDto
{
    public int? PreviousId { get; set; }
    public int? NextId { get; set; }
    public int Position { get; set; }
    public int Total { get; set; }
    public bool UsesFilteredContext { get; set; }
}

public sealed class ContactoDetailDto
{
    public int Id { get; set; }
    public string NombreApellido { get; set; } = string.Empty;
    public string Domicilio { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string ProvinciaCodigo { get; set; } = string.Empty;
    public string CodigoPostal { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string TelefonoAlternativo { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool MailPagosCobranzas { get; set; }
    public bool MailOrdenCompra { get; set; }
    public bool MailOrdenTrabajo { get; set; }
    public string Website { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public long? IdConversacionWhatsApp { get; set; }
    public List<ContactoCuentaDto> CuentasVinculadas { get; set; } = [];
}

public sealed class ContactoSaveRequest
{
    public int? Id { get; set; }
    public string NombreApellido { get; set; } = string.Empty;
    public string Domicilio { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string ProvinciaCodigo { get; set; } = string.Empty;
    public string CodigoPostal { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string TelefonoAlternativo { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool MailPagosCobranzas { get; set; }
    public bool MailOrdenCompra { get; set; }
    public bool MailOrdenTrabajo { get; set; }
    public string Website { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class ProvinciaOptionDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

public sealed class ContactoCuentaDto
{
    public string Codigo { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public bool VinculadoPorTabla { get; set; }
    public bool VinculadoPorCuentaRel { get; set; }
}

public sealed class ContactoMergeCandidateDto
{
    public int Id { get; set; }
    public string NombreApellido { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string CuentaRel { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public sealed class ContactoMergeRequest
{
    public int ContactoPrincipalId { get; set; }
    public int ContactoDuplicadoId { get; set; }
}

public sealed class ContactosViewSettingsDto
{
    public string Vista { get; set; } = ContactosViewModeKeys.Listado;
    public string AgruparPor { get; set; } = ContactosViewGroupKeys.None;
    public List<ContactoViewColumnDto> Columnas { get; set; } = [];
}

public sealed class ContactoViewColumnDto
{
    public string Key     { get; set; } = string.Empty;
    public string Label   { get; set; } = string.Empty;
    public bool   Visible { get; set; }
    public int    Order   { get; set; }
}

public static class ContactosViewColumnKeys
{
    public const string Nombre    = "nombre";
    public const string Localidad = "localidad";
    public const string Provincia = "provincia";
    public const string Telefono  = "telefono";
    public const string Celular   = "celular";
    public const string Email     = "email";
    public const string Cargo     = "cargo";
}

public static class ContactosViewGroupKeys
{
    public const string None   = "none";
    public const string Activo = "activo";
}

public static class ContactosViewModeKeys
{
    public const string Listado = "listado";
    public const string Kanban  = "kanban";
}

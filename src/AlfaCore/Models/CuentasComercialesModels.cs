namespace AlfaCore.Models;

public enum CuentaComercialTipo
{
    Cliente = 1,
    Proveedor = 2
}

public sealed class CuentaComercialFilters
{
    public string Texto { get; set; } = string.Empty;
    public bool? Activo { get; set; } = true;
    public bool? Bloqueado { get; set; }
    public string ProvinciaCodigo { get; set; } = string.Empty;
    public string PaisCodigo { get; set; } = string.Empty;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class CuentaComercialGridItemDto
{
    public string Codigo { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Contacto { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string ProvinciaCodigo { get; set; } = string.Empty;
    public string ProvinciaDescripcion { get; set; } = string.Empty;
    public string PaisCodigo { get; set; } = string.Empty;
    public string PaisDescripcion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Mail { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string IvaCodigo { get; set; } = string.Empty;
    public string IvaDescripcion { get; set; } = string.Empty;
    public string IdVendedor { get; set; } = string.Empty;
    public string NombreVendedor { get; set; } = string.Empty;
    public string IdLista { get; set; } = string.Empty;
    public bool Bloqueado { get; set; }
    public bool Activo { get; set; } = true;
    public string Calle { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Piso { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
}

public class CuentaComercialSaveRequest
{
    public string CodigoOriginal { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Contacto { get; set; } = string.Empty;
    public string Calle { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Piso { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    public string CodigoPostal { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string ProvinciaCodigo { get; set; } = string.Empty;
    public string PaisCodigo { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Fax { get; set; } = string.Empty;
    public string Mail { get; set; } = string.Empty;
    public string Web { get; set; } = string.Empty;
    public string DocumentoTipoCodigo { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string IvaCodigo { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public decimal? LimiteCredito { get; set; }
    public string CondicionComercialCodigo { get; set; } = string.Empty;
    public string ListaPrecioCodigo { get; set; } = string.Empty;
    public short? Clase { get; set; }
    public string VendedorCodigo { get; set; } = string.Empty;
    public string CuentaPrincipal { get; set; } = string.Empty;
    public string CodigoImputacion { get; set; } = string.Empty;
    public string CodigoOpcional { get; set; } = string.Empty;
    public string MonedaCodigo { get; set; } = string.Empty;
    public string ClasificacionCodigo { get; set; } = string.Empty;
    public string LugarEntrega { get; set; } = string.Empty;
    public string DatosPago { get; set; } = string.Empty;
    public string Manual { get; set; } = string.Empty;
    public string NotaCuenta { get; set; } = string.Empty;
    public string SituacionIb { get; set; } = string.Empty;
    public string ConvenioIb { get; set; } = string.Empty;
    public string ConceptoIb { get; set; } = string.Empty;
    public string NroConstanciaIb { get; set; } = string.Empty;
    public bool Bloqueado { get; set; }
    public bool ExentoIvaServicios { get; set; }
    public bool ExentoIvaArticulos { get; set; }
    public bool ExentoIvaOtros { get; set; }
    public bool ProveedorLocal { get; set; }
    public bool ProveedorCompartido { get; set; }
    public List<CuentaComercialCondicionDescuentoDto> DescuentosCondicion { get; set; } = [];
}

public sealed class CuentaComercialDetailDto : CuentaComercialSaveRequest
{
    public bool Activo { get; set; } = true;
    public string TipoVista { get; set; } = string.Empty;
    public DateTime? FechaHoraGrabacion { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
}

public sealed class CuentaComercialLookupOptionDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

public sealed class CuentaComercialLookupDataDto
{
    public string TituloCuenta { get; set; } = string.Empty;
    public bool UsaListasDePrecio { get; set; }
    public List<CuentaComercialLookupOptionDto> Paises { get; set; } = [];
    public List<CuentaComercialLookupOptionDto> Provincias { get; set; } = [];
    public List<CuentaComercialLookupOptionDto> CondicionesIva { get; set; } = [];
    public List<CuentaComercialLookupOptionDto> TiposDocumento { get; set; } = [];
    public List<CuentaComercialLookupOptionDto> CondicionesComerciales { get; set; } = [];
    public List<CuentaComercialLookupOptionDto> Vendedores { get; set; } = [];
    public List<CuentaComercialLookupOptionDto> ListasPrecio { get; set; } = [];
    public List<CuentaComercialLookupOptionDto> Monedas { get; set; } = [];
    public List<CuentaComercialLookupOptionDto> Clasificaciones { get; set; } = [];
}

public sealed class CuentaComercialCondicionDescuentoDto
{
    public string CondicionCodigo { get; set; } = string.Empty;
    public string CondicionDescripcion { get; set; } = string.Empty;
    public decimal? Descuento1 { get; set; }
    public decimal? Descuento2 { get; set; }
    public decimal? Descuento3 { get; set; }
    public decimal? Descuento4 { get; set; }
    public decimal? Descuento5 { get; set; }
}

public sealed class CuentaComercialContactoDto
{
    public int Id { get; set; }
    public int IdContacto { get; set; }
    public string NombreApellido { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public bool VinculadoPorTabla { get; set; }
    public bool VinculadoPorCuentaRel { get; set; }
}

public sealed class CuentaComercialContactoCandidateDto
{
    public int Id { get; set; }
    public int IdContacto { get; set; }
    public string NombreApellido { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string CuentaRel { get; set; } = string.Empty;
}

public sealed class CuentaComercialContactoCreateRequest
{
    public string CuentaCodigo { get; set; } = string.Empty;
    public string NombreApellido { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class CuentaComercialContactoQuickUpdateRequest
{
    public int Id { get; set; }
    public string CuentaCodigo { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
}

public sealed class CuentaComercialContactoLinkRequest
{
    public string CuentaCodigo { get; set; } = string.Empty;
    public int ContactoId { get; set; }
}

public sealed class CuentaComercialViewSettingsDto
{
    public string AgruparPor { get; set; } = CuentaComercialViewGroupKeys.None;
    public List<CuentaComercialViewColumnDto> Columnas { get; set; } = [];
}

public sealed class CuentaComercialViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
}

public static class CuentaComercialViewColumnKeys
{
    public const string Codigo = "codigo";
    public const string RazonSocial = "razon-social";
    public const string Contacto = "contacto";
    public const string Localidad = "localidad";
    public const string Provincia = "provincia";
    public const string Telefono = "telefono";
    public const string Mail = "mail";
    public const string Documento = "documento";
    public const string Iva = "iva";
    public const string Vendedor = "vendedor";
    public const string Lista = "lista";
    public const string Bloqueado = "bloqueado";
}

public static class CuentaComercialViewGroupKeys
{
    public const string None = "none";
    public const string Activo = "activo";
    public const string Provincia = "provincia";
    public const string Bloqueado = "bloqueado";
}

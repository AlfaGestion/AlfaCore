namespace AlfaCore.Models;

public sealed class CargaViajesFilters
{
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string TipoVehiculo { get; set; } = string.Empty;
    public string TarifaFletero { get; set; } = string.Empty;
    public string Activo { get; set; } = string.Empty;
    public string Disponible { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string IdComprobante { get; set; } = string.Empty;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class CargaViajesGridItemDto
{
    public int Id { get; set; }
    public string Tc { get; set; } = "VJ";
    public string IdComprobante { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public string DestinoCodigo { get; set; } = string.Empty;
    public string DestinoDescripcion { get; set; } = string.Empty;
    public string ChoferCodigo { get; set; } = string.Empty;
    public string ChoferNombre { get; set; } = string.Empty;
    public string TipoVehiculoCodigo { get; set; } = string.Empty;
    public string TipoVehiculoDescripcion { get; set; } = string.Empty;
    public decimal TotalCliente { get; set; }
    public decimal TotalFletero { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTime? FechaHoraAlta { get; set; }
}

public sealed class CargaViajesDetailDto : CargaViajesGridItemDto
{
    public string Lista { get; set; } = string.Empty;
    public string ListaDescripcion { get; set; } = string.Empty;
    public string ClienteDisplay { get; set; } = string.Empty;
    public string ChoferDisplay { get; set; } = string.Empty;
    public string DestinoDisplay { get; set; } = string.Empty;
    public string TipoVehiculoDisplay { get; set; } = string.Empty;
    public string ListaDisplay { get; set; } = string.Empty;
    public decimal Peaje { get; set; }
    public int CantidadViajes { get; set; } = 1;
    public decimal PorcentajeAdic { get; set; }
    public decimal PorcentajeAdic1 { get; set; }
    public decimal PorcentajeAdic2 { get; set; }
    public decimal PorcentajeAdic3 { get; set; }
    public decimal PorcentajeAdic4 { get; set; }
    public decimal TotalAdic { get; set; }
    public decimal TotalAdic1 { get; set; }
    public decimal TotalAdic2 { get; set; }
    public decimal TotalAdic3 { get; set; }
    public decimal TotalAdic4 { get; set; }
    public decimal TotalAdicionales { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public decimal IdListaRMTRF { get; set; }
    public decimal TotalImporte { get; set; }
    public decimal TotalFlete { get; set; }
    public decimal TotalTarifadoCliente { get; set; }
    public decimal TotalTarifadoFlete { get; set; }
}

public sealed class CargaViajeSaveRequest
{
    public int? Id { get; set; }
    public string Tc { get; set; } = "VJ";
    public string? IdComprobante { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Cliente { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string TipoVehiculo { get; set; } = string.Empty;
    public string Lista { get; set; } = string.Empty;
    public string ClienteDisplay { get; set; } = string.Empty;
    public string ChoferDisplay { get; set; } = string.Empty;
    public string DestinoDisplay { get; set; } = string.Empty;
    public string TipoVehiculoDisplay { get; set; } = string.Empty;
    public string ListaDisplay { get; set; } = string.Empty;
    public decimal ImporteCliente { get; set; }
    public decimal ImporteFletero { get; set; }
    public decimal Peaje { get; set; }
    public int CantidadViajes { get; set; } = 1;
    public decimal PorcentajeAdic { get; set; }
    public decimal PorcentajeAdic1 { get; set; }
    public decimal PorcentajeAdic2 { get; set; }
    public decimal PorcentajeAdic3 { get; set; }
    public decimal PorcentajeAdic4 { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string Estado { get; set; } = CargaViajeEstadoKeys.Pendiente;
    public string? UsuarioAccion { get; set; }
}

public sealed class CargaViajeLookupOptionDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Subtitulo { get; set; } = string.Empty;
    public string Lista { get; set; } = string.Empty;
}

public sealed class CargaViajesLookupDto
{
    public List<CargaViajeLookupOptionDto> Clientes { get; set; } = [];
    public List<CargaViajeLookupOptionDto> Choferes { get; set; } = [];
    public List<CargaViajeLookupOptionDto> Destinos { get; set; } = [];
    public List<CargaViajeLookupOptionDto> TipoVehiculos { get; set; } = [];
    public List<string> Estados { get; set; } = [];
}

public sealed class CargaViajeTarifaGridItemDto
{
    public int Id { get; set; }
    public string IdLista { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string TipoVehiculo { get; set; } = string.Empty;
    public bool TarifaFletero { get; set; }
    public decimal PorcentajeAdic { get; set; }
    public decimal PorcentajeAdic1 { get; set; }
    public decimal PorcentajeAdic2 { get; set; }
    public decimal PorcentajeAdic3 { get; set; }
    public decimal PorcentajeAdic4 { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class CargaViajeTarifaSaveRequest
{
    public string IdLista { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string TipoVehiculo { get; set; } = string.Empty;
    public bool TarifaFletero { get; set; }
    public decimal PorcentajeAdic { get; set; }
    public decimal PorcentajeAdic1 { get; set; }
    public decimal PorcentajeAdic2 { get; set; }
    public decimal PorcentajeAdic3 { get; set; }
    public decimal PorcentajeAdic4 { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class CargaViajeChoferGridItemDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public bool Disponible { get; set; } = true;
}

public sealed class CargaViajeChoferSaveRequest
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public sealed class CargaViajeDestinoGridItemDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public sealed class CargaViajeDestinoSaveRequest
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public sealed class CargaViajeTipoVehiculoGridItemDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool? Activo { get; set; }
}

public sealed class CargaViajeTipoVehiculoSaveRequest
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public sealed class CargaViajeTipoVehiculoViewSettingsDto
{
    public string AgruparPor { get; set; } = CargaViajeTipoVehiculoViewGroupKeys.None;
    public List<CargaViajeTipoVehiculoViewColumnDto> Columnas { get; set; } = [];
}

public sealed class CargaViajeTipoVehiculoViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
}

public static class CargaViajeTipoVehiculoViewColumnKeys
{
    public const string Codigo = "codigo";
    public const string Descripcion = "descripcion";
    public const string Activo = "activo";
}

public static class CargaViajeTipoVehiculoViewGroupKeys
{
    public const string None = "none";
    public const string Activo = "activo";
}

public sealed class CargaViajesViewSettingsDto
{
    public string AgruparPor { get; set; } = CargaViajesViewGroupKeys.None;
    public List<CargaViajesViewColumnDto> Columnas { get; set; } = [];
}

public sealed class CargaViajesConfigDto
{
    public string Sucursal { get; set; } = "0001";
    public string Letra { get; set; } = "X";
    public List<string> NombresAdicionales { get; set; } = ["Adicional 1", "Adicional 2", "Adicional 3", "Adicional 4", "Adicional 5"];
    public List<decimal> PorcentajesAdicionales { get; set; } = [0m, 0m, 0m, 0m, 0m];
}

public sealed class CargaViajesViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
}

public static class CargaViajesViewColumnKeys
{
    public const string Fecha = "fecha";
    public const string IdComprobante = "id-comprobante";
    public const string Cliente = "cliente";
    public const string Destino = "destino";
    public const string Chofer = "chofer";
    public const string TipoVehiculo = "tipo-vehiculo";
    public const string TotalCliente = "total-cliente";
    public const string TotalFletero = "total-fletero";
    public const string Estado = "estado";
    public const string Usuario = "usuario";
    public const string Alta = "alta";
}

public static class CargaViajesViewGroupKeys
{
    public const string None = "none";
    public const string Estado = "estado";
    public const string Usuario = "usuario";
    public const string Activo = "activo";
}

public static class CargaViajeEstadoKeys
{
    public const string Pendiente = "PENDIENTE";
    public const string Finalizado = "FINALIZADO";
    public const string Anulado = "ANULADO";

    public static readonly IReadOnlyList<string> All = [Pendiente, Finalizado, Anulado];
}

namespace AlfaCore.Models;

public sealed class CargaViajesFilters
{
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string AgruparPor { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string TipoVehiculo { get; set; } = string.Empty;
    public string TarifaFletero { get; set; } = string.Empty;
    public string Activo { get; set; } = string.Empty;
    public string Disponible { get; set; } = string.Empty;
    public string EsFletero { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string IdComprobante { get; set; } = string.Empty;
    public string SortBy { get; set; } = string.Empty;
    public bool SortDescending { get; set; } = false;
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
    public decimal PorcentajeAdic5 { get; set; }
    public decimal TotalAdic { get; set; }
    public decimal TotalAdic1 { get; set; }
    public decimal TotalAdic2 { get; set; }
    public decimal TotalAdic3 { get; set; }
    public decimal TotalAdic4 { get; set; }
    public decimal TotalAdic5 { get; set; }
    public decimal TotalAdicionales { get; set; }
    public string AdicionalFijo1Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo1Importe { get; set; }
    public bool AdicionalFijo1Aplicado { get; set; }
    public bool AdicionalFijo1PideCantidad { get; set; }
    public decimal AdicionalFijo1Cantidad { get; set; } = 1m;
    public string AdicionalFijo2Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo2Importe { get; set; }
    public bool AdicionalFijo2Aplicado { get; set; }
    public bool AdicionalFijo2PideCantidad { get; set; }
    public decimal AdicionalFijo2Cantidad { get; set; } = 1m;
    public string AdicionalFijo3Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo3Importe { get; set; }
    public bool AdicionalFijo3Aplicado { get; set; }
    public bool AdicionalFijo3PideCantidad { get; set; }
    public decimal AdicionalFijo3Cantidad { get; set; } = 1m;
    public decimal AdicionalFijo1ImporteFletero { get; set; }
    public decimal AdicionalFijo2ImporteFletero { get; set; }
    public decimal AdicionalFijo3ImporteFletero { get; set; }
    public decimal TotalAdicionalesFijos { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public decimal IdListaRMTRF { get; set; }
    public decimal TotalImporte { get; set; }
    public decimal TotalFlete { get; set; }
    public decimal TotalTarifadoCliente { get; set; }
    public decimal TotalTarifadoFlete { get; set; }
    public decimal ImporteCliente { get; set; }
    public decimal ImporteFletero { get; set; }
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
    public decimal PorcentajeAdic5 { get; set; }
    public string AdicionalFijo1Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo1Importe { get; set; }
    public bool AdicionalFijo1Aplicado { get; set; }
    public bool AdicionalFijo1PideCantidad { get; set; }
    public decimal AdicionalFijo1Cantidad { get; set; } = 1m;
    public string AdicionalFijo2Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo2Importe { get; set; }
    public bool AdicionalFijo2Aplicado { get; set; }
    public bool AdicionalFijo2PideCantidad { get; set; }
    public decimal AdicionalFijo2Cantidad { get; set; } = 1m;
    public string AdicionalFijo3Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo3Importe { get; set; }
    public bool AdicionalFijo3Aplicado { get; set; }
    public bool AdicionalFijo3PideCantidad { get; set; }
    public decimal AdicionalFijo3Cantidad { get; set; } = 1m;
    public decimal AdicionalFijo1ImporteFletero { get; set; }
    public decimal AdicionalFijo2ImporteFletero { get; set; }
    public decimal AdicionalFijo3ImporteFletero { get; set; }
    public decimal TotalAdicionalesFijos { get; set; }
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
    public string AdicionalFijo1Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo1Importe { get; set; }
    public bool AdicionalFijo1PideCantidad { get; set; }
    public string AdicionalFijo2Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo2Importe { get; set; }
    public bool AdicionalFijo2PideCantidad { get; set; }
    public string AdicionalFijo3Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo3Importe { get; set; }
    public bool AdicionalFijo3PideCantidad { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime? FechaHoraModificacion { get; set; }
    public string UsuarioModificacion { get; set; } = string.Empty;
}

public sealed class CargaViajeTarifaClienteResumenDto
{
    public int IdTarifa { get; set; }
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public string DestinoCodigo { get; set; } = string.Empty;
    public string DestinoNombre { get; set; } = string.Empty;
    public string TipoVehiculoCodigo { get; set; } = string.Empty;
    public string TipoVehiculoNombre { get; set; } = string.Empty;
    public string ListaCodigo { get; set; } = string.Empty;
    public string ListaNombre { get; set; } = string.Empty;
    public decimal ImporteCliente { get; set; }
    public decimal ImporteFletero { get; set; }
    public string FleteroCodigoSugerido { get; set; } = string.Empty;
    public string FleteroNombreSugerido { get; set; } = string.Empty;
    public int FleteroCoincidencias { get; set; }
    public bool AdicionalFijo1PideCantidad { get; set; }
    public bool AdicionalFijo2PideCantidad { get; set; }
    public bool AdicionalFijo3PideCantidad { get; set; }
    public decimal AdicionalFijo1ImporteFletero { get; set; }
    public decimal AdicionalFijo2ImporteFletero { get; set; }
    public decimal AdicionalFijo3ImporteFletero { get; set; }
    public bool Activo { get; set; } = true;
    public bool TarifaFletero { get; set; }
    public bool EsTarifaGeneral { get; set; }
}

public sealed class CargaViajeTarifaSaveRequest
{
    public int Id { get; set; }
    public string IdLista { get; set; } = string.Empty;
    public string OriginalIdLista { get; set; } = string.Empty;
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
    public string AdicionalFijo1Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo1Importe { get; set; }
    public bool AdicionalFijo1PideCantidad { get; set; }
    public string AdicionalFijo2Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo2Importe { get; set; }
    public bool AdicionalFijo2PideCantidad { get; set; }
    public string AdicionalFijo3Descripcion { get; set; } = string.Empty;
    public decimal AdicionalFijo3Importe { get; set; }
    public bool AdicionalFijo3PideCantidad { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class CargaViajeChoferGridItemDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public bool Disponible { get; set; } = true;
    public bool EsFletero { get; set; }
}

public sealed class CargaViajeChoferSaveRequest
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public bool EsFletero { get; set; }
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

public sealed class CargaViajesAgrupacionDto<TItem>
{
    public string GrupoNombre { get; set; } = string.Empty;
    public string GrupoTipo { get; set; } = string.Empty;
    public int CantidadViajes { get; set; }
    public decimal TotalCliente { get; set; }
    public decimal TotalFletero { get; set; }
    public decimal TotalPeajes { get; set; }
    public decimal Resultado { get; set; }
    public IReadOnlyList<TItem> DetalleViajes { get; set; } = [];
}

public sealed class CargaViajesConfigDto
{
    public string Sucursal { get; set; } = "0001";
    public string Letra { get; set; } = "X";
    public string ChoferGeneral { get; set; } = string.Empty;
    public string? CodigoTarifaGeneral { get; set; }
    public int PorcentajesAdicionalesHabilitados { get; set; } = 3;
    public List<string> NombresAdicionales { get; set; } = ["Adicional 1", "Adicional 2", "Adicional 3", "Adicional 4", "Adicional 5", "Comisión"];
    public List<decimal> PorcentajesAdicionales { get; set; } = [0m, 0m, 0m, 0m, 0m, 0m];
    public bool[] AdicionalesHabilitados { get; set; } = [false, false, false, false, false, false];
    public bool[] AdicionalesSumarFletero { get; set; } = [false, false, false, false, false, false];
    public bool[] EsPorcentajeAdicionales { get; set; } = [true, true, true, true, true, true];
}

public sealed class CargaViajePreviewDto
{
    public CargaViajesDetailDto Viaje { get; set; } = new();
    public CargaViajesConfigDto Configuracion { get; set; } = new();
}

public sealed class ViajeConceptoTotalDto
{
    public int? Indice { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public bool AportaAlTotal { get; set; } = true;
}

public sealed class ViajeTotalesDto
{
    public List<ViajeConceptoTotalDto> ConceptosCliente { get; set; } = [];
    public List<ViajeConceptoTotalDto> ConceptosFletero { get; set; } = [];
    public decimal TotalCliente { get; set; }
    public decimal TotalFletero { get; set; }
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
    public const string Chofer = "chofer";
    public const string Fletero = "fletero";
    public const string Cliente = "cliente";
    public const string TipoVehiculo = "tipo-vehiculo";
    public const string Destino = "destino";
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

public sealed class CargaViajesReporteLiquidacionFilters
{
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public bool IncluirChoferes { get; set; } = true;
    public bool IncluirFleteros { get; set; } = false;
    public bool IncluirClientes { get; set; } = false;
    public bool IncluirTodoJunto { get; set; } = false;
    public string AgruparPor { get; set; } = CargaViajesViewGroupKeys.None;
    public string RangoRapido { get; set; } = CargaViajesReporteRangoRapidoKeys.MesActual;
    public string TipoPersona { get; set; } = CargaViajesReporteTipoPersonaKeys.ChoferesYFleteros;
    public string Modo { get; set; } = CargaViajesReporteModoKeys.Detallado;
    public string ChoferCodigo { get; set; } = string.Empty;
    public string ChoferTexto { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteTexto { get; set; } = string.Empty;
    public string DestinoCodigo { get; set; } = string.Empty;
    public string DestinoTexto { get; set; } = string.Empty;
    public string Estado { get; set; } = CargaViajesReporteEstadoKeys.Todos;
    public string EstadoPago { get; set; } = CargaViajesLiquidacionEstadoPagoKeys.Todos;
}

public sealed class CargaViajeReporteClienteConceptoDto
{
    public string Descripcion { get; set; } = string.Empty;
    public decimal Importe { get; set; }
}

public sealed class CargaViajeReporteClienteRowDto
{
    public CargaViajesDetailDto Viaje { get; set; } = new();
    public IReadOnlyList<CargaViajeReporteClienteConceptoDto> Conceptos { get; set; } = [];
}

public sealed class CargaViajesLiquidacionFilters
{
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string RangoRapido { get; set; } = CargaViajesReporteRangoRapidoKeys.MesActual;
    public string TipoPersona { get; set; } = CargaViajesReporteTipoPersonaKeys.ChoferesYFleteros;
    public bool IncluirChoferes { get; set; } = true;
    public bool IncluirFleteros { get; set; } = true;
    public string AgruparPor { get; set; } = CargaViajesViewGroupKeys.None;
    public string ChoferCodigo { get; set; } = string.Empty;
    public string ChoferTexto { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteTexto { get; set; } = string.Empty;
    public string DestinoCodigo { get; set; } = string.Empty;
    public string DestinoTexto { get; set; } = string.Empty;
    public string TipoVehiculoCodigo { get; set; } = string.Empty;
    public string TipoVehiculoTexto { get; set; } = string.Empty;
    public string Estado { get; set; } = CargaViajeEstadoKeys.Pendiente;
    public bool MostrarLiquidados { get; set; }
    public string EstadoPago { get; set; } = CargaViajesLiquidacionEstadoPagoKeys.Pendientes;
}

public sealed class CargaViajeReporteLiquidacionRowDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string IdComprobante { get; set; } = string.Empty;
    public string ChoferCodigo { get; set; } = string.Empty;
    public string ChoferNombre { get; set; } = string.Empty;
    public bool EsFletero { get; set; }
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public string DestinoCodigo { get; set; } = string.Empty;
    public string DestinoDescripcion { get; set; } = string.Empty;
    public string TipoVehiculoCodigo { get; set; } = string.Empty;
    public string TipoVehiculoDescripcion { get; set; } = string.Empty;
    public int CantidadViajes { get; set; }
    public decimal TotalFlete { get; set; }
    public decimal TotalConPeaje { get; set; }
    public decimal Peaje { get; set; }
    public bool FletePagado { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public IReadOnlyList<CargaViajeReporteClienteConceptoDto> ConceptosFletero { get; set; } = [];
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class CargaViajeLiquidacionRowDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Comprobante { get; set; } = string.Empty;
    public string ChoferCodigo { get; set; } = string.Empty;
    public string ChoferNombre { get; set; } = string.Empty;
    public bool EsFletero { get; set; }
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public string DestinoCodigo { get; set; } = string.Empty;
    public string DestinoDescripcion { get; set; } = string.Empty;
    public string TipoVehiculoCodigo { get; set; } = string.Empty;
    public string TipoVehiculoDescripcion { get; set; } = string.Empty;
    public int CantidadViajes { get; set; }
    public decimal TotalFlete { get; set; }
    public decimal Peaje { get; set; }
    public decimal TotalConPeaje { get; set; }
    public bool FletePagado { get; set; }
    public DateTime? FechaPagoFlete { get; set; }
    public string UsuarioPagoFlete { get; set; } = string.Empty;
    public string ObservacionPagoFlete { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
}

public sealed class CargaViajesMarcarPagadoRequest
{
    public List<int> Ids { get; set; } = [];
    public DateTime FechaPago { get; set; } = DateTime.Today;
    public string? Observacion { get; set; }
    public string? Usuario { get; set; }
}

public sealed class CargaViajeReporteLiquidacionResumenDto
{
    public string ChoferCodigo { get; set; } = string.Empty;
    public string ChoferNombre { get; set; } = string.Empty;
    public bool EsFletero { get; set; }
    public int CantidadViajes { get; set; }
    public decimal TotalFlete { get; set; }
    public decimal TotalPeaje { get; set; }
    public decimal TotalConPeaje { get; set; }
}

public sealed class CargaViajesReportTypeOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public static class CargaViajesReporteTipoKeys
{
    public const string LiquidacionChoferesFleteros = "liquidacion-choferes-fleteros";
    public const string LiquidacionChoferes = "liquidacion-choferes";
    public const string LiquidacionFleteros = "liquidacion-fleteros";
}

public static class CargaViajesLiquidacionEstadoPagoKeys
{
    public const string Pendientes = "pendientes";
    public const string Pagados = "pagados";
    public const string Todos = "todos";
}

public static class CargaViajesReporteRangoRapidoKeys
{
    public const string Hoy = "hoy";
    public const string SemanaActual = "semana-actual";
    public const string MesActual = "mes-actual";
    public const string MesAnterior = "mes-anterior";
    public const string AnioActual = "anio-actual";
    public const string AnioAnterior = "anio-anterior";
    public const string Personalizado = "personalizado";
    public const string Todo = "todo";

    public static readonly IReadOnlyList<string> All = [Hoy, SemanaActual, MesActual, MesAnterior, AnioActual, AnioAnterior, Personalizado, Todo];
}

public static class CargaViajesReporteTipoPersonaKeys
{
    public const string Choferes = "choferes";
    public const string Fleteros = "fleteros";
    public const string ChoferesYFleteros = "choferes-y-fleteros";

    public static readonly IReadOnlyList<string> All = [Choferes, Fleteros, ChoferesYFleteros];
}

public static class CargaViajesReporteModoKeys
{
    public const string Detallado = "detallado";
    public const string Resumen = "resumen";

    public static readonly IReadOnlyList<string> All = [Detallado, Resumen];
}

public static class CargaViajesReporteEstadoKeys
{
    public const string Todos = "todos";
    public const string Pendiente = "pendiente";
    public const string Finalizado = "finalizado";
    public const string PendientePago = "pendiente-pago";

    public static readonly IReadOnlyList<string> All = [Todos, Pendiente, Finalizado, PendientePago];
}

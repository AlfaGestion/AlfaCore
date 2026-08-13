namespace AlfaCore.Models;

public sealed class DashboardSummaryDto
{
    public decimal TotalComprado { get; init; }
    public decimal TicketPromedio { get; init; }
    public int CantidadComprobantes { get; init; }
    public int ProveedoresActivos { get; init; }
    public decimal NetoTotal { get; init; }
    public decimal IvaTotal { get; init; }
    public int CantidadArticulos { get; init; }
    public IReadOnlyList<MonthlyPointDto> EvolucionMensual { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopProveedores { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopRubros { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopArticulos { get; init; } = [];
    public IReadOnlyList<StatusMetricDto> Estados { get; init; } = [];
}

public sealed class MonthlyPointDto
{
    public string Periodo { get; init; } = string.Empty;
    public decimal Total { get; init; }
}

public sealed class CategoryTotalDto
{
    public string Categoria { get; init; } = string.Empty;
    public string? Codigo { get; init; }
    public decimal Total { get; init; }
    public decimal Participacion { get; init; }
}

public sealed class StatusMetricDto
{
    public string Estado { get; init; } = string.Empty;
    public int Cantidad { get; init; }
    public decimal Total { get; init; }
}

public class DashboardFilters
{
    public DashboardFilters()
    {
        var today = DateTime.Today;
        FechaDesde = new DateTime(today.Year, today.Month, 1);
        FechaHasta = today;
    }

    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string? Proveedor { get; set; }
    public string? Articulo { get; set; }
    public string? ArticuloCodigo { get; set; }
    public string? ArticuloDescripcion { get; set; }
    public string? Rubro { get; set; }
    public string? Familia { get; set; }
    public string? Usuario { get; set; }
    public string? Sucursal { get; set; }
    public string? Deposito { get; set; }
    public string? Estado { get; set; }
    public string? TipoComprobante { get; set; }

    public DashboardFilters Clone() => (DashboardFilters)MemberwiseClone();

    public DashboardFilters WithoutDates()
    {
        var copy = Clone();
        copy.FechaDesde = null;
        copy.FechaHasta = null;
        return copy;
    }
}

public sealed class FilterOptionsDto
{
    public IReadOnlyList<string> Proveedores { get; init; } = [];
    public IReadOnlyList<string> Articulos { get; init; } = [];
    public IReadOnlyList<string> Rubros { get; init; } = [];
    public IReadOnlyList<string> Familias { get; init; } = [];
    public IReadOnlyList<string> Usuarios { get; init; } = [];
    public IReadOnlyList<string> Sucursales { get; init; } = [];
    public IReadOnlyList<string> Depositos { get; init; } = [];
    public IReadOnlyList<string> Estados { get; init; } = [];
    public IReadOnlyList<string> TiposComprobante { get; init; } = [];
}

public sealed class ComprobanteDto
{
    public string Tc { get; init; } = string.Empty;
    public string IdComprobante { get; init; } = string.Empty;
    public string Numero { get; init; } = string.Empty;
    public DateTime Fecha { get; init; }
    public string Cuenta { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string Sucursal { get; init; } = string.Empty;
    public string Deposito { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public decimal NetoDashboard { get; init; }
    public decimal IvaDashboard { get; init; }
    public decimal ImporteDashboard { get; init; }
    public string EstadoComprobante { get; init; } = string.Empty;
    public int CantidadItems { get; init; }
    public bool TieneDetalle { get; init; }
    public bool EsContable { get; init; }
    public bool IvaEnCero { get; init; }
    public string AlertaOperativa { get; init; } = string.Empty;
}

public sealed class ComprobantesFilter : DashboardFilters
{
    public int Pagina { get; set; } = 1;
    public int TamanioPagina { get; set; } = 20;
}

public sealed class ComprobantesResultDto
{
    public IReadOnlyList<ComprobanteDto> Items { get; init; } = [];
    public int TotalRegistros { get; init; }
    public int PaginaActual { get; init; }
    public int TamanioPagina { get; init; }
    public int TotalPaginas => TamanioPagina == 0 ? 0 : (int)Math.Ceiling((double)TotalRegistros / TamanioPagina);
}

public sealed class ComprobanteDetalleDto
{
    public ComprobanteDto Cabecera { get; init; } = new();
    public IReadOnlyList<ComprobanteItemDto> Items { get; init; } = [];
}

public sealed class ComprobantesOverviewDto
{
    public decimal TotalComprado { get; init; }
    public int CantidadComprobantes { get; init; }
    public int ProveedoresActivos { get; init; }
    public decimal TicketPromedio { get; init; }
    public int ComprobantesEnCero { get; init; }
    public int ComprobantesSinDetalle { get; init; }
    public int ComprobantesIvaCero { get; init; }
    public decimal ComprobanteMaximo { get; init; }
    public string TcPredominante { get; init; } = string.Empty;
    public decimal TcPredominanteParticipacion { get; init; }
    public IReadOnlyList<MonthlyPointDto> EvolucionSemanal { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> ComposicionTipos { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopComprobantes { get; init; } = [];
    public IReadOnlyList<ComprobantesAlertDto> Alertas { get; init; } = [];
}

public sealed class ComprobantesAlertDto
{
    public string Tipo { get; init; } = "info";
    public string Titulo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
}

public sealed class ComprobanteItemDto
{
    public string IdArticulo { get; init; } = string.Empty;
    public string DescripcionArticulo { get; init; } = string.Empty;
    public string Rubro { get; init; } = string.Empty;
    public string Familia { get; init; } = string.Empty;
    public decimal Cantidad { get; init; }
    public decimal Costo { get; init; }
    public decimal Total { get; init; }
}

public sealed class ProveedorResumenDto
{
    public string Cuenta { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public decimal TotalComprado { get; init; }
    public decimal Participacion { get; init; }
    public int CantidadComprobantes { get; init; }
    public decimal TicketPromedio { get; init; }
    public DateTime? UltimaCompra { get; init; }
    public DateTime? PrimeraCompra { get; init; }
    public decimal? VariacionVsAnterior { get; init; }
    public bool EsNuevo { get; init; }
}

public sealed class ProveedoresKpiDto
{
    public decimal TotalComprado { get; init; }
    public int ProveedoresActivos { get; init; }
    public string TopProveedorNombre { get; init; } = string.Empty;
    public decimal TopProveedorTotal { get; init; }
    public decimal ConcentracionTop5 { get; init; }
    public decimal? VariacionTotalVsAnterior { get; init; }
    public string? MayorCaidaNombre { get; init; }
    public decimal? MayorCaidaVariacion { get; init; }
    public string? MayorCrecimientoNombre { get; init; }
    public decimal? MayorCrecimientoVariacion { get; init; }
}

public sealed class ProveedoresPageDto
{
    public ProveedoresKpiDto Kpis { get; init; } = new();
    public IReadOnlyList<ProveedorResumenDto> Proveedores { get; init; } = [];
}

public sealed class ProveedorDetalleDto
{
    public ProveedorResumenDto Resumen { get; init; } = new();
    public IReadOnlyList<CategoryTotalDto> TopArticulos { get; init; } = [];
    public IReadOnlyList<ComprobanteDto> UltimosComprobantes { get; init; } = [];
    public IReadOnlyList<MonthlyPointDto> EvolucionMensual { get; init; } = [];
}

public sealed class RubroResumenDto
{
    public string Rubro { get; init; } = string.Empty;
    public decimal TotalComprado { get; init; }
    public decimal Participacion { get; init; }
    public int CantidadArticulos { get; init; }
    public int CantidadComprobantes { get; init; }
    public decimal? TotalAnterior { get; init; }
    public decimal? VariacionVsAnterior { get; init; }
    public decimal TicketPromedio { get; init; }
    public DateTime? UltimaCompra { get; init; }
}

public sealed class RubroDetalleDto
{
    public RubroResumenDto Resumen { get; init; } = new();
    public IReadOnlyList<MonthlyPointDto> EvolucionMensual { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopArticulos { get; init; } = [];
    public IReadOnlyList<ComprobanteDto> UltimosComprobantes { get; init; } = [];
}

public sealed class RubrosKpiDto
{
    public decimal TotalComprado { get; init; }
    public int CantidadRubrosActivos { get; init; }
    public string RubroPrincipal { get; init; } = "Sin datos";
    public decimal ParticipacionRubroPrincipal { get; init; }
    public decimal? VariacionTotalVsAnterior { get; init; }
    public string RubroMayorCrecimiento { get; init; } = "Sin datos";
    public decimal? RubroMayorCrecimientoVariacion { get; init; }
    public string RubroMayorCaida { get; init; } = "Sin datos";
    public decimal? RubroMayorCaidaVariacion { get; init; }
    public decimal ConcentracionTop3 { get; init; }
}

public sealed class RubrosInsightDto
{
    public string Tipo { get; init; } = "info";
    public string Mensaje { get; init; } = string.Empty;
}

public sealed class RubrosPageDto
{
    public RubrosKpiDto Kpis { get; init; } = new();
    public IReadOnlyList<RubroResumenDto> Rubros { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> DistribucionGasto { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopRubros { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> VariacionesPositivas { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> VariacionesNegativas { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> ConcentracionTop3VsResto { get; init; } = [];
    public IReadOnlyList<RubrosInsightDto> Insights { get; init; } = [];
}

public sealed class FamiliaResumenDto
{
    public string Familia { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string PadreIdFamilia { get; init; } = string.Empty;
    public int NivelJerarquico { get; init; }
    public bool TieneHijos { get; init; }
    public decimal TotalComprado { get; init; }
    public decimal Participacion { get; init; }
    public int CantidadArticulos { get; init; }
    public int CantidadProveedores { get; init; }
    public int CantidadComprobantes { get; init; }
    public decimal? TotalAnterior { get; init; }
    public decimal? VariacionVsAnterior { get; init; }
    public decimal TicketPromedio { get; init; }
    public DateTime? UltimaCompra { get; init; }
}

public sealed class FamiliaDetalleDto
{
    public FamiliaResumenDto Resumen { get; init; } = new();
    public IReadOnlyList<MonthlyPointDto> EvolucionMensual { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> ComposicionInterna { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> Articulos { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> Proveedores { get; init; } = [];
    public IReadOnlyList<ComprobanteDto> UltimosComprobantes { get; init; } = [];
}

public sealed class FamiliasKpiDto
{
    public decimal TotalComprado { get; init; }
    public int CantidadFamiliasActivas { get; init; }
    public string FamiliaPrincipal { get; init; } = "Sin datos";
    public decimal ParticipacionFamiliaPrincipal { get; init; }
    public decimal? VariacionTotalVsAnterior { get; init; }
    public string FamiliaMayorCrecimiento { get; init; } = "Sin datos";
    public decimal? FamiliaMayorCrecimientoVariacion { get; init; }
    public string FamiliaMayorCaida { get; init; } = "Sin datos";
    public decimal? FamiliaMayorCaidaVariacion { get; init; }
    public decimal ConcentracionTop5 { get; init; }
}

public sealed class FamiliasInsightDto
{
    public string Tipo { get; init; } = "info";
    public string Mensaje { get; init; } = string.Empty;
}

public sealed class FamiliasPageDto
{
    public FamiliasKpiDto Kpis { get; init; } = new();
    public IReadOnlyList<FamiliaResumenDto> Familias { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopFamilias { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> DistribucionGasto { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> VariacionesPositivas { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> VariacionesNegativas { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> ConcentracionTop5VsResto { get; init; } = [];
    public IReadOnlyList<FamiliasInsightDto> Insights { get; init; } = [];
}

public sealed class ArticuloResumenDto
{
    public string IdArticulo { get; init; } = string.Empty;
    public string DescripcionArticulo { get; init; } = string.Empty;
    public decimal CantidadComprada { get; init; }
    public decimal TotalComprado { get; init; }
    public decimal CostoPromedio { get; init; }
    public decimal PrecioActual { get; init; }
    public decimal PrecioAnterior { get; init; }
    public decimal? VariacionPrecio { get; init; }
    public string ProveedorPrincipal { get; init; } = string.Empty;
    public string ProveedorPrincipalCuenta { get; init; } = string.Empty;
    public decimal ParticipacionProveedorPrincipal { get; init; }
    public DateTime? UltimaCompra { get; init; }
    public DateTime? FechaAlta { get; init; }
    public int CantidadCompras { get; init; }
    /// <summary>True si el artículo no tiene ninguna compra anterior a FechaDesde del período actual.</summary>
    public bool EsNuevo { get; init; }
}

public sealed class ArticuloDetalleDto
{
    public ArticuloResumenDto Resumen { get; init; } = new();
    public IReadOnlyList<MonthlyPointDto> EvolucionCosto { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> Proveedores { get; init; } = [];
    public IReadOnlyList<ComprobanteDto> Historial { get; init; } = [];
}

public sealed class ArticulosKpiDto
{
    public decimal TotalComprado { get; init; }
    public int CantidadArticulosDistintos { get; init; }
    public int CantidadItems { get; init; }
    public decimal CostoPromedioGeneral { get; init; }
    public int ArticulosConAumento { get; init; }
    public int ArticulosConBaja { get; init; }
    public int ArticulosNuevos { get; init; }
    public string MayorAumentoArticulo { get; init; } = string.Empty;
    public decimal? MayorAumentoVariacion { get; init; }
    public string MayorBajaArticulo { get; init; } = string.Empty;
    public decimal? MayorBajaVariacion { get; init; }
}

public sealed class ArticulosInsightDto
{
    public string Tipo { get; init; } = "info";
    public string Mensaje { get; init; } = string.Empty;
}

public sealed class ArticulosPageDto
{
    public ArticulosKpiDto Kpis { get; init; } = new();
    public IReadOnlyList<ArticuloResumenDto> Articulos { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopPorTotal { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopPorCantidad { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopAumentos { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> TopBajas { get; init; } = [];
    public IReadOnlyList<ArticulosInsightDto> Insights { get; init; } = [];
}

public sealed class ActividadKpisDto
{
    public int CantidadComprobantes { get; init; }
    public int CantidadItems { get; init; }
    public int UsuariosActivos { get; init; }
    public decimal PromedioItemsPorComprobante { get; init; }
    public decimal PromedioComprobantesPorUsuario { get; init; }
    public string DiaMayorActividad { get; init; } = string.Empty;
    public string UsuarioMasActivo { get; init; } = string.Empty;
    public string UsuarioMasDetalle { get; init; } = string.Empty;
    public int ComprobantesConDetalleMaximos { get; init; }
    public int ItemsDelUsuarioMasDetalle { get; init; }
}

public sealed class ActividadUsuarioResumenDto
{
    public string Usuario { get; init; } = string.Empty;
    public int CantidadComprobantes { get; init; }
    public int CantidadItems { get; init; }
    public decimal PromedioItemsPorComprobante { get; init; }
    public decimal ImporteTotal { get; init; }
    public DateTime? UltimaActividad { get; init; }
    public int DiasConActividad { get; init; }
    public int ComprobantesConDetalle { get; init; }
    public int ComprobantesContables { get; init; }
    public decimal PorcentajeConDetalle { get; init; }
}

public sealed class ActividadDiaDto
{
    public DateTime Fecha { get; init; }
    public int CantidadComprobantes { get; init; }
    public int CantidadItems { get; init; }
    public decimal ImporteTotal { get; init; }
}

public sealed class ActividadInsightDto
{
    public string Tipo { get; init; } = "info";
    public string Mensaje { get; init; } = string.Empty;
}

public sealed class ActividadPageDto
{
    public ActividadKpisDto Kpis { get; init; } = new();
    public IReadOnlyList<ActividadUsuarioResumenDto> Usuarios { get; init; } = [];
    public IReadOnlyList<ActividadDiaDto> ActividadPorDia { get; init; } = [];
    public IReadOnlyList<MonthlyPointDto> SerieComprobantesPorDia { get; init; } = [];
    public IReadOnlyList<MonthlyPointDto> SerieItemsPorDia { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> ComprobantesPorUsuario { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> ItemsPorUsuario { get; init; } = [];
    public IReadOnlyList<CategoryTotalDto> SegmentacionTipoCarga { get; init; } = [];
    public IReadOnlyList<ActividadInsightDto> Insights { get; init; } = [];
}

public sealed class ActividadUsuarioDetalleDto
{
    public ActividadUsuarioResumenDto Resumen { get; init; } = new();
    public IReadOnlyList<MonthlyPointDto> SerieComprobantesPorDia { get; init; } = [];
    public IReadOnlyList<MonthlyPointDto> SerieItemsPorDia { get; init; } = [];
    public IReadOnlyList<ComprobanteDto> Comprobantes { get; init; } = [];
}

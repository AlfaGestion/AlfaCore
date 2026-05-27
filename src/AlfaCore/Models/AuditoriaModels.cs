namespace AlfaCore.Models;

public sealed class AuditoriaErrorRowDto
{
    public int Id { get; init; }
    public DateTime Fecha { get; init; }
    public string Proceso { get; init; } = string.Empty;
    public int ErrorCodigo { get; init; }
    public string Descripcion { get; init; } = string.Empty;
    public string Sql { get; init; } = string.Empty;
    public string Pc { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
}

public sealed class AuditoriaErrorFilterDto
{
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public string Usuario { get; init; } = string.Empty;
    public string Proceso { get; init; } = string.Empty;
    public string Pc { get; init; } = string.Empty;
    public int? ErrorCodigo { get; init; }
    public string Texto { get; init; } = string.Empty;
    public int MaxRows { get; init; } = 200;
}

public sealed class AuditoriaResumenDto
{
    public int ErrorsToday { get; init; }
    public int ErrorsLast7Days { get; init; }
    public string TopProcess { get; init; } = string.Empty;
    public string TopUser { get; init; } = string.Empty;
    public IReadOnlyList<AuditoriaSerieDto> ErrorsByDay { get; init; } = [];
    public IReadOnlyList<AuditoriaRankingItemDto> TopProcesses { get; init; } = [];
    public IReadOnlyList<AuditoriaRankingItemDto> TopUsers { get; init; } = [];
}

public sealed class AuditoriaSerieDto
{
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
}

public sealed class AuditoriaRankingItemDto
{
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
}

public sealed class AuditoriaUsuarioFilterDto
{
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public string Texto { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public string Pc { get; init; } = string.Empty;
    public string TipoComprobante { get; init; } = string.Empty;
    public string Riesgo { get; init; } = string.Empty;
    public string TipoControl { get; init; } = string.Empty;
    public string CuentaCliente { get; init; } = string.Empty;
    public int? DiasMinimosSinFactura { get; init; }
    public int? UmbralModificaciones { get; init; }
    public int? DiasToleranciaDuplicados { get; init; }
    public bool SoloDiferenciasSucursal { get; init; }
    public string OrdenCampo { get; init; } = "fecha";
    public string OrdenDireccion { get; init; } = "desc";
    public int Pagina { get; init; } = 1;
    public int TamanioPagina { get; init; } = 50;
}

public sealed class AuditoriaUsuarioRowDto
{
    public string TipoControl { get; init; } = string.Empty;
    public string TipoControlLabel { get; init; } = string.Empty;
    public string Riesgo { get; init; } = string.Empty;
    public DateTime FechaHora { get; init; }
    public DateTime? FechaHoraHasta { get; init; }
    public string Usuario { get; init; } = string.Empty;
    public string Pc { get; init; } = string.Empty;
    public string TipoComprobante { get; init; } = string.Empty;
    public string IdComprobante { get; init; } = string.Empty;
    public string Cuenta { get; init; } = string.Empty;
    public string Cliente { get; init; } = string.Empty;
    public decimal? ImporteOriginal { get; init; }
    public decimal? ImporteAplicado { get; init; }
    public decimal? Diferencia { get; init; }
    public int CantidadOcurrencias { get; init; }
    public string Proceso { get; init; } = string.Empty;
    public string Formulario { get; init; } = string.Empty;
    public string FacturaRelacionada { get; init; } = string.Empty;
    public string EstadoFacturacion { get; init; } = string.Empty;
    public string SystemUserDetalle { get; init; } = string.Empty;
    public string Motivo { get; init; } = string.Empty;
    public string Observaciones { get; init; } = string.Empty;
    public int DiasPendientes { get; init; }
    public string NumeroNormalizado { get; init; } = string.Empty;
    public string SucursalesDetectadas { get; init; } = string.Empty;
    public string UsuariosDetectados { get; init; } = string.Empty;
    public string ImpactoContable { get; init; } = string.Empty;
    public int ComprobantesInvolucrados { get; init; }
    public int ContablesDuplicados { get; init; }
    public string DetalleGrupo { get; init; } = string.Empty;
}

public sealed class AuditoriaUsuarioStatsDto
{
    public int TotalAlertas { get; init; }
    public int RiesgoAlto { get; init; }
    public int RiesgoMedio { get; init; }
    public int ControlesDetectados { get; init; }
    public int ComprobantesInvolucrados { get; init; }
    public int ContablesDuplicados { get; init; }
    public int UsuariosInvolucrados { get; init; }
    public decimal? PromedioMinutosCancelacion { get; init; }
    public int MaximoMinutosCancelacion { get; init; }
    public decimal? ImporteTotalCancelado { get; init; }
}

public sealed class AuditoriaUsuarioResultDto
{
    public IReadOnlyList<AuditoriaUsuarioRowDto> Items { get; init; } = [];
    public AuditoriaUsuarioStatsDto Stats { get; init; } = new();
    public int TotalRegistros { get; init; }
    public int Pagina { get; init; }
    public int TamanioPagina { get; init; }
    public int DiasMinimosSinFacturaDefault { get; init; }
    public int UmbralModificacionesDefault { get; init; }
    public int DiasToleranciaDuplicadosDefault { get; init; }
    public bool SoloDiferenciasSucursalDefault { get; init; }
}

public sealed class AuditoriaUsuarioLookupsDto
{
    public IReadOnlyList<string> Usuarios { get; init; } = [];
    public IReadOnlyList<string> Pcs { get; init; } = [];
    public IReadOnlyList<string> TiposComprobante { get; init; } = [];
    public IReadOnlyList<string> CuentasClientes { get; init; } = [];
    public IReadOnlyList<AuditoriaLookupItemDto> TiposControl { get; init; } = [];
    public IReadOnlyList<AuditoriaLookupItemDto> Riesgos { get; init; } = [];
    public int DiasMinimosSinFacturaDefault { get; init; }
    public int UmbralModificacionesDefault { get; init; }
    public int DiasToleranciaDuplicadosDefault { get; init; }
    public bool SoloDiferenciasSucursalDefault { get; init; }
}

public sealed class AuditoriaUsuarioSettingsDto
{
    public int HoraInicioLaboral { get; init; } = 6;
    public int HoraFinLaboral { get; init; } = 21;
    public IReadOnlyList<int> DiasLaborales { get; init; } = [1, 2, 3, 4, 5];
    public bool ControlarPcHabitual { get; init; } = true;
    public int HoraInicioSugerida { get; init; } = 6;
    public int HoraFinSugerida { get; init; } = 21;
    public IReadOnlyList<int> DiasLaboralesSugeridos { get; init; } = [1, 2, 3, 4, 5];
    public bool ControlarPcHabitualSugerido { get; init; } = true;
    public int DiasAnalizados { get; init; }
    public int AccionesAnalizadas { get; init; }
    public bool UsaValoresInferidos { get; init; }
    public string ResumenInferencia { get; init; } = string.Empty;
}

public sealed class AuditoriaLookupItemDto
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class AuditoriaComprobanteFilterDto
{
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public string Texto { get; init; } = string.Empty;
    public string TipoComprobante { get; init; } = string.Empty;
    public string Riesgo { get; init; } = string.Empty;
    public string TipoControl { get; init; } = string.Empty;
    public string CuentaCliente { get; init; } = string.Empty;
    public string OrdenCampo { get; init; } = "fecha";
    public string OrdenDireccion { get; init; } = "desc";
    public int Pagina { get; init; } = 1;
    public int TamanioPagina { get; init; } = 50;
}

public sealed class AuditoriaComprobanteRowDto
{
    public string TipoControl { get; init; } = string.Empty;
    public string TipoControlLabel { get; init; } = string.Empty;
    public string Riesgo { get; init; } = string.Empty;
    public DateTime FechaHora { get; init; }
    public string TipoComprobante { get; init; } = string.Empty;
    public string IdComprobante { get; init; } = string.Empty;
    public int IdComplemento { get; init; }
    public string Cuenta { get; init; } = string.Empty;
    public string Cliente { get; init; } = string.Empty;
    public decimal? ImporteCabecera { get; init; }
    public decimal? ImporteDetalle { get; init; }
    public decimal? Diferencia { get; init; }
    public int CantidadOcurrencias { get; init; }
    public string Motivo { get; init; } = string.Empty;
    public string Observaciones { get; init; } = string.Empty;
}

public sealed class AuditoriaComprobanteStatsDto
{
    public int TotalAlertas { get; init; }
    public int RiesgoAlto { get; init; }
    public int RiesgoMedio { get; init; }
    public int ControlesDetectados { get; init; }
}

public sealed class AuditoriaComprobanteResultDto
{
    public IReadOnlyList<AuditoriaComprobanteRowDto> Items { get; init; } = [];
    public AuditoriaComprobanteStatsDto Stats { get; init; } = new();
    public int TotalRegistros { get; init; }
    public int Pagina { get; init; }
    public int TamanioPagina { get; init; }
}

public sealed class AuditoriaComprobanteLookupsDto
{
    public IReadOnlyList<string> TiposComprobante { get; init; } = [];
    public IReadOnlyList<string> CuentasClientes { get; init; } = [];
    public IReadOnlyList<AuditoriaLookupItemDto> TiposControl { get; init; } = [];
    public IReadOnlyList<AuditoriaLookupItemDto> Riesgos { get; init; } = [];
}

using System.Text.Json;

namespace AlfaCore.Models;

public sealed class InterfacesFilters
{
    public DateTime? Desde { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime? Hasta { get; set; } = DateTime.Today;
    public int? IdEstado { get; set; }
    public int? IdTipoDocumento { get; set; }
    public string Texto { get; set; } = string.Empty;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class InterfacesEstadoOptionDto
{
    public int IdEstado { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int Orden { get; set; }
    public bool Activo { get; set; }
    public bool PermiteEdicion { get; set; }
    public bool EsInicial { get; set; }
    public bool EsFinal { get; set; }
    public string Color { get; set; } = string.Empty;
}

public sealed class InterfacesTipoDocumentoOptionDto
{
    public int IdTipoDocumento { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int Orden { get; set; }
    public bool Activo { get; set; }
}

public sealed class InterfacesUploadSettingsDto
{
    public string DestinoTipo { get; set; } = "FTP";
    public string DestinoNombre { get; set; } = "Recepción principal";
    public string RutaBase { get; set; } = string.Empty;
    public string FtpHost { get; set; } = "alfanet.ddns.net";
    public int FtpPuerto { get; set; } = 21;
    public string FtpUsuario { get; set; } = "ftpalfa";
    public string FtpClave { get; set; } = "24681012";
    public bool FtpModoPasivo { get; set; } = true;
    public string EstadoInicialCodigo { get; set; } = "A_PROCESAR";
    public int TamanoMaximoMb { get; set; } = 25;
    public long TamanoMaximoBytes => Math.Max(1, TamanoMaximoMb) * 1024L * 1024L;
    public IReadOnlyList<string> ExtensionesPermitidas { get; set; } = [];

    public bool UsaFtp => string.Equals(DestinoTipo, "FTP", StringComparison.OrdinalIgnoreCase);
    public bool UsaCarpeta => string.Equals(DestinoTipo, "CARPETA", StringComparison.OrdinalIgnoreCase);

    public string GetDestinoResumen()
        => UsaFtp
            ? $"{BuildFtpBaseUrl()} · usuario {FtpUsuario}"
            : RutaBase;

    public string BuildFtpBaseUrl()
    {
        var host = (FtpHost ?? string.Empty).Trim();
        var path = NormalizeFtpPath(RutaBase);
        return string.IsNullOrWhiteSpace(host)
            ? string.Empty
            : $"ftp://{host}:{Math.Max(1, FtpPuerto)}{path}";
    }

    public static string NormalizeFtpPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/";

        var normalized = value.Trim().Replace('\\', '/');
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;
        return normalized.TrimEnd('/') + "/";
    }
}

public sealed class InterfacesCompraIaSettingsDto
{
    public bool Habilitado { get; set; }
    public string PythonExe { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string WorkDir { get; set; } = string.Empty;
    public bool WorkerHabilitado { get; set; } = true;
    public int WorkerMaxParalelo { get; set; } = 1;
    public int WorkerIntervaloSegundos { get; set; } = 10;
}

public sealed class InterfacesCompraIaProbeResultDto
{
    public string PythonVersion { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string WorkDir { get; set; } = string.Empty;
}

public sealed class InterfacesInboxItemDto
{
    public long IdComprobanteRecibido { get; set; }
    public DateTime FechaHoraGrabacion { get; set; }
    public string UsuarioAlta { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;
    public int CantidadAdjuntos { get; set; }
    public bool Eliminado { get; set; }
    public int IdEstado { get; set; }
    public string EstadoCodigo { get; set; } = string.Empty;
    public string EstadoDescripcion { get; set; } = string.Empty;
    public bool PermiteEdicion { get; set; }
    public int IdTipoDocumento { get; set; }
    public string TipoDocumentoCodigo { get; set; } = string.Empty;
    public string TipoDocumentoDescripcion { get; set; } = string.Empty;
}

public sealed class InterfacesViewSettingsDto
{
    public string AgruparPor { get; set; } = InterfacesViewGroupKeys.None;
    public List<InterfacesViewColumnDto> Columnas { get; set; } = [];
}

public sealed class InterfacesViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
}

public static class InterfacesViewColumnKeys
{
    public const string Numero = "numero";
    public const string Fecha = "fecha";
    public const string Tipo = "tipo";
    public const string Estado = "estado";
    public const string Usuario = "usuario";
    public const string Observacion = "observacion";
    public const string Adjuntos = "adjuntos";
}

public static class InterfacesViewGroupKeys
{
    public const string None = "none";
    public const string Estado = "estado";
    public const string Tipo = "tipo";
}

public sealed class InterfacesAdjuntoDto
{
    public long IdAdjunto { get; set; }
    public long IdComprobanteRecibido { get; set; }
    public int Orden { get; set; }
    public string NombreOriginal { get; set; } = string.Empty;
    public string NombreGuardado { get; set; } = string.Empty;
    public string RutaRelativa { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public bool EsPrincipal { get; set; }
    public bool Eliminado { get; set; }
    public DateTime FechaHoraGrabacion { get; set; }
}

public sealed class InterfacesHistorialDto
{
    public long IdHistorial { get; set; }
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Pc { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public int? IdEstadoAnterior { get; set; }
    public string EstadoAnteriorDescripcion { get; set; } = string.Empty;
    public int? IdEstadoNuevo { get; set; }
    public string EstadoNuevoDescripcion { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;
    public string DataJson { get; set; } = string.Empty;
}

public sealed class InterfacesDetalleDto
{
    public long IdComprobanteRecibido { get; set; }
    public DateTime FechaHoraGrabacion { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    public DateTime FechaHoraEstado { get; set; }
    public DateTime? FechaHoraAnulacion { get; set; }
    public string UsuarioAlta { get; set; } = string.Empty;
    public string PcAlta { get; set; } = string.Empty;
    public string UsuarioModificacion { get; set; } = string.Empty;
    public string PcModificacion { get; set; } = string.Empty;
    public string UsuarioAnulacion { get; set; } = string.Empty;
    public string PcAnulacion { get; set; } = string.Empty;
    public int IdEstado { get; set; }
    public string EstadoCodigo { get; set; } = string.Empty;
    public string EstadoDescripcion { get; set; } = string.Empty;
    public bool PermiteEdicion { get; set; }
    public int IdTipoDocumento { get; set; }
    public string TipoDocumentoCodigo { get; set; } = string.Empty;
    public string TipoDocumentoDescripcion { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;
    public string MotivoAnulacion { get; set; } = string.Empty;
    public int CantidadAdjuntos { get; set; }
    public string RutaBase { get; set; } = string.Empty;
    public string ReferenciaExterna { get; set; } = string.Empty;
    public bool Eliminado { get; set; }
    public InterfacesCompraIaResultadoDto? LecturaCompra { get; set; }
    public IReadOnlyList<InterfacesAdjuntoDto> Adjuntos { get; set; } = [];
    public IReadOnlyList<InterfacesHistorialDto> Historial { get; set; } = [];
}

public sealed class InterfacesCompraIaResultadoDto
{
    public int Id { get; set; }
    public long? IdComprobanteRecibido { get; set; }
    public long? IdAdjuntoFuente { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime FechaHoraProceso { get; set; }
    public DateTime? FechaHoraInicio { get; set; }
    public DateTime? FechaHoraFin { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    public string UsuarioProceso { get; set; } = string.Empty;
    public string ObservacionesRevision { get; set; } = string.Empty;
    public bool SolicitarCancelacion { get; set; }
    public int Intentos { get; set; }
    public string ArchivoRutaOriginal { get; set; } = string.Empty;
    public string ArchivoNombreOriginal { get; set; } = string.Empty;
    public string ArchivoNombreRenombrado { get; set; } = string.Empty;
    public string ProveedorNombre { get; set; } = string.Empty;
    public string ProveedorCuit { get; set; } = string.Empty;
    public string ProveedorDomicilio { get; set; } = string.Empty;
    public string ProveedorCondIva { get; set; } = string.Empty;
    public string CuentaContable { get; set; } = string.Empty;
    public string MatchMetodo { get; set; } = string.Empty;
    public string TipoComprobante { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public string PuntoVenta { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public DateTime? Fecha { get; set; }
    public DateTime? Vencimiento { get; set; }
    public string Cae { get; set; } = string.Empty;
    public DateTime? VtoCae { get; set; }
    public string Moneda { get; set; } = string.Empty;
    public decimal? NetoGravado { get; set; }
    public decimal? NetoNoGravado { get; set; }
    public decimal? Exento { get; set; }
    public decimal? Iva21 { get; set; }
    public decimal? Iva105 { get; set; }
    public decimal? Iva27 { get; set; }
    public decimal? PercepcionIva { get; set; }
    public decimal? PercepcionIibb { get; set; }
    public decimal? PercepcionGanancias { get; set; }
    public decimal? ImpuestosInternos { get; set; }
    public decimal? OtrosImpuestos { get; set; }
    public decimal? Total { get; set; }
    public string LectorObservaciones { get; set; } = string.Empty;
    public string LectorError { get; set; } = string.Empty;
    public string JsonResultado { get; set; } = string.Empty;
    public IReadOnlyList<InterfacesCompraIaDetalleItemDto> Items { get; set; } = [];
}

public sealed class InterfacesCompraIaQueueSnapshotDto
{
    public int Pendientes { get; set; }
    public int Procesando { get; set; }
    public int Procesados { get; set; }
    public int ConError { get; set; }
    public int Cancelados { get; set; }
    public IReadOnlyList<InterfacesCompraIaResultadoDto> Items { get; set; } = [];
}

public sealed class InterfacesCompraIaDetalleItemDto
{
    public int Id { get; set; }
    public int IdCab { get; set; }
    public int NroRenglon { get; set; }
    public string Cantidad { get; set; } = string.Empty;
    public string CodigoArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Ud { get; set; } = string.Empty;
    public decimal? ImporteLista { get; set; }
    public double? Dto1 { get; set; }
    public double? Dto2 { get; set; }
    public decimal? ImporteNeto { get; set; }
    public double? Iva { get; set; }
    public decimal? ImpuestosInternos { get; set; }
    public decimal? Total { get; set; }
    public string AuxNroLote { get; set; } = string.Empty;
    public string AuxNroSerie { get; set; } = string.Empty;
    public string BlPq { get; set; } = string.Empty;
    public string Moneda { get; set; } = string.Empty;
    public decimal? TotImpInt { get; set; }
}

public sealed class InterfacesCrearAdjuntoRequest
{
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public Stream Contenido { get; set; } = Stream.Null;
}

public sealed class InterfacesCrearComprobanteRequest
{
    public int IdTipoDocumento { get; set; }
    public string Observacion { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
    public IReadOnlyList<InterfacesCrearAdjuntoRequest> Adjuntos { get; set; } = [];
}

public sealed class InterfacesCambioEstadoRequest
{
    public long IdComprobanteRecibido { get; set; }
    public int IdEstadoNuevo { get; set; }
    public string Observacion { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
}

public sealed class InterfacesActualizarComprobanteRequest
{
    public long IdComprobanteRecibido { get; set; }
    public int IdTipoDocumento { get; set; }
    public string Observacion { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
}

public sealed class InterfacesAgregarAdjuntosRequest
{
    public long IdComprobanteRecibido { get; set; }
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
    public IReadOnlyList<InterfacesCrearAdjuntoRequest> Adjuntos { get; set; } = [];
}

public sealed class InterfacesEliminarAdjuntoRequest
{
    public long IdAdjunto { get; set; }
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;
}

public sealed class InterfacesAdjuntoServeDto
{
    public string RutaCompleta { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
}

public sealed class InterfacesEliminarComprobantesRequest
{
    public IReadOnlyList<long> IdsComprobanteRecibido { get; set; } = [];
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
}

public sealed class InterfacesDetectarCompraRequest
{
    public long IdComprobanteRecibido { get; set; }
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
}

public sealed class InterfacesCompraIaAccionRequest
{
    public int Id { get; set; }
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
}

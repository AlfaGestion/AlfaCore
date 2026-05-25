namespace AlfaCore.Models;

public sealed class ActualizacionesDashboardDto
{
    public string FechaUpdateActual { get; set; } = string.Empty;
    public string RutaLocal { get; set; } = string.Empty;
    public string RutaRed { get; set; } = string.Empty;
    public string RutaOrigenActiva { get; set; } = string.Empty;
    public bool RutaRedDisponible { get; set; }
    public bool UsaRutaRed { get; set; }
    public string BaseActiva { get; set; } = string.Empty;
    public ActualizacionesMotorEstadoDto MotorEstado { get; set; } = new();
    public IReadOnlyList<ActualizacionScriptDto> Scripts { get; set; } = [];
    public IReadOnlyList<ActualizacionScriptDto> Pendientes { get; set; } = [];
    public IReadOnlyList<ActualizacionHistorialDto> Historial { get; set; } = [];
}

public sealed class ActualizacionesSettingsDto
{
    public string RutaRed { get; set; } = string.Empty;
}

public sealed class ActualizacionesRunRequest
{
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
    public bool ForzarRutaLocal { get; set; }
    public bool EsEjecucionAutomatica { get; set; }
}

public sealed class ActualizacionesRunResultDto
{
    public int CantidadAplicada { get; set; }
    public string VersionAnterior { get; set; } = string.Empty;
    public string VersionFinal { get; set; } = string.Empty;
    public string RutaOrigen { get; set; } = string.Empty;
    public bool SinCambios { get; set; }
    public IReadOnlyList<string> ScriptsAplicados { get; set; } = [];
}

public sealed class ActualizacionScriptDto
{
    public string VersionKey { get; set; } = string.Empty;
    public DateTime FechaVersion { get; set; }
    public int Correlativo { get; set; }
    public string FechaVersionTexto => FechaVersion.ToString("dd/MM/yyyy");
    public string Archivo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string RutaCompleta { get; set; } = string.Empty;
}

public sealed class ActualizacionHistorialDto
{
    public int IdActualizacionHist { get; set; }
    public DateTime FechaHora { get; set; }
    public string VersionAnterior { get; set; } = string.Empty;
    public string VersionNueva { get; set; } = string.Empty;
    public string ScriptArchivo { get; set; } = string.Empty;
    public string RutaOrigen { get; set; } = string.Empty;
    public string Resultado { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Pc { get; set; } = string.Empty;
    public string ErrorDetalle { get; set; } = string.Empty;
}

public sealed class ActualizacionesMotorEstadoDto
{
    public bool EnEjecucion { get; set; }
    public bool EsEjecucionAutomatica { get; set; }
    public bool TieneRegistro { get; set; }
    public bool UltimaEjecucionOk { get; set; }
    public bool SinCambios { get; set; }
    public DateTime? InicioUltimaEjecucion { get; set; }
    public DateTime? FinUltimaEjecucion { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string ErrorDetalle { get; set; } = string.Empty;
    public string BaseObjetivo { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
    public string PcAccion { get; set; } = string.Empty;
    public string VersionFinal { get; set; } = string.Empty;
    public string RutaOrigen { get; set; } = string.Empty;
    public int CantidadAplicada { get; set; }
    public IReadOnlyList<string> ScriptsAplicados { get; set; } = [];
}

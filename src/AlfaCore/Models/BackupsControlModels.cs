namespace AlfaCore.Models;

public sealed class BackupStatusRequest
{
    public string IdCliente { get; set; } = string.Empty;
    public string DbServidor { get; set; } = string.Empty;
    public string DbNombre { get; set; } = string.Empty;
    public string TipoBackup { get; set; } = string.Empty;
    public string Resultado { get; set; } = string.Empty;
    public string? Mensaje { get; set; }
    public string? HostCliente { get; set; }
    public string? UsuarioSql { get; set; }
    public string? InstanciaSql { get; set; }
    public string? VersionSql { get; set; }
    public string? SistemaOperativo { get; set; }
    public decimal? TamanioBaseMB { get; set; }
    public decimal? EspacioLibreDiscoGB { get; set; }
    public decimal? EspacioTotalDiscoGB { get; set; }
    public decimal? EspacioLibreDiscoBckGB { get; set; }
    public decimal? EspacioTotalDiscoBckGB { get; set; }
    public DateTime FechaHoraBackup { get; set; }
}

public sealed class BackupControlDto
{
    public long IdControl { get; init; }
    public string IdCliente { get; init; } = string.Empty;
    public int? IdBase { get; init; }
    public string DbServidor { get; init; } = string.Empty;
    public string DbNombre { get; init; } = string.Empty;
    public string TipoBackup { get; init; } = string.Empty;
    public string Resultado { get; init; } = string.Empty;
    public string? Mensaje { get; init; }
    public DateTime FechaHoraBackup { get; init; }
    public DateTime FechaHoraRecepcion { get; init; }
}

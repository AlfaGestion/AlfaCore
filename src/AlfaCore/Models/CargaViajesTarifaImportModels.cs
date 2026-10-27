namespace AlfaCore.Models;

public sealed class CargaViajesTarifaImportRow
{
    public string NombreLista { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;
    public string ChoferCodigo { get; set; } = string.Empty;
    public string TipoVehiculo { get; set; } = string.Empty;
    public string TipoVehiculoCodigo { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string DestinoCodigo { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public bool TarifaFletero { get; set; }
    public bool Seleccionada { get; set; }
    public bool TarifaExistente { get; set; }
    public int TarifaExistenteId { get; set; }
    public string TarifaExistenteIdLista { get; set; } = string.Empty;
    public string EstadoCoincidencia { get; set; } = "Pendiente";
    public string Confianza { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

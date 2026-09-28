namespace AlfaCore.Models;

public sealed class MercadoPagoPointConfigDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string TerminalId { get; set; } = string.Empty;
    public string PosExternalId { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool ObtenerMedioPagoAutomatico { get; set; }
    public bool ModoPrueba { get; set; }
    public bool MismoMedioParaQrYTarjeta { get; set; } = true;
    public string MedioPagoQr { get; set; } = string.Empty;
    public string MedioPagoTarjeta { get; set; } = string.Empty;
    public List<string> MediosPagoHabilitados { get; set; } = [];
    public List<MercadoPagoPointPuntoVentaDto> PuntosVenta { get; set; } = [];
}

public sealed class MercadoPagoPointPuntoVentaDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public List<string> TerminalesAsignadas { get; set; } = [];
    public string NombreVisible => string.IsNullOrWhiteSpace(Codigo) ? Descripcion : $"{Codigo} - {Descripcion}";
}

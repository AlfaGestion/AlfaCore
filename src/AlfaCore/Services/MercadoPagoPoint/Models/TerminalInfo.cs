namespace AlfaCore.Services.MercadoPagoPoint.Models;

public sealed class TerminalInfo
{
    public string Id { get; set; } = string.Empty;
    public string OperatingMode { get; set; } = string.Empty;
    public string Store { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

namespace AlfaCore.Services.MercadoPagoPoint;

/// <summary>Puerto de las opciones del cliente original (C:\dev\AlfaMercadoPagoPoint, .NET Framework
/// 4.8/COM para el sistema de escritorio VB6) -- se conserva el mismo shape probado, sin los campos de
/// logging a archivo (acá se usa ILogger/IAppEventService).</summary>
public sealed class MercadoPagoPointOptions
{
    public string AccessToken { get; set; } = string.Empty;
    public string TerminalId { get; set; } = string.Empty;
    public double PollingIntervalSeconds { get; set; } = 2.0d;
    public double TimeoutSeconds { get; set; } = 120.0d;
    public string PointPrintMode { get; set; } = "no_ticket";
    public string ExpirationTime { get; set; } = "PT15M";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
            throw new ArgumentException("AccessToken es obligatorio.", nameof(AccessToken));

        if (string.IsNullOrWhiteSpace(TerminalId))
            throw new ArgumentException("TerminalId es obligatorio.", nameof(TerminalId));

        if (PollingIntervalSeconds <= 0)
            throw new ArgumentException("PollingIntervalSeconds debe ser mayor a cero.", nameof(PollingIntervalSeconds));

        if (TimeoutSeconds <= 0)
            throw new ArgumentException("TimeoutSeconds debe ser mayor a cero.", nameof(TimeoutSeconds));
    }
}

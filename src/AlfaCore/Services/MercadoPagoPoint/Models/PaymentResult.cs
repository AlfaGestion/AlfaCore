namespace AlfaCore.Services.MercadoPagoPoint.Models;

public sealed class PaymentResult
{
    public ResultadoPago ResultCode { get; set; }
    public string? OrderId { get; set; }
    public string? Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Approved { get; set; }
    public string PaymentId { get; set; } = string.Empty;
    public string PaymentMethodType { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string PaymentMethodId { get; set; } = string.Empty;
    public int Installments { get; set; }
    public double ApprovedAmount { get; set; }
    public string StatusDetail { get; set; } = string.Empty;
}

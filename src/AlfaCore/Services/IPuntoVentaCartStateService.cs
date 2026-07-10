using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IPuntoVentaCartStateService
{
    List<PuntoVentaCartItemDto> CartItems { get; }
    List<PuntoVentaPaymentLineDto> PaymentLines { get; }
    string? SelectedCartItemId { get; set; }
    void Clear();
    void ClearPayments();
}

public sealed class PuntoVentaCartStateService : IPuntoVentaCartStateService
{
    public List<PuntoVentaCartItemDto> CartItems { get; } = [];
    public List<PuntoVentaPaymentLineDto> PaymentLines { get; } = [];
    public string? SelectedCartItemId { get; set; }

    public void Clear()
    {
        CartItems.Clear();
        PaymentLines.Clear();
        SelectedCartItemId = null;
    }

    public void ClearPayments() => PaymentLines.Clear();
}

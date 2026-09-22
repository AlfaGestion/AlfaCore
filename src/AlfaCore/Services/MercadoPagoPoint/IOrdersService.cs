using AlfaCore.Services.MercadoPagoPoint.Models;

namespace AlfaCore.Services.MercadoPagoPoint;

public interface IOrdersService
{
    Task<Order> CreateOrderAsync(MercadoPagoPointOptions options, decimal amount, string externalReference,
        string description, string? defaultType, string idempotencyKey, CancellationToken ct);

    Task<Order> GetOrderAsync(MercadoPagoPointOptions options, string orderId, CancellationToken ct);

    Task<Order> CancelOrderAsync(MercadoPagoPointOptions options, string orderId, CancellationToken ct);

    bool IsFinalStatus(string status);

    bool IsApprovedStatus(string status);

    bool CanAttemptCancel(string status);
}

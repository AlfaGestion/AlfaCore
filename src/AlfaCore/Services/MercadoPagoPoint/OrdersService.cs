using System.Globalization;
using System.Text.Json.Nodes;
using AlfaCore.Services.MercadoPagoPoint.Models;

namespace AlfaCore.Services.MercadoPagoPoint;

/// <summary>Puerto casi literal de Services/OrdersService.cs del proyecto original.</summary>
public sealed class OrdersService(MercadoPagoHttpClient client) : IOrdersService
{
    private static readonly string[] AllowedDefaultTypes = ["credit_card", "debit_card", "qr"];

    public async Task<Order> CreateOrderAsync(MercadoPagoPointOptions options, decimal amount, string externalReference,
        string description, string? defaultType, string idempotencyKey, CancellationToken ct)
    {
        var payload = BuildCreatePayload(options, amount, externalReference, description, defaultType);
        var response = await client.PostAsync("/v1/orders", options.AccessToken, payload, idempotencyKey, ct);
        return Order.FromJsonObject(response);
    }

    public async Task<Order> GetOrderAsync(MercadoPagoPointOptions options, string orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("orderId es obligatorio.", nameof(orderId));

        var response = await client.GetAsync("/v1/orders/" + orderId, options.AccessToken, ct);
        return Order.FromJsonObject(response);
    }

    public async Task<Order> CancelOrderAsync(MercadoPagoPointOptions options, string orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("orderId es obligatorio.", nameof(orderId));

        var response = await client.PostAsync("/v1/orders/" + orderId + "/cancel", options.AccessToken, null, null, ct);
        return Order.FromJsonObject(response);
    }

    internal static JsonObject BuildCreatePayload(MercadoPagoPointOptions options, decimal amount, string externalReference,
        string description, string? defaultType)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (amount <= 0m)
            throw new ArgumentException("El importe debe ser mayor a cero.", nameof(amount));

        if (string.IsNullOrWhiteSpace(externalReference))
            throw new ArgumentException("externalReference es obligatorio.", nameof(externalReference));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("description es obligatorio.", nameof(description));

        ValidateDefaultType(defaultType);

        var config = new JsonObject
        {
            ["point"] = new JsonObject
            {
                ["terminal_id"] = options.TerminalId,
                ["print_on_terminal"] = options.PointPrintMode ?? "no_ticket"
            }
        };

        if (!string.IsNullOrWhiteSpace(defaultType))
        {
            config["payment_method"] = new JsonObject
            {
                ["default_type"] = defaultType
            };
        }

        var payload = new JsonObject
        {
            ["type"] = "point",
            ["external_reference"] = externalReference,
            ["description"] = description,
            ["transactions"] = new JsonObject
            {
                ["payments"] = new JsonArray
                {
                    new JsonObject { ["amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture) }
                }
            },
            ["config"] = config
        };

        if (!string.IsNullOrWhiteSpace(options.ExpirationTime))
            payload["expiration_time"] = options.ExpirationTime;

        return payload;
    }

    public bool IsFinalStatus(string status)
        => string.Equals(status, "processed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "refunded", StringComparison.OrdinalIgnoreCase);

    public bool IsApprovedStatus(string status)
        => string.Equals(status, "processed", StringComparison.OrdinalIgnoreCase);

    public bool CanAttemptCancel(string status)
        => string.Equals(status, "created", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "at_terminal", StringComparison.OrdinalIgnoreCase);

    private static void ValidateDefaultType(string? defaultType)
    {
        if (string.IsNullOrWhiteSpace(defaultType))
            return;

        if (AllowedDefaultTypes.Contains(defaultType, StringComparer.Ordinal))
            return;

        throw new MercadoPagoException("default_type inválido. Valores permitidos: credit_card, debit_card, qr.");
    }
}

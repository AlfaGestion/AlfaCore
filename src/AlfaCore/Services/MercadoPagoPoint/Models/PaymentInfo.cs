using System.Globalization;
using System.Text.Json.Nodes;

namespace AlfaCore.Services.MercadoPagoPoint.Models;

/// <summary>Extrae el pago relevante de transactions.payments[] de una order -- prefiere el que ya
/// tiene status=processed, si no toma el primero. Puerto casi literal de PaymentInfo.cs original.</summary>
internal sealed class PaymentInfo
{
    public static PaymentInfo Empty { get; } = new();

    public string PaymentId { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string CardBrand { get; set; } = string.Empty;
    public int Installments { get; set; }
    public double Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusDetail { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;

    public static PaymentInfo FromOrderJson(JsonObject? order)
    {
        if (order is null)
            return Empty;

        var payment = SelectRelevantPayment(order);
        if (payment is null)
        {
            return new PaymentInfo { ExternalReference = order.GetStringOrEmpty("external_reference") };
        }

        var paymentMethod = payment["payment_method"] as JsonObject;
        var paymentMethodType = paymentMethod.GetStringOrEmpty("type");
        var cardType = paymentMethod.GetStringOrEmpty("card_type");
        if (string.IsNullOrWhiteSpace(cardType))
        {
            if (string.Equals(paymentMethodType, "credit_card", StringComparison.OrdinalIgnoreCase)
                || string.Equals(paymentMethodType, "debit_card", StringComparison.OrdinalIgnoreCase))
                cardType = paymentMethodType;
            else if (string.Equals(paymentMethodType, "qr", StringComparison.OrdinalIgnoreCase))
                cardType = string.Empty;
        }

        return new PaymentInfo
        {
            PaymentId = payment.GetStringOrEmpty("id"),
            PaymentMethod = paymentMethodType,
            CardType = cardType,
            CardBrand = paymentMethod.GetStringOrEmpty("id"),
            Installments = ReadInt(payment.SelectPath("payment_method.installments")) ?? ReadInt(payment["installments"]) ?? 0,
            Amount = ReadAmount(payment),
            Status = payment.GetStringOrEmpty("status"),
            StatusDetail = payment.GetStringOrEmpty("status_detail"),
            ExternalReference = order.GetStringOrEmpty("external_reference")
        };
    }

    private static JsonObject? SelectRelevantPayment(JsonObject order)
    {
        if (order.SelectPath("transactions.payments") is not JsonArray payments || payments.Count == 0)
            return null;

        JsonObject? first = null;
        foreach (var token in payments)
        {
            if (token is not JsonObject payment)
                continue;

            first ??= payment;

            if (string.Equals(payment.GetStringOrEmpty("status"), "processed", StringComparison.OrdinalIgnoreCase))
                return payment;
        }

        return first;
    }

    private static int? ReadInt(JsonNode? token)
    {
        if (token is not JsonValue value)
            return null;

        if (value.TryGetValue<int>(out var intValue))
            return intValue;

        if (value.TryGetValue<double>(out var doubleValue))
            return Convert.ToInt32(doubleValue, CultureInfo.InvariantCulture);

        if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    private static double ReadAmount(JsonObject payment)
        // Point puede devolver en approved_amount el importe solicitado y en
        // total_paid_amount el total efectivamente cobrado al cliente. Este
        // último incluye el recargo de tarjeta cuando corresponde.
        => ReadDouble(payment["total_paid_amount"])
           ?? ReadDouble(payment["approved_amount"])
           ?? ReadDouble(payment["transaction_amount"])
           ?? ReadDouble(payment["amount"])
           ?? 0d;

    private static double? ReadDouble(JsonNode? token)
    {
        if (token is not JsonValue value)
            return null;

        if (value.TryGetValue<double>(out var doubleValue))
            return doubleValue;

        if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }
}

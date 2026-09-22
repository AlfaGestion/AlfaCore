using System.Text.Json.Nodes;

namespace AlfaCore.Services.MercadoPagoPoint.Models;

public sealed class Order
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusDetail { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public string PaymentMethodType { get; set; } = string.Empty;
    public string PaymentMethodId { get; set; } = string.Empty;
    public int Installments { get; set; }
    public string PaymentId { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public double ApprovedAmount { get; set; }
    internal PaymentInfo? PaymentInfo { get; set; }
    public JsonObject? Raw { get; set; }

    public static Order FromJsonObject(JsonObject? value)
    {
        if (value is null)
            return new Order();

        var paymentInfo = PaymentInfo.FromOrderJson(value);

        return new Order
        {
            Id = value.GetStringOrEmpty("id"),
            Status = value.GetStringOrEmpty("status"),
            StatusDetail = value.GetStringOrNull("status_detail") ?? paymentInfo.StatusDetail,
            ExternalReference = value.GetStringOrEmpty("external_reference"),
            PaymentMethodType = paymentInfo.PaymentMethod,
            PaymentMethodId = paymentInfo.CardBrand,
            Installments = paymentInfo.Installments,
            PaymentId = paymentInfo.PaymentId,
            CardType = paymentInfo.CardType,
            ApprovedAmount = paymentInfo.Amount,
            PaymentInfo = paymentInfo,
            Raw = value
        };
    }
}

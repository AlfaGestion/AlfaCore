using System.Globalization;
using AlfaCore.Services.MercadoPagoPoint.Models;

namespace AlfaCore.Services.MercadoPagoPoint;

public enum TimeoutDecision
{
    ContinueWaiting,
    Retry,
    Cancel
}

public enum FailureDecision
{
    Retry,
    Cancel
}

/// <summary>Punto de enganche entre el state machine de polling (abajo) y quien lo consume. En el
/// proyecto original esto lo implementaba un formulario WinForms con diálogos bloqueantes; acá lo
/// implementa MercadoPagoPointPosService de forma headless (ver ese archivo) para que lo consuma
/// VentasPuntoVenta.razor sin diálogos modales de servidor.</summary>
public interface IPaymentFlowInteraction
{
    void OnOrderCreated(Order order, decimal amount);
    void OnStatusChanged(string status);
    Task ShowApprovedAsync();
    Task<FailureDecision> PromptFailureAsync(string message);
    Task<TimeoutDecision> PromptTimeoutAsync(string message);
}

/// <summary>Puerto casi literal de Services/PaymentFlowController.cs del proyecto original -- mismo
/// algoritmo de polling probado (crea la orden, espera con GetOrderAsync cada PollingIntervalSeconds
/// hasta processed/estado final/timeout, con la lógica de "no crear una orden nueva si la anterior
/// podría aprobarse sola" para evitar cobros duplicados). Solo cambia el logger.</summary>
public sealed class PaymentFlowController(
    MercadoPagoPointOptions options,
    IOrdersService ordersService,
    ILogger<PaymentFlowController> logger,
    decimal amount,
    string externalReference,
    string description)
{
    private volatile bool _cancelRequested;

    public void RequestCancel() => _cancelRequested = true;

    public bool CancelRequested => _cancelRequested;

    public async Task<PaymentResult> RunAsync(IPaymentFlowInteraction interaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        Order? previousOrder = null;
        var pendingCreateError = false;

        while (true)
        {
            if (!pendingCreateError)
            {
                if (previousOrder is not null)
                {
                    var safetyResult = await EnsureSafeBeforeNewOrderAsync(previousOrder, ct);
                    if (safetyResult is not null)
                        return safetyResult;
                }

                var idempotencyKey = Guid.NewGuid().ToString("D");
                Order createdOrder;
                try
                {
                    createdOrder = await ordersService.CreateOrderAsync(options, amount, externalReference, description, null, idempotencyKey, ct);
                }
                catch (Exception ex)
                {
                    pendingCreateError = true;
                    logger.LogError(ex, "Error al crear order para external_reference={ExternalReference}.", externalReference);
                    var createDecision = await interaction.PromptFailureAsync("No se pudo crear la orden.\n\nMotivo:\n" + ex.Message);
                    if (createDecision == FailureDecision.Cancel)
                        return CreateResult(ResultadoPago.Error, null, ex.Message, false, "error");

                    continue;
                }

                pendingCreateError = false;
                previousOrder = createdOrder;
                interaction.OnOrderCreated(createdOrder, amount);
            }
            else
            {
                pendingCreateError = false;
                continue;
            }

            var waitOutcome = await WaitForOrderAsync(previousOrder, interaction, ct);
            if (waitOutcome.Result is not null)
                return waitOutcome.Result;

            previousOrder = waitOutcome.LastKnownOrder ?? previousOrder;
            if (waitOutcome.Reason == WaitOutcomeReason.Timeout)
            {
                var timeoutDecision = await interaction.PromptTimeoutAsync("La operación está demorando más de lo esperado.");
                if (timeoutDecision == TimeoutDecision.ContinueWaiting)
                    continue;

                if (timeoutDecision == TimeoutDecision.Cancel)
                    return await CancelFromUserAsync(previousOrder, ct);

                var safeResult = await EnsureSafeBeforeNewOrderAsync(previousOrder, ct);
                if (safeResult is not null)
                    return safeResult;

                continue;
            }

            if (waitOutcome.Reason == WaitOutcomeReason.FinalNonApproved)
            {
                var retryDecision = await interaction.PromptFailureAsync("No se pudo completar el pago.\n\nMotivo:\n" + waitOutcome.LastKnownOrder?.Status);
                if (retryDecision == FailureDecision.Cancel)
                    return CreateResult(ResultadoPago.Error, waitOutcome.LastKnownOrder, "No se pudo completar el pago.", false);

                continue;
            }
        }
    }

    private async Task<WaitOutcome> WaitForOrderAsync(Order order, IPaymentFlowInteraction interaction, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        var lastStatus = string.Empty;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (_cancelRequested)
            {
                var cancelled = await CancelFromUserAsync(order, ct);
                return WaitOutcome.FromResult(cancelled, order);
            }

            var current = await ordersService.GetOrderAsync(options, order.Id, ct);
            if (!string.Equals(current.Status, lastStatus, StringComparison.OrdinalIgnoreCase))
            {
                lastStatus = current.Status ?? string.Empty;
                interaction.OnStatusChanged(lastStatus);
                logger.LogInformation(
                    "external_reference={ExternalReference} | order_id={OrderId} | terminal_id={TerminalId} | status={Status}",
                    externalReference, current.Id, MaskTerminalId(options.TerminalId), current.Status);
            }

            if (ordersService.IsApprovedStatus(current.Status ?? string.Empty))
            {
                logger.LogInformation(
                    "Pago aprobado | order_id={OrderId} | payment_id={PaymentId} | external_reference={ExternalReference} | importe={Amount} | status_detail={StatusDetail} | medio_pago={PaymentMethodType} | tipo_tarjeta={CardType} | cuotas={Installments}",
                    current.Id, current.PaymentId, externalReference, current.ApprovedAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    current.StatusDetail, current.PaymentMethodType, current.CardType, current.Installments);
                await interaction.ShowApprovedAsync();
                return WaitOutcome.FromResult(CreateResult(ResultadoPago.Aprobado, current, "Pago aprobado", true), current);
            }

            if (ordersService.IsFinalStatus(current.Status ?? string.Empty))
                return new WaitOutcome(WaitOutcomeReason.FinalNonApproved, current, null);

            if ((DateTime.UtcNow - start).TotalSeconds >= options.TimeoutSeconds)
                return new WaitOutcome(WaitOutcomeReason.Timeout, current, null);

            await Task.Delay(TimeSpan.FromSeconds(options.PollingIntervalSeconds), ct);
        }
    }

    private async Task<PaymentResult?> EnsureSafeBeforeNewOrderAsync(Order previousOrder, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(previousOrder.Id))
            return null;

        var refreshed = await ordersService.GetOrderAsync(options, previousOrder.Id, ct);
        if (ordersService.IsApprovedStatus(refreshed.Status))
            return CreateResult(ResultadoPago.Aprobado, refreshed, "Pago aprobado", true);

        if (ordersService.CanAttemptCancel(refreshed.Status))
        {
            try
            {
                var cancelled = await ordersService.CancelOrderAsync(options, refreshed.Id, ct);
                refreshed = !string.IsNullOrWhiteSpace(cancelled.Status) ? cancelled : await ordersService.GetOrderAsync(options, refreshed.Id, ct);
            }
            catch (MercadoPagoException)
            {
                refreshed = await ordersService.GetOrderAsync(options, refreshed.Id, ct);
            }
        }

        if (ordersService.IsApprovedStatus(refreshed.Status))
            return CreateResult(ResultadoPago.Aprobado, refreshed, "Pago aprobado", true);

        if (ordersService.CanAttemptCancel(refreshed.Status))
            return CreateResult(ResultadoPago.Timeout, refreshed, "La order anterior sigue activa. No es seguro crear una nueva orden.", false);

        return null;
    }

    private async Task<PaymentResult> CancelFromUserAsync(Order? currentOrder, CancellationToken ct)
    {
        var latest = currentOrder;
        if (latest is not null && !string.IsNullOrWhiteSpace(latest.Id))
        {
            try
            {
                latest = await ordersService.GetOrderAsync(options, latest.Id, ct);
                if (ordersService.IsApprovedStatus(latest.Status))
                    return CreateResult(ResultadoPago.Aprobado, latest, "Pago aprobado", true);

                if (ordersService.CanAttemptCancel(latest.Status))
                    latest = await ordersService.CancelOrderAsync(options, latest.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo cancelar la order actual.");
            }
        }

        return CreateResult(ResultadoPago.Cancelado, latest, "Operación cancelada por el usuario", false, "canceled");
    }

    private static PaymentResult CreateResult(ResultadoPago code, Order? order, string message, bool approved, string? fallbackStatus = null)
        => new()
        {
            ResultCode = code,
            OrderId = order?.Id,
            Status = order?.Status ?? fallbackStatus,
            Message = message,
            Approved = approved,
            PaymentId = approved ? order?.PaymentId ?? string.Empty : string.Empty,
            PaymentMethodType = approved ? order?.PaymentMethodType ?? string.Empty : string.Empty,
            CardType = approved ? order?.CardType ?? string.Empty : string.Empty,
            PaymentMethodId = approved ? order?.PaymentMethodId ?? string.Empty : string.Empty,
            Installments = approved ? order?.Installments ?? 0 : 0,
            ApprovedAmount = approved ? order?.ApprovedAmount ?? 0d : 0d,
            StatusDetail = approved ? order?.StatusDetail ?? string.Empty : string.Empty
        };

    private static string MaskTerminalId(string terminalId)
    {
        if (string.IsNullOrWhiteSpace(terminalId) || terminalId.Length <= 6)
            return terminalId ?? string.Empty;

        return terminalId[..6] + "..." + terminalId[^4..];
    }

    private sealed class WaitOutcome(WaitOutcomeReason reason, Order? lastKnownOrder, PaymentResult? result)
    {
        public WaitOutcomeReason Reason { get; } = reason;
        public Order? LastKnownOrder { get; } = lastKnownOrder;
        public PaymentResult? Result { get; } = result;

        public static WaitOutcome FromResult(PaymentResult result, Order order) => new(WaitOutcomeReason.ResultReady, order, result);
    }

    private enum WaitOutcomeReason
    {
        ResultReady,
        FinalNonApproved,
        Timeout
    }
}

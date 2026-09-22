using AlfaCore.Services.MercadoPagoPoint;
using AlfaCore.Services.MercadoPagoPoint.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class MercadoPagoPointPosService(
    IMercadoPagoPointConfigService configService,
    IOrdersService ordersService,
    ISessionService sessionService,
    IConfiguration configuration,
    IAppEventService appEvents,
    ILogger<PaymentFlowController> flowLogger) : IMercadoPagoPointPosService
{
    private const string ModuleName = "MercadoPagoPoint";
    private const string DescripcionOrden = "Venta Punto de Venta AlfaCore";

    private PaymentFlowController? _controladorActivo;

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<PaymentResult> CobrarAsync(decimal importe, string externalReference, Action<string> onStatusChanged, CancellationToken ct = default)
    {
        var options = await configService.ResolveOptionsAsync(ct)
            ?? throw new InvalidOperationException("Mercado Pago Point no está configurado para esta base (falta el Access Token).");

        try
        {
            options.Validate();
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        var controller = new PaymentFlowController(options, ordersService, flowLogger, importe, externalReference, DescripcionOrden);
        _controladorActivo = controller;

        var interaction = new BlazorPaymentFlowInteraction(this, onStatusChanged, ct);

        try
        {
            var result = await controller.RunAsync(interaction, ct);
            await PersistirResultadoFinalAsync(interaction.IdOrderMp, externalReference, importe, result, ct);

            await appEvents.LogAuditAsync(ModuleName, "Cobrar", "MP_POINT_ORDENES", interaction.IdOrderMp ?? externalReference,
                result.Approved ? "Cobro aprobado por Mercado Pago Point." : $"Cobro no aprobado ({result.Status}).",
                new { ExternalReference = externalReference, Importe = importe, result.Status, result.PaymentId }, ct);

            return result;
        }
        finally
        {
            _controladorActivo = null;
        }
    }

    public void SolicitarCancelacion() => _controladorActivo?.RequestCancel();

    public async Task ActualizarDesdeWebhookAsync(string idOrderMp, CancellationToken ct = default)
    {
        var options = await configService.ResolveOptionsAsync(ct);
        if (options is null)
            return;

        // Nunca se confía en el body del webhook -- siempre se vuelve a pedir el estado real a la
        // API antes de actualizar nada (mismo criterio que alfampoint/webhooks.py).
        var order = await ordersService.GetOrderAsync(options, idOrderMp, ct);
        var result = new PaymentResult
        {
            OrderId = order.Id,
            Status = order.Status,
            Approved = ordersService.IsApprovedStatus(order.Status),
            PaymentId = order.PaymentId,
            PaymentMethodType = order.PaymentMethodType,
            CardType = order.CardType
        };

        await PersistirResultadoFinalAsync(order.Id, order.ExternalReference, (decimal)order.ApprovedAmount, result, ct);
    }

    public async Task VincularComprobanteAsync(string externalReference, int idComprobante, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            UPDATE dbo.MP_POINT_ORDENES SET IdComprobante = @IdComprobante, FechaActualizacionUtc = GETUTCDATE()
            WHERE ExternalReference = @ExternalReference;
            """, cn);
        cmd.Parameters.AddWithValue("@IdComprobante", idComprobante);
        cmd.Parameters.AddWithValue("@ExternalReference", externalReference);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task PersistirOrdenCreadaAsync(string idOrderMp, string externalReference, decimal importe, string estado, CancellationToken ct)
    {
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM dbo.MP_POINT_ORDENES WHERE IdOrderMp = @IdOrderMp)
                    INSERT INTO dbo.MP_POINT_ORDENES (IdOrderMp, ExternalReference, Estado, Importe)
                    VALUES (@IdOrderMp, @ExternalReference, @Estado, @Importe);
                """, cn);
            cmd.Parameters.AddWithValue("@IdOrderMp", idOrderMp);
            cmd.Parameters.AddWithValue("@ExternalReference", externalReference);
            cmd.Parameters.AddWithValue("@Estado", estado);
            cmd.Parameters.AddWithValue("@Importe", importe);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "PersistirOrdenCreada", ex, "No se pudo registrar la orden de Mercado Pago Point.", new { IdOrderMp = idOrderMp, ExternalReference = externalReference }, ct: ct);
        }
    }

    private async Task PersistirResultadoFinalAsync(string? idOrderMp, string externalReference, decimal importe, PaymentResult result, CancellationToken ct)
    {
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("""
                IF EXISTS (SELECT 1 FROM dbo.MP_POINT_ORDENES WHERE ExternalReference = @ExternalReference)
                    UPDATE dbo.MP_POINT_ORDENES
                    SET Estado = @Estado, PaymentId = @PaymentId, MedioPago = @MedioPago, TipoTarjeta = @TipoTarjeta,
                        FechaActualizacionUtc = GETUTCDATE()
                    WHERE ExternalReference = @ExternalReference;
                ELSE
                    INSERT INTO dbo.MP_POINT_ORDENES (IdOrderMp, ExternalReference, Estado, Importe, PaymentId, MedioPago, TipoTarjeta)
                    VALUES (@IdOrderMp, @ExternalReference, @Estado, @Importe, @PaymentId, @MedioPago, @TipoTarjeta);
                """, cn);
            cmd.Parameters.AddWithValue("@IdOrderMp", (object?)idOrderMp ?? externalReference);
            cmd.Parameters.AddWithValue("@ExternalReference", externalReference);
            cmd.Parameters.AddWithValue("@Estado", result.Status ?? result.ResultCode.ToString());
            cmd.Parameters.AddWithValue("@Importe", importe);
            cmd.Parameters.AddWithValue("@PaymentId", (object?)result.PaymentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MedioPago", (object?)result.PaymentMethodType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TipoTarjeta", (object?)result.CardType ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "PersistirResultadoFinal", ex, "No se pudo actualizar el resultado del cobro con Mercado Pago Point.", new { ExternalReference = externalReference }, ct: ct);
        }
    }

    /// <summary>Adapta el state machine de polling (pensado originalmente para diálogos WinForms
    /// bloqueantes) a un consumidor headless: nunca da un cobro por perdido solo, sigue esperando ante
    /// timeout salvo que el cajero ya haya pedido cancelar explícitamente (nunca reintenta un cobro
    /// rechazado sin que el cajero lo pida de nuevo -- evita re-cobrar sin que el cajero lo decida).</summary>
    private sealed class BlazorPaymentFlowInteraction(MercadoPagoPointPosService owner, Action<string> onStatusChanged, CancellationToken ct)
        : IPaymentFlowInteraction
    {
        public string? IdOrderMp { get; private set; }

        public void OnOrderCreated(Order order, decimal amount)
        {
            IdOrderMp = order.Id;
            onStatusChanged("Esperando pago en la terminal...");
            _ = owner.PersistirOrdenCreadaAsync(order.Id, order.ExternalReference, amount, order.Status, ct);
        }

        public void OnStatusChanged(string status) => onStatusChanged(status);

        public Task ShowApprovedAsync()
        {
            onStatusChanged("Pago aprobado.");
            return Task.CompletedTask;
        }

        public Task<FailureDecision> PromptFailureAsync(string message)
        {
            onStatusChanged(message);
            return Task.FromResult(FailureDecision.Cancel);
        }

        public Task<TimeoutDecision> PromptTimeoutAsync(string message)
        {
            onStatusChanged(message);
            return Task.FromResult(TimeoutDecision.ContinueWaiting);
        }
    }
}

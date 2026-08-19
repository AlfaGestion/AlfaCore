using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Único <see cref="IPaymentProvider"/> real en v1: el pago ya fue confirmado por fuera del sistema
/// (transferencia recibida, efectivo entregado) y alguien de Alfa lo carga a mano — nace
/// directamente en <see cref="PagoEstados.Aprobado"/>, no pasa por <see cref="PagoEstados.Pendiente"/>.
/// Ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md, Fase 3.
/// </summary>
public sealed class ManualPaymentProvider : IPaymentProvider
{
    public string Codigo => "MANUAL";

    public async Task<int> RegistrarPagoAsync(
        RegistrarPagoManualRequest request,
        string registradoPor,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dbo.Pagos
                (IdCliente, IdCargo, Fecha, Importe, Moneda, Estado, MedioPago, Provider, Referencia, Observaciones, RegistradoPor, FechaAprobacion)
            OUTPUT INSERTED.Id
            VALUES
                (@IdCliente, @IdCargo, @Fecha, @Importe, @Moneda, @Estado, @MedioPago, @Provider, @Referencia, @Observaciones, @RegistradoPor, @FechaAprobacion);
            """;

        var fecha = request.Fecha ?? DateTime.UtcNow;

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            request.IdCliente,
            IdCargo = request.IdCargo,
            Fecha = fecha,
            request.Importe,
            request.Moneda,
            Estado = PagoEstados.Aprobado,
            request.MedioPago,
            Provider = Codigo,
            request.Referencia,
            request.Observaciones,
            RegistradoPor = string.IsNullOrWhiteSpace(registradoPor) ? null : registradoPor,
            FechaAprobacion = DateTime.UtcNow
        }, transaction, cancellationToken: ct)).ConfigureAwait(false);
    }
}

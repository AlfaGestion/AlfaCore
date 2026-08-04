using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaTurnoValidator(
    IConfiguration configuration,
    ISessionService sessionService) : IPuntoVentaTurnoValidator
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<ValidationResult> ValidateAperturaAsync(PuntoVentaTurnoAperturaRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();

        if (request.IdPuntoVenta <= 0)
            result.Add("id-punto-venta", "Debe seleccionar un punto de venta.");
        if (string.IsNullOrWhiteSpace(request.IdCaja))
            result.Add("id-caja", "No se pudo determinar la caja del usuario actual.");
        if (request.SaldoInicial < 0)
            result.Add("saldo-inicial", "El saldo inicial no puede ser negativo.");

        foreach (var billete in request.Billetes)
        {
            if (billete.Cantidad < 0)
                result.Add($"billete-{billete.IdDenominacion}", "La cantidad de billetes/monedas no puede ser negativa.");
        }

        if (!result.IsValid)
            return result;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var puntoVentaExiste = await cn.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.POS_PUNTOVENTA WHERE ID = @IdPuntoVenta;",
            new { request.IdPuntoVenta }, cancellationToken: ct));
        if (puntoVentaExiste == 0)
            result.Add("id-punto-venta", "El punto de venta seleccionado no existe.");

        var turnoAbierto = await cn.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.POS_TURNO WHERE IDPUNTOVENTA = @IdPuntoVenta AND IDCAJA = @IdCaja AND ESTADO = @Estado;",
            new { request.IdPuntoVenta, request.IdCaja, Estado = PuntoVentaTurnoEstadoKeys.Abierto }, cancellationToken: ct));
        if (turnoAbierto > 0)
            result.Add("turno", "Ya hay un turno abierto para esta caja.");

        return result;
    }

    public Task<ValidationResult> ValidateCierreAsync(PuntoVentaTurnoCierreRequest request, PuntoVentaTurnoDto turno, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(turno);

        var result = new ValidationResult();

        if (turno.Estado != PuntoVentaTurnoEstadoKeys.Abierto)
            result.Add("turno", "El turno ya está cerrado.");
        if (request.SaldoFinalDeclarado < 0)
            result.Add("saldo-final", "El saldo final declarado no puede ser negativo.");

        foreach (var billete in request.Billetes)
        {
            if (billete.Cantidad < 0)
                result.Add($"billete-{billete.IdDenominacion}", "La cantidad de billetes/monedas no puede ser negativa.");
        }

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateCorteXAsync(PuntoVentaTurnoCorteXRequest request, PuntoVentaTurnoDto turno, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(turno);

        var result = new ValidationResult();

        if (turno.Estado != PuntoVentaTurnoEstadoKeys.Abierto)
            result.Add("turno", "El turno ya está cerrado.");
        if (request.SaldoContado is < 0)
            result.Add("saldo-contado", "El saldo contado no puede ser negativo.");

        foreach (var billete in request.Billetes)
        {
            if (billete.Cantidad < 0)
                result.Add($"billete-{billete.IdDenominacion}", "La cantidad de billetes/monedas no puede ser negativa.");
        }

        return Task.FromResult(result);
    }
}

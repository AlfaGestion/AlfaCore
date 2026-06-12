using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CargaViajesValidator(
    IConfiguration configuration,
    ISessionService sessionService) : ICargaViajesValidator
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<ValidationResult> ValidateViajeForSaveAsync(CargaViajeSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        ValidateCommonCodeFields(request.Cliente, request.Chofer, request.Destino, request.TipoVehiculo, result);
        ValidateMoney(request.ImporteCliente, "importe-cliente", result);
        ValidateMoney(request.ImporteFletero, "importe-fletero", result);
        ValidateMoney(request.Peaje, "peaje", result);
        ValidatePercent(request.PorcentajeAdic, "porcentaje-adic", result);
        ValidatePercent(request.PorcentajeAdic1, "porcentaje-adic1", result);
        ValidatePercent(request.PorcentajeAdic2, "porcentaje-adic2", result);
        ValidatePercent(request.PorcentajeAdic3, "porcentaje-adic3", result);
        ValidatePercent(request.PorcentajeAdic4, "porcentaje-adic4", result);

        if (request.CantidadViajes <= 0)
            result.Add("cantidad-viajes", "La cantidad de viajes debe ser mayor a cero.");

        if (!string.IsNullOrWhiteSpace(request.Estado) && !CargaViajeEstadoKeys.All.Contains(request.Estado.Trim().ToUpperInvariant()))
            result.Add("estado", "El estado seleccionado no es válido.");

        if (!result.IsValid)
            return result;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        if (!await ExistsAsync(cn, "Vt_Clientes", "CODIGO", request.Cliente, ct))
            result.Add("cliente", "El cliente seleccionado no existe.");

        if (!await ExistsAsync(cn, await ResolveChoferTableAsync(cn, ct), await ResolveChoferCodeColumnAsync(cn, ct), request.Chofer, ct))
            result.Add("chofer", "El chofer seleccionado no existe.");

        if (!await ExistsAsync(cn, await ResolveDestinoTableAsync(cn, ct), await ResolveDestinoCodeColumnAsync(cn, ct), request.Destino, ct))
            result.Add("destino", "El destino seleccionado no existe.");

        if (!await ExistsAsync(cn, await ResolveTipoVehiculoTableAsync(cn, ct), "CODIGO", request.TipoVehiculo, ct))
            result.Add("tipo-vehiculo", "El tipo de vehículo seleccionado no existe.");

        return result;
    }

    public async Task<ValidationResult> ValidateTarifaForSaveAsync(CargaViajeTarifaSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(request.IdLista))
            result.Add("id-lista", "El IdLista es obligatorio.");
        if (string.IsNullOrWhiteSpace(request.Nombre))
            result.Add("nombre", "El nombre es obligatorio.");
        if (request.Importe < 0)
            result.Add("importe", "El importe no puede ser negativo.");
        ValidateCommonCodeFields(request.Cliente, request.Chofer, request.Destino, request.TipoVehiculo, result);
        ValidatePercent(request.PorcentajeAdic, "porcentaje-adic", result);
        ValidatePercent(request.PorcentajeAdic1, "porcentaje-adic1", result);
        ValidatePercent(request.PorcentajeAdic2, "porcentaje-adic2", result);
        ValidatePercent(request.PorcentajeAdic3, "porcentaje-adic3", result);
        ValidatePercent(request.PorcentajeAdic4, "porcentaje-adic4", result);
        return result;
    }

    public Task<ValidationResult> ValidateChoferForSaveAsync(CargaViajeChoferSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(request.Codigo))
            result.Add("codigo", "El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(request.Nombre))
            result.Add("nombre", "El nombre es obligatorio.");
        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateDestinoForSaveAsync(CargaViajeDestinoSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(request.Codigo))
            result.Add("codigo", "El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(request.Descripcion))
            result.Add("descripcion", "La descripción es obligatoria.");
        return Task.FromResult(result);
    }

    private static void ValidateCommonCodeFields(string cliente, string chofer, string destino, string tipoVehiculo, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(cliente))
            result.Add("cliente", "Seleccioná un cliente.");
        if (string.IsNullOrWhiteSpace(chofer))
            result.Add("chofer", "Seleccioná un chofer.");
        if (string.IsNullOrWhiteSpace(destino))
            result.Add("destino", "Seleccioná un destino.");
        if (string.IsNullOrWhiteSpace(tipoVehiculo))
            result.Add("tipo-vehiculo", "Seleccioná un tipo de vehículo.");
    }

    private static void ValidateMoney(decimal value, string fieldKey, ValidationResult result)
    {
        if (value < 0)
            result.Add(fieldKey, "El valor no puede ser negativo.");
    }

    private static void ValidatePercent(decimal value, string fieldKey, ValidationResult result)
    {
        if (value < 0 || value > 100)
            result.Add(fieldKey, "El porcentaje debe estar entre 0 y 100.");
    }

    private static async Task<bool> ExistsAsync(SqlConnection cn, string tableName, string columnName, string value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(value))
            return false;

        var sql = $"""
            SELECT COUNT(1)
            FROM dbo.{tableName}
            WHERE UPPER(LTRIM(RTRIM({columnName}))) = @Value;
            """;
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Value = value.Trim().ToUpperInvariant() }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<string> ResolveChoferTableAsync(SqlConnection cn, CancellationToken ct)
        => await ResolveExistingTableAsync(cn, ct, "TA_CHOFERES", "MA_CHOFERES");

    private static async Task<string> ResolveDestinoTableAsync(SqlConnection cn, CancellationToken ct)
        => await ResolveExistingTableAsync(cn, ct, "TA_DESTINOS", "V_TA_DESTINO");

    private static async Task<string> ResolveTipoVehiculoTableAsync(SqlConnection cn, CancellationToken ct)
        => await ResolveExistingTableAsync(cn, ct, "TA_TIPOVEHICULO");

    private static async Task<string> ResolveChoferCodeColumnAsync(SqlConnection cn, CancellationToken ct)
        => await ResolveExistingColumnAsync(cn, ct, await ResolveChoferTableAsync(cn, ct), "CODIGO", "CODIGO");

    private static async Task<string> ResolveDestinoCodeColumnAsync(SqlConnection cn, CancellationToken ct)
        => await ResolveExistingColumnAsync(cn, ct, await ResolveDestinoTableAsync(cn, ct), "CODIGO", "IDDESTINO", "IdDestino");

    private static async Task<string> ResolveExistingTableAsync(SqlConnection cn, CancellationToken ct, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var exists = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM sys.objects WHERE object_id = OBJECT_ID(@FullName);",
                new { FullName = $"dbo.{candidate}" },
                cancellationToken: ct));
            if (exists > 0)
                return candidate;
        }

        return candidates.First();
    }

    private static async Task<string> ResolveExistingColumnAsync(SqlConnection cn, CancellationToken ct, string tableName, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var exists = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(@FullName) AND UPPER(name) = UPPER(@ColumnName);",
                new { FullName = $"dbo.{tableName}", ColumnName = candidate },
                cancellationToken: ct));
            if (exists > 0)
                return candidate;
        }

        return candidates.First();
    }
}

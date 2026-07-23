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

        await using (var configCn = new SqlConnection(ConnectionString))
        {
            await configCn.OpenAsync(ct);
            var config = CargaViajesService.BuildConfiguracion(await CargaViajesService.LoadConfiguracionAsync(configCn, ct));
            var valoresAdicionales = new[]
            {
                request.PorcentajeAdic,
                request.PorcentajeAdic1,
                request.PorcentajeAdic2,
                request.PorcentajeAdic3,
                request.PorcentajeAdic4
            };
            var fieldKeys = new[] { "porcentaje-adic", "porcentaje-adic1", "porcentaje-adic2", "porcentaje-adic3", "porcentaje-adic4" };
            for (var i = 0; i < valoresAdicionales.Length; i++)
            {
                // Cada adicional general puede configurarse como Porcentaje (0-100) o
                // como Importe fijo: validar siempre en rango 0-100 rompía el guardado
                // cuando el adicional estaba configurado como Importe (ej. $35.000).
                var esPorcentaje = i >= config.EsPorcentajeAdicionales.Length || config.EsPorcentajeAdicionales[i];
                if (esPorcentaje)
                    ValidatePercent(valoresAdicionales[i], fieldKeys[i], result);
                else
                    ValidateMoney(valoresAdicionales[i], fieldKeys[i], result);
            }
        }

        ValidateTarifaAdicionalFijo(request.AdicionalFijo1Descripcion, request.AdicionalFijo1Importe, "adicional-fijo-1", result);
        ValidateTarifaAdicionalFijo(request.AdicionalFijo2Descripcion, request.AdicionalFijo2Importe, "adicional-fijo-2", result);
        ValidateTarifaAdicionalFijo(request.AdicionalFijo3Descripcion, request.AdicionalFijo3Importe, "adicional-fijo-3", result);

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

        var tarifaFletero = request.TarifaFletero;
        if (tarifaFletero)
        {
            if (string.IsNullOrWhiteSpace(request.Chofer))
                result.Add("chofer", "Debe seleccionar un chofer.");
        }
        else if (string.IsNullOrWhiteSpace(request.Cliente))
        {
            result.Add("cliente", "Debe seleccionar un cliente.");
        }

        if (string.IsNullOrWhiteSpace(request.Destino))
            result.Add("destino", "Debe seleccionar un destino.");
        if (string.IsNullOrWhiteSpace(request.TipoVehiculo))
            result.Add("tipo-vehiculo", "Debe seleccionar un tipo de vehículo.");

        if (!result.IsValid)
            return result;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        if (!tarifaFletero)
        {
            if (!await ExistsAsync(cn, "Vt_Clientes", "CODIGO", request.Cliente, ct))
                result.Add("cliente", "El cliente seleccionado no existe.");
        }
        else if (!await ExistsAsync(cn, await ResolveChoferTableAsync(cn, ct), await ResolveChoferCodeColumnAsync(cn, ct), request.Chofer, ct))
        {
            result.Add("chofer", "El chofer seleccionado no existe.");
        }

        if (!await ExistsAsync(cn, await ResolveDestinoTableAsync(cn, ct), await ResolveDestinoCodeColumnAsync(cn, ct), request.Destino, ct))
            result.Add("destino", "El destino seleccionado no existe.");

        if (!await ExistsAsync(cn, await ResolveTipoVehiculoTableAsync(cn, ct), "CODIGO", request.TipoVehiculo, ct))
            result.Add("tipo-vehiculo", "El tipo de vehículo seleccionado no existe.");

        if (!result.IsValid)
            return result;

        var idListaColumn = await ResolveExistingColumnAsync(cn, ct, "TA_TARIFA", "IDLISTA", "ID_LISTA");
        var currentIdLista = request.IdLista.Trim().ToUpperInvariant();
        var originalIdLista = string.IsNullOrWhiteSpace(request.OriginalIdLista)
            ? string.Empty
            : request.OriginalIdLista.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(originalIdLista) || !string.Equals(originalIdLista, currentIdLista, StringComparison.OrdinalIgnoreCase))
        {
            if (await ExistsAsync(cn, "TA_TARIFA", idListaColumn, currentIdLista, ct))
                result.Add("id-lista", "Ya existe una tarifa con ese código de lista.");
        }

        var hasDuplicate = await HasDuplicateTarifaAsync(cn, request, ct);
        if (hasDuplicate)
            result.Add("tarifa-duplicada", "Ya existe una tarifa para esta combinación.");

        return result;
    }

    public async Task<ValidationResult> ValidateConfiguracionForSaveAsync(CargaViajesConfigDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        ValidateCommonTextField(request.Sucursal, "sucursal", "La sucursal es obligatoria.", result);
        ValidateCommonTextField(request.Letra, "letra", "La letra es obligatoria.", result);

        for (var i = 0; i < 5; i++)
        {
            var enabled = GetArrayValue(request.AdicionalesHabilitados, i);
            var sumarFletero = GetArrayValue(request.AdicionalesSumarFletero, i);
            var nombre = GetText(request.NombresAdicionales, i);
            var porcentaje = GetDecimal(request.PorcentajesAdicionales, i);

            if (enabled)
            {
                if (string.IsNullOrWhiteSpace(nombre))
                    result.Add($"adicional-{i + 1}-nombre", $"El adicional {i + 1} habilitado debe tener nombre.");
                if (porcentaje < 0m)
                    result.Add($"adicional-{i + 1}-porcentaje", $"El porcentaje del adicional {i + 1} no puede ser negativo.");
            }

            if (!enabled && sumarFletero)
                result.Add($"adicional-{i + 1}-sumar-fletero", $"El adicional {i + 1} no puede sumar a fleteros si está deshabilitado.");
        }

        var codigoTarifaGeneral = (request.CodigoTarifaGeneral ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(codigoTarifaGeneral))
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            var idListaColumn = await ResolveExistingColumnAsync(cn, ct, "TA_TARIFA", "IDLISTA", "ID_LISTA");
            if (!await ExistsAsync(cn, "TA_TARIFA", idListaColumn, codigoTarifaGeneral, ct))
                result.Add("codigo-tarifa-general", "El código de tarifa general seleccionado no existe.");
        }

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

    private static void ValidateMoney(decimal value, string fieldKey, ValidationResult result)
    {
        if (value < 0)
            result.Add(fieldKey, "El valor no puede ser negativo.");
    }

    private static void ValidateCommonCodeFields(string cliente, string chofer, string destino, string tipoVehiculo, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(cliente))
            result.Add("cliente", "Debe seleccionar un cliente.");
        if (string.IsNullOrWhiteSpace(chofer))
            result.Add("chofer", "Debe seleccionar un chofer.");
        if (string.IsNullOrWhiteSpace(destino))
            result.Add("destino", "Debe seleccionar un destino.");
        if (string.IsNullOrWhiteSpace(tipoVehiculo))
            result.Add("tipo-vehiculo", "Debe seleccionar un tipo de vehículo.");
    }

    private static void ValidatePercent(decimal value, string fieldKey, ValidationResult result)
    {
        if (value < 0 || value > 100)
            result.Add(fieldKey, "El porcentaje debe estar entre 0 y 100.");
    }

    private static void ValidateTarifaAdicionalFijo(string? descripcion, decimal importe, string fieldKey, ValidationResult result)
    {
        var text = (descripcion ?? string.Empty).Trim();
        if (importe < 0)
            result.Add(fieldKey, "El importe no puede ser negativo.");

        if (importe > 0m && string.IsNullOrWhiteSpace(text))
            result.Add(fieldKey, "Debe indicar la descripción del adicional fijo.");
    }

    private static void ValidateCommonTextField(string? value, string fieldKey, string message, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value))
            result.Add(fieldKey, message);
    }

    private static string GetText(IReadOnlyList<string> values, int index)
        => index >= 0 && index < values.Count ? (values[index] ?? string.Empty).Trim() : string.Empty;

    private static decimal GetDecimal(IReadOnlyList<decimal> values, int index)
        => index >= 0 && index < values.Count ? values[index] : 0m;

    private static bool GetArrayValue(IReadOnlyList<bool> values, int index)
        => index >= 0 && index < values.Count && values[index];

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

    private static async Task<bool> HasDuplicateTarifaAsync(SqlConnection cn, CargaViajeTarifaSaveRequest request, CancellationToken ct)
    {
        var idListaColumn = await ResolveExistingColumnAsync(cn, ct, "TA_TARIFA", "IDLISTA", "ID_LISTA");
        var clienteColumn = await ResolveExistingColumnAsync(cn, ct, "TA_TARIFA", "IDCLIENTE", "CLIENTE");
        var choferColumn = await ResolveExistingColumnAsync(cn, ct, "TA_TARIFA", "IDCHOFER", "CHOFER");
        var destinoColumn = await ResolveExistingColumnAsync(cn, ct, "TA_TARIFA", "IDDESTINO", "DESTINO");
        var tipoVehiculoColumn = await ResolveExistingColumnAsync(cn, ct, "TA_TARIFA", "IDTIPOVEHICULO", "TIPOVEHICULO");
        var idLista = string.IsNullOrWhiteSpace(request.OriginalIdLista)
            ? request.IdLista.Trim().ToUpperInvariant()
            : request.OriginalIdLista.Trim().ToUpperInvariant();
        var cliente = (request.Cliente ?? string.Empty).Trim().ToUpperInvariant();
        var chofer = (request.Chofer ?? string.Empty).Trim().ToUpperInvariant();
        var destino = (request.Destino ?? string.Empty).Trim().ToUpperInvariant();
        var tipoVehiculo = (request.TipoVehiculo ?? string.Empty).Trim().ToUpperInvariant();

        var sql = $"""
            SELECT COUNT(1)
            FROM dbo.TA_TARIFA
            WHERE ISNULL(Activo, 1) = 1
              AND ISNULL(TarifaFletero, 0) = @TarifaFletero
              AND UPPER(LTRIM(RTRIM(ISNULL({clienteColumn}, '')))) = @Cliente
              AND UPPER(LTRIM(RTRIM(ISNULL({choferColumn}, '')))) = @Chofer
              AND UPPER(LTRIM(RTRIM(ISNULL({destinoColumn}, '')))) = @Destino
              AND UPPER(LTRIM(RTRIM(ISNULL({tipoVehiculoColumn}, '')))) = @TipoVehiculo
              AND UPPER(LTRIM(RTRIM(ISNULL({idListaColumn}, '')))) <> @IdLista;
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            TarifaFletero = request.TarifaFletero ? 1 : 0,
            Cliente = request.TarifaFletero ? string.Empty : cliente,
            Chofer = request.TarifaFletero ? chofer : string.Empty,
            Destino = destino,
            TipoVehiculo = tipoVehiculo,
            IdLista = idLista
        }, cancellationToken: ct));

        return count > 0;
    }

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



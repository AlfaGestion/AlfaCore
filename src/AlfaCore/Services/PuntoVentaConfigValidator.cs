using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaConfigValidator(
    IConfiguration configuration,
    ISessionService sessionService) : IPuntoVentaConfigValidator
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<ValidationResult> ValidatePuntoVentaForSaveAsync(PuntoVentaEntidadSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        var codigo = (request.Codigo ?? string.Empty).Trim();
        var nombre = (request.Nombre ?? string.Empty).Trim();
        var modo = (request.Modo ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(codigo))
            result.Add("codigo", "El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            result.Add("nombre", "El nombre es obligatorio.");
        if (!PuntoVentaModoKeys.All.Contains(modo))
            result.Add("modo", "El modo seleccionado no es válido.");

        if (!result.IsValid)
            return result;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var duplicado = await cn.QuerySingleAsync<int>(new CommandDefinition("""
            SELECT COUNT(1) FROM dbo.POS_PUNTOVENTA
            WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo
              AND (@Id IS NULL OR ID <> @Id);
            """,
            new { Codigo = codigo.ToUpperInvariant(), request.Id },
            cancellationToken: ct));
        if (duplicado > 0)
            result.Add("codigo", "Ya existe un punto de venta con ese código.");

        var sucursal = (request.Sucursal ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(sucursal))
        {
            var sucursalDuplicada = await cn.QuerySingleAsync<int>(new CommandDefinition("""
                SELECT COUNT(1) FROM dbo.POS_PUNTOVENTA
                WHERE UPPER(LTRIM(RTRIM(SUCURSAL))) = @Sucursal
                  AND (@Id IS NULL OR ID <> @Id);
                """,
                new { Sucursal = sucursal.ToUpperInvariant(), request.Id },
                cancellationToken: ct));
            if (sucursalDuplicada > 0)
                result.Add("sucursal", "Ya existe otro punto de venta con esa sucursal (dato único dado de alta en ARCA).");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateSectorForSaveAsync(PuntoVentaSectorSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        var codigo = (request.Codigo ?? string.Empty).Trim();
        var nombre = (request.Nombre ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(codigo))
            result.Add("codigo", "El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            result.Add("nombre", "El nombre es obligatorio.");
        if (request.IdPuntoVenta <= 0)
            result.Add("id-punto-venta", "Debe seleccionar un punto de venta.");

        if (!result.IsValid)
            return result;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var puntoVentaExiste = await cn.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.POS_PUNTOVENTA WHERE ID = @IdPuntoVenta;",
            new { request.IdPuntoVenta },
            cancellationToken: ct));
        if (puntoVentaExiste == 0)
            result.Add("id-punto-venta", "El punto de venta seleccionado no existe.");

        var duplicado = await cn.QuerySingleAsync<int>(new CommandDefinition("""
            SELECT COUNT(1) FROM dbo.POS_SECTOR
            WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo
              AND IDPUNTOVENTA = @IdPuntoVenta
              AND (@Id IS NULL OR ID <> @Id);
            """,
            new { Codigo = codigo.ToUpperInvariant(), request.IdPuntoVenta, request.Id },
            cancellationToken: ct));
        if (duplicado > 0)
            result.Add("codigo", "Ya existe un sector con ese código en este punto de venta.");

        return result;
    }

    public async Task<ValidationResult> ValidateMesaForSaveAsync(PuntoVentaMesaSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        var codigo = (request.Codigo ?? string.Empty).Trim();
        var nombre = (request.Nombre ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(codigo))
            result.Add("codigo", "El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            result.Add("nombre", "El nombre es obligatorio.");
        if (request.IdSector <= 0)
            result.Add("id-sector", "Debe seleccionar un sector.");
        if (request.Capacidad is <= 0)
            result.Add("capacidad", "La capacidad debe ser mayor a cero.");

        if (!result.IsValid)
            return result;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var sectorExiste = await cn.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.POS_SECTOR WHERE ID = @IdSector;",
            new { request.IdSector },
            cancellationToken: ct));
        if (sectorExiste == 0)
            result.Add("id-sector", "El sector seleccionado no existe.");

        var duplicado = await cn.QuerySingleAsync<int>(new CommandDefinition("""
            SELECT COUNT(1) FROM dbo.POS_MESA
            WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo
              AND IDSECTOR = @IdSector
              AND (@Id IS NULL OR ID <> @Id);
            """,
            new { Codigo = codigo.ToUpperInvariant(), request.IdSector, request.Id },
            cancellationToken: ct));
        if (duplicado > 0)
            result.Add("codigo", "Ya existe una mesa con ese código en este sector.");

        return result;
    }
}

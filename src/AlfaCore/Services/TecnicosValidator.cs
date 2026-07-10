using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class TecnicosValidator(
    IConfiguration configuration,
    ISessionService sessionService) : ITecnicosValidator
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<ValidationResult> ValidateForSaveAsync(TecnicoSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result      = new ValidationResult();
        var idOriginal  = (request.IdTecnicoOriginal ?? string.Empty).Trim();
        var idTecnico   = (request.IdTecnico ?? string.Empty).Trim();
        var nombre      = (request.Nombre ?? string.Empty).Trim();
        var cargo       = (request.Cargo ?? string.Empty).Trim();
        var domicilio   = (request.Domicilio ?? string.Empty).Trim();
        var localidad   = (request.Localidad ?? string.Empty).Trim();
        var telefono    = (request.Telefono ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(idTecnico))
            result.Add("id-tecnico", "El código del técnico es obligatorio.");
        else if (idTecnico.Length > 4)
            result.Add("id-tecnico", "El código no puede superar 4 caracteres.");
        else if (!string.IsNullOrWhiteSpace(idOriginal) &&
                 !string.Equals(idOriginal, idTecnico, StringComparison.OrdinalIgnoreCase))
            result.Add("id-tecnico", "El código del técnico no se puede modificar desde esta pantalla.");

        if (string.IsNullOrWhiteSpace(nombre))
            result.Add("nombre", "El nombre es obligatorio.");
        else if (nombre.Length > 100)
            result.Add("nombre", "El nombre no puede superar 100 caracteres.");

        if (string.IsNullOrWhiteSpace(cargo))
            result.Add("cargo", "El cargo es obligatorio.");
        else if (cargo.Length > 100)
            result.Add("cargo", "El cargo no puede superar 100 caracteres.");

        if (domicilio.Length > 100)
            result.Add("domicilio", "El domicilio no puede superar 100 caracteres.");

        if (localidad.Length > 100)
            result.Add("localidad", "La localidad no puede superar 100 caracteres.");

        if (telefono.Length > 50)
            result.Add("telefono", "El teléfono no puede superar 50 caracteres.");

        if (request.CostoHora.HasValue && request.CostoHora.Value < 0)
            result.Add("costo-hora", "El costo por hora no puede ser negativo.");

        if (!result.IsValid)
            return result;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        if (string.IsNullOrWhiteSpace(idOriginal))
        {
            if (await ExistsAsync(cn, idTecnico, ct))
                result.Add("id-tecnico", "Ya existe un técnico con ese código.");
        }
        else
        {
            if (!await ExistsAsync(cn, idOriginal, ct))
            {
                result.Add(string.Empty, "El técnico seleccionado ya no existe en la base activa.");
                return result;
            }

        }

        return result;
    }

    private static async Task<bool> ExistsAsync(SqlConnection cn, string idTecnico, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.V_TA_Tecnicos
            WHERE UPPER(LTRIM(RTRIM(IdTecnico))) = @IdTecnico;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdTecnico", idTecnico.Trim().ToUpperInvariant());
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }
}

using System.Text.RegularExpressions;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class TablasReferenciaService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : ITablasReferenciaService
{
    private static readonly Regex SafeIdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlyList<TablaReferenciaDto>> GetTablasAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT Clave, NombreVisible, CASE WHEN LTRIM(RTRIM(ISNULL(ColumnaColor, ''))) <> '' THEN 1 ELSE 0 END
            FROM dbo.ALFACORE_TABLAS_REFERENCIA
            WHERE Activo = 1
            ORDER BY Orden, NombreVisible
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        var result = new List<TablaReferenciaDto>();
        while (await rd.ReadAsync(ct))
        {
            result.Add(new TablaReferenciaDto
            {
                Clave = GetString(rd, 0),
                NombreVisible = GetString(rd, 1),
                TieneColor = rd.GetInt32(2) == 1
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<TablaReferenciaFilaDto>> GetFilasAsync(string clave, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var registro = await LoadRegistroAsync(cn, clave, ct);

        var colorSelect = registro.ColumnaColor is null ? "NULL" : $"t.[{registro.ColumnaColor}]";
        var sql = $"""
            SELECT t.[{registro.ColumnaId}], ISNULL(t.[{registro.ColumnaCodigo}], ''), ISNULL(t.[{registro.ColumnaDescripcion}], ''), {colorSelect}
            FROM dbo.[{registro.TablaFisica}] t
            ORDER BY t.[{registro.ColumnaDescripcion}], t.[{registro.ColumnaCodigo}]
            """;

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        var result = new List<TablaReferenciaFilaDto>();
        while (await rd.ReadAsync(ct))
        {
            result.Add(new TablaReferenciaFilaDto
            {
                Id = Convert.ToInt32(rd.GetValue(0)),
                Codigo = GetString(rd, 1),
                Descripcion = GetString(rd, 2),
                ColorHex = registro.ColumnaColor is null || rd.IsDBNull(3) ? null : ColorIntToHex(Convert.ToInt32(rd.GetValue(3)))
            });
        }

        return result;
    }

    public async Task GuardarFilaAsync(string clave, GuardarFilaTablaReferenciaRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var codigo = (request.Codigo ?? string.Empty).Trim();
        var descripcion = (request.Descripcion ?? string.Empty).Trim();
        if (codigo.Length == 0)
            throw new AppUserFacingException("El código es obligatorio.", "TABLAS_REF_CODIGO");
        if (descripcion.Length == 0)
            throw new AppUserFacingException("La descripción es obligatoria.", "TABLAS_REF_DESCRIPCION");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var registro = await LoadRegistroAsync(cn, clave, ct);

        var colorValue = registro.ColumnaColor is null ? (int?)null : ColorHexToInt(request.ColorHex);

        string sql;
        if (request.Id.HasValue)
        {
            var colorSet = registro.ColumnaColor is null ? string.Empty : $", t.[{registro.ColumnaColor}] = @Color";
            sql = $"""
                UPDATE t SET t.[{registro.ColumnaCodigo}] = @Codigo, t.[{registro.ColumnaDescripcion}] = @Descripcion{colorSet}
                FROM dbo.[{registro.TablaFisica}] t
                WHERE t.[{registro.ColumnaId}] = @Id;
                """;
        }
        else
        {
            var colorColumn = registro.ColumnaColor is null ? string.Empty : $", [{registro.ColumnaColor}]";
            var colorParam = registro.ColumnaColor is null ? string.Empty : ", @Color";
            sql = $"""
                INSERT INTO dbo.[{registro.TablaFisica}] ([{registro.ColumnaCodigo}], [{registro.ColumnaDescripcion}]{colorColumn})
                VALUES (@Codigo, @Descripcion{colorParam});
                """;
        }

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Codigo", codigo);
        cmd.Parameters.AddWithValue("@Descripcion", descripcion);
        if (request.Id.HasValue)
            cmd.Parameters.AddWithValue("@Id", request.Id.Value);
        if (registro.ColumnaColor is not null)
            cmd.Parameters.AddWithValue("@Color", colorValue.HasValue ? colorValue.Value : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        await appEvents.LogAuditAsync(
            "TablasReferencia",
            request.Id.HasValue ? "EditarFila" : "CrearFila",
            registro.TablaFisica,
            request.Id?.ToString() ?? codigo,
            $"{(request.Id.HasValue ? "Editada" : "Creada")} fila de {registro.NombreVisible}.",
            new { clave, codigo, descripcion },
            ct);
    }

    public async Task EliminarFilaAsync(string clave, int id, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var registro = await LoadRegistroAsync(cn, clave, ct);

        var sql = $"DELETE t FROM dbo.[{registro.TablaFisica}] t WHERE t.[{registro.ColumnaId}] = @Id;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Id", id);

        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (ex.Number is 547)
        {
            throw new AppUserFacingException(
                $"No se puede borrar: hay registros que usan este valor de {registro.NombreVisible}.",
                "TABLAS_REF_EN_USO");
        }

        await appEvents.LogAuditAsync(
            "TablasReferencia",
            "EliminarFila",
            registro.TablaFisica,
            id.ToString(),
            $"Eliminada fila de {registro.NombreVisible}.",
            new { clave, id },
            ct);
    }

    private static async Task<RegistroTablaReferencia> LoadRegistroAsync(SqlConnection cn, string clave, CancellationToken ct)
    {
        var claveNormalizada = (clave ?? string.Empty).Trim();
        if (claveNormalizada.Length == 0)
            throw new AppUserFacingException("Falta indicar qué tabla de referencia editar.", "TABLAS_REF_CLAVE");

        const string sql = """
            SELECT NombreVisible, TablaFisica, ColumnaId, ColumnaCodigo, ColumnaDescripcion, ColumnaColor
            FROM dbo.ALFACORE_TABLAS_REFERENCIA
            WHERE UPPER(LTRIM(RTRIM(Clave))) = UPPER(LTRIM(RTRIM(@Clave)))
              AND Activo = 1
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Clave", claveNormalizada);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            throw new AppUserFacingException("Esa tabla de referencia no existe o está deshabilitada.", "TABLAS_REF_NO_EXISTE");

        var registro = new RegistroTablaReferencia
        {
            NombreVisible = GetString(rd, 0),
            TablaFisica = GetString(rd, 1),
            ColumnaId = GetString(rd, 2),
            ColumnaCodigo = GetString(rd, 3),
            ColumnaDescripcion = GetString(rd, 4),
            ColumnaColor = rd.IsDBNull(5) ? null : GetString(rd, 5)
        };

        // Defensa en profundidad: aunque el registro solo lo carga un admin, nunca se interpolan
        // nombres de tabla/columna en SQL dinámico sin validar que sean identificadores seguros.
        EnsureSafeIdentifier(registro.TablaFisica, "tabla física");
        EnsureSafeIdentifier(registro.ColumnaId, "columna Id");
        EnsureSafeIdentifier(registro.ColumnaCodigo, "columna Código");
        EnsureSafeIdentifier(registro.ColumnaDescripcion, "columna Descripción");
        if (registro.ColumnaColor is not null)
            EnsureSafeIdentifier(registro.ColumnaColor, "columna Color");

        return registro;
    }

    private static void EnsureSafeIdentifier(string value, string label)
    {
        if (!SafeIdentifierRegex.IsMatch(value))
            throw new AppUserFacingException($"Configuración inválida: el nombre de {label} no es un identificador SQL seguro.", "TABLAS_REF_IDENTIFICADOR_INVALIDO");
    }

    /// <summary>
    /// dbo.TA_CLASIFICACIONES.Color y tablas similares del sistema legado guardan el color como
    /// un long de VB6 (&amp;H00BBGGRR, canales en orden inverso al de un color web) — no es un
    /// RGB directo.
    /// </summary>
    private static string ColorIntToHex(int value)
    {
        var r = value & 0xFF;
        var g = (value >> 8) & 0xFF;
        var b = (value >> 16) & 0xFF;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static int? ColorHexToInt(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        var normalized = hex.Trim().TrimStart('#');
        if (normalized.Length != 6 || !int.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
            return null;

        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        return r | (g << 8) | (b << 16);
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private sealed class RegistroTablaReferencia
    {
        public string NombreVisible { get; set; } = string.Empty;
        public string TablaFisica { get; set; } = string.Empty;
        public string ColumnaId { get; set; } = string.Empty;
        public string ColumnaCodigo { get; set; } = string.Empty;
        public string ColumnaDescripcion { get; set; } = string.Empty;
        public string? ColumnaColor { get; set; }
    }
}

using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed record AyudaConversacionResumen(Guid IdConversacion, string Titulo, DateTime Fecha, int Preguntas);
public sealed record AyudaTurnoGuardado(string Pregunta, AyudaRespuesta Respuesta);

/// <summary>
/// Historial de la ayuda con IA (2026-11-26): cada pregunta y respuesta queda guardada por usuario y
/// conversación (dbo.AYUDA_IA_MENSAJES) para retomarla. Las más viejas que AYUDA_IA_HISTORIAL_DIAS
/// (TA_CONFIGURACION, 60 por defecto) se borran al guardar. Si la tabla todavía no existe (base sin
/// actualizar), la ayuda funciona igual, sin historial.
/// </summary>
public interface IAyudaHistorialService
{
    Task GuardarAsync(Guid idConversacion, string tema, string pregunta, AyudaRespuesta respuesta, CancellationToken ct = default);
    Task<IReadOnlyList<AyudaConversacionResumen>> ListarAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AyudaTurnoGuardado>> CargarAsync(Guid idConversacion, CancellationToken ct = default);
    Task BorrarAsync(Guid idConversacion, CancellationToken ct = default);
    /// <summary>Cuántos días se guardan las consultas (AYUDA_IA_HISTORIAL_DIAS).</summary>
    Task<int> DiasAsync(CancellationToken ct = default);
}

public sealed class AyudaHistorialService(ISessionService sessionService, IConfiguration configuration, IAppModeService appMode,
    IAppUserSessionService users, IAppEventService eventos) : IAyudaHistorialService
{
    internal const string ClaveDias = "AYUDA_IA_HISTORIAL_DIAS";
    internal const int DiasPorDefecto = 60;
    private const string SiExisteTabla = "IF OBJECT_ID(N'dbo.AYUDA_IA_MENSAJES', N'U') IS NOT NULL ";

    private string ConnectionString => TenantConnectionGuard.Resolve(sessionService, configuration, appMode, "Ayuda");
    private string Usuario => (users.GetCurrentUserName() ?? string.Empty).Trim();

    public async Task GuardarAsync(Guid idConversacion, string tema, string pregunta, AyudaRespuesta respuesta, CancellationToken ct = default)
    {
        if (Usuario.Length == 0)
            return;
        var fuentes = JsonSerializer.Serialize(respuesta.Fuentes.Select(f => new { f.Tema, f.Articulo, f.Titulo }));
        const string sql = SiExisteTabla + """
            BEGIN
                INSERT INTO dbo.AYUDA_IA_MENSAJES (IdConversacion, Usuario, Tema, Pregunta, Respuesta, FuentesJson)
                VALUES (@IdConversacion, @Usuario, @Tema, @Pregunta, @Respuesta, @Fuentes);

                DECLARE @Dias int = @DiasPorDefecto;
                IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
                    SELECT TOP (1) @Dias = CASE WHEN ISNUMERIC(VALOR) = 1 AND CAST(VALOR AS int) > 0 THEN CAST(VALOR AS int) ELSE @DiasPorDefecto END
                    FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                DELETE FROM dbo.AYUDA_IA_MENSAJES WHERE FechaHora < DATEADD(day, -@Dias, GETDATE());
            END
            """;
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                IdConversacion = idConversacion, Usuario, Tema = Recortar(tema, 60), Pregunta = Recortar(pregunta, 2000),
                Respuesta = respuesta.Texto, Fuentes = fuentes, DiasPorDefecto, Clave = ClaveDias
            }, cancellationToken: ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La respuesta ya se mostró: si no se puede guardar, sólo queda registrado el error.
            await eventos.LogErrorAsync("Ayuda", "HistorialGuardar", ex, "No se pudo guardar la consulta en el historial de la ayuda.");
        }
    }

    public async Task<IReadOnlyList<AyudaConversacionResumen>> ListarAsync(CancellationToken ct = default)
    {
        if (Usuario.Length == 0)
            return [];
        const string sql = """
            SELECT TOP (40) m.IdConversacion,
                   (SELECT TOP (1) p.Pregunta FROM dbo.AYUDA_IA_MENSAJES p
                    WHERE p.IdConversacion = m.IdConversacion ORDER BY p.IdMensaje) AS Titulo,
                   MAX(m.FechaHora) AS Fecha, COUNT(1) AS Preguntas
            FROM dbo.AYUDA_IA_MENSAJES m
            WHERE m.Usuario = @Usuario
            GROUP BY m.IdConversacion
            ORDER BY MAX(m.FechaHora) DESC;
            """;
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            if (!await TablaExisteAsync(cn, ct))
                return [];
            var filas = await cn.QueryAsync<(Guid IdConversacion, string Titulo, DateTime Fecha, int Preguntas)>(new CommandDefinition(sql, new { Usuario }, cancellationToken: ct));
            return filas.Select(f => new AyudaConversacionResumen(f.IdConversacion, f.Titulo ?? string.Empty, f.Fecha, f.Preguntas)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await eventos.LogErrorAsync("Ayuda", "HistorialListar", ex, "No se pudo leer el historial de la ayuda.");
            return [];
        }
    }

    public async Task<IReadOnlyList<AyudaTurnoGuardado>> CargarAsync(Guid idConversacion, CancellationToken ct = default)
    {
        if (Usuario.Length == 0)
            return [];
        const string sql = """
            SELECT Pregunta, Respuesta, FuentesJson
            FROM dbo.AYUDA_IA_MENSAJES
            WHERE IdConversacion = @IdConversacion AND Usuario = @Usuario
            ORDER BY IdMensaje;
            """;
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            if (!await TablaExisteAsync(cn, ct))
                return [];
            var filas = await cn.QueryAsync<(string Pregunta, string Respuesta, string? FuentesJson)>(new CommandDefinition(sql, new { IdConversacion = idConversacion, Usuario }, cancellationToken: ct));
            return filas.Select(f => new AyudaTurnoGuardado(f.Pregunta, new AyudaRespuesta(f.Respuesta, LeerFuentes(f.FuentesJson)))).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await eventos.LogErrorAsync("Ayuda", "HistorialCargar", ex, "No se pudo abrir la conversación del historial de la ayuda.");
            return [];
        }
    }

    public async Task BorrarAsync(Guid idConversacion, CancellationToken ct = default)
    {
        if (Usuario.Length == 0)
            return;
        const string sql = SiExisteTabla + "DELETE FROM dbo.AYUDA_IA_MENSAJES WHERE IdConversacion = @IdConversacion AND Usuario = @Usuario;";
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition(sql, new { IdConversacion = idConversacion, Usuario }, cancellationToken: ct));
    }

    public async Task<int> DiasAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP (1) VALOR FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """;
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            var valor = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { Clave = ClaveDias }, cancellationToken: ct));
            return int.TryParse(valor?.Trim(), out var dias) && dias > 0 ? dias : DiasPorDefecto;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DiasPorDefecto;
        }
    }

    /// <summary>Fuentes guardadas: sólo las que siguen existiendo en los manuales (se vuelven a leer de ahí).</summary>
    internal static IReadOnlyList<AyudaFuente> LeerFuentes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var fuentes = new List<AyudaFuente>();
            foreach (var f in doc.RootElement.EnumerateArray())
            {
                var tema = f.GetProperty("Tema").GetString() ?? string.Empty;
                var articulo = f.GetProperty("Articulo").GetString() ?? string.Empty;
                if (!AyudaManuales.Temas.Any(t => t.Clave == tema))
                    continue;
                var manual = AyudaManuales.Obtener(tema);
                if (manual.Articulos.FirstOrDefault(a => a.Id == articulo) is { } a)
                    fuentes.Add(new AyudaFuente(tema, a.Id, manual.Tema.Nombre + " · " + a.Titulo, a.Contenido));
            }
            return fuentes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task<bool> TablaExisteAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.AYUDA_IA_MENSAJES', N'U') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 1;

    private static string Recortar(string? s, int max) => (s ?? string.Empty).Length > max ? s![..max] : s ?? string.Empty;
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IConversacionAsistenteCorreccionesService
{
    /// <summary>Propone una corrección; si quien la propone es administrador de Conversaciones queda aprobada.</summary>
    Task<AsistenteCorreccionDto> ProponerAsync(AsistenteCorreccionPropuesta propuesta, CancellationToken ct = default);

    Task<IReadOnlyList<AsistenteCorreccionDto>> ListarAsync(string? estado, CancellationToken ct = default);
    Task<int> ContarPendientesAsync(CancellationToken ct = default);

    /// <summary>Aprueba (con los textos posiblemente editados por el administrador).</summary>
    Task AprobarAsync(AsistenteCorreccionDto correccion, string usuario, CancellationToken ct = default);
    Task RechazarAsync(int idCorreccion, string usuario, CancellationToken ct = default);
    Task EliminarAsync(int idCorreccion, string usuario, CancellationToken ct = default);

    /// <summary>Correcciones aprobadas parecidas a la consulta, de la más parecida a la menos.</summary>
    Task<IReadOnlyList<AsistenteCorreccionCoincidencia>> BuscarAplicablesAsync(string consulta, int maximo = 3, CancellationToken ct = default);
    Task RegistrarUsoAsync(int idCorreccion, CancellationToken ct = default);

    /// <summary>👍 Marca (o desmarca, si ya estaba) una respuesta del asistente como correcta. Devuelve si quedó marcada.</summary>
    Task<bool> AlternarRespuestaBuenaAsync(AsistenteCorreccionPropuesta propuesta, CancellationToken ct = default);

    /// <summary>Respuestas del asistente de la conversación ya marcadas como buenas o corregidas: IdMensaje → acción.</summary>
    Task<IReadOnlyDictionary<long, string>> GetMensajesMarcadosAsync(long idConversacion, CancellationToken ct = default);
}

/// <summary>
/// Lo que el equipo de cada cliente le enseña a su asistente IA (2026-10-15). Las correcciones
/// aprobadas se buscan por parecido de palabras con la consulta nueva (sin costo de IA) y el
/// asistente las sigue con prioridad (ver ConversacionesService.EjecutarRespuestaBotAsync).
/// </summary>
public sealed class ConversacionAsistenteCorreccionesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IConversacionesAuthorizationService authorization,
    IAppEventService appEvents) : IConversacionAsistenteCorreccionesService
{
    /// <summary>Desde este parecido la corrección se usa como guía para el asistente.</summary>
    public const double ParecidoMinimo = 0.5;

    /// <summary>Desde este parecido (casi la misma pregunta) se responde directo con el texto corregido.</summary>
    public const double ParecidoDirecto = 0.8;

    private static readonly HashSet<string> PalabrasVacias = new(StringComparer.Ordinal)
    {
        "que", "como", "cual", "cuales", "para", "por", "con", "sin", "una", "uno", "unos", "unas", "los", "las",
        "del", "the", "este", "esta", "esto", "eso", "esa", "ese", "hay", "tengo", "tenes", "tiene", "tienen",
        "puedo", "podes", "puede", "pueden", "quiero", "queria", "necesito", "hola", "buen", "buenas", "buenos",
        "dia", "dias", "tardes", "noches", "gracias", "favor", "porfa", "algo", "mas", "muy", "pero", "cuando",
        "donde", "nos", "les", "mis", "sus", "son", "era", "ser", "esta", "estan", "estoy", "sobre", "me", "mi",
        "te", "se", "lo", "la", "le", "de", "el", "en", "y", "o", "a", "es", "si", "no", "al", "un", "ya"
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<AsistenteCorreccionDto> ProponerAsync(AsistenteCorreccionPropuesta propuesta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(propuesta);
        var pregunta = Recortar(propuesta.PreguntaCliente, 1000);
        var accion = AsistenteCorreccionAcciones.Normalizar(propuesta.Accion);
        if (accion == AsistenteCorreccionAcciones.Buena)
            throw new InvalidOperationException("Para marcar una respuesta como correcta usá el 👍.");
        var correcta = (propuesta.RespuestaCorrecta ?? string.Empty).Trim();
        if (pregunta.Length == 0)
            throw new InvalidOperationException("Falta la pregunta del cliente.");
        if (accion == AsistenteCorreccionAcciones.Responder && correcta.Length == 0)
            throw new InvalidOperationException("Escribí qué tendría que haber respondido el asistente.");

        var esAdmin = await authorization.CanManageAsync(ct);
        var estado = esAdmin ? AsistenteCorreccionEstados.Aprobada : AsistenteCorreccionEstados.Pendiente;
        var usuario = Recortar(propuesta.Usuario, 50);

        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES', N'U') IS NULL
            BEGIN
                SELECT CAST(-1 AS int);
                RETURN;
            END;
            INSERT INTO dbo.CONV_ASISTENTE_CORRECCIONES
                (IdConversacion, IdMensaje, PreguntaCliente, RespuestaAsistente, Accion, RespuestaCorrecta,
                 Estado, UsuarioPropone, FechaPropuesta, UsuarioRevisa, FechaRevision)
            VALUES
                (@IdConversacion, @IdMensaje, @Pregunta, @RespuestaAsistente, @Accion, @Correcta,
                 @Estado, @Usuario, GETDATE(),
                 CASE WHEN @Estado = N'APROBADA' THEN @Usuario END,
                 CASE WHEN @Estado = N'APROBADA' THEN GETDATE() END);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", (object?)propuesta.IdConversacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdMensaje", (object?)propuesta.IdMensaje ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Pregunta", pregunta);
        cmd.Parameters.AddWithValue("@RespuestaAsistente", (object?)NullSiVacio(propuesta.RespuestaAsistente) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Accion", accion);
        cmd.Parameters.AddWithValue("@Correcta", (object?)NullSiVacio(correcta) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@Usuario", (object?)NullSiVacio(usuario) ?? DBNull.Value);
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (id <= 0)
            throw new InvalidOperationException("Esta base todavía no tiene habilitadas las correcciones del asistente. Probá de nuevo en unos minutos.");

        await appEvents.LogAuditAsync("Conversaciones", "AsistenteCorreccionPropuesta", "CONV_ASISTENTE_CORRECCIONES",
            id.ToString(CultureInfo.InvariantCulture),
            esAdmin ? "Corrección del asistente cargada y aprobada." : "Corrección del asistente propuesta (pendiente de aprobación).",
            new { propuesta.IdConversacion, propuesta.IdMensaje, Accion = accion, Estado = estado }, ct);

        return new AsistenteCorreccionDto
        {
            IdCorreccion = id,
            IdConversacion = propuesta.IdConversacion,
            IdMensaje = propuesta.IdMensaje,
            PreguntaCliente = pregunta,
            RespuestaAsistente = propuesta.RespuestaAsistente ?? string.Empty,
            Accion = accion,
            RespuestaCorrecta = correcta,
            Estado = estado,
            UsuarioPropone = usuario,
            FechaPropuesta = DateTime.Now
        };
    }

    public async Task<IReadOnlyList<AsistenteCorreccionDto>> ListarAsync(string? estado, CancellationToken ct = default)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES', N'U') IS NULL
                RETURN;
            SELECT IdCorreccion, IdConversacion, IdMensaje, PreguntaCliente, ISNULL(RespuestaAsistente, N''), Accion,
                   ISNULL(RespuestaCorrecta, N''), Estado, ISNULL(UsuarioPropone, N''), FechaPropuesta,
                   ISNULL(UsuarioRevisa, N''), FechaRevision, VecesUsada, UltimoUso
            FROM dbo.CONV_ASISTENTE_CORRECCIONES
            WHERE Baja = 0 AND (@Estado IS NULL OR Estado = @Estado)
            ORDER BY CASE WHEN Estado = N'PENDIENTE' THEN 0 ELSE 1 END, FechaPropuesta DESC;
            """;
        var items = new List<AsistenteCorreccionDto>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Estado", (object?)NullSiVacio(estado?.ToUpperInvariant()) ?? DBNull.Value);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new AsistenteCorreccionDto
            {
                IdCorreccion = rd.GetInt32(0),
                IdConversacion = rd.IsDBNull(1) ? null : rd.GetInt64(1),
                IdMensaje = rd.IsDBNull(2) ? null : rd.GetInt64(2),
                PreguntaCliente = rd.GetString(3),
                RespuestaAsistente = rd.GetString(4),
                Accion = rd.GetString(5),
                RespuestaCorrecta = rd.GetString(6),
                Estado = rd.GetString(7),
                UsuarioPropone = rd.GetString(8),
                FechaPropuesta = rd.GetDateTime(9),
                UsuarioRevisa = rd.GetString(10),
                FechaRevision = rd.IsDBNull(11) ? null : rd.GetDateTime(11),
                VecesUsada = rd.GetInt32(12),
                UltimoUso = rd.IsDBNull(13) ? null : rd.GetDateTime(13)
            });
        }

        return items;
    }

    public async Task<int> ContarPendientesAsync(CancellationToken ct = default)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES', N'U') IS NULL
                SELECT 0;
            ELSE
                SELECT COUNT(1) FROM dbo.CONV_ASISTENTE_CORRECCIONES WHERE Baja = 0 AND Estado = N'PENDIENTE';
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    public async Task AprobarAsync(AsistenteCorreccionDto correccion, string usuario, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(correccion);
        await authorization.EnsureCanManageAsync(ct);
        var accion = AsistenteCorreccionAcciones.Normalizar(correccion.Accion);
        var pregunta = Recortar(correccion.PreguntaCliente, 1000);
        var correcta = (correccion.RespuestaCorrecta ?? string.Empty).Trim();
        if (pregunta.Length == 0)
            throw new InvalidOperationException("Falta la pregunta del cliente.");
        if (accion == AsistenteCorreccionAcciones.Responder && correcta.Length == 0)
            throw new InvalidOperationException("Escribí qué tendría que haber respondido el asistente.");

        await EjecutarAsync("""
            UPDATE dbo.CONV_ASISTENTE_CORRECCIONES
            SET PreguntaCliente = @Pregunta, Accion = @Accion, RespuestaCorrecta = @Correcta,
                Estado = N'APROBADA', UsuarioRevisa = @Usuario, FechaRevision = GETDATE()
            WHERE IdCorreccion = @Id AND Baja = 0;
            """, cmd =>
        {
            cmd.Parameters.AddWithValue("@Id", correccion.IdCorreccion);
            cmd.Parameters.AddWithValue("@Pregunta", pregunta);
            cmd.Parameters.AddWithValue("@Accion", accion);
            cmd.Parameters.AddWithValue("@Correcta", (object?)NullSiVacio(correcta) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Usuario", (object?)NullSiVacio(Recortar(usuario, 50)) ?? DBNull.Value);
        }, ct);
        await appEvents.LogAuditAsync("Conversaciones", "AsistenteCorreccionAprobada", "CONV_ASISTENTE_CORRECCIONES",
            correccion.IdCorreccion.ToString(CultureInfo.InvariantCulture), "Corrección del asistente aprobada.", new { Accion = accion }, ct);
    }

    public async Task RechazarAsync(int idCorreccion, string usuario, CancellationToken ct = default)
    {
        await authorization.EnsureCanManageAsync(ct);
        await EjecutarAsync("""
            UPDATE dbo.CONV_ASISTENTE_CORRECCIONES
            SET Estado = N'RECHAZADA', UsuarioRevisa = @Usuario, FechaRevision = GETDATE()
            WHERE IdCorreccion = @Id AND Baja = 0;
            """, cmd =>
        {
            cmd.Parameters.AddWithValue("@Id", idCorreccion);
            cmd.Parameters.AddWithValue("@Usuario", (object?)NullSiVacio(Recortar(usuario, 50)) ?? DBNull.Value);
        }, ct);
        await appEvents.LogAuditAsync("Conversaciones", "AsistenteCorreccionRechazada", "CONV_ASISTENTE_CORRECCIONES",
            idCorreccion.ToString(CultureInfo.InvariantCulture), "Corrección del asistente rechazada.", null, ct);
    }

    public async Task EliminarAsync(int idCorreccion, string usuario, CancellationToken ct = default)
    {
        await authorization.EnsureCanManageAsync(ct);
        await EjecutarAsync("UPDATE dbo.CONV_ASISTENTE_CORRECCIONES SET Baja = 1 WHERE IdCorreccion = @Id;",
            cmd => cmd.Parameters.AddWithValue("@Id", idCorreccion), ct);
        await appEvents.LogAuditAsync("Conversaciones", "AsistenteCorreccionEliminada", "CONV_ASISTENTE_CORRECCIONES",
            idCorreccion.ToString(CultureInfo.InvariantCulture), "Corrección del asistente eliminada.", new { usuario }, ct);
    }

    public async Task<IReadOnlyList<AsistenteCorreccionCoincidencia>> BuscarAplicablesAsync(string consulta, int maximo = 3, CancellationToken ct = default)
    {
        var palabrasConsulta = PalabrasClave(consulta);
        if (palabrasConsulta.Count == 0)
            return [];

        var aprobadas = await ListarAsync(AsistenteCorreccionEstados.Aprobada, ct);
        return aprobadas
            .Select(c => new AsistenteCorreccionCoincidencia(c, Parecido(palabrasConsulta, PalabrasClave(c.PreguntaCliente))))
            .Where(c => c.Parecido >= ParecidoMinimo)
            // Ante dos correcciones igual de parecidas gana la más nueva (la última palabra del equipo).
            .OrderByDescending(c => c.Parecido)
            .ThenByDescending(c => c.Correccion.FechaRevision ?? c.Correccion.FechaPropuesta)
            .Take(Math.Max(1, maximo))
            .ToList();
    }

    public async Task<bool> AlternarRespuestaBuenaAsync(AsistenteCorreccionPropuesta propuesta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(propuesta);
        var pregunta = Recortar(propuesta.PreguntaCliente, 1000);
        var respuesta = (propuesta.RespuestaAsistente ?? string.Empty).Trim();
        if (propuesta.IdMensaje is null || respuesta.Length == 0)
            throw new InvalidOperationException("No se encontró la respuesta del asistente.");

        // Ya marcada -> se desmarca (baja). Si no, se marca aprobada: es sólo un ejemplo, no necesita revisión.
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES', N'U') IS NULL
            BEGIN
                SELECT CAST(-1 AS int);
                RETURN;
            END;
            DECLARE @Existente int = (
                SELECT TOP (1) IdCorreccion FROM dbo.CONV_ASISTENTE_CORRECCIONES
                WHERE IdMensaje = @IdMensaje AND Accion = N'BUENA' AND Baja = 0);
            IF @Existente IS NOT NULL
            BEGIN
                UPDATE dbo.CONV_ASISTENTE_CORRECCIONES SET Baja = 1 WHERE IdCorreccion = @Existente;
                SELECT CAST(0 AS int);
                RETURN;
            END;
            INSERT INTO dbo.CONV_ASISTENTE_CORRECCIONES
                (IdConversacion, IdMensaje, PreguntaCliente, RespuestaAsistente, Accion, RespuestaCorrecta,
                 Estado, UsuarioPropone, FechaPropuesta, UsuarioRevisa, FechaRevision)
            VALUES
                (@IdConversacion, @IdMensaje, @Pregunta, @Respuesta, N'BUENA', @Respuesta,
                 N'APROBADA', @Usuario, GETDATE(), @Usuario, GETDATE());
            SELECT CAST(1 AS int);
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", (object?)propuesta.IdConversacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdMensaje", propuesta.IdMensaje.Value);
        cmd.Parameters.AddWithValue("@Pregunta", pregunta.Length == 0 ? "(sin texto)" : pregunta);
        cmd.Parameters.AddWithValue("@Respuesta", respuesta);
        cmd.Parameters.AddWithValue("@Usuario", (object?)NullSiVacio(Recortar(propuesta.Usuario, 50)) ?? DBNull.Value);
        var resultado = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (resultado < 0)
            throw new InvalidOperationException("Esta base todavía no tiene habilitadas las correcciones del asistente. Probá de nuevo en unos minutos.");

        await appEvents.LogAuditAsync("Conversaciones", resultado == 1 ? "AsistenteRespuestaBuena" : "AsistenteRespuestaBuenaQuitada",
            "CONV_MENSAJES", propuesta.IdMensaje.Value.ToString(CultureInfo.InvariantCulture),
            resultado == 1 ? "Respuesta del asistente marcada como correcta." : "Se quitó la marca de respuesta correcta.",
            new { propuesta.IdConversacion }, ct);
        return resultado == 1;
    }

    public async Task<IReadOnlyDictionary<long, string>> GetMensajesMarcadosAsync(long idConversacion, CancellationToken ct = default)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES', N'U') IS NULL
                RETURN;
            SELECT IdMensaje, Accion
            FROM dbo.CONV_ASISTENTE_CORRECCIONES
            WHERE IdConversacion = @Id AND IdMensaje IS NOT NULL AND Baja = 0 AND Estado <> N'RECHAZADA'
            ORDER BY IdCorreccion;
            """;
        var marcas = new Dictionary<long, string>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Id", idConversacion);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            marcas[rd.GetInt64(0)] = rd.GetString(1);
        return marcas;
    }

    public Task RegistrarUsoAsync(int idCorreccion, CancellationToken ct = default)
        => EjecutarAsync("UPDATE dbo.CONV_ASISTENTE_CORRECCIONES SET VecesUsada = VecesUsada + 1, UltimoUso = GETDATE() WHERE IdCorreccion = @Id;",
            cmd => cmd.Parameters.AddWithValue("@Id", idCorreccion), ct);

    /// <summary>Palabras con contenido (sin tildes, sin palabras vacías, de 3 letras o más; plurales simples unificados).</summary>
    internal static HashSet<string> PalabrasClave(string? texto)
    {
        var normalizado = QuitarTildes(texto ?? string.Empty).ToLowerInvariant();
        return Regex.Matches(normalizado, "[a-z0-9]+")
            .Select(m => m.Value)
            .Where(p => p.Length >= 3 && !PalabrasVacias.Contains(p))
            .Select(Raiz)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Unifica plurales y la 3ª persona del plural ("facturas"→"factura", "imprimen"→"imprime").</summary>
    private static string Raiz(string palabra)
    {
        if (palabra.Length > 4 && palabra.EndsWith("es", StringComparison.Ordinal))
            return palabra[..^2];
        if (palabra.Length > 3 && palabra.EndsWith('s'))
            return palabra[..^1];
        if (palabra.Length > 5 && (palabra.EndsWith("en", StringComparison.Ordinal) || palabra.EndsWith("an", StringComparison.Ordinal)))
            return palabra[..^1];
        return palabra;
    }

    /// <summary>
    /// Qué tanto se parecen dos consultas (0 a 1): el mayor entre la proporción de palabras en común
    /// sobre la pregunta enseñada y sobre el total (así "no me imprime la factura" encuentra
    /// "no imprime factura" aunque una tenga palabras de más).
    /// </summary>
    internal static double Parecido(IReadOnlySet<string> consulta, IReadOnlySet<string> enseñada)
    {
        if (consulta.Count == 0 || enseñada.Count == 0)
            return 0;
        var comunes = consulta.Count(enseñada.Contains);
        var union = consulta.Count + enseñada.Count - comunes;
        var sobreEnseñada = (double)comunes / enseñada.Count;
        var jaccard = (double)comunes / union;
        // Cubrir toda la pregunta enseñada cuenta mucho, pero una consulta mucho más larga baja el parecido.
        return Math.Max(jaccard, sobreEnseñada * Math.Min(1.0, (double)enseñada.Count / consulta.Count * 1.5));
    }

    private async Task EjecutarAsync(string sql, Action<SqlCommand> parametros, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "IF OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES', N'U') IS NULL RETURN;\n" + sql, cn);
        parametros(cmd);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string QuitarTildes(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);
        foreach (var c in normalizado)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Recortar(string? texto, int max)
    {
        var t = (texto ?? string.Empty).Trim();
        return t.Length <= max ? t : t[..max];
    }

    private static string? NullSiVacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}

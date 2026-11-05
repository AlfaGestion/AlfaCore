using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class BriefService(IConfiguration configuration) : IBriefService
{
    internal const int MaxItems = 50;
    internal const int MaxLargoMensaje = 300;
    private const int CommandTimeoutSeconds = 20;

    // Argentina no tiene horario de verano: el offset fijo evita depender de la zona de Windows.
    private static readonly TimeSpan ArgentinaOffset = TimeSpan.FromHours(-3);

    private string Token => configuration["Brief:Token"]?.Trim() ?? string.Empty;
    private string ConnectionString => configuration["Brief:ConnectionString"]?.Trim() ?? string.Empty;

    public bool EstaHabilitado => Token.Length > 0 && ConnectionString.Length > 0;

    public bool TokenValido(string? presentado)
    {
        var esperado = Token;
        if (esperado.Length == 0 || string.IsNullOrEmpty(presentado))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presentado)),
            SHA256.HashData(Encoding.UTF8.GetBytes(esperado)));
    }

    public async Task<BriefSoportePendientesResponse> GetSoportePendientesAsync(CancellationToken ct = default)
    {
        // Compatibilidad SQL 100: TOP en vez de OFFSET/FETCH. Las notas internas no cuentan como "escribió último".
        const string sql = """
            SELECT TOP (@Max)
                c.IdConversacion AS Id,
                ISNULL(NULLIF(LTRIM(RTRIM(cli.RAZON_SOCIAL)), ''), ISNULL(c.NombreVisible, '')) AS Cliente,
                ISNULL(NULLIF(LTRIM(RTRIM(ct.Nombre_y_Apellido)), ''), ISNULL(c.NombreVisible, '')) AS Contacto,
                ISNULL(c.Canal, '') AS Canal,
                ISNULL(tk.Titulo, ISNULL(c.ResumenUltimoMensaje, '')) AS Asunto,
                ISNULL(c.FechaHoraPrimerMensaje, c.FechaHora_Grabacion) AS FechaApertura,
                ISNULL(ult.FechaHora, c.FechaHoraUltimoMensaje) AS FechaUltimoMensaje,
                CASE WHEN ult.Direction = N'SALIENTE' THEN N'nosotros' ELSE N'cliente' END AS UltimoEscribio,
                ISNULL(tec.Nombre, '') AS Tecnico,
                ISNULL(est.Descripcion, c.CodigoEstado) AS Estado,
                CASE WHEN ISNULL(ult.Direction, N'ENTRANTE') = N'ENTRANTE'
                     THEN CONVERT(decimal(10, 1), DATEDIFF(MINUTE, ISNULL(ult.FechaHora, c.FechaHoraUltimoMensaje), GETDATE()) / 60.0)
                     ELSE CONVERT(decimal(10, 1), 0) END AS HorasSinRespuesta,
                ISNULL(cm.Texto, '') AS UltimoMensajeClienteRaw
            FROM dbo.CONV_CONVERSACIONES c
            JOIN dbo.CONV_ESTADOS est ON est.CodigoEstado = c.CodigoEstado
            LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = c.ClienteCodigo
            LEFT JOIN dbo.MA_CONTACTOS ct ON ct.id = c.IdContacto
            LEFT JOIN dbo.V_TA_Tecnicos tec ON LTRIM(RTRIM(tec.IdTecnico)) = LTRIM(RTRIM(c.IdTecnico))
            OUTER APPLY (
                SELECT TOP 1 t.Titulo
                FROM dbo.TICK_TICKETS t
                WHERE t.IdConversacion = c.IdConversacion AND ISNULL(t.Baja, 0) = 0
                ORDER BY t.IdTicket DESC
            ) tk
            OUTER APPLY (
                SELECT TOP 1 m.Direction, m.FechaHora
                FROM dbo.CONV_MENSAJES m
                WHERE m.IdConversacion = c.IdConversacion AND m.Direction IN (N'ENTRANTE', N'SALIENTE')
                ORDER BY m.FechaHora DESC, m.IdMensaje DESC
            ) ult
            OUTER APPLY (
                SELECT TOP 1 m.Texto
                FROM dbo.CONV_MENSAJES m
                WHERE m.IdConversacion = c.IdConversacion AND m.Direction = N'ENTRANTE'
                ORDER BY m.FechaHora DESC, m.IdMensaje DESC
            ) cm
            WHERE ISNULL(est.EsCerrado, 0) = 0
              AND ISNULL(c.Archivada, 0) = 0
            ORDER BY
                CASE WHEN ISNULL(ult.Direction, N'ENTRANTE') = N'ENTRANTE'
                     THEN DATEDIFF(MINUTE, ISNULL(ult.FechaHora, c.FechaHoraUltimoMensaje), GETDATE())
                     ELSE -1 END DESC,
                c.IdConversacion DESC;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        var rows = (await cn.QueryAsync<SoporteRow>(new CommandDefinition(
            sql, new { Max = MaxItems }, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct))).ToList();

        var items = rows.Select(r => new BriefSoportePendienteDto
        {
            Id = r.Id,
            Cliente = r.Cliente,
            Contacto = r.Contacto,
            Canal = r.Canal,
            Asunto = BriefTextSanitizer.Limpiar(r.Asunto, 160),
            FechaApertura = ToIso(r.FechaApertura),
            FechaUltimoMensaje = ToIso(r.FechaUltimoMensaje),
            UltimoEscribio = r.UltimoEscribio,
            Tecnico = r.Tecnico,
            Estado = r.Estado,
            HorasSinRespuesta = r.HorasSinRespuesta,
            UltimoMensajeCliente = BriefTextSanitizer.Limpiar(r.UltimoMensajeClienteRaw, MaxLargoMensaje)
        }).ToList();

        return new BriefSoportePendientesResponse
        {
            Generado = ToIso(DateTime.Now)!,
            Total = items.Count,
            Maximo = MaxItems,
            Items = items
        };
    }

    public async Task<BriefActividadResponse> GetActividadAsync(DateTime desde, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var idTecnico = IdTecnicoConfigurado();

        var args = new { Max = MaxItems, Desde = desde.Date, Tecnico = idTecnico };

        const string partesSql = """
            SELECT TOP (@Max)
                p.IdParteHora AS Id,
                ISNULL(NULLIF(p.Descripcion, ''), p.TipoTrabajo) AS Titulo,
                ISNULL(cli.RAZON_SOCIAL, '') AS Cliente,
                ISNULL(p.Estado, '') AS Estado,
                CONVERT(datetime, p.Fecha) AS Fecha,
                CONVERT(datetime, NULL) AS Vencimiento,
                p.Minutos AS Minutos
            FROM dbo.ALFACORE_PARTES_HORAS p
            LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = p.ClienteCodigo
            WHERE ISNULL(p.Baja, 0) = 0
              AND p.Fecha >= @Desde
              AND LTRIM(RTRIM(p.IdTecnico)) = @Tecnico
            ORDER BY p.Fecha DESC, p.IdParteHora DESC;
            """;

        const string ticketsAbiertosSql = """
            SELECT TOP (@Max)
                k.IdTicket AS Id,
                k.Titulo AS Titulo,
                ISNULL(cli.RAZON_SOCIAL, '') AS Cliente,
                ISNULL(est.Nombre, k.CodigoEstado) AS Estado,
                k.FechaHoraAlta AS Fecha,
                CONVERT(datetime, NULL) AS Vencimiento,
                CONVERT(int, NULL) AS Minutos,
                k.Prioridad AS Prioridad
            FROM dbo.TICK_TICKETS k
            JOIN dbo.TICK_ESTADOS est ON est.CodigoEstado = k.CodigoEstado
            LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = k.ClienteCodigo
            WHERE ISNULL(k.Baja, 0) = 0
              AND ISNULL(est.EsCerrado, 0) = 0
              AND LTRIM(RTRIM(k.IdTecnico)) = @Tecnico
            ORDER BY k.Prioridad DESC, k.FechaHoraAlta ASC;
            """;

        // La mesa de ayuda recibe los tickets del bot sin técnico: sin esta lista el resumen los ignoraría.
        const string ticketsSinAsignarSql = """
            SELECT TOP (@Max)
                k.IdTicket AS Id,
                k.Titulo AS Titulo,
                ISNULL(cli.RAZON_SOCIAL, '') AS Cliente,
                ISNULL(est.Nombre, k.CodigoEstado) AS Estado,
                k.FechaHoraAlta AS Fecha,
                CONVERT(datetime, NULL) AS Vencimiento,
                CONVERT(int, NULL) AS Minutos,
                k.Prioridad AS Prioridad
            FROM dbo.TICK_TICKETS k
            JOIN dbo.TICK_ESTADOS est ON est.CodigoEstado = k.CodigoEstado
            LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = k.ClienteCodigo
            WHERE ISNULL(k.Baja, 0) = 0
              AND ISNULL(est.EsCerrado, 0) = 0
              AND LTRIM(RTRIM(ISNULL(k.IdTecnico, ''))) = ''
            ORDER BY k.Prioridad DESC, k.FechaHoraAlta ASC;
            """;

        const string ticketsCerradosSql = """
            SELECT TOP (@Max)
                k.IdTicket AS Id,
                k.Titulo AS Titulo,
                ISNULL(cli.RAZON_SOCIAL, '') AS Cliente,
                ISNULL(est.Nombre, k.CodigoEstado) AS Estado,
                k.FechaHoraCierre AS Fecha,
                CONVERT(datetime, NULL) AS Vencimiento,
                CONVERT(int, NULL) AS Minutos,
                k.Prioridad AS Prioridad
            FROM dbo.TICK_TICKETS k
            JOIN dbo.TICK_ESTADOS est ON est.CodigoEstado = k.CodigoEstado
            LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = k.ClienteCodigo
            WHERE ISNULL(k.Baja, 0) = 0
              AND ISNULL(est.EsCerrado, 0) = 1
              AND k.FechaHoraCierre >= @Desde
              AND LTRIM(RTRIM(k.IdTecnico)) = @Tecnico
            ORDER BY k.FechaHoraCierre DESC;
            """;

        var partes = await QueryItemsAsync(cn, partesSql, args, "PARTE_HORAS", ct);
        var abiertas = await QueryItemsAsync(cn, ticketsAbiertosSql, args, "TICKET", ct);
        var sinAsignar = await QueryItemsAsync(cn, ticketsSinAsignarSql, args, "TICKET", ct);
        var cerradas = await QueryItemsAsync(cn, ticketsCerradosSql, args, "TICKET", ct);

        return new BriefActividadResponse
        {
            Generado = ToIso(DateTime.Now)!,
            Desde = desde.ToString("yyyy-MM-dd"),
            Tecnico = idTecnico,
            Maximo = MaxItems,
            PartesHoras = partes,
            TicketsAbiertosAsignados = abiertas,
            TicketsSinAsignar = sinAsignar,
            TicketsCerradosDesde = cerradas
        };
    }

    public async Task<BriefOportunidadesResponse> GetOportunidadesAsync(bool soloMias, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var idTecnico = soloMias ? IdTecnicoConfigurado() : string.Empty;

        // Abiertas = etapa que no es Ganada ni Perdida. Primero lo que requiere acción ya (vencida),
        // después lo que no tiene próxima acción cargada, y al final lo programado.
        const string sql = """
            SELECT TOP (@Max)
                o.IdOportunidad AS Id,
                o.Numero,
                o.Titulo,
                ISNULL(NULLIF(LTRIM(RTRIM(cli.RAZON_SOCIAL)), ''), '') AS Cliente,
                e.Nombre AS Etapa,
                o.ImporteEstimado,
                o.Probabilidad,
                o.Prioridad,
                ISNULL(tec.Nombre, '') AS Tecnico,
                o.FechaHoraAlta AS FechaAlta,
                CONVERT(datetime, o.FechaCierreEstimada) AS FechaCierreEstimada,
                mov.UltimoMovimiento,
                ISNULL(DATEDIFF(DAY, mov.UltimoMovimiento, GETDATE()), 0) AS DiasSinMovimiento,
                ISNULL(pa.Titulo, '') AS ProximaAccion,
                ISNULL(pa.TipoAccion, '') AS ProximaAccionTipo,
                pa.Vencimiento AS ProximaAccionVencimiento,
                CASE WHEN pa.IdTarea IS NULL THEN N'SIN_ACCION'
                     WHEN pa.Vencimiento < GETDATE() THEN N'VENCIDA'
                     ELSE N'PROGRAMADA' END AS ProximaAccionEstado
            FROM dbo.CRM_OPORTUNIDADES o
            JOIN dbo.CRM_ETAPAS e ON e.IdEtapa = o.IdEtapa
            LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = o.ClienteCodigo
            LEFT JOIN dbo.V_TA_Tecnicos tec ON LTRIM(RTRIM(tec.IdTecnico)) = LTRIM(RTRIM(o.IdTecnico))
            OUTER APPLY (
                SELECT TOP 1 t.IdTarea, t.Titulo, t.TipoAccion, t.Vencimiento
                FROM dbo.CRM_TAREAS t
                WHERE t.IdOportunidad = o.IdOportunidad
                  AND ISNULL(t.Baja, 0) = 0 AND ISNULL(t.Completada, 0) = 0
                ORDER BY t.Vencimiento ASC, t.IdTarea
            ) pa
            OUTER APPLY (
                SELECT MAX(x.FechaHora) AS UltimoMovimiento
                FROM (
                    SELECT a.FechaHora FROM dbo.CRM_ACTIVIDAD a WHERE a.IdOportunidad = o.IdOportunidad
                    UNION ALL SELECT o.FechaHoraAlta
                    UNION ALL SELECT o.FechaHoraModificacion
                ) x
            ) mov
            WHERE ISNULL(o.Baja, 0) = 0
              AND ISNULL(e.EsGanada, 0) = 0
              AND ISNULL(e.EsPerdida, 0) = 0
              AND (@Tecnico = N'' OR LTRIM(RTRIM(o.IdTecnico)) = @Tecnico)
            ORDER BY
                CASE WHEN pa.IdTarea IS NOT NULL AND pa.Vencimiento < GETDATE() THEN 0
                     WHEN pa.IdTarea IS NULL THEN 1
                     ELSE 2 END,
                pa.Vencimiento ASC,
                o.Prioridad DESC,
                o.IdOportunidad DESC;
            """;

        var rows = await cn.QueryAsync<OportunidadRow>(new CommandDefinition(
            sql, new { Max = MaxItems, Tecnico = idTecnico }, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct));

        var items = rows.Select(r => new BriefOportunidadDto
        {
            Id = r.Id,
            Numero = r.Numero,
            Titulo = BriefTextSanitizer.Limpiar(r.Titulo, 200),
            Cliente = r.Cliente,
            Etapa = r.Etapa,
            ImporteEstimado = r.ImporteEstimado,
            Probabilidad = r.Probabilidad,
            Prioridad = r.Prioridad,
            Tecnico = r.Tecnico,
            FechaAlta = ToIso(r.FechaAlta),
            FechaCierreEstimada = ToIso(r.FechaCierreEstimada),
            UltimoMovimiento = ToIso(r.UltimoMovimiento),
            DiasSinMovimiento = r.DiasSinMovimiento,
            ProximaAccion = BriefTextSanitizer.Limpiar(r.ProximaAccion, 200),
            ProximaAccionTipo = r.ProximaAccionTipo,
            ProximaAccionVencimiento = ToIso(r.ProximaAccionVencimiento),
            ProximaAccionEstado = r.ProximaAccionEstado
        }).ToList();

        return new BriefOportunidadesResponse
        {
            Generado = ToIso(DateTime.Now)!,
            Total = items.Count,
            Maximo = MaxItems,
            SoloMias = soloMias,
            Items = items
        };
    }

    private async Task<List<BriefActividadItemDto>> QueryItemsAsync(
        SqlConnection cn, string sql, object args, string tipo, CancellationToken ct)
    {
        var rows = await cn.QueryAsync<ActividadRow>(new CommandDefinition(
            sql, args, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct));

        return rows.Select(r => new BriefActividadItemDto
        {
            Tipo = tipo,
            Id = r.Id,
            Titulo = BriefTextSanitizer.Limpiar(r.Titulo, 200),
            Cliente = r.Cliente,
            Estado = r.Estado,
            Fecha = ToIso(r.Fecha),
            Vencimiento = ToIso(r.Vencimiento),
            Minutos = r.Minutos,
            Prioridad = r.Prioridad
        }).ToList();
    }

    // Viene de la config del servidor y no de TA_CONFIGURACION: así el usuario SQL de solo lectura
    // no necesita acceso a esa tabla (guarda claves y tokens).
    private string IdTecnicoConfigurado()
    {
        var id = configuration["Brief:IdTecnico"]?.Trim() ?? string.Empty;
        if (id.Length == 0)
            throw new BriefConfigException("Falta configurar Brief:IdTecnico (variable de entorno Brief__IdTecnico).");
        return id;
    }

    /// <summary>La base guarda hora local de Argentina sin zona: se le agrega el offset -03:00.</summary>
    internal static string? ToIso(DateTime? valor)
    {
        if (valor is null)
            return null;

        var local = DateTime.SpecifyKind(valor.Value, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, ArgentinaOffset).ToString("yyyy-MM-dd'T'HH:mm:sszzz");
    }

    private sealed class SoporteRow
    {
        public long Id { get; set; }
        public string Cliente { get; set; } = string.Empty;
        public string Contacto { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string Asunto { get; set; } = string.Empty;
        public DateTime? FechaApertura { get; set; }
        public DateTime? FechaUltimoMensaje { get; set; }
        public string UltimoEscribio { get; set; } = string.Empty;
        public string Tecnico { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public decimal HorasSinRespuesta { get; set; }
        public string UltimoMensajeClienteRaw { get; set; } = string.Empty;
    }

    private sealed class OportunidadRow
    {
        public long Id { get; set; }
        public int Numero { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public string Etapa { get; set; } = string.Empty;
        public decimal ImporteEstimado { get; set; }
        public int Probabilidad { get; set; }
        public int Prioridad { get; set; }
        public string Tecnico { get; set; } = string.Empty;
        public DateTime? FechaAlta { get; set; }
        public DateTime? FechaCierreEstimada { get; set; }
        public DateTime? UltimoMovimiento { get; set; }
        public int DiasSinMovimiento { get; set; }
        public string ProximaAccion { get; set; } = string.Empty;
        public string ProximaAccionTipo { get; set; } = string.Empty;
        public DateTime? ProximaAccionVencimiento { get; set; }
        public string ProximaAccionEstado { get; set; } = string.Empty;
    }

    private sealed class ActividadRow
    {
        public long Id { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime? Fecha { get; set; }
        public DateTime? Vencimiento { get; set; }
        public int? Minutos { get; set; }
        public int? Prioridad { get; set; }
    }
}

/// <summary>Falta una clave de configuración necesaria; el endpoint la traduce a 503 con mensaje corto.</summary>
public sealed class BriefConfigException(string message) : Exception(message);

/// <summary>
/// Limpia texto libre antes de sacarlo del sistema: oculta lo que parece contraseña, número de
/// tarjeta o CBU, colapsa espacios y trunca. Es un filtro de mejor esfuerzo, no una garantía.
/// </summary>
internal static partial class BriefTextSanitizer
{
    [GeneratedRegex(@"(?<k>contrase(?:ñ|n)a|clave(?:\s+fiscal)?|password|passwd|pass|pin|token|cvv|cvc)\s*(?:es|:|=)?\s*\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretoRegex();

    // 14 a 22 dígitos, con espacios o guiones opcionales: tarjetas (13-19) y CBU (22).
    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){14,22}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NumeroLargoRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex EspaciosRegex();

    public static string Limpiar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        // Primero se oculta y recién después se trunca, para no dejar a la vista un dato cortado a medias.
        var limpio = SecretoRegex().Replace(texto, m => m.Groups["k"].Value + ": [OCULTO]");
        limpio = NumeroLargoRegex().Replace(limpio, "[NRO OCULTO]");
        limpio = EspaciosRegex().Replace(limpio, " ").Trim();

        return limpio.Length <= maximo ? limpio : limpio[..(maximo - 1)].TrimEnd() + "…";
    }
}

using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace AlfaCore.Services;

/// <summary>
/// Avisos automáticos de Tickets (plazo por vencer o vencido, seguimiento vencido, cliente prioritario esperando y
/// resumen diario por técnico) y prioridad sugerida por IA. Lo dispara <see cref="TicketsAvisosHostedService"/> por
/// base o el botón «Revisar avisos ahora» de Tickets → Configuración. Qué avisar lo decide
/// <see cref="TicketsAvisosCalculo"/>; TICK_AVISOS evita mandar el mismo aviso dos veces.
/// </summary>
public sealed partial class TicketsService
{
    private const string UsuarioSistema = "AlfaCore";
    private const int IaTicketsPorCiclo = 10;

    public Task<TicketsAvisosConfigDto> GetAvisosConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetAvisosConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await LeerAvisosConfigAsync(cn, token);
        }, "No se pudo leer la configuración de avisos de tickets.", ct);

    public Task SaveAvisosConfigAsync(TicketsAvisosConfigDto config, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveAvisosConfig", async token =>
        {
            ArgumentNullException.ThrowIfNull(config);
            var coordinadores = string.Join(", ", config.CoordinadoresLista());
            if (coordinadores.Length > 150)
                throw new InvalidOperationException("La lista de usuarios que coordinan es demasiado larga (máximo 150 caracteres).");

            var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TICKETS-AVISOS-ACTIVO"] = SiNo(config.Activo),
                ["TICKETS-AVISO-POR-VENCER"] = SiNo(config.AvisarPorVencer),
                ["TICKETS-AVISO-VENCIDO"] = SiNo(config.AvisarVencido),
                ["TICKETS-AVISO-SEGUIMIENTO"] = SiNo(config.AvisarSeguimiento),
                ["TICKETS-AVISO-COORDINADORES"] = coordinadores,
                ["TICKETS-AVISO-PRIORITARIO-HORAS"] = Math.Clamp(config.PrioritarioHoras, 0, 1000).ToString(CultureInfo.InvariantCulture),
                ["TICKETS-RESUMEN-DIARIO-HORA"] = config.ResumenHora is int h ? Math.Clamp(h, 0, 23).ToString(CultureInfo.InvariantCulture) : string.Empty,
                ["TICKETS-IA-PRIORIDAD-BOT"] = SiNo(config.IaPrioridadBot)
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            await GuardarClavesAsync(cn, (SqlTransaction)tx, valores, token);
            await tx.CommitAsync(token);
        }, "No se pudo guardar la configuración de avisos de tickets.", ct);

    public Task<IReadOnlyList<TicketAvisoEnviadoDto>> GetUltimosAvisosAsync(int cantidad, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetUltimosAvisos", async token =>
        {
            var result = new List<TicketAvisoEnviadoDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TieneTablaAvisosAsync(cn, token))
                return (IReadOnlyList<TicketAvisoEnviadoDto>)result;

            var sql = $"""
                SELECT TOP ({Math.Clamp(cantidad, 1, 200).ToString(CultureInfo.InvariantCulture)})
                    a.IdTicket, ISNULL(t.Numero, 0), a.Tipo, ISNULL(a.Destino, N''), ISNULL(a.Detalle, N''), a.FechaHora
                FROM dbo.TICK_AVISOS a
                LEFT JOIN dbo.TICK_TICKETS t ON t.IdTicket = a.IdTicket
                ORDER BY a.FechaHora DESC, a.IdAviso DESC;
                """;
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                result.Add(new TicketAvisoEnviadoDto
                {
                    IdTicket = rd.GetInt64(0),
                    Numero = GetInt(rd, 1),
                    Tipo = GetString(rd, 2),
                    Destino = GetString(rd, 3),
                    Detalle = GetString(rd, 4),
                    FechaHora = rd.GetDateTime(5)
                });
            }
            return (IReadOnlyList<TicketAvisoEnviadoDto>)result;
        }, "No se pudieron leer los últimos avisos de tickets.", ct);

    public Task<TicketsAvisosResultadoDto> ProcesarAvisosAsync(bool manual, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ProcesarAvisos", async token =>
        {
            var resultado = new TicketsAvisosResultadoDto();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TieneTablaAvisosAsync(cn, token))
            {
                resultado.Mensaje = "Falta aplicar la actualización de avisos de Tickets en esta base.";
                return resultado;
            }

            var config = await LeerAvisosConfigAsync(cn, token);
            if (!config.Activo)
            {
                resultado.Mensaje = "Los avisos automáticos están apagados.";
                return resultado;
            }

            var horario = await LeerHorarioAsync(token);
            var ahora = (await LeerEsquemaAtencionAsync(cn, token)).Ahora;
            // El proceso automático no manda avisos de noche ni el fin de semana; los deja para el próximo horario hábil.
            if (!manual && !horario.EsHabil(ahora))
            {
                resultado.Mensaje = "Fuera del horario de atención.";
                return resultado;
            }

            resultado.Ejecutado = true;
            var abiertos = await BuscarParaAtencionAsync(cn, new TicketsFilters { Bandeja = TicketBandejaKeys.Abiertos }, "t.FechaHoraAlta DESC", token);
            var coordinadores = config.CoordinadoresLista();
            var tecnicos = abiertos
                .Where(t => !string.IsNullOrWhiteSpace(t.IdTecnico))
                .GroupBy(t => t.IdTecnico.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(t => t.TecnicoNombre.Trim()).FirstOrDefault(n => n.Length > 0) ?? g.Key, StringComparer.OrdinalIgnoreCase);

            var avisos = TicketsAvisosCalculo.Detectar(abiertos, config)
                .Concat(TicketsAvisosCalculo.Resumen(abiertos, config, horario, ahora));
            foreach (var aviso in avisos)
            {
                token.ThrowIfCancellationRequested();
                var aCoordinacion = aviso.ParaCoordinadores || aviso.IdTecnico.Length == 0;
                // Sin nadie a quien avisar no se marca: si después se cargan coordinadores, el aviso sale.
                if (aCoordinacion && coordinadores.Count == 0)
                    continue;

                var destino = aCoordinacion
                    ? string.Join(", ", coordinadores)
                    : tecnicos.TryGetValue(aviso.IdTecnico, out var nombre) ? nombre : aviso.IdTecnico;
                if (!await RegistrarAvisoAsync(cn, aviso.IdTicket, aviso.Tipo, aviso.Clave, destino, aviso.Detalle, token))
                    continue;

                if (aCoordinacion)
                    await AvisarCoordinadoresAsync(coordinadores, aviso, token);
                else if (avisosPush is not null)
                    await avisosPush.NotificarTecnicoAsync(aviso.IdTecnico, aviso.Titulo, aviso.Cuerpo, aviso.Url, token);

                if (aviso.IdTicket > 0)
                    await RegistrarActividadAsync(cn, aviso.IdTicket, "AVISO", $"Aviso a {destino}: {aviso.Detalle}.", token);

                resultado.Enviados++;
                resultado.Detalle.Add($"{aviso.Titulo} → {destino}");
            }

            if (config.IaPrioridadBot)
                resultado.PrioridadesIa = await PriorizarTicketsBotAsync(cn, abiertos, ahora, resultado.Detalle, token);

            resultado.Mensaje = resultado.Enviados == 0 && resultado.PrioridadesIa == 0
                ? "No había avisos nuevos para mandar."
                : $"Se mandaron {resultado.Enviados} aviso(s)" + (resultado.PrioridadesIa > 0 ? $" y la IA revisó {resultado.PrioridadesIa} ticket(s) del bot." : ".");
            return resultado;
        }, "No se pudieron procesar los avisos de tickets.", ct);

    public Task<TicketSugerenciaIaDto> SugerirPrioridadAsync(TicketSugerenciaIaRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SugerirPrioridad", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Titulo) && string.IsNullOrWhiteSpace(InformeHtmlSanitizer.ATexto(request.Descripcion)))
                return new TicketSugerenciaIaDto { Mensaje = "Escribí el nombre o la descripción del ticket para que la IA pueda sugerir una prioridad." };

            var bloqueo = await IaNoDisponibleAsync(token);
            if (bloqueo is not null)
                return new TicketSugerenciaIaDto { Mensaje = bloqueo };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var mensajes = request.IdTicket is long id && id > 0 ? await LeerMensajesTicketAsync(cn, id, token) : [];
            return await PedirSugerenciaAsync(request.Titulo, request.Descripcion, request.Cliente, mensajes, token)
                   ?? new TicketSugerenciaIaDto { Mensaje = "La IA no devolvió una sugerencia. Probá de nuevo en un momento." };
        }, "No se pudo pedir la sugerencia de prioridad a la IA.", ct);

    /// <summary>
    /// Tickets que creó el bot en el último día: la IA sugiere prioridad y resumen. Si nadie tocó el ticket todavía,
    /// la prioridad se aplica; si ya lo revisó una persona, queda solo anotada en la actividad.
    /// </summary>
    private async Task<int> PriorizarTicketsBotAsync(SqlConnection cn, IReadOnlyList<TicketGridItemDto> abiertos, DateTime ahora,
        List<string> detalle, CancellationToken ct)
    {
        var candidatos = abiertos
            .Where(t => TicketsAtencionCalculo.EnBandeja(t, TicketBandejaKeys.Bot, null) && t.FechaHoraAlta >= ahora.AddDays(-1))
            .OrderBy(t => t.FechaHoraAlta)
            .ToList();
        if (candidatos.Count == 0 || await IaNoDisponibleAsync(ct) is not null)
            return 0;

        var revisados = 0;
        foreach (var ticket in candidatos)
        {
            if (revisados >= IaTicketsPorCiclo)
                break;
            // Se marca antes de llamar a la IA: aunque falle, no se vuelve a pagar por el mismo ticket.
            if (!await RegistrarAvisoAsync(cn, ticket.IdTicket, TicketAvisoTipos.IaPrioridad, "ALTA", "IA", "Prioridad sugerida por IA", ct))
                continue;
            revisados++;

            var (titulo, descripcion) = await LeerTextoTicketAsync(cn, ticket.IdTicket, ct);
            var mensajes = await LeerMensajesTicketAsync(cn, ticket.IdTicket, ct);
            var sugerencia = await PedirSugerenciaAsync(titulo, descripcion, ticket.ClienteNombre, mensajes, ct);
            if (sugerencia is null)
            {
                await ActualizarDetalleAvisoAsync(cn, ticket.IdTicket, "La IA no devolvió sugerencia", ct);
                continue;
            }

            var aplicada = sugerencia.Prioridad != ticket.Prioridad
                           && await AplicarPrioridadIaAsync(cn, ticket.IdTicket, ticket.Prioridad, sugerencia.Prioridad, ct);
            var nombre = NombrePrioridad(sugerencia.Prioridad);
            var texto = sugerencia.Prioridad == ticket.Prioridad
                ? $"La IA confirmó la prioridad {nombre}: {sugerencia.Motivo}"
                : aplicada
                    ? $"La IA cambió la prioridad de {NombrePrioridad(ticket.Prioridad)} a {nombre}: {sugerencia.Motivo}"
                    : $"La IA sugiere prioridad {nombre} (no se cambió porque una persona ya revisó el ticket): {sugerencia.Motivo}";
            if (sugerencia.Resumen.Length > 0)
                texto += $" Resumen: {sugerencia.Resumen}";

            await RegistrarActividadAsync(cn, ticket.IdTicket, "IA", texto, ct);
            await ActualizarDetalleAvisoAsync(cn, ticket.IdTicket, $"Prioridad sugerida: {nombre}{(aplicada ? " (aplicada)" : string.Empty)}", ct);
            detalle.Add($"IA · {ticket.CodigoVisible}: {nombre}{(aplicada ? " (aplicada)" : string.Empty)}");
        }
        return revisados;
    }

    private async Task<TicketSugerenciaIaDto?> PedirSugerenciaAsync(string titulo, string? descripcion, string? cliente,
        IReadOnlyList<(bool DelCliente, string Texto)> mensajes, CancellationToken ct)
    {
        if (ia is null)
            return null;
        var contexto = TicketsIaPrioridad.ArmarContexto(titulo, descripcion, cliente, mensajes);
        var respuesta = await ia.GenerarTextoAsync(TicketsIaPrioridad.SystemPrompt, contexto, IaUsoFunciones.TicketPrioridad, 30, null, ct);
        return TicketsIaPrioridad.Parsear(respuesta);
    }

    /// <summary>Null si se puede usar la IA; si no, el motivo para mostrar.</summary>
    private async Task<string?> IaNoDisponibleAsync(CancellationToken ct)
    {
        if (ia is null || !ia.IsConfigured)
            return "La IA no está configurada en este servidor.";
        try
        {
            // Si la central no responde, el estado es null y se sigue (igual que el asistente de Conversaciones).
            if (iaConsumo is { Disponible: true } && await iaConsumo.GetTopeEstadoBaseActivaAsync(ct) is { Alcanzado: true })
                return "Se alcanzó el tope mensual de créditos de IA.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "TopeIa", ex, "No se pudo consultar el tope de IA; se sigue sin tope.",
                null, AppEventSeverity.Warning, ct);
        }
        return null;
    }

    private async Task AvisarCoordinadoresAsync(IReadOnlyList<string> usuarios, TicketAviso aviso, CancellationToken ct)
    {
        if (pushService is null || usuarios.Count == 0)
            return;
        try
        {
            await pushService.NotifyAvisoAsync(usuarios.ToList(), aviso.Titulo, aviso.Cuerpo, aviso.Url, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "AvisoCoordinadores", ex, "No se pudo enviar el aviso de tickets a coordinación.",
                new { aviso.IdTicket, aviso.Tipo }, AppEventSeverity.Warning, ct);
        }
    }

    private static async Task<TicketsAvisosConfigDto> LeerAvisosConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var config = new TicketsAvisosConfigDto();
        const string sql = """
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR, '')))
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'TICKETS-AVISOS-ACTIVO', N'TICKETS-AVISO-POR-VENCER', N'TICKETS-AVISO-VENCIDO',
                N'TICKETS-AVISO-SEGUIMIENTO', N'TICKETS-AVISO-COORDINADORES', N'TICKETS-AVISO-PRIORITARIO-HORAS',
                N'TICKETS-RESUMEN-DIARIO-HORA', N'TICKETS-IA-PRIORIDAD-BOT');
            """;
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var valor = GetString(rd, 1);
            switch (GetString(rd, 0))
            {
                case "TICKETS-AVISOS-ACTIVO": config.Activo = EsSi(valor, config.Activo); break;
                case "TICKETS-AVISO-POR-VENCER": config.AvisarPorVencer = EsSi(valor, config.AvisarPorVencer); break;
                case "TICKETS-AVISO-VENCIDO": config.AvisarVencido = EsSi(valor, config.AvisarVencido); break;
                case "TICKETS-AVISO-SEGUIMIENTO": config.AvisarSeguimiento = EsSi(valor, config.AvisarSeguimiento); break;
                case "TICKETS-AVISO-COORDINADORES": config.Coordinadores = valor; break;
                case "TICKETS-AVISO-PRIORITARIO-HORAS":
                    if (int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var horas)) config.PrioritarioHoras = Math.Max(0, horas);
                    break;
                case "TICKETS-RESUMEN-DIARIO-HORA":
                    config.ResumenHora = int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hora) && hora is >= 0 and <= 23 ? hora : null;
                    break;
                case "TICKETS-IA-PRIORIDAD-BOT": config.IaPrioridadBot = EsSi(valor, config.IaPrioridadBot); break;
            }
        }
        return config;
    }

    private static async Task<bool> TieneTablaAvisosAsync(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.TICK_AVISOS', N'U') IS NULL THEN 0 ELSE 1 END;", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>Marca el aviso como enviado. False si ya estaba (otro ciclo u otro servidor lo mandó).</summary>
    private static async Task<bool> RegistrarAvisoAsync(SqlConnection cn, long idTicket, string tipo, string clave, string destino, string detalle, CancellationToken ct)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.TICK_AVISOS WITH (UPDLOCK, HOLDLOCK) WHERE IdTicket = @IdTicket AND Tipo = @Tipo AND Clave = @Clave)
                INSERT INTO dbo.TICK_AVISOS (IdTicket, Tipo, Clave, Destino, Detalle, FechaHora)
                VALUES (@IdTicket, @Tipo, @Clave, @Destino, @Detalle, GETDATE());
            """;
        try
        {
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdTicket", idTicket);
            cmd.Parameters.AddWithValue("@Tipo", tipo);
            cmd.Parameters.AddWithValue("@Clave", Recortar(clave, 120));
            cmd.Parameters.AddWithValue("@Destino", Recortar(destino, 200));
            cmd.Parameters.AddWithValue("@Detalle", Recortar(detalle, 400));
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false;
        }
    }

    private static async Task ActualizarDetalleAvisoAsync(SqlConnection cn, long idTicket, string detalle, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "UPDATE dbo.TICK_AVISOS SET Detalle = @Detalle WHERE IdTicket = @IdTicket AND Tipo = @Tipo AND Clave = N'ALTA';", cn);
        cmd.Parameters.AddWithValue("@IdTicket", idTicket);
        cmd.Parameters.AddWithValue("@Tipo", TicketAvisoTipos.IaPrioridad);
        cmd.Parameters.AddWithValue("@Detalle", Recortar(detalle, 400));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task RegistrarActividadAsync(SqlConnection cn, long idTicket, string tipo, string texto, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await InsertActivityAsync(cn, tx, idTicket, tipo, texto, UsuarioSistema, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Aplica la prioridad de la IA solo si nadie la cambió ni trabajó el ticket todavía.</summary>
    private static async Task<bool> AplicarPrioridadIaAsync(SqlConnection cn, long idTicket, int actual, int nueva, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.TICK_TICKETS
            SET Prioridad = @Nueva, FechaHoraModificacion = GETDATE()
            WHERE IdTicket = @IdTicket AND ISNULL(Prioridad, 0) = @Actual
              AND NOT EXISTS (SELECT 1 FROM dbo.TICK_ACTIVIDAD a
                              WHERE a.IdTicket = @IdTicket AND ISNULL(a.Usuario, N'') NOT IN (N'', N'AlfaCore'));
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdTicket", idTicket);
        cmd.Parameters.AddWithValue("@Actual", actual);
        cmd.Parameters.AddWithValue("@Nueva", Math.Clamp(nueva, 0, 3));
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    private static async Task<(string Titulo, string Descripcion)> LeerTextoTicketAsync(SqlConnection cn, long idTicket, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT ISNULL(Titulo, N''), ISNULL(Descripcion, N'') FROM dbo.TICK_TICKETS WHERE IdTicket = @IdTicket;", cn);
        cmd.Parameters.AddWithValue("@IdTicket", idTicket);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? (GetString(rd, 0), GetString(rd, 1)) : (string.Empty, string.Empty);
    }

    /// <summary>Últimos 20 mensajes con texto de la conversación vinculada, del más viejo al más nuevo.</summary>
    private static async Task<IReadOnlyList<(bool DelCliente, string Texto)>> LeerMensajesTicketAsync(SqlConnection cn, long idTicket, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NOT NULL
                SELECT x.Direction, x.Texto
                FROM (
                    SELECT TOP (20) m.Direction, CAST(m.Texto AS nvarchar(max)) AS Texto, m.FechaHora, m.IdMensaje
                    FROM dbo.TICK_TICKETS t
                    INNER JOIN dbo.CONV_MENSAJES m ON m.IdConversacion = t.IdConversacion
                    WHERE t.IdTicket = @IdTicket AND m.Direction IN (N'ENTRANTE', N'SALIENTE') AND ISNULL(m.Texto, N'') <> N''
                    ORDER BY m.FechaHora DESC, m.IdMensaje DESC
                ) x
                ORDER BY x.FechaHora, x.IdMensaje;
            """;
        var result = new List<(bool, string)>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdTicket", idTicket);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            result.Add((string.Equals(GetString(rd, 0), "ENTRANTE", StringComparison.OrdinalIgnoreCase), GetString(rd, 1)));
        return result;
    }

    internal static string NombrePrioridad(int prioridad) => prioridad switch
    {
        >= 3 => "Alta",
        2 => "Media",
        1 => "Baja",
        _ => "Sin prioridad"
    };

    private static string SiNo(bool valor) => valor ? "S" : "N";

    private static bool EsSi(string valor, bool porDefecto)
        => valor.Length == 0 ? porDefecto : valor.Trim().ToUpperInvariant() is "S" or "SI" or "SÍ" or "1" or "TRUE";

    private static string Recortar(string? texto, int largo)
    {
        var limpio = (texto ?? string.Empty).Trim();
        return limpio.Length <= largo ? limpio : limpio[..largo];
    }
}

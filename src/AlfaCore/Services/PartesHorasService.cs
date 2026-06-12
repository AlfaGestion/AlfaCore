using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PartesHorasService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IPartesHorasService
{
    private const string ModuleName = "PartesHoras";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<ParteHoraGridItemDto>> SearchAsync(PartesHorasFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Search", async token =>
        {
            filters ??= new PartesHorasFilters();
            var pageSize = Math.Clamp(filters.PageSize, 1, 200);
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            var args = BuildFilterArgs(filters, skip, pageSize);
            var rows = (await cn.QueryAsync<ParteHoraGridItemDto>(new CommandDefinition($"""
                {SelectGridSql()}
                {BaseFromSql()}
                {WhereSql()}
                ORDER BY p.Fecha DESC, p.IdParteHora DESC
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
                """, args, cancellationToken: token))).ToList();

            var total = await cn.ExecuteScalarAsync<int>(new CommandDefinition($"""
                SELECT COUNT(1)
                {BaseFromSql()}
                {WhereSql()};
                """, args, cancellationToken: token));

            return new PagedResult<ParteHoraGridItemDto>
            {
                Items = rows,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar los partes de horas.", ct);

    public Task<ParteHoraDetailDto?> GetByIdAsync(long idParteHora, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetById", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            return await cn.QueryFirstOrDefaultAsync<ParteHoraDetailDto>(new CommandDefinition($"""
                SELECT
                    p.IdParteHora,
                    p.Fecha,
                    ISNULL(p.ClienteCodigo, '') AS ClienteCodigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
                    p.IdContacto,
                    ISNULL(contacto.Nombre_y_Apellido, '') AS ContactoNombre,
                    p.IdTicket,
                    CASE WHEN t.Numero IS NULL THEN '' ELSE RIGHT('0000' + CONVERT(varchar(10), t.Numero), 4) END AS TicketCodigo,
                    ISNULL(t.Titulo, '') AS TicketTitulo,
                    ISNULL(p.IdTecnico, '') AS IdTecnico,
                    ISNULL(tec.Nombre, '') AS TecnicoNombre,
                    ISNULL(p.Usuario, '') AS Usuario,
                    ISNULL(p.TipoTrabajo, '') AS TipoTrabajo,
                    p.Minutos,
                    p.Facturable,
                    p.Excedente,
                    ISNULL(p.Estado, '') AS Estado,
                    ISNULL(p.Descripcion, '') AS Descripcion,
                    p.CostoHora,
                    ISNULL(p.UsuarioAprobacion, '') AS UsuarioAprobacion,
                    p.FechaHoraAprobacion,
                    p.FechaHoraAlta,
                    p.FechaHoraModificacion
                FROM dbo.ALFACORE_PARTES_HORAS p
                LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = p.ClienteCodigo
                LEFT JOIN dbo.TICK_TICKETS t ON t.IdTicket = p.IdTicket
                LEFT JOIN dbo.V_TA_Tecnicos tec ON LTRIM(RTRIM(tec.IdTecnico)) = LTRIM(RTRIM(p.IdTecnico))
                LEFT JOIN dbo.MA_CONTACTOS contacto ON contacto.id = p.IdContacto
                WHERE p.IdParteHora = @IdParteHora
                  AND ISNULL(p.Baja, 0) = 0;
                """, new { IdParteHora = idParteHora }, cancellationToken: token));
        }, "No se pudo cargar el parte de horas.", ct);

    public Task<long> SaveAsync(ParteHoraSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Save", async token =>
        {
            Validate(request);
            var normalized = Normalize(request);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!string.IsNullOrWhiteSpace(normalized.ClienteCodigo))
            {
                var monthly = await GetMonthlyUsageAsync(cn, normalized.ClienteCodigo, normalized.Fecha, normalized.IdParteHora, token);
                var config = await GetClienteConfigInternalAsync(cn, normalized.ClienteCodigo, token);
                if (config is not null && config.HorasIncluidasMes > 0)
                {
                    var limit = (config.HorasIncluidasMes * 60) + config.ToleranciaMinutos;
                    normalized.Excedente = normalized.Facturable && monthly + normalized.Minutos > limit;
                }
            }

            var costoHora = await ResolveCostoHoraAsync(cn, normalized.IdTecnico, token);
            long id;
            if (normalized.IdParteHora.HasValue && normalized.IdParteHora.Value > 0)
            {
                const string sql = """
                    UPDATE dbo.ALFACORE_PARTES_HORAS
                    SET Fecha = @Fecha,
                        ClienteCodigo = @ClienteCodigo,
                        IdContacto = @IdContacto,
                        IdTicket = @IdTicket,
                        IdTecnico = @IdTecnico,
                        Usuario = @Usuario,
                        TipoTrabajo = @TipoTrabajo,
                        Minutos = @Minutos,
                        Facturable = @Facturable,
                        Excedente = @Excedente,
                        Estado = @Estado,
                        Descripcion = @Descripcion,
                        CostoHora = @CostoHora,
                        UsuarioModificacion = @UsuarioAccion,
                        FechaHoraModificacion = GETDATE()
                    WHERE IdParteHora = @IdParteHora
                      AND ISNULL(Baja, 0) = 0;
                    """;
                await cn.ExecuteAsync(new CommandDefinition(sql, new
                {
                    normalized.IdParteHora,
                    normalized.Fecha,
                    ClienteCodigo = DbNullable(normalized.ClienteCodigo),
                    normalized.IdContacto,
                    normalized.IdTicket,
                    IdTecnico = DbNullable(normalized.IdTecnico),
                    normalized.Usuario,
                    normalized.TipoTrabajo,
                    normalized.Minutos,
                    normalized.Facturable,
                    normalized.Excedente,
                    normalized.Estado,
                    normalized.Descripcion,
                    CostoHora = costoHora,
                    normalized.UsuarioAccion
                }, cancellationToken: token));
                id = normalized.IdParteHora.Value;
            }
            else
            {
                const string sql = """
                    INSERT INTO dbo.ALFACORE_PARTES_HORAS
                    (
                        Fecha, ClienteCodigo, IdContacto, IdTicket, IdTecnico, Usuario,
                        TipoTrabajo, Minutos, Facturable, Excedente, Estado, Descripcion,
                        CostoHora, UsuarioAlta, FechaHoraAlta, Baja
                    )
                    VALUES
                    (
                        @Fecha, @ClienteCodigo, @IdContacto, @IdTicket, @IdTecnico, @Usuario,
                        @TipoTrabajo, @Minutos, @Facturable, @Excedente, @Estado, @Descripcion,
                        @CostoHora, @UsuarioAccion, GETDATE(), 0
                    );
                    SELECT CONVERT(bigint, SCOPE_IDENTITY());
                    """;
                id = await cn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
                {
                    normalized.Fecha,
                    ClienteCodigo = DbNullable(normalized.ClienteCodigo),
                    normalized.IdContacto,
                    normalized.IdTicket,
                    IdTecnico = DbNullable(normalized.IdTecnico),
                    normalized.Usuario,
                    normalized.TipoTrabajo,
                    normalized.Minutos,
                    normalized.Facturable,
                    normalized.Excedente,
                    normalized.Estado,
                    normalized.Descripcion,
                    CostoHora = costoHora,
                    normalized.UsuarioAccion
                }, cancellationToken: token));
            }

            await LogTicketActivityAsync(cn, id, normalized, token);
            await appEvents.LogAuditAsync(ModuleName, "Save", "ALFACORE_PARTES_HORAS", id.ToString(), "Parte de horas guardado.", new { normalized.ClienteCodigo, normalized.IdTicket, normalized.Minutos }, token);
            return id;
        }, "No se pudo guardar el parte de horas.", ct);

    public Task DeleteAsync(long idParteHora, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("Delete", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.ALFACORE_PARTES_HORAS
                SET Baja = 1,
                    UsuarioModificacion = @Usuario,
                    FechaHoraModificacion = GETDATE()
                WHERE IdParteHora = @IdParteHora
                  AND Estado <> @Aprobado;
                """, new { IdParteHora = idParteHora, Usuario = NormalizeUser(usuarioAccion), Aprobado = ParteHoraEstadoKeys.Aprobado }, cancellationToken: token));
        }, "No se pudo anular el parte de horas.", ct);

    public Task AprobarAsync(long idParteHora, string usuarioAccion, CancellationToken ct = default)
        => ChangeEstadoAsync(idParteHora, ParteHoraEstadoKeys.Aprobado, usuarioAccion, "Aprobar", "No se pudo aprobar el parte de horas.", ct);

    public Task RechazarAsync(long idParteHora, string usuarioAccion, CancellationToken ct = default)
        => ChangeEstadoAsync(idParteHora, ParteHoraEstadoKeys.Rechazado, usuarioAccion, "Rechazar", "No se pudo rechazar el parte de horas.", ct);

    public Task<ParteHoraDashboardDto> GetDashboardAsync(PartesHorasFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Dashboard", async token =>
        {
            filters ??= new PartesHorasFilters();
            await using var cn = new SqlConnection(ConnectionString);
            var args = BuildFilterArgs(filters, 0, 200);
            var dashboard = await cn.QueryFirstAsync<ParteHoraDashboardDto>(new CommandDefinition($"""
                SELECT
                    ISNULL(SUM(p.Minutos), 0) AS MinutosTrabajados,
                    ISNULL(SUM(CASE WHEN p.Facturable = 1 THEN p.Minutos ELSE 0 END), 0) AS MinutosFacturables,
                    ISNULL(SUM(CASE WHEN p.Excedente = 1 THEN p.Minutos ELSE 0 END), 0) AS MinutosExcedentes,
                    ISNULL(SUM(CASE WHEN p.Estado IN (N'BORRADOR', N'PRESENTADO') THEN 1 ELSE 0 END), 0) AS PartesPendientes
                {BaseFromSql()}
                {WhereSql()};
                """, args, cancellationToken: token));

            dashboard.Clientes = (await cn.QueryAsync<ParteHoraResumenClienteDto>(new CommandDefinition($"""
                SELECT TOP (20)
                    ISNULL(p.ClienteCodigo, '') AS ClienteCodigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
                    SUM(p.Minutos) AS MinutosTrabajados,
                    SUM(CASE WHEN p.Facturable = 1 THEN p.Minutos ELSE 0 END) AS MinutosFacturables,
                    SUM(CASE WHEN p.Excedente = 1 THEN p.Minutos ELSE 0 END) AS MinutosExcedentes,
                    ISNULL(cfg.HorasIncluidasMes, 0) * 60 AS MinutosIncluidos,
                    ISNULL(cfg.ToleranciaMinutos, 0) AS ToleranciaMinutos,
                    SUM(CASE WHEN p.CostoHora IS NULL THEN 0 ELSE (CONVERT(decimal(18, 4), p.Minutos) / 60) * p.CostoHora END) AS CostoInterno,
                    COUNT(DISTINCT p.IdTicket) AS Tickets
                {BaseFromSql()}
                LEFT JOIN dbo.ALFACORE_PARTES_HORAS_CLIENTES cfg ON cfg.ClienteCodigo = p.ClienteCodigo AND ISNULL(cfg.Activa, 1) = 1
                {WhereSql()}
                GROUP BY p.ClienteCodigo, cli.RAZON_SOCIAL, cfg.HorasIncluidasMes, cfg.ToleranciaMinutos
                ORDER BY SUM(p.Minutos) DESC;
                """, args, cancellationToken: token))).ToList();

            dashboard.Tecnicos = (await cn.QueryAsync<ParteHoraResumenTecnicoDto>(new CommandDefinition($"""
                SELECT TOP (20)
                    ISNULL(p.IdTecnico, '') AS IdTecnico,
                    ISNULL(tec.Nombre, '') AS TecnicoNombre,
                    SUM(p.Minutos) AS MinutosTrabajados,
                    SUM(CASE WHEN p.Facturable = 1 THEN p.Minutos ELSE 0 END) AS MinutosFacturables,
                    SUM(CASE WHEN p.CostoHora IS NULL THEN 0 ELSE (CONVERT(decimal(18, 4), p.Minutos) / 60) * p.CostoHora END) AS CostoInterno
                {BaseFromSql()}
                {WhereSql()}
                GROUP BY p.IdTecnico, tec.Nombre
                ORDER BY SUM(p.Minutos) DESC;
                """, args, cancellationToken: token))).ToList();

            return dashboard;
        }, "No se pudo cargar el resumen de horas.", ct);

    public Task<ParteHoraLookupDto> GetLookupsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Lookups", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var tecnicos = await cn.QueryAsync<ParteHoraLookupOptionDto>(new CommandDefinition("""
                SELECT TOP (200)
                    LTRIM(RTRIM(ISNULL(IdTecnico, ''))) AS Codigo,
                    ISNULL(Nombre, '') AS Titulo,
                    ISNULL(UsuarioAsociado, '') AS Subtitulo
                FROM dbo.V_TA_Tecnicos
                WHERE ISNULL(Baja, 0) = 0
                ORDER BY Nombre;
                """, cancellationToken: token));

            return new ParteHoraLookupDto { Tecnicos = tecnicos.ToList() };
        }, "No se pudieron cargar los datos auxiliares de partes.", ct);

    public Task<IReadOnlyList<ParteHoraLookupOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<ParteHoraLookupOptionDto>>("SearchClientes", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var search = SearchTextHelper.LikeContains((texto ?? string.Empty).Trim());
            var rows = await cn.QueryAsync<ParteHoraLookupOptionDto>(new CommandDefinition("""
                SELECT TOP (25)
                    CODIGO AS Codigo,
                    RAZON_SOCIAL AS Titulo,
                    CONCAT(ISNULL(LOCALIDAD, ''), CASE WHEN ISNULL(PROVINCIA, '') = '' THEN '' ELSE ' - ' + PROVINCIA END) AS Subtitulo
                FROM dbo.VT_CLIENTES
                WHERE @Search = '%%'
                   OR CODIGO COLLATE Latin1_General_CI_AI LIKE @Search
                   OR RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @Search
                ORDER BY RAZON_SOCIAL;
                """, new { Search = search }, cancellationToken: token));
            return rows.ToList();
        }, "No se pudieron buscar clientes.", ct);

    public Task<IReadOnlyList<ParteHoraTicketOptionDto>> SearchTicketsAsync(string texto, string? clienteCodigo = null, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<ParteHoraTicketOptionDto>>("SearchTickets", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var rows = await cn.QueryAsync<ParteHoraTicketOptionDto>(new CommandDefinition("""
                SELECT TOP (25)
                    t.IdTicket,
                    t.Numero,
                    RIGHT('0000' + CONVERT(varchar(10), t.Numero), 4) AS CodigoVisible,
                    ISNULL(t.Titulo, '') AS Titulo,
                    ISNULL(t.ClienteCodigo, '') AS ClienteCodigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre
                FROM dbo.TICK_TICKETS t
                LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = t.ClienteCodigo
                WHERE ISNULL(t.Baja, 0) = 0
                  AND (@ClienteCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(t.ClienteCodigo, '')))) = @ClienteCodigo)
                  AND (@Search = '' OR CONVERT(varchar(20), t.Numero) LIKE @SearchLike OR ISNULL(t.Titulo, '') COLLATE Latin1_General_CI_AI LIKE @SearchLike)
                ORDER BY t.FechaHoraAlta DESC, t.IdTicket DESC;
                """, new
            {
                ClienteCodigo = TrimUpper(clienteCodigo),
                Search = (texto ?? string.Empty).Trim(),
                SearchLike = SearchTextHelper.LikeContains((texto ?? string.Empty).Trim())
            }, cancellationToken: token));
            return rows.ToList();
        }, "No se pudieron buscar tickets.", ct);

    public Task<ParteHoraClienteConfigDto?> GetClienteConfigAsync(string clienteCodigo, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetClienteConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            return await GetClienteConfigInternalAsync(cn, clienteCodigo, token);
        }, "No se pudo cargar la configuración del cliente.", ct);

    public Task SaveClienteConfigAsync(ParteHoraClienteConfigDto config, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveClienteConfig", async token =>
        {
            if (string.IsNullOrWhiteSpace(config.ClienteCodigo))
                throw ValidationError("cliente", "Seleccioná un cliente para configurar su bolsa mensual.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.ALFACORE_PARTES_HORAS_CLIENTES
                SET HorasIncluidasMes = @HorasIncluidasMes,
                    ToleranciaMinutos = @ToleranciaMinutos,
                    Activa = @Activa,
                    Observaciones = @Observaciones,
                    UsuarioModificacion = @Usuario,
                    FechaHoraModificacion = GETDATE()
                WHERE ClienteCodigo = @ClienteCodigo;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.ALFACORE_PARTES_HORAS_CLIENTES
                    (
                        ClienteCodigo, HorasIncluidasMes, ToleranciaMinutos, Activa,
                        Observaciones, UsuarioAlta, FechaHoraAlta
                    )
                    VALUES
                    (
                        @ClienteCodigo, @HorasIncluidasMes, @ToleranciaMinutos, @Activa,
                        @Observaciones, @Usuario, GETDATE()
                    );
                END;
                """, new
            {
                ClienteCodigo = config.ClienteCodigo.Trim().ToUpperInvariant(),
                HorasIncluidasMes = Math.Max(0, config.HorasIncluidasMes),
                ToleranciaMinutos = Math.Max(0, config.ToleranciaMinutos),
                config.Activa,
                Observaciones = config.Observaciones?.Trim() ?? string.Empty,
                Usuario = NormalizeUser(usuarioAccion)
            }, cancellationToken: token));
        }, "No se pudo guardar la bolsa mensual del cliente.", ct);

    private Task ChangeEstadoAsync(long idParteHora, string estado, string usuarioAccion, string action, string userMessage, CancellationToken ct)
        => ExecuteLoggedAsync(action, async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.ALFACORE_PARTES_HORAS
                SET Estado = @Estado,
                    UsuarioAprobacion = @Usuario,
                    FechaHoraAprobacion = GETDATE(),
                    UsuarioModificacion = @Usuario,
                    FechaHoraModificacion = GETDATE()
                WHERE IdParteHora = @IdParteHora
                  AND ISNULL(Baja, 0) = 0;
                """, new { IdParteHora = idParteHora, Estado = estado, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
        }, userMessage, ct);

    private static string SelectGridSql() => """
        SELECT
            p.IdParteHora,
            p.Fecha,
            ISNULL(p.ClienteCodigo, '') AS ClienteCodigo,
            ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
            p.IdTicket,
            CASE WHEN t.Numero IS NULL THEN '' ELSE RIGHT('0000' + CONVERT(varchar(10), t.Numero), 4) END AS TicketCodigo,
            ISNULL(t.Titulo, '') AS TicketTitulo,
            ISNULL(p.IdTecnico, '') AS IdTecnico,
            ISNULL(tec.Nombre, '') AS TecnicoNombre,
            ISNULL(p.Usuario, '') AS Usuario,
            ISNULL(p.TipoTrabajo, '') AS TipoTrabajo,
            p.Minutos,
            p.Facturable,
            p.Excedente,
            ISNULL(p.Estado, '') AS Estado,
            ISNULL(p.Descripcion, '') AS Descripcion,
            p.FechaHoraAlta
        """;

    private static string BaseFromSql() => """
        FROM dbo.ALFACORE_PARTES_HORAS p
        LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = p.ClienteCodigo
        LEFT JOIN dbo.TICK_TICKETS t ON t.IdTicket = p.IdTicket
        LEFT JOIN dbo.V_TA_Tecnicos tec ON LTRIM(RTRIM(tec.IdTecnico)) = LTRIM(RTRIM(p.IdTecnico))
        """;

    private static string WhereSql() => """
        WHERE ISNULL(p.Baja, 0) = 0
          AND (@FechaDesde IS NULL OR p.Fecha >= @FechaDesde)
          AND (@FechaHasta IS NULL OR p.Fecha < DATEADD(DAY, 1, @FechaHasta))
          AND (@ClienteCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(p.ClienteCodigo, '')))) = @ClienteCodigo)
          AND (@IdTecnico = '' OR UPPER(LTRIM(RTRIM(ISNULL(p.IdTecnico, '')))) = @IdTecnico)
          AND (@Usuario = '' OR UPPER(LTRIM(RTRIM(ISNULL(p.Usuario, '')))) = @Usuario)
          AND (@Estado = '' OR UPPER(LTRIM(RTRIM(ISNULL(p.Estado, '')))) = @Estado)
          AND (@TipoTrabajo = '' OR UPPER(LTRIM(RTRIM(ISNULL(p.TipoTrabajo, '')))) = @TipoTrabajo)
          AND (@IdTicket IS NULL OR p.IdTicket = @IdTicket)
          AND (@SoloFacturables = 0 OR p.Facturable = 1)
          AND (@SoloExcedentes = 0 OR p.Excedente = 1)
          AND (
                @Texto = ''
                OR ISNULL(p.ClienteCodigo, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                OR ISNULL(cli.RAZON_SOCIAL, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                OR ISNULL(t.Titulo, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                OR CONVERT(varchar(20), ISNULL(t.Numero, 0)) LIKE @TextoLike
                OR ISNULL(p.Descripcion, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
              )
        """;

    private static object BuildFilterArgs(PartesHorasFilters filters, int skip, int pageSize) => new
    {
        filters.FechaDesde,
        filters.FechaHasta,
        ClienteCodigo = TrimUpper(filters.ClienteCodigo),
        IdTecnico = TrimUpper(filters.IdTecnico),
        Usuario = TrimUpper(filters.Usuario),
        Estado = TrimUpper(filters.Estado),
        TipoTrabajo = TrimUpper(filters.TipoTrabajo),
        filters.IdTicket,
        filters.SoloFacturables,
        filters.SoloExcedentes,
        Texto = (filters.Texto ?? string.Empty).Trim(),
        TextoLike = SearchTextHelper.LikeContains((filters.Texto ?? string.Empty).Trim()),
        Skip = skip,
        PageSize = pageSize
    };

    private static void Validate(ParteHoraSaveRequest request)
    {
        if (request.Fecha == default)
            throw ValidationError("fecha", "Indicá la fecha del trabajo.");
        if (request.Minutos <= 0)
            throw ValidationError("minutos", "La duración debe ser mayor a cero.");
        if (request.Minutos > 24 * 60)
            throw ValidationError("minutos", "La duración no puede superar 24 horas en un único parte.");
        if (string.IsNullOrWhiteSpace(request.IdTecnico) && string.IsNullOrWhiteSpace(request.Usuario))
            throw ValidationError("tecnico", "Indicá el técnico o usuario que realizó el trabajo.");
        if (request.TipoTrabajo != ParteHoraTipoTrabajoKeys.Interno && string.IsNullOrWhiteSpace(request.ClienteCodigo))
            throw ValidationError("cliente", "Indicá el cliente o usá tipo Interno para tareas no imputables.");
    }

    private static ParteHoraSaveRequest Normalize(ParteHoraSaveRequest request)
        => new()
        {
            IdParteHora = request.IdParteHora,
            Fecha = request.Fecha.Date,
            ClienteCodigo = request.ClienteCodigo?.Trim().ToUpperInvariant() ?? string.Empty,
            IdContacto = request.IdContacto,
            IdTicket = request.IdTicket,
            IdTecnico = request.IdTecnico?.Trim().ToUpperInvariant() ?? string.Empty,
            Usuario = NormalizeUser(request.Usuario),
            TipoTrabajo = NormalizeAllowed(request.TipoTrabajo, ParteHoraTipoTrabajoKeys.All, ParteHoraTipoTrabajoKeys.Soporte),
            Minutos = request.Minutos,
            Facturable = request.Facturable,
            Excedente = request.Excedente,
            Estado = NormalizeAllowed(request.Estado, ParteHoraEstadoKeys.All, ParteHoraEstadoKeys.Borrador),
            Descripcion = request.Descripcion?.Trim() ?? string.Empty,
            UsuarioAccion = NormalizeUser(request.UsuarioAccion)
        };

    private static string NormalizeAllowed(string? value, IReadOnlyCollection<string> allowed, string fallback)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : fallback;
    }

    private static async Task<int> GetMonthlyUsageAsync(SqlConnection cn, string clienteCodigo, DateTime fecha, long? excludedId, CancellationToken ct)
    {
        var monthStart = new DateTime(fecha.Year, fecha.Month, 1);
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT ISNULL(SUM(Minutos), 0)
            FROM dbo.ALFACORE_PARTES_HORAS
            WHERE ISNULL(Baja, 0) = 0
              AND Facturable = 1
              AND UPPER(LTRIM(RTRIM(ISNULL(ClienteCodigo, '')))) = @ClienteCodigo
              AND Fecha >= @MonthStart
              AND Fecha < DATEADD(MONTH, 1, @MonthStart)
              AND (@ExcludedId IS NULL OR IdParteHora <> @ExcludedId);
            """, new { ClienteCodigo = clienteCodigo.Trim().ToUpperInvariant(), MonthStart = monthStart, ExcludedId = excludedId }, cancellationToken: ct));
    }

    private static async Task<decimal?> ResolveCostoHoraAsync(SqlConnection cn, string idTecnico, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idTecnico))
            return null;

        return await cn.ExecuteScalarAsync<decimal?>(new CommandDefinition("""
            SELECT TOP (1) CostoHora
            FROM dbo.V_TA_Tecnicos
            WHERE UPPER(LTRIM(RTRIM(ISNULL(IdTecnico, '')))) = @IdTecnico;
            """, new { IdTecnico = idTecnico.Trim().ToUpperInvariant() }, cancellationToken: ct));
    }

    private static async Task<ParteHoraClienteConfigDto?> GetClienteConfigInternalAsync(SqlConnection cn, string clienteCodigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clienteCodigo))
            return null;

        return await cn.QueryFirstOrDefaultAsync<ParteHoraClienteConfigDto>(new CommandDefinition("""
            SELECT
                cfg.ClienteCodigo,
                ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
                cfg.HorasIncluidasMes,
                cfg.ToleranciaMinutos,
                cfg.Activa,
                ISNULL(cfg.Observaciones, '') AS Observaciones
            FROM dbo.ALFACORE_PARTES_HORAS_CLIENTES cfg
            LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = cfg.ClienteCodigo
            WHERE UPPER(LTRIM(RTRIM(cfg.ClienteCodigo))) = @ClienteCodigo;
            """, new { ClienteCodigo = clienteCodigo.Trim().ToUpperInvariant() }, cancellationToken: ct));
    }

    private static async Task LogTicketActivityAsync(SqlConnection cn, long id, ParteHoraSaveRequest request, CancellationToken ct)
    {
        if (!request.IdTicket.HasValue)
            return;

        await cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.TICK_ACTIVIDAD (IdTicket, TipoActividad, Descripcion, Usuario, FechaHora)
            VALUES (@IdTicket, N'HORAS', @Descripcion, @Usuario, GETDATE());
            """, new
        {
            request.IdTicket,
            Descripcion = $"Parte de horas #{id}: {FormatMinutes(request.Minutos)} - {request.Descripcion}",
            Usuario = request.UsuarioAccion
        }, cancellationToken: ct));
    }

    private static string FormatMinutes(int minutes)
        => $"{minutes / 60}h {minutes % 60:00}m";

    private static AppValidationException ValidationError(string field, string message)
    {
        var validation = new ValidationResult();
        validation.Add(field, message);
        return new AppValidationException(message, validation);
    }

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string TrimUpper(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (AppValidationException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private async Task ExecuteLoggedAsync(string action, Func<CancellationToken, Task> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            await operation(ct);
        }
        catch (AppValidationException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }
}

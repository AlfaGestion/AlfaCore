using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class CrmService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IConversacionAnalisisService analisisService) : ICrmService
{
    private const string ModuleName = "CRM";
    private const string ConfigGroup = "CRM";
    private const string ViewConfigPrefix = "USUVIEW-CRM-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<CrmLookupDto> GetLookupsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetLookups", async token =>
        {
            const string sql = """
                SELECT
                    IdEtapa,
                    Nombre,
                    ISNULL(Color, '') AS Color,
                    ISNULL(EsGanada, 0) AS EsGanada,
                    ISNULL(EsPerdida, 0) AS EsPerdida,
                    ISNULL(Orden, 0) AS Orden
                FROM dbo.CRM_ETAPAS
                WHERE ISNULL(Activa, 1) = 1
                ORDER BY Orden, IdEtapa;

                SELECT
                    LTRIM(RTRIM(ISNULL(IdTecnico, ''))) AS IdTecnico,
                    ISNULL(Nombre, '') AS Nombre,
                    ISNULL(Cargo, '') AS Cargo,
                    ISNULL(UsuarioAsociado, '') AS UsuarioAsociado,
                    ISNULL(SistemaAsociado, '') AS SistemaAsociado
                FROM dbo.V_TA_Tecnicos
                WHERE ISNULL(Baja, 0) = 0
                ORDER BY Nombre;

                SELECT IdEtiqueta, Nombre, ISNULL(Color, '') AS Color
                FROM dbo.CRM_ETIQUETAS
                WHERE ISNULL(Activa, 1) = 1
                ORDER BY Nombre;

                SELECT IdMotivo, Nombre, ISNULL(Orden, 0) AS Orden
                FROM dbo.CRM_MOTIVOS_PERDIDA
                WHERE ISNULL(Activo, 1) = 1
                ORDER BY Orden, Nombre;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            using var grid = await cn.QueryMultipleAsync(new CommandDefinition(sql, cancellationToken: token));
            return new CrmLookupDto
            {
                Etapas = (await grid.ReadAsync<CrmEtapaDto>()).AsList(),
                Tecnicos = (await grid.ReadAsync<ConversacionTecnicoOptionDto>()).AsList(),
                Etiquetas = (await grid.ReadAsync<CrmEtiquetaDto>()).AsList(),
                MotivosPerdida = (await grid.ReadAsync<CrmMotivoPerdidaDto>()).AsList()
            };
        }, "No se pudieron cargar los datos auxiliares de CRM.", ct);

    public Task<PagedResult<CrmOpportunityDto>> SearchAsync(CrmFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Search", async token =>
        {
            filters ??= new CrmFilters();
            var pageSize = Math.Clamp(filters.PageSize, 1, 200);
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;
            var orderBy = BuildOrderBy(filters.OrdenarPor, filters.OrdenDescendente);
            var sql = $"""
                {OpportunitySelectSql()}
                {OpportunityFromSql()}
                {OpportunityWhereSql()}
                ORDER BY {orderBy}, o.IdOportunidad DESC
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                {OpportunityFromSql()}
                {OpportunityWhereSql()};
                """;

            var args = BuildFilterParameters(filters);
            args.Add("Skip", skip);
            args.Add("PageSize", pageSize);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            using var grid = await cn.QueryMultipleAsync(new CommandDefinition(sql, args, cancellationToken: token));
            var rows = (await grid.ReadAsync<CrmOpportunityDto>()).AsList();
            var total = await grid.ReadFirstOrDefaultAsync<int>();
            await HydrateEtiquetasAsync(cn, rows, token);

            return new PagedResult<CrmOpportunityDto>
            {
                Items = rows,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar las oportunidades de CRM.", ct);

    public Task<IReadOnlyList<CrmOpportunityDto>> GetKanbanAsync(CrmFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetKanban", async token =>
        {
            filters ??= new CrmFilters();
            var limit = Math.Clamp(filters.PageSize <= 0 ? 160 : filters.PageSize, 25, 240);
            var sql = $"""
                {OpportunitySelectSql()}
                {OpportunityFromSql()}
                {OpportunityWhereSql()}
                ORDER BY e.Orden, o.Prioridad DESC, o.FechaHoraAlta DESC, o.IdOportunidad DESC
                OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY;
                """;

            var args = BuildFilterParameters(filters);
            args.Add("Limit", limit);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = (await cn.QueryAsync<CrmOpportunityDto>(new CommandDefinition(sql, args, cancellationToken: token))).AsList();
            await HydrateEtiquetasAsync(cn, rows, token);
            return (IReadOnlyList<CrmOpportunityDto>)rows;
        }, "No se pudo cargar el tablero CRM.", ct);

    public Task<CrmOpportunityDetailDto?> GetByIdAsync(long idOportunidad, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetById", async token =>
        {
            var sql = $"""
                {OpportunitySelectSql()}
                {OpportunityFromSql()}
                WHERE o.IdOportunidad = @IdOportunidad
                  AND ISNULL(o.Baja, 0) = 0;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var item = await cn.QueryFirstOrDefaultAsync<CrmOpportunityDetailDto>(
                new CommandDefinition(sql, new { IdOportunidad = idOportunidad }, cancellationToken: token));
            if (item is null)
                return null;

            await HydrateEtiquetasAsync(cn, new[] { item }, token);
            item.Actividad = (await GetActivityAsync(cn, idOportunidad, token)).ToList();
            item.MensajesOrigenDetalle = (await GetSourceMessagesAsync(cn, idOportunidad, token)).ToList();
            item.Tareas = (await GetTareasAsync(cn, idOportunidad, token)).ToList();
            return item;
        }, "No se pudo cargar el detalle de la oportunidad.", ct);

    public Task<long> SaveOpportunityAsync(CrmOpportunitySaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveOpportunity", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            NormalizeOpportunity(request);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var idEtapa = request.IdEtapa.GetValueOrDefault();
                if (idEtapa <= 0)
                    idEtapa = await GetDefaultStageIdAsync(cn, tx, token);

                // Motivo de pérdida: obligatorio si la etapa destino es "perdida"; se limpia si no lo es.
                var esPerdida = await IsStageLostAsync(cn, tx, idEtapa, token);
                int? idMotivoPerdida = esPerdida ? (request.IdMotivoPerdida is > 0 ? request.IdMotivoPerdida : null) : null;
                if (esPerdida && idMotivoPerdida is null)
                    throw new InvalidOperationException("Elegí el motivo de pérdida antes de marcar la oportunidad como perdida.");

                var isNew = request.IdOportunidad <= 0;
                var id = request.IdOportunidad;
                if (isNew)
                {
                    var numero = await GetNextNumberAsync(cn, tx, token);
                    id = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                        INSERT INTO dbo.CRM_OPORTUNIDADES
                        (
                            Numero, Titulo, Descripcion, IdEtapa, Prioridad, Probabilidad, ImporteEstimado,
                            FechaCierreEstimada, IdTecnico, ClienteCodigo, IdContacto, CanalOrigen,
                            IdConversacion, IdMotivoPerdida, UsuarioAlta, FechaHoraAlta,
                            FechaHoraCierre
                        )
                        OUTPUT INSERTED.IdOportunidad
                        VALUES
                        (
                            @Numero, @Titulo, @Descripcion, @IdEtapa, @Prioridad, @Probabilidad, @ImporteEstimado,
                            @FechaCierreEstimada, @IdTecnico, @ClienteCodigo, @IdContacto, @CanalOrigen,
                            @IdConversacion, @IdMotivoPerdida, @UsuarioAlta, GETDATE(),
                            CASE WHEN EXISTS (SELECT 1 FROM dbo.CRM_ETAPAS e WHERE e.IdEtapa = @IdEtapa AND (ISNULL(e.EsGanada, 0) = 1 OR ISNULL(e.EsPerdida, 0) = 1)) THEN GETDATE() ELSE NULL END
                        );
                        """, new
                        {
                            Numero = numero,
                            request.Titulo,
                            request.Descripcion,
                            IdEtapa = idEtapa,
                            request.Prioridad,
                            request.Probabilidad,
                            request.ImporteEstimado,
                            request.FechaCierreEstimada,
                            IdTecnico = EmptyToNull(request.IdTecnico),
                            ClienteCodigo = EmptyToNull(request.ClienteCodigo),
                            request.IdContacto,
                            CanalOrigen = EmptyToNull(request.CanalOrigen),
                            request.IdConversacion,
                            IdMotivoPerdida = idMotivoPerdida,
                            UsuarioAlta = NormalizeUser(request.UsuarioAccion)
                        }, tx, cancellationToken: token));

                    await AddActivityAsync(cn, tx, id, "ALTA", "Oportunidad creada.", request.UsuarioAccion, token);
                }
                else
                {
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.CRM_OPORTUNIDADES
                        SET Titulo = @Titulo,
                            Descripcion = @Descripcion,
                            IdEtapa = @IdEtapa,
                            Prioridad = @Prioridad,
                            Probabilidad = @Probabilidad,
                            ImporteEstimado = @ImporteEstimado,
                            FechaCierreEstimada = @FechaCierreEstimada,
                            IdTecnico = @IdTecnico,
                            ClienteCodigo = @ClienteCodigo,
                            IdContacto = @IdContacto,
                            CanalOrigen = @CanalOrigen,
                            IdConversacion = @IdConversacion,
                            IdMotivoPerdida = @IdMotivoPerdida,
                            FechaHoraModificacion = GETDATE(),
                            FechaHoraCierre = CASE
                                WHEN EXISTS (SELECT 1 FROM dbo.CRM_ETAPAS e WHERE e.IdEtapa = @IdEtapa AND (ISNULL(e.EsGanada, 0) = 1 OR ISNULL(e.EsPerdida, 0) = 1))
                                THEN COALESCE(FechaHoraCierre, GETDATE())
                                ELSE NULL
                            END
                        WHERE IdOportunidad = @IdOportunidad
                          AND ISNULL(Baja, 0) = 0;
                        """, new
                        {
                            request.IdOportunidad,
                            request.Titulo,
                            request.Descripcion,
                            IdEtapa = idEtapa,
                            request.Prioridad,
                            request.Probabilidad,
                            request.ImporteEstimado,
                            request.FechaCierreEstimada,
                            IdTecnico = EmptyToNull(request.IdTecnico),
                            ClienteCodigo = EmptyToNull(request.ClienteCodigo),
                            request.IdContacto,
                            CanalOrigen = EmptyToNull(request.CanalOrigen),
                            request.IdConversacion,
                            IdMotivoPerdida = idMotivoPerdida
                        }, tx, cancellationToken: token));

                    await AddActivityAsync(cn, tx, id, "EDICION", "Oportunidad actualizada.", request.UsuarioAccion, token);
                }

                await ReplaceTagsAsync(cn, tx, id, request.IdEtiquetas, token);
                await ReplaceSourceMessagesAsync(cn, tx, id, request.IdMensajes, token);

                await tx.CommitAsync(token);
                return id;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar la oportunidad.", ct);

    public Task QuickUpdateAsync(CrmQuickUpdateRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("QuickUpdate", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdOportunidad <= 0)
                throw new InvalidOperationException("Seleccioná una oportunidad válida.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                // Si se mueve a una etapa "perdida", el motivo es obligatorio; al salir de ella se limpia.
                bool actualizarMotivo = request.IdEtapa is > 0;
                int? idMotivoPerdida = null;
                if (request.IdEtapa is > 0)
                {
                    var esPerdida = await IsStageLostAsync(cn, tx, request.IdEtapa.Value, token);
                    if (esPerdida)
                    {
                        idMotivoPerdida = request.IdMotivoPerdida is > 0 ? request.IdMotivoPerdida : null;
                        if (idMotivoPerdida is null)
                            throw new InvalidOperationException("Elegí el motivo de pérdida antes de marcar la oportunidad como perdida.");
                    }
                }

                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.CRM_OPORTUNIDADES
                    SET IdEtapa = COALESCE(@IdEtapa, IdEtapa),
                        IdTecnico = CASE WHEN @ActualizarTecnico = 1 THEN @IdTecnico ELSE IdTecnico END,
                        Prioridad = COALESCE(@Prioridad, Prioridad),
                        Probabilidad = COALESCE(@Probabilidad, Probabilidad),
                        IdMotivoPerdida = CASE WHEN @ActualizarMotivo = 1 THEN @IdMotivoPerdida ELSE IdMotivoPerdida END,
                        FechaHoraModificacion = GETDATE(),
                        FechaHoraCierre = CASE
                            WHEN @IdEtapa IS NOT NULL
                             AND EXISTS (SELECT 1 FROM dbo.CRM_ETAPAS e WHERE e.IdEtapa = @IdEtapa AND (ISNULL(e.EsGanada, 0) = 1 OR ISNULL(e.EsPerdida, 0) = 1))
                            THEN COALESCE(FechaHoraCierre, GETDATE())
                            WHEN @IdEtapa IS NOT NULL THEN NULL
                            ELSE FechaHoraCierre
                        END
                    WHERE IdOportunidad = @IdOportunidad
                      AND ISNULL(Baja, 0) = 0;
                    """, new
                    {
                        request.IdOportunidad,
                        request.IdEtapa,
                        ActualizarTecnico = request.IdTecnico is not null,
                        IdTecnico = EmptyToNull(request.IdTecnico),
                        request.Prioridad,
                        request.Probabilidad,
                        ActualizarMotivo = actualizarMotivo,
                        IdMotivoPerdida = idMotivoPerdida
                    }, tx, cancellationToken: token));

                await AddActivityAsync(cn, tx, request.IdOportunidad, "MOVIMIENTO", "Oportunidad actualizada desde acción rápida.", request.UsuarioAccion, token);
                await tx.CommitAsync(token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo actualizar la oportunidad.", ct);

    public Task AddNoteAsync(CrmNotaRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("AddNote", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var text = NormalizeText(request.Texto, 4000, "La nota es obligatoria.");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await AddActivityAsync(cn, null, request.IdOportunidad, "NOTA", text, request.UsuarioAccion, token);
        }, "No se pudo agregar la nota.", ct);

    public Task<long> SaveTareaAsync(CrmTareaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveTarea", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdOportunidad <= 0)
                throw new InvalidOperationException("La oportunidad es obligatoria.");
            var titulo = NormalizeText(request.Titulo, 200, "El título de la acción es obligatorio.");
            var detalle = string.IsNullOrWhiteSpace(request.Detalle) ? null : request.Detalle.Trim();
            var tipo = NormalizeTareaTipo(request.TipoAccion);
            if (request.Vencimiento == default)
                throw new InvalidOperationException("La fecha de vencimiento es obligatoria.");
            var idTecnico = string.IsNullOrWhiteSpace(request.IdTecnico) ? null : request.IdTecnico.Trim();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                long id;
                if (request.IdTarea > 0)
                {
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.CRM_TAREAS
                        SET TipoAccion = @TipoAccion,
                            Titulo = @Titulo,
                            Detalle = @Detalle,
                            Vencimiento = @Vencimiento,
                            IdTecnico = @IdTecnico,
                            FechaHoraModificacion = GETDATE()
                        WHERE IdTarea = @IdTarea AND ISNULL(Baja, 0) = 0;
                        """, new { request.IdTarea, TipoAccion = tipo, Titulo = titulo, Detalle = detalle, request.Vencimiento, IdTecnico = idTecnico }, tx, cancellationToken: token));
                    id = request.IdTarea;
                    await AddActivityAsync(cn, tx, request.IdOportunidad, "TAREA", $"Acción actualizada: {titulo}", request.UsuarioAccion, token);
                }
                else
                {
                    id = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                        INSERT INTO dbo.CRM_TAREAS (IdOportunidad, TipoAccion, Titulo, Detalle, Vencimiento, IdTecnico, UsuarioAlta)
                        OUTPUT INSERTED.IdTarea
                        VALUES (@IdOportunidad, @TipoAccion, @Titulo, @Detalle, @Vencimiento, @IdTecnico, @UsuarioAlta);
                        """, new { request.IdOportunidad, TipoAccion = tipo, Titulo = titulo, Detalle = detalle, request.Vencimiento, IdTecnico = idTecnico, UsuarioAlta = NormalizeUser(request.UsuarioAccion) }, tx, cancellationToken: token));
                    await AddActivityAsync(cn, tx, request.IdOportunidad, "TAREA", $"Próxima acción agendada: {titulo}", request.UsuarioAccion, token);
                }

                await tx.CommitAsync(token);
                return id;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar la acción.", ct);

    public Task CompleteTareaAsync(long idTarea, bool completada, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("CompleteTarea", async token =>
        {
            if (idTarea <= 0)
                throw new InvalidOperationException("Seleccioná una acción válida.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var info = await cn.QueryFirstOrDefaultAsync(new CommandDefinition(
                    "SELECT IdOportunidad, ISNULL(Titulo, '') AS Titulo FROM dbo.CRM_TAREAS WHERE IdTarea = @IdTarea AND ISNULL(Baja, 0) = 0;",
                    new { IdTarea = idTarea }, tx, cancellationToken: token));
                if (info is null)
                    throw new InvalidOperationException("La acción indicada no existe.");
                long idOportunidad = (long)info.IdOportunidad;
                string tituloTarea = (string)info.Titulo;

                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.CRM_TAREAS
                    SET Completada = @Completada,
                        FechaHoraCompletada = CASE WHEN @Completada = 1 THEN GETDATE() ELSE NULL END,
                        UsuarioCompleta = CASE WHEN @Completada = 1 THEN @Usuario ELSE NULL END,
                        FechaHoraModificacion = GETDATE()
                    WHERE IdTarea = @IdTarea;
                    """, new { IdTarea = idTarea, Completada = completada, Usuario = NormalizeUser(usuarioAccion) }, tx, cancellationToken: token));

                if (completada)
                    await AddActivityAsync(cn, tx, idOportunidad, "TAREA", $"Acción completada: {tituloTarea}", usuarioAccion, token);

                await tx.CommitAsync(token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo actualizar la acción.", ct);

    public Task DeleteTareaAsync(long idTarea, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("DeleteTarea", async token =>
        {
            if (idTarea <= 0)
                throw new InvalidOperationException("Seleccioná una acción válida.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.CRM_TAREAS
                SET Baja = 1, FechaHoraModificacion = GETDATE()
                WHERE IdTarea = @IdTarea;
                """, new { IdTarea = idTarea }, cancellationToken: token));
        }, "No se pudo eliminar la acción.", ct);

    public Task<CrmDashboardDto> GetDashboardAsync(int diasEstancamiento = 7, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetDashboard", async token =>
        {
            var dias = Math.Clamp(diasEstancamiento, 1, 365);
            const string sql = """
                -- Pipeline por etapa abierta (bruto y ponderado importe*probabilidad)
                SELECT
                    e.IdEtapa,
                    ISNULL(e.Nombre, '') AS Nombre,
                    ISNULL(e.Color, '') AS Color,
                    COUNT(o.IdOportunidad) AS Cantidad,
                    ISNULL(SUM(o.ImporteEstimado), 0) AS ImporteBruto,
                    ISNULL(SUM(o.ImporteEstimado * o.Probabilidad / 100.0), 0) AS ImportePonderado
                FROM dbo.CRM_ETAPAS e
                LEFT JOIN dbo.CRM_OPORTUNIDADES o
                    ON o.IdEtapa = e.IdEtapa AND ISNULL(o.Baja, 0) = 0
                WHERE ISNULL(e.Activa, 1) = 1 AND ISNULL(e.EsGanada, 0) = 0 AND ISNULL(e.EsPerdida, 0) = 0
                GROUP BY e.IdEtapa, e.Nombre, e.Color, e.Orden
                ORDER BY e.Orden, e.IdEtapa;

                -- Cerradas (ganadas / perdidas) para la tasa de conversión
                SELECT
                    ISNULL(SUM(CASE WHEN ISNULL(e.EsGanada, 0) = 1 THEN 1 ELSE 0 END), 0) AS Ganadas,
                    ISNULL(SUM(CASE WHEN ISNULL(e.EsGanada, 0) = 1 THEN o.ImporteEstimado ELSE 0 END), 0) AS ImporteGanado,
                    ISNULL(SUM(CASE WHEN ISNULL(e.EsPerdida, 0) = 1 THEN 1 ELSE 0 END), 0) AS Perdidas
                FROM dbo.CRM_OPORTUNIDADES o
                INNER JOIN dbo.CRM_ETAPAS e ON e.IdEtapa = o.IdEtapa
                WHERE ISNULL(o.Baja, 0) = 0 AND (ISNULL(e.EsGanada, 0) = 1 OR ISNULL(e.EsPerdida, 0) = 1);

                -- Ranking por técnico (abiertas)
                SELECT
                    LTRIM(RTRIM(ISNULL(o.IdTecnico, ''))) AS IdTecnico,
                    ISNULL(t.Nombre, '') AS Nombre,
                    COUNT(*) AS Abiertas,
                    ISNULL(SUM(o.ImporteEstimado), 0) AS ImporteBruto,
                    ISNULL(SUM(o.ImporteEstimado * o.Probabilidad / 100.0), 0) AS ImportePonderado
                FROM dbo.CRM_OPORTUNIDADES o
                INNER JOIN dbo.CRM_ETAPAS e ON e.IdEtapa = o.IdEtapa
                LEFT JOIN dbo.V_TA_Tecnicos t ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(o.IdTecnico))
                WHERE ISNULL(o.Baja, 0) = 0 AND ISNULL(e.EsGanada, 0) = 0 AND ISNULL(e.EsPerdida, 0) = 0
                GROUP BY LTRIM(RTRIM(ISNULL(o.IdTecnico, ''))), t.Nombre
                ORDER BY ImportePonderado DESC, Abiertas DESC;

                -- Estancadas: abiertas sin actividad en más de @Dias días
                SELECT COUNT(*)
                FROM dbo.CRM_OPORTUNIDADES o
                INNER JOIN dbo.CRM_ETAPAS e ON e.IdEtapa = o.IdEtapa
                WHERE ISNULL(o.Baja, 0) = 0 AND ISNULL(e.EsGanada, 0) = 0 AND ISNULL(e.EsPerdida, 0) = 0
                  AND ISNULL((SELECT MAX(a.FechaHora) FROM dbo.CRM_ACTIVIDAD a WHERE a.IdOportunidad = o.IdOportunidad), o.FechaHoraAlta)
                      < DATEADD(day, -@Dias, GETDATE());

                -- Motivos de las oportunidades perdidas
                SELECT
                    COALESCE(NULLIF(mot.Nombre, ''), N'Sin motivo') AS Nombre,
                    COUNT(*) AS Cantidad
                FROM dbo.CRM_OPORTUNIDADES o
                INNER JOIN dbo.CRM_ETAPAS e ON e.IdEtapa = o.IdEtapa
                LEFT JOIN dbo.CRM_MOTIVOS_PERDIDA mot ON mot.IdMotivo = o.IdMotivoPerdida
                WHERE ISNULL(o.Baja, 0) = 0 AND ISNULL(e.EsPerdida, 0) = 1
                GROUP BY COALESCE(NULLIF(mot.Nombre, ''), N'Sin motivo')
                ORDER BY Cantidad DESC;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            using var grid = await cn.QueryMultipleAsync(new CommandDefinition(sql, new { Dias = dias }, cancellationToken: token));

            var porEtapa = (await grid.ReadAsync<CrmDashboardEtapaDto>()).AsList();
            var cerradas = await grid.ReadFirstOrDefaultAsync();
            var porTecnico = (await grid.ReadAsync<CrmDashboardTecnicoDto>()).AsList();
            var estancadas = await grid.ReadFirstOrDefaultAsync<int>();
            var motivosPerdida = (await grid.ReadAsync<CrmDashboardMotivoDto>()).AsList();

            int ganadas = cerradas is null ? 0 : (int)cerradas.Ganadas;
            decimal importeGanado = cerradas is null ? 0m : (decimal)cerradas.ImporteGanado;
            int perdidas = cerradas is null ? 0 : (int)cerradas.Perdidas;
            var cerradasTotal = ganadas + perdidas;

            return new CrmDashboardDto
            {
                PorEtapa = porEtapa,
                PorTecnico = porTecnico,
                TotalAbiertas = porEtapa.Sum(x => x.Cantidad),
                ImporteBruto = porEtapa.Sum(x => x.ImporteBruto),
                ImportePonderado = porEtapa.Sum(x => x.ImportePonderado),
                Ganadas = ganadas,
                ImporteGanado = importeGanado,
                Perdidas = perdidas,
                Estancadas = estancadas,
                DiasEstancamiento = dias,
                TasaConversion = cerradasTotal == 0 ? 0 : (double)ganadas / cerradasTotal,
                MotivosPerdida = motivosPerdida
            };
        }, "No se pudo cargar el tablero del CRM.", ct);

    public Task<CrmConversationPrefillDto?> GetConversationPrefillAsync(long idConversacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetConversationPrefill", async token =>
        {
            if (idConversacion <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // Guardado con OBJECT_ID: el CRM no depende físicamente de CONV_* (puede correr en una
            // base sin Conversaciones); si no existe la tabla, simplemente no hay precarga.
            var hasConv = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL THEN 0 ELSE 1 END;",
                cancellationToken: token)) == 1;
            if (!hasConv)
                return null;

            return await cn.QueryFirstOrDefaultAsync<CrmConversationPrefillDto>(new CommandDefinition("""
                SELECT TOP (1)
                    c.IdConversacion,
                    COALESCE(NULLIF(cli.RAZON_SOCIAL, ''), NULLIF(mc.Nombre_y_Apellido, ''), NULLIF(c.NombreVisible, ''), N'Oportunidad') AS TituloSugerido,
                    ISNULL(c.ClienteCodigo, '') AS ClienteCodigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
                    c.IdContacto,
                    ISNULL(mc.Nombre_y_Apellido, '') AS ContactoNombre,
                    ISNULL(c.Canal, '') AS CanalOrigen
                FROM dbo.CONV_CONVERSACIONES c
                LEFT JOIN dbo.VT_CLIENTES cli ON LTRIM(RTRIM(cli.Codigo)) = LTRIM(RTRIM(c.ClienteCodigo))
                LEFT JOIN dbo.MA_CONTACTOS mc ON mc.id = c.IdContacto
                WHERE c.IdConversacion = @IdConversacion;
                """, new { IdConversacion = idConversacion }, cancellationToken: token));
        }, "No se pudo leer la conversación de origen.", ct);

    public Task<ConversacionExtraccionDto?> ExtractOportunidadDesdeConversacionAsync(long idConversacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("ExtractOportunidadDesdeConversacion", async token =>
        {
            if (idConversacion <= 0 || !analisisService.IsConfigured)
                return (ConversacionExtraccionDto?)null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var hasMsg = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL THEN 0 ELSE 1 END;",
                cancellationToken: token)) == 1;
            if (!hasMsg)
                return null;

            var mensajes = (await cn.QueryAsync<ConversacionMensajeDto>(new CommandDefinition("""
                SELECT TOP (60)
                    ISNULL(Direction, '') AS Direction,
                    ISNULL(CAST(Texto AS nvarchar(max)), '') AS Texto,
                    FechaHora
                FROM dbo.CONV_MENSAJES
                WHERE IdConversacion = @IdConversacion
                  AND Direction IN (N'ENTRANTE', N'SALIENTE')
                  AND ISNULL(CAST(Texto AS nvarchar(max)), '') <> ''
                ORDER BY FechaHora DESC;
                """, new { IdConversacion = idConversacion }, cancellationToken: token))).AsList();

            if (mensajes.Count == 0)
                return null;

            return await analisisService.ExtraerOportunidadAsync(mensajes, token);
        }, "No se pudo extraer los datos de la conversación.", ct);

    public Task<int> SaveEtapaAsync(CrmEtapaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveEtapa", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var name = NormalizeText(request.Nombre, 80, "El nombre de la etapa es obligatorio.");
            var color = NormalizeColor(request.Color, "#2563EB");
            if (request.EsGanada && request.EsPerdida)
                throw new InvalidOperationException("Una etapa no puede ser ganada y perdida al mismo tiempo.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (request.IdEtapa > 0)
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.CRM_ETAPAS
                    SET Nombre = @Nombre,
                        Color = @Color,
                        EsGanada = @EsGanada,
                        EsPerdida = @EsPerdida,
                        Orden = @Orden,
                        Activa = 1,
                        FechaHora_Modificacion = GETDATE()
                    WHERE IdEtapa = @IdEtapa;
                    """, new { request.IdEtapa, Nombre = name, Color = color, request.EsGanada, request.EsPerdida, request.Orden }, cancellationToken: token));
                return request.IdEtapa;
            }

            return await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO dbo.CRM_ETAPAS (Nombre, Color, EsGanada, EsPerdida, Orden, Activa)
                OUTPUT INSERTED.IdEtapa
                VALUES (@Nombre, @Color, @EsGanada, @EsPerdida, @Orden, 1);
                """, new { Nombre = name, Color = color, request.EsGanada, request.EsPerdida, request.Orden }, cancellationToken: token));
        }, "No se pudo guardar la etapa.", ct);

    public Task DeleteEtapaAsync(int idEtapa, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("DeleteEtapa", async token =>
        {
            if (idEtapa <= 0)
                throw new InvalidOperationException("Seleccioná una etapa válida.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var activeCount = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.CRM_ETAPAS WHERE ISNULL(Activa, 1) = 1;",
                cancellationToken: token));
            if (activeCount <= 1)
                throw new InvalidOperationException("No se puede eliminar la última etapa activa del CRM.");

            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.CRM_ETAPAS
                SET Activa = 0,
                    FechaHora_Modificacion = GETDATE()
                WHERE IdEtapa = @IdEtapa;
                """, new { IdEtapa = idEtapa }, cancellationToken: token));
        }, "No se pudo eliminar la etapa.", ct);

    public Task<int> SaveEtiquetaAsync(CrmEtiquetaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveEtiqueta", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var name = NormalizeText(request.Nombre, 60, "El nombre de la etiqueta es obligatorio.");
            var color = NormalizeColor(request.Color, "#14B8A6");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (request.IdEtiqueta > 0)
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.CRM_ETIQUETAS
                    SET Nombre = @Nombre,
                        Color = @Color,
                        Activa = 1
                    WHERE IdEtiqueta = @IdEtiqueta;
                    """, new { request.IdEtiqueta, Nombre = name, Color = color }, cancellationToken: token));
                return request.IdEtiqueta;
            }

            return await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO dbo.CRM_ETIQUETAS (Nombre, Color, Activa)
                OUTPUT INSERTED.IdEtiqueta
                VALUES (@Nombre, @Color, 1);
                """, new { Nombre = name, Color = color }, cancellationToken: token));
        }, "No se pudo guardar la etiqueta.", ct);

    public Task DeleteEtiquetaAsync(int idEtiqueta, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("DeleteEtiqueta", async token =>
        {
            if (idEtiqueta <= 0)
                throw new InvalidOperationException("Seleccioná una etiqueta válida.");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.CRM_ETIQUETAS SET Activa = 0 WHERE IdEtiqueta = @IdEtiqueta;",
                new { IdEtiqueta = idEtiqueta },
                cancellationToken: token));
        }, "No se pudo eliminar la etiqueta.", ct);

    public Task<int> SaveMotivoPerdidaAsync(CrmMotivoPerdidaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveMotivoPerdida", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var name = NormalizeText(request.Nombre, 100, "El nombre del motivo es obligatorio.");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (request.IdMotivo > 0)
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.CRM_MOTIVOS_PERDIDA
                    SET Nombre = @Nombre,
                        Orden = @Orden,
                        Activo = 1
                    WHERE IdMotivo = @IdMotivo;
                    """, new { request.IdMotivo, Nombre = name, request.Orden }, cancellationToken: token));
                return request.IdMotivo;
            }

            return await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO dbo.CRM_MOTIVOS_PERDIDA (Nombre, Orden, Activo)
                OUTPUT INSERTED.IdMotivo
                VALUES (@Nombre, @Orden, 1);
                """, new { Nombre = name, request.Orden }, cancellationToken: token));
        }, "No se pudo guardar el motivo de pérdida.", ct);

    public Task DeleteMotivoPerdidaAsync(int idMotivo, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("DeleteMotivoPerdida", async token =>
        {
            if (idMotivo <= 0)
                throw new InvalidOperationException("Seleccioná un motivo válido.");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.CRM_MOTIVOS_PERDIDA SET Activo = 0 WHERE IdMotivo = @IdMotivo;",
                new { IdMotivo = idMotivo },
                cancellationToken: token));
        }, "No se pudo eliminar el motivo de pérdida.", ct);

    public Task<CrmViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetViewSettings", async token =>
        {
            var defaults = BuildDefaultViewSettings();
            if (string.IsNullOrWhiteSpace(userName))
                return defaults;

            await using var cn = new SqlConnection(ConnectionString);
            var raw = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
                SELECT TOP (1) COALESCE(NULLIF(CONVERT(nvarchar(max), VALOR), N''), CONVERT(nvarchar(max), ValorAux))
                FROM dbo.TA_CONFIGURACION
                WHERE CLAVE = @Clave;
                """, new { Clave = BuildViewConfigKey(userName) }, cancellationToken: token));

            if (string.IsNullOrWhiteSpace(raw))
                return defaults;

            var saved = JsonSerializer.Deserialize<CrmViewSettingsDto>(raw, JsonOptions);
            return MergeViewSettings(saved, defaults);
        }, "No se pudo cargar la configuración de vista de CRM.", ct);

    public Task SaveViewSettingsAsync(string userName, CrmViewSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return;

            var normalized = MergeViewSettings(settings, BuildDefaultViewSettings());
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition("""
                IF EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE = @Clave)
                BEGIN
                    UPDATE dbo.TA_CONFIGURACION
                    SET GRUPO = @Grupo,
                        VALOR = CASE WHEN LEN(@Json) <= 150 THEN @Json ELSE N'' END,
                        ValorAux = CASE WHEN LEN(@Json) > 150 THEN @Json ELSE NULL END,
                        DESCRIPCION = N'Vista CRM',
                        FechaHora_Modificacion = GETDATE()
                    WHERE CLAVE = @Clave;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, ValorAux, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
                    VALUES (@Grupo, @Clave, CASE WHEN LEN(@Json) <= 150 THEN @Json ELSE N'' END, CASE WHEN LEN(@Json) > 150 THEN @Json ELSE NULL END, N'Vista CRM', GETDATE(), GETDATE());
                END
                """, new { Grupo = ConfigGroup, Clave = BuildViewConfigKey(userName), Json = json }, cancellationToken: token));
        }, "No se pudo guardar la configuración de vista de CRM.", ct);

    private static string OpportunitySelectSql() => """
        SELECT
            o.IdOportunidad,
            o.Numero,
            RIGHT('0000' + CONVERT(varchar(10), o.Numero), 4) AS CodigoVisible,
            ISNULL(o.Titulo, '') AS Titulo,
            ISNULL(CAST(o.Descripcion AS nvarchar(max)), '') AS Descripcion,
            o.IdEtapa,
            ISNULL(e.Nombre, '') AS EtapaNombre,
            ISNULL(e.Color, '') AS EtapaColor,
            ISNULL(e.Orden, 0) AS EtapaOrden,
            ISNULL(e.EsGanada, 0) AS EtapaEsGanada,
            ISNULL(e.EsPerdida, 0) AS EtapaEsPerdida,
            ISNULL(o.Prioridad, 0) AS Prioridad,
            ISNULL(o.Probabilidad, 0) AS Probabilidad,
            ISNULL(o.ImporteEstimado, 0) AS ImporteEstimado,
            o.FechaCierreEstimada,
            ISNULL(o.IdTecnico, '') AS IdTecnico,
            ISNULL(tec.Nombre, '') AS TecnicoNombre,
            ISNULL(o.ClienteCodigo, '') AS ClienteCodigo,
            ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
            o.IdContacto,
            ISNULL(mc.Nombre_y_Apellido, '') AS ContactoNombre,
            ISNULL(o.CanalOrigen, '') AS CanalOrigen,
            o.IdConversacion,
            ISNULL(msg.CantidadMensajes, 0) AS MensajesOrigen,
            o.IdMotivoPerdida,
            ISNULL(mot.Nombre, '') AS MotivoPerdidaNombre,
            ISNULL(o.UsuarioAlta, '') AS UsuarioAlta,
            o.FechaHoraAlta,
            o.FechaHoraModificacion,
            o.FechaHoraCierre,
            prox.IdTarea AS ProximaAccionId,
            ISNULL(prox.TipoAccion, '') AS ProximaAccionTipo,
            ISNULL(prox.Titulo, '') AS ProximaAccionTitulo,
            prox.Vencimiento AS ProximaAccionVencimiento,
            CASE WHEN prox.Vencimiento IS NOT NULL AND prox.Vencimiento < GETDATE() THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS ProximaAccionVencida
        """;

    private static string OpportunityFromSql() => """
        FROM dbo.CRM_OPORTUNIDADES o
        INNER JOIN dbo.CRM_ETAPAS e ON e.IdEtapa = o.IdEtapa
        LEFT JOIN dbo.V_TA_Tecnicos tec ON LTRIM(RTRIM(tec.IdTecnico)) = LTRIM(RTRIM(o.IdTecnico))
        LEFT JOIN dbo.VT_CLIENTES cli ON LTRIM(RTRIM(cli.Codigo)) = LTRIM(RTRIM(o.ClienteCodigo))
        LEFT JOIN dbo.MA_CONTACTOS mc ON mc.id = o.IdContacto
        LEFT JOIN dbo.CRM_MOTIVOS_PERDIDA mot ON mot.IdMotivo = o.IdMotivoPerdida
        OUTER APPLY (
            SELECT COUNT(*) AS CantidadMensajes
            FROM dbo.CRM_OPORTUNIDAD_MENSAJES om
            WHERE om.IdOportunidad = o.IdOportunidad
        ) msg
        OUTER APPLY (
            SELECT TOP (1) t.IdTarea, t.TipoAccion, t.Titulo, t.Vencimiento
            FROM dbo.CRM_TAREAS t
            WHERE t.IdOportunidad = o.IdOportunidad
              AND ISNULL(t.Completada, 0) = 0
              AND ISNULL(t.Baja, 0) = 0
            ORDER BY t.Vencimiento ASC, t.IdTarea ASC
        ) prox
        """;

    private static string OpportunityWhereSql() => """
        WHERE ISNULL(o.Baja, 0) = 0
          AND (@IncluirCerradas = 1 OR (ISNULL(e.EsGanada, 0) = 0 AND ISNULL(e.EsPerdida, 0) = 0))
          AND (@IdEtapa IS NULL OR o.IdEtapa = @IdEtapa)
          AND (@SoloSinAsignar = 0 OR LTRIM(RTRIM(ISNULL(o.IdTecnico, ''))) = '')
          AND (@IdTecnico IS NULL OR LTRIM(RTRIM(ISNULL(o.IdTecnico, ''))) = LTRIM(RTRIM(@IdTecnico)))
          AND (@ClienteCodigo IS NULL OR LTRIM(RTRIM(ISNULL(o.ClienteCodigo, ''))) = LTRIM(RTRIM(@ClienteCodigo)))
          AND (@IdEtiqueta IS NULL OR EXISTS (
                SELECT 1 FROM dbo.CRM_OPORTUNIDAD_ETIQUETAS oe
                WHERE oe.IdOportunidad = o.IdOportunidad AND oe.IdEtiqueta = @IdEtiqueta
          ))
          AND (
                @Texto IS NULL
                OR ISNULL(o.Titulo, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                OR ISNULL(CAST(o.Descripcion AS nvarchar(max)), '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                OR ISNULL(cli.RAZON_SOCIAL, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                OR ISNULL(mc.Nombre_y_Apellido, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
          )
        """;

    private static DynamicParameters BuildFilterParameters(CrmFilters filters)
    {
        var p = new DynamicParameters();
        var text = string.IsNullOrWhiteSpace(filters.Texto) ? null : filters.Texto.Trim();
        p.Add("Texto", text);
        p.Add("TextoLike", text is null ? null : $"%{text}%");
        p.Add("IdEtapa", filters.IdEtapa);
        p.Add("IdTecnico", string.IsNullOrWhiteSpace(filters.IdTecnico) ? null : filters.IdTecnico.Trim());
        p.Add("SoloSinAsignar", filters.SoloSinAsignar);
        p.Add("ClienteCodigo", string.IsNullOrWhiteSpace(filters.ClienteCodigo) ? null : filters.ClienteCodigo.Trim());
        p.Add("IdEtiqueta", filters.IdEtiqueta);
        p.Add("IncluirCerradas", filters.IncluirCerradas);
        return p;
    }

    private static string BuildOrderBy(string? columnKey, bool descending)
    {
        var expression = columnKey switch
        {
            CrmViewColumnKeys.Numero => "o.Numero",
            CrmViewColumnKeys.Titulo => "ISNULL(o.Titulo, '')",
            CrmViewColumnKeys.Etapa => "ISNULL(e.Orden, 0)",
            CrmViewColumnKeys.Importe => "ISNULL(o.ImporteEstimado, 0)",
            CrmViewColumnKeys.Probabilidad => "ISNULL(o.Probabilidad, 0)",
            CrmViewColumnKeys.Asignado => "ISNULL(tec.Nombre, '')",
            CrmViewColumnKeys.Cliente => "ISNULL(cli.RAZON_SOCIAL, '')",
            CrmViewColumnKeys.Contacto => "ISNULL(mc.Nombre_y_Apellido, '')",
            _ => "o.FechaHoraAlta"
        };
        return $"{expression} {(descending ? "DESC" : "ASC")}";
    }

    private static async Task HydrateEtiquetasAsync(SqlConnection cn, IReadOnlyList<CrmOpportunityDto> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        var ids = rows.Select(x => x.IdOportunidad).Distinct().ToArray();
        var etiquetas = await cn.QueryAsync<(long IdOportunidad, int IdEtiqueta, string Nombre, string Color)>(
            new CommandDefinition("""
                SELECT oe.IdOportunidad, e.IdEtiqueta, e.Nombre, ISNULL(e.Color, '')
                FROM dbo.CRM_OPORTUNIDAD_ETIQUETAS oe
                INNER JOIN dbo.CRM_ETIQUETAS e ON e.IdEtiqueta = oe.IdEtiqueta
                WHERE oe.IdOportunidad IN @Ids
                  AND ISNULL(e.Activa, 1) = 1
                ORDER BY e.Nombre;
                """, new { Ids = ids }, cancellationToken: ct));

        var byId = rows.ToDictionary(x => x.IdOportunidad);
        foreach (var item in etiquetas)
        {
            if (byId.TryGetValue(item.IdOportunidad, out var row))
                row.Etiquetas.Add(new CrmEtiquetaDto { IdEtiqueta = item.IdEtiqueta, Nombre = item.Nombre, Color = item.Color });
        }
    }

    private static Task<IEnumerable<CrmActividadDto>> GetActivityAsync(SqlConnection cn, long idOportunidad, CancellationToken ct)
        => cn.QueryAsync<CrmActividadDto>(new CommandDefinition("""
            SELECT IdActividad, TipoActividad, ISNULL(Descripcion, '') AS Descripcion, ISNULL(Usuario, '') AS Usuario, FechaHora
            FROM dbo.CRM_ACTIVIDAD
            WHERE IdOportunidad = @IdOportunidad
            ORDER BY FechaHora DESC, IdActividad DESC;
            """, new { IdOportunidad = idOportunidad }, cancellationToken: ct));

    private static async Task<IEnumerable<CrmMensajeOrigenDto>> GetSourceMessagesAsync(SqlConnection cn, long idOportunidad, CancellationToken ct)
    {
        var hasConvMessages = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL THEN 0 ELSE 1 END;",
            cancellationToken: ct)) == 1;
        if (!hasConvMessages)
            return [];

        return await cn.QueryAsync<CrmMensajeOrigenDto>(new CommandDefinition("""
            SELECT
                m.IdMensaje,
                m.IdConversacion,
                CASE WHEN ISNULL(m.Direction, '') = 'ENTRANTE' THEN N'Cliente / Contacto' ELSE COALESCE(NULLIF(t.Nombre, ''), NULLIF(m.UsuarioAutor, ''), N'Equipo Alfa') END AS Autor,
                ISNULL(m.Direction, '') AS Direccion,
                ISNULL(m.MessageType, '') AS TipoMensaje,
                ISNULL(m.Texto, '') AS Texto,
                m.FechaHora
            FROM dbo.CRM_OPORTUNIDAD_MENSAJES om
            INNER JOIN dbo.CONV_MENSAJES m ON m.IdMensaje = om.IdMensaje
            LEFT JOIN dbo.V_TA_Tecnicos t ON t.IdTecnico = m.IdTecnicoAutor
            WHERE om.IdOportunidad = @IdOportunidad
            ORDER BY om.Orden, m.FechaHora, m.IdMensaje;
            """, new { IdOportunidad = idOportunidad }, cancellationToken: ct));
    }

    private static async Task<int> GetDefaultStageIdAsync(SqlConnection cn, SqlTransaction tx, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT TOP (1) IdEtapa
            FROM dbo.CRM_ETAPAS
            WHERE ISNULL(Activa, 1) = 1
            ORDER BY Orden, IdEtapa;
            """, transaction: tx, cancellationToken: ct));

    private static async Task<int> GetNextNumberAsync(SqlConnection cn, SqlTransaction tx, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT ISNULL(MAX(Numero), 0) + 1 FROM dbo.CRM_OPORTUNIDADES WITH (UPDLOCK, HOLDLOCK);",
            transaction: tx,
            cancellationToken: ct));

    private static async Task<bool> IsStageLostAsync(SqlConnection cn, SqlTransaction tx, int idEtapa, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.CRM_ETAPAS WHERE IdEtapa = @IdEtapa AND ISNULL(EsPerdida, 0) = 1) THEN 1 ELSE 0 END;",
            new { IdEtapa = idEtapa }, tx, cancellationToken: ct)) == 1;

    private static Task<IEnumerable<CrmTareaDto>> GetTareasAsync(SqlConnection cn, long idOportunidad, CancellationToken ct)
        => cn.QueryAsync<CrmTareaDto>(new CommandDefinition("""
            SELECT
                t.IdTarea,
                t.IdOportunidad,
                ISNULL(t.TipoAccion, '') AS TipoAccion,
                ISNULL(t.Titulo, '') AS Titulo,
                ISNULL(CAST(t.Detalle AS nvarchar(max)), '') AS Detalle,
                t.Vencimiento,
                ISNULL(t.IdTecnico, '') AS IdTecnico,
                ISNULL(tec.Nombre, '') AS TecnicoNombre,
                ISNULL(t.Completada, 0) AS Completada,
                t.FechaHoraCompletada,
                CASE WHEN ISNULL(t.Completada, 0) = 0 AND t.Vencimiento < GETDATE() THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS Vencida
            FROM dbo.CRM_TAREAS t
            LEFT JOIN dbo.V_TA_Tecnicos tec ON LTRIM(RTRIM(tec.IdTecnico)) = LTRIM(RTRIM(t.IdTecnico))
            WHERE t.IdOportunidad = @IdOportunidad AND ISNULL(t.Baja, 0) = 0
            ORDER BY ISNULL(t.Completada, 0) ASC, t.Vencimiento ASC, t.IdTarea ASC;
            """, new { IdOportunidad = idOportunidad }, cancellationToken: ct));

    private static string NormalizeTareaTipo(string? tipo)
    {
        var value = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            CrmTareaTipos.Llamada => CrmTareaTipos.Llamada,
            CrmTareaTipos.Email => CrmTareaTipos.Email,
            CrmTareaTipos.Reunion => CrmTareaTipos.Reunion,
            CrmTareaTipos.WhatsApp => CrmTareaTipos.WhatsApp,
            _ => CrmTareaTipos.Tarea
        };
    }

    private static Task AddActivityAsync(SqlConnection cn, SqlTransaction? tx, long idOportunidad, string type, string description, string? user, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.CRM_ACTIVIDAD (IdOportunidad, TipoActividad, Descripcion, Usuario, FechaHora)
            VALUES (@IdOportunidad, @TipoActividad, @Descripcion, @Usuario, GETDATE());
            """, new
            {
                IdOportunidad = idOportunidad,
                TipoActividad = type,
                Descripcion = description,
                Usuario = NormalizeUser(user)
            }, tx, cancellationToken: ct));

    private static async Task ReplaceTagsAsync(SqlConnection cn, SqlTransaction tx, long idOportunidad, IEnumerable<int>? tagIds, CancellationToken ct)
    {
        await cn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.CRM_OPORTUNIDAD_ETIQUETAS WHERE IdOportunidad = @IdOportunidad;",
            new { IdOportunidad = idOportunidad }, tx, cancellationToken: ct));

        var ids = tagIds?.Where(x => x > 0).Distinct().Take(20).ToList() ?? [];
        foreach (var idEtiqueta in ids)
        {
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.CRM_OPORTUNIDAD_ETIQUETAS (IdOportunidad, IdEtiqueta)
                SELECT @IdOportunidad, @IdEtiqueta
                WHERE EXISTS (SELECT 1 FROM dbo.CRM_ETIQUETAS WHERE IdEtiqueta = @IdEtiqueta AND ISNULL(Activa, 1) = 1);
                """, new { IdOportunidad = idOportunidad, IdEtiqueta = idEtiqueta }, tx, cancellationToken: ct));
        }
    }

    private static async Task ReplaceSourceMessagesAsync(SqlConnection cn, SqlTransaction tx, long idOportunidad, IEnumerable<long>? messageIds, CancellationToken ct)
    {
        await cn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.CRM_OPORTUNIDAD_MENSAJES WHERE IdOportunidad = @IdOportunidad;",
            new { IdOportunidad = idOportunidad }, tx, cancellationToken: ct));

        var ids = messageIds?.Where(x => x > 0).Distinct().Take(50).ToList() ?? [];
        for (var i = 0; i < ids.Count; i++)
        {
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.CRM_OPORTUNIDAD_MENSAJES (IdOportunidad, IdMensaje, Orden)
                VALUES (@IdOportunidad, @IdMensaje, @Orden);
                """, new { IdOportunidad = idOportunidad, IdMensaje = ids[i], Orden = i + 1 }, tx, cancellationToken: ct));
        }
    }

    private static void NormalizeOpportunity(CrmOpportunitySaveRequest request)
    {
        request.Titulo = NormalizeText(request.Titulo, 180, "El nombre de la oportunidad es obligatorio.");
        request.Descripcion = (request.Descripcion ?? string.Empty).Trim();
        if (request.Descripcion.Length > 4000)
            request.Descripcion = request.Descripcion[..4000];
        request.Prioridad = Math.Clamp(request.Prioridad, 0, 3);
        request.Probabilidad = Math.Clamp(request.Probabilidad, 0, 100);
        if (request.ImporteEstimado < 0)
            throw new InvalidOperationException("El importe estimado no puede ser negativo.");
    }

    private static string NormalizeText(string? value, int maxLength, string requiredMessage)
    {
        var text = Regex.Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
        if (text.Length == 0)
            throw new InvalidOperationException(requiredMessage);
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        var color = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)
            ? color.ToUpperInvariant()
            : fallback;
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private static CrmViewSettingsDto BuildDefaultViewSettings() => new()
    {
        AgruparPor = CrmViewGroupKeys.Etapa,
        Columnas =
        [
            new() { Key = CrmViewColumnKeys.Numero, Label = "Número", Visible = true, Order = 1 },
            new() { Key = CrmViewColumnKeys.Titulo, Label = "Oportunidad", Visible = true, Order = 2 },
            new() { Key = CrmViewColumnKeys.Etapa, Label = "Etapa", Visible = true, Order = 3 },
            new() { Key = CrmViewColumnKeys.Importe, Label = "Importe", Visible = true, Order = 4 },
            new() { Key = CrmViewColumnKeys.Probabilidad, Label = "Probabilidad", Visible = true, Order = 5 },
            new() { Key = CrmViewColumnKeys.Asignado, Label = "Asignado", Visible = true, Order = 6 },
            new() { Key = CrmViewColumnKeys.Cliente, Label = "Cliente", Visible = true, Order = 7 },
            new() { Key = CrmViewColumnKeys.Contacto, Label = "Contacto", Visible = false, Order = 8 },
            new() { Key = CrmViewColumnKeys.Etiquetas, Label = "Etiquetas", Visible = true, Order = 9 },
            new() { Key = CrmViewColumnKeys.Fecha, Label = "Fecha", Visible = true, Order = 10 }
        ]
    };

    private static CrmViewSettingsDto MergeViewSettings(CrmViewSettingsDto? saved, CrmViewSettingsDto defaults)
    {
        if (saved is null)
            return defaults;
        var byKey = saved.Columnas.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var columns = defaults.Columnas
            .Select(d => byKey.TryGetValue(d.Key, out var s) ? s : d)
            .OrderBy(x => x.Order)
            .ToList();
        if (!columns.Any(x => x.Visible))
            columns.First().Visible = true;
        return new CrmViewSettingsDto
        {
            AgruparPor = string.IsNullOrWhiteSpace(saved.AgruparPor) ? defaults.AgruparPor : saved.AgruparPor,
            Columnas = columns
        };
    }

    private static string BuildViewConfigKey(string userName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant())));
        return $"{ViewConfigPrefix}{hash[..24]}";
    }

    private static bool TryBuildKnownSqlMessage(SqlException ex, out string message)
    {
        message = string.Empty;
        if (ex.Number != 208)
            return false;
        if (!ex.Message.Contains("CRM_", StringComparison.OrdinalIgnoreCase))
            return false;
        message = "El módulo CRM todavía no está inicializado en la base activa. Ejecutá las actualizaciones pendientes y recargá la pantalla.";
        return true;
    }

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (SqlException ex) when (TryBuildKnownSqlMessage(ex, out var knownMessage))
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(knownMessage, incidentId, ex);
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
        => await ExecuteLoggedAsync(action, async token =>
        {
            await operation(token);
            return true;
        }, userMessage, ct);
}

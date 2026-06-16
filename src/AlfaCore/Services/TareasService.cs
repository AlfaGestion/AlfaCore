using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace AlfaCore.Services;

public sealed class TareasService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IConversacionesService conversacionesService) : ITareasService
{
    private const string ModuleName = "Tareas";
    private const string SistemaFijo = "CN000PR";
    private const string TaskAssignmentTemplateMetaName = "nueva_tarea_asignada";
    private const string TaskAssignmentLegacyTemplateMetaName = "tarea_asignada";
    private const string TaskCompletionTemplateMetaName = "tarea_finalizada";
    private const string TaskAssignmentTemplateConfigKey = "TAREAS-WHATSAPP-PLANTILLA-ASIGNACION";
    private const string TaskCompletionTemplateConfigKey = "TAREAS-WHATSAPP-PLANTILLA-FINALIZACION";
    private const string TaskWhatsAppEnabledConfigKey = "TAREAS-WHATSAPP-NOTIFICACIONES";
    private const long MaxAttachmentBytes = 50L * 1024L * 1024L;
    private static readonly string[] AllowedAttachmentPrefixes = ["image/", "audio/", "video/"];

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<TareasPageDto> GetPageAsync(string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPage", async token =>
        {
            var user = NormalizeUser(usuario);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureDefaultListAsync(cn, user, token);
            await EnsureTaskPriorityColumnAsync(cn, token);
            await EnsureTaskGroupsReadyAsync(cn, token);
            await EnsureQuickNotesColumnsAsync(cn, token);
            var userActiveFilter = await HasUsuarioActivoColumnAsync(cn, token)
                ? "AND ISNULL(Activo, 1) = 1"
                : string.Empty;
            var hasAttachmentsTable = await HasTaskAttachmentsTableAsync(cn, token);
            var attachmentCountSelect = hasAttachmentsTable ? "ISNULL(ta.Cantidad, 0)" : "0";
            var attachmentCountApply = hasAttachmentsTable
                ? """
                OUTER APPLY
                (
                    SELECT COUNT(1) AS Cantidad
                    FROM dbo.ALFACORE_TAREAS_ADJUNTOS a
                    WHERE a.IdTarea = t.IdTarea
                      AND ISNULL(a.Activa, 1) = 1
                ) ta
                """
                : string.Empty;

            var sql = $"""
                WITH ListasVisibles AS
                (
                    SELECT l.*
                    FROM dbo.ALFACORE_TAREAS_LISTAS l
                    WHERE ISNULL(l.Activa, 1) = 1
                      AND (
                            UPPER(LTRIM(RTRIM(ISNULL(l.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                         OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.ALFACORE_TAREAS_COMPARTIDOS c
                                WHERE c.TipoObjeto = N'LISTA'
                                  AND c.IdObjeto = l.IdLista
                                  AND ISNULL(c.Activa, 1) = 1
                                  AND (
                                        c.EsPublico = 1
                                     OR UPPER(LTRIM(RTRIM(ISNULL(c.UsuarioDestino, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                                  )
                            )
                      )
                )
                SELECT
                    l.IdLista,
                    l.Nombre,
                    CONVERT(bit, ISNULL(l.EsDefault, 0)) AS EsDefault,
                    ISNULL(l.UsuarioAlta, '') AS UsuarioPropietario,
                    CONVERT(bit, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(l.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario))) THEN 1 ELSE 0 END) AS EsPropia,
                    SUM(CASE WHEN t.IdTarea IS NOT NULL AND t.Estado <> 'COMPLETADA' AND ISNULL(t.Activa, 1) = 1 THEN 1 ELSE 0 END) AS Pendientes,
                    SUM(CASE WHEN t.IdTarea IS NOT NULL AND t.Estado = 'COMPLETADA' AND ISNULL(t.Activa, 1) = 1 THEN 1 ELSE 0 END) AS Completadas
                FROM ListasVisibles l
                LEFT JOIN dbo.ALFACORE_TAREAS t ON t.IdLista = l.IdLista
                GROUP BY l.IdLista, l.Nombre, l.EsDefault, l.UsuarioAlta
                ORDER BY ISNULL(l.EsDefault, 0) DESC, l.Nombre;

                WITH ListasVisibles AS
                (
                    SELECT l.IdLista
                    FROM dbo.ALFACORE_TAREAS_LISTAS l
                    WHERE ISNULL(l.Activa, 1) = 1
                      AND (
                            UPPER(LTRIM(RTRIM(ISNULL(l.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                         OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.ALFACORE_TAREAS_COMPARTIDOS c
                                WHERE c.TipoObjeto = N'LISTA'
                                  AND c.IdObjeto = l.IdLista
                                  AND ISNULL(c.Activa, 1) = 1
                                  AND (
                                        c.EsPublico = 1
                                     OR UPPER(LTRIM(RTRIM(ISNULL(c.UsuarioDestino, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                                  )
                            )
                      )
                ),
                TareasVisibles AS
                (
                    SELECT t.IdTarea
                    FROM dbo.ALFACORE_TAREAS t
                    LEFT JOIN ListasVisibles lv ON lv.IdLista = t.IdLista
                    WHERE ISNULL(t.Activa, 1) = 1
                      AND (
                            lv.IdLista IS NOT NULL
                         OR UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                         OR CHARINDEX(N'|' + UPPER(LTRIM(RTRIM(@Usuario))) + N'|', N'|' + REPLACE(REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAsignado, '')))), N';', N'|'), N',', N'|') + N'|') > 0
                         OR CHARINDEX(N'|TODOS|', N'|' + REPLACE(REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAsignado, '')))), N';', N'|'), N',', N'|') + N'|') > 0
                         OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS gm
                                INNER JOIN dbo.ALFACORE_TAREAS_GRUPOS g ON g.IdGrupo = gm.IdGrupo
                                    AND ISNULL(g.Activa, 1) = 1
                                WHERE ISNULL(gm.Activa, 1) = 1
                                  AND UPPER(LTRIM(RTRIM(ISNULL(gm.Usuario, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                                  AND CHARINDEX(N'|' + UPPER(LTRIM(RTRIM(gm.Grupo))) + N'|', N'|' + REPLACE(REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAsignado, '')))), N';', N'|'), N',', N'|') + N'|') > 0
                            )
                         OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.ALFACORE_TAREAS_COMPARTIDOS c
                                WHERE c.TipoObjeto = N'TAREA'
                                  AND c.IdObjeto = t.IdTarea
                                  AND ISNULL(c.Activa, 1) = 1
                                  AND (
                                        c.EsPublico = 1
                                     OR UPPER(LTRIM(RTRIM(ISNULL(c.UsuarioDestino, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                                  )
                            )
                      )
                )
                SELECT
                    t.IdTarea,
                    t.IdLista,
                    ISNULL(l.Nombre, '') AS ListaNombre,
                    ISNULL(t.Titulo, '') AS Titulo,
                    ISNULL(CONVERT(nvarchar(max), t.Descripcion), '') AS Descripcion,
                    t.FechaVencimiento,
                    ISNULL(t.UsuarioAsignado, '') AS UsuarioAsignado,
                    ISNULL(t.Estado, 'PENDIENTE') AS Estado,
                    ISNULL(t.Prioridad, 'MEDIA') AS Prioridad,
                    ISNULL(t.UsuarioAlta, '') AS UsuarioAlta,
                    CONVERT(bit, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario))) THEN 1 ELSE 0 END) AS EsPropia,
                    {attachmentCountSelect} AS Adjuntos,
                    t.FechaHoraAlta,
                    t.FechaHoraModificacion,
                    t.FechaHoraCompletada
                FROM dbo.ALFACORE_TAREAS t
                INNER JOIN dbo.ALFACORE_TAREAS_LISTAS l ON l.IdLista = t.IdLista
                INNER JOIN TareasVisibles tv ON tv.IdTarea = t.IdTarea
                {attachmentCountApply}
                WHERE ISNULL(t.Activa, 1) = 1
                  AND ISNULL(l.Activa, 1) = 1
                  AND t.Estado <> 'COMPLETADA'
                ORDER BY
                    CASE ISNULL(t.Prioridad, 'MEDIA')
                        WHEN 'ALTA' THEN 0
                        WHEN 'MEDIA' THEN 1
                        ELSE 2
                    END,
                    CASE WHEN t.FechaVencimiento IS NULL THEN 1 ELSE 0 END,
                    t.FechaVencimiento,
                    t.FechaHoraAlta DESC;

                WITH ListasVisibles AS
                (
                    SELECT l.IdLista
                    FROM dbo.ALFACORE_TAREAS_LISTAS l
                    WHERE ISNULL(l.Activa, 1) = 1
                      AND (
                            UPPER(LTRIM(RTRIM(ISNULL(l.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                         OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.ALFACORE_TAREAS_COMPARTIDOS c
                                WHERE c.TipoObjeto = N'LISTA'
                                  AND c.IdObjeto = l.IdLista
                                  AND ISNULL(c.Activa, 1) = 1
                                  AND (
                                        c.EsPublico = 1
                                     OR UPPER(LTRIM(RTRIM(ISNULL(c.UsuarioDestino, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                                  )
                            )
                      )
                ),
                TareasVisibles AS
                (
                    SELECT t.IdTarea
                    FROM dbo.ALFACORE_TAREAS t
                    LEFT JOIN ListasVisibles lv ON lv.IdLista = t.IdLista
                    WHERE ISNULL(t.Activa, 1) = 1
                      AND (
                            lv.IdLista IS NOT NULL
                         OR UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                         OR CHARINDEX(N'|' + UPPER(LTRIM(RTRIM(@Usuario))) + N'|', N'|' + REPLACE(REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAsignado, '')))), N';', N'|'), N',', N'|') + N'|') > 0
                         OR CHARINDEX(N'|TODOS|', N'|' + REPLACE(REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAsignado, '')))), N';', N'|'), N',', N'|') + N'|') > 0
                         OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS gm
                                INNER JOIN dbo.ALFACORE_TAREAS_GRUPOS g ON g.IdGrupo = gm.IdGrupo
                                    AND ISNULL(g.Activa, 1) = 1
                                WHERE ISNULL(gm.Activa, 1) = 1
                                  AND UPPER(LTRIM(RTRIM(ISNULL(gm.Usuario, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                                  AND CHARINDEX(N'|' + UPPER(LTRIM(RTRIM(gm.Grupo))) + N'|', N'|' + REPLACE(REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAsignado, '')))), N';', N'|'), N',', N'|') + N'|') > 0
                            )
                         OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.ALFACORE_TAREAS_COMPARTIDOS c
                                WHERE c.TipoObjeto = N'TAREA'
                                  AND c.IdObjeto = t.IdTarea
                                  AND ISNULL(c.Activa, 1) = 1
                                  AND (
                                        c.EsPublico = 1
                                     OR UPPER(LTRIM(RTRIM(ISNULL(c.UsuarioDestino, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                                  )
                            )
                      )
                )
                SELECT
                    t.IdTarea,
                    t.IdLista,
                    ISNULL(l.Nombre, '') AS ListaNombre,
                    ISNULL(t.Titulo, '') AS Titulo,
                    ISNULL(CONVERT(nvarchar(max), t.Descripcion), '') AS Descripcion,
                    t.FechaVencimiento,
                    ISNULL(t.UsuarioAsignado, '') AS UsuarioAsignado,
                    ISNULL(t.Estado, 'PENDIENTE') AS Estado,
                    ISNULL(t.Prioridad, 'MEDIA') AS Prioridad,
                    ISNULL(t.UsuarioAlta, '') AS UsuarioAlta,
                    CONVERT(bit, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario))) THEN 1 ELSE 0 END) AS EsPropia,
                    {attachmentCountSelect} AS Adjuntos,
                    t.FechaHoraAlta,
                    t.FechaHoraModificacion,
                    t.FechaHoraCompletada
                FROM dbo.ALFACORE_TAREAS t
                INNER JOIN dbo.ALFACORE_TAREAS_LISTAS l ON l.IdLista = t.IdLista
                INNER JOIN TareasVisibles tv ON tv.IdTarea = t.IdTarea
                {attachmentCountApply}
                WHERE ISNULL(t.Activa, 1) = 1
                  AND ISNULL(l.Activa, 1) = 1
                  AND t.Estado = 'COMPLETADA'
                ORDER BY t.FechaHoraCompletada DESC, t.IdTarea DESC;

                SELECT
                    IdNota,
                    ISNULL(Texto, '') AS Texto,
                    ISNULL(CONVERT(nvarchar(max), Detalle), '') AS Detalle,
                    ISNULL(Usuario, '') AS Usuario,
                    CONVERT(bit, CASE WHEN UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario))) THEN 1 ELSE 0 END) AS EsPropia,
                    Fecha,
                    CONVERT(bit, ISNULL(Completada, 0)) AS Completada,
                    ISNULL(Orden, 0) AS Orden,
                    FechaHoraAlta,
                    FechaHoraCompletada
                FROM dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS n
                WHERE ISNULL(n.Activa, 1) = 1
                  AND (
                        UPPER(LTRIM(RTRIM(n.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
                     OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.ALFACORE_TAREAS_COMPARTIDOS c
                            WHERE c.TipoObjeto = N'NOTA'
                              AND c.IdObjeto = n.IdNota
                              AND ISNULL(c.Activa, 1) = 1
                              AND (
                                    c.EsPublico = 1
                                 OR UPPER(LTRIM(RTRIM(ISNULL(c.UsuarioDestino, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                              )
                        )
                  )
                ORDER BY Completada, ISNULL(Orden, 0), FechaHoraAlta DESC;

                SELECT
                    ISNULL(NOMBRE, '') AS Nombre,
                    ISNULL(email_de, '') AS Email
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND ISNULL(EsGrupo, 0) = 0
                  {userActiveFilter}
                ORDER BY NOMBRE;

                SELECT
                    ISNULL(g.Nombre, '') AS Grupo,
                    ISNULL(gm.Usuario, '') AS Usuario
                FROM dbo.ALFACORE_TAREAS_GRUPOS g
                LEFT JOIN dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS gm ON gm.IdGrupo = g.IdGrupo
                    AND ISNULL(gm.Activa, 1) = 1
                WHERE ISNULL(g.Activa, 1) = 1
                ORDER BY g.Nombre, gm.Usuario;

                SELECT
                    TipoObjeto,
                    IdObjeto,
                    CONVERT(bit, ISNULL(EsPublico, 0)) AS EsPublico,
                    ISNULL(UsuarioDestino, '') AS UsuarioDestino
                FROM dbo.ALFACORE_TAREAS_COMPARTIDOS
                WHERE ISNULL(Activa, 1) = 1
                  AND TipoObjeto IN (N'LISTA', N'NOTA', N'TAREA');
                """;

            using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, new { Usuario = user, Sistema = SistemaFijo }, cancellationToken: token));
            var page = new TareasPageDto
            {
                Listas = (await multi.ReadAsync<TareaListaDto>()).ToList(),
                Tareas = (await multi.ReadAsync<TareaItemDto>()).ToList(),
                Completadas = (await multi.ReadAsync<TareaItemDto>()).ToList(),
                NotasRapidas = (await multi.ReadAsync<TareaNotaRapidaDto>()).ToList(),
                Usuarios = (await multi.ReadAsync<TareaUsuarioDto>()).ToList()
            };

            page.Grupos = BuildTaskGroups((await multi.ReadAsync<TareaGroupMemberRow>()).ToList());
            ApplySharing(page, (await multi.ReadAsync<TareaShareRow>()).ToList());
            return page;
        }, "No se pudieron cargar las tareas.", ct);

    public Task<int> SaveListAsync(TareaListaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveList", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var nombre = request.Nombre.Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                throw new InvalidOperationException("El nombre de la lista es obligatorio.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var user = NormalizeUser(request.UsuarioAccion);
            await EnsureDefaultListAsync(cn, user, token);

            if (request.IdLista is int id && id > 0)
            {
                var isDefault = await cn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    """
                    SELECT CONVERT(bit, ISNULL(EsDefault, 0))
                    FROM dbo.ALFACORE_TAREAS_LISTAS
                    WHERE IdLista = @IdLista
                      AND UPPER(LTRIM(RTRIM(ISNULL(UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)));
                    """,
                    new { IdLista = id, Usuario = user },
                    cancellationToken: token));

                if (!await IsListOwnedByUserAsync(cn, id, user, token))
                    throw new InvalidOperationException("Solo podés modificar listas propias.");

                if (isDefault && !string.Equals(nombre, "Mis tareas", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("La lista por defecto no se puede renombrar.");

                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.ALFACORE_TAREAS_LISTAS
                    SET Nombre = @Nombre,
                        FechaHora_Modificacion = GETDATE()
                    WHERE IdLista = @IdLista
                      AND ISNULL(Activa, 1) = 1;
                    """,
                    new { IdLista = id, Nombre = nombre },
                    cancellationToken: token));
                return id;
            }

            return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO dbo.ALFACORE_TAREAS_LISTAS (Nombre, EsDefault, UsuarioAlta, FechaHora_Alta, Activa)
                OUTPUT INSERTED.IdLista
                VALUES (@Nombre, 0, @Usuario, GETDATE(), 1);
                """,
                new { Nombre = nombre, Usuario = NormalizeUser(request.UsuarioAccion) },
                cancellationToken: token));
        }, "No se pudo guardar la lista de tareas.", ct);

    public Task DeleteListAsync(int idLista, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DeleteList", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var user = NormalizeUser(usuarioAccion);
            var defaultId = await EnsureDefaultListAsync(cn, user, token);

            var isDefault = await cn.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT CONVERT(bit, ISNULL(EsDefault, 0))
                FROM dbo.ALFACORE_TAREAS_LISTAS
                WHERE IdLista = @IdLista
                  AND UPPER(LTRIM(RTRIM(ISNULL(UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)));
                """,
                new { IdLista = idLista, Usuario = user },
                cancellationToken: token));

            if (!await IsListOwnedByUserAsync(cn, idLista, user, token))
                throw new InvalidOperationException("Solo podés eliminar listas propias.");

            if (isDefault || idLista == defaultId)
                throw new InvalidOperationException("La lista Mis tareas no se puede eliminar.");

            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS
                SET IdLista = @DefaultId,
                    FechaHoraModificacion = GETDATE()
                WHERE IdLista = @IdLista
                  AND ISNULL(Activa, 1) = 1;

                UPDATE dbo.ALFACORE_TAREAS_LISTAS
                SET Activa = 0,
                    FechaHora_Modificacion = GETDATE()
                WHERE IdLista = @IdLista
                  AND ISNULL(EsDefault, 0) = 0;
                """,
                new { IdLista = idLista, DefaultId = defaultId },
                cancellationToken: token));
        }, "No se pudo eliminar la lista de tareas.", ct);

    public Task SaveGroupsAsync(TareaGruposSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveGroups", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var user = NormalizeUser(request.UsuarioAccion);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureTaskGroupsReadyAsync(cn, token);

            var groups = request.Grupos
                .Select(x => new TareaGrupoDto
                {
                    Grupo = NormalizeGroupName(x.Grupo),
                    Usuarios = x.Usuarios
                        .Select(NormalizeUser)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(u => u)
                        .ToList()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Grupo))
                .Where(x => !string.Equals(x.Grupo, "Todos", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.Grupo, StringComparer.OrdinalIgnoreCase)
                .Select(x => new TareaGrupoDto
                {
                    Grupo = x.First().Grupo,
                    Usuarios = x.SelectMany(g => g.Usuarios)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(u => u)
                        .ToList()
                })
                .OrderBy(x => x.Grupo)
                .ToList();

            using var tx = cn.BeginTransaction();
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS_GRUPOS
                SET Activa = 0,
                    FechaHoraModificacion = GETDATE(),
                    UsuarioModificacion = @UsuarioAccion
                WHERE ISNULL(Activa, 1) = 1;

                UPDATE gm
                SET Activa = 0,
                    FechaHoraModificacion = GETDATE(),
                    UsuarioModificacion = @UsuarioAccion
                FROM dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS gm
                INNER JOIN dbo.ALFACORE_TAREAS_GRUPOS g ON g.IdGrupo = gm.IdGrupo
                WHERE ISNULL(gm.Activa, 1) = 1;
                """,
                new { UsuarioAccion = user },
                transaction: tx,
                cancellationToken: token));

            foreach (var group in groups)
            {
                var idGrupo = await SaveGroupHeaderAsync(cn, group.Grupo, user, tx, token);
                foreach (var member in group.Usuarios)
                    await cn.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS
                            (IdGrupo, Grupo, Usuario, UsuarioAlta, FechaHoraAlta, Activa)
                        VALUES
                            (@IdGrupo, @Grupo, @Usuario, @UsuarioAccion, GETDATE(), 1);
                        """,
                        new { IdGrupo = idGrupo, Grupo = group.Grupo, Usuario = member, UsuarioAccion = user },
                        transaction: tx,
                        cancellationToken: token));
            }

            tx.Commit();
        }, "No se pudieron guardar los grupos de tareas.", ct);

    public Task<long> SaveTaskAsync(TareaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveTask", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var titulo = request.Titulo.Trim();
            if (string.IsNullOrWhiteSpace(titulo))
                throw new InvalidOperationException("El título de la tarea es obligatorio.");

            var estado = NormalizeState(request.Estado);
            var prioridad = NormalizePriority(request.Prioridad);
            var user = NormalizeUser(request.UsuarioAccion);
            var asignado = NormalizeAssignees(request.UsuarioAsignado, user);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureTaskPriorityColumnAsync(cn, token);
            var defaultId = await EnsureDefaultListAsync(cn, user, token);
            var idLista = request.IdLista <= 0 ? defaultId : request.IdLista;
            if (!await CanAccessListAsync(cn, idLista, user, token)
                && await IsDefaultPersonalListAsync(cn, idLista, token))
                idLista = defaultId;
            if (!await CanAccessListAsync(cn, idLista, user, token))
                throw new InvalidOperationException("No tenés acceso a la lista seleccionada.");
            if (HasSharingSelection(request.CompartirConTodos, request.UsuariosCompartidos))
                await EnsureTaskSharingReadyAsync(cn, token);

            if (request.IdTarea is long id && id > 0)
            {
                if (!await CanAccessTaskAsync(cn, id, user, token))
                    throw new InvalidOperationException("No tenés acceso a la tarea seleccionada.");

                var previous = await GetTaskNotificationSnapshotAsync(cn, id, token);
                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.ALFACORE_TAREAS
                    SET IdLista = @IdLista,
                        Titulo = @Titulo,
                        Descripcion = @Descripcion,
                        FechaVencimiento = @FechaVencimiento,
                        UsuarioAsignado = @UsuarioAsignado,
                        Estado = @Estado,
                        Prioridad = @Prioridad,
                        FechaHoraModificacion = GETDATE(),
                        FechaHoraCompletada = CASE
                            WHEN @Estado = 'COMPLETADA' AND Estado <> 'COMPLETADA' THEN GETDATE()
                            WHEN @Estado <> 'COMPLETADA' THEN NULL
                            ELSE FechaHoraCompletada
                        END
                    WHERE IdTarea = @IdTarea
                      AND ISNULL(Activa, 1) = 1;
                    """,
                    new
                    {
                        IdTarea = id,
                        IdLista = idLista,
                        Titulo = titulo,
                        Descripcion = EmptyToNull(request.Descripcion),
                        request.FechaVencimiento,
                        UsuarioAsignado = asignado,
                        Estado = estado,
                        Prioridad = prioridad
                    },
                    cancellationToken: token));
                if (await IsTaskOwnedByUserAsync(cn, id, user, token))
                    await SaveSharingRulesAsync(cn, "TAREA", id, request.CompartirConTodos, request.UsuariosCompartidos, user, null, token);
                if (previous is not null && !AssignmentsEquivalent(previous.UsuarioAsignado, asignado))
                    await NotifyTaskAssignmentAsync(cn, id, titulo, asignado, user, token);
                if (previous is not null
                    && !string.Equals(previous.Estado, TareaEstadoKeys.Completada, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(estado, TareaEstadoKeys.Completada, StringComparison.OrdinalIgnoreCase))
                    await NotifyTaskCompletionAsync(cn, id, titulo, asignado, previous.UsuarioAlta, user, token);
                return id;
            }

            var newId = await cn.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                INSERT INTO dbo.ALFACORE_TAREAS
                    (IdLista, Titulo, Descripcion, FechaVencimiento, UsuarioAsignado, Estado, Prioridad, UsuarioAlta, FechaHoraAlta, Activa)
                OUTPUT INSERTED.IdTarea
                VALUES
                    (@IdLista, @Titulo, @Descripcion, @FechaVencimiento, @UsuarioAsignado, @Estado, @Prioridad, @UsuarioAlta, GETDATE(), 1);
                """,
                new
                {
                    IdLista = idLista,
                    Titulo = titulo,
                    Descripcion = EmptyToNull(request.Descripcion),
                    request.FechaVencimiento,
                    UsuarioAsignado = asignado,
                    Estado = estado,
                    Prioridad = prioridad,
                    UsuarioAlta = user
                },
                cancellationToken: token));
            await SaveSharingRulesAsync(cn, "TAREA", newId, request.CompartirConTodos, request.UsuariosCompartidos, user, null, token);
            await NotifyTaskAssignmentAsync(cn, newId, titulo, asignado, user, token);
            if (string.Equals(estado, TareaEstadoKeys.Completada, StringComparison.OrdinalIgnoreCase))
                await NotifyTaskCompletionAsync(cn, newId, titulo, asignado, user, user, token);
            return newId;
        }, "No se pudo guardar la tarea.", ct);

    public Task MoveTaskToListAsync(long idTarea, int idLista, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "MoveTaskToList", async token =>
        {
            if (idTarea <= 0 || idLista <= 0)
                throw new InvalidOperationException("La tarea o la lista destino no son válidas.");

            var user = NormalizeUser(usuarioAccion);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await EnsureTaskPriorityColumnAsync(cn, token);
            if (!await CanAccessTaskAsync(cn, idTarea, user, token))
                throw new InvalidOperationException("No tenés acceso a la tarea seleccionada.");

            if (!await CanAccessListAsync(cn, idLista, user, token))
                throw new InvalidOperationException("No tenés acceso a la lista destino.");

            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS
                SET IdLista = @IdLista,
                    FechaHoraModificacion = GETDATE()
                WHERE IdTarea = @IdTarea
                  AND ISNULL(Activa, 1) = 1;
                """,
                new { IdTarea = idTarea, IdLista = idLista },
                cancellationToken: token));
        }, "No se pudo mover la tarea.", ct);

    public Task<IReadOnlyList<TareaAdjuntoDto>> GetTaskAttachmentsAsync(long idTarea, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<TareaAdjuntoDto>>(ModuleName, "GetTaskAttachments", async token =>
        {
            var user = NormalizeUser(usuarioAccion);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await HasTaskAttachmentsTableAsync(cn, token))
                return Array.Empty<TareaAdjuntoDto>();
            if (!await CanAccessTaskAsync(cn, idTarea, user, token))
                throw new InvalidOperationException("No tenés acceso a la tarea seleccionada.");

            var rows = await cn.QueryAsync<TareaAdjuntoRecord>(new CommandDefinition(
                """
                SELECT
                    IdAdjunto,
                    IdTarea,
                    ISNULL(NombreArchivo, '') AS NombreArchivo,
                    ISNULL(MimeType, '') AS MimeType,
                    ISNULL(TamanoBytes, 0) AS TamanoBytes,
                    Contenido,
                    ISNULL(UsuarioAlta, '') AS UsuarioAlta,
                    FechaHoraAlta
                FROM dbo.ALFACORE_TAREAS_ADJUNTOS
                WHERE IdTarea = @IdTarea
                  AND ISNULL(Activa, 1) = 1
                ORDER BY FechaHoraAlta, IdAdjunto;
                """,
                new { IdTarea = idTarea },
                cancellationToken: token));

            return rows
                .Select(x => new TareaAdjuntoDto
                {
                    IdAdjunto = x.IdAdjunto,
                    IdTarea = x.IdTarea,
                    NombreArchivo = x.NombreArchivo,
                    MimeType = x.MimeType,
                    TamanoBytes = x.TamanoBytes,
                    ContenidoBase64 = Convert.ToBase64String(x.Contenido),
                    UsuarioAlta = x.UsuarioAlta,
                    FechaHoraAlta = x.FechaHoraAlta
                })
                .ToList();
        }, "No se pudieron cargar los adjuntos de la tarea.", ct);

    public Task AddTaskAttachmentsAsync(long idTarea, IReadOnlyList<TareaAdjuntoUploadDto> adjuntos, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AddTaskAttachments", async token =>
        {
            if (adjuntos.Count == 0)
                return;

            var user = NormalizeUser(usuarioAccion);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureTaskAttachmentsReadyAsync(cn, token);
            if (!await CanAccessTaskAsync(cn, idTarea, user, token))
                throw new InvalidOperationException("No tenés acceso a la tarea seleccionada.");

            foreach (var adjunto in adjuntos)
                ValidateAttachment(adjunto);

            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                foreach (var adjunto in adjuntos)
                {
                    await cn.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO dbo.ALFACORE_TAREAS_ADJUNTOS
                            (IdTarea, NombreArchivo, MimeType, TamanoBytes, Contenido, UsuarioAlta, FechaHoraAlta, Activa)
                        VALUES
                            (@IdTarea, @NombreArchivo, @MimeType, @TamanoBytes, @Contenido, @UsuarioAlta, GETDATE(), 1);
                        """,
                        new
                        {
                            IdTarea = idTarea,
                            NombreArchivo = Truncate(adjunto.NombreArchivo, 255),
                            MimeType = Truncate(adjunto.MimeType, 100),
                            adjunto.TamanoBytes,
                            adjunto.Contenido,
                            UsuarioAlta = user
                        },
                        transaction: (SqlTransaction)tx,
                        cancellationToken: token));
                }

                await tx.CommitAsync(token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudieron agregar los adjuntos a la tarea.", ct);

    public Task DeleteTaskAttachmentAsync(long idAdjunto, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DeleteTaskAttachment", async token =>
        {
            var user = NormalizeUser(usuarioAccion);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureTaskAttachmentsReadyAsync(cn, token);

            var idTarea = await cn.ExecuteScalarAsync<long?>(new CommandDefinition(
                """
                SELECT IdTarea
                FROM dbo.ALFACORE_TAREAS_ADJUNTOS
                WHERE IdAdjunto = @IdAdjunto
                  AND ISNULL(Activa, 1) = 1;
                """,
                new { IdAdjunto = idAdjunto },
                cancellationToken: token));

            if (idTarea is null)
                return;
            if (!await CanAccessTaskAsync(cn, idTarea.Value, user, token))
                throw new InvalidOperationException("No tenés acceso a la tarea seleccionada.");

            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS_ADJUNTOS
                SET Activa = 0,
                    FechaHoraModificacion = GETDATE(),
                    UsuarioModificacion = @Usuario
                WHERE IdAdjunto = @IdAdjunto;
                """,
                new { IdAdjunto = idAdjunto, Usuario = user },
                cancellationToken: token));
        }, "No se pudo eliminar el adjunto de la tarea.", ct);

    public Task ChangeTaskStateAsync(long idTarea, string estado, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ChangeTaskState", async token =>
        {
            var user = NormalizeUser(usuarioAccion);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await CanAccessTaskAsync(cn, idTarea, user, token))
                throw new InvalidOperationException("No tenés acceso a la tarea seleccionada.");

            var previous = await GetTaskNotificationSnapshotAsync(cn, idTarea, token);
            var normalizedState = NormalizeState(estado);
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS
                SET Estado = @Estado,
                    FechaHoraModificacion = GETDATE(),
                    FechaHoraCompletada = CASE WHEN @Estado = 'COMPLETADA' THEN GETDATE() ELSE NULL END
                WHERE IdTarea = @IdTarea
                  AND ISNULL(Activa, 1) = 1;
                """,
                new { IdTarea = idTarea, Estado = normalizedState },
                cancellationToken: token));
            if (previous is not null
                && !string.Equals(previous.Estado, TareaEstadoKeys.Completada, StringComparison.OrdinalIgnoreCase)
                && string.Equals(normalizedState, TareaEstadoKeys.Completada, StringComparison.OrdinalIgnoreCase))
                await NotifyTaskCompletionAsync(cn, idTarea, previous.Titulo, previous.UsuarioAsignado, previous.UsuarioAlta, user, token);
        }, "No se pudo cambiar el estado de la tarea.", ct);

    public Task DuplicateTaskAsync(long idTarea, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DuplicateTask", async token =>
        {
            var user = NormalizeUser(usuarioAccion);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureTaskPriorityColumnAsync(cn, token);
            if (!await CanAccessTaskAsync(cn, idTarea, user, token))
                throw new InvalidOperationException("No tenés acceso a la tarea seleccionada.");

            await cn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ALFACORE_TAREAS
                    (IdLista, Titulo, Descripcion, FechaVencimiento, UsuarioAsignado, Estado, Prioridad, UsuarioAlta, FechaHoraAlta, Activa)
                SELECT
                    IdLista,
                    CONCAT(Titulo, N' (copia)'),
                    Descripcion,
                    FechaVencimiento,
                    UsuarioAsignado,
                    'PENDIENTE',
                    ISNULL(Prioridad, N'MEDIA'),
                    @Usuario,
                    GETDATE(),
                    1
                FROM dbo.ALFACORE_TAREAS
                WHERE IdTarea = @IdTarea
                  AND ISNULL(Activa, 1) = 1;
                """,
                new { IdTarea = idTarea, Usuario = user },
                cancellationToken: token));
        }, "No se pudo duplicar la tarea.", ct);

    public Task DeleteTaskAsync(long idTarea, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DeleteTask", async token =>
        {
            var user = NormalizeUser(usuarioAccion);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await CanAccessTaskAsync(cn, idTarea, user, token))
                throw new InvalidOperationException("No tenés acceso a la tarea seleccionada.");

            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS
                SET Activa = 0,
                    FechaHoraModificacion = GETDATE()
                WHERE IdTarea = @IdTarea;
                """,
                new { IdTarea = idTarea },
                cancellationToken: token));
        }, "No se pudo eliminar la tarea.", ct);

    public Task<long> AddQuickNoteAsync(string texto, string usuario, CancellationToken ct = default)
        => SaveQuickNoteAsync(new TareaNotaRapidaSaveRequest
        {
            Texto = texto,
            UsuarioAccion = usuario
        }, ct);

    public Task<long> SaveQuickNoteAsync(TareaNotaRapidaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AddQuickNote", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var clean = request.Texto.Trim();
            var detail = request.Detalle.Trim();
            if (string.IsNullOrWhiteSpace(clean))
                throw new InvalidOperationException("La nota rápida no puede estar vacía.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureQuickNotesColumnsAsync(cn, token);
            var user = NormalizeUser(request.UsuarioAccion);

            if (request.IdNota is long idNota && idNota > 0)
            {
                var affected = await cn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                    SET Texto = @Texto,
                        Detalle = @Detalle
                    WHERE IdNota = @IdNota
                      AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
                      AND ISNULL(Activa, 1) = 1;
                    """,
                    new { IdNota = idNota, Texto = clean, Detalle = detail, Usuario = user },
                    cancellationToken: token));

                if (affected == 0)
                    throw new InvalidOperationException("Solo podés editar tareas rápidas propias.");

                return idNota;
            }

            var nextOrder = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT ISNULL(MAX(Orden), 0) + 10
                FROM dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                WHERE UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
                  AND ISNULL(Activa, 1) = 1
                  AND ISNULL(Completada, 0) = 0;
                """,
                new { Usuario = user },
                cancellationToken: token));

            return await cn.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                INSERT INTO dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                    (Texto, Detalle, Fecha, Usuario, Completada, Orden, FechaHoraAlta, Activa)
                OUTPUT INSERTED.IdNota
                VALUES
                    (@Texto, @Detalle, CONVERT(date, GETDATE()), @Usuario, 0, @Orden, GETDATE(), 1);
                """,
                new { Texto = clean, Detalle = detail, Usuario = user, Orden = nextOrder },
                cancellationToken: token));
        }, "No se pudo guardar la nota rápida.", ct);

    public Task ToggleQuickNoteAsync(long idNota, bool completada, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ToggleQuickNote", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureQuickNotesColumnsAsync(cn, token);
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                SET Completada = @Completada,
                    FechaHoraCompletada = CASE WHEN @Completada = 1 THEN GETDATE() ELSE NULL END
                WHERE IdNota = @IdNota
                  AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
                  AND ISNULL(Activa, 1) = 1;
                """,
                new { IdNota = idNota, Completada = completada, Usuario = NormalizeUser(usuario) },
                cancellationToken: token));
        }, "No se pudo actualizar la nota rápida.", ct);

    public Task DeleteQuickNoteAsync(long idNota, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DeleteQuickNote", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                SET Activa = 0
                WHERE IdNota = @IdNota
                  AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)));
                """,
                new { IdNota = idNota, Usuario = NormalizeUser(usuario) },
                cancellationToken: token));
        }, "No se pudo eliminar la nota rápida.", ct);

    public Task ClearCompletedQuickNotesAsync(string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ClearCompletedQuickNotes", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                SET Activa = 0
                WHERE Completada = 1
                  AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)));
                """,
                new { Usuario = NormalizeUser(usuario) },
                cancellationToken: token));
        }, "No se pudieron limpiar las notas completadas.", ct);

    public Task ReorderQuickNotesAsync(IReadOnlyList<long> orderedIds, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ReorderQuickNotes", async token =>
        {
            ArgumentNullException.ThrowIfNull(orderedIds);
            var ids = orderedIds.Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0)
                return;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureQuickNotesColumnsAsync(cn, token);
            var user = NormalizeUser(usuario);

            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                for (var i = 0; i < ids.Length; i++)
                {
                    await cn.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                        SET Orden = @Orden
                        WHERE IdNota = @IdNota
                          AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
                          AND ISNULL(Activa, 1) = 1
                          AND ISNULL(Completada, 0) = 0;
                        """,
                        new { IdNota = ids[i], Orden = (i + 1) * 10, Usuario = user },
                        (SqlTransaction)tx,
                        cancellationToken: token));
                }

                await tx.CommitAsync(token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo reordenar las notas rápidas.", ct);

    public Task SaveSharingAsync(TareaCompartirRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveSharing", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var user = NormalizeUser(request.UsuarioAccion);
            var type = ToSharedObjectType(request.TipoObjeto);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var ownsObject = request.TipoObjeto switch
            {
                TareaObjetoCompartidoTipo.Lista => await IsListOwnedByUserAsync(cn, checked((int)request.IdObjeto), user, token),
                TareaObjetoCompartidoTipo.Nota => await IsNoteOwnedByUserAsync(cn, request.IdObjeto, user, token),
                TareaObjetoCompartidoTipo.Tarea => await IsTaskOwnedByUserAsync(cn, request.IdObjeto, user, token),
                _ => false
            };

            if (!ownsObject)
                throw new InvalidOperationException("Solo podés compartir elementos propios.");
            if (request.TipoObjeto == TareaObjetoCompartidoTipo.Tarea && HasSharingSelection(request.CompartirConTodos, request.Usuarios))
                await EnsureTaskSharingReadyAsync(cn, token);

            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                await SaveSharingRulesAsync(cn, type, request.IdObjeto, request.CompartirConTodos, request.Usuarios, user, (SqlTransaction)tx, token);

                await tx.CommitAsync(token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar la configuración de compartición.", ct);

    private static async Task<int> EnsureDefaultListAsync(SqlConnection cn, string usuario, CancellationToken ct)
    {
        const string sql = """
            IF NOT EXISTS (
                SELECT 1
                FROM dbo.ALFACORE_TAREAS_LISTAS
                WHERE EsDefault = 1
                  AND UPPER(LTRIM(RTRIM(ISNULL(UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                  AND ISNULL(Activa, 1) = 1
            )
            BEGIN
                INSERT INTO dbo.ALFACORE_TAREAS_LISTAS (Nombre, EsDefault, UsuarioAlta, FechaHora_Alta, Activa)
                VALUES (N'Mis tareas', 1, @Usuario, GETDATE(), 1);
            END;

            SELECT TOP (1) IdLista
            FROM dbo.ALFACORE_TAREAS_LISTAS
            WHERE EsDefault = 1
              AND UPPER(LTRIM(RTRIM(ISNULL(UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
              AND ISNULL(Activa, 1) = 1
            ORDER BY IdLista;
            """;

        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Usuario = NormalizeUser(usuario) }, cancellationToken: ct));
    }

    private static async Task<bool> HasUsuarioActivoColumnAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN COL_LENGTH('dbo.TA_USUARIOS', 'Activo') IS NULL THEN 0 ELSE 1 END;",
            cancellationToken: ct)) == 1;

    private static async Task<bool> HasTaskAttachmentsTableAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.ALFACORE_TAREAS_ADJUNTOS', N'U') IS NULL THEN 0 ELSE 1 END;",
            cancellationToken: ct)) == 1;

    private static async Task EnsureTaskPriorityColumnAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteAsync(new CommandDefinition(
            """
            IF COL_LENGTH(N'dbo.ALFACORE_TAREAS', N'Prioridad') IS NULL
            BEGIN
                ALTER TABLE dbo.ALFACORE_TAREAS
                ADD Prioridad nvarchar(10) NOT NULL
                    CONSTRAINT DF_ALFACORE_TAREAS_Prioridad DEFAULT (N'MEDIA');
            END;

            UPDATE dbo.ALFACORE_TAREAS
            SET Prioridad = N'MEDIA'
            WHERE Prioridad IS NULL
               OR LTRIM(RTRIM(Prioridad)) = N''
               OR UPPER(LTRIM(RTRIM(Prioridad))) NOT IN (N'ALTA', N'MEDIA', N'BAJA');
            """,
            cancellationToken: ct));

    private static async Task EnsureQuickNotesColumnsAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteAsync(new CommandDefinition(
            """
            IF COL_LENGTH(N'dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS', N'Detalle') IS NULL
            BEGIN
                ALTER TABLE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                ADD Detalle nvarchar(max) NULL;
            END;

            IF COL_LENGTH(N'dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS', N'Orden') IS NULL
            BEGIN
                ALTER TABLE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                ADD Orden int NOT NULL
                    CONSTRAINT DF_ALFACORE_TAREAS_NOTAS_Orden DEFAULT (0);
            END;

            ;WITH Pendientes AS
            (
                SELECT
                    IdNota,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY UPPER(LTRIM(RTRIM(Usuario))), ISNULL(Completada, 0)
                        ORDER BY FechaHoraAlta DESC, IdNota DESC
                    ) AS RowNum
                FROM dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                WHERE ISNULL(Activa, 1) = 1
                  AND ISNULL(Orden, 0) = 0
            )
            UPDATE n
            SET Orden = p.RowNum * 10
            FROM dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS n
            INNER JOIN Pendientes p ON p.IdNota = n.IdNota;
            """,
            cancellationToken: ct));

    private static void ApplySharing(TareasPageDto page, IReadOnlyList<TareaShareRow> shares)
    {
        foreach (var lista in page.Listas)
        {
            var listShares = shares
                .Where(x => x.TipoObjeto == "LISTA" && x.IdObjeto == lista.IdLista)
                .ToArray();
            lista.CompartidaConTodos = listShares.Any(x => x.EsPublico);
            lista.UsuariosCompartidos = listShares
                .Where(x => !x.EsPublico && !string.IsNullOrWhiteSpace(x.UsuarioDestino))
                .Select(x => x.UsuarioDestino)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        foreach (var nota in page.NotasRapidas)
        {
            var noteShares = shares
                .Where(x => x.TipoObjeto == "NOTA" && x.IdObjeto == nota.IdNota)
                .ToArray();
            nota.CompartidaConTodos = noteShares.Any(x => x.EsPublico);
            nota.UsuariosCompartidos = noteShares
                .Where(x => !x.EsPublico && !string.IsNullOrWhiteSpace(x.UsuarioDestino))
                .Select(x => x.UsuarioDestino)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        foreach (var tarea in page.Tareas.Concat(page.Completadas))
        {
            var taskShares = shares
                .Where(x => x.TipoObjeto == "TAREA" && x.IdObjeto == tarea.IdTarea)
                .ToArray();
            tarea.CompartidaConTodos = taskShares.Any(x => x.EsPublico);
            tarea.UsuariosCompartidos = taskShares
                .Where(x => !x.EsPublico && !string.IsNullOrWhiteSpace(x.UsuarioDestino))
                .Select(x => x.UsuarioDestino)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }
    }

    private static List<TareaGrupoDto> BuildTaskGroups(IReadOnlyList<TareaGroupMemberRow> rows)
        => rows
            .Select(x => x.Grupo)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .Select(groupName => new TareaGrupoDto
            {
                Grupo = groupName,
                Usuarios = rows
                    .Where(x => string.Equals(x.Grupo, groupName, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Usuario)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList()
            })
            .ToList();

    private static async Task<bool> IsListOwnedByUserAsync(SqlConnection cn, int idLista, string usuario, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.ALFACORE_TAREAS_LISTAS
                WHERE IdLista = @IdLista
                  AND ISNULL(Activa, 1) = 1
                  AND UPPER(LTRIM(RTRIM(ISNULL(UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
            )
            THEN 1 ELSE 0 END;
            """,
            new { IdLista = idLista, Usuario = usuario },
            cancellationToken: ct)) == 1;

    private static async Task<bool> IsNoteOwnedByUserAsync(SqlConnection cn, long idNota, string usuario, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                WHERE IdNota = @IdNota
                  AND ISNULL(Activa, 1) = 1
                  AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
            )
            THEN 1 ELSE 0 END;
            """,
            new { IdNota = idNota, Usuario = usuario },
            cancellationToken: ct)) == 1;

    private static async Task<bool> IsTaskOwnedByUserAsync(SqlConnection cn, long idTarea, string usuario, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.ALFACORE_TAREAS
                WHERE IdTarea = @IdTarea
                  AND ISNULL(Activa, 1) = 1
                  AND UPPER(LTRIM(RTRIM(ISNULL(UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
            )
            THEN 1 ELSE 0 END;
            """,
            new { IdTarea = idTarea, Usuario = usuario },
            cancellationToken: ct)) == 1;

    private static async Task<bool> CanAccessListAsync(SqlConnection cn, int idLista, string usuario, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.ALFACORE_TAREAS_LISTAS l
                WHERE l.IdLista = @IdLista
                  AND ISNULL(l.Activa, 1) = 1
                  AND (
                        UPPER(LTRIM(RTRIM(ISNULL(l.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                     OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.ALFACORE_TAREAS_COMPARTIDOS c
                            WHERE c.TipoObjeto = N'LISTA'
                              AND c.IdObjeto = l.IdLista
                              AND ISNULL(c.Activa, 1) = 1
                              AND (
                                    c.EsPublico = 1
                                 OR UPPER(LTRIM(RTRIM(ISNULL(c.UsuarioDestino, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                              )
                        )
                  )
            )
            THEN 1 ELSE 0 END;
            """,
            new { IdLista = idLista, Usuario = usuario },
            cancellationToken: ct)) == 1;

    private static async Task<bool> IsDefaultPersonalListAsync(SqlConnection cn, int idLista, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.ALFACORE_TAREAS_LISTAS
                WHERE IdLista = @IdLista
                  AND ISNULL(Activa, 1) = 1
                  AND ISNULL(EsDefault, 0) = 1
                  AND UPPER(LTRIM(RTRIM(ISNULL(Nombre, '')))) = N'MIS TAREAS'
            )
            THEN 1 ELSE 0 END;
            """,
            new { IdLista = idLista },
            cancellationToken: ct)) == 1;

    private static async Task<bool> CanAccessTaskAsync(SqlConnection cn, long idTarea, string usuario, CancellationToken ct)
    {
        await EnsureTaskGroupsReadyAsync(cn, ct);
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.ALFACORE_TAREAS t
                INNER JOIN dbo.ALFACORE_TAREAS_LISTAS l ON l.IdLista = t.IdLista
                WHERE t.IdTarea = @IdTarea
                  AND ISNULL(t.Activa, 1) = 1
                  AND ISNULL(l.Activa, 1) = 1
                  AND (
                        UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                     OR CHARINDEX(N'|' + UPPER(LTRIM(RTRIM(@Usuario))) + N'|', N'|' + REPLACE(REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAsignado, '')))), N';', N'|'), N',', N'|') + N'|') > 0
                     OR CHARINDEX(N'|TODOS|', N'|' + REPLACE(REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAsignado, '')))), N';', N'|'), N',', N'|') + N'|') > 0
                     OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS gm
                            INNER JOIN dbo.ALFACORE_TAREAS_GRUPOS g ON g.IdGrupo = gm.IdGrupo
                                AND ISNULL(g.Activa, 1) = 1
                            WHERE ISNULL(gm.Activa, 1) = 1
                              AND UPPER(LTRIM(RTRIM(ISNULL(gm.Usuario, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                              AND CHARINDEX(N'|' + UPPER(LTRIM(RTRIM(gm.Grupo))) + N'|', N'|' + REPLACE(REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.UsuarioAsignado, '')))), N';', N'|'), N',', N'|') + N'|') > 0
                        )
                     OR UPPER(LTRIM(RTRIM(ISNULL(l.UsuarioAlta, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                     OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.ALFACORE_TAREAS_COMPARTIDOS c
                            WHERE c.TipoObjeto = N'LISTA'
                              AND c.IdObjeto = l.IdLista
                              AND ISNULL(c.Activa, 1) = 1
                              AND (
                                    c.EsPublico = 1
                                 OR UPPER(LTRIM(RTRIM(ISNULL(c.UsuarioDestino, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                              )
                        )
                     OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.ALFACORE_TAREAS_COMPARTIDOS c
                            WHERE c.TipoObjeto = N'TAREA'
                              AND c.IdObjeto = t.IdTarea
                              AND ISNULL(c.Activa, 1) = 1
                              AND (
                                    c.EsPublico = 1
                                 OR UPPER(LTRIM(RTRIM(ISNULL(c.UsuarioDestino, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                              )
                        )
                  )
            )
            THEN 1 ELSE 0 END;
            """,
            new { IdTarea = idTarea, Usuario = usuario },
            cancellationToken: ct)) == 1;
    }

    private static string ToSharedObjectType(TareaObjetoCompartidoTipo tipo)
        => tipo switch
        {
            TareaObjetoCompartidoTipo.Lista => "LISTA",
            TareaObjetoCompartidoTipo.Nota => "NOTA",
            TareaObjetoCompartidoTipo.Tarea => "TAREA",
            _ => throw new InvalidOperationException("Tipo de elemento no válido para compartir.")
        };

    private static async Task SaveSharingRulesAsync(
        SqlConnection cn,
        string tipoObjeto,
        long idObjeto,
        bool compartirConTodos,
        IEnumerable<string> usuarios,
        string usuarioAlta,
        SqlTransaction? tx,
        CancellationToken ct)
    {
        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ALFACORE_TAREAS_COMPARTIDOS
            SET Activa = 0
            WHERE TipoObjeto = @TipoObjeto
              AND IdObjeto = @IdObjeto
              AND ISNULL(Activa, 1) = 1;
            """,
            new { TipoObjeto = tipoObjeto, IdObjeto = idObjeto },
            transaction: tx,
            cancellationToken: ct));

        if (compartirConTodos)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ALFACORE_TAREAS_COMPARTIDOS
                    (TipoObjeto, IdObjeto, UsuarioDestino, EsPublico, UsuarioAlta, FechaHoraAlta, Activa)
                VALUES
                    (@TipoObjeto, @IdObjeto, NULL, 1, @UsuarioAlta, GETDATE(), 1);
                """,
                new { TipoObjeto = tipoObjeto, IdObjeto = idObjeto, UsuarioAlta = usuarioAlta },
                transaction: tx,
                cancellationToken: ct));
            return;
        }

        var users = usuarios
            .Select(NormalizeUser)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !string.Equals(x, usuarioAlta, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var destino in users)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ALFACORE_TAREAS_COMPARTIDOS
                    (TipoObjeto, IdObjeto, UsuarioDestino, EsPublico, UsuarioAlta, FechaHoraAlta, Activa)
                VALUES
                    (@TipoObjeto, @IdObjeto, @UsuarioDestino, 0, @UsuarioAlta, GETDATE(), 1);
                """,
                new { TipoObjeto = tipoObjeto, IdObjeto = idObjeto, UsuarioDestino = destino, UsuarioAlta = usuarioAlta },
                transaction: tx,
            cancellationToken: ct));
        }
    }

    private static async Task<long> SaveGroupHeaderAsync(
        SqlConnection cn,
        string groupName,
        string usuarioAccion,
        SqlTransaction tx,
        CancellationToken ct)
    {
        var existingId = await cn.ExecuteScalarAsync<long?>(new CommandDefinition(
            """
            SELECT TOP 1 IdGrupo
            FROM dbo.ALFACORE_TAREAS_GRUPOS
            WHERE UPPER(LTRIM(RTRIM(Nombre))) = UPPER(LTRIM(RTRIM(@Nombre)))
            ORDER BY IdGrupo;
            """,
            new { Nombre = groupName },
            transaction: tx,
            cancellationToken: ct));

        if (existingId is long idGrupo)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS_GRUPOS
                SET Nombre = @Nombre,
                    Activa = 1,
                    FechaHoraModificacion = GETDATE(),
                    UsuarioModificacion = @UsuarioAccion
                WHERE IdGrupo = @IdGrupo;
                """,
                new { IdGrupo = idGrupo, Nombre = groupName, UsuarioAccion = usuarioAccion },
                transaction: tx,
                cancellationToken: ct));
            return idGrupo;
        }

        return await cn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO dbo.ALFACORE_TAREAS_GRUPOS
                (Nombre, UsuarioAlta, FechaHoraAlta, Activa)
            OUTPUT INSERTED.IdGrupo
            VALUES
                (@Nombre, @UsuarioAccion, GETDATE(), 1);
            """,
            new { Nombre = groupName, UsuarioAccion = usuarioAccion },
            transaction: tx,
            cancellationToken: ct));
    }

    private static bool HasSharingSelection(bool compartirConTodos, IEnumerable<string> usuarios)
        => compartirConTodos || usuarios.Any(x => !string.IsNullOrWhiteSpace(x));

    private static async Task EnsureTaskSharingReadyAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_COMPARTIDOS', N'U') IS NULL
            BEGIN
                SELECT CAST(0 AS bit);
                RETURN;
            END;

            SELECT CAST(CASE WHEN EXISTS
            (
                SELECT 1
                FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_COMPARTIDOS')
                  AND name = N'CK_ALFACORE_TAREAS_COMPARTIDOS_Tipo'
                  AND definition LIKE N'%TAREA%'
            )
            THEN 1 ELSE 0 END AS bit);
            """;

        var ready = await cn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, cancellationToken: ct));
        if (!ready)
            throw new InvalidOperationException("La base activa todavía no tiene aplicada la actualización para compartir tareas individuales. Ejecutá Actualizaciones y volvé a guardar la tarea.");
    }

    private static async Task EnsureTaskAttachmentsReadyAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await HasTaskAttachmentsTableAsync(cn, ct))
            throw new InvalidOperationException("La base activa todavía no tiene aplicada la actualización para adjuntos de tareas. Ejecutá Actualizaciones y volvé a guardar la tarea.");
    }

    private static async Task EnsureTaskGroupsReadyAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteAsync(new CommandDefinition(
            """
            IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_GRUPOS', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ALFACORE_TAREAS_GRUPOS
                (
                    IdGrupo bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    Nombre nvarchar(50) NOT NULL,
                    UsuarioAlta nvarchar(120) NULL,
                    FechaHoraAlta datetime NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_GRUPOS_FechaAlta DEFAULT (GETDATE()),
                    UsuarioModificacion nvarchar(120) NULL,
                    FechaHoraModificacion datetime NULL,
                    Activa bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_GRUPOS_Activa DEFAULT (1)
                );

                CREATE INDEX IX_ALFACORE_TAREAS_GRUPOS_Nombre
                    ON dbo.ALFACORE_TAREAS_GRUPOS (Nombre, Activa);
            END;

            IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS
                (
                    IdGrupoMiembro bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    IdGrupo bigint NULL,
                    Grupo nvarchar(50) NOT NULL,
                    Usuario nvarchar(120) NOT NULL,
                    UsuarioAlta nvarchar(120) NULL,
                    FechaHoraAlta datetime NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_GRUPOS_MIEMBROS_FechaAlta DEFAULT (GETDATE()),
                    UsuarioModificacion nvarchar(120) NULL,
                    FechaHoraModificacion datetime NULL,
                    Activa bit NOT NULL CONSTRAINT DF_ALFACORE_TAREAS_GRUPOS_MIEMBROS_Activa DEFAULT (1)
                );

                CREATE INDEX IX_ALFACORE_TAREAS_GRUPOS_MIEMBROS_GrupoUsuario
                    ON dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS (Grupo, Usuario, Activa);
            END;

            IF COL_LENGTH(N'dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS', N'IdGrupo') IS NULL
            BEGIN
                ALTER TABLE dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS
                ADD IdGrupo bigint NULL;
            END;

            INSERT INTO dbo.ALFACORE_TAREAS_GRUPOS (Nombre, UsuarioAlta, FechaHoraAlta, Activa)
            SELECT src.Nombre, N'Sistema', GETDATE(), 1
            FROM
            (
                SELECT Nombre = N'Soporte'
                UNION ALL
                SELECT Nombre = N'Programación'
                UNION
                SELECT DISTINCT LTRIM(RTRIM(Grupo)) AS Nombre
                FROM dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS
                WHERE LTRIM(RTRIM(ISNULL(Grupo, N''))) <> N''
            ) src
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.ALFACORE_TAREAS_GRUPOS g
                WHERE UPPER(LTRIM(RTRIM(g.Nombre))) = UPPER(LTRIM(RTRIM(src.Nombre)))
            );

            UPDATE gm
            SET IdGrupo = g.IdGrupo
            FROM dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS gm
            INNER JOIN dbo.ALFACORE_TAREAS_GRUPOS g
                ON UPPER(LTRIM(RTRIM(g.Nombre))) = UPPER(LTRIM(RTRIM(gm.Grupo)))
            WHERE gm.IdGrupo IS NULL;
            """,
            cancellationToken: ct));

    private static void ValidateAttachment(TareaAdjuntoUploadDto adjunto)
    {
        if (adjunto.Contenido.Length == 0 || adjunto.TamanoBytes <= 0)
            throw new InvalidOperationException("Uno de los adjuntos está vacío.");
        if (adjunto.TamanoBytes > MaxAttachmentBytes || adjunto.Contenido.LongLength > MaxAttachmentBytes)
            throw new InvalidOperationException("Uno de los adjuntos supera el máximo permitido de 50 MB.");

        var mimeType = (adjunto.MimeType ?? string.Empty).Trim();
        if (!AllowedAttachmentPrefixes.Any(prefix => mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Solo se permiten imágenes, audios y videos en tareas.");
    }

    private async Task NotifyTaskAssignmentAsync(SqlConnection cn, long idTarea, string titulo, string usuarioAsignado, string usuarioAccion, CancellationToken ct)
        => await NotifyTaskByWhatsAppAsync(cn, "NotifyTaskAssignment", TaskAssignmentTemplateConfigKey, TaskAssignmentTemplateMetaName, idTarea, titulo, usuarioAsignado, usuarioAccion, ct, null, TaskAssignmentLegacyTemplateMetaName);

    private async Task NotifyTaskCompletionAsync(SqlConnection cn, long idTarea, string titulo, string usuarioAsignado, string usuarioDestino, string usuarioAccion, CancellationToken ct)
        => await NotifyTaskByWhatsAppAsync(cn, "NotifyTaskCompletion", TaskCompletionTemplateConfigKey, TaskCompletionTemplateMetaName, idTarea, titulo, usuarioAsignado, usuarioAccion, ct, usuarioDestino);

    private async Task NotifyTaskByWhatsAppAsync(SqlConnection cn, string action, string templateConfigKey, string defaultTemplateMetaName, long idTarea, string titulo, string usuarioAsignado, string usuarioAccion, CancellationToken ct, string? usuarioDestino = null, params string[] fallbackTemplateNames)
    {
        try
        {
            if (!await TaskWhatsAppNotificationsEnabledAsync(cn, ct))
                return;

            var templateName = await ReadTaskConfigValueAsync(cn, templateConfigKey, ct);
            var template = await FindTaskTemplateAsync(BuildTaskTemplateCandidates(templateName, defaultTemplateMetaName, fallbackTemplateNames), ct);
            if (template is null)
                return;

            var recipients = await GetTaskNotificationRecipientsAsync(cn, string.IsNullOrWhiteSpace(usuarioDestino) ? usuarioAsignado : usuarioDestino, ct);
            if (recipients.Count == 0)
                return;

            var values = new List<string>
            {
                titulo,
                DisplayAssignees(usuarioAsignado),
                usuarioAccion,
                DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR"))
            };

            foreach (var recipient in recipients)
            {
                var conversation = await conversacionesService.CreateOrGetWhatsAppConversationAsync(new ConversacionCrearWhatsAppRequest
                {
                    TelefonoWhatsApp = recipient.TelefonoWhatsApp
                }, ct);

                await conversacionesService.SendTemplateMessageAsync(new ConversacionPlantillaSendRequest
                {
                    IdConversacion = conversation.IdConversacion,
                    IdPlantilla = template.IdPlantilla,
                    ValoresVariables = values,
                    UsuarioAccion = usuarioAccion,
                    SistemaAccion = SistemaFijo
                }, ct);
            }
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                ModuleName,
                action,
                ex,
                "No se pudo enviar la notificacion de WhatsApp de la tarea.",
                new { idTarea, titulo, usuarioAsignado, usuarioDestino, usuarioAccion, templateConfigKey, defaultTemplateMetaName, fallbackTemplateNames },
                AppEventSeverity.Warning,
                ct);
        }
    }

    private async Task<ConversacionPlantillaDto?> FindTaskTemplateAsync(IEnumerable<string> templateNameOrIds, CancellationToken ct)
    {
        foreach (var templateNameOrId in templateNameOrIds)
        {
            var value = (templateNameOrId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idPlantilla) && idPlantilla > 0)
            {
                var byId = await conversacionesService.GetTemplateAsync(idPlantilla, ct);
                if (byId is not null)
                    return byId;
                continue;
            }

            var matches = await conversacionesService.GetTemplatesAsync(new ConversacionPlantillaFilters
            {
                Search = value,
                IncluirInactivas = false
            }, ct);

            var match = matches.FirstOrDefault(x =>
                string.Equals(x.NombreMeta, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.NombreVisible, value, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return null;
    }

    private static IReadOnlyList<string> BuildTaskTemplateCandidates(string configuredValue, string defaultTemplateMetaName, IEnumerable<string> fallbackTemplateNames)
        => new[] { configuredValue, defaultTemplateMetaName }
            .Concat(fallbackTemplateNames)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static async Task<bool> TaskWhatsAppNotificationsEnabledAsync(SqlConnection cn, CancellationToken ct)
    {
        var value = await ReadTaskConfigValueAsync(cn, TaskWhatsAppEnabledConfigKey, ct);
        return string.IsNullOrWhiteSpace(value)
               || !new[] { "NO", "N", "FALSE", "0" }.Contains(value.Trim().ToUpperInvariant(), StringComparer.Ordinal);
    }

    private static async Task<string> ReadTaskConfigValueAsync(SqlConnection cn, string key, CancellationToken ct)
    {
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NULL THEN 0 ELSE 1 END;",
                cancellationToken: ct)) != 1)
            return string.Empty;

        return await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT TOP 1 ISNULL(VALOR, N'')
            FROM dbo.TA_CONFIGURACION
            WHERE CLAVE = @Clave;
            """,
            new { Clave = key },
            cancellationToken: ct)) ?? string.Empty;
    }

    private static async Task<TareaNotificationSnapshot?> GetTaskNotificationSnapshotAsync(SqlConnection cn, long idTarea, CancellationToken ct)
        => await cn.QueryFirstOrDefaultAsync<TareaNotificationSnapshot>(new CommandDefinition(
            """
            SELECT
                ISNULL(Titulo, '') AS Titulo,
                ISNULL(UsuarioAsignado, '') AS UsuarioAsignado,
                ISNULL(UsuarioAlta, '') AS UsuarioAlta,
                ISNULL(Estado, 'PENDIENTE') AS Estado
            FROM dbo.ALFACORE_TAREAS
            WHERE IdTarea = @IdTarea
              AND ISNULL(Activa, 1) = 1;
            """,
            new { IdTarea = idTarea },
            cancellationToken: ct));

    private static async Task<IReadOnlyList<TaskNotificationRecipient>> GetTaskNotificationRecipientsAsync(SqlConnection cn, string usuarioAsignado, CancellationToken ct)
    {
        var tokens = ParseAssignees(usuarioAsignado).ToList();
        if (tokens.Count == 0)
            return Array.Empty<TaskNotificationRecipient>();

        await EnsureTaskGroupsReadyAsync(cn, ct);

        var technicians = (await cn.QueryAsync<TaskTechnicianRow>(new CommandDefinition(
            """
            SELECT
                ISNULL(Nombre, '') AS Nombre,
                ISNULL(Telefono, '') AS TelefonoWhatsApp,
                ISNULL(UsuarioAsociado, '') AS UsuarioAsociado
            FROM dbo.V_TA_Tecnicos
            WHERE ISNULL(Baja, 0) = 0;
            """,
            cancellationToken: ct))).ToList();

        var groups = (await cn.QueryAsync<TareaGroupMemberRow>(new CommandDefinition(
            """
            SELECT
                ISNULL(g.Nombre, '') AS Grupo,
                ISNULL(gm.Usuario, '') AS Usuario
            FROM dbo.ALFACORE_TAREAS_GRUPOS g
            INNER JOIN dbo.ALFACORE_TAREAS_GRUPOS_MIEMBROS gm ON gm.IdGrupo = g.IdGrupo
                AND ISNULL(gm.Activa, 1) = 1
            WHERE ISNULL(g.Activa, 1) = 1;
            """,
            cancellationToken: ct))).ToList();

        var selectedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (string.Equals(token, "Todos", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var tech in technicians)
                    selectedUsers.Add(FirstNonEmpty(tech.UsuarioAsociado, tech.Nombre));
                continue;
            }

            var groupMembers = groups
                .Where(x => string.Equals(x.Grupo, token, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Usuario)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            if (groupMembers.Length > 0)
            {
                foreach (var member in groupMembers)
                    selectedUsers.Add(member);
            }
            else
            {
                selectedUsers.Add(token);
            }
        }

        return technicians
            .Where(x => selectedUsers.Contains(x.Nombre) || selectedUsers.Contains(x.UsuarioAsociado))
            .Where(x => !string.IsNullOrWhiteSpace(x.TelefonoWhatsApp))
            .GroupBy(x => NormalizePhoneForDistinct(x.TelefonoWhatsApp), StringComparer.OrdinalIgnoreCase)
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => x.First())
            .Select(x => new TaskNotificationRecipient
            {
                Nombre = FirstNonEmpty(x.Nombre, x.UsuarioAsociado),
                TelefonoWhatsApp = x.TelefonoWhatsApp
            })
            .ToList();
    }

    private static IEnumerable<string> ParseAssignees(string? usuarios)
        => (usuarios ?? string.Empty)
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x));

    private static bool AssignmentsEquivalent(string? left, string? right)
    {
        var a = ParseAssignees(left).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var b = ParseAssignees(right).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        return a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
    }

    private static string DisplayAssignees(string usuarioAsignado)
        => string.Join(", ", ParseAssignees(usuarioAsignado));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string NormalizePhoneForDistinct(string? value)
        => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string Truncate(string? value, int maxLength)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? "adjunto" : value.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (SqlException ex) when (ex.Number is 208 or 207)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException("El módulo Tareas todavía no está inicializado en la base activa. Ejecutá las actualizaciones y recargá la pantalla.", incidentId, ex);
        }
        catch (SqlException ex) when (ex.Number == 547 && ex.Message.Contains("ALFACORE_TAREAS_COMPARTIDOS", StringComparison.OrdinalIgnoreCase))
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException("La base activa todavía no permite compartir tareas individuales. Ejecutá Actualizaciones y volvé a guardar la tarea.", incidentId, ex);
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private async Task ExecuteLoggedAsync(
        string module,
        string action,
        Func<CancellationToken, Task> operation,
        string userMessage,
        CancellationToken ct)
        => await ExecuteLoggedAsync(module, action, async token =>
        {
            await operation(token);
            return true;
        }, userMessage, ct);

    private static string NormalizeUser(string? usuario)
        => string.IsNullOrWhiteSpace(usuario) ? Environment.UserName : usuario.Trim();

    private static string NormalizeGroupName(string? grupo)
        => Truncate(string.Join(" ", (grupo ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)), 50);

    private static string NormalizeAssignees(string? usuarios, string fallbackUser)
    {
        var values = (usuarios ?? string.Empty)
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? fallbackUser : string.Join(",", values);
    }

    private static string NormalizeState(string? estado)
    {
        var value = (estado ?? string.Empty).Trim().ToUpperInvariant();
        return TareaEstadoKeys.All.Contains(value, StringComparer.Ordinal) ? value : TareaEstadoKeys.Pendiente;
    }

    private static string NormalizePriority(string? prioridad)
    {
        var value = (prioridad ?? string.Empty).Trim().ToUpperInvariant();
        return TareaPrioridadKeys.All.Contains(value, StringComparer.Ordinal) ? value : TareaPrioridadKeys.Media;
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class TareaShareRow
    {
        public string TipoObjeto { get; set; } = string.Empty;
        public long IdObjeto { get; set; }
        public bool EsPublico { get; set; }
        public string UsuarioDestino { get; set; } = string.Empty;
    }

    private sealed class TareaGroupMemberRow
    {
        public string Grupo { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
    }

    private sealed class TareaAdjuntoRecord
    {
        public long IdAdjunto { get; set; }
        public long IdTarea { get; set; }
        public string NombreArchivo { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public long TamanoBytes { get; set; }
        public byte[] Contenido { get; set; } = [];
        public string UsuarioAlta { get; set; } = string.Empty;
        public DateTime FechaHoraAlta { get; set; }
    }

    private sealed class TareaNotificationSnapshot
    {
        public string Titulo { get; set; } = string.Empty;
        public string UsuarioAsignado { get; set; } = string.Empty;
        public string UsuarioAlta { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
    }

    private sealed class TaskTechnicianRow
    {
        public string Nombre { get; set; } = string.Empty;
        public string TelefonoWhatsApp { get; set; } = string.Empty;
        public string UsuarioAsociado { get; set; } = string.Empty;
    }

    private sealed class TaskNotificationRecipient
    {
        public string Nombre { get; set; } = string.Empty;
        public string TelefonoWhatsApp { get; set; } = string.Empty;
    }
}

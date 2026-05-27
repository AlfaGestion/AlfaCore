using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class TareasService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : ITareasService
{
    private const string ModuleName = "Tareas";
    private const string SistemaFijo = "CN000PR";

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
            var userActiveFilter = await HasUsuarioActivoColumnAsync(cn, token)
                ? "AND ISNULL(Activo, 1) = 1"
                : string.Empty;

            var sql = $"""
                SELECT
                    l.IdLista,
                    l.Nombre,
                    CONVERT(bit, ISNULL(l.EsDefault, 0)) AS EsDefault,
                    SUM(CASE WHEN t.IdTarea IS NOT NULL AND t.Estado <> 'COMPLETADA' AND ISNULL(t.Activa, 1) = 1 THEN 1 ELSE 0 END) AS Pendientes,
                    SUM(CASE WHEN t.IdTarea IS NOT NULL AND t.Estado = 'COMPLETADA' AND ISNULL(t.Activa, 1) = 1 THEN 1 ELSE 0 END) AS Completadas
                FROM dbo.ALFACORE_TAREAS_LISTAS l
                LEFT JOIN dbo.ALFACORE_TAREAS t ON t.IdLista = l.IdLista
                WHERE ISNULL(l.Activa, 1) = 1
                GROUP BY l.IdLista, l.Nombre, l.EsDefault
                ORDER BY ISNULL(l.EsDefault, 0) DESC, l.Nombre;

                SELECT
                    t.IdTarea,
                    t.IdLista,
                    ISNULL(l.Nombre, '') AS ListaNombre,
                    ISNULL(t.Titulo, '') AS Titulo,
                    ISNULL(CONVERT(nvarchar(max), t.Descripcion), '') AS Descripcion,
                    t.FechaVencimiento,
                    ISNULL(t.UsuarioAsignado, '') AS UsuarioAsignado,
                    ISNULL(t.Estado, 'PENDIENTE') AS Estado,
                    ISNULL(t.UsuarioAlta, '') AS UsuarioAlta,
                    t.FechaHoraAlta,
                    t.FechaHoraModificacion,
                    t.FechaHoraCompletada
                FROM dbo.ALFACORE_TAREAS t
                INNER JOIN dbo.ALFACORE_TAREAS_LISTAS l ON l.IdLista = t.IdLista
                WHERE ISNULL(t.Activa, 1) = 1
                  AND ISNULL(l.Activa, 1) = 1
                  AND t.Estado <> 'COMPLETADA'
                ORDER BY
                    CASE WHEN t.FechaVencimiento IS NULL THEN 1 ELSE 0 END,
                    t.FechaVencimiento,
                    t.FechaHoraAlta DESC;

                SELECT
                    t.IdTarea,
                    t.IdLista,
                    ISNULL(l.Nombre, '') AS ListaNombre,
                    ISNULL(t.Titulo, '') AS Titulo,
                    ISNULL(CONVERT(nvarchar(max), t.Descripcion), '') AS Descripcion,
                    t.FechaVencimiento,
                    ISNULL(t.UsuarioAsignado, '') AS UsuarioAsignado,
                    ISNULL(t.Estado, 'PENDIENTE') AS Estado,
                    ISNULL(t.UsuarioAlta, '') AS UsuarioAlta,
                    t.FechaHoraAlta,
                    t.FechaHoraModificacion,
                    t.FechaHoraCompletada
                FROM dbo.ALFACORE_TAREAS t
                INNER JOIN dbo.ALFACORE_TAREAS_LISTAS l ON l.IdLista = t.IdLista
                WHERE ISNULL(t.Activa, 1) = 1
                  AND ISNULL(l.Activa, 1) = 1
                  AND t.Estado = 'COMPLETADA'
                ORDER BY t.FechaHoraCompletada DESC, t.IdTarea DESC;

                SELECT
                    IdNota,
                    ISNULL(Texto, '') AS Texto,
                    Fecha,
                    CONVERT(bit, ISNULL(Completada, 0)) AS Completada,
                    FechaHoraAlta,
                    FechaHoraCompletada
                FROM dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                WHERE ISNULL(Activa, 1) = 1
                  AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
                ORDER BY Completada, FechaHoraAlta DESC;

                SELECT
                    ISNULL(NOMBRE, '') AS Nombre,
                    ISNULL(email_de, '') AS Email
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND ISNULL(EsGrupo, 0) = 0
                  {userActiveFilter}
                ORDER BY NOMBRE;
                """;

            using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, new { Usuario = user, Sistema = SistemaFijo }, cancellationToken: token));
            return new TareasPageDto
            {
                Listas = (await multi.ReadAsync<TareaListaDto>()).ToList(),
                Tareas = (await multi.ReadAsync<TareaItemDto>()).ToList(),
                Completadas = (await multi.ReadAsync<TareaItemDto>()).ToList(),
                NotasRapidas = (await multi.ReadAsync<TareaNotaRapidaDto>()).ToList(),
                Usuarios = (await multi.ReadAsync<TareaUsuarioDto>()).ToList()
            };
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
            await EnsureDefaultListAsync(cn, request.UsuarioAccion, token);

            if (request.IdLista is int id && id > 0)
            {
                var isDefault = await cn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT CONVERT(bit, ISNULL(EsDefault, 0)) FROM dbo.ALFACORE_TAREAS_LISTAS WHERE IdLista = @IdLista;",
                    new { IdLista = id },
                    cancellationToken: token));

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
            var defaultId = await EnsureDefaultListAsync(cn, usuarioAccion, token);

            var isDefault = await cn.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT CONVERT(bit, ISNULL(EsDefault, 0)) FROM dbo.ALFACORE_TAREAS_LISTAS WHERE IdLista = @IdLista;",
                new { IdLista = idLista },
                cancellationToken: token));

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

    public Task<long> SaveTaskAsync(TareaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveTask", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var titulo = request.Titulo.Trim();
            if (string.IsNullOrWhiteSpace(titulo))
                throw new InvalidOperationException("El título de la tarea es obligatorio.");

            var estado = NormalizeState(request.Estado);
            var user = NormalizeUser(request.UsuarioAccion);
            var asignado = string.IsNullOrWhiteSpace(request.UsuarioAsignado) ? user : request.UsuarioAsignado.Trim();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var defaultId = await EnsureDefaultListAsync(cn, user, token);
            var idLista = request.IdLista <= 0 ? defaultId : request.IdLista;

            if (request.IdTarea is long id && id > 0)
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.ALFACORE_TAREAS
                    SET IdLista = @IdLista,
                        Titulo = @Titulo,
                        Descripcion = @Descripcion,
                        FechaVencimiento = @FechaVencimiento,
                        UsuarioAsignado = @UsuarioAsignado,
                        Estado = @Estado,
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
                        Estado = estado
                    },
                    cancellationToken: token));
                return id;
            }

            return await cn.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                INSERT INTO dbo.ALFACORE_TAREAS
                    (IdLista, Titulo, Descripcion, FechaVencimiento, UsuarioAsignado, Estado, UsuarioAlta, FechaHoraAlta, Activa)
                OUTPUT INSERTED.IdTarea
                VALUES
                    (@IdLista, @Titulo, @Descripcion, @FechaVencimiento, @UsuarioAsignado, @Estado, @UsuarioAlta, GETDATE(), 1);
                """,
                new
                {
                    IdLista = idLista,
                    Titulo = titulo,
                    Descripcion = EmptyToNull(request.Descripcion),
                    request.FechaVencimiento,
                    UsuarioAsignado = asignado,
                    Estado = estado,
                    UsuarioAlta = user
                },
                cancellationToken: token));
        }, "No se pudo guardar la tarea.", ct);

    public Task ChangeTaskStateAsync(long idTarea, string estado, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ChangeTaskState", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_TAREAS
                SET Estado = @Estado,
                    FechaHoraModificacion = GETDATE(),
                    FechaHoraCompletada = CASE WHEN @Estado = 'COMPLETADA' THEN GETDATE() ELSE NULL END
                WHERE IdTarea = @IdTarea
                  AND ISNULL(Activa, 1) = 1;
                """,
                new { IdTarea = idTarea, Estado = NormalizeState(estado) },
                cancellationToken: token));
        }, "No se pudo cambiar el estado de la tarea.", ct);

    public Task DuplicateTaskAsync(long idTarea, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DuplicateTask", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ALFACORE_TAREAS
                    (IdLista, Titulo, Descripcion, FechaVencimiento, UsuarioAsignado, Estado, UsuarioAlta, FechaHoraAlta, Activa)
                SELECT
                    IdLista,
                    CONCAT(Titulo, N' (copia)'),
                    Descripcion,
                    FechaVencimiento,
                    UsuarioAsignado,
                    'PENDIENTE',
                    @Usuario,
                    GETDATE(),
                    1
                FROM dbo.ALFACORE_TAREAS
                WHERE IdTarea = @IdTarea
                  AND ISNULL(Activa, 1) = 1;
                """,
                new { IdTarea = idTarea, Usuario = NormalizeUser(usuarioAccion) },
                cancellationToken: token));
        }, "No se pudo duplicar la tarea.", ct);

    public Task DeleteTaskAsync(long idTarea, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DeleteTask", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
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
        => ExecuteLoggedAsync(ModuleName, "AddQuickNote", async token =>
        {
            var clean = texto.Trim();
            if (string.IsNullOrWhiteSpace(clean))
                throw new InvalidOperationException("La nota rápida no puede estar vacía.");

            await using var cn = new SqlConnection(ConnectionString);
            return await cn.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                INSERT INTO dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
                    (Texto, Fecha, Usuario, Completada, FechaHoraAlta, Activa)
                OUTPUT INSERTED.IdNota
                VALUES
                    (@Texto, CONVERT(date, GETDATE()), @Usuario, 0, GETDATE(), 1);
                """,
                new { Texto = clean, Usuario = NormalizeUser(usuario) },
                cancellationToken: token));
        }, "No se pudo guardar la nota rápida.", ct);

    public Task ToggleQuickNoteAsync(long idNota, bool completada, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ToggleQuickNote", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
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

    private static async Task<int> EnsureDefaultListAsync(SqlConnection cn, string usuario, CancellationToken ct)
    {
        const string sql = """
            IF NOT EXISTS (
                SELECT 1
                FROM dbo.ALFACORE_TAREAS_LISTAS
                WHERE EsDefault = 1
                  AND ISNULL(Activa, 1) = 1
            )
            BEGIN
                INSERT INTO dbo.ALFACORE_TAREAS_LISTAS (Nombre, EsDefault, UsuarioAlta, FechaHora_Alta, Activa)
                VALUES (N'Mis tareas', 1, @Usuario, GETDATE(), 1);
            END;

            SELECT TOP (1) IdLista
            FROM dbo.ALFACORE_TAREAS_LISTAS
            WHERE EsDefault = 1
              AND ISNULL(Activa, 1) = 1
            ORDER BY IdLista;
            """;

        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Usuario = NormalizeUser(usuario) }, cancellationToken: ct));
    }

    private static async Task<bool> HasUsuarioActivoColumnAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN COL_LENGTH('dbo.TA_USUARIOS', 'Activo') IS NULL THEN 0 ELSE 1 END;",
            cancellationToken: ct)) == 1;

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

    private static string NormalizeState(string? estado)
    {
        var value = (estado ?? string.Empty).Trim().ToUpperInvariant();
        return TareaEstadoKeys.All.Contains(value, StringComparer.Ordinal) ? value : TareaEstadoKeys.Pendiente;
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Seguridad &gt; Autorización de tareas y Seguridad &gt; Perfiles. Dónde se guarda cada permiso:
/// <list type="bullet">
/// <item>Opciones del menú y acciones comunes: dbo.ALFACORE_TAREAS_WEB.</item>
/// <item>Acciones explícitas (<see cref="PermisosWebCatalogo"/>): su propio almacén.</item>
/// <item>Perfil: dbo.ALFACORE_PERFILES_WEB*. Sus permisos se copian a esas mismas tablas con IdPerfil, así todo lo
/// que lee los permisos sigue funcionando igual. IdPerfil NULL = permiso propio del usuario.</item>
/// </list>
/// </summary>
public sealed class AutorizacionTareasService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    ICentralAdminService centralAdminService,
    IAppUserSessionService appUserSession) : IAutorizacionTareasService
{
    private const string ModuleName = "AutorizacionTareas";
    private const string DefaultUnidadNegocioVisual = "1";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    private string Editor => appUserSession.CurrentUser?.UserName?.Trim() is { Length: > 0 } u ? u : "AlfaCore";

    public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosAsync(string sistemaPermisos, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<UsuarioSistemaDto>>(ModuleName, "GetUsuarios", async token =>
        {
            if (string.IsNullOrWhiteSpace(sistemaPermisos))
                throw new InvalidOperationException("No se recibió el sistema de permisos.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "TA_USUARIOS", token))
                return [];

            var hasActivo = await ColumnExistsAsync(cn, "TA_USUARIOS", "Activo", token);
            var sql = $"""
                SELECT
                    ISNULL(NOMBRE, '') AS Nombre,
                    ISNULL(SISTEMA, '') AS Sistema,
                    {(hasActivo ? "ISNULL(Activo, 1)" : "CAST(1 AS bit)")} AS Activo,
                    ISNULL(Administrador, 0) AS Administrador,
                    ISNULL(EsGrupo, 0) AS EsGrupo
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND ISNULL(EsGrupo, 0) = 0
                  {(hasActivo ? "AND ISNULL(Activo, 1) = 1" : string.Empty)}
                ORDER BY NOMBRE;
                """;

            var rows = await cn.QueryAsync<UsuarioSistemaDto>(new CommandDefinition(sql, new
            {
                Sistema = sistemaPermisos.Trim().ToUpperInvariant()
            }, cancellationToken: token));

            return rows.ToArray();
        }, "No se pudieron cargar los usuarios del sistema.", ct);

    public Task<AutorizacionTareasDto> GetAutorizacionAsync(string menuSistema, string sistemaPermisos, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetAutorizacion", async token =>
        {
            if (string.IsNullOrWhiteSpace(menuSistema))
                throw new InvalidOperationException("No se recibió el menú del sistema.");
            if (string.IsNullOrWhiteSpace(sistemaPermisos))
                throw new InvalidOperationException("No se recibió el sistema de permisos.");
            if (string.IsNullOrWhiteSpace(usuario))
                throw new InvalidOperationException("Seleccioná un usuario antes de consultar.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "ALFACORE_MENU_WEB", token) || !await TableExistsAsync(cn, "ALFACORE_TAREAS_WEB", token))
                throw new InvalidOperationException("La base activa no tiene completas las tablas web de menú o permisos.");

            var userData = await LoadUserDataAsync(cn, sistemaPermisos, usuario, token);
            var unidadesNegocio = await LoadUnidadesNegocioAsync(cn, token);
            var cajas = await LoadCajasAsync(cn, token);
            var depositos = await LoadDepositosAsync(cn, token);
            var unidadNegocio = ResolveUnidadNegocio(userData.UNegocio, unidadesNegocio);
            var idCaja = ResolveOptionalCatalogValue(userData.IdCaja, cajas);
            var idDeposito = ResolveOptionalCatalogValue(userData.IdDeposito, depositos);

            var esquema = await LoadEsquemaAsync(cn, token);
            var claves = await LoadClavesUsuarioAsync(cn, null, esquema, sistemaPermisos, usuario, token);
            var menu = await LoadMenuAsync(cn, menuSistema, token);
            var items = BuildTree(menu, key => claves.Propias.Contains(key) || claves.DePerfil.Contains(key), claves.DePerfil.Contains);

            IReadOnlyList<PerfilWebResumenDto> perfiles = esquema.Perfiles ? await LoadPerfilesAsync(cn, sistemaPermisos, token) : Array.Empty<PerfilWebResumenDto>();
            var idPerfil = esquema.Perfiles ? await LoadPerfilUsuarioAsync(cn, null, sistemaPermisos, usuario, token) : null;

            return new AutorizacionTareasDto
            {
                MenuSistema = menuSistema.Trim().ToUpperInvariant(),
                PermisosSistema = sistemaPermisos.Trim().ToUpperInvariant(),
                Usuario = usuario.Trim(),
                Administrador = userData.Administrador,
                VerProforma = userData.VerProforma,
                UNegocio = unidadNegocio,
                IdCaja = idCaja,
                IdDeposito = idDeposito,
                UnidadesNegocio = unidadesNegocio,
                Cajas = cajas,
                Depositos = depositos,
                Items = items,
                Restringido = claves.Restringido,
                IdPerfil = idPerfil,
                PerfilesDisponibles = esquema.Perfiles,
                Perfiles = perfiles
            };
        }, "No se pudo cargar la autorización de tareas.", ct);

    public Task GuardarAutorizacionAsync(GuardarAutorizacionTareasRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GuardarAutorizacion", async token =>
        {
            if (request is null)
                throw new InvalidOperationException("No se recibió la autorización a guardar.");
            if (string.IsNullOrWhiteSpace(request.MenuSistema))
                throw new InvalidOperationException("No se recibió el menú del sistema.");
            if (string.IsNullOrWhiteSpace(request.PermisosSistema))
                throw new InvalidOperationException("No se recibió el sistema de permisos.");
            if (string.IsNullOrWhiteSpace(request.Usuario))
                throw new InvalidOperationException("Seleccioná un usuario antes de grabar.");
            if (string.IsNullOrWhiteSpace(request.UNegocio))
                throw new InvalidOperationException("La unidad de negocio es obligatoria.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "ALFACORE_MENU_WEB", token) || !await TableExistsAsync(cn, "ALFACORE_TAREAS_WEB", token))
                throw new InvalidOperationException("La base activa no tiene completas las tablas web de menú o permisos.");

            var unidadesNegocio = await LoadUnidadesNegocioAsync(cn, token);
            var cajas = await LoadCajasAsync(cn, token);
            var depositos = await LoadDepositosAsync(cn, token);
            var unidadNegocio = ResolveUnidadNegocio(request.UNegocio, unidadesNegocio);
            var idCaja = ResolveOptionalCatalogValue(request.IdCaja, cajas);
            var idDeposito = ResolveOptionalCatalogValue(request.IdDeposito, depositos);
            var hasAdministrador = await ColumnExistsAsync(cn, "TA_USUARIOS", "Administrador", token);
            var hasIdCaja = await ColumnExistsAsync(cn, "TA_USUARIOS", "IDCAJA", token);
            var hasUNegocio = await ColumnExistsAsync(cn, "TA_USUARIOS", "UNegocio", token);
            var hasIdDeposito = await ColumnExistsAsync(cn, "TA_USUARIOS", "IDDEPOSITO", token);
            var hasVerProforma = await ColumnExistsAsync(cn, "TA_USUARIOS", "VerProforma", token);

            var esquema = await LoadEsquemaAsync(cn, token);
            var menu = await LoadMenuAsync(cn, request.MenuSistema, token);
            var managedKeys = menu.ManagedKeys;

            int? idPerfil = esquema.Perfiles ? request.IdPerfil : null;
            var clavesPerfil = idPerfil is int p
                ? await LoadClavesPerfilAsync(cn, null, p, token)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (idPerfil is not null && !await PerfilExisteAsync(cn, null, idPerfil.Value, request.PermisosSistema, token))
                throw new InvalidOperationException("El perfil elegido ya no existe. Recargá la pantalla.");

            // Lo que viene del perfil no se guarda como propio: se maneja desde el perfil. Pero si el usuario
            // ya lo tenía como propio, sigue siendo propio (así no lo pierde si después le sacan el perfil).
            var actuales = await LoadClavesUsuarioAsync(cn, null, esquema, request.PermisosSistema, request.Usuario, token);
            var selected = new HashSet<string>(
                request.TareasSeleccionadas
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Where(x => managedKeys.Contains(x) && (!clavesPerfil.Contains(x) || actuales.Propias.Contains(x))),
                StringComparer.OrdinalIgnoreCase);
            var expandedSelection = ExpandSelectionWithAncestors(selected, menu.ParentByKey, managedKeys);

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);

            try
            {
                await UpdateUserAssignmentsAsync(
                    cn,
                    tx,
                    request.PermisosSistema,
                    request.Usuario,
                    request.Administrador,
                    request.VerProforma,
                    unidadNegocio,
                    idCaja,
                    idDeposito,
                    hasAdministrador,
                    hasIdCaja,
                    hasUNegocio,
                    hasIdDeposito,
                    hasVerProforma,
                    token);

                await GuardarClavesPropiasAsync(cn, tx, esquema, request.PermisosSistema, request.Usuario, managedKeys, expandedSelection, token);

                if (esquema.Perfiles)
                {
                    await AsignarPerfilAsync(cn, tx, request.PermisosSistema, request.Usuario, idPerfil, token);
                    await SincronizarPerfilUsuarioAsync(cn, tx, esquema, menu, request.PermisosSistema, request.Usuario, idPerfil, clavesPerfil, token);
                }

                await tx.CommitAsync(token);
                await appEvents.LogAuditAsync(
                    ModuleName,
                    "GuardarAutorizacionWeb",
                    "ALFACORE_TAREAS_WEB",
                    $"{request.PermisosSistema.Trim().ToUpperInvariant()}::{request.Usuario.Trim().ToUpperInvariant()}",
                    "Permisos web actualizados.",
                    new
                    {
                        Usuario = request.Usuario.Trim(),
                        Sistema = request.PermisosSistema.Trim().ToUpperInvariant(),
                        request.Administrador,
                        UNegocio = unidadNegocio,
                        IdCaja = idCaja,
                        IdDeposito = idDeposito,
                        IdPerfil = idPerfil,
                        CantidadClaves = expandedSelection.Count,
                        Claves = expandedSelection.OrderBy(x => x).ToArray(),
                        Editor
                    },
                    token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar la autorización de tareas.", ct);

    // ---------------------------------------------------------------- Perfiles

    public Task<IReadOnlyList<PerfilWebResumenDto>> GetPerfilesAsync(string sistemaPermisos, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<PerfilWebResumenDto>>(ModuleName, "GetPerfiles", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var esquema = await LoadEsquemaAsync(cn, token);
            return esquema.Perfiles ? await LoadPerfilesAsync(cn, sistemaPermisos, token) : Array.Empty<PerfilWebResumenDto>();
        }, "No se pudieron cargar los perfiles.", ct);

    public Task<PerfilWebArbolDto> GetPerfilArbolAsync(string menuSistema, string sistemaPermisos, int? idPerfil, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPerfilArbol", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var esquema = await LoadEsquemaAsync(cn, token);
            if (!esquema.Perfiles)
                throw new InvalidOperationException("La base todavía no tiene perfiles. Actualizá AlfaCore y volvé a intentar.");

            PerfilWebResumenDto? perfil = null;
            if (idPerfil is int id)
            {
                perfil = (await LoadPerfilesAsync(cn, sistemaPermisos, token)).FirstOrDefault(x => x.IdPerfil == id)
                    ?? throw new InvalidOperationException("El perfil ya no existe.");
            }

            var claves = new HashSet<string>(perfil?.Claves ?? [], StringComparer.OrdinalIgnoreCase);
            var menu = await LoadMenuAsync(cn, menuSistema, token);
            return new PerfilWebArbolDto
            {
                Perfil = perfil,
                Items = BuildTree(menu, claves.Contains, _ => false)
            };
        }, "No se pudo cargar el perfil.", ct);

    public Task<int> GuardarPerfilAsync(GuardarPerfilWebRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GuardarPerfil", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var nombre = (request.Nombre ?? string.Empty).Trim();
            if (nombre.Length == 0)
                throw new AutorizacionReglaException("Poné un nombre al perfil (por ejemplo Vendedor o Administración).");
            if (nombre.Length > 80)
                throw new AutorizacionReglaException("El nombre del perfil puede tener hasta 80 caracteres.");
            var descripcion = (request.Descripcion ?? string.Empty).Trim();
            if (descripcion.Length > 300)
                descripcion = descripcion[..300];
            var sistema = request.Sistema.Trim().ToUpperInvariant();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var esquema = await LoadEsquemaAsync(cn, token);
            if (!esquema.Perfiles)
                throw new AutorizacionReglaException("La base todavía no tiene perfiles. Actualizá AlfaCore y volvé a intentar.");

            var menu = await LoadMenuAsync(cn, request.MenuSistema, token);
            var claves = request.Claves
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Where(menu.ManagedKeys.Contains)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var repetido = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(1) FROM dbo.ALFACORE_PERFILES_WEB
                WHERE UPPER(LTRIM(RTRIM(Sistema))) = @Sistema AND UPPER(LTRIM(RTRIM(Nombre))) = UPPER(@Nombre)
                  AND (@IdPerfil IS NULL OR IdPerfil <> @IdPerfil);
                """, new { Sistema = sistema, Nombre = nombre, request.IdPerfil }, cancellationToken: token));
            if (repetido > 0)
                throw new AutorizacionReglaException($"Ya hay un perfil llamado \"{nombre}\".");

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                int idPerfil;
                if (request.IdPerfil is int existente)
                {
                    if (!await PerfilExisteAsync(cn, tx, existente, sistema, token))
                        throw new AutorizacionReglaException("El perfil ya no existe. Recargá la pantalla.");
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.ALFACORE_PERFILES_WEB
                        SET Nombre = @Nombre, Descripcion = @Descripcion, FechaHoraModificacion = GETDATE()
                        WHERE IdPerfil = @IdPerfil;
                        """, new { Nombre = nombre, Descripcion = descripcion, IdPerfil = existente }, tx, cancellationToken: token));
                    idPerfil = existente;
                }
                else
                {
                    idPerfil = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                        INSERT INTO dbo.ALFACORE_PERFILES_WEB (Sistema, Nombre, Descripcion, UsuarioGrabacion)
                        VALUES (@Sistema, @Nombre, @Descripcion, @Editor);
                        SELECT CAST(SCOPE_IDENTITY() AS int);
                        """, new { Sistema = sistema, Nombre = nombre, Descripcion = descripcion, Editor }, tx, cancellationToken: token));
                }

                await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.ALFACORE_PERFILES_WEB_CLAVES WHERE IdPerfil = @IdPerfil;",
                    new { IdPerfil = idPerfil }, tx, cancellationToken: token));
                foreach (var clave in claves.OrderBy(x => x))
                {
                    await cn.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO dbo.ALFACORE_PERFILES_WEB_CLAVES (IdPerfil, Clave) VALUES (@IdPerfil, @Clave);",
                        new { IdPerfil = idPerfil, Clave = clave }, tx, cancellationToken: token));
                }

                // Los usuarios que ya tienen este perfil reciben los cambios en el momento.
                foreach (var (usuario, sistemaUsuario) in await LoadUsuariosPerfilAsync(cn, tx, idPerfil, token))
                    await SincronizarPerfilUsuarioAsync(cn, tx, esquema, menu, sistemaUsuario, usuario, idPerfil, claves, token);

                await tx.CommitAsync(token);
                await appEvents.LogAuditAsync(ModuleName, "GuardarPerfil", "ALFACORE_PERFILES_WEB",
                    idPerfil.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Perfil de permisos guardado.",
                    new { IdPerfil = idPerfil, Nombre = nombre, Claves = claves.OrderBy(x => x).ToArray(), Editor }, token);
                return idPerfil;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar el perfil.", ct);

    public Task EliminarPerfilAsync(string menuSistema, string sistemaPermisos, int idPerfil, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "EliminarPerfil", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var esquema = await LoadEsquemaAsync(cn, token);
            if (!esquema.Perfiles)
                throw new AutorizacionReglaException("La base todavía no tiene perfiles.");
            var menu = await LoadMenuAsync(cn, menuSistema, token);

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                if (!await PerfilExisteAsync(cn, tx, idPerfil, sistemaPermisos, token))
                    throw new AutorizacionReglaException("El perfil ya no existe. Recargá la pantalla.");

                // Los usuarios se quedan sin perfil y pierden solo lo que venía del perfil.
                var usuarios = await LoadUsuariosPerfilAsync(cn, tx, idPerfil, token);
                foreach (var (usuario, sistema) in usuarios)
                {
                    await AsignarPerfilAsync(cn, tx, sistema, usuario, null, token);
                    await SincronizarPerfilUsuarioAsync(cn, tx, esquema, menu, sistema, usuario, null, [], token);
                }

                await cn.ExecuteAsync(new CommandDefinition("""
                    DELETE FROM dbo.ALFACORE_PERFILES_WEB_CLAVES WHERE IdPerfil = @IdPerfil;
                    DELETE FROM dbo.ALFACORE_PERFILES_WEB WHERE IdPerfil = @IdPerfil;
                    """, new { IdPerfil = idPerfil }, tx, cancellationToken: token));

                await tx.CommitAsync(token);
                await appEvents.LogAuditAsync(ModuleName, "EliminarPerfil", "ALFACORE_PERFILES_WEB",
                    idPerfil.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Perfil de permisos eliminado.",
                    new { IdPerfil = idPerfil, Usuarios = usuarios.Select(x => x.Usuario).ToArray(), Editor }, token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo eliminar el perfil.", ct);

    // ---------------------------------------------------------------- Permisos: lectura y escritura

    private sealed record EsquemaPermisos(bool TareasConPerfil, bool Explicitos, bool ConvAdministradores, bool ConvAdministradoresConPerfil, bool Perfiles);

    private sealed record ClavesUsuario(HashSet<string> Propias, HashSet<string> DePerfil, bool Restringido);

    private sealed class MenuWeb
    {
        public List<MenuAutorizacionRow> Rows { get; init; } = [];
        public Dictionary<string, string> ParentByKey { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ManagedKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<EsquemaPermisos> LoadEsquemaAsync(SqlConnection cn, CancellationToken ct)
    {
        var row = await cn.QueryFirstAsync<(bool TareasConPerfil, bool Explicitos, bool ConvAdm, bool ConvAdmPerfil, bool Perfiles)>(new CommandDefinition("""
            SELECT
                CAST(CASE WHEN COL_LENGTH(N'dbo.ALFACORE_TAREAS_WEB', N'IdPerfil') IS NULL THEN 0 ELSE 1 END AS bit),
                CAST(CASE WHEN OBJECT_ID(N'dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS', N'U') IS NULL THEN 0 ELSE 1 END AS bit),
                CAST(CASE WHEN OBJECT_ID(N'dbo.CONV_ADMINISTRADORES', N'U') IS NULL THEN 0 ELSE 1 END AS bit),
                CAST(CASE WHEN COL_LENGTH(N'dbo.CONV_ADMINISTRADORES', N'IdPerfil') IS NULL THEN 0 ELSE 1 END AS bit),
                CAST(CASE WHEN OBJECT_ID(N'dbo.ALFACORE_PERFILES_WEB', N'U') IS NULL
                           OR OBJECT_ID(N'dbo.ALFACORE_PERFILES_WEB_CLAVES', N'U') IS NULL
                           OR OBJECT_ID(N'dbo.ALFACORE_PERFILES_WEB_USUARIOS', N'U') IS NULL
                           OR COL_LENGTH(N'dbo.ALFACORE_TAREAS_WEB', N'IdPerfil') IS NULL
                           OR OBJECT_ID(N'dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS', N'U') IS NULL
                      THEN 0 ELSE 1 END AS bit);
            """, cancellationToken: ct));
        return new EsquemaPermisos(row.TareasConPerfil, row.Explicitos, row.ConvAdm, row.ConvAdmPerfil, row.Perfiles);
    }

    /// <summary>Claves del usuario separadas en propias y de su perfil, de los tres almacenes.</summary>
    private static async Task<ClavesUsuario> LoadClavesUsuarioAsync(SqlConnection cn, SqlTransaction? tx, EsquemaPermisos esquema, string sistema, string usuario, CancellationToken ct)
    {
        var propias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dePerfil = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var args = new { Usuario = usuario.Trim().ToUpperInvariant(), Sistema = sistema.Trim().ToUpperInvariant() };
        var restringido = false;

        var tareasSql = $"""
            SELECT UPPER(LTRIM(RTRIM(Clave))) AS Clave, {(esquema.TareasConPerfil ? "CAST(CASE WHEN IdPerfil IS NULL THEN 0 ELSE 1 END AS bit)" : "CAST(0 AS bit)")} AS DePerfil
            FROM dbo.ALFACORE_TAREAS_WEB
            WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema AND ISNULL(Clave, '') <> '';
            """;
        foreach (var r in await cn.QueryAsync<(string Clave, bool DePerfil)>(new CommandDefinition(tareasSql, args, tx, cancellationToken: ct)))
        {
            restringido = true;
            (r.DePerfil ? dePerfil : propias).Add(r.Clave);
        }

        if (esquema.Explicitos)
        {
            const string sql = """
                SELECT UPPER(LTRIM(RTRIM(Clave))) AS Clave, CAST(CASE WHEN IdPerfil IS NULL THEN 0 ELSE 1 END AS bit) AS DePerfil
                FROM dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS
                WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema;
                """;
            foreach (var r in await cn.QueryAsync<(string Clave, bool DePerfil)>(new CommandDefinition(sql, args, tx, cancellationToken: ct)))
                (r.DePerfil ? dePerfil : propias).Add(r.Clave);
        }

        if (esquema.ConvAdministradores)
        {
            var sql = $"""
                SELECT TOP (1) {(esquema.ConvAdministradoresConPerfil ? "CAST(CASE WHEN IdPerfil IS NULL THEN 0 ELSE 1 END AS bit)" : "CAST(0 AS bit)")}
                FROM dbo.CONV_ADMINISTRADORES
                WHERE UPPER(LTRIM(RTRIM(ISNULL(Usuario, N'')))) = @Usuario AND UPPER(LTRIM(RTRIM(ISNULL(Sistema, N'')))) = @Sistema;
                """;
            var fila = await cn.QueryFirstOrDefaultAsync<bool?>(new CommandDefinition(sql, args, tx, cancellationToken: ct));
            if (fila is bool esDePerfil)
                (esDePerfil ? dePerfil : propias).Add(PermisosWebCatalogo.ConversacionesVerTodos);
        }

        return new ClavesUsuario(propias, dePerfil, restringido);
    }

    /// <summary>
    /// Reemplaza los permisos PROPIOS del usuario (IdPerfil NULL) dentro de las claves que maneja esta pantalla.
    /// No toca lo que viene del perfil ni las claves de módulos no contratados.
    /// </summary>
    private async Task GuardarClavesPropiasAsync(SqlConnection cn, SqlTransaction tx, EsquemaPermisos esquema, string sistema, string usuario,
        HashSet<string> managedKeys, HashSet<string> seleccion, CancellationToken ct)
    {
        var u = usuario.Trim().ToUpperInvariant();
        var s = sistema.Trim().ToUpperInvariant();
        var soloPropias = esquema.TareasConPerfil ? " AND IdPerfil IS NULL" : string.Empty;

        var managedTareas = managedKeys.Where(k => PermisosWebCatalogo.AlmacenDe(k) == PermisoWebAlmacen.Tareas).ToArray();
        var selTareas = seleccion.Where(k => PermisosWebCatalogo.AlmacenDe(k) == PermisoWebAlmacen.Tareas).ToArray();
        if (managedTareas.Length > 0)
        {
            var delete = $"""
                DELETE FROM dbo.ALFACORE_TAREAS_WEB
                WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND UPPER(LTRIM(RTRIM(Clave))) IN @Managed {(selTareas.Length > 0 ? "AND UPPER(LTRIM(RTRIM(Clave))) NOT IN @Selected" : string.Empty)}{soloPropias};
                """;
            await cn.ExecuteAsync(new CommandDefinition(delete, new { Usuario = u, Sistema = s, Managed = managedTareas, Selected = selTareas }, tx, cancellationToken: ct));
        }

        // Lo tildado a mano que hoy figura "del perfil" pasa a ser propio (si no, al sincronizar el perfil se borraría).
        if (esquema.TareasConPerfil && selTareas.Length > 0)
        {
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.ALFACORE_TAREAS_WEB SET IdPerfil = NULL
                WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND UPPER(LTRIM(RTRIM(Clave))) IN @Selected AND IdPerfil IS NOT NULL;
                """, new { Usuario = u, Sistema = s, Selected = selTareas }, tx, cancellationToken: ct));
        }

        foreach (var clave in selTareas.OrderBy(x => x))
        {
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
                SELECT @UsuarioRaw, @SistemaRaw, @Clave, GETDATE(), @Editor
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB
                    WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                      AND UPPER(LTRIM(RTRIM(Clave))) = @Clave);
                """, new { Usuario = u, Sistema = s, UsuarioRaw = usuario.Trim(), SistemaRaw = sistema.Trim(), Clave = clave, Editor }, tx, cancellationToken: ct));
        }

        if (esquema.Explicitos)
        {
            var managedExp = managedKeys.Where(k => PermisosWebCatalogo.AlmacenDe(k) == PermisoWebAlmacen.Explicitos).ToArray();
            var selExp = seleccion.Where(k => PermisosWebCatalogo.AlmacenDe(k) == PermisoWebAlmacen.Explicitos).ToArray();
            if (managedExp.Length > 0)
            {
                var delete = $"""
                    DELETE FROM dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS
                    WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema AND IdPerfil IS NULL
                      AND UPPER(LTRIM(RTRIM(Clave))) IN @Managed {(selExp.Length > 0 ? "AND UPPER(LTRIM(RTRIM(Clave))) NOT IN @Selected" : string.Empty)};
                    """;
                await cn.ExecuteAsync(new CommandDefinition(delete, new { Usuario = u, Sistema = s, Managed = managedExp, Selected = selExp }, tx, cancellationToken: ct));
            }

            if (selExp.Length > 0)
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS SET IdPerfil = NULL
                    WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                      AND UPPER(LTRIM(RTRIM(Clave))) IN @Selected AND IdPerfil IS NOT NULL;
                    """, new { Usuario = u, Sistema = s, Selected = selExp }, tx, cancellationToken: ct));
            }

            foreach (var clave in selExp)
                await InsertExplicitoAsync(cn, tx, usuario, sistema, clave, null, ct);
        }

        if (esquema.ConvAdministradores && managedKeys.Contains(PermisosWebCatalogo.ConversacionesVerTodos))
        {
            if (seleccion.Contains(PermisosWebCatalogo.ConversacionesVerTodos))
            {
                if (esquema.ConvAdministradoresConPerfil)
                {
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.CONV_ADMINISTRADORES SET IdPerfil = NULL
                        WHERE UPPER(LTRIM(RTRIM(ISNULL(Usuario, N'')))) = @Usuario AND UPPER(LTRIM(RTRIM(ISNULL(Sistema, N'')))) = @Sistema AND IdPerfil IS NOT NULL;
                        """, new { Usuario = u, Sistema = s }, tx, cancellationToken: ct));
                }
                await InsertConvAdministradorAsync(cn, tx, esquema, usuario, sistema, null, ct);
            }
            else
            {
                var delete = $"""
                    DELETE FROM dbo.CONV_ADMINISTRADORES
                    WHERE UPPER(LTRIM(RTRIM(ISNULL(Usuario, N'')))) = @Usuario AND UPPER(LTRIM(RTRIM(ISNULL(Sistema, N'')))) = @Sistema
                    {(esquema.ConvAdministradoresConPerfil ? "AND IdPerfil IS NULL" : string.Empty)};
                    """;
                await cn.ExecuteAsync(new CommandDefinition(delete, new { Usuario = u, Sistema = s }, tx, cancellationToken: ct));
            }
        }
    }

    /// <summary>
    /// Deja los permisos que vienen del perfil iguales al perfil: borra los anteriores (IdPerfil con valor) y
    /// copia los actuales marcados con el IdPerfil. Si el usuario ya tenía la clave como propia, queda la propia.
    /// </summary>
    private async Task SincronizarPerfilUsuarioAsync(SqlConnection cn, SqlTransaction tx, EsquemaPermisos esquema, MenuWeb menu,
        string sistema, string usuario, int? idPerfil, IReadOnlyCollection<string> clavesPerfil, CancellationToken ct)
    {
        var args = new { Usuario = usuario.Trim().ToUpperInvariant(), Sistema = sistema.Trim().ToUpperInvariant() };
        await cn.ExecuteAsync(new CommandDefinition($"""
            DELETE FROM dbo.ALFACORE_TAREAS_WEB
            WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema AND IdPerfil IS NOT NULL;
            DELETE FROM dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS
            WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema AND IdPerfil IS NOT NULL;
            {(esquema.ConvAdministradoresConPerfil
                ? "DELETE FROM dbo.CONV_ADMINISTRADORES WHERE UPPER(LTRIM(RTRIM(ISNULL(Usuario, N'')))) = @Usuario AND UPPER(LTRIM(RTRIM(ISNULL(Sistema, N'')))) = @Sistema AND IdPerfil IS NOT NULL;"
                : string.Empty)}
            """, args, tx, cancellationToken: ct));

        if (idPerfil is not int id || clavesPerfil.Count == 0)
            return;

        var claves = ExpandSelectionWithAncestors(
            clavesPerfil.Where(menu.ManagedKeys.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase),
            menu.ParentByKey,
            menu.ManagedKeys);

        foreach (var clave in claves.OrderBy(x => x))
        {
            switch (PermisosWebCatalogo.AlmacenDe(clave))
            {
                case PermisoWebAlmacen.Explicitos:
                    await InsertExplicitoAsync(cn, tx, usuario, sistema, clave, id, ct);
                    break;
                case PermisoWebAlmacen.ConversacionesVerTodos:
                    if (esquema.ConvAdministradores)
                        await InsertConvAdministradorAsync(cn, tx, esquema, usuario, sistema, id, ct);
                    break;
                default:
                    await cn.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion, IdPerfil)
                        SELECT @UsuarioRaw, @SistemaRaw, @Clave, GETDATE(), @Editor, @IdPerfil
                        WHERE NOT EXISTS (
                            SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB
                            WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                              AND UPPER(LTRIM(RTRIM(Clave))) = @Clave);
                        """, new
                    {
                        args.Usuario,
                        args.Sistema,
                        UsuarioRaw = usuario.Trim(),
                        SistemaRaw = sistema.Trim(),
                        Clave = clave,
                        Editor,
                        IdPerfil = id
                    }, tx, cancellationToken: ct));
                    break;
            }
        }
    }

    private async Task InsertExplicitoAsync(SqlConnection cn, SqlTransaction tx, string usuario, string sistema, string clave, int? idPerfil, CancellationToken ct)
        => await cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS (Usuario, Sistema, Clave, IdPerfil, FechaHoraGrabacion, UsuarioGrabacion)
            SELECT @UsuarioRaw, @SistemaRaw, @Clave, @IdPerfil, GETDATE(), @Editor
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.ALFACORE_PERMISOS_WEB_EXPLICITOS
                WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                  AND UPPER(LTRIM(RTRIM(Clave))) = @Clave);
            """, new
        {
            Usuario = usuario.Trim().ToUpperInvariant(),
            Sistema = sistema.Trim().ToUpperInvariant(),
            UsuarioRaw = usuario.Trim(),
            SistemaRaw = sistema.Trim(),
            Clave = clave.Trim().ToUpperInvariant(),
            IdPerfil = idPerfil,
            Editor
        }, tx, cancellationToken: ct));

    private static async Task InsertConvAdministradorAsync(SqlConnection cn, SqlTransaction tx, EsquemaPermisos esquema, string usuario, string sistema, int? idPerfil, CancellationToken ct)
    {
        var sql = esquema.ConvAdministradoresConPerfil
            ? """
              INSERT INTO dbo.CONV_ADMINISTRADORES (Usuario, Sistema, IdPerfil)
              SELECT @UsuarioRaw, @SistemaRaw, @IdPerfil
              WHERE NOT EXISTS (SELECT 1 FROM dbo.CONV_ADMINISTRADORES
                  WHERE UPPER(LTRIM(RTRIM(ISNULL(Usuario, N'')))) = @Usuario AND UPPER(LTRIM(RTRIM(ISNULL(Sistema, N'')))) = @Sistema);
              """
            : """
              INSERT INTO dbo.CONV_ADMINISTRADORES (Usuario, Sistema)
              SELECT @UsuarioRaw, @SistemaRaw
              WHERE NOT EXISTS (SELECT 1 FROM dbo.CONV_ADMINISTRADORES
                  WHERE UPPER(LTRIM(RTRIM(ISNULL(Usuario, N'')))) = @Usuario AND UPPER(LTRIM(RTRIM(ISNULL(Sistema, N'')))) = @Sistema);
              """;
        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Usuario = usuario.Trim().ToUpperInvariant(),
            Sistema = sistema.Trim().ToUpperInvariant(),
            UsuarioRaw = usuario.Trim(),
            SistemaRaw = sistema.Trim(),
            IdPerfil = idPerfil
        }, tx, cancellationToken: ct));
    }

    private async Task AsignarPerfilAsync(SqlConnection cn, SqlTransaction tx, string sistema, string usuario, int? idPerfil, CancellationToken ct)
        => await cn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM dbo.ALFACORE_PERFILES_WEB_USUARIOS
            WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema;
            IF @IdPerfil IS NOT NULL
                INSERT INTO dbo.ALFACORE_PERFILES_WEB_USUARIOS (Usuario, Sistema, IdPerfil, UsuarioGrabacion)
                VALUES (@UsuarioRaw, @SistemaRaw, @IdPerfil, @Editor);
            """, new
        {
            Usuario = usuario.Trim().ToUpperInvariant(),
            Sistema = sistema.Trim().ToUpperInvariant(),
            UsuarioRaw = usuario.Trim(),
            SistemaRaw = sistema.Trim(),
            IdPerfil = idPerfil,
            Editor
        }, tx, cancellationToken: ct));

    private static async Task<int?> LoadPerfilUsuarioAsync(SqlConnection cn, SqlTransaction? tx, string sistema, string usuario, CancellationToken ct)
        => await cn.QueryFirstOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT TOP (1) IdPerfil FROM dbo.ALFACORE_PERFILES_WEB_USUARIOS
            WHERE UPPER(LTRIM(RTRIM(Usuario))) = @Usuario AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema;
            """, new { Usuario = usuario.Trim().ToUpperInvariant(), Sistema = sistema.Trim().ToUpperInvariant() }, tx, cancellationToken: ct));

    private static async Task<HashSet<string>> LoadClavesPerfilAsync(SqlConnection cn, SqlTransaction? tx, int idPerfil, CancellationToken ct)
        => (await cn.QueryAsync<string>(new CommandDefinition(
                "SELECT UPPER(LTRIM(RTRIM(Clave))) FROM dbo.ALFACORE_PERFILES_WEB_CLAVES WHERE IdPerfil = @IdPerfil;",
                new { IdPerfil = idPerfil }, tx, cancellationToken: ct)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static async Task<bool> PerfilExisteAsync(SqlConnection cn, SqlTransaction? tx, int idPerfil, string sistema, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.ALFACORE_PERFILES_WEB WHERE IdPerfil = @IdPerfil AND UPPER(LTRIM(RTRIM(Sistema))) = @Sistema;",
            new { IdPerfil = idPerfil, Sistema = sistema.Trim().ToUpperInvariant() }, tx, cancellationToken: ct)) > 0;

    private static async Task<List<(string Usuario, string Sistema)>> LoadUsuariosPerfilAsync(SqlConnection cn, SqlTransaction? tx, int idPerfil, CancellationToken ct)
        => (await cn.QueryAsync<(string Usuario, string Sistema)>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(Usuario)), LTRIM(RTRIM(Sistema)) FROM dbo.ALFACORE_PERFILES_WEB_USUARIOS WHERE IdPerfil = @IdPerfil;",
                new { IdPerfil = idPerfil }, tx, cancellationToken: ct)))
            .ToList();

    private static async Task<IReadOnlyList<PerfilWebResumenDto>> LoadPerfilesAsync(SqlConnection cn, string sistema, CancellationToken ct)
    {
        var args = new { Sistema = sistema.Trim().ToUpperInvariant() };
        var perfiles = (await cn.QueryAsync<(int IdPerfil, string Nombre, string Descripcion)>(new CommandDefinition("""
            SELECT IdPerfil, LTRIM(RTRIM(Nombre)), ISNULL(Descripcion, '')
            FROM dbo.ALFACORE_PERFILES_WEB
            WHERE UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
            ORDER BY Nombre;
            """, args, cancellationToken: ct))).ToList();
        if (perfiles.Count == 0)
            return [];

        var claves = (await cn.QueryAsync<(int IdPerfil, string Clave)>(new CommandDefinition("""
            SELECT c.IdPerfil, UPPER(LTRIM(RTRIM(c.Clave)))
            FROM dbo.ALFACORE_PERFILES_WEB_CLAVES c
            JOIN dbo.ALFACORE_PERFILES_WEB p ON p.IdPerfil = c.IdPerfil
            WHERE UPPER(LTRIM(RTRIM(p.Sistema))) = @Sistema;
            """, args, cancellationToken: ct))).ToLookup(x => x.IdPerfil, x => x.Clave);
        var usuarios = (await cn.QueryAsync<(int IdPerfil, string Usuario)>(new CommandDefinition("""
            SELECT u.IdPerfil, LTRIM(RTRIM(u.Usuario))
            FROM dbo.ALFACORE_PERFILES_WEB_USUARIOS u
            JOIN dbo.ALFACORE_PERFILES_WEB p ON p.IdPerfil = u.IdPerfil
            WHERE UPPER(LTRIM(RTRIM(p.Sistema))) = @Sistema
            ORDER BY u.Usuario;
            """, args, cancellationToken: ct))).ToLookup(x => x.IdPerfil, x => x.Usuario);

        return perfiles.Select(p => new PerfilWebResumenDto
        {
            IdPerfil = p.IdPerfil,
            Nombre = p.Nombre,
            Descripcion = p.Descripcion.Trim(),
            Claves = claves[p.IdPerfil].ToArray(),
            Usuarios = usuarios[p.IdPerfil].ToArray()
        }).ToArray();
    }

    // ---------------------------------------------------------------- Menú y árbol

    /// <summary>
    /// Opciones del menú web que esta pantalla maneja. Si el cliente no contrató un módulo, sus opciones ni se
    /// muestran ni se tocan al guardar (así un permiso previo no se borra solo por guardar otra sección).
    /// </summary>
    private async Task<MenuWeb> LoadMenuAsync(SqlConnection cn, string menuSistema, CancellationToken ct)
    {
        const string sql = """
            SELECT
                ISNULL(w.Menu, '') AS Menu,
                UPPER(LTRIM(RTRIM(ISNULL(w.PadreClave, '')))) AS Titulo,
                UPPER(LTRIM(RTRIM(ISNULL(w.Clave, '')))) AS Clave,
                ISNULL(NULLIF(w.NombreWeb, ''), w.Clave) AS Nombre,
                ISNULL(w.DescripcionWeb, ISNULL(w.Observacion, '')) AS Descripcion,
                CAST('' AS nvarchar(150)) AS Proceso,
                ISNULL(w.HabilitadoWeb, 1) AS Habilitado,
                ISNULL(w.OrdenWeb, 0) AS OrdenMenu,
                ISNULL(w.RutaWeb, '') AS RutaWeb,
                ISNULL(w.HabilitadoWeb, 1) AS HabilitadoWeb
            FROM dbo.ALFACORE_MENU_WEB w
            WHERE UPPER(LTRIM(RTRIM(w.Menu))) = @MenuSistema
              AND ISNULL(w.HabilitadoWeb, 1) = 1
              AND ISNULL(w.Clave, '') <> ''
            ORDER BY ISNULL(w.OrdenWeb, 0), w.Clave, ISNULL(NULLIF(w.NombreWeb, ''), w.Clave);
            """;

        var rows = (await cn.QueryAsync<MenuAutorizacionRow>(new CommandDefinition(sql, new
        {
            MenuSistema = menuSistema.Trim().ToUpperInvariant()
        }, cancellationToken: ct))).ToList();

        // Las autorizaciones de comprobantes tienen un bloque propio al comienzo del
        // árbol. Se reemplazan las filas visuales anteriores para no depender de que
        // una base vieja las tenga bajo D6054 o con otro orden. Las claves guardadas
        // siguen siendo las mismas de ALFACORE_TAREAS_WEB.
        rows.RemoveAll(x =>
            string.Equals(x.Clave?.Trim(), PermisosWebCatalogo.PuntoVentaComprobantes, StringComparison.OrdinalIgnoreCase)
            || x.Clave?.StartsWith("D6054-COMPROBANTE-", StringComparison.OrdinalIgnoreCase) == true);

        var clavesExistentes = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Clave))
            .Select(x => x.Clave!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var padreComprobantes = PermisosWebCatalogo.PuntoVentaComprobantes;
        if (!clavesExistentes.Contains(padreComprobantes))
        {
            rows.Add(new MenuAutorizacionRow
            {
                Menu = menuSistema.Trim().ToUpperInvariant(),
                Titulo = string.Empty,
                Clave = padreComprobantes,
                Nombre = "Comprobantes habilitados",
                Descripcion = "Comprobantes que el usuario puede utilizar en el Punto de Venta.",
                Proceso = string.Empty,
                Habilitado = true,
                OrdenMenu = "00001",
                RutaWeb = string.Empty,
                HabilitadoWeb = true
            });
            clavesExistentes.Add(padreComprobantes);
        }
        var ordenComprobante = 2;
        foreach (var accion in PermisosWebCatalogo.Acciones.Where(x =>
            x.Almacen == PermisoWebAlmacen.Tareas
            && x.Clave.StartsWith("D6054-COMPROBANTE-", StringComparison.OrdinalIgnoreCase)))
        {
            if (clavesExistentes.Contains(accion.Clave))
                continue;

            rows.Add(new MenuAutorizacionRow
            {
                Menu = menuSistema.Trim().ToUpperInvariant(),
                Titulo = accion.PadreClave,
                Clave = accion.Clave,
                Nombre = accion.Nombre,
                Descripcion = accion.Descripcion,
                Proceso = string.Empty,
                Habilitado = true,
                OrdenMenu = $"{ordenComprobante++:00000}",
                RutaWeb = string.Empty,
                HabilitadoWeb = true
            });
            clavesExistentes.Add(accion.Clave);
        }

        var parentByKey = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Clave))
            .GroupBy(x => x.Clave!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Titulo?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        // Solo lo contratado (a diferencia del menú, acá no hay excepción para "Administrar": esta pantalla
        // ya está restringida a superadmin en el menú).
        var moduloFiltro = await centralAdminService.GetModuloMenuFiltroParaClienteActualAsync(ct).ConfigureAwait(false);
        if (moduloFiltro is not null)
        {
            var includeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var key = row.Clave!.Trim();
                var esAutorizacionComprobante = key.Equals(padreComprobantes, StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("D6054-COMPROBANTE-", StringComparison.OrdinalIgnoreCase);
                if (!esAutorizacionComprobante && !moduloFiltro.PermiteClave(key, parentByKey))
                    continue;

                includeKeys.Add(key);
                var current = key;
                while (parentByKey.TryGetValue(current, out var parent) && !string.IsNullOrWhiteSpace(parent))
                {
                    includeKeys.Add(parent);
                    current = parent;
                }
            }

            rows = rows.Where(x => includeKeys.Contains(x.Clave!.Trim())).ToList();
        }

        return new MenuWeb
        {
            Rows = rows,
            ParentByKey = parentByKey,
            ManagedKeys = rows.Select(x => x.Clave!.Trim().ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static List<MenuAutorizacionItemDto> BuildTree(MenuWeb menu, Func<string, bool> autorizado, Func<string, bool> porPerfil)
    {
        var rows = menu.Rows;
        var nodeByKey = rows
            .GroupBy(x => x.Clave!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g =>
            {
                var accion = PermisosWebCatalogo.Buscar(g.Key);
                return new MenuAutorizacionItemDto
                {
                    Menu = g.First().Menu?.Trim() ?? string.Empty,
                    Clave = g.Key,
                    Titulo = g.First().Titulo?.Trim() ?? string.Empty,
                    Nombre = g.First().Nombre?.Trim() ?? g.Key,
                    Descripcion = g.First().Descripcion?.Trim() ?? accion?.Descripcion ?? string.Empty,
                    Proceso = null,
                    Habilitado = g.First().Habilitado,
                    RutaWeb = string.IsNullOrWhiteSpace(g.First().RutaWeb) ? null : g.First().RutaWeb?.Trim(),
                    HabilitadoWeb = g.First().HabilitadoWeb,
                    Autorizado = autorizado(g.Key),
                    PorPerfil = porPerfil(g.Key),
                    EsAccion = accion is not null,
                    Explicito = accion?.Explicito == true
                };
            }, StringComparer.OrdinalIgnoreCase);

        var roots = new List<MenuAutorizacionItemDto>();
        foreach (var node in nodeByKey.Values.OrderBy(x => ParseOrder(rows, x.Clave)).ThenBy(x => x.Nombre))
        {
            if (string.Equals(node.Clave, "D", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(node.Titulo) && nodeByKey.TryGetValue(node.Titulo, out var parent))
            {
                if (string.Equals(parent.Clave, "D", StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(node);
                    continue;
                }

                parent.Hijos.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        SortTree(roots, rows);
        return roots;
    }

    // ---------------------------------------------------------------- Usuario (TA_USUARIOS) y catálogos

    private static string ResolveUnidadNegocio(string? currentValue, IReadOnlyList<AutorizacionCatalogoItemDto> unidadesNegocio)
    {
        var resolved = ResolveOptionalCatalogValue(currentValue, unidadesNegocio);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        var preferred = unidadesNegocio.FirstOrDefault(x =>
            string.Equals(x.CodigoVisual, DefaultUnidadNegocioVisual, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
            return preferred.Codigo;

        return unidadesNegocio.FirstOrDefault()?.Codigo ?? string.Empty;
    }

    private static string ResolveOptionalCatalogValue(string? currentValue, IReadOnlyList<AutorizacionCatalogoItemDto> options)
    {
        var normalized = currentValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var match = options.FirstOrDefault(x =>
            string.Equals(x.Codigo.Trim(), normalized, StringComparison.OrdinalIgnoreCase));

        return match?.Codigo ?? string.Empty;
    }

    private static async Task<UsuarioAutorizacionRow> LoadUserDataAsync(
        SqlConnection cn,
        string sistemaPermisos,
        string usuario,
        CancellationToken ct)
    {
        var hasAdministrador = await ColumnExistsAsync(cn, "TA_USUARIOS", "Administrador", ct);
        var hasIdCaja = await ColumnExistsAsync(cn, "TA_USUARIOS", "IDCAJA", ct);
        var hasUNegocio = await ColumnExistsAsync(cn, "TA_USUARIOS", "UNegocio", ct);
        var hasIdDeposito = await ColumnExistsAsync(cn, "TA_USUARIOS", "IDDEPOSITO", ct);
        var hasVerProforma = await ColumnExistsAsync(cn, "TA_USUARIOS", "VerProforma", ct);

        var sql = $"""
            SELECT TOP (1)
                ISNULL(NOMBRE, '') AS Nombre,
                {(hasAdministrador ? "CASE WHEN ISNULL(Administrador, 0) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END" : "CAST(0 AS bit)")} AS Administrador,
                {(hasIdCaja ? "ISNULL(IDCAJA, '')" : "''")} AS IdCaja,
                {(hasUNegocio ? "ISNULL(UNegocio, '')" : "''")} AS UNegocio,
                {(hasIdDeposito ? "ISNULL(IDDEPOSITO, '')" : "''")} AS IdDeposito
                ,{(hasVerProforma ? "CASE WHEN ISNULL(VerProforma, 1) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END" : "CAST(1 AS bit)")} AS VerProforma
            FROM dbo.TA_USUARIOS
            WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
              AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Usuario;
            """;

        var user = await cn.QueryFirstOrDefaultAsync<UsuarioAutorizacionRow>(new CommandDefinition(sql, new
        {
            Sistema = sistemaPermisos.Trim().ToUpperInvariant(),
            Usuario = usuario.Trim().ToUpperInvariant()
        }, cancellationToken: ct));

        if (user is null)
            throw new InvalidOperationException("El usuario seleccionado ya no existe en TA_USUARIOS.");

        return user;
    }

    private static async Task<IReadOnlyList<AutorizacionCatalogoItemDto>> LoadUnidadesNegocioAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await ObjectExistsAsync(cn, "V_TA_UnidadNegocio", ct))
            return [];

        var rows = await cn.QueryAsync<AutorizacionCatalogoItemDto>(new CommandDefinition("""
            SELECT
                ISNULL(Codigo, '') AS Codigo,
                ISNULL(Descripcion, '') AS Descripcion,
                '' AS UnidadNegocio,
                CAST(0 AS bit) AS Compartido
            FROM dbo.V_TA_UnidadNegocio
            ORDER BY Codigo;
            """, cancellationToken: ct));

        return rows.ToArray();
    }

    private static async Task<IReadOnlyList<AutorizacionCatalogoItemDto>> LoadCajasAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await ObjectExistsAsync(cn, "V_TA_CAJAS", ct))
            return [];

        var rows = await cn.QueryAsync<AutorizacionCatalogoItemDto>(new CommandDefinition("""
            SELECT
                ISNULL(IDCAJAS, '') AS Codigo,
                ISNULL(Descripcion, '') AS Descripcion,
                ISNULL(UnidadNegocio, '') AS UnidadNegocio,
                CAST(0 AS bit) AS Compartido
            FROM dbo.V_TA_CAJAS
            ORDER BY IDCAJAS;
            """, cancellationToken: ct));

        return rows.ToArray();
    }

    private static async Task<IReadOnlyList<AutorizacionCatalogoItemDto>> LoadDepositosAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await ObjectExistsAsync(cn, "V_TA_DEPOSITO", ct))
            return [];

        var rows = await cn.QueryAsync<AutorizacionCatalogoItemDto>(new CommandDefinition("""
            SELECT
                ISNULL(IDDEPOSITO, '') AS Codigo,
                ISNULL(Descripcion, '') AS Descripcion,
                ISNULL(UnidadNegocio, '') AS UnidadNegocio,
                ISNULL(Compartido, 0) AS Compartido
            FROM dbo.V_TA_DEPOSITO
            ORDER BY IDDEPOSITO;
            """, cancellationToken: ct));

        return rows.ToArray();
    }

    private static async Task UpdateUserAssignmentsAsync(
        SqlConnection cn,
        SqlTransaction tx,
        string sistemaPermisos,
        string usuario,
        bool administrador,
        bool verProforma,
        string unidadNegocio,
        string idCaja,
        string idDeposito,
        bool hasAdministrador,
        bool hasIdCaja,
        bool hasUNegocio,
        bool hasIdDeposito,
        bool hasVerProforma,
        CancellationToken ct)
    {
        var assignments = new List<string>();
        if (hasAdministrador)
            assignments.Add("Administrador = @Administrador");
        if (hasIdCaja)
            assignments.Add("IDCAJA = @IdCaja");
        if (hasUNegocio)
            assignments.Add("UNegocio = @UNegocio");
        if (hasIdDeposito)
            assignments.Add("IDDEPOSITO = @IdDeposito");
        if (hasVerProforma)
            assignments.Add("VerProforma = @VerProforma");

        if (assignments.Count == 0)
            return;

        var sql = $"""
            UPDATE dbo.TA_USUARIOS
            SET {string.Join("," + Environment.NewLine + "    ", assignments)}
            WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
              AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Usuario;
            """;

        var affected = await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Administrador = administrador,
            VerProforma = verProforma,
            IdCaja = DbNullable(idCaja),
            UNegocio = unidadNegocio,
            IdDeposito = DbNullable(idDeposito),
            Sistema = sistemaPermisos.Trim().ToUpperInvariant(),
            Usuario = usuario.Trim().ToUpperInvariant()
        }, tx, cancellationToken: ct));

        if (affected == 0)
            throw new InvalidOperationException("No se encontró el usuario a actualizar en TA_USUARIOS.");
    }

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

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
        catch (AutorizacionReglaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new InvalidOperationException($"{userMessage} Código: {incidentId}", ex);
        }
    }

    private async Task ExecuteLoggedAsync(
        string module,
        string action,
        Func<CancellationToken, Task> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            await operation(ct);
        }
        catch (AutorizacionReglaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new InvalidOperationException($"{userMessage} Código: {incidentId}", ex);
        }
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.tables
            WHERE object_id = OBJECT_ID(@FullName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { FullName = $"dbo.{tableName}" }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection cn, string objectName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.objects
            WHERE object_id = OBJECT_ID(@FullName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { FullName = $"dbo.{objectName}" }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@FullName)
              AND LOWER(name) = LOWER(@ColumnName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            FullName = $"dbo.{tableName}",
            ColumnName = columnName
        }, cancellationToken: ct));
        return count > 0;
    }

    private static int ParseOrder(IEnumerable<MenuAutorizacionRow> rows, string clave)
    {
        var row = rows.FirstOrDefault(x => string.Equals(x.Clave?.Trim(), clave, StringComparison.OrdinalIgnoreCase));
        var order = row?.OrdenMenu?.Trim();

        if (!string.IsNullOrWhiteSpace(order))
        {
            var digits = new string(order.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed))
                return parsed;
        }

        var fallbackDigits = new string(clave.Where(char.IsDigit).ToArray());
        return int.TryParse(fallbackDigits, out var fallback) ? fallback : int.MaxValue;
    }

    private static void SortTree(List<MenuAutorizacionItemDto> nodes, IReadOnlyList<MenuAutorizacionRow> rows)
    {
        nodes.Sort((a, b) =>
        {
            var left = ParseOrder(rows, a.Clave);
            var right = ParseOrder(rows, b.Clave);
            var compare = left.CompareTo(right);
            return compare != 0 ? compare : string.Compare(a.Nombre, b.Nombre, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var node in nodes)
            SortTree(node.Hijos, rows);
    }

    /// <summary>
    /// Suma los padres de cada clave elegida, salvo los de los permisos explícitos: tildar solo "Configurar
    /// Conversaciones" a un usuario sin restricciones no tiene que darle "Conversaciones" como opción del menú
    /// (eso lo convertiría en restringido y le escondería el resto).
    /// </summary>
    internal static HashSet<string> ExpandSelectionWithAncestors(
        HashSet<string> selected,
        IReadOnlyDictionary<string, string> parentByKey,
        IReadOnlyCollection<string> managedKeys)
    {
        var expanded = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        var managed = new HashSet<string>(managedKeys, StringComparer.OrdinalIgnoreCase);

        foreach (var key in selected.Where(k => !PermisosWebCatalogo.EsExplicito(k)).ToArray())
        {
            var current = key;
            while (parentByKey.TryGetValue(current, out var parent) && !string.IsNullOrWhiteSpace(parent))
            {
                if (!managed.Contains(parent))
                    break;

                expanded.Add(parent);
                current = parent;
            }
        }

        return expanded;
    }

    private sealed class MenuAutorizacionRow
    {
        public string? Menu { get; init; }
        public string? Titulo { get; init; }
        public string? Clave { get; init; }
        public string? Nombre { get; init; }
        public string? Descripcion { get; init; }
        public string? Proceso { get; init; }
        public bool Habilitado { get; init; }
        public string? OrdenMenu { get; init; }
        public string? RutaWeb { get; init; }
        public bool HabilitadoWeb { get; init; }
    }

    /// <summary>Validación para mostrar tal cual al usuario (no genera incidente).</summary>
    public sealed class AutorizacionReglaException(string message) : InvalidOperationException(message);

    private sealed class UsuarioAutorizacionRow
    {
        public string Nombre { get; init; } = string.Empty;
        public bool Administrador { get; init; }
        public bool VerProforma { get; init; } = true;
        public string IdCaja { get; init; } = string.Empty;
        public string UNegocio { get; init; } = string.Empty;
        public string IdDeposito { get; init; } = string.Empty;
    }
}

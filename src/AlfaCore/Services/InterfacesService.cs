using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlfaCore.Services;

public sealed class InterfacesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IInterfacesConfigService interfacesConfigService) : IInterfacesService
{
    private readonly IAppEventService _appEvents = appEvents;
    private const string ModuleName = "Interfaces";
    private const string ConfigGroup = "INTERFACES";
    private const string ViewConfigPrefix = "USUVIEW-INTERFACES-";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<IReadOnlyList<InterfacesEstadoOptionDto>> GetStatesAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "GetStates", async token =>
        {
            const string sql = """
                SELECT
                    IdEstado,
                    ISNULL(Codigo, ''),
                    ISNULL(Descripcion, ''),
                    ISNULL(Orden, 0),
                    ISNULL(Activo, 1),
                    ISNULL(PermiteEdicion, 0),
                    ISNULL(EsInicial, 0),
                    ISNULL(EsFinal, 0),
                    ISNULL(Color, '')
                FROM dbo.INT_ESTADO
                WHERE ISNULL(Activo, 1) = 1
                ORDER BY Orden, Descripcion
                """;

            var items = new List<InterfacesEstadoOptionDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new InterfacesEstadoOptionDto
                {
                    IdEstado = rd.GetInt32(0),
                    Codigo = GetString(rd, 1),
                    Descripcion = GetString(rd, 2),
                    Orden = GetInt(rd, 3),
                    Activo = GetBool(rd, 4),
                    PermiteEdicion = GetBool(rd, 5),
                    EsInicial = GetBool(rd, 6),
                    EsFinal = GetBool(rd, 7),
                    Color = GetString(rd, 8)
                });
            }

            return (IReadOnlyList<InterfacesEstadoOptionDto>)items;
        }, "No se pudieron cargar los estados de Interfaces.", ct);

    public Task<IReadOnlyList<InterfacesTipoDocumentoOptionDto>> GetDocumentTypesAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "GetDocumentTypes", async token =>
        {
            const string sql = """
                SELECT
                    IdTipoDocumento,
                    ISNULL(Codigo, ''),
                    ISNULL(Descripcion, ''),
                    ISNULL(Orden, 0),
                    ISNULL(Activo, 1)
                FROM dbo.INT_TIPO_DOCUMENTO
                WHERE ISNULL(Activo, 1) = 1
                ORDER BY Orden, Descripcion
                """;

            var items = new List<InterfacesTipoDocumentoOptionDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new InterfacesTipoDocumentoOptionDto
                {
                    IdTipoDocumento = rd.GetInt32(0),
                    Codigo = GetString(rd, 1),
                    Descripcion = GetString(rd, 2),
                    Orden = GetInt(rd, 3),
                    Activo = GetBool(rd, 4)
                });
            }

            return (IReadOnlyList<InterfacesTipoDocumentoOptionDto>)items;
        }, "No se pudieron cargar los tipos documentales.", ct);

    public Task<PagedResult<InterfacesInboxItemDto>> SearchAsync(InterfacesFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "Search", async token =>
        {
            filters ??= new InterfacesFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            const string sql = """
                SELECT
                    c.IdComprobanteRecibido,
                    c.FechaHora_Grabacion,
                    ISNULL(c.UsuarioAlta, ''),
                    ISNULL(c.Observacion, ''),
                    ISNULL(c.CantidadAdjuntos, 0),
                    ISNULL(c.Eliminado, 0),
                    e.IdEstado,
                    ISNULL(e.Codigo, ''),
                    ISNULL(e.Descripcion, ''),
                    ISNULL(e.PermiteEdicion, 0),
                    t.IdTipoDocumento,
                    ISNULL(t.Codigo, ''),
                    ISNULL(t.Descripcion, '')
                FROM dbo.INT_COMPROBANTE_RECIBIDO c
                INNER JOIN dbo.INT_ESTADO e
                    ON e.IdEstado = c.IdEstado
                INNER JOIN dbo.INT_TIPO_DOCUMENTO t
                    ON t.IdTipoDocumento = c.IdTipoDocumento
                WHERE (@Desde IS NULL OR c.FechaHora_Grabacion >= @Desde)
                  AND (@Hasta IS NULL OR c.FechaHora_Grabacion < DATEADD(day, 1, @Hasta))
                  AND (@IdEstado IS NULL OR c.IdEstado = @IdEstado)
                  AND (@IdTipoDocumento IS NULL OR c.IdTipoDocumento = @IdTipoDocumento)
                  AND (
                        @Texto = ''
                        OR ISNULL(c.Observacion, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.ReferenciaExterna, '') LIKE '%' + @Texto + '%'
                        OR CONVERT(nvarchar(30), c.IdComprobanteRecibido) LIKE '%' + @Texto + '%'
                        OR ISNULL(c.UsuarioAlta, '') LIKE '%' + @Texto + '%'
                      )
                ORDER BY c.FechaHora_Grabacion DESC, c.IdComprobanteRecibido DESC
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(1)
                FROM dbo.INT_COMPROBANTE_RECIBIDO c
                WHERE (@Desde IS NULL OR c.FechaHora_Grabacion >= @Desde)
                  AND (@Hasta IS NULL OR c.FechaHora_Grabacion < DATEADD(day, 1, @Hasta))
                  AND (@IdEstado IS NULL OR c.IdEstado = @IdEstado)
                  AND (@IdTipoDocumento IS NULL OR c.IdTipoDocumento = @IdTipoDocumento)
                  AND (
                        @Texto = ''
                        OR ISNULL(c.Observacion, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.ReferenciaExterna, '') LIKE '%' + @Texto + '%'
                        OR CONVERT(nvarchar(30), c.IdComprobanteRecibido) LIKE '%' + @Texto + '%'
                        OR ISNULL(c.UsuarioAlta, '') LIKE '%' + @Texto + '%'
                      );
                """;

            var items = new List<InterfacesInboxItemDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Desde", (object?)filters.Desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta", (object?)filters.Hasta ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IdEstado", (object?)filters.IdEstado ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IdTipoDocumento", (object?)filters.IdTipoDocumento ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Texto", filters.Texto?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new InterfacesInboxItemDto
                {
                    IdComprobanteRecibido = rd.GetInt64(0),
                    FechaHoraGrabacion = rd.GetDateTime(1),
                    UsuarioAlta = GetString(rd, 2),
                    Observacion = GetString(rd, 3),
                    CantidadAdjuntos = GetInt(rd, 4),
                    Eliminado = GetBool(rd, 5),
                    IdEstado = GetInt(rd, 6),
                    EstadoCodigo = GetString(rd, 7),
                    EstadoDescripcion = GetString(rd, 8),
                    PermiteEdicion = GetBool(rd, 9),
                    IdTipoDocumento = GetInt(rd, 10),
                    TipoDocumentoCodigo = GetString(rd, 11),
                    TipoDocumentoDescripcion = GetString(rd, 12)
                });
            }

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
                total = Convert.ToInt32(rd.GetValue(0));

            return new PagedResult<InterfacesInboxItemDto>
            {
                Items = items,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar los comprobantes recibidos.", ct);

    public Task<InterfacesViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return CreateDefaultViewSettings();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildViewConfigKey(userName);
            var sql = $"""
                SELECT TOP (1)
                    ISNULL(VALOR, ''),
                    ISNULL({detailColumn}, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Clave", configKey.ToUpperInvariant());
            await using var rd = await cmd.ExecuteReaderAsync(token);
            if (!await rd.ReadAsync(token))
                return CreateDefaultViewSettings();

            var raw = ResolveStoredValue(GetString(rd, 0), GetString(rd, 1));
            if (string.IsNullOrWhiteSpace(raw))
                return CreateDefaultViewSettings();

            var parsed = JsonSerializer.Deserialize<InterfacesViewSettingsDto>(raw, JsonOptions);
            return NormalizeViewSettings(parsed);
        }, "No se pudo cargar la configuración de vista.", ct);

    public Task SaveViewSettingsAsync(string userName, InterfacesViewSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la vista.");

            var normalized = NormalizeViewSettings(settings);
            var serialized = JsonSerializer.Serialize(normalized, JsonOptions);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var stored = SplitStoredValue(serialized);
            var configKey = BuildViewConfigKey(userName);
            var sql = $"""
                UPDATE dbo.TA_CONFIGURACION
                SET
                    VALOR = @Valor,
                    {detailColumn} = @ValorAux,
                    GRUPO = @Grupo,
                    FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION
                    (
                        CLAVE,
                        VALOR,
                        {detailColumn},
                        GRUPO,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion
                    )
                    VALUES
                    (
                        @Clave,
                        @Valor,
                        @ValorAux,
                        @Grupo,
                        GETDATE(),
                        GETDATE()
                    );
                END;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@ClaveNormalizada", configKey.ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Clave", configKey);
            cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value, 150));
            cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue, 4000));
            cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
            await cmd.ExecuteNonQueryAsync(token);

            await _appEvents.LogAuditAsync(
                ModuleName,
                "SaveViewSettings",
                "TA_CONFIGURACION",
                configKey,
                "Configuración de vista de interfaces actualizada.",
                new { UserName = userName.Trim(), normalized.AgruparPor, Columnas = normalized.Columnas },
                token);
        }, "No se pudo guardar la configuración de vista.", ct);

    public Task<InterfacesDetalleDto?> GetByIdAsync(long idComprobanteRecibido, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "GetById", async token =>
        {
            const string headerSql = """
                SELECT
                    c.IdComprobanteRecibido,
                    c.FechaHora_Grabacion,
                    c.FechaHora_Modificacion,
                    c.FechaHoraEstado,
                    c.FechaHoraAnulacion,
                    ISNULL(c.UsuarioAlta, ''),
                    ISNULL(c.PcAlta, ''),
                    ISNULL(c.UsuarioModificacion, ''),
                    ISNULL(c.PcModificacion, ''),
                    ISNULL(c.UsuarioAnulacion, ''),
                    ISNULL(c.PcAnulacion, ''),
                    e.IdEstado,
                    ISNULL(e.Codigo, ''),
                    ISNULL(e.Descripcion, ''),
                    ISNULL(e.PermiteEdicion, 0),
                    t.IdTipoDocumento,
                    ISNULL(t.Codigo, ''),
                    ISNULL(t.Descripcion, ''),
                    ISNULL(c.Observacion, ''),
                    ISNULL(c.MotivoAnulacion, ''),
                    ISNULL(c.CantidadAdjuntos, 0),
                    ISNULL(c.RutaBase, ''),
                    ISNULL(c.ReferenciaExterna, ''),
                    ISNULL(c.Eliminado, 0)
                FROM dbo.INT_COMPROBANTE_RECIBIDO c
                INNER JOIN dbo.INT_ESTADO e
                    ON e.IdEstado = c.IdEstado
                INNER JOIN dbo.INT_TIPO_DOCUMENTO t
                    ON t.IdTipoDocumento = c.IdTipoDocumento
                WHERE c.IdComprobanteRecibido = @IdComprobanteRecibido
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            InterfacesDetalleDto? detail = null;
            await using (var cmd = new SqlCommand(headerSql, cn))
            {
                cmd.Parameters.AddWithValue("@IdComprobanteRecibido", idComprobanteRecibido);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                if (await rd.ReadAsync(token))
                {
                    detail = new InterfacesDetalleDto
                    {
                        IdComprobanteRecibido = rd.GetInt64(0),
                        FechaHoraGrabacion = rd.GetDateTime(1),
                        FechaHoraModificacion = rd.IsDBNull(2) ? null : rd.GetDateTime(2),
                        FechaHoraEstado = rd.GetDateTime(3),
                        FechaHoraAnulacion = rd.IsDBNull(4) ? null : rd.GetDateTime(4),
                        UsuarioAlta = GetString(rd, 5),
                        PcAlta = GetString(rd, 6),
                        UsuarioModificacion = GetString(rd, 7),
                        PcModificacion = GetString(rd, 8),
                        UsuarioAnulacion = GetString(rd, 9),
                        PcAnulacion = GetString(rd, 10),
                        IdEstado = GetInt(rd, 11),
                        EstadoCodigo = GetString(rd, 12),
                        EstadoDescripcion = GetString(rd, 13),
                        PermiteEdicion = GetBool(rd, 14),
                        IdTipoDocumento = GetInt(rd, 15),
                        TipoDocumentoCodigo = GetString(rd, 16),
                        TipoDocumentoDescripcion = GetString(rd, 17),
                        Observacion = GetString(rd, 18),
                        MotivoAnulacion = GetString(rd, 19),
                        CantidadAdjuntos = GetInt(rd, 20),
                        RutaBase = GetString(rd, 21),
                        ReferenciaExterna = GetString(rd, 22),
                        Eliminado = GetBool(rd, 23)
                    };
                }
            }

            if (detail is null)
                return null;

            detail.Adjuntos = await GetAttachmentsInternalAsync(cn, idComprobanteRecibido, token);
            detail.Historial = await GetHistoryInternalAsync(cn, idComprobanteRecibido, token);
            detail.LecturaCompra = await GetCompraDetectionInternalAsync(cn, idComprobanteRecibido, token);
            return detail;
        }, "No se pudo cargar el comprobante seleccionado.", ct);

    public Task<long> CreateAsync(InterfacesCrearComprobanteRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "Create", async token =>
        {
            ValidateCreateRequest(request);
            var settings = await interfacesConfigService.GetUploadSettingsAsync(token);
            ValidateSettings(settings);

            var initialStateCode = string.IsNullOrWhiteSpace(settings.EstadoInicialCodigo) ? "A_PROCESAR" : settings.EstadoInicialCodigo.Trim();
            var now = DateTime.Now;
            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var state = await GetStateByCodeAsync(cn, initialStateCode, token)
                ?? throw new InvalidOperationException($"No existe el estado inicial configurado para Interfaces: {initialStateCode}.");
            await EnsureDocumentTypeExistsAsync(cn, request.IdTipoDocumento, token);

            await using var tx = await cn.BeginTransactionAsync(token);
            var savedFiles = new List<string>();
            try
            {
                const string insertSql = """
                    INSERT INTO dbo.INT_COMPROBANTE_RECIBIDO
                    (
                        UsuarioAlta,
                        PcAlta,
                        IdEstado,
                        IdTipoDocumento,
                        Observacion,
                        CantidadAdjuntos,
                        RutaBase,
                        FechaHoraEstado,
                        Eliminado
                    )
                    VALUES
                    (
                        @UsuarioAlta,
                        @PcAlta,
                        @IdEstado,
                        @IdTipoDocumento,
                        @Observacion,
                        0,
                        @RutaBase,
                        GETDATE(),
                        0
                    );

                    SELECT CAST(SCOPE_IDENTITY() AS bigint);
                    """;

                long idComprobante;
                await using (var cmd = new SqlCommand(insertSql, cn, (SqlTransaction)tx))
                {
                    var storedBase = ResolveStoredBase(settings);
                    cmd.Parameters.AddWithValue("@UsuarioAlta", user);
                    cmd.Parameters.AddWithValue("@PcAlta", pc);
                    cmd.Parameters.AddWithValue("@IdEstado", state.IdEstado);
                    cmd.Parameters.AddWithValue("@IdTipoDocumento", request.IdTipoDocumento);
                    cmd.Parameters.AddWithValue("@Observacion", DbNullable(request.Observacion, 1000));
                    cmd.Parameters.AddWithValue("@RutaBase", DbNullable(storedBase, 500));
                    idComprobante = Convert.ToInt64(await cmd.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
                }

                var relativeFolder = BuildComprobanteFolder(now, idComprobante);
                if (settings.UsaCarpeta)
                    Directory.CreateDirectory(Path.Combine(settings.RutaBase, relativeFolder));

                var order = 1;
                foreach (var attachment in request.Adjuntos)
                {
                    ValidateAttachment(attachment, settings);
                    var extension = NormalizeExtension(Path.GetExtension(attachment.NombreArchivo));
                    var savedName = $"{order:0000}_{Guid.NewGuid():N}{extension}";
                    var relativePath = CombineStoragePath(relativeFolder, savedName);
                    await SaveAttachmentAsync(settings, relativePath, attachment, savedFiles, token);

                    const string insertAttachmentSql = """
                        INSERT INTO dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO
                        (
                            IdComprobanteRecibido,
                            Orden,
                            NombreOriginal,
                            NombreGuardado,
                            RutaRelativa,
                            Extension,
                            MimeType,
                            TamanoBytes,
                            EsPrincipal,
                            Eliminado,
                            UsuarioAlta,
                            PcAlta
                        )
                        VALUES
                        (
                            @IdComprobanteRecibido,
                            @Orden,
                            @NombreOriginal,
                            @NombreGuardado,
                            @RutaRelativa,
                            @Extension,
                            @MimeType,
                            @TamanoBytes,
                            @EsPrincipal,
                            0,
                            @UsuarioAlta,
                            @PcAlta
                        );
                        """;

                    await using var attachmentCmd = new SqlCommand(insertAttachmentSql, cn, (SqlTransaction)tx);
                    attachmentCmd.Parameters.AddWithValue("@IdComprobanteRecibido", idComprobante);
                    attachmentCmd.Parameters.AddWithValue("@Orden", order);
                    attachmentCmd.Parameters.AddWithValue("@NombreOriginal", Truncate(attachment.NombreArchivo, 255));
                    attachmentCmd.Parameters.AddWithValue("@NombreGuardado", Truncate(savedName, 255));
                    attachmentCmd.Parameters.AddWithValue("@RutaRelativa", Truncate(relativePath, 500));
                    attachmentCmd.Parameters.AddWithValue("@Extension", DbNullable(extension, 20));
                    attachmentCmd.Parameters.AddWithValue("@MimeType", DbNullable(attachment.MimeType, 100));
                    attachmentCmd.Parameters.AddWithValue("@TamanoBytes", attachment.TamanoBytes);
                    attachmentCmd.Parameters.AddWithValue("@EsPrincipal", order == 1);
                    attachmentCmd.Parameters.AddWithValue("@UsuarioAlta", user);
                    attachmentCmd.Parameters.AddWithValue("@PcAlta", pc);
                    await attachmentCmd.ExecuteNonQueryAsync(token);
                    order++;
                }

                await UpdateAttachmentCountAsync(cn, (SqlTransaction)tx, idComprobante, request.Adjuntos.Count, token);
                await InsertHistoryAsync(cn, (SqlTransaction)tx, idComprobante, "ALTA", null, state.IdEstado, user, pc, request.Observacion,
                    new
                    {
                        request.IdTipoDocumento,
                        CantidadAdjuntos = request.Adjuntos.Count
                    }, token);

                await tx.CommitAsync(token);

                await _appEvents.LogAuditAsync(
                    "Interfaces",
                    "Create",
                    "INT_COMPROBANTE_RECIBIDO",
                    idComprobante.ToString(CultureInfo.InvariantCulture),
                    "Comprobante recibido creado.",
                    new
                    {
                        request.IdTipoDocumento,
                        EstadoInicial = state.Codigo,
                        CantidadAdjuntos = request.Adjuntos.Count
                    },
                    token);

                return idComprobante;
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(token);
                }
                catch
                {
                }

                await CleanupSavedFilesAsync(settings, savedFiles, token);

                throw;
            }
        }, "No se pudo registrar el comprobante recibido.", ct);

    public Task UpdateAsync(InterfacesActualizarComprobanteRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "Update", async token =>
        {
            if (request.IdComprobanteRecibido <= 0)
                throw new InvalidOperationException("El comprobante es obligatorio.");
            if (request.IdTipoDocumento <= 0)
                throw new InvalidOperationException("El tipo documental es obligatorio.");

            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var current = await GetByIdAsync(request.IdComprobanteRecibido, token)
                ?? throw new InvalidOperationException("El comprobante indicado no existe.");
            if (current.Eliminado)
                throw new InvalidOperationException("El comprobante seleccionado está anulado y no permite nuevas modificaciones.");

            var targetDocumentTypeId = current.PermiteEdicion
                ? request.IdTipoDocumento
                : current.IdTipoDocumento;

            await EnsureDocumentTypeExistsAsync(cn, targetDocumentTypeId, token);

            await using var tx = await cn.BeginTransactionAsync(token);
            const string sql = """
                UPDATE dbo.INT_COMPROBANTE_RECIBIDO
                SET
                    IdTipoDocumento = @IdTipoDocumento,
                    Observacion = @Observacion,
                    FechaHora_Modificacion = GETDATE(),
                    UsuarioModificacion = @UsuarioModificacion,
                    PcModificacion = @PcModificacion
                WHERE IdComprobanteRecibido = @IdComprobanteRecibido
                """;

            await using (var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@IdTipoDocumento", targetDocumentTypeId);
                cmd.Parameters.AddWithValue("@Observacion", DbNullable(request.Observacion, 1000));
                cmd.Parameters.AddWithValue("@UsuarioModificacion", DbNullable(user, 50));
                cmd.Parameters.AddWithValue("@PcModificacion", DbNullable(pc, 100));
                cmd.Parameters.AddWithValue("@IdComprobanteRecibido", request.IdComprobanteRecibido);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await InsertHistoryAsync(cn, (SqlTransaction)tx, request.IdComprobanteRecibido, "MODIFICACION", current.IdEstado, current.IdEstado, user, pc, request.Observacion,
                new
                {
                    TipoAnterior = current.IdTipoDocumento,
                    TipoNuevo = targetDocumentTypeId,
                    SoloNota = !current.PermiteEdicion
                }, token);

            await tx.CommitAsync(token);

            await _appEvents.LogAuditAsync(
                "Interfaces",
                "Update",
                "INT_COMPROBANTE_RECIBIDO",
                request.IdComprobanteRecibido.ToString(CultureInfo.InvariantCulture),
                "Comprobante recibido actualizado.",
                new
                {
                    TipoAnterior = current.IdTipoDocumento,
                    TipoNuevo = targetDocumentTypeId,
                    SoloNota = !current.PermiteEdicion
                },
                token);
        }, "No se pudo actualizar el comprobante.", ct);

    public Task AddAttachmentsAsync(InterfacesAgregarAdjuntosRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "AddAttachments", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdComprobanteRecibido <= 0)
                throw new InvalidOperationException("El comprobante es obligatorio.");
            if (request.Adjuntos is null || request.Adjuntos.Count == 0)
                throw new InvalidOperationException("Debés seleccionar al menos un archivo.");

            var settings = await interfacesConfigService.GetUploadSettingsAsync(token);
            ValidateSettings(settings);
            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);
            var current = await RequireEditableComprobanteAsync(request.IdComprobanteRecibido, token);

            var relativeFolder = BuildComprobanteFolder(current.FechaHoraGrabacion, request.IdComprobanteRecibido);
            if (settings.UsaCarpeta)
                Directory.CreateDirectory(Path.Combine(settings.RutaBase, relativeFolder));

            var nextOrder = current.Adjuntos.Count == 0 ? 1 : current.Adjuntos.Max(x => x.Orden) + 1;
            var savedFiles = new List<string>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                foreach (var attachment in request.Adjuntos)
                {
                    ValidateAttachment(attachment, settings);
                    var extension = NormalizeExtension(Path.GetExtension(attachment.NombreArchivo));
                    var savedName = $"{nextOrder:0000}_{Guid.NewGuid():N}{extension}";
                    var relativePath = CombineStoragePath(relativeFolder, savedName);
                    await SaveAttachmentAsync(settings, relativePath, attachment, savedFiles, token);

                    const string insertAttachmentSql = """
                        INSERT INTO dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO
                        (
                            IdComprobanteRecibido,
                            Orden,
                            NombreOriginal,
                            NombreGuardado,
                            RutaRelativa,
                            Extension,
                            MimeType,
                            TamanoBytes,
                            EsPrincipal,
                            Eliminado,
                            UsuarioAlta,
                            PcAlta
                        )
                        VALUES
                        (
                            @IdComprobanteRecibido,
                            @Orden,
                            @NombreOriginal,
                            @NombreGuardado,
                            @RutaRelativa,
                            @Extension,
                            @MimeType,
                            @TamanoBytes,
                            0,
                            0,
                            @UsuarioAlta,
                            @PcAlta
                        )
                        """;

                    await using var attachmentCmd = new SqlCommand(insertAttachmentSql, cn, (SqlTransaction)tx);
                    attachmentCmd.Parameters.AddWithValue("@IdComprobanteRecibido", request.IdComprobanteRecibido);
                    attachmentCmd.Parameters.AddWithValue("@Orden", nextOrder);
                    attachmentCmd.Parameters.AddWithValue("@NombreOriginal", Truncate(attachment.NombreArchivo, 255));
                    attachmentCmd.Parameters.AddWithValue("@NombreGuardado", Truncate(savedName, 255));
                    attachmentCmd.Parameters.AddWithValue("@RutaRelativa", Truncate(relativePath, 500));
                    attachmentCmd.Parameters.AddWithValue("@Extension", DbNullable(extension, 20));
                    attachmentCmd.Parameters.AddWithValue("@MimeType", DbNullable(attachment.MimeType, 100));
                    attachmentCmd.Parameters.AddWithValue("@TamanoBytes", attachment.TamanoBytes);
                    attachmentCmd.Parameters.AddWithValue("@UsuarioAlta", user);
                    attachmentCmd.Parameters.AddWithValue("@PcAlta", pc);
                    await attachmentCmd.ExecuteNonQueryAsync(token);
                    nextOrder++;
                }

                await RecalculateAttachmentCountAsync(cn, (SqlTransaction)tx, request.IdComprobanteRecibido, user, pc, token);
                await InsertHistoryAsync(cn, (SqlTransaction)tx, request.IdComprobanteRecibido, "ADJUNTO_ALTA", current.IdEstado, current.IdEstado, user, pc,
                    $"Se agregaron {request.Adjuntos.Count} adjunto(s).", new { Cantidad = request.Adjuntos.Count }, token);

                await tx.CommitAsync(token);

                await _appEvents.LogAuditAsync(
                    "Interfaces",
                    "AddAttachments",
                    "INT_COMPROBANTE_RECIBIDO",
                    request.IdComprobanteRecibido.ToString(CultureInfo.InvariantCulture),
                    "Se agregaron adjuntos al comprobante.",
                    new { Cantidad = request.Adjuntos.Count },
                    token);
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(token);
                }
                catch
                {
                }

                await CleanupSavedFilesAsync(settings, savedFiles, token);

                throw;
            }
        }, "No se pudieron agregar adjuntos al comprobante.", ct);

    public Task RemoveAttachmentAsync(InterfacesEliminarAdjuntoRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "RemoveAttachment", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdAdjunto <= 0)
                throw new InvalidOperationException("El adjunto es obligatorio.");

            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var attachment = await GetAttachmentByIdAsync(cn, request.IdAdjunto, token)
                ?? throw new InvalidOperationException("El adjunto seleccionado no existe.");
            var current = await RequireEditableComprobanteAsync(attachment.IdComprobanteRecibido, token);

            await using var tx = await cn.BeginTransactionAsync(token);
            const string sql = """
                UPDATE dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO
                SET
                    Eliminado = 1,
                    FechaHora_Modificacion = GETDATE(),
                    UsuarioModificacion = @UsuarioModificacion,
                    PcModificacion = @PcModificacion
                WHERE IdAdjunto = @IdAdjunto
                """;

            await using (var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@UsuarioModificacion", DbNullable(user, 50));
                cmd.Parameters.AddWithValue("@PcModificacion", DbNullable(pc, 100));
                cmd.Parameters.AddWithValue("@IdAdjunto", request.IdAdjunto);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await RecalculateAttachmentCountAsync(cn, (SqlTransaction)tx, attachment.IdComprobanteRecibido, user, pc, token);
            await InsertHistoryAsync(cn, (SqlTransaction)tx, attachment.IdComprobanteRecibido, "ADJUNTO_BAJA", current.IdEstado, current.IdEstado, user, pc,
                request.Observacion, new { request.IdAdjunto, attachment.NombreOriginal }, token);

            await tx.CommitAsync(token);

            await _appEvents.LogAuditAsync(
                "Interfaces",
                "RemoveAttachment",
                "INT_COMPROBANTE_RECIBIDO",
                attachment.IdComprobanteRecibido.ToString(CultureInfo.InvariantCulture),
                "Adjunto dado de baja lógicamente.",
                new { request.IdAdjunto, attachment.NombreOriginal },
                token);
        }, "No se pudo quitar el adjunto.", ct);

    public Task ChangeStatusAsync(InterfacesCambioEstadoRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "ChangeStatus", async token =>
        {
            if (request.IdComprobanteRecibido <= 0)
                throw new InvalidOperationException("El comprobante es obligatorio.");
            if (request.IdEstadoNuevo <= 0)
                throw new InvalidOperationException("El estado destino es obligatorio.");

            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detail = await GetByIdAsync(request.IdComprobanteRecibido, token)
                ?? throw new InvalidOperationException("El comprobante indicado no existe.");

            var newState = await GetStateByIdAsync(cn, request.IdEstadoNuevo, token)
                ?? throw new InvalidOperationException("El estado seleccionado no existe.");

            if (detail.IdEstado == newState.IdEstado)
                return;

            await using var tx = await cn.BeginTransactionAsync(token);
            const string updateSql = """
                UPDATE dbo.INT_COMPROBANTE_RECIBIDO
                SET
                    IdEstado = @IdEstado,
                    FechaHoraEstado = GETDATE(),
                    FechaHora_Modificacion = GETDATE(),
                    UsuarioModificacion = @UsuarioModificacion,
                    PcModificacion = @PcModificacion,
                    Eliminado = @Eliminado,
                    FechaHoraAnulacion = @FechaHoraAnulacion,
                    UsuarioAnulacion = @UsuarioAnulacion,
                    PcAnulacion = @PcAnulacion,
                    MotivoAnulacion = @MotivoAnulacion
                WHERE IdComprobanteRecibido = @IdComprobanteRecibido
                """;

            var isAnulado = string.Equals(newState.Codigo, "ANULADO", StringComparison.OrdinalIgnoreCase);

            await using (var cmd = new SqlCommand(updateSql, cn, (SqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@IdEstado", newState.IdEstado);
                cmd.Parameters.AddWithValue("@UsuarioModificacion", DbNullable(user, 50));
                cmd.Parameters.AddWithValue("@PcModificacion", DbNullable(pc, 100));
                cmd.Parameters.AddWithValue("@Eliminado", isAnulado);
                cmd.Parameters.AddWithValue("@FechaHoraAnulacion", isAnulado ? DateTime.Now : DBNull.Value);
                cmd.Parameters.AddWithValue("@UsuarioAnulacion", isAnulado ? DbNullable(user, 50) : DBNull.Value);
                cmd.Parameters.AddWithValue("@PcAnulacion", isAnulado ? DbNullable(pc, 100) : DBNull.Value);
                cmd.Parameters.AddWithValue("@MotivoAnulacion", isAnulado ? DbNullable(request.Observacion, 500) : DBNull.Value);
                cmd.Parameters.AddWithValue("@IdComprobanteRecibido", request.IdComprobanteRecibido);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await InsertHistoryAsync(
                cn,
                (SqlTransaction)tx,
                request.IdComprobanteRecibido,
                isAnulado ? "ANULACION" : "CAMBIO_ESTADO",
                detail.IdEstado,
                newState.IdEstado,
                user,
                pc,
                request.Observacion,
                new
                {
                    EstadoAnterior = detail.EstadoCodigo,
                    EstadoNuevo = newState.Codigo
                },
                token);

            await tx.CommitAsync(token);

            await _appEvents.LogAuditAsync(
                "Interfaces",
                "ChangeStatus",
                "INT_COMPROBANTE_RECIBIDO",
                request.IdComprobanteRecibido.ToString(CultureInfo.InvariantCulture),
                isAnulado ? "Comprobante anulado." : "Estado de comprobante actualizado.",
                new
                {
                    EstadoAnterior = detail.EstadoCodigo,
                    EstadoNuevo = newState.Codigo
                },
                token);
        }, "No se pudo actualizar el estado del comprobante.", ct);

    public Task<InterfacesCompraIaResultadoDto> DetectCompraAsync(InterfacesDetectarCompraRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "DetectCompra", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdComprobanteRecibido <= 0)
                throw new InvalidOperationException("El comprobante es obligatorio.");

            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);
            return await RunCompraDetectionAsync(request.IdComprobanteRecibido, user, pc, null, null, token);
        }, "No se pudo detectar la información del comprobante.", ct);

    public Task<InterfacesCompraIaResultadoDto> QueueCompraDetectionAsync(InterfacesDetectarCompraRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "QueueDetectCompra", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdComprobanteRecibido <= 0)
                throw new InvalidOperationException("El comprobante es obligatorio.");

            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);
            await EnsureCompraIaConfigurationReadyAsync(token);

            var detail = await GetByIdAsync(request.IdComprobanteRecibido, token)
                ?? throw new InvalidOperationException("El comprobante indicado no existe.");
            var eligibleAttachments = GetEligibleCompraAttachments(detail);
            if (eligibleAttachments.Count == 0)
                throw new InvalidOperationException("El documento no tiene adjuntos PDF o imagen compatibles para ejecutar la detección.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var queued = await EnqueueCompraDetectionInternalAsync(cn, detail, eligibleAttachments[0], user, pc, token);

            await _appEvents.LogAuditAsync(
                "Interfaces",
                "QueueDetectCompra",
                "IA_Compras_CAB",
                queued.Id.ToString(CultureInfo.InvariantCulture),
                "Documento encolado para detección automática de compra.",
                new
                {
                    detail.IdComprobanteRecibido,
                    queued.Estado
                },
                token);

            return queued;
        }, "No se pudo encolar la detección del comprobante.", ct);

    public Task<InterfacesCompraIaQueueSnapshotDto> GetCompraIaQueueSnapshotAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "GetCompraIaQueueSnapshot", async token =>
        {
            const string summarySql = """
                SELECT
                    SUM(CASE WHEN Estado = 'PENDIENTE_LECTURA' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN Estado = 'PROCESANDO_LECTURA' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN Estado IN ('PROCESADO', 'SIN_PROVEEDOR') THEN 1 ELSE 0 END),
                    SUM(CASE WHEN Estado = 'ERROR_LECTURA' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN Estado = 'CANCELADO' THEN 1 ELSE 0 END)
                FROM dbo.IA_Compras_CAB;
                """;

            const string itemsSql = """
                SELECT TOP (20)
                    ID,
                    IdComprobanteRecibido,
                    IdAdjuntoFuente,
                    ISNULL(Estado, ''),
                    FechaHora_Proceso,
                    FechaHora_Inicio,
                    FechaHora_Fin,
                    FechaHora_Modificacion,
                    ISNULL(Usuario_Proceso, ''),
                    ISNULL(Observaciones_Rev, ''),
                    ISNULL(SolicitarCancelacion, 0),
                    ISNULL(Intentos, 0),
                    ISNULL(Archivo_RutaOriginal, ''),
                    ISNULL(Archivo_NombreOriginal, ''),
                    ISNULL(Archivo_NombreRenombrado, ''),
                    ISNULL(Proveedor_Nombre, ''),
                    ISNULL(Proveedor_CUIT, ''),
                    ISNULL(Proveedor_Domicilio, ''),
                    ISNULL(Proveedor_CondIVA, ''),
                    ISNULL(Cuenta_Contable, ''),
                    ISNULL(Match_Metodo, ''),
                    ISNULL(TipoComprobante, ''),
                    ISNULL(Letra, ''),
                    ISNULL(PuntoVenta, ''),
                    ISNULL(Numero, ''),
                    Fecha,
                    Vencimiento,
                    ISNULL(CAE, ''),
                    VtoCAE,
                    ISNULL(Moneda, ''),
                    NetoGravado,
                    NetoNoGravado,
                    Exento,
                    IVA_21,
                    IVA_105,
                    IVA_27,
                    Percepcion_IVA,
                    Percepcion_IIBB,
                    Percepcion_Ganancias,
                    ImpuestosInternos,
                    OtrosImpuestos,
                    Total,
                    ISNULL(Lector_Observaciones, ''),
                    ISNULL(Lector_Error, ''),
                    ISNULL(JsonResultado, '')
                FROM dbo.IA_Compras_CAB
                ORDER BY
                    CASE Estado
                        WHEN 'PROCESANDO_LECTURA' THEN 0
                        WHEN 'PENDIENTE_LECTURA' THEN 1
                        WHEN 'ERROR_LECTURA' THEN 2
                        WHEN 'SIN_PROVEEDOR' THEN 3
                        WHEN 'PROCESADO' THEN 4
                        WHEN 'CANCELADO' THEN 5
                        ELSE 6
                    END,
                    FechaHora_Proceso DESC,
                    ID DESC;
                """;

            var snapshot = new InterfacesCompraIaQueueSnapshotDto();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await using (var cmd = new SqlCommand(summarySql, cn))
            await using (var rd = await cmd.ExecuteReaderAsync(token))
            {
                if (await rd.ReadAsync(token))
                {
                    snapshot.Pendientes = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd.GetValue(0), CultureInfo.InvariantCulture);
                    snapshot.Procesando = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd.GetValue(1), CultureInfo.InvariantCulture);
                    snapshot.Procesados = rd.IsDBNull(2) ? 0 : Convert.ToInt32(rd.GetValue(2), CultureInfo.InvariantCulture);
                    snapshot.ConError = rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd.GetValue(3), CultureInfo.InvariantCulture);
                    snapshot.Cancelados = rd.IsDBNull(4) ? 0 : Convert.ToInt32(rd.GetValue(4), CultureInfo.InvariantCulture);
                }
            }

            var items = new List<InterfacesCompraIaResultadoDto>();
            await using (var cmd = new SqlCommand(itemsSql, cn))
            await using (var rd = await cmd.ExecuteReaderAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    items.Add(MapCompraIaResult(rd));
                }
            }

            snapshot.Items = items;
            return snapshot;
        }, "No se pudo cargar la cola de procesamiento de compras.", ct);

    public Task CancelCompraDetectionAsync(InterfacesCompraIaAccionRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "CancelCompraDetection", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Id <= 0)
                throw new InvalidOperationException("El registro de cola es obligatorio.");

            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await CancelCompraDetectionInternalAsync(cn, request.Id, user, pc, token);
        }, "No se pudo cancelar el procesamiento de compra.", ct);

    public Task RetryCompraDetectionAsync(InterfacesCompraIaAccionRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "RetryCompraDetection", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Id <= 0)
                throw new InvalidOperationException("El registro de cola es obligatorio.");

            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await RetryCompraDetectionInternalAsync(cn, request.Id, user, pc, token);
        }, "No se pudo reencolar el procesamiento de compra.", ct);

    public Task<int> ProcessCompraIaQueueAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "ProcessCompraIaQueue", async token =>
        {
            var settings = await interfacesConfigService.GetCompraIaSettingsAsync(token);
            if (!settings.WorkerHabilitado || !settings.Habilitado)
                return 0;

            var maxParallel = Math.Max(1, settings.WorkerMaxParalelo);
            var tasks = Enumerable.Range(0, maxParallel)
                .Select(_ => ProcessNextQueuedCompraDetectionAsync(token))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            return results.Sum();
        }, "No se pudo procesar la cola de compras.", ct);

    public Task<int> DeleteAsync(InterfacesEliminarComprobantesRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "Delete", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var ids = request.IdsComprobanteRecibido
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                throw new InvalidOperationException("Debés seleccionar al menos un documento para eliminar.");

            var user = NormalizeActor(request.UsuarioAccion, Environment.UserName, 50);
            var pc = NormalizeActor(request.PcAccion, ResolvePc(), 100);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var snapshot = await GetDeleteSnapshotAsync(cn, ids, token);
            if (snapshot.Count == 0)
                return 0;

            var existingIds = snapshot
                .Select(item => item.IdComprobanteRecibido)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                await DeleteByIdsAsync(cn, (SqlTransaction)tx, "dbo.INT_COMPROBANTE_RECIBIDO_HIST", "IdComprobanteRecibido", existingIds, token);
                await DeleteByIdsAsync(cn, (SqlTransaction)tx, "dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO", "IdComprobanteRecibido", existingIds, token);
                await DeleteByIdsAsync(cn, (SqlTransaction)tx, "dbo.INT_COMPROBANTE_RECIBIDO", "IdComprobanteRecibido", existingIds, token);
                await tx.CommitAsync(token);
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(token);
                }
                catch
                {
                }

                throw;
            }

            await CleanupDeletedFilesAsync(snapshot, token);

            await _appEvents.LogAuditAsync(
                "Interfaces",
                "Delete",
                "INT_COMPROBANTE_RECIBIDO",
                string.Join(",", existingIds),
                "Comprobantes eliminados físicamente junto con sus registros dependientes.",
                new
                {
                    Cantidad = existingIds.Count,
                    Ids = existingIds
                },
                token);

            return existingIds.Count;
        }, "No se pudieron eliminar los documentos seleccionados.", ct);

    public Task<InterfacesAdjuntoServeDto?> GetAttachmentForServeAsync(long idAdjunto, CancellationToken ct = default)
        => ExecuteLoggedAsync("Interfaces", "GetAttachmentForServe", async token =>
        {
            const string sql = """
                SELECT
                    ISNULL(c.RutaBase, ''),
                    ISNULL(a.RutaRelativa, ''),
                    ISNULL(a.MimeType, ''),
                    ISNULL(a.NombreOriginal, '')
                FROM dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO a
                INNER JOIN dbo.INT_COMPROBANTE_RECIBIDO c
                    ON c.IdComprobanteRecibido = a.IdComprobanteRecibido
                WHERE a.IdAdjunto = @IdAdjunto
                  AND ISNULL(a.Eliminado, 0) = 0
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            if (!await rd.ReadAsync(token))
                return null;

            var rutaBase = GetString(rd, 0);
            var rutaRelativa = GetString(rd, 1);
            if (string.IsNullOrWhiteSpace(rutaBase) || string.IsNullOrWhiteSpace(rutaRelativa))
                return null;

            return new InterfacesAdjuntoServeDto
            {
                RutaCompleta = BuildStoredFileReference(rutaBase, rutaRelativa),
                MimeType = GetString(rd, 2),
                NombreArchivo = GetString(rd, 3)
            };
        }, "No se pudo obtener el adjunto solicitado.", ct);

    private static async Task UpdateAttachmentCountAsync(SqlConnection cn, SqlTransaction tx, long idComprobante, int count, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.INT_COMPROBANTE_RECIBIDO
            SET CantidadAdjuntos = @CantidadAdjuntos
            WHERE IdComprobanteRecibido = @IdComprobanteRecibido
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@CantidadAdjuntos", count);
        cmd.Parameters.AddWithValue("@IdComprobanteRecibido", idComprobante);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task RecalculateAttachmentCountAsync(SqlConnection cn, SqlTransaction tx, long idComprobante, string usuario, string pc, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.INT_COMPROBANTE_RECIBIDO
            SET
                CantidadAdjuntos =
                (
                    SELECT COUNT(*)
                    FROM dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO
                    WHERE IdComprobanteRecibido = @IdComprobanteRecibido
                      AND ISNULL(Eliminado, 0) = 0
                ),
                FechaHora_Modificacion = GETDATE(),
                UsuarioModificacion = @UsuarioModificacion,
                PcModificacion = @PcModificacion
            WHERE IdComprobanteRecibido = @IdComprobanteRecibido
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@IdComprobanteRecibido", idComprobante);
        cmd.Parameters.AddWithValue("@UsuarioModificacion", DbNullable(usuario, 50));
        cmd.Parameters.AddWithValue("@PcModificacion", DbNullable(pc, 100));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureDocumentTypeExistsAsync(SqlConnection cn, int idTipoDocumento, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.INT_TIPO_DOCUMENTO
            WHERE IdTipoDocumento = @IdTipoDocumento
              AND ISNULL(Activo, 1) = 1
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdTipoDocumento", idTipoDocumento);
        var exists = await cmd.ExecuteScalarAsync(ct);
        if (exists is null)
            throw new InvalidOperationException("El tipo documental seleccionado no existe o está inactivo.");
    }

    private static async Task<IReadOnlyList<InterfacesDeleteSnapshotItem>> GetDeleteSnapshotAsync(SqlConnection cn, IReadOnlyList<long> ids, CancellationToken ct)
    {
        using var cmd = new SqlCommand(string.Empty, cn);
        var inClause = AddIdParameters(cmd, "@Id", ids);
        cmd.CommandText = $"""
            SELECT
                c.IdComprobanteRecibido,
                ISNULL(c.RutaBase, ''),
                ISNULL(a.RutaRelativa, '')
            FROM dbo.INT_COMPROBANTE_RECIBIDO c
            LEFT JOIN dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO a
                ON a.IdComprobanteRecibido = c.IdComprobanteRecibido
            WHERE c.IdComprobanteRecibido IN ({inClause})
            ORDER BY c.IdComprobanteRecibido, a.IdAdjunto
            """;

        var items = new List<InterfacesDeleteSnapshotItem>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new InterfacesDeleteSnapshotItem
            {
                IdComprobanteRecibido = rd.GetInt64(0),
                RutaBase = GetString(rd, 1),
                RutaRelativa = GetString(rd, 2)
            });
        }

        return items;
    }

    private static async Task<IReadOnlyList<InterfacesAdjuntoDto>> GetAttachmentsInternalAsync(SqlConnection cn, long idComprobanteRecibido, CancellationToken ct)
    {
        const string sql = """
            SELECT
                IdAdjunto,
                IdComprobanteRecibido,
                ISNULL(Orden, 0),
                ISNULL(NombreOriginal, ''),
                ISNULL(NombreGuardado, ''),
                ISNULL(RutaRelativa, ''),
                ISNULL(Extension, ''),
                ISNULL(MimeType, ''),
                ISNULL(TamanoBytes, 0),
                ISNULL(EsPrincipal, 0),
                ISNULL(Eliminado, 0),
                FechaHora_Grabacion
            FROM dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO
            WHERE IdComprobanteRecibido = @IdComprobanteRecibido
              AND ISNULL(Eliminado, 0) = 0
            ORDER BY Orden, IdAdjunto
            """;

        var items = new List<InterfacesAdjuntoDto>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdComprobanteRecibido", idComprobanteRecibido);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new InterfacesAdjuntoDto
            {
                IdAdjunto = rd.GetInt64(0),
                IdComprobanteRecibido = rd.GetInt64(1),
                Orden = GetInt(rd, 2),
                NombreOriginal = GetString(rd, 3),
                NombreGuardado = GetString(rd, 4),
                RutaRelativa = GetString(rd, 5),
                Extension = GetString(rd, 6),
                MimeType = GetString(rd, 7),
                TamanoBytes = GetLong(rd, 8),
                EsPrincipal = GetBool(rd, 9),
                Eliminado = GetBool(rd, 10),
                FechaHoraGrabacion = rd.GetDateTime(11)
            });
        }

        return items;
    }

    private async Task CleanupDeletedFilesAsync(IReadOnlyList<InterfacesDeleteSnapshotItem> snapshot, CancellationToken ct)
    {
        if (snapshot.Count == 0)
            return;

        InterfacesUploadSettingsDto? settings = null;
        var fullPaths = snapshot
            .Where(item => !string.IsNullOrWhiteSpace(item.RutaBase) && !string.IsNullOrWhiteSpace(item.RutaRelativa))
            .Select(item => BuildStoredFileReference(item.RutaBase, item.RutaRelativa))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var fullPath in fullPaths)
        {
            try
            {
                if (fullPath.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                {
                    settings ??= await interfacesConfigService.GetUploadSettingsAsync(ct);
                    await DeleteFtpFileIfExistsAsync(settings, fullPath, ct);
                }
                else if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (Exception ex)
            {
                await _appEvents.LogErrorAsync(
                    "Interfaces",
                    "DeleteCleanup",
                    ex,
                    "No se pudo eliminar un archivo físico asociado al comprobante borrado.",
                    new { Ruta = fullPath },
                    AppEventSeverity.Warning,
                    ct);
            }
        }
    }

    private static async Task<InterfacesAdjuntoDto?> GetAttachmentByIdAsync(SqlConnection cn, long idAdjunto, CancellationToken ct)
    {
        const string sql = """
            SELECT
                IdAdjunto,
                IdComprobanteRecibido,
                ISNULL(Orden, 0),
                ISNULL(NombreOriginal, ''),
                ISNULL(NombreGuardado, ''),
                ISNULL(RutaRelativa, ''),
                ISNULL(Extension, ''),
                ISNULL(MimeType, ''),
                ISNULL(TamanoBytes, 0),
                ISNULL(EsPrincipal, 0),
                ISNULL(Eliminado, 0),
                FechaHora_Grabacion
            FROM dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO
            WHERE IdAdjunto = @IdAdjunto
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new InterfacesAdjuntoDto
        {
            IdAdjunto = rd.GetInt64(0),
            IdComprobanteRecibido = rd.GetInt64(1),
            Orden = GetInt(rd, 2),
            NombreOriginal = GetString(rd, 3),
            NombreGuardado = GetString(rd, 4),
            RutaRelativa = GetString(rd, 5),
            Extension = GetString(rd, 6),
            MimeType = GetString(rd, 7),
            TamanoBytes = GetLong(rd, 8),
            EsPrincipal = GetBool(rd, 9),
            Eliminado = GetBool(rd, 10),
            FechaHoraGrabacion = rd.GetDateTime(11)
        };
    }

    private static async Task<IReadOnlyList<InterfacesHistorialDto>> GetHistoryInternalAsync(SqlConnection cn, long idComprobanteRecibido, CancellationToken ct)
    {
        const string sql = """
            SELECT
                h.IdHistorial,
                h.FechaHora,
                ISNULL(h.Usuario, ''),
                ISNULL(h.Pc, ''),
                ISNULL(h.Accion, ''),
                h.IdEstadoAnterior,
                ISNULL(ea.Descripcion, ''),
                h.IdEstadoNuevo,
                ISNULL(en.Descripcion, ''),
                ISNULL(h.Observacion, ''),
                ISNULL(h.DataJson, '')
            FROM dbo.INT_COMPROBANTE_RECIBIDO_HIST h
            LEFT JOIN dbo.INT_ESTADO ea
                ON ea.IdEstado = h.IdEstadoAnterior
            LEFT JOIN dbo.INT_ESTADO en
                ON en.IdEstado = h.IdEstadoNuevo
            WHERE h.IdComprobanteRecibido = @IdComprobanteRecibido
            ORDER BY h.FechaHora DESC, h.IdHistorial DESC
            """;

        var items = new List<InterfacesHistorialDto>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdComprobanteRecibido", idComprobanteRecibido);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new InterfacesHistorialDto
            {
                IdHistorial = rd.GetInt64(0),
                FechaHora = rd.GetDateTime(1),
                Usuario = GetString(rd, 2),
                Pc = GetString(rd, 3),
                Accion = GetString(rd, 4),
                IdEstadoAnterior = rd.IsDBNull(5) ? null : rd.GetInt32(5),
                EstadoAnteriorDescripcion = GetString(rd, 6),
                IdEstadoNuevo = rd.IsDBNull(7) ? null : rd.GetInt32(7),
                EstadoNuevoDescripcion = GetString(rd, 8),
                Observacion = GetString(rd, 9),
                DataJson = GetString(rd, 10)
            });
        }

        return items;
    }

    private static async Task InsertHistoryAsync(
        SqlConnection cn,
        SqlTransaction tx,
        long idComprobanteRecibido,
        string accion,
        int? idEstadoAnterior,
        int? idEstadoNuevo,
        string usuario,
        string pc,
        string observacion,
        object? data,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.INT_COMPROBANTE_RECIBIDO_HIST
            (
                IdComprobanteRecibido,
                Usuario,
                Pc,
                Accion,
                IdEstadoAnterior,
                IdEstadoNuevo,
                Observacion,
                DataJson
            )
            VALUES
            (
                @IdComprobanteRecibido,
                @Usuario,
                @Pc,
                @Accion,
                @IdEstadoAnterior,
                @IdEstadoNuevo,
                @Observacion,
                @DataJson
            )
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@IdComprobanteRecibido", idComprobanteRecibido);
        cmd.Parameters.AddWithValue("@Usuario", DbNullable(usuario, 50));
        cmd.Parameters.AddWithValue("@Pc", DbNullable(pc, 100));
        cmd.Parameters.AddWithValue("@Accion", Truncate(accion, 30));
        cmd.Parameters.AddWithValue("@IdEstadoAnterior", (object?)idEstadoAnterior ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdEstadoNuevo", (object?)idEstadoNuevo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Observacion", DbNullable(observacion, 1000));
        cmd.Parameters.AddWithValue("@DataJson", DbNullable(SerializeData(data), 4000));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<InterfacesEstadoOptionDto?> GetStateByIdAsync(SqlConnection cn, int idEstado, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                IdEstado,
                ISNULL(Codigo, ''),
                ISNULL(Descripcion, ''),
                ISNULL(Orden, 0),
                ISNULL(Activo, 1),
                ISNULL(PermiteEdicion, 0),
                ISNULL(EsInicial, 0),
                ISNULL(EsFinal, 0),
                ISNULL(Color, '')
            FROM dbo.INT_ESTADO
            WHERE IdEstado = @IdEstado
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdEstado", idEstado);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new InterfacesEstadoOptionDto
        {
            IdEstado = rd.GetInt32(0),
            Codigo = GetString(rd, 1),
            Descripcion = GetString(rd, 2),
            Orden = GetInt(rd, 3),
            Activo = GetBool(rd, 4),
            PermiteEdicion = GetBool(rd, 5),
            EsInicial = GetBool(rd, 6),
            EsFinal = GetBool(rd, 7),
            Color = GetString(rd, 8)
        };
    }

    private static async Task<InterfacesEstadoOptionDto?> GetStateByCodeAsync(SqlConnection cn, string codigoEstado, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                IdEstado,
                ISNULL(Codigo, ''),
                ISNULL(Descripcion, ''),
                ISNULL(Orden, 0),
                ISNULL(Activo, 1),
                ISNULL(PermiteEdicion, 0),
                ISNULL(EsInicial, 0),
                ISNULL(EsFinal, 0),
                ISNULL(Color, '')
            FROM dbo.INT_ESTADO
            WHERE UPPER(LTRIM(RTRIM(Codigo))) = UPPER(LTRIM(RTRIM(@Codigo)))
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Codigo", codigoEstado.Trim());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new InterfacesEstadoOptionDto
        {
            IdEstado = rd.GetInt32(0),
            Codigo = GetString(rd, 1),
            Descripcion = GetString(rd, 2),
            Orden = GetInt(rd, 3),
            Activo = GetBool(rd, 4),
            PermiteEdicion = GetBool(rd, 5),
            EsInicial = GetBool(rd, 6),
            EsFinal = GetBool(rd, 7),
            Color = GetString(rd, 8)
        };
    }

    private static void ValidateCreateRequest(InterfacesCrearComprobanteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IdTipoDocumento <= 0)
            throw new InvalidOperationException("El tipo documental es obligatorio.");
        if (request.Adjuntos is null || request.Adjuntos.Count == 0)
            throw new InvalidOperationException("Debés adjuntar al menos un archivo.");
    }

    private static void ValidateSettings(InterfacesUploadSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.RutaBase))
            throw new InvalidOperationException("La ruta base o carpeta remota para documentos no está configurada.");
        if (settings.UsaFtp)
        {
            if (string.IsNullOrWhiteSpace(settings.FtpHost))
                throw new InvalidOperationException("El host FTP no está configurado.");
            if (string.IsNullOrWhiteSpace(settings.FtpUsuario))
                throw new InvalidOperationException("El usuario FTP no está configurado.");
        }
    }

    private static void ValidateAttachment(InterfacesCrearAdjuntoRequest attachment, InterfacesUploadSettingsDto settings)
    {
        if (attachment is null)
            throw new InvalidOperationException("Se recibió un adjunto inválido.");
        if (string.IsNullOrWhiteSpace(attachment.NombreArchivo))
            throw new InvalidOperationException("El nombre del archivo es obligatorio.");
        if (attachment.TamanoBytes <= 0)
            throw new InvalidOperationException("Uno de los archivos está vacío.");
        if (attachment.TamanoBytes > settings.TamanoMaximoBytes)
            throw new InvalidOperationException($"Uno de los archivos supera el máximo permitido de {settings.TamanoMaximoMb} MB.");

        var extension = NormalizeExtension(Path.GetExtension(attachment.NombreArchivo));
        if (settings.ExtensionesPermitidas.Count > 0
            && !settings.ExtensionesPermitidas.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"La extensión {extension} no está permitida para Interfaces.");
        }
    }

    private async Task<InterfacesDetalleDto> RequireEditableComprobanteAsync(long idComprobanteRecibido, CancellationToken ct)
    {
        var current = await GetByIdAsync(idComprobanteRecibido, ct)
            ?? throw new InvalidOperationException("El comprobante indicado no existe.");

        if (!current.PermiteEdicion)
            throw new InvalidOperationException("El comprobante seleccionado ya no permite edición por su estado actual.");

        return current;
    }

    private static InterfacesViewSettingsDto CreateDefaultViewSettings()
        => new()
        {
            AgruparPor = InterfacesViewGroupKeys.None,
            Columnas =
            [
                new() { Key = InterfacesViewColumnKeys.Numero, Label = "Número", Visible = true, Order = 0 },
                new() { Key = InterfacesViewColumnKeys.Fecha, Label = "Fecha", Visible = true, Order = 1 },
                new() { Key = InterfacesViewColumnKeys.Tipo, Label = "Tipo", Visible = true, Order = 2 },
                new() { Key = InterfacesViewColumnKeys.Estado, Label = "Estado", Visible = true, Order = 3 },
                new() { Key = InterfacesViewColumnKeys.Usuario, Label = "Usuario", Visible = true, Order = 4 },
                new() { Key = InterfacesViewColumnKeys.Observacion, Label = "Observación", Visible = true, Order = 5 },
                new() { Key = InterfacesViewColumnKeys.Adjuntos, Label = "Adjuntos", Visible = true, Order = 6 }
            ]
        };

    private static InterfacesViewSettingsDto NormalizeViewSettings(InterfacesViewSettingsDto? settings)
    {
        var defaults = CreateDefaultViewSettings();
        if (settings is null)
            return defaults;

        var incoming = settings.Columnas
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        var normalized = new InterfacesViewSettingsDto
        {
            AgruparPor = settings.AgruparPor switch
            {
                InterfacesViewGroupKeys.Estado => InterfacesViewGroupKeys.Estado,
                InterfacesViewGroupKeys.Tipo => InterfacesViewGroupKeys.Tipo,
                _ => InterfacesViewGroupKeys.None
            },
            Columnas = defaults.Columnas
                .Select(defaultCol =>
                {
                    if (!incoming.TryGetValue(defaultCol.Key, out var source))
                        return new InterfacesViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = defaultCol.Visible, Order = defaultCol.Order };

                    return new InterfacesViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = source.Visible, Order = source.Order };
                })
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select((col, idx) => { col.Order = idx; return col; })
                .ToList()
        };

        if (!normalized.Columnas.Any(c => c.Visible))
            normalized.Columnas[0].Visible = true;

        return normalized;
    }

    private static string BuildViewConfigKey(string userName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant())));
        return $"{ViewConfigPrefix}{hash[..24]}";
    }

    private static async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END, name
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var column = Convert.ToString(result) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
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
        catch (SqlException ex) when (ex.Number == 208)
        {
            var incidentId = await _appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException("El esquema del módulo Interfaces no está disponible en la base activa.", incidentId, ex);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await _appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
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
        catch (SqlException ex) when (ex.Number == 208)
        {
            var incidentId = await _appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException("El esquema del módulo Interfaces no está disponible en la base activa.", incidentId, ex);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await _appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private static string ResolvePc() => Environment.MachineName;

    private static string NormalizeActor(string? value, string fallback, int maxLength)
    {
        var resolved = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return Truncate(resolved, maxLength);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        return extension.Trim().StartsWith('.')
            ? extension.Trim().ToLowerInvariant()
            : "." + extension.Trim().ToLowerInvariant();
    }

    private static string ResolveStoredBase(InterfacesUploadSettingsDto settings)
        => settings.UsaFtp ? settings.BuildFtpBaseUrl().TrimEnd('/') : settings.RutaBase;

    private static string CombineStoragePath(string folder, string fileName)
        => Path.Combine(folder, fileName);

    private static string BuildStoredFileReference(string rutaBase, string rutaRelativa)
    {
        if (rutaBase.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            var relative = rutaRelativa.Replace('\\', '/').TrimStart('/');
            return rutaBase.TrimEnd('/') + "/" + relative;
        }

        return Path.Combine(rutaBase, rutaRelativa);
    }

    private static string BuildComprobanteFolder(DateTime fechaHoraGrabacion, long idComprobanteRecibido)
        => Path.Combine(
            fechaHoraGrabacion.ToString("yyyy_MM", CultureInfo.InvariantCulture),
            idComprobanteRecibido.ToString(CultureInfo.InvariantCulture));

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length > 150 ? (string.Empty, normalized) : (normalized, string.Empty);
    }

    private static async Task SaveAttachmentAsync(
        InterfacesUploadSettingsDto settings,
        string relativePath,
        InterfacesCrearAdjuntoRequest attachment,
        List<string> savedFiles,
        CancellationToken ct)
    {
        if (settings.UsaFtp)
        {
            var remoteUrl = settings.BuildFtpBaseUrl().TrimEnd('/') + "/" + relativePath.Replace('\\', '/').TrimStart('/');
            await EnsureFtpDirectoriesAsync(settings, relativePath, ct);
            await UploadToFtpAsync(settings, remoteUrl, attachment.Contenido, ct);
            savedFiles.Add(remoteUrl);
            return;
        }

        var absolutePath = Path.Combine(settings.RutaBase, relativePath);
        await using var fs = File.Create(absolutePath);
        await attachment.Contenido.CopyToAsync(fs, ct);
        savedFiles.Add(absolutePath);
    }

    private static async Task CleanupSavedFilesAsync(InterfacesUploadSettingsDto settings, IReadOnlyList<string> savedFiles, CancellationToken ct)
    {
        foreach (var path in savedFiles)
        {
            try
            {
                if (settings.UsaFtp)
                    await DeleteFtpFileIfExistsAsync(settings, path, ct);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }

#pragma warning disable SYSLIB0014
    private static async Task EnsureFtpDirectoriesAsync(InterfacesUploadSettingsDto settings, string relativePath, CancellationToken ct)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            return;

        var current = settings.BuildFtpBaseUrl().TrimEnd('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            current += "/" + parts[i];
            var request = (FtpWebRequest)WebRequest.Create(current);
            request.Method = WebRequestMethods.Ftp.MakeDirectory;
            request.Credentials = new NetworkCredential(settings.FtpUsuario, settings.FtpClave);
            request.UseBinary = true;
            request.UsePassive = settings.FtpModoPasivo;
            request.KeepAlive = false;
            try
            {
                using var response = (FtpWebResponse)await request.GetResponseAsync();
            }
            catch (WebException ex)
            {
                if (ex.Response is FtpWebResponse ftpResponse)
                {
                    if (ftpResponse.StatusCode is FtpStatusCode.ActionNotTakenFileUnavailable or FtpStatusCode.ActionNotTakenFilenameNotAllowed)
                        continue;
                }
            }
        }
    }

    private static async Task UploadToFtpAsync(InterfacesUploadSettingsDto settings, string remoteUrl, Stream content, CancellationToken ct)
    {
        var request = (FtpWebRequest)WebRequest.Create(remoteUrl);
        request.Method = WebRequestMethods.Ftp.UploadFile;
        request.Credentials = new NetworkCredential(settings.FtpUsuario, settings.FtpClave);
        request.UseBinary = true;
        request.UsePassive = settings.FtpModoPasivo;
        request.KeepAlive = false;

        await using (var requestStream = await request.GetRequestStreamAsync())
        {
            if (content.CanSeek)
                content.Position = 0;
            await content.CopyToAsync(requestStream, ct);
        }

        using var response = (FtpWebResponse)await request.GetResponseAsync();
    }

    private static async Task DeleteFtpFileIfExistsAsync(InterfacesUploadSettingsDto settings, string remoteUrl, CancellationToken ct)
    {
        var request = (FtpWebRequest)WebRequest.Create(remoteUrl);
        request.Method = WebRequestMethods.Ftp.DeleteFile;
        request.Credentials = new NetworkCredential(settings.FtpUsuario, settings.FtpClave);
        request.UseBinary = true;
        request.UsePassive = settings.FtpModoPasivo;
        request.KeepAlive = false;

        try
        {
            using var response = (FtpWebResponse)await request.GetResponseAsync();
        }
        catch (WebException)
        {
        }
    }
#pragma warning restore SYSLIB0014

    private async Task<InterfacesCompraIaResultadoDto?> GetCompraDetectionInternalAsync(SqlConnection cn, long idComprobanteRecibido, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                ID,
                IdComprobanteRecibido,
                IdAdjuntoFuente,
                ISNULL(Estado, ''),
                FechaHora_Proceso,
                FechaHora_Inicio,
                FechaHora_Fin,
                FechaHora_Modificacion,
                ISNULL(Usuario_Proceso, ''),
                ISNULL(Observaciones_Rev, ''),
                ISNULL(SolicitarCancelacion, 0),
                ISNULL(Intentos, 0),
                ISNULL(Archivo_RutaOriginal, ''),
                ISNULL(Archivo_NombreOriginal, ''),
                ISNULL(Archivo_NombreRenombrado, ''),
                ISNULL(Proveedor_Nombre, ''),
                ISNULL(Proveedor_CUIT, ''),
                ISNULL(Proveedor_Domicilio, ''),
                ISNULL(Proveedor_CondIVA, ''),
                ISNULL(Cuenta_Contable, ''),
                ISNULL(Match_Metodo, ''),
                ISNULL(TipoComprobante, ''),
                ISNULL(Letra, ''),
                ISNULL(PuntoVenta, ''),
                ISNULL(Numero, ''),
                Fecha,
                Vencimiento,
                ISNULL(CAE, ''),
                VtoCAE,
                ISNULL(Moneda, ''),
                NetoGravado,
                NetoNoGravado,
                Exento,
                IVA_21,
                IVA_105,
                IVA_27,
                Percepcion_IVA,
                Percepcion_IIBB,
                Percepcion_Ganancias,
                ImpuestosInternos,
                OtrosImpuestos,
                Total,
                ISNULL(Lector_Observaciones, ''),
                ISNULL(Lector_Error, ''),
                ISNULL(JsonResultado, '')
            FROM dbo.IA_Compras_CAB
            WHERE IdComprobanteRecibido = @IdComprobanteRecibido
            ORDER BY FechaHora_Proceso DESC, ID DESC
            """;

        InterfacesCompraIaResultadoDto? result = null;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@IdComprobanteRecibido", idComprobanteRecibido);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct))
            {
                result = MapCompraIaResult(rd);
            }
        }

        if (result is null)
            return null;

        result.Items = await GetCompraDetectionItemsInternalAsync(cn, result.Id, ct);
        return result;
    }

    private static InterfacesCompraIaResultadoDto MapCompraIaResult(SqlDataReader rd)
        => new()
        {
            Id = GetInt(rd, 0),
            IdComprobanteRecibido = rd.IsDBNull(1) ? null : rd.GetInt64(1),
            IdAdjuntoFuente = rd.IsDBNull(2) ? null : rd.GetInt64(2),
            Estado = GetString(rd, 3),
            FechaHoraProceso = rd.GetDateTime(4),
            FechaHoraInicio = rd.IsDBNull(5) ? null : rd.GetDateTime(5),
            FechaHoraFin = rd.IsDBNull(6) ? null : rd.GetDateTime(6),
            FechaHoraModificacion = rd.IsDBNull(7) ? null : rd.GetDateTime(7),
            UsuarioProceso = GetString(rd, 8),
            ObservacionesRevision = GetString(rd, 9),
            SolicitarCancelacion = GetBool(rd, 10),
            Intentos = GetInt(rd, 11),
            ArchivoRutaOriginal = GetString(rd, 12),
            ArchivoNombreOriginal = GetString(rd, 13),
            ArchivoNombreRenombrado = GetString(rd, 14),
            ProveedorNombre = GetString(rd, 15),
            ProveedorCuit = GetString(rd, 16),
            ProveedorDomicilio = GetString(rd, 17),
            ProveedorCondIva = GetString(rd, 18),
            CuentaContable = GetString(rd, 19),
            MatchMetodo = GetString(rd, 20),
            TipoComprobante = GetString(rd, 21),
            Letra = GetString(rd, 22),
            PuntoVenta = GetString(rd, 23),
            Numero = GetString(rd, 24),
            Fecha = rd.IsDBNull(25) ? null : rd.GetDateTime(25),
            Vencimiento = rd.IsDBNull(26) ? null : rd.GetDateTime(26),
            Cae = GetString(rd, 27),
            VtoCae = rd.IsDBNull(28) ? null : rd.GetDateTime(28),
            Moneda = GetString(rd, 29),
            NetoGravado = GetNullableDecimal(rd, 30),
            NetoNoGravado = GetNullableDecimal(rd, 31),
            Exento = GetNullableDecimal(rd, 32),
            Iva21 = GetNullableDecimal(rd, 33),
            Iva105 = GetNullableDecimal(rd, 34),
            Iva27 = GetNullableDecimal(rd, 35),
            PercepcionIva = GetNullableDecimal(rd, 36),
            PercepcionIibb = GetNullableDecimal(rd, 37),
            PercepcionGanancias = GetNullableDecimal(rd, 38),
            ImpuestosInternos = GetNullableDecimal(rd, 39),
            OtrosImpuestos = GetNullableDecimal(rd, 40),
            Total = GetNullableDecimal(rd, 41),
            LectorObservaciones = GetString(rd, 42),
            LectorError = GetString(rd, 43),
            JsonResultado = GetString(rd, 44)
        };

    private static async Task<IReadOnlyList<InterfacesCompraIaDetalleItemDto>> GetCompraDetectionItemsInternalAsync(SqlConnection cn, int idCab, CancellationToken ct)
    {
        const string sql = """
            SELECT
                ID,
                ID_CAB,
                NroRenglon,
                ISNULL(Cantidad, ''),
                ISNULL(Codigo_Articulo, ''),
                ISNULL(Descripcion, ''),
                ISNULL(UD, ''),
                Importe_Lista,
                Dto1,
                Dto2,
                Importe_Neto,
                IVA,
                ImpuestosInternos,
                Total,
                ISNULL(AuxNroLote, ''),
                ISNULL(AuxNroSerie, ''),
                ISNULL(BlPq, ''),
                ISNULL(Moneda, ''),
                TotImpInt
            FROM dbo.IA_Compras_DET
            WHERE ID_CAB = @ID_CAB
            ORDER BY NroRenglon, ID
            """;

        var items = new List<InterfacesCompraIaDetalleItemDto>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@ID_CAB", idCab);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new InterfacesCompraIaDetalleItemDto
            {
                Id = GetInt(rd, 0),
                IdCab = GetInt(rd, 1),
                NroRenglon = GetInt(rd, 2),
                Cantidad = GetString(rd, 3),
                CodigoArticulo = GetString(rd, 4),
                Descripcion = GetString(rd, 5),
                Ud = GetString(rd, 6),
                ImporteLista = GetNullableDecimal(rd, 7),
                Dto1 = GetNullableDouble(rd, 8),
                Dto2 = GetNullableDouble(rd, 9),
                ImporteNeto = GetNullableDecimal(rd, 10),
                Iva = GetNullableDouble(rd, 11),
                ImpuestosInternos = GetNullableDecimal(rd, 12),
                Total = GetNullableDecimal(rd, 13),
                AuxNroLote = GetString(rd, 14),
                AuxNroSerie = GetString(rd, 15),
                BlPq = GetString(rd, 16),
                Moneda = GetString(rd, 17),
                TotImpInt = GetNullableDecimal(rd, 18)
            });
        }

        return items;
    }

    private async Task EnsureCompraIaConfigurationReadyAsync(CancellationToken ct)
    {
        _ = NormalizeCompraIaSettings(await interfacesConfigService.GetCompraIaSettingsAsync(ct));
    }

    private static List<InterfacesAdjuntoDto> GetEligibleCompraAttachments(InterfacesDetalleDto detail)
    {
        if (!string.Equals(detail.TipoDocumentoCodigo, "COMPROBANTE_COMPRA", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La detección automática solo está disponible para comprobantes de compras.");

        return detail.Adjuntos
            .Where(static x => !x.Eliminado)
            .Where(x => IsFacturaReaderSupportedExtension(x.Extension, x.NombreOriginal))
            .OrderByDescending(static x => x.EsPrincipal)
            .ThenBy(static x => x.Orden)
            .ThenBy(static x => x.IdAdjunto)
            .ToList();
    }

    private async Task<InterfacesCompraIaResultadoDto> RunCompraDetectionAsync(
        long idComprobanteRecibido,
        string user,
        string pc,
        int? queueId,
        Func<CancellationToken, Task<bool>>? shouldCancelAsync,
        CancellationToken ct)
    {
        var detail = await GetByIdAsync(idComprobanteRecibido, ct)
            ?? throw new InvalidOperationException("El comprobante indicado no existe.");
        var eligibleAttachments = GetEligibleCompraAttachments(detail);
        if (eligibleAttachments.Count == 0)
            throw new InvalidOperationException("El documento no tiene adjuntos PDF o imagen compatibles para ejecutar la detección.");

        var uploadSettings = await interfacesConfigService.GetUploadSettingsAsync(ct);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var detectorSettings = NormalizeCompraIaSettings(await interfacesConfigService.GetCompraIaSettingsAsync(ct));

        var tempRoot = Path.Combine(Path.GetTempPath(), "AlfaCore", "interfaces-ia", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var stagedFiles = new List<string>();
        try
        {
            foreach (var attachment in eligibleAttachments)
            {
                if (shouldCancelAsync is not null && await shouldCancelAsync(ct))
                    throw new OperationCanceledException("El procesamiento fue cancelado por solicitud del usuario.");

                stagedFiles.Add(await StageAttachmentForIaAsync(uploadSettings, attachment, tempRoot, ct));
            }

            var jsonPath = await ExecuteFacturaReaderAsync(detectorSettings, stagedFiles, tempRoot, shouldCancelAsync, ct);
            var jsonText = await File.ReadAllTextAsync(jsonPath, Encoding.UTF8, ct);
            var payload = ParseCompraIaPayload(jsonText);
            payload = await ResolveProviderDataAsync(cn, payload, ct);

            await UpsertCompraDetectionAsync(
                cn,
                detail,
                eligibleAttachments[0],
                user,
                payload,
                jsonText,
                null,
                pc,
                ct);

            var saved = queueId.HasValue
                ? await GetCompraDetectionByQueueIdInternalAsync(cn, queueId.Value, ct)
                : await GetCompraDetectionInternalAsync(cn, idComprobanteRecibido, ct);

            return saved ?? throw new InvalidOperationException("No se pudo recuperar la detección guardada.");
        }
        catch (OperationCanceledException)
        {
            if (queueId.HasValue)
                await MarkCompraDetectionCancelledAsync(cn, queueId.Value, user, "Cancelado por el usuario.", ct);
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                await UpsertCompraDetectionAsync(
                    cn,
                    detail,
                    eligibleAttachments[0],
                    user,
                    null,
                    string.Empty,
                    Truncate(ex.Message, 1000),
                    pc,
                    ct);
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private async Task<int> ProcessNextQueuedCompraDetectionAsync(CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var claimed = await TryClaimNextQueuedCompraDetectionAsync(cn, ct);
        if (claimed is null || !claimed.IdComprobanteRecibido.HasValue)
            return 0;

        try
        {
            await RunCompraDetectionAsync(
                claimed.IdComprobanteRecibido.Value,
                string.IsNullOrWhiteSpace(claimed.UsuarioProceso) ? "WORKER" : claimed.UsuarioProceso,
                ResolvePc(),
                claimed.Id,
                token => IsCancellationRequestedAsync(claimed.Id, token),
                ct);

            await _appEvents.LogAuditAsync(
                "Interfaces",
                "ProcessCompraIaQueue",
                "IA_Compras_CAB",
                claimed.Id.ToString(CultureInfo.InvariantCulture),
                "Procesamiento automático de factura finalizado desde la cola.",
                new
                {
                    claimed.IdComprobanteRecibido,
                    claimed.Estado
                },
                ct);

            return 1;
        }
        catch (OperationCanceledException)
        {
            return 1;
        }
    }

    private static async Task<InterfacesCompraIaResultadoDto?> TryClaimNextQueuedCompraDetectionAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            ;WITH next_job AS
            (
                SELECT TOP (1) ID
                FROM dbo.IA_Compras_CAB WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Estado = 'PENDIENTE_LECTURA'
                  AND ISNULL(SolicitarCancelacion, 0) = 0
                ORDER BY FechaHora_Proceso, ID
            )
            UPDATE cab
            SET
                Estado = 'PROCESANDO_LECTURA',
                FechaHora_Inicio = GETDATE(),
                FechaHora_Fin = NULL,
                FechaHora_Modificacion = GETDATE(),
                SolicitarCancelacion = 0,
                Intentos = ISNULL(Intentos, 0) + 1
            OUTPUT
                inserted.ID,
                inserted.IdComprobanteRecibido,
                inserted.IdAdjuntoFuente,
                inserted.Estado,
                inserted.FechaHora_Proceso,
                inserted.FechaHora_Inicio,
                inserted.FechaHora_Fin,
                inserted.FechaHora_Modificacion,
                ISNULL(inserted.Usuario_Proceso, ''),
                ISNULL(inserted.Observaciones_Rev, ''),
                ISNULL(inserted.SolicitarCancelacion, 0),
                ISNULL(inserted.Intentos, 0),
                ISNULL(inserted.Archivo_RutaOriginal, ''),
                ISNULL(inserted.Archivo_NombreOriginal, ''),
                ISNULL(inserted.Archivo_NombreRenombrado, ''),
                ISNULL(inserted.Proveedor_Nombre, ''),
                ISNULL(inserted.Proveedor_CUIT, ''),
                ISNULL(inserted.Proveedor_Domicilio, ''),
                ISNULL(inserted.Proveedor_CondIVA, ''),
                ISNULL(inserted.Cuenta_Contable, ''),
                ISNULL(inserted.Match_Metodo, ''),
                ISNULL(inserted.TipoComprobante, ''),
                ISNULL(inserted.Letra, ''),
                ISNULL(inserted.PuntoVenta, ''),
                ISNULL(inserted.Numero, ''),
                inserted.Fecha,
                inserted.Vencimiento,
                ISNULL(inserted.CAE, ''),
                inserted.VtoCAE,
                ISNULL(inserted.Moneda, ''),
                inserted.NetoGravado,
                inserted.NetoNoGravado,
                inserted.Exento,
                inserted.IVA_21,
                inserted.IVA_105,
                inserted.IVA_27,
                inserted.Percepcion_IVA,
                inserted.Percepcion_IIBB,
                inserted.Percepcion_Ganancias,
                inserted.ImpuestosInternos,
                inserted.OtrosImpuestos,
                inserted.Total,
                ISNULL(inserted.Lector_Observaciones, ''),
                ISNULL(inserted.Lector_Error, ''),
                ISNULL(inserted.JsonResultado, '')
            FROM dbo.IA_Compras_CAB cab
            INNER JOIN next_job next_job
                ON next_job.ID = cab.ID;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return MapCompraIaResult(rd);
    }

    private async Task<InterfacesCompraIaResultadoDto> EnqueueCompraDetectionInternalAsync(
        SqlConnection cn,
        InterfacesDetalleDto detail,
        InterfacesAdjuntoDto sourceAttachment,
        string user,
        string pc,
        CancellationToken ct)
    {
        var settings = await interfacesConfigService.GetUploadSettingsAsync(ct);
        var storedBase = ResolveStoredBase(settings);
        var storedPath = BuildStoredFileReference(storedBase, sourceAttachment.RutaRelativa);

        const string selectSql = """
            SELECT TOP (1) ID
            FROM dbo.IA_Compras_CAB
            WHERE IdComprobanteRecibido = @IdComprobanteRecibido
            ORDER BY FechaHora_Proceso DESC, ID DESC
            """;

        int? existingId = null;
        await using (var selectCmd = new SqlCommand(selectSql, cn))
        {
            selectCmd.Parameters.AddWithValue("@IdComprobanteRecibido", detail.IdComprobanteRecibido);
            var scalar = await selectCmd.ExecuteScalarAsync(ct);
            if (scalar is not null && scalar != DBNull.Value)
                existingId = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
        }

        const string pendingNote = "En cola para lectura automática.";
        if (existingId.HasValue)
        {
            const string updateSql = """
                UPDATE dbo.IA_Compras_CAB
                SET
                    IdAdjuntoFuente = @IdAdjuntoFuente,
                    Estado = 'PENDIENTE_LECTURA',
                    FechaHora_Proceso = GETDATE(),
                    FechaHora_Inicio = NULL,
                    FechaHora_Fin = NULL,
                    FechaHora_Modificacion = GETDATE(),
                    Usuario_Proceso = @Usuario_Proceso,
                    Observaciones_Rev = @Observaciones_Rev,
                    SolicitarCancelacion = 0,
                    Archivo_RutaOriginal = @Archivo_RutaOriginal,
                    Archivo_NombreOriginal = @Archivo_NombreOriginal,
                    Archivo_NombreRenombrado = @Archivo_NombreRenombrado,
                    Lector_Error = NULL,
                    JsonResultado = NULL
                WHERE ID = @ID;
                """;

            await using var updateCmd = new SqlCommand(updateSql, cn);
            updateCmd.Parameters.AddWithValue("@ID", existingId.Value);
            updateCmd.Parameters.AddWithValue("@IdAdjuntoFuente", sourceAttachment.IdAdjunto);
            updateCmd.Parameters.AddWithValue("@Usuario_Proceso", DbNullable(user, 50));
            updateCmd.Parameters.AddWithValue("@Observaciones_Rev", DbNullable(pendingNote, 500));
            updateCmd.Parameters.AddWithValue("@Archivo_RutaOriginal", Truncate(storedPath, 500));
            updateCmd.Parameters.AddWithValue("@Archivo_NombreOriginal", Truncate(sourceAttachment.NombreOriginal, 260));
            updateCmd.Parameters.AddWithValue("@Archivo_NombreRenombrado", DbNullable(sourceAttachment.NombreGuardado, 260));
            await updateCmd.ExecuteNonQueryAsync(ct);
        }
        else
        {
            const string insertSql = """
                INSERT INTO dbo.IA_Compras_CAB
                (
                    IdComprobanteRecibido,
                    IdAdjuntoFuente,
                    Estado,
                    Usuario_Proceso,
                    Observaciones_Rev,
                    Archivo_RutaOriginal,
                    Archivo_NombreOriginal,
                    Archivo_NombreRenombrado,
                    SolicitarCancelacion,
                    Intentos
                )
                VALUES
                (
                    @IdComprobanteRecibido,
                    @IdAdjuntoFuente,
                    'PENDIENTE_LECTURA',
                    @Usuario_Proceso,
                    @Observaciones_Rev,
                    @Archivo_RutaOriginal,
                    @Archivo_NombreOriginal,
                    @Archivo_NombreRenombrado,
                    0,
                    0
                );
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            await using var insertCmd = new SqlCommand(insertSql, cn);
            insertCmd.Parameters.AddWithValue("@IdComprobanteRecibido", detail.IdComprobanteRecibido);
            insertCmd.Parameters.AddWithValue("@IdAdjuntoFuente", sourceAttachment.IdAdjunto);
            insertCmd.Parameters.AddWithValue("@Usuario_Proceso", DbNullable(user, 50));
            insertCmd.Parameters.AddWithValue("@Observaciones_Rev", DbNullable(pendingNote, 500));
            insertCmd.Parameters.AddWithValue("@Archivo_RutaOriginal", Truncate(storedPath, 500));
            insertCmd.Parameters.AddWithValue("@Archivo_NombreOriginal", Truncate(sourceAttachment.NombreOriginal, 260));
            insertCmd.Parameters.AddWithValue("@Archivo_NombreRenombrado", DbNullable(sourceAttachment.NombreGuardado, 260));
            existingId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }

        return await GetCompraDetectionByQueueIdInternalAsync(cn, existingId!.Value, ct)
            ?? throw new InvalidOperationException("No se pudo recuperar la cola de lectura encolada.");
    }

    private async Task CancelCompraDetectionInternalAsync(SqlConnection cn, int id, string user, string pc, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) Estado
            FROM dbo.IA_Compras_CAB
            WHERE ID = @ID;
            """;

        string currentState;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@ID", id);
            currentState = Convert.ToString(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(currentState))
            throw new InvalidOperationException("No se encontró el registro de procesamiento indicado.");

        if (string.Equals(currentState, "PENDIENTE_LECTURA", StringComparison.OrdinalIgnoreCase))
        {
            await MarkCompraDetectionCancelledAsync(cn, id, user, "Cancelado antes de iniciar el procesamiento.", ct);
        }
        else if (string.Equals(currentState, "PROCESANDO_LECTURA", StringComparison.OrdinalIgnoreCase))
        {
            const string updateSql = """
                UPDATE dbo.IA_Compras_CAB
                SET
                    SolicitarCancelacion = 1,
                    FechaHora_Modificacion = GETDATE(),
                    Observaciones_Rev = @Observaciones_Rev
                WHERE ID = @ID;
                """;

            await using var cmd = new SqlCommand(updateSql, cn);
            cmd.Parameters.AddWithValue("@ID", id);
            cmd.Parameters.AddWithValue("@Observaciones_Rev", DbNullable("Cancelación solicitada por usuario.", 500));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        else
        {
            throw new InvalidOperationException("Solo se pueden cancelar procesos pendientes o en ejecución.");
        }

        await _appEvents.LogAuditAsync(
            "Interfaces",
            "CancelCompraDetection",
            "IA_Compras_CAB",
            id.ToString(CultureInfo.InvariantCulture),
            "Cancelación de lectura automática solicitada.",
            new { Estado = currentState, Usuario = user, Pc = pc },
            ct);
    }

    private async Task RetryCompraDetectionInternalAsync(SqlConnection cn, int id, string user, string pc, CancellationToken ct)
    {
        const string updateSql = """
            UPDATE dbo.IA_Compras_CAB
            SET
                Estado = 'PENDIENTE_LECTURA',
                SolicitarCancelacion = 0,
                FechaHora_Proceso = GETDATE(),
                FechaHora_Inicio = NULL,
                FechaHora_Fin = NULL,
                FechaHora_Modificacion = GETDATE(),
                Usuario_Proceso = @Usuario_Proceso,
                Observaciones_Rev = @Observaciones_Rev,
                Lector_Error = NULL
            WHERE ID = @ID;

            IF @@ROWCOUNT = 0
                THROW 50000, 'No se encontró el registro de procesamiento indicado.', 1;
            """;

        await using var cmd = new SqlCommand(updateSql, cn);
        cmd.Parameters.AddWithValue("@ID", id);
        cmd.Parameters.AddWithValue("@Usuario_Proceso", DbNullable(user, 50));
        cmd.Parameters.AddWithValue("@Observaciones_Rev", DbNullable("Reencolado manualmente.", 500));
        await cmd.ExecuteNonQueryAsync(ct);

        await _appEvents.LogAuditAsync(
            "Interfaces",
            "RetryCompraDetection",
            "IA_Compras_CAB",
            id.ToString(CultureInfo.InvariantCulture),
            "Registro de lectura automática reencolado.",
            new { Usuario = user, Pc = pc },
            ct);
    }

    private async Task<bool> IsCancellationRequestedAsync(int id, CancellationToken ct)
    {
        const string sql = """
            SELECT ISNULL(SolicitarCancelacion, 0)
            FROM dbo.IA_Compras_CAB
            WHERE ID = @ID;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@ID", id);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return scalar is not null
            && scalar != DBNull.Value
            && Convert.ToInt32(scalar, CultureInfo.InvariantCulture) != 0;
    }

    private async Task<InterfacesCompraIaResultadoDto?> GetCompraDetectionByQueueIdInternalAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                ID,
                IdComprobanteRecibido,
                IdAdjuntoFuente,
                ISNULL(Estado, ''),
                FechaHora_Proceso,
                FechaHora_Inicio,
                FechaHora_Fin,
                FechaHora_Modificacion,
                ISNULL(Usuario_Proceso, ''),
                ISNULL(Observaciones_Rev, ''),
                ISNULL(SolicitarCancelacion, 0),
                ISNULL(Intentos, 0),
                ISNULL(Archivo_RutaOriginal, ''),
                ISNULL(Archivo_NombreOriginal, ''),
                ISNULL(Archivo_NombreRenombrado, ''),
                ISNULL(Proveedor_Nombre, ''),
                ISNULL(Proveedor_CUIT, ''),
                ISNULL(Proveedor_Domicilio, ''),
                ISNULL(Proveedor_CondIVA, ''),
                ISNULL(Cuenta_Contable, ''),
                ISNULL(Match_Metodo, ''),
                ISNULL(TipoComprobante, ''),
                ISNULL(Letra, ''),
                ISNULL(PuntoVenta, ''),
                ISNULL(Numero, ''),
                Fecha,
                Vencimiento,
                ISNULL(CAE, ''),
                VtoCAE,
                ISNULL(Moneda, ''),
                NetoGravado,
                NetoNoGravado,
                Exento,
                IVA_21,
                IVA_105,
                IVA_27,
                Percepcion_IVA,
                Percepcion_IIBB,
                Percepcion_Ganancias,
                ImpuestosInternos,
                OtrosImpuestos,
                Total,
                ISNULL(Lector_Observaciones, ''),
                ISNULL(Lector_Error, ''),
                ISNULL(JsonResultado, '')
            FROM dbo.IA_Compras_CAB
            WHERE ID = @ID;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@ID", id);
        InterfacesCompraIaResultadoDto? result = null;
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            if (await rd.ReadAsync(ct))
                result = MapCompraIaResult(rd);
        }

        if (result is null)
            return null;

        result.Items = await GetCompraDetectionItemsInternalAsync(cn, result.Id, ct);
        return result;
    }

    private async Task MarkCompraDetectionCancelledAsync(SqlConnection cn, int id, string user, string note, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.IA_Compras_CAB
            SET
                Estado = 'CANCELADO',
                SolicitarCancelacion = 0,
                FechaHora_Fin = ISNULL(FechaHora_Fin, GETDATE()),
                FechaHora_Modificacion = GETDATE(),
                Usuario_Proceso = @Usuario_Proceso,
                Observaciones_Rev = @Observaciones_Rev,
                Lector_Error = NULL
            WHERE ID = @ID;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@ID", id);
        cmd.Parameters.AddWithValue("@Usuario_Proceso", DbNullable(user, 50));
        cmd.Parameters.AddWithValue("@Observaciones_Rev", DbNullable(note, 500));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static InterfacesCompraIaSettings NormalizeCompraIaSettings(InterfacesCompraIaSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Habilitado)
            throw new InvalidOperationException("La detección automática de facturas de compras está deshabilitada en la base activa.");
        if (string.IsNullOrWhiteSpace(settings.PythonExe) || !File.Exists(settings.PythonExe))
            throw new InvalidOperationException("No se encontró el ejecutable Python configurado para la detección automática.");
        if (string.IsNullOrWhiteSpace(settings.ScriptPath) || !File.Exists(settings.ScriptPath))
            throw new InvalidOperationException("No se encontró el script configurado para la detección automática de facturas.");

        var workDir = settings.WorkDir;
        if (string.IsNullOrWhiteSpace(workDir))
            workDir = Path.GetDirectoryName(settings.ScriptPath) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
            throw new InvalidOperationException("No existe la carpeta de trabajo configurada para la detección automática de facturas.");

        return new InterfacesCompraIaSettings
        {
            PythonExe = settings.PythonExe,
            ScriptPath = settings.ScriptPath,
            WorkingDirectory = workDir
        };
    }

    private static async Task<string> StageAttachmentForIaAsync(
        InterfacesUploadSettingsDto settings,
        InterfacesAdjuntoDto attachment,
        string tempRoot,
        CancellationToken ct)
    {
        var extension = NormalizeExtension(string.IsNullOrWhiteSpace(attachment.Extension)
            ? Path.GetExtension(attachment.NombreOriginal)
            : attachment.Extension);
        var fileName = $"{attachment.Orden:0000}_{attachment.IdAdjunto}{extension}";
        var outputPath = Path.Combine(tempRoot, fileName);

        if (settings.UsaFtp)
        {
            var remoteUrl = BuildStoredFileReference(ResolveStoredBase(settings), attachment.RutaRelativa);
            await DownloadFtpFileAsync(settings, remoteUrl, outputPath, ct);
            return outputPath;
        }

        var sourcePath = BuildStoredFileReference(ResolveStoredBase(settings), attachment.RutaRelativa);
        await using var source = File.OpenRead(sourcePath);
        await using var target = File.Create(outputPath);
        await source.CopyToAsync(target, ct);
        return outputPath;
    }

    private static async Task<string> ExecuteFacturaReaderAsync(
        InterfacesCompraIaSettings settings,
        IReadOnlyList<string> stagedFiles,
        string tempRoot,
        Func<CancellationToken, Task<bool>>? shouldCancelAsync,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.PythonExe,
            WorkingDirectory = settings.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(settings.ScriptPath);
        foreach (var file in stagedFiles)
            startInfo.ArgumentList.Add(file);
        startInfo.ArgumentList.Add("--outdir");
        startInfo.ArgumentList.Add(tempRoot);
        startInfo.ArgumentList.Add("--auto");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("No se pudo iniciar el lector automático de facturas.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        while (!process.HasExited)
        {
            await Task.Delay(1000, ct);
            if (shouldCancelAsync is not null && await shouldCancelAsync(ct))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                throw new OperationCanceledException("El procesamiento fue cancelado por solicitud del usuario.");
            }
        }

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? "El lector automático finalizó con error."
                : stderr);

        var jsonPath = stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            throw new InvalidOperationException("El lector automático no devolvió un archivo JSON válido.");

        return jsonPath;
    }

    private static InterfacesCompraIaPayload ParseCompraIaPayload(string jsonText)
    {
        var root = JsonNode.Parse(jsonText)?.AsObject()
            ?? throw new InvalidOperationException("El lector devolvió un JSON inválido.");

        var cab = root["CAB"]?.AsObject();
        var totales = root["TOTALES"]?.AsObject();
        var meta = root["meta"]?.AsObject();
        var rows = root["ROWS"]?.AsArray();

        var payload = new InterfacesCompraIaPayload
        {
            ProveedorNombre = ReadJsonString(cab, "Nombre"),
            ProveedorCuit = Truncate(ReadJsonString(cab, "NUMERO_CUIT"), 13),
            ProveedorDomicilio = ReadJsonString(cab, "DOMICILIO"),
            ProveedorCondIva = ReadJsonString(cab, "CONDICIONIVA"),
            CuentaContable = Truncate(ReadJsonString(cab, "CUENTA"), 15),
            MatchMetodo = string.Empty,
            TipoComprobante = FirstNonEmpty(
                ReadJsonString(meta, "comprobante_raw"),
                ReadJsonString(cab, "CONCEPTO")),
            Letra = Truncate(ReadJsonString(cab, "LETRA"), 1),
            PuntoVenta = Truncate(ReadJsonString(cab, "SUCURSAL"), 4),
            Numero = Truncate(ReadJsonString(cab, "NUMERO"), 8),
            Fecha = ParseFlexibleDate(ReadJsonString(cab, "Fecha")),
            Vencimiento = ParseFlexibleDate(ReadJsonString(cab, "Vencimiento")),
            Cae = Truncate(ReadJsonString(cab, "NROCAI"), 14),
            VtoCae = ParseFlexibleDate(ReadJsonString(cab, "FHVToCAI")),
            Moneda = FirstNonEmpty(ReadJsonString(totales, "Moneda"), ReadJsonString(meta, "moneda_detectada")),
            NetoGravado = ReadJsonDecimal(totales, "Neto gravado"),
            NetoNoGravado = FirstNonNull(ReadJsonDecimal(totales, "Neto no gravado"), ReadJsonDecimal(totales, "No gravado")),
            Exento = ReadJsonDecimal(totales, "Exento"),
            Iva21 = FirstNonNull(ReadJsonDecimal(totales, "IVA 21%"), ReadJsonDecimal(totales, "IVA 21")),
            Iva105 = FirstNonNull(ReadJsonDecimal(totales, "IVA 10.5%"), ReadJsonDecimal(totales, "IVA 10.5")),
            Iva27 = FirstNonNull(ReadJsonDecimal(totales, "IVA 27%"), ReadJsonDecimal(totales, "IVA 27")),
            PercepcionIva = ReadJsonDecimal(totales, "Percepcion IVA"),
            PercepcionIibb = ReadJsonDecimal(totales, "Percepcion IIBB"),
            PercepcionGanancias = ReadJsonDecimal(totales, "Percepcion Ganancias"),
            ImpuestosInternos = ReadJsonDecimal(totales, "Impuestos internos"),
            OtrosImpuestos = ReadJsonDecimal(totales, "Otros impuestos"),
            Total = FirstNonNull(ReadJsonDecimal(totales, "Total final"), ReadJsonDecimal(totales, "Total")),
            LectorObservaciones = ReadJsonString(meta, "observaciones"),
            LectorError = string.Empty,
            Items = []
        };

        if (rows is not null)
        {
            var lineNumber = 1;
            foreach (var node in rows)
            {
                if (node is not JsonObject row)
                    continue;

                payload.Items.Add(new InterfacesCompraIaPayloadItem
                {
                    NroRenglon = lineNumber++,
                    Cantidad = ReadJsonString(row, "Cantidad"),
                    CodigoArticulo = ReadJsonString(row, "Codigo_Articulo"),
                    Descripcion = ReadJsonString(row, "Descripcion"),
                    Ud = ReadJsonString(row, "UD"),
                    ImporteLista = ReadJsonDecimal(row, "Importe_Lista"),
                    Dto1 = ReadJsonDouble(row, "% Dto1"),
                    Dto2 = ReadJsonDouble(row, "% Dto2"),
                    ImporteNeto = ReadJsonDecimal(row, "Importe_Neto"),
                    Iva = ReadJsonDouble(row, "IVA"),
                    ImpuestosInternos = ReadJsonDecimal(row, "Impuestos internos"),
                    Total = ReadJsonDecimal(row, "Total"),
                    AuxNroLote = ReadJsonString(row, "AuxNroLote"),
                    AuxNroSerie = ReadJsonString(row, "AuxNroSerie"),
                    BlPq = ReadJsonString(row, "Bl/Pq"),
                    Moneda = ReadJsonString(row, "Moneda"),
                    TotImpInt = ReadJsonDecimal(row, "Tot.Imp.Int")
                });
            }
        }

        return payload;
    }

    private async Task<InterfacesCompraIaPayload> ResolveProviderDataAsync(SqlConnection cn, InterfacesCompraIaPayload payload, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(payload.CuentaContable) && !LooksLikeCuit(payload.CuentaContable))
        {
            payload.MatchMetodo = "LECTOR";
            return payload;
        }

        var providerMatch = await FindProveedorAsync(cn, payload.ProveedorCuit, payload.ProveedorNombre, ct);
        if (providerMatch is null)
        {
            payload.CuentaContable = string.Empty;
            payload.MatchMetodo = "SIN_MATCH";
            return payload;
        }

        payload.CuentaContable = providerMatch.Codigo;
        payload.MatchMetodo = providerMatch.MatchMetodo;
        if (string.IsNullOrWhiteSpace(payload.ProveedorNombre))
            payload.ProveedorNombre = providerMatch.Nombre;
        if (string.IsNullOrWhiteSpace(payload.ProveedorCuit))
            payload.ProveedorCuit = providerMatch.Cuit;
        if (string.IsNullOrWhiteSpace(payload.ProveedorDomicilio))
            payload.ProveedorDomicilio = providerMatch.Domicilio;
        return payload;
    }

    private async Task<ProveedorMatchResult?> FindProveedorAsync(SqlConnection cn, string cuit, string nombre, CancellationToken ct)
    {
        var columns = await ResolveProveedorColumnsAsync(cn, ct);
        if (string.IsNullOrWhiteSpace(columns.Code) || string.IsNullOrWhiteSpace(columns.Name))
            return null;

        if (!string.IsNullOrWhiteSpace(cuit))
        {
            var match = await SearchProveedorAsync(cn, columns, cuit, string.Empty, "CUIT", ct);
            if (match is not null)
                return match;
        }

        if (!string.IsNullOrWhiteSpace(nombre))
            return await SearchProveedorAsync(cn, columns, string.Empty, nombre, "NOMBRE", ct);

        return null;
    }

    private static async Task<ProveedorColumns> ResolveProveedorColumnsAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.VT_PROVEEDORES')
            """;

        var names = new List<string>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            names.Add(GetString(rd, 0));

        var normalized = names.ToDictionary(NormalizeTextKey, static x => x, StringComparer.OrdinalIgnoreCase);
        return new ProveedorColumns
        {
            Code = PickProviderColumn(normalized, ["CODIGO", "CUENTA", "IDPROVEEDOR", "ID", "COD_PROVEEDOR"]),
            Name = PickProviderColumn(normalized, ["NOMBRE", "RAZON_SOCIAL", "RAZONSOCIAL", "DESCRIPCION"]),
            Cuit = PickProviderColumn(normalized, ["CUIT", "NUMERO_CUIT", "NROCUIT", "DOCUMENTO", "NRODOCUMENTO", "NUMERO_DOCUMENTO"]),
            Address = PickProviderColumn(normalized, ["DOMICILIO", "DIRECCION", "CALLE", "LOCALIDAD"])
        };
    }

    private static async Task<ProveedorMatchResult?> SearchProveedorAsync(
        SqlConnection cn,
        ProveedorColumns columns,
        string cuit,
        string nombre,
        string method,
        CancellationToken ct)
    {
        var cuitColumn = string.IsNullOrWhiteSpace(columns.Cuit) ? columns.Name : columns.Cuit;
        var addressColumn = string.IsNullOrWhiteSpace(columns.Address) ? columns.Name : columns.Address;
        var sql = new StringBuilder($"""
            SELECT TOP (1)
                CAST([{columns.Code}] AS nvarchar(100)),
                CAST([{columns.Name}] AS nvarchar(250)),
                CAST([{cuitColumn}] AS nvarchar(100)),
                CAST([{addressColumn}] AS nvarchar(250))
            FROM dbo.VT_PROVEEDORES
            WHERE 1 = 1
            """);

        await using var cmd = new SqlCommand();
        cmd.Connection = cn;
        if (!string.IsNullOrWhiteSpace(cuit))
        {
            sql.AppendLine()
                .Append($" AND REPLACE(REPLACE(REPLACE(CAST([{cuitColumn}] AS nvarchar(100)), '-', ''), '.', ''), ' ', '') LIKE @Cuit");
            cmd.Parameters.AddWithValue("@Cuit", "%" + OnlyDigits(cuit) + "%");
        }

        if (!string.IsNullOrWhiteSpace(nombre))
        {
            sql.AppendLine()
                .Append($"""
                     AND (
                            CAST([{columns.Name}] AS nvarchar(250)) LIKE @Nombre
                            OR CAST([{addressColumn}] AS nvarchar(250)) LIKE @Nombre
                         )
                    """);
            cmd.Parameters.AddWithValue("@Nombre", "%" + nombre.Trim() + "%");
        }

        sql.AppendLine()
            .Append($" ORDER BY CAST([{columns.Name}] AS nvarchar(250))");
        cmd.CommandText = sql.ToString();

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new ProveedorMatchResult
        {
            Codigo = Truncate(GetString(rd, 0), 15),
            Nombre = GetString(rd, 1),
            Cuit = Truncate(OnlyDigits(GetString(rd, 2)), 13),
            Domicilio = GetString(rd, 3),
            MatchMetodo = method
        };
    }

    private async Task UpsertCompraDetectionAsync(
        SqlConnection cn,
        InterfacesDetalleDto detail,
        InterfacesAdjuntoDto sourceAttachment,
        string user,
        InterfacesCompraIaPayload? payload,
        string rawJson,
        string? readerError,
        string pc,
        CancellationToken ct)
    {
        var settings = await interfacesConfigService.GetUploadSettingsAsync(ct);
        var storedBase = ResolveStoredBase(settings);
        var storedPath = BuildStoredFileReference(storedBase, sourceAttachment.RutaRelativa);
        var state = readerError is not null
            ? "ERROR_LECTURA"
            : string.IsNullOrWhiteSpace(payload?.CuentaContable)
                ? "SIN_PROVEEDOR"
                : "PROCESADO";

        const string selectSql = """
            SELECT TOP (1) ID
            FROM dbo.IA_Compras_CAB
            WHERE IdComprobanteRecibido = @IdComprobanteRecibido
            ORDER BY FechaHora_Proceso DESC, ID DESC
            """;

        int? existingId = null;
        await using (var selectCmd = new SqlCommand(selectSql, cn))
        {
            selectCmd.Parameters.AddWithValue("@IdComprobanteRecibido", detail.IdComprobanteRecibido);
            var scalar = await selectCmd.ExecuteScalarAsync(ct);
            if (scalar is not null && scalar != DBNull.Value)
                existingId = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
        }

        await using var tx = await cn.BeginTransactionAsync(ct);
        try
        {
            var recordId = existingId ?? 0;
            if (existingId.HasValue)
            {
                const string updateSql = """
                    UPDATE dbo.IA_Compras_CAB
                    SET
                        IdAdjuntoFuente = @IdAdjuntoFuente,
                        Estado = @Estado,
                        FechaHora_Proceso = GETDATE(),
                        FechaHora_Inicio = ISNULL(FechaHora_Inicio, GETDATE()),
                        FechaHora_Fin = GETDATE(),
                        FechaHora_Modificacion = GETDATE(),
                        Usuario_Proceso = @Usuario_Proceso,
                        Observaciones_Rev = @Observaciones_Rev,
                        SolicitarCancelacion = 0,
                        Archivo_RutaOriginal = @Archivo_RutaOriginal,
                        Archivo_NombreOriginal = @Archivo_NombreOriginal,
                        Archivo_NombreRenombrado = @Archivo_NombreRenombrado,
                        Proveedor_Nombre = @Proveedor_Nombre,
                        Proveedor_CUIT = @Proveedor_CUIT,
                        Proveedor_Domicilio = @Proveedor_Domicilio,
                        Proveedor_CondIVA = @Proveedor_CondIVA,
                        Cuenta_Contable = @Cuenta_Contable,
                        Match_Metodo = @Match_Metodo,
                        TipoComprobante = @TipoComprobante,
                        Letra = @Letra,
                        PuntoVenta = @PuntoVenta,
                        Numero = @Numero,
                        Fecha = @Fecha,
                        Vencimiento = @Vencimiento,
                        CAE = @CAE,
                        VtoCAE = @VtoCAE,
                        Moneda = @Moneda,
                        NetoGravado = @NetoGravado,
                        NetoNoGravado = @NetoNoGravado,
                        Exento = @Exento,
                        IVA_21 = @IVA_21,
                        IVA_105 = @IVA_105,
                        IVA_27 = @IVA_27,
                        Percepcion_IVA = @Percepcion_IVA,
                        Percepcion_IIBB = @Percepcion_IIBB,
                        Percepcion_Ganancias = @Percepcion_Ganancias,
                        ImpuestosInternos = @ImpuestosInternos,
                        OtrosImpuestos = @OtrosImpuestos,
                        Total = @Total,
                        Lector_Observaciones = @Lector_Observaciones,
                        Lector_Error = @Lector_Error,
                        JsonResultado = @JsonResultado
                    WHERE ID = @ID
                    """;

                await using var updateCmd = new SqlCommand(updateSql, cn, (SqlTransaction)tx);
                FillCompraCabParameters(updateCmd, detail, sourceAttachment, user, pc, storedPath, payload, rawJson, readerError, state);
                updateCmd.Parameters.AddWithValue("@ID", existingId.Value);
                await updateCmd.ExecuteNonQueryAsync(ct);
                recordId = existingId.Value;

                await using var deleteCmd = new SqlCommand("DELETE FROM dbo.IA_Compras_DET WHERE ID_CAB = @ID_CAB", cn, (SqlTransaction)tx);
                deleteCmd.Parameters.AddWithValue("@ID_CAB", recordId);
                await deleteCmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                const string insertSql = """
                    INSERT INTO dbo.IA_Compras_CAB
                    (
                        IdComprobanteRecibido,
                        IdAdjuntoFuente,
                        Estado,
                        FechaHora_Inicio,
                        FechaHora_Fin,
                        Usuario_Proceso,
                        Observaciones_Rev,
                        Archivo_RutaOriginal,
                        Archivo_NombreOriginal,
                        Archivo_NombreRenombrado,
                        SolicitarCancelacion,
                        Intentos,
                        Proveedor_Nombre,
                        Proveedor_CUIT,
                        Proveedor_Domicilio,
                        Proveedor_CondIVA,
                        Cuenta_Contable,
                        Match_Metodo,
                        TipoComprobante,
                        Letra,
                        PuntoVenta,
                        Numero,
                        Fecha,
                        Vencimiento,
                        CAE,
                        VtoCAE,
                        Moneda,
                        NetoGravado,
                        NetoNoGravado,
                        Exento,
                        IVA_21,
                        IVA_105,
                        IVA_27,
                        Percepcion_IVA,
                        Percepcion_IIBB,
                        Percepcion_Ganancias,
                        ImpuestosInternos,
                        OtrosImpuestos,
                        Total,
                        Lector_Observaciones,
                        Lector_Error,
                        JsonResultado
                    )
                    VALUES
                    (
                        @IdComprobanteRecibido,
                        @IdAdjuntoFuente,
                        @Estado,
                        GETDATE(),
                        GETDATE(),
                        @Usuario_Proceso,
                        @Observaciones_Rev,
                        @Archivo_RutaOriginal,
                        @Archivo_NombreOriginal,
                        @Archivo_NombreRenombrado,
                        0,
                        1,
                        @Proveedor_Nombre,
                        @Proveedor_CUIT,
                        @Proveedor_Domicilio,
                        @Proveedor_CondIVA,
                        @Cuenta_Contable,
                        @Match_Metodo,
                        @TipoComprobante,
                        @Letra,
                        @PuntoVenta,
                        @Numero,
                        @Fecha,
                        @Vencimiento,
                        @CAE,
                        @VtoCAE,
                        @Moneda,
                        @NetoGravado,
                        @NetoNoGravado,
                        @Exento,
                        @IVA_21,
                        @IVA_105,
                        @IVA_27,
                        @Percepcion_IVA,
                        @Percepcion_IIBB,
                        @Percepcion_Ganancias,
                        @ImpuestosInternos,
                        @OtrosImpuestos,
                        @Total,
                        @Lector_Observaciones,
                        @Lector_Error,
                        @JsonResultado
                    );
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;

                await using var insertCmd = new SqlCommand(insertSql, cn, (SqlTransaction)tx);
                FillCompraCabParameters(insertCmd, detail, sourceAttachment, user, pc, storedPath, payload, rawJson, readerError, state);
                recordId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            }

            if (payload is not null)
            {
                const string insertDetSql = """
                    INSERT INTO dbo.IA_Compras_DET
                    (
                        ID_CAB,
                        NroRenglon,
                        Cantidad,
                        Codigo_Articulo,
                        Descripcion,
                        UD,
                        Importe_Lista,
                        Dto1,
                        Dto2,
                        Importe_Neto,
                        IVA,
                        ImpuestosInternos,
                        Total,
                        AuxNroLote,
                        AuxNroSerie,
                        BlPq,
                        Moneda,
                        TotImpInt
                    )
                    VALUES
                    (
                        @ID_CAB,
                        @NroRenglon,
                        @Cantidad,
                        @Codigo_Articulo,
                        @Descripcion,
                        @UD,
                        @Importe_Lista,
                        @Dto1,
                        @Dto2,
                        @Importe_Neto,
                        @IVA,
                        @ImpuestosInternos,
                        @Total,
                        @AuxNroLote,
                        @AuxNroSerie,
                        @BlPq,
                        @Moneda,
                        @TotImpInt
                    )
                    """;

                foreach (var item in payload.Items)
                {
                    await using var detCmd = new SqlCommand(insertDetSql, cn, (SqlTransaction)tx);
                    detCmd.Parameters.AddWithValue("@ID_CAB", recordId);
                    detCmd.Parameters.AddWithValue("@NroRenglon", item.NroRenglon);
                    detCmd.Parameters.AddWithValue("@Cantidad", DbNullable(item.Cantidad, 20));
                    detCmd.Parameters.AddWithValue("@Codigo_Articulo", DbNullable(item.CodigoArticulo, 50));
                    detCmd.Parameters.AddWithValue("@Descripcion", DbNullable(item.Descripcion, 200));
                    detCmd.Parameters.AddWithValue("@UD", DbNullable(item.Ud, 10));
                    detCmd.Parameters.AddWithValue("@Importe_Lista", DbNullable(item.ImporteLista));
                    detCmd.Parameters.AddWithValue("@Dto1", DbNullable(item.Dto1));
                    detCmd.Parameters.AddWithValue("@Dto2", DbNullable(item.Dto2));
                    detCmd.Parameters.AddWithValue("@Importe_Neto", DbNullable(item.ImporteNeto));
                    detCmd.Parameters.AddWithValue("@IVA", DbNullable(item.Iva));
                    detCmd.Parameters.AddWithValue("@ImpuestosInternos", DbNullable(item.ImpuestosInternos));
                    detCmd.Parameters.AddWithValue("@Total", DbNullable(item.Total));
                    detCmd.Parameters.AddWithValue("@AuxNroLote", DbNullable(item.AuxNroLote, 50));
                    detCmd.Parameters.AddWithValue("@AuxNroSerie", DbNullable(item.AuxNroSerie, 50));
                    detCmd.Parameters.AddWithValue("@BlPq", DbNullable(item.BlPq, 20));
                    detCmd.Parameters.AddWithValue("@Moneda", DbNullable(item.Moneda, 10));
                    detCmd.Parameters.AddWithValue("@TotImpInt", DbNullable(item.TotImpInt));
                    await detCmd.ExecuteNonQueryAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            try
            {
                await tx.RollbackAsync(ct);
            }
            catch
            {
            }

            throw;
        }
    }

    private static void FillCompraCabParameters(
        SqlCommand cmd,
        InterfacesDetalleDto detail,
        InterfacesAdjuntoDto sourceAttachment,
        string user,
        string pc,
        string storedPath,
        InterfacesCompraIaPayload? payload,
        string rawJson,
        string? readerError,
        string state)
    {
        cmd.Parameters.AddWithValue("@IdComprobanteRecibido", detail.IdComprobanteRecibido);
        cmd.Parameters.AddWithValue("@IdAdjuntoFuente", sourceAttachment.IdAdjunto);
        cmd.Parameters.AddWithValue("@Estado", state);
        cmd.Parameters.AddWithValue("@Usuario_Proceso", DbNullable(user, 50));
        cmd.Parameters.AddWithValue("@Observaciones_Rev", DbNullable(BuildCompraQueueNote(state, readerError), 500));
        cmd.Parameters.AddWithValue("@Archivo_RutaOriginal", Truncate(storedPath, 500));
        cmd.Parameters.AddWithValue("@Archivo_NombreOriginal", Truncate(sourceAttachment.NombreOriginal, 260));
        cmd.Parameters.AddWithValue("@Archivo_NombreRenombrado", DbNullable(sourceAttachment.NombreGuardado, 260));
        cmd.Parameters.AddWithValue("@Proveedor_Nombre", DbNullable(payload?.ProveedorNombre, 150));
        cmd.Parameters.AddWithValue("@Proveedor_CUIT", DbNullable(payload?.ProveedorCuit, 13));
        cmd.Parameters.AddWithValue("@Proveedor_Domicilio", DbNullable(payload?.ProveedorDomicilio, 150));
        cmd.Parameters.AddWithValue("@Proveedor_CondIVA", DbNullable(payload?.ProveedorCondIva, 30));
        cmd.Parameters.AddWithValue("@Cuenta_Contable", DbNullable(payload?.CuentaContable, 15));
        cmd.Parameters.AddWithValue("@Match_Metodo", DbNullable(payload?.MatchMetodo, 50));
        cmd.Parameters.AddWithValue("@TipoComprobante", DbNullable(payload?.TipoComprobante, 50));
        cmd.Parameters.AddWithValue("@Letra", DbNullable(payload?.Letra, 1));
        cmd.Parameters.AddWithValue("@PuntoVenta", DbNullable(payload?.PuntoVenta, 4));
        cmd.Parameters.AddWithValue("@Numero", DbNullable(payload?.Numero, 8));
        cmd.Parameters.AddWithValue("@Fecha", DbNullable(payload?.Fecha));
        cmd.Parameters.AddWithValue("@Vencimiento", DbNullable(payload?.Vencimiento));
        cmd.Parameters.AddWithValue("@CAE", DbNullable(payload?.Cae, 14));
        cmd.Parameters.AddWithValue("@VtoCAE", DbNullable(payload?.VtoCae));
        cmd.Parameters.AddWithValue("@Moneda", DbNullable(payload?.Moneda, 10));
        cmd.Parameters.AddWithValue("@NetoGravado", DbNullable(payload?.NetoGravado));
        cmd.Parameters.AddWithValue("@NetoNoGravado", DbNullable(payload?.NetoNoGravado));
        cmd.Parameters.AddWithValue("@Exento", DbNullable(payload?.Exento));
        cmd.Parameters.AddWithValue("@IVA_21", DbNullable(payload?.Iva21));
        cmd.Parameters.AddWithValue("@IVA_105", DbNullable(payload?.Iva105));
        cmd.Parameters.AddWithValue("@IVA_27", DbNullable(payload?.Iva27));
        cmd.Parameters.AddWithValue("@Percepcion_IVA", DbNullable(payload?.PercepcionIva));
        cmd.Parameters.AddWithValue("@Percepcion_IIBB", DbNullable(payload?.PercepcionIibb));
        cmd.Parameters.AddWithValue("@Percepcion_Ganancias", DbNullable(payload?.PercepcionGanancias));
        cmd.Parameters.AddWithValue("@ImpuestosInternos", DbNullable(payload?.ImpuestosInternos));
        cmd.Parameters.AddWithValue("@OtrosImpuestos", DbNullable(payload?.OtrosImpuestos));
        cmd.Parameters.AddWithValue("@Total", DbNullable(payload?.Total));
        cmd.Parameters.AddWithValue("@Lector_Observaciones", DbNullable(payload?.LectorObservaciones, 1000));
        cmd.Parameters.AddWithValue("@Lector_Error", DbNullable(readerError ?? payload?.LectorError, 1000));
        cmd.Parameters.AddWithValue("@JsonResultado", string.IsNullOrWhiteSpace(rawJson) ? DBNull.Value : rawJson);
    }

    private static string BuildCompraQueueNote(string state, string? readerError)
        => !string.IsNullOrWhiteSpace(readerError)
            ? readerError.Trim()
            : state switch
            {
                "PROCESADO" => "Procesamiento finalizado correctamente.",
                "SIN_PROVEEDOR" => "Procesamiento finalizado sin match de proveedor.",
                "ERROR_LECTURA" => "El lector automático devolvió un error.",
                "CANCELADO" => "Procesamiento cancelado.",
                _ => string.Empty
            };

    private static bool IsFacturaReaderSupportedExtension(string extension, string originalName)
    {
        var normalized = NormalizeExtension(string.IsNullOrWhiteSpace(extension) ? Path.GetExtension(originalName) : extension);
        return normalized is ".pdf" or ".jpg" or ".jpeg" or ".png" or ".webp";
    }

    private static string ReadJsonString(JsonObject? obj, string key)
        => obj?[key]?.GetValue<string>()?.Trim() ?? string.Empty;

    private static decimal? ReadJsonDecimal(JsonObject? obj, string key)
        => ParseFlexibleDecimal(ReadJsonString(obj, key));

    private static double? ReadJsonDouble(JsonObject? obj, string key)
    {
        var value = ReadJsonString(obj, key);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace("%", string.Empty).Trim().Replace(",", ".");
        return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ParseFlexibleDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().Replace(" ", string.Empty);
        if (normalized.Contains(',') && normalized.Contains('.'))
        {
            normalized = normalized.Replace(".", string.Empty).Replace(",", ".");
        }
        else if (normalized.Contains(','))
        {
            normalized = normalized.Replace(",", ".");
        }

        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ParseFlexibleDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;

        return DateTime.TryParse(normalized, CultureInfo.GetCultureInfo("es-AR"), DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static string ReadValue(IReadOnlyDictionary<string, string> values, string key, string fallback = "")
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static string NormalizeTextKey(string value)
    {
        var chars = value
            .Trim()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private static string PickProviderColumn(Dictionary<string, string> normalizedColumns, IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var key = NormalizeTextKey(candidate);
            if (normalizedColumns.TryGetValue(key, out var exact))
                return exact;
        }

        foreach (var pair in normalizedColumns)
        {
            foreach (var candidate in candidates)
            {
                var key = NormalizeTextKey(candidate);
                if (pair.Key.StartsWith(key, StringComparison.OrdinalIgnoreCase) || pair.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }
        }

        return string.Empty;
    }

    private static string OnlyDigits(string value)
        => new(value.Where(char.IsDigit).ToArray());

    private static bool LooksLikeCuit(string value)
        => OnlyDigits(value).Length == 11;

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static decimal? FirstNonNull(params decimal?[] values)
        => values.FirstOrDefault(static x => x.HasValue);

    private static decimal? GetNullableDecimal(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? null : Convert.ToDecimal(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static double? GetNullableDouble(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? null : Convert.ToDouble(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static object DbNullable(decimal? value)
        => value.HasValue ? value.Value : DBNull.Value;

    private static object DbNullable(double? value)
        => value.HasValue ? value.Value : DBNull.Value;

    private static object DbNullable(DateTime? value)
        => value.HasValue ? value.Value : DBNull.Value;

#pragma warning disable SYSLIB0014
    private static async Task DownloadFtpFileAsync(InterfacesUploadSettingsDto settings, string remoteUrl, string outputPath, CancellationToken ct)
    {
        var request = (FtpWebRequest)WebRequest.Create(remoteUrl);
        request.Method = WebRequestMethods.Ftp.DownloadFile;
        request.Credentials = new NetworkCredential(settings.FtpUsuario, settings.FtpClave);
        request.UseBinary = true;
        request.UsePassive = settings.FtpModoPasivo;
        request.KeepAlive = false;

        using var response = (FtpWebResponse)await request.GetResponseAsync();
        await using var responseStream = response.GetResponseStream()
            ?? throw new InvalidOperationException("No se pudo abrir el adjunto remoto para ejecutar la detección.");
        await using var output = File.Create(outputPath);
        await responseStream.CopyToAsync(output, ct);
    }
#pragma warning restore SYSLIB0014

    private static object DbNullable(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : Truncate(value.Trim(), maxLength);

    private static string AddIdParameters(SqlCommand cmd, string prefix, IReadOnlyList<long> ids)
    {
        var parameterNames = new List<string>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var parameterName = $"{prefix}{i}";
            cmd.Parameters.AddWithValue(parameterName, ids[i]);
            parameterNames.Add(parameterName);
        }

        return string.Join(", ", parameterNames);
    }

    private static async Task DeleteByIdsAsync(
        SqlConnection cn,
        SqlTransaction tx,
        string tableName,
        string columnName,
        IReadOnlyList<long> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return;

        using var cmd = new SqlCommand(string.Empty, cn, tx);
        var inClause = AddIdParameters(cmd, "@Id", ids);
        cmd.CommandText = $"DELETE FROM {tableName} WHERE {columnName} IN ({inClause})";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string SerializeData(object? data)
    {
        if (data is null)
            return string.Empty;

        return JsonSerializer.Serialize(data);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= maxLength ? value.Trim() : value.Trim()[..maxLength];
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static int GetInt(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt32(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static long GetLong(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt64(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index), CultureInfo.InvariantCulture);

    private sealed class InterfacesDeleteSnapshotItem
    {
        public long IdComprobanteRecibido { get; init; }
        public string RutaBase { get; init; } = string.Empty;
        public string RutaRelativa { get; init; } = string.Empty;
    }

    private sealed class InterfacesCompraIaSettings
    {
        public string PythonExe { get; init; } = string.Empty;
        public string ScriptPath { get; init; } = string.Empty;
        public string WorkingDirectory { get; init; } = string.Empty;
    }

    private sealed class InterfacesCompraIaPayload
    {
        public string ProveedorNombre { get; set; } = string.Empty;
        public string ProveedorCuit { get; set; } = string.Empty;
        public string ProveedorDomicilio { get; set; } = string.Empty;
        public string ProveedorCondIva { get; set; } = string.Empty;
        public string CuentaContable { get; set; } = string.Empty;
        public string MatchMetodo { get; set; } = string.Empty;
        public string TipoComprobante { get; set; } = string.Empty;
        public string Letra { get; set; } = string.Empty;
        public string PuntoVenta { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public DateTime? Fecha { get; set; }
        public DateTime? Vencimiento { get; set; }
        public string Cae { get; set; } = string.Empty;
        public DateTime? VtoCae { get; set; }
        public string Moneda { get; set; } = string.Empty;
        public decimal? NetoGravado { get; set; }
        public decimal? NetoNoGravado { get; set; }
        public decimal? Exento { get; set; }
        public decimal? Iva21 { get; set; }
        public decimal? Iva105 { get; set; }
        public decimal? Iva27 { get; set; }
        public decimal? PercepcionIva { get; set; }
        public decimal? PercepcionIibb { get; set; }
        public decimal? PercepcionGanancias { get; set; }
        public decimal? ImpuestosInternos { get; set; }
        public decimal? OtrosImpuestos { get; set; }
        public decimal? Total { get; set; }
        public string LectorObservaciones { get; set; } = string.Empty;
        public string LectorError { get; set; } = string.Empty;
        public List<InterfacesCompraIaPayloadItem> Items { get; set; } = [];
    }

    private sealed class InterfacesCompraIaPayloadItem
    {
        public int NroRenglon { get; set; }
        public string Cantidad { get; set; } = string.Empty;
        public string CodigoArticulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Ud { get; set; } = string.Empty;
        public decimal? ImporteLista { get; set; }
        public double? Dto1 { get; set; }
        public double? Dto2 { get; set; }
        public decimal? ImporteNeto { get; set; }
        public double? Iva { get; set; }
        public decimal? ImpuestosInternos { get; set; }
        public decimal? Total { get; set; }
        public string AuxNroLote { get; set; } = string.Empty;
        public string AuxNroSerie { get; set; } = string.Empty;
        public string BlPq { get; set; } = string.Empty;
        public string Moneda { get; set; } = string.Empty;
        public decimal? TotImpInt { get; set; }
    }

    private sealed class ProveedorColumns
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Cuit { get; init; } = string.Empty;
        public string Address { get; init; } = string.Empty;
    }

    private sealed class ProveedorMatchResult
    {
        public string Codigo { get; init; } = string.Empty;
        public string Nombre { get; init; } = string.Empty;
        public string Cuit { get; init; } = string.Empty;
        public string Domicilio { get; init; } = string.Empty;
        public string MatchMetodo { get; init; } = string.Empty;
    }
}

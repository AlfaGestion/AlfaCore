using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class CotizacionesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IArticuloPrecioResolverService priceResolver,
    ICrmService crmService,
    ICrmCotizacionService crmCotizacionService,
    IServiceProvider serviceProvider,
    IUsuariosService usuariosService,
    IAppUserSessionService appUserSession) : ICotizacionesService
{
    private const string ModuleName = "Cotizaciones";
    private const string DefaultTc = "COT";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<CotizacionListItemDto>> GetListAsync(CotizacionListFiltersDto filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetList", async token =>
        {
            var pageNumber = filters.PageNumber < 1 ? 1 : filters.PageNumber;
            var pageSize = Math.Clamp(filters.PageSize <= 0 ? 50 : filters.PageSize, 1, 200);
            var texto = string.IsNullOrWhiteSpace(filters.Texto) ? null : $"%{filters.Texto.Trim()}%";
            var estado = string.IsNullOrWhiteSpace(filters.Estado) ? null : filters.Estado.Trim().ToUpperInvariant();
            var cliente = string.IsNullOrWhiteSpace(filters.CodigoCliente) ? null : filters.CodigoCliente.Trim();
            var soloVencidas = filters.SoloVencidas == true ? (bool?)true : null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var rows = (await cn.QueryAsync<CotizacionListRow>(new CommandDefinition("""
                ;WITH Filtered AS
                (
                    SELECT
                        c.IdCotizacion, c.Numero, ISNULL(c.TC, 'COT') AS TC, c.Estado,
                        c.IdOportunidad, ISNULL(c.CodigoCliente, '') AS CodigoCliente,
                        v.IdVersion, v.NumeroVersion, v.Fecha, v.FechaVencimiento,
                        ISNULL(v.EmpresaProspecto, '') AS EmpresaProspecto,
                        ISNULL(v.ContactoNombre, '') AS ContactoNombre,
                        ISNULL(v.CodigoMoneda, '') AS CodigoMoneda,
                        v.Total,
                        COUNT(*) OVER() AS TotalRows
                    FROM dbo.COT_COTIZACION c
                    INNER JOIN dbo.COT_VERSION v ON v.IdVersion = c.IdVersionActual
                    WHERE ISNULL(c.Baja, 0) = 0
                      AND (@Estado IS NULL OR c.Estado = @Estado)
                      AND (@Cliente IS NULL OR c.CodigoCliente = @Cliente)
                      AND (@SoloCrm IS NULL OR (@SoloCrm = 1 AND c.IdOportunidad IS NOT NULL) OR (@SoloCrm = 0 AND c.IdOportunidad IS NULL))
                      AND (@FechaDesde IS NULL OR v.Fecha >= @FechaDesde)
                      AND (@FechaHasta IS NULL OR v.Fecha <= @FechaHasta)
                      AND (@Texto IS NULL
                           OR v.EmpresaProspecto LIKE @Texto
                           OR v.ContactoNombre LIKE @Texto
                           OR c.CodigoCliente LIKE @Texto
                           OR CAST(c.Numero AS nvarchar(20)) LIKE @Texto)
                      AND (@SoloVencidas IS NULL
                           OR (c.Estado NOT IN ('ACEPTADA', 'RECHAZADA', 'ANULADA') AND v.FechaVencimiento < CAST(GETDATE() AS date)))
                )
                SELECT * FROM Filtered
                ORDER BY Fecha DESC, IdCotizacion DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """, new
            {
                Estado = estado,
                Cliente = cliente,
                SoloCrm = filters.SoloOrigenCrm,
                filters.FechaDesde,
                filters.FechaHasta,
                Texto = texto,
                SoloVencidas = soloVencidas,
                Offset = (pageNumber - 1) * pageSize,
                PageSize = pageSize
            }, cancellationToken: token))).AsList();

            var items = rows.Select(r => new CotizacionListItemDto
            {
                IdCotizacion = r.IdCotizacion,
                IdVersion = r.IdVersion,
                Numero = r.Numero,
                TC = r.TC,
                NumeroVersion = r.NumeroVersion,
                Fecha = r.Fecha,
                FechaVencimiento = r.FechaVencimiento,
                EmpresaProspecto = r.EmpresaProspecto,
                CodigoCliente = r.CodigoCliente,
                ContactoNombre = r.ContactoNombre,
                CodigoMoneda = r.CodigoMoneda,
                Total = r.Total,
                Estado = r.Estado,
                IdOportunidad = r.IdOportunidad
            }).ToList();

            return new PagedResult<CotizacionListItemDto>
            {
                Items = items,
                Total = rows.Count > 0 ? rows[0].TotalRows : 0,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar las cotizaciones.", ct);

    public Task<IReadOnlyList<CotizacionListItemDto>> GetByOportunidadAsync(long idOportunidad, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetByOportunidad", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = (await cn.QueryAsync<CotizacionListRow>(new CommandDefinition("""
                SELECT
                    c.IdCotizacion, c.Numero, ISNULL(c.TC, 'COT') AS TC, c.Estado,
                    c.IdOportunidad, ISNULL(c.CodigoCliente, '') AS CodigoCliente,
                    v.IdVersion, v.NumeroVersion, v.Fecha, v.FechaVencimiento,
                    ISNULL(v.EmpresaProspecto, '') AS EmpresaProspecto,
                    ISNULL(v.ContactoNombre, '') AS ContactoNombre,
                    ISNULL(v.CodigoMoneda, '') AS CodigoMoneda,
                    v.Total, 0 AS TotalRows
                FROM dbo.COT_COTIZACION c
                INNER JOIN dbo.COT_VERSION v ON v.IdVersion = c.IdVersionActual
                WHERE c.IdOportunidad = @IdOportunidad AND ISNULL(c.Baja, 0) = 0
                ORDER BY c.FechaHoraAlta DESC, c.IdCotizacion DESC;
                """, new { IdOportunidad = idOportunidad }, cancellationToken: token))).AsList();

            return (IReadOnlyList<CotizacionListItemDto>)rows.Select(r => new CotizacionListItemDto
            {
                IdCotizacion = r.IdCotizacion,
                IdVersion = r.IdVersion,
                Numero = r.Numero,
                TC = r.TC,
                NumeroVersion = r.NumeroVersion,
                Fecha = r.Fecha,
                FechaVencimiento = r.FechaVencimiento,
                EmpresaProspecto = r.EmpresaProspecto,
                CodigoCliente = r.CodigoCliente,
                ContactoNombre = r.ContactoNombre,
                CodigoMoneda = r.CodigoMoneda,
                Total = r.Total,
                Estado = r.Estado,
                IdOportunidad = r.IdOportunidad
            }).ToList();
        }, "No se pudieron cargar las cotizaciones de la oportunidad.", ct);

    public Task<CotizacionVersionDetailDto?> GetVersionDetailAsync(long idVersion, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetVersionDetail", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await LoadVersionDetailAsync(cn, idVersion, null, token);
        }, "No se pudo cargar la cotización.", ct);

    public Task<long> CreateAsync(CotizacionCreateRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Create", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var empresaProspecto = (request.EmpresaProspecto ?? string.Empty).Trim();
                var codigoCliente = string.IsNullOrWhiteSpace(request.CodigoCliente) ? null : request.CodigoCliente.Trim();
                if (codigoCliente is not null && empresaProspecto.Length == 0)
                {
                    var pricing = await priceResolver.ResolveContextAsync(cn, codigoCliente, token, tx);
                    empresaProspecto = pricing.ClienteNombre;
                }

                var numero = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT ISNULL(MAX(Numero), 0) + 1 FROM dbo.COT_COTIZACION WITH (UPDLOCK, HOLDLOCK);",
                    transaction: tx, cancellationToken: token));

                var idCotizacion = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                    INSERT INTO dbo.COT_COTIZACION (Numero, TC, IdOportunidad, CodigoCliente, Estado, UsuarioAlta, FechaHoraAlta)
                    OUTPUT INSERTED.IdCotizacion
                    VALUES (@Numero, @TC, @IdOportunidad, @CodigoCliente, @Estado, @Usuario, GETDATE());
                    """, new
                {
                    Numero = numero,
                    TC = DefaultTc,
                    request.IdOportunidad,
                    CodigoCliente = codigoCliente,
                    Estado = CotizacionEstados.Borrador,
                    Usuario = NormalizeUser(request.UsuarioAccion)
                }, tx, cancellationToken: token));

                var idVersion = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                    INSERT INTO dbo.COT_VERSION
                    (IdCotizacion, NumeroVersion, Fecha, EmpresaProspecto, ContactoNombre, ContactoEmail, ContactoTelefono,
                     DocumentoFiscal, CodigoMoneda, EstadoVersion, UsuarioAlta, FechaHoraAlta)
                    OUTPUT INSERTED.IdVersion
                    VALUES (@IdCotizacion, 1, CAST(GETDATE() AS date), @EmpresaProspecto, @ContactoNombre, @ContactoEmail, @ContactoTelefono,
                            @DocumentoFiscal, @CodigoMoneda, @Estado, @Usuario, GETDATE());
                    """, new
                {
                    IdCotizacion = idCotizacion,
                    EmpresaProspecto = NullIfEmpty(empresaProspecto),
                    ContactoNombre = NullIfEmpty(request.ContactoNombre),
                    ContactoEmail = NullIfEmpty(request.ContactoEmail),
                    ContactoTelefono = NullIfEmpty(request.ContactoTelefono),
                    DocumentoFiscal = NullIfEmpty(request.DocumentoFiscal),
                    CodigoMoneda = NullIfEmpty(request.CodigoMoneda),
                    Estado = CotizacionEstados.Borrador,
                    Usuario = NormalizeUser(request.UsuarioAccion)
                }, tx, cancellationToken: token));

                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.COT_COTIZACION SET IdVersionActual = @IdVersion WHERE IdCotizacion = @IdCotizacion;",
                    new { IdVersion = idVersion, IdCotizacion = idCotizacion }, tx, cancellationToken: token));

                await tx.CommitAsync(token);
                return idVersion;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo crear la cotización.", ct);

    public Task<long> CreateNewVersionAsync(long idCotizacion, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("CreateNewVersion", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var actual = await cn.QueryFirstOrDefaultAsync<long?>(new CommandDefinition(
                    "SELECT IdVersionActual FROM dbo.COT_COTIZACION WHERE IdCotizacion = @Id AND ISNULL(Baja, 0) = 0;",
                    new { Id = idCotizacion }, tx, cancellationToken: token));
                if (actual is null)
                    throw new InvalidOperationException("La cotización indicada no existe.");

                var detail = await LoadVersionDetailAsync(cn, actual.Value, tx, token)
                             ?? throw new InvalidOperationException("No se pudo cargar la versión actual de la cotización.");

                var nuevaVersion = detail.NumeroVersion + 1;
                var idVersionNueva = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                    INSERT INTO dbo.COT_VERSION
                    (IdCotizacion, NumeroVersion, Fecha, FechaVencimiento, EmpresaProspecto, ContactoNombre, ContactoEmail,
                     ContactoTelefono, DocumentoFiscal, CodigoMoneda, Observaciones, CuerpoPropuesta,
                     DescuentoGeneralPorcentaje, Subtotal, TotalDescuento, Total, IncluyePortada, EstadoVersion, UsuarioAlta, FechaHoraAlta)
                    OUTPUT INSERTED.IdVersion
                    VALUES (@IdCotizacion, @NumeroVersion, CAST(GETDATE() AS date), @FechaVencimiento, @EmpresaProspecto, @ContactoNombre,
                            @ContactoEmail, @ContactoTelefono, @DocumentoFiscal, @CodigoMoneda, @Observaciones, @CuerpoPropuesta,
                            @DescuentoGeneralPorcentaje, @Subtotal, @TotalDescuento, @Total, @IncluyePortada, @Estado, @Usuario, GETDATE());
                    """, new
                {
                    IdCotizacion = idCotizacion,
                    NumeroVersion = nuevaVersion,
                    detail.FechaVencimiento,
                    EmpresaProspecto = NullIfEmpty(detail.EmpresaProspecto),
                    ContactoNombre = NullIfEmpty(detail.ContactoNombre),
                    ContactoEmail = NullIfEmpty(detail.ContactoEmail),
                    ContactoTelefono = NullIfEmpty(detail.ContactoTelefono),
                    DocumentoFiscal = NullIfEmpty(detail.DocumentoFiscal),
                    CodigoMoneda = NullIfEmpty(detail.CodigoMoneda),
                    Observaciones = NullIfEmpty(detail.Observaciones),
                    CuerpoPropuesta = NullIfEmpty(detail.CuerpoPropuesta),
                    detail.DescuentoGeneralPorcentaje,
                    detail.Subtotal,
                    detail.TotalDescuento,
                    detail.Total,
                    detail.IncluyePortada,
                    Estado = CotizacionEstados.Borrador,
                    Usuario = NormalizeUser(usuarioAccion)
                }, tx, cancellationToken: token));

                await CopySeccionesYLineasAsync(cn, tx, detail, idVersionNueva, token);

                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.COT_COTIZACION
                    SET IdVersionActual = @IdVersion, Estado = @Estado, FechaHoraModificacion = GETDATE()
                    WHERE IdCotizacion = @Id;
                    """, new { IdVersion = idVersionNueva, Estado = CotizacionEstados.Borrador, Id = idCotizacion }, tx, cancellationToken: token));

                await tx.CommitAsync(token);
                return idVersionNueva;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo crear una nueva versión de la cotización.", ct);

    private static async Task CopySeccionesYLineasAsync(SqlConnection cn, SqlTransaction tx, CotizacionVersionDetailDto origen, long idVersionNueva, CancellationToken ct)
    {
        var mapaSecciones = new Dictionary<long, long>();
        foreach (var s in origen.Secciones.OrderBy(x => x.Orden))
        {
            var nuevoId = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT INTO dbo.COT_SECCION (IdVersion, Orden, Titulo, Descripcion, MostrarSubtotal, Activo)
                OUTPUT INSERTED.IdSeccion
                VALUES (@IdVersion, @Orden, @Titulo, @Descripcion, @MostrarSubtotal, 1);
                """, new { IdVersion = idVersionNueva, s.Orden, s.Titulo, s.Descripcion, s.MostrarSubtotal }, tx, cancellationToken: ct));
            mapaSecciones[s.IdSeccion] = nuevoId;
        }

        foreach (var l in origen.Lineas.OrderBy(x => x.Orden))
        {
            long? idSeccionNueva = l.IdSeccion.HasValue && mapaSecciones.TryGetValue(l.IdSeccion.Value, out var mapped) ? mapped : null;
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.COT_DET
                (IdVersion, IdSeccion, Orden, Tipo, CodigoRef, Descripcion, Cantidad, PrecioBase, PorcentajeDescuento,
                 PrecioUnitario, TasaIva, Subtotal, ImpactaTotal, OrigenPrecio)
                VALUES (@IdVersion, @IdSeccion, @Orden, @Tipo, @CodigoRef, @Descripcion, @Cantidad, @PrecioBase, @PorcentajeDescuento,
                        @PrecioUnitario, @TasaIva, @Subtotal, @ImpactaTotal, @OrigenPrecio);
                """, new
            {
                IdVersion = idVersionNueva,
                IdSeccion = idSeccionNueva,
                l.Orden,
                l.Tipo,
                l.CodigoRef,
                l.Descripcion,
                l.Cantidad,
                l.PrecioBase,
                l.PorcentajeDescuento,
                l.PrecioUnitario,
                l.TasaIva,
                l.Subtotal,
                l.ImpactaTotal,
                l.OrigenPrecio
            }, tx, cancellationToken: ct));
        }
    }

    public Task SaveVersionAsync(CotizacionSaveVersionRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveVersion", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdVersion <= 0)
                throw new InvalidOperationException("Versión de cotización inválida.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var estadoActual = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                    "SELECT EstadoVersion FROM dbo.COT_VERSION WHERE IdVersion = @Id;",
                    new { Id = request.IdVersion }, tx, cancellationToken: token));
                if (estadoActual is null)
                    throw new InvalidOperationException("La versión indicada no existe.");
                if (!string.Equals(estadoActual, CotizacionEstados.Borrador, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Esta versión ya fue enviada y quedó de solo lectura. Creá una nueva versión para modificarla.");

                var permiteDescuentoLinea = await PermiteDescuentoPorLineaInternalAsync(cn, token, tx);

                decimal subtotal = 0m;
                var computedLineas = new List<CotizacionLineaDto>();
                foreach (var l in request.Lineas.Where(x => !string.IsNullOrWhiteSpace(x.Descripcion) && x.Cantidad != 0))
                {
                    var pctDescuento = permiteDescuentoLinea ? decimal.Round(l.PorcentajeDescuento, 4) : 0m;
                    var precioUnitario = decimal.Round(l.PrecioBase * (1m - pctDescuento / 100m), 4);
                    var lineaSubtotal = decimal.Round(precioUnitario * l.Cantidad, 2);
                    if (l.ImpactaTotal)
                        subtotal += lineaSubtotal;
                    computedLineas.Add(new CotizacionLineaDto
                    {
                        IdSeccion = l.IdSeccion,
                        Orden = computedLineas.Count,
                        Tipo = l.Tipo,
                        CodigoRef = NullIfEmpty(l.CodigoRef),
                        Descripcion = l.Descripcion.Trim(),
                        Cantidad = l.Cantidad,
                        PrecioBase = decimal.Round(l.PrecioBase, 4),
                        PorcentajeDescuento = pctDescuento,
                        PrecioUnitario = precioUnitario,
                        TasaIva = decimal.Round(l.TasaIva, 4),
                        Subtotal = lineaSubtotal,
                        ImpactaTotal = l.ImpactaTotal,
                        OrigenPrecio = l.OrigenPrecio
                    });
                }

                var descuentoGeneral = decimal.Round(request.DescuentoGeneralPorcentaje, 4);
                var totalDescuento = decimal.Round(subtotal * descuentoGeneral / 100m, 2);
                var total = subtotal - totalDescuento;

                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.COT_VERSION
                    SET EmpresaProspecto = @EmpresaProspecto, ContactoNombre = @ContactoNombre, ContactoEmail = @ContactoEmail,
                        ContactoTelefono = @ContactoTelefono, DocumentoFiscal = @DocumentoFiscal, CodigoMoneda = @CodigoMoneda,
                        FechaVencimiento = @FechaVencimiento, Observaciones = @Observaciones, CuerpoPropuesta = @CuerpoPropuesta,
                        DescuentoGeneralPorcentaje = @DescuentoGeneral, Subtotal = @Subtotal, TotalDescuento = @TotalDescuento,
                        Total = @Total, IncluyePortada = @IncluyePortada, FechaHoraModificacion = GETDATE()
                    WHERE IdVersion = @IdVersion;
                    """, new
                {
                    request.IdVersion,
                    EmpresaProspecto = NullIfEmpty(request.EmpresaProspecto),
                    ContactoNombre = NullIfEmpty(request.ContactoNombre),
                    ContactoEmail = NullIfEmpty(request.ContactoEmail),
                    ContactoTelefono = NullIfEmpty(request.ContactoTelefono),
                    DocumentoFiscal = NullIfEmpty(request.DocumentoFiscal),
                    CodigoMoneda = NullIfEmpty(request.CodigoMoneda),
                    request.FechaVencimiento,
                    Observaciones = NullIfEmpty(request.Observaciones),
                    CuerpoPropuesta = NullIfEmpty(request.CuerpoPropuesta),
                    DescuentoGeneral = descuentoGeneral,
                    Subtotal = subtotal,
                    TotalDescuento = totalDescuento,
                    Total = total,
                    request.IncluyePortada
                }, tx, cancellationToken: token));

                await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.COT_DET WHERE IdVersion = @Id; DELETE FROM dbo.COT_SECCION WHERE IdVersion = @Id;",
                    new { Id = request.IdVersion }, tx, cancellationToken: token));

                var mapaSecciones = new Dictionary<long, long>();
                var ordenSeccion = 0;
                foreach (var s in request.Secciones)
                {
                    var nuevoId = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                        INSERT INTO dbo.COT_SECCION (IdVersion, Orden, Titulo, Descripcion, MostrarSubtotal, Activo)
                        OUTPUT INSERTED.IdSeccion
                        VALUES (@IdVersion, @Orden, @Titulo, @Descripcion, @MostrarSubtotal, 1);
                        """, new { IdVersion = request.IdVersion, Orden = ordenSeccion++, s.Titulo, s.Descripcion, s.MostrarSubtotal }, tx, cancellationToken: token));
                    mapaSecciones[s.IdSeccion] = nuevoId;
                }

                foreach (var l in computedLineas)
                {
                    long? idSeccionNueva = l.IdSeccion.HasValue && mapaSecciones.TryGetValue(l.IdSeccion.Value, out var mapped) ? mapped : null;
                    await cn.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO dbo.COT_DET
                        (IdVersion, IdSeccion, Orden, Tipo, CodigoRef, Descripcion, Cantidad, PrecioBase, PorcentajeDescuento,
                         PrecioUnitario, TasaIva, Subtotal, ImpactaTotal, OrigenPrecio)
                        VALUES (@IdVersion, @IdSeccion, @Orden, @Tipo, @CodigoRef, @Descripcion, @Cantidad, @PrecioBase, @PorcentajeDescuento,
                                @PrecioUnitario, @TasaIva, @Subtotal, @ImpactaTotal, @OrigenPrecio);
                        """, new
                    {
                        IdVersion = request.IdVersion,
                        IdSeccion = idSeccionNueva,
                        l.Orden,
                        l.Tipo,
                        l.CodigoRef,
                        l.Descripcion,
                        l.Cantidad,
                        l.PrecioBase,
                        l.PorcentajeDescuento,
                        l.PrecioUnitario,
                        l.TasaIva,
                        l.Subtotal,
                        l.ImpactaTotal,
                        l.OrigenPrecio
                    }, tx, cancellationToken: token));
                }

                await tx.CommitAsync(token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar la cotización.", ct);

    public Task MarkEnviadaAsync(long idVersion, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("MarkEnviada", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var info = await cn.QueryFirstOrDefaultAsync<(long IdCotizacion, string EstadoVersion)>(new CommandDefinition(
                "SELECT IdCotizacion, EstadoVersion FROM dbo.COT_VERSION WHERE IdVersion = @Id;",
                new { Id = idVersion }, cancellationToken: token));
            if (info.IdCotizacion <= 0)
                throw new InvalidOperationException("La versión indicada no existe.");
            if (!string.Equals(info.EstadoVersion, CotizacionEstados.Borrador, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Esta versión ya fue enviada.");

            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.COT_VERSION SET EstadoVersion = @Estado, FechaHoraEnvio = GETDATE(), FechaHoraModificacion = GETDATE() WHERE IdVersion = @Id;",
                new { Id = idVersion, Estado = CotizacionEstados.Enviada }, cancellationToken: token));
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.COT_COTIZACION SET Estado = @Estado, FechaHoraModificacion = GETDATE() WHERE IdCotizacion = @Id;",
                new { Id = info.IdCotizacion, Estado = CotizacionEstados.Enviada }, cancellationToken: token));
        }, "No se pudo marcar la cotización como enviada.", ct);

    public Task<bool> MarkAceptadaAsync(long idVersion, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("MarkAceptada", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var info = await cn.QueryFirstOrDefaultAsync<(long IdCotizacion, long? IdOportunidad)>(new CommandDefinition(
                "SELECT IdCotizacion, IdOportunidad FROM dbo.COT_VERSION v INNER JOIN dbo.COT_COTIZACION c ON c.IdCotizacion = v.IdCotizacion WHERE v.IdVersion = @Id;",
                new { Id = idVersion }, cancellationToken: token));
            if (info.IdCotizacion <= 0)
                throw new InvalidOperationException("La versión indicada no existe.");

            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.COT_VERSION SET EstadoVersion = @Estado, FechaHoraModificacion = GETDATE() WHERE IdVersion = @Id;",
                new { Id = idVersion, Estado = CotizacionEstados.Aceptada }, cancellationToken: token));
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.COT_COTIZACION SET Estado = @Estado, FechaHoraModificacion = GETDATE() WHERE IdCotizacion = @Id;",
                new { Id = info.IdCotizacion, Estado = CotizacionEstados.Aceptada }, cancellationToken: token));

            if (info.IdOportunidad is not { } idOportunidad)
                return false;

            var etapasGanadas = (await cn.QueryAsync<int>(new CommandDefinition(
                "SELECT IdEtapa FROM dbo.CRM_ETAPAS WHERE ISNULL(EsGanada, 0) = 1 AND ISNULL(Activa, 1) = 1;",
                cancellationToken: token))).ToList();
            if (etapasGanadas.Count != 1)
                return false;

            await crmService.QuickUpdateAsync(new CrmQuickUpdateRequest
            {
                IdOportunidad = idOportunidad,
                IdEtapa = etapasGanadas[0],
                UsuarioAccion = usuarioAccion
            }, token);
            return true;
        }, "No se pudo marcar la cotización como aceptada.", ct);

    public Task MarkRechazadaAsync(long idVersion, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("MarkRechazada", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var idCotizacion = await cn.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT IdCotizacion FROM dbo.COT_VERSION WHERE IdVersion = @Id;", new { Id = idVersion }, cancellationToken: token));
            if (idCotizacion is null)
                throw new InvalidOperationException("La versión indicada no existe.");

            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.COT_VERSION SET EstadoVersion = @Estado, FechaHoraModificacion = GETDATE() WHERE IdVersion = @Id;",
                new { Id = idVersion, Estado = CotizacionEstados.Rechazada }, cancellationToken: token));
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.COT_COTIZACION SET Estado = @Estado, FechaHoraModificacion = GETDATE() WHERE IdCotizacion = @Id;",
                new { Id = idCotizacion, Estado = CotizacionEstados.Rechazada }, cancellationToken: token));
        }, "No se pudo marcar la cotización como rechazada.", ct);

    public Task AnularAsync(long idCotizacion, string? usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync("Anular", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.COT_COTIZACION SET Estado = @Estado, FechaHoraModificacion = GETDATE() WHERE IdCotizacion = @Id AND ISNULL(Baja, 0) = 0;",
                new { Id = idCotizacion, Estado = CotizacionEstados.Anulada }, cancellationToken: token));
        }, "No se pudo anular la cotización.", ct);

    public Task<IReadOnlyList<CrmCotizacionArticuloDto>> SearchArticulosAsync(string? clienteCodigo, string texto, int take = 25, CancellationToken ct = default)
        => ExecuteLoggedAsync("SearchArticulos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var pricing = await priceResolver.ResolveContextAsync(cn, clienteCodigo, token);
            return await priceResolver.SearchArticulosAsync(cn, pricing, texto, take, token);
        }, "No se pudieron cargar los artículos.", ct);

    public Task<IReadOnlyList<CotizacionTareaDto>> SearchTareasAsync(string texto, int take = 25, CancellationToken ct = default)
        => ExecuteLoggedAsync("SearchTareas", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await SqlObjectExistsAsync(cn, "dbo.V_TA_Tareas", token))
                return (IReadOnlyList<CotizacionTareaDto>)Array.Empty<CotizacionTareaDto>();

            var like = $"%{(texto ?? string.Empty).Trim().ToUpperInvariant()}%";
            var limit = Math.Clamp(take, 1, 100);
            var rows = await cn.QueryAsync<CotizacionTareaDto>(new CommandDefinition("""
                SELECT TOP (@Take)
                    LTRIM(RTRIM(IdTarea)) AS IdTarea,
                    ISNULL(Descripcion, '') AS Descripcion,
                    HorasEstimadas,
                    ISNULL(ValorHora, 0) AS ValorHora,
                    ISNULL(TasaIVA, 0) AS TasaIva,
                    ISNULL(Exento, 0) AS Exento
                FROM dbo.V_TA_Tareas
                WHERE UPPER(LTRIM(RTRIM(Descripcion))) LIKE @Like
                   OR UPPER(LTRIM(RTRIM(IdTarea))) LIKE @Like
                ORDER BY Descripcion;
                """, new { Take = limit, Like = like }, cancellationToken: token));
            return (IReadOnlyList<CotizacionTareaDto>)rows.AsList();
        }, "No se pudieron cargar los servicios/tareas.", ct);

    public Task<CotizacionShareDto> EnsureShareAsync(long idVersion, CancellationToken ct = default)
        => ExecuteLoggedAsync("EnsureShare", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // ExecuteScalarAsync devuelve NULL tanto si la versión no existe como si existe pero
            // PublicToken todavía es NULL -- que es el caso normal de CUALQUIER cotización que
            // nunca se compartió. Hay que distinguir ambos casos con un EXISTS separado; antes
            // esto tiraba "La versión indicada no existe" para toda cotización nueva.
            var fila = await cn.QueryFirstOrDefaultAsync<(bool Existe, string? PublicToken)>(new CommandDefinition("""
                SELECT CAST(1 AS bit) AS Existe, PublicToken
                FROM dbo.COT_VERSION WHERE IdVersion = @Id;
                """, new { Id = idVersion }, cancellationToken: token));
            if (!fila.Existe)
                throw new InvalidOperationException("La versión indicada no existe.");

            var tk = (fila.PublicToken ?? string.Empty).Trim();
            if (tk.Length == 0)
            {
                tk = Guid.NewGuid().ToString("N");
                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.COT_VERSION SET PublicToken = @Token WHERE IdVersion = @Id;",
                    new { Id = idVersion, Token = tk }, cancellationToken: token));
            }

            return new CotizacionShareDto
            {
                IdVersion = idVersion,
                IdBase = sessionService.GetActiveSession()?.BaseId ?? 0,
                Token = tk
            };
        }, "No se pudo preparar el enlace de la cotización.", ct);

    public Task<bool> SendByEmailAsync(long idVersion, string destinatario, string? publicUrl = null, string? mensaje = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("SendByEmail", async token =>
        {
            var to = (destinatario ?? string.Empty).Trim();
            if (to.Length == 0)
                throw new InvalidOperationException("Ingresá un email de destino.");
            _ = new System.Net.Mail.MailAddress(to);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var detail = await LoadVersionDetailAsync(cn, idVersion, null, token)
                         ?? throw new InvalidOperationException("La cotización indicada no existe.");

            // Si quien está enviando tiene su propio email configurado (Usuarios → Email propio),
            // se usa esa cuenta; si no, el correo general de la empresa (Configuración General →
            // Email); si tampoco está cargado, la cuenta de rescate de AlfaGestión (más abajo). El
            // puerto/SSL siempre salen del general -- TA_USUARIOS no tiene esas columnas por
            // usuario, solo servidor/usuario/contraseña/remitente/autenticación.
            var usuarioActual = appUserSession.GetCurrentUserName(detail.UsuarioAlta ?? string.Empty);
            var mail = await ResolveEffectiveMailConfigAsync(cn, usuarioActual, token);

            // La cuenta de rescate es compartida por todas las instalaciones -- un tope por mes,
            // por base, evita que una instalación sin configurar (o un loop/bug) agote la
            // reputación de esa cuenta para el resto. Se chequea ANTES de gastar tiempo generando
            // el PDF; si ya está en el límite, ni intenta enviar.
            if (mail.EsFallback)
            {
                var contadorActual = await ObtenerContadorFallbackAsync(cn, token);
                if (contadorActual >= LimiteMensualCuentaFallback)
                    throw new InvalidOperationException(
                        $"Se alcanzó el límite mensual ({LimiteMensualCuentaFallback}) de envíos con la cuenta de correo de AlfaGestión. " +
                        "Configurá tu propio correo en Utilidades → Configuración General → Email (o en tu usuario) para seguir enviando.");
            }

            // El PDF adjunto sale del mismo pipeline que "Descargar PDF" (plantilla del Diseñador
            // de comprobantes) -- así el cliente ve siempre lo mismo por cualquier canal. No se
            // puede inyectar ICotizacionDocumentService por constructor: ese servicio depende de
            // ICotizacionesService, e inyectarlo acá crearía una dependencia circular. Se resuelve
            // recién en este punto, cuando CotizacionesService ya terminó de construirse.
            var documentService = serviceProvider.GetRequiredService<ICotizacionDocumentService>();
            var pdfBytes = await documentService.GeneratePdfAsync(idVersion, uNegocio: null, token);
            var html = BuildEmailHtml(detail, publicUrl, mensaje);

            using var message = new System.Net.Mail.MailMessage
            {
                From = string.IsNullOrWhiteSpace(mail.DisplayName)
                    ? new System.Net.Mail.MailAddress(mail.From)
                    : new System.Net.Mail.MailAddress(mail.From, mail.DisplayName),
                Subject = $"Cotización {detail.CodigoVisible}",
                Body = html,
                IsBodyHtml = true
            };
            message.To.Add(to);
            using var pdfStream = new MemoryStream(pdfBytes);
            message.Attachments.Add(new System.Net.Mail.Attachment(pdfStream, $"{detail.CodigoVisible}.pdf", "application/pdf"));

            using var client = new System.Net.Mail.SmtpClient(mail.Server, mail.Port)
            {
                EnableSsl = mail.EnableSsl,
                DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = mail.RequiresAuth ? new System.Net.NetworkCredential(mail.Login, mail.Password) : null
            };
            await client.SendMailAsync(message, token);

            if (mail.EsFallback)
                await IncrementarContadorFallbackAsync(cn, token);

            if (string.Equals(detail.EstadoVersion, CotizacionEstados.Borrador, StringComparison.OrdinalIgnoreCase))
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.COT_VERSION SET EstadoVersion = @Estado, FechaHoraEnvio = GETDATE(), FechaHoraModificacion = GETDATE() WHERE IdVersion = @Id;",
                    new { Id = idVersion, Estado = CotizacionEstados.Enviada }, cancellationToken: token));
                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.COT_COTIZACION SET Estado = @Estado, FechaHoraModificacion = GETDATE() WHERE IdCotizacion = @Id;",
                    new { Id = detail.IdCotizacion, Estado = CotizacionEstados.Enviada }, cancellationToken: token));
            }

            return mail.EsFallback;
        }, "No se pudo enviar la cotización por email.", ct);

    /// <summary>Resuelve con qué cuenta enviar, en orden: 1) el email propio del usuario que está
    /// enviando (TA_USUARIOS.email_*, cargado en Usuarios → Email propio); 2) el correo general de
    /// la empresa (TA_CONFIGURACION EMAIL_*, cargado en Configuración General → Email); 3) si
    /// ninguno de los dos está cargado, la cuenta de rescate de AlfaGestión (RegistroPublico:Email*
    /// en appsettings/.env -- la misma que ya usa el email de verificación de cuenta y "pedidos",
    /// que sabemos que efectivamente llega). El puerto y SSL siempre salen del general/rescate
    /// porque TA_USUARIOS no tiene esas columnas por usuario.</summary>
    private async Task<EffectiveMailConfig> ResolveEffectiveMailConfigAsync(SqlConnection cn, string? usuarioActual, CancellationToken ct)
    {
        var portRaw = FirstNonEmpty(await ReadConfigAsync(cn, "EMAIL_PORT", ct), configuration["RegistroPublico:EmailPort"]);
        var sslRaw = FirstNonEmpty(await ReadConfigAsync(cn, "EMAIL_SSL", ct), configuration["RegistroPublico:EmailSsl"]);
        var enableSsl = sslRaw.Equals("SI", StringComparison.OrdinalIgnoreCase)
            || sslRaw.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || sslRaw.Equals("1", StringComparison.OrdinalIgnoreCase);
        var port = int.TryParse(portRaw, out var parsedPort) && parsedPort > 0 ? parsedPort : 0;

        if (!string.IsNullOrWhiteSpace(usuarioActual) && port > 0)
        {
            var userConfig = await usuariosService.GetEmailConfigAsync(usuarioActual, ct);
            if (userConfig is { EstaCompleto: true })
            {
                var from = string.IsNullOrWhiteSpace(userConfig.De) ? userConfig.Usuario : userConfig.De;
                return new EffectiveMailConfig(userConfig.Server, port, userConfig.Usuario, from,
                    NullIfEmpty(userConfig.NombreRemitente), userConfig.Password, userConfig.Autenticacion, enableSsl, EsFallback: false);
            }
        }

        // Todo o nada: si falta CUALQUIERA de los 3 campos esenciales (servidor/cuenta/clave) en
        // la base del cliente, no se usa NADA de ahí -- nunca se mezcla, por ejemplo, un
        // EMAIL_SERVER con basura vieja (se encontró literalmente el valor "Otro", un resto de
        // algún combo de proveedores) con la cuenta/clave de rescate: esa mezcla intenta conectar
        // a un host que no existe y explota con un error de DNS que además el sistema mostraba mal
        // (ver fix de IsFtpAccessError en AppUiOperationService -- esos códigos de socket no son
        // exclusivos de FTP, cualquier falla de red los puede tirar).
        var server = (await ReadConfigAsync(cn, "EMAIL_SERVER", ct)).Trim();
        var account = (await ReadConfigAsync(cn, "EMAIL_CTA", ct)).Trim();
        var password = (await ReadConfigAsync(cn, "EMAIL_PASS", ct)).Trim();
        if (server.Length > 0 && port > 0 && account.Length > 0 && password.Length > 0)
            return new EffectiveMailConfig(server, port, account, account, null, password, true, enableSsl, EsFallback: false);

        // Cuenta de rescate de AlfaGestión -- también todo o nada, y todos sus campos (incluido
        // puerto/SSL) salen de RegistroPublico:Email*, nunca mezclados con lo que haya (o falte)
        // en la base del cliente.
        var vendorServer = (configuration["RegistroPublico:EmailServer"] ?? string.Empty).Trim();
        var vendorAccount = (configuration["RegistroPublico:EmailAccount"] ?? string.Empty).Trim();
        var vendorPassword = (configuration["RegistroPublico:EmailPassword"] ?? string.Empty).Trim();
        var vendorPortRaw = (configuration["RegistroPublico:EmailPort"] ?? string.Empty).Trim();
        var vendorSslRaw = (configuration["RegistroPublico:EmailSsl"] ?? string.Empty).Trim();
        var vendorSsl = vendorSslRaw.Equals("SI", StringComparison.OrdinalIgnoreCase)
            || vendorSslRaw.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || vendorSslRaw.Equals("1", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(vendorServer) || !int.TryParse(vendorPortRaw, out var vendorPort) || vendorPort <= 0
            || string.IsNullOrWhiteSpace(vendorAccount) || string.IsNullOrWhiteSpace(vendorPassword))
            throw new InvalidOperationException("Falta configurar el correo saliente. Revisá Utilidades → Configuración General → Email, o cargá un email propio en tu usuario.");
        return new EffectiveMailConfig(vendorServer, vendorPort, vendorAccount, vendorAccount, null, vendorPassword, true, vendorSsl, EsFallback: true);
    }

    /// <summary>Primer valor no vacío, recortado. Solo se usa para puerto/SSL -- esos dos no
    /// forman parte del "todo o nada" servidor/cuenta/clave porque mezclarlos no arma un host
    /// inválido, como sí pasaba antes cuando un EMAIL_SERVER viejo/incompleto en la base se
    /// mezclaba con la cuenta de rescate.</summary>
    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private const int LimiteMensualCuentaFallback = 200;
    private const string ClaveContadorFallback = "EMAIL_FALLBACK_CONTADOR";

    /// <summary>Envíos ya hechos con la cuenta de rescate en el mes actual (el contador se guarda
    /// como "yyyyMM:cantidad" en TA_CONFIGURACION y se reinicia solo -- no se resetea a mano, un
    /// período distinto al actual simplemente se lee como 0).</summary>
    private async Task<int> ObtenerContadorFallbackAsync(SqlConnection cn, CancellationToken ct)
    {
        var raw = await ReadConfigAsync(cn, ClaveContadorFallback, ct);
        var partes = raw.Split(':', 2);
        return partes.Length == 2 && partes[0] == DateTime.Now.ToString("yyyyMM") && int.TryParse(partes[1], out var contador)
            ? contador
            : 0;
    }

    private async Task IncrementarContadorFallbackAsync(SqlConnection cn, CancellationToken ct)
    {
        var contadorActual = await ObtenerContadorFallbackAsync(cn, ct);
        await SetConfigAsync(cn, ClaveContadorFallback, $"{DateTime.Now:yyyyMM}:{contadorActual + 1}", null, ct);
    }

    private sealed record EffectiveMailConfig(string Server, int Port, string Login, string From, string? DisplayName, string Password, bool RequiresAuth, bool EnableSsl, bool EsFallback);

    public Task<CotizacionAlfaConfigDto> GetAlfaConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetAlfaConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await LoadAlfaConfigAsync(cn, token);
        }, "No se pudo cargar la configuración del configurador Alfa Gestión.", ct);

    public Task SaveAlfaConfigAsync(CotizacionAlfaConfigDto config, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveAlfaConfig", async token =>
        {
            ArgumentNullException.ThrowIfNull(config);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await SetConfigAsync(cn, "COTIZACIONES_ALFA_PRECIO_BASE", config.PrecioBase.ToString(System.Globalization.CultureInfo.InvariantCulture), null, token);
            await SetConfigAsync(cn, "COTIZACIONES_ALFA_PRECIO_USUARIO", config.PrecioPorUsuario.ToString(System.Globalization.CultureInfo.InvariantCulture), null, token);
            await SetConfigAsync(cn, "COTIZACIONES_ALFA_MODULOS", string.Empty, JsonSerializer.Serialize(config.Modulos), token);
            await SetConfigAsync(cn, "COTIZACIONES_ALFA_PACKS", string.Empty, JsonSerializer.Serialize(config.Packs), token);
        }, "No se pudo guardar la configuración del configurador Alfa Gestión.", ct);

    public Task<CotizacionAlfaResultDto> BuildAlfaLinesAsync(string? clienteCodigo, CotizacionAlfaSelectionRequest selection, CancellationToken ct = default)
        => ExecuteLoggedAsync("BuildAlfaLines", async token =>
        {
            ArgumentNullException.ThrowIfNull(selection);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var config = await LoadAlfaConfigAsync(cn, token);

            var modulos = config.Modulos.Where(m => selection.ModulosCodigo.Contains(m.Codigo, StringComparer.OrdinalIgnoreCase)).ToList();
            var usuarios = Math.Max(1, selection.CantidadUsuarios);
            var result = new CotizacionAlfaResultDto();
            var orden = 0;

            if (modulos.Count > 0)
            {
                result.Lineas.Add(new CotizacionLineaDto
                {
                    Orden = orden++,
                    Tipo = CotizacionDetTipos.Informativo,
                    Descripcion = $"Módulos incluidos: {string.Join(", ", modulos.Select(m => m.Nombre))}",
                    Cantidad = 1,
                    ImpactaTotal = false,
                    OrigenPrecio = CotizacionOrigenPrecio.Manual
                });
            }

            var precioLicencia = config.PrecioBase + usuarios * config.PrecioPorUsuario;
            result.Lineas.Add(new CotizacionLineaDto
            {
                Orden = orden++,
                Tipo = CotizacionDetTipos.Libre,
                Descripcion = $"Alfa Gestión - Licencia ({usuarios} usuario{(usuarios == 1 ? "" : "s")})",
                Cantidad = 1,
                PrecioBase = precioLicencia,
                PrecioUnitario = precioLicencia,
                Subtotal = precioLicencia,
                ImpactaTotal = true,
                OrigenPrecio = CotizacionOrigenPrecio.Manual
            });

            var regla = config.Packs
                .Where(p => usuarios <= p.MaxUsuarios && modulos.Count <= p.MaxModulos)
                .OrderBy(p => p.MaxUsuarios)
                .ThenBy(p => p.MaxModulos)
                .FirstOrDefault();
            if (regla is not null && !string.IsNullOrWhiteSpace(regla.IdTarea))
            {
                result.PackRecomendado = regla;
                if (await SqlObjectExistsAsync(cn, "dbo.V_TA_Tareas", token))
                {
                    var tarea = await cn.QueryFirstOrDefaultAsync<CotizacionTareaDto>(new CommandDefinition(
                        "SELECT LTRIM(RTRIM(IdTarea)) AS IdTarea, ISNULL(Descripcion,'') AS Descripcion, HorasEstimadas, ISNULL(ValorHora,0) AS ValorHora, ISNULL(TasaIVA,0) AS TasaIva, ISNULL(Exento,0) AS Exento FROM dbo.V_TA_Tareas WHERE LTRIM(RTRIM(IdTarea)) = @Id;",
                        new { Id = regla.IdTarea.Trim() }, cancellationToken: token));
                    if (tarea is not null)
                        result.PackRecomendadoDescripcion = tarea.Descripcion;

                    if (!string.IsNullOrWhiteSpace(selection.IdTareaPackElegido))
                    {
                        var elegida = string.Equals(selection.IdTareaPackElegido, regla.IdTarea, StringComparison.OrdinalIgnoreCase)
                            ? tarea
                            : await cn.QueryFirstOrDefaultAsync<CotizacionTareaDto>(new CommandDefinition(
                                "SELECT LTRIM(RTRIM(IdTarea)) AS IdTarea, ISNULL(Descripcion,'') AS Descripcion, HorasEstimadas, ISNULL(ValorHora,0) AS ValorHora, ISNULL(TasaIVA,0) AS TasaIva, ISNULL(Exento,0) AS Exento FROM dbo.V_TA_Tareas WHERE LTRIM(RTRIM(IdTarea)) = @Id;",
                                new { Id = selection.IdTareaPackElegido.Trim() }, cancellationToken: token));
                        if (elegida is not null)
                        {
                            var cantidad = elegida.HorasEstimadas is > 0 ? elegida.HorasEstimadas.Value : 1m;
                            var precioUnitario = elegida.ValorHora;
                            result.Lineas.Add(new CotizacionLineaDto
                            {
                                Orden = orden++,
                                Tipo = CotizacionDetTipos.Tarea,
                                CodigoRef = elegida.IdTarea,
                                Descripcion = elegida.Descripcion,
                                Cantidad = cantidad,
                                PrecioBase = precioUnitario,
                                PrecioUnitario = precioUnitario,
                                TasaIva = elegida.TasaIva,
                                Subtotal = decimal.Round(precioUnitario * cantidad, 2),
                                ImpactaTotal = true,
                                OrigenPrecio = CotizacionOrigenPrecio.Manual
                            });
                        }
                    }
                }
            }

            return result;
        }, "No se pudo armar la configuración de Alfa Gestión.", ct);

    public Task<bool> PermiteDescuentoPorLineaAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("PermiteDescuentoPorLinea", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await PermiteDescuentoPorLineaInternalAsync(cn, token);
        }, "No se pudo leer la configuración de descuento por línea.", ct);

    public Task SetPermiteDescuentoPorLineaAsync(bool permitido, CancellationToken ct = default)
        => ExecuteLoggedAsync("SetPermiteDescuentoPorLinea", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await SetConfigAsync(cn, "COTIZACIONES_PERMITE_DESCUENTO_LINEA", permitido ? "1" : "0", null, token);
        }, "No se pudo guardar la configuración de descuento por línea.", ct);

    public Task<IReadOnlyList<CotizacionVersionSummaryDto>> GetVersionesAsync(long idCotizacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetVersiones", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = (await cn.QueryAsync<CotizacionVersionSummaryDto>(new CommandDefinition("""
                SELECT
                    v.IdVersion, v.NumeroVersion, ISNULL(v.EstadoVersion, 'BORRADOR') AS EstadoVersion,
                    v.FechaHoraAlta, v.FechaHoraEnvio, ISNULL(v.UsuarioAlta, '') AS UsuarioAlta,
                    CAST(CASE WHEN v.IdVersion = c.IdVersionActual THEN 1 ELSE 0 END AS bit) AS EsActual
                FROM dbo.COT_VERSION v
                INNER JOIN dbo.COT_COTIZACION c ON c.IdCotizacion = v.IdCotizacion
                WHERE v.IdCotizacion = @Id
                ORDER BY v.NumeroVersion DESC;
                """, new { Id = idCotizacion }, cancellationToken: token))).AsList();
            return (IReadOnlyList<CotizacionVersionSummaryDto>)rows;
        }, "No se pudo cargar el historial de versiones.", ct);

    public Task<IReadOnlyList<CotizacionClienteOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync("SearchClientes", async token =>
        {
            var search = (texto ?? string.Empty).Trim();
            if (search.Length < 2)
                return (IReadOnlyList<CotizacionClienteOptionDto>)Array.Empty<CotizacionClienteOptionDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = (await cn.QueryAsync<CotizacionClienteOptionDto>(new CommandDefinition("""
                SELECT TOP (12)
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    ISNULL(RAZON_SOCIAL, '') AS RazonSocial,
                    ISNULL(LOCALIDAD, '') AS Localidad,
                    ISNULL(PROVINCIA, '') AS Provincia
                FROM dbo.VT_CLIENTES
                WHERE LTRIM(RTRIM(ISNULL(CODIGO, ''))) <> ''
                  AND (
                        CODIGO LIKE @Prefijo
                        OR RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @Contiene
                      )
                ORDER BY RAZON_SOCIAL;
                """, new { Prefijo = $"{search}%", Contiene = $"%{search}%" }, cancellationToken: token))).AsList();
            return (IReadOnlyList<CotizacionClienteOptionDto>)rows;
        }, "No se pudieron buscar clientes.", ct);

    public Task<CotizacionClienteInfoDto?> ResolveClienteAsync(string codigoCliente, CancellationToken ct = default)
        => ExecuteLoggedAsync("ResolveCliente", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigoCliente))
                return (CotizacionClienteInfoDto?)null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var pricing = await priceResolver.ResolveContextAsync(cn, codigoCliente, token);
            return new CotizacionClienteInfoDto
            {
                Codigo = pricing.ClienteCodigo,
                Nombre = pricing.ClienteNombre,
                IdLista = pricing.IdLista,
                ClasePrecio = pricing.ClasePrecio,
                EsConsumidorFinal = pricing.EsConsumidorFinal
            };
        }, "No se pudo resolver el cliente.", ct);

    public Task<CotizacionResumenDto> GetResumenAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetResumen", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var row = await cn.QueryFirstOrDefaultAsync<CotizacionResumenDto>(new CommandDefinition("""
                SELECT
                    ISNULL(SUM(CASE WHEN c.Estado NOT IN ('ACEPTADA', 'RECHAZADA', 'ANULADA') THEN v.Total ELSE 0 END), 0) AS TotalAbierto,
                    ISNULL(SUM(CASE WHEN c.Estado = 'ENVIADA' THEN 1 ELSE 0 END), 0) AS EsperandoRespuesta,
                    ISNULL(SUM(CASE WHEN c.Estado = 'ACEPTADA' AND c.FechaHoraModificacion >= @InicioMes THEN 1 ELSE 0 END), 0) AS AceptadasEsteMes
                FROM dbo.COT_COTIZACION c
                INNER JOIN dbo.COT_VERSION v ON v.IdVersion = c.IdVersionActual
                WHERE ISNULL(c.Baja, 0) = 0;
                """, new { InicioMes = inicioMes }, cancellationToken: token));
            return row ?? new CotizacionResumenDto();
        }, "No se pudo cargar el resumen de cotizaciones.", ct);

    // El asistente de IA (búsqueda de artículos por lenguaje natural / redacción de propuesta)
    // ya está implementado y probado en ICrmCotizacionService -- no toca CRM_COTIZACION, solo
    // llama a OpenAI y al mismo IArticuloPrecioResolverService que ya usamos acá. Se delega
    // directo en vez de duplicar el prompt engineering.
    public Task<IReadOnlyList<CrmCotizacionAiLineaSugeridaDto>> SuggestLinesFromPromptAsync(string? clienteCodigo, string prompt, CancellationToken ct = default)
        => crmCotizacionService.SuggestLinesFromPromptAsync(clienteCodigo, prompt, ct);

    public Task<string> GenerateServiceProposalAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default)
        => crmCotizacionService.GenerateServiceProposalAsync(prompt, clienteNombre, ct);

    public Task<string> GenerateEmailMessageAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default)
        => crmCotizacionService.GenerateEmailMessageAsync(prompt, clienteNombre, ct);

    private const string ClaveEmailTextoPredeterminado = "COTIZACIONES_EMAIL_TEXTO";

    public Task<string> GetEmailTextoPredeterminadoAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetEmailTextoPredeterminado", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await ReadConfigJsonAsync(cn, ClaveEmailTextoPredeterminado, token);
        }, "No se pudo cargar el texto predeterminado del email.", ct);

    public Task SetEmailTextoPredeterminadoAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync("SetEmailTextoPredeterminado", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await SetConfigAsync(cn, ClaveEmailTextoPredeterminado, string.Empty, texto ?? string.Empty, token);
        }, "No se pudo guardar el texto predeterminado del email.", ct);

    // ---- Helpers privados ----

    private async Task<bool> PermiteDescuentoPorLineaInternalAsync(SqlConnection cn, CancellationToken ct, SqlTransaction? tx = null)
        => ParseBool(await ReadConfigAsync(cn, "COTIZACIONES_PERMITE_DESCUENTO_LINEA", ct, tx));

    private async Task<CotizacionAlfaConfigDto> LoadAlfaConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var precioBase = await ReadConfigAsync(cn, "COTIZACIONES_ALFA_PRECIO_BASE", ct);
        var precioUsuario = await ReadConfigAsync(cn, "COTIZACIONES_ALFA_PRECIO_USUARIO", ct);
        var modulosJson = await ReadConfigJsonAsync(cn, "COTIZACIONES_ALFA_MODULOS", ct);
        var packsJson = await ReadConfigJsonAsync(cn, "COTIZACIONES_ALFA_PACKS", ct);

        return new CotizacionAlfaConfigDto
        {
            PrecioBase = decimal.TryParse(precioBase, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pb) ? pb : 0m,
            PrecioPorUsuario = decimal.TryParse(precioUsuario, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pu) ? pu : 0m,
            Modulos = DeserializeOrEmpty<CotizacionAlfaModuloDto>(modulosJson),
            Packs = DeserializeOrEmpty<CotizacionAlfaPackReglaDto>(packsJson)
        };
    }

    private static List<T> DeserializeOrEmpty<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<CotizacionVersionDetailDto?> LoadVersionDetailAsync(SqlConnection cn, long idVersion, SqlTransaction? tx, CancellationToken ct)
    {
        var header = await cn.QueryFirstOrDefaultAsync<CotizacionVersionRow>(new CommandDefinition("""
            SELECT
                v.IdVersion, v.IdCotizacion, c.Numero, ISNULL(c.TC, 'COT') AS TC, v.NumeroVersion,
                ISNULL(c.Estado, 'BORRADOR') AS EstadoCotizacion, ISNULL(v.EstadoVersion, 'BORRADOR') AS EstadoVersion,
                c.IdOportunidad, c.CodigoCliente,
                v.Fecha, v.FechaVencimiento,
                ISNULL(v.EmpresaProspecto, '') AS EmpresaProspecto,
                ISNULL(v.ContactoNombre, '') AS ContactoNombre,
                ISNULL(v.ContactoEmail, '') AS ContactoEmail,
                ISNULL(v.ContactoTelefono, '') AS ContactoTelefono,
                ISNULL(v.DocumentoFiscal, '') AS DocumentoFiscal,
                ISNULL(v.CodigoMoneda, '') AS CodigoMoneda,
                ISNULL(v.Observaciones, '') AS Observaciones,
                ISNULL(v.CuerpoPropuesta, '') AS CuerpoPropuesta,
                v.DescuentoGeneralPorcentaje, v.Subtotal, v.TotalDescuento, v.Total, v.PublicToken,
                v.IncluyePortada, v.UsuarioAlta
            FROM dbo.COT_VERSION v
            INNER JOIN dbo.COT_COTIZACION c ON c.IdCotizacion = v.IdCotizacion
            WHERE v.IdVersion = @Id;
            """, new { Id = idVersion }, tx, cancellationToken: ct));
        if (header is null)
            return null;

        var secciones = (await cn.QueryAsync<CotizacionSeccionDto>(new CommandDefinition("""
            SELECT IdSeccion, Orden, ISNULL(Titulo, '') AS Titulo, Descripcion, MostrarSubtotal
            FROM dbo.COT_SECCION WHERE IdVersion = @Id ORDER BY Orden, IdSeccion;
            """, new { Id = idVersion }, tx, cancellationToken: ct))).AsList();

        var lineas = (await cn.QueryAsync<CotizacionLineaDto>(new CommandDefinition("""
            SELECT IdDetalle, IdSeccion, Orden, ISNULL(Tipo, 'LIBRE') AS Tipo, CodigoRef, ISNULL(Descripcion, '') AS Descripcion,
                   Cantidad, PrecioBase, PorcentajeDescuento, PrecioUnitario, TasaIva, Subtotal, ImpactaTotal, OrigenPrecio
            FROM dbo.COT_DET WHERE IdVersion = @Id ORDER BY Orden, IdDetalle;
            """, new { Id = idVersion }, tx, cancellationToken: ct))).AsList();

        return new CotizacionVersionDetailDto
        {
            IdVersion = header.IdVersion,
            IdCotizacion = header.IdCotizacion,
            Numero = header.Numero,
            TC = header.TC,
            NumeroVersion = header.NumeroVersion,
            EstadoCotizacion = header.EstadoCotizacion,
            EstadoVersion = header.EstadoVersion,
            IdOportunidad = header.IdOportunidad,
            CodigoCliente = header.CodigoCliente,
            Fecha = header.Fecha,
            FechaVencimiento = header.FechaVencimiento,
            EmpresaProspecto = header.EmpresaProspecto,
            ContactoNombre = header.ContactoNombre,
            ContactoEmail = header.ContactoEmail,
            ContactoTelefono = header.ContactoTelefono,
            DocumentoFiscal = header.DocumentoFiscal,
            CodigoMoneda = header.CodigoMoneda,
            Observaciones = header.Observaciones,
            CuerpoPropuesta = header.CuerpoPropuesta,
            DescuentoGeneralPorcentaje = header.DescuentoGeneralPorcentaje,
            Subtotal = header.Subtotal,
            TotalDescuento = header.TotalDescuento,
            Total = header.Total,
            PublicToken = header.PublicToken,
            IncluyePortada = header.IncluyePortada,
            UsuarioAlta = header.UsuarioAlta,
            Secciones = secciones,
            Lineas = lineas
        };
    }

    /// <summary>Cuerpo del email: simple y con soporte de cliente de correo garantizado (nada de
    /// CSS de impresión, mm, grid, etc. -- eso vive en el PDF adjunto, que sale del mismo pipeline
    /// que "Descargar PDF"). Antes este método armaba todo el detalle a mano en HTML, con un
    /// formato distinto al PDF y mostrando además las Observaciones internas -- ahora el email
    /// solo presenta y adjunta/enlaza el documento real.</summary>
    /// <summary>El "mensaje" es texto plano tipeado por el usuario (o generado por IA) en el
    /// diálogo de envío -- nunca HTML crudo, por eso se encodea igual que el resto y los saltos de
    /// línea se preservan con white-space:pre-wrap en vez de reinterpretar como markup.</summary>
    private static string BuildEmailHtml(CotizacionVersionDetailDto d, string? publicUrl, string? mensaje)
    {
        var ar = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
        string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        var sb = new System.Text.StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"></head>")
          .Append("<body style=\"margin:0;background:#f1f5f9;\">")
          .Append("<div style=\"max-width:600px;margin:0 auto;padding:28px 24px;font-family:Segoe UI,Arial,sans-serif;color:#0f172a;background:#ffffff;border-radius:8px;\">")
          .Append("<div style=\"font-size:16px;font-weight:700;border-bottom:2px solid #2563eb;padding-bottom:12px;margin-bottom:16px;\">Cotización ")
          .Append(E(d.CodigoVisible)).Append("</div>");

        var texto = (mensaje ?? string.Empty).Trim();
        if (texto.Length > 0)
        {
            sb.Append("<div style=\"font-size:14px;line-height:1.6;margin:0 0 16px;white-space:pre-wrap;\">").Append(E(texto)).Append("</div>");
        }
        else
        {
            sb.Append("<p style=\"font-size:14px;line-height:1.6;margin:0 0 16px;\">Te compartimos la cotización <strong>")
              .Append(E(d.CodigoVisible)).Append("</strong>");
            if (!string.IsNullOrWhiteSpace(d.EmpresaProspecto))
                sb.Append(" para <strong>").Append(E(d.EmpresaProspecto)).Append("</strong>");
            sb.Append(". Ante cualquier consulta, quedamos a disposición.</p>");
        }

        sb.Append("<p style=\"font-size:13px;color:#64748b;margin:0 0 12px;\">Se adjunta el PDF de la cotización");
        if (!string.IsNullOrWhiteSpace(publicUrl))
            sb.Append(", y también podés verla online acá: <a href=\"").Append(E(publicUrl)).Append("\">").Append(E(publicUrl)).Append("</a>");
        sb.Append(".</p>");

        if (d.FechaVencimiento is { } vencimiento)
            sb.Append("<p style=\"font-size:13px;color:#64748b;margin:0;\">Válida hasta ").Append(vencimiento.ToString("dd/MM/yyyy", ar)).Append(".</p>");

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private async Task<bool> SqlObjectExistsAsync(SqlConnection cn, string objectName, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(@Name) IS NOT NULL THEN 1 ELSE 0 END;",
            new { Name = objectName }, cancellationToken: ct)) == 1;

    private async Task<string> ReadConfigAsync(SqlConnection cn, string clave, CancellationToken ct, SqlTransaction? tx = null)
    {
        var value = await cn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition("""
            SELECT TOP (1) CASE WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR)) ELSE '' END
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """, new { Clave = clave.ToUpperInvariant() }, tx, cancellationToken: ct));
        return value ?? string.Empty;
    }

    private async Task<string> ReadConfigJsonAsync(SqlConnection cn, string clave, CancellationToken ct)
    {
        var value = await cn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition("""
            SELECT TOP (1)
                CASE
                    WHEN ISNULL(CAST(ValorAux AS nvarchar(max)), '') <> '' THEN CAST(ValorAux AS nvarchar(max))
                    WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR))
                    ELSE ''
                END
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """, new { Clave = clave.ToUpperInvariant() }, cancellationToken: ct));
        return value ?? string.Empty;
    }

    private async Task SetConfigAsync(SqlConnection cn, string clave, string valor, string? valorAux, CancellationToken ct)
    {
        var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;",
            new { Clave = clave.ToUpperInvariant() }, cancellationToken: ct));
        if (existe > 0)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, ValorAux = @ValorAux, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;",
                new { Clave = clave.ToUpperInvariant(), Valor = valor, ValorAux = valorAux }, cancellationToken: ct));
        }
        else
        {
            await cn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, ValorAux, FechaHora_Grabacion) VALUES (N'COTIZACIONES', @Clave, @Valor, @ValorAux, GETDATE());",
                new { Clave = clave, Valor = valor, ValorAux = valorAux }, cancellationToken: ct));
        }
    }

    private static bool ParseBool(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();
        return v is "1" or "S" or "SI" or "SÍ" or "TRUE" or "T" or "Y";
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeUser(string? user) => string.IsNullOrWhiteSpace(user) ? "web" : user.Trim();

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
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

    private sealed class CotizacionListRow
    {
        public long IdCotizacion { get; set; }
        public long IdVersion { get; set; }
        public int Numero { get; set; }
        public string TC { get; set; } = "COT";
        public string Estado { get; set; } = CotizacionEstados.Borrador;
        public long? IdOportunidad { get; set; }
        public string CodigoCliente { get; set; } = string.Empty;
        public int NumeroVersion { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public string EmpresaProspecto { get; set; } = string.Empty;
        public string ContactoNombre { get; set; } = string.Empty;
        public string CodigoMoneda { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public int TotalRows { get; set; }
    }

    private sealed class CotizacionVersionRow
    {
        public long IdVersion { get; set; }
        public long IdCotizacion { get; set; }
        public int Numero { get; set; }
        public string TC { get; set; } = "COT";
        public int NumeroVersion { get; set; }
        public string EstadoCotizacion { get; set; } = CotizacionEstados.Borrador;
        public string EstadoVersion { get; set; } = CotizacionEstados.Borrador;
        public long? IdOportunidad { get; set; }
        public string? CodigoCliente { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public string EmpresaProspecto { get; set; } = string.Empty;
        public string ContactoNombre { get; set; } = string.Empty;
        public string ContactoEmail { get; set; } = string.Empty;
        public string ContactoTelefono { get; set; } = string.Empty;
        public string DocumentoFiscal { get; set; } = string.Empty;
        public string CodigoMoneda { get; set; } = string.Empty;
        public string Observaciones { get; set; } = string.Empty;
        public string CuerpoPropuesta { get; set; } = string.Empty;
        public decimal DescuentoGeneralPorcentaje { get; set; }
        public decimal Subtotal { get; set; }
        public decimal TotalDescuento { get; set; }
        public decimal Total { get; set; }
        public string? PublicToken { get; set; }
        public bool IncluyePortada { get; set; } = true;
        public string? UsuarioAlta { get; set; }
    }
}

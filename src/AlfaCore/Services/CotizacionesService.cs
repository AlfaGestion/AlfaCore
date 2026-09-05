using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class CotizacionesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    ICentralBasesService centralBasesService,
    IArticuloPrecioResolverService priceResolver,
    ICrmService crmService) : ICotizacionesService
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
                     DescuentoGeneralPorcentaje, Subtotal, TotalDescuento, Total, EstadoVersion, UsuarioAlta, FechaHoraAlta)
                    OUTPUT INSERTED.IdVersion
                    VALUES (@IdCotizacion, @NumeroVersion, CAST(GETDATE() AS date), @FechaVencimiento, @EmpresaProspecto, @ContactoNombre,
                            @ContactoEmail, @ContactoTelefono, @DocumentoFiscal, @CodigoMoneda, @Observaciones, @CuerpoPropuesta,
                            @DescuentoGeneralPorcentaje, @Subtotal, @TotalDescuento, @Total, @Estado, @Usuario, GETDATE());
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
                        Total = @Total, FechaHoraModificacion = GETDATE()
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
                    Total = total
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
            var actual = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT PublicToken FROM dbo.COT_VERSION WHERE IdVersion = @Id;", new { Id = idVersion }, cancellationToken: token));
            if (actual is null)
                throw new InvalidOperationException("La versión indicada no existe.");

            var tk = actual.Trim();
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

    public Task<string?> RenderPublicHtmlAsync(int idBase, string token, CancellationToken ct = default)
        => ExecuteLoggedAsync("RenderPublic", async innerCt =>
        {
            var tk = (token ?? string.Empty).Trim();
            if (idBase <= 0 || tk.Length == 0)
                return null;

            var baseInfo = await centralBasesService.GetByIdAsync(idBase, innerCt);
            if (baseInfo is null)
                return null;

            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = baseInfo.DbServer,
                InitialCatalog = baseInfo.DbName,
                UserID = baseInfo.DbUser,
                Password = baseInfo.DbPassword,
                TrustServerCertificate = true
            }.ConnectionString;

            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(innerCt);

            var idVersion = await cn.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT IdVersion FROM dbo.COT_VERSION WHERE PublicToken = @Token;", new { Token = tk }, cancellationToken: innerCt));
            if (idVersion is null)
                return null;

            var detail = await LoadVersionDetailAsync(cn, idVersion.Value, null, innerCt);
            return detail is null ? null : BuildPublicHtml(detail);
        }, "No se pudo mostrar la cotización.", ct);

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
                v.DescuentoGeneralPorcentaje, v.Subtotal, v.TotalDescuento, v.Total, v.PublicToken
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
            Secciones = secciones,
            Lineas = lineas
        };
    }

    private static string BuildPublicHtml(CotizacionVersionDetailDto d)
    {
        var ar = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
        string Money(decimal v) => v.ToString("C0", ar);
        string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        var sb = new System.Text.StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<title>Cotización ").Append(E(d.CodigoVisible)).Append("</title></head><body style=\"margin:0;background:#f1f5f9;\">")
          .Append("<div style=\"max-width:720px;margin:0 auto;padding:24px;font-family:Segoe UI,Arial,sans-serif;color:#0f172a;background:#ffffff;\">")
          .Append("<div style=\"display:flex;justify-content:space-between;gap:16px;border-bottom:2px solid #2563eb;padding-bottom:12px;margin-bottom:16px;\">")
          .Append("<div style=\"font-size:16px;font-weight:700;\">Cotización ").Append(E(d.CodigoVisible)).Append(" · v").Append(d.NumeroVersion).Append("</div>")
          .Append("<div style=\"text-align:right;font-size:12px;color:#64748b;\">").Append(d.Fecha.ToString("dd/MM/yyyy", ar));
        if (d.FechaVencimiento is { } v)
            sb.Append("<br/>Válida hasta ").Append(v.ToString("dd/MM/yyyy", ar));
        sb.Append("</div></div>");

        if (!string.IsNullOrWhiteSpace(d.EmpresaProspecto))
            sb.Append("<div style=\"margin-bottom:12px;font-size:14px;\"><strong>Para:</strong> ").Append(E(d.EmpresaProspecto)).Append("</div>");

        if (!string.IsNullOrWhiteSpace(d.CuerpoPropuesta))
            sb.Append("<div style=\"font-size:14px;line-height:1.5;margin-bottom:16px;\">").Append(d.CuerpoPropuesta).Append("</div>");

        foreach (var seccion in d.Secciones.OrderBy(s => s.Orden))
        {
            var lineasSeccion = d.Lineas.Where(l => l.IdSeccion == seccion.IdSeccion).OrderBy(l => l.Orden).ToList();
            if (lineasSeccion.Count == 0)
                continue;
            sb.Append("<h3 style=\"font-size:14px;margin:16px 0 6px;\">").Append(E(seccion.Titulo)).Append("</h3>");
            AppendLineasTable(sb, lineasSeccion, Money, E, ar);
        }

        var sinSeccion = d.Lineas.Where(l => l.IdSeccion is null).OrderBy(l => l.Orden).ToList();
        if (sinSeccion.Count > 0)
            AppendLineasTable(sb, sinSeccion, Money, E, ar);

        sb.Append("<div style=\"text-align:right;font-size:14px;margin-top:12px;\">");
        if (d.DescuentoGeneralPorcentaje > 0)
        {
            sb.Append("<div>Subtotal: ").Append(Money(d.Subtotal)).Append("</div>");
            sb.Append("<div>Descuento (").Append(d.DescuentoGeneralPorcentaje.ToString("0.##", ar)).Append("%): -").Append(Money(d.TotalDescuento)).Append("</div>");
        }
        sb.Append("<div style=\"font-size:18px;margin-top:4px;\">Total: <strong>").Append(Money(d.Total)).Append("</strong></div>");
        sb.Append("</div>");

        if (!string.IsNullOrWhiteSpace(d.Observaciones))
            sb.Append("<div style=\"margin-top:16px;font-size:12px;color:#64748b;\">").Append(E(d.Observaciones)).Append("</div>");

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void AppendLineasTable(System.Text.StringBuilder sb, List<CotizacionLineaDto> lineas, Func<decimal, string> money, Func<string?, string> encode, IFormatProvider ar)
    {
        sb.Append("<table style=\"width:100%;border-collapse:collapse;font-size:13px;margin-bottom:8px;\"><tbody>");
        foreach (var l in lineas)
        {
            sb.Append("<tr>");
            sb.Append("<td style=\"padding:5px 8px;border-bottom:1px solid #f1f5f9;\">").Append(encode(l.Descripcion)).Append("</td>");
            if (l.ImpactaTotal)
            {
                sb.Append("<td style=\"padding:5px 8px;border-bottom:1px solid #f1f5f9;text-align:right;white-space:nowrap;\">").Append(l.Cantidad.ToString("0.##", ar)).Append("</td>");
                sb.Append("<td style=\"padding:5px 8px;border-bottom:1px solid #f1f5f9;text-align:right;white-space:nowrap;\">").Append(money(l.Subtotal)).Append("</td>");
            }
            else
            {
                sb.Append("<td colspan=\"2\"></td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
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
    }
}

using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CrmCotizacionService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : ICrmCotizacionService
{
    private const string ModuleName = "CRM";
    private const string DefaultTc = "CTZ";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<CrmCotizacionPricingContextDto> ResolvePricingContextAsync(string? clienteCodigo, CancellationToken ct = default)
        => ExecuteLoggedAsync("CotizacionPricingContext", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await ResolvePricingContextInternalAsync(cn, clienteCodigo, token);
        }, "No se pudo resolver la lista de precios del cliente.", ct);

    public Task<IReadOnlyList<CrmCotizacionArticuloDto>> SearchArticulosAsync(string? clienteCodigo, string texto, int take = 25, CancellationToken ct = default)
        => ExecuteLoggedAsync("CotizacionSearchArticulos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var pricing = await ResolvePricingContextInternalAsync(cn, clienteCodigo, token);
            var clase = Math.Clamp(pricing.ClasePrecio, 1, 8);
            var limit = Math.Clamp(take, 1, 100);

            var palabras = (texto ?? string.Empty).ToUpperInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var wordFilters = new System.Text.StringBuilder();
            var parameters = new DynamicParameters();
            parameters.Add("IdLista", pricing.IdLista);
            parameters.Add("Take", limit);
            for (var i = 0; i < palabras.Length; i++)
            {
                parameters.Add($"Like{i}", $"%{palabras[i]}%");
                wordFilters.Append($"""

                  AND (
                        UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @Like{i}
                        OR UPPER(LTRIM(RTRIM(a.DESCRIPCION))) LIKE @Like{i}
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, '')))) LIKE @Like{i}
                  )
                """);
            }

            var sql = $"""
                SELECT TOP (@Take)
                    LTRIM(RTRIM(a.IDARTICULO)) AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.CODIGOBARRA)), '') AS Codigo,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS Descripcion,
                    ISNULL(CAST(a.TasaIVA AS decimal(9,4)), 0) AS TasaIva,
                    ISNULL(a.PRECIO{clase}, 0) AS PrecioMaestro,
                    ISNULL(p.Precio{clase}, 0) AS PrecioLista
                FROM dbo.V_MA_ARTICULOS a
                LEFT JOIN dbo.V_MA_Precios p
                    ON p.IdArticulo = a.IDARTICULO
                   AND p.IdLista = @IdLista
                   AND p.TipoLista = 'V'
                WHERE ISNULL(a.Suspendido, 0) <> 1
                  AND ISNULL(a.SuspendidoV, 0) <> 1
                  {wordFilters}
                ORDER BY a.DESCRIPCION, a.IDARTICULO;
                """;

            var rows = await cn.QueryAsync<ArticuloPrecioRow>(new CommandDefinition(sql, parameters, cancellationToken: token));

            var result = new List<CrmCotizacionArticuloDto>();
            foreach (var row in rows)
            {
                var bruto = pricing.UsaListas && row.PrecioLista > 0 ? row.PrecioLista : row.PrecioMaestro;
                var (neto, conIva) = SplitPrice(bruto, row.TasaIva, pricing.PreciosConIva);
                result.Add(new CrmCotizacionArticuloDto
                {
                    IdArticulo = row.IdArticulo,
                    Codigo = row.Codigo,
                    Descripcion = row.Descripcion,
                    TasaIva = decimal.Round(row.TasaIva, 4),
                    PrecioUnitarioNeto = neto,
                    PrecioUnitarioConIva = conIva
                });
            }

            return (IReadOnlyList<CrmCotizacionArticuloDto>)result;
        }, "No se pudieron cargar los artículos para la cotización.", ct);

    public Task<IReadOnlyList<CrmCotizacionDto>> GetByOportunidadAsync(long idOportunidad, CancellationToken ct = default)
        => ExecuteLoggedAsync("CotizacionesPorOportunidad", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = await cn.QueryAsync<CrmCotizacionDto>(new CommandDefinition($"""
                {HeaderSelectSql()}
                FROM dbo.CRM_COTIZACION c
                LEFT JOIN dbo.V_TA_Tecnicos t ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(c.IdTecnico))
                WHERE c.IdOportunidad = @IdOportunidad AND ISNULL(c.Baja, 0) = 0
                ORDER BY c.FechaHoraAlta DESC, c.IdCotizacion DESC;
                """, new { IdOportunidad = idOportunidad }, cancellationToken: token));
            return (IReadOnlyList<CrmCotizacionDto>)rows.AsList();
        }, "No se pudieron cargar las cotizaciones de la oportunidad.", ct);

    public Task<CrmCotizacionDetailDto?> GetByIdAsync(long idCotizacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("CotizacionById", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var header = await cn.QueryFirstOrDefaultAsync<CrmCotizacionDetailDto>(new CommandDefinition($"""
                {HeaderSelectSql()}
                FROM dbo.CRM_COTIZACION c
                LEFT JOIN dbo.V_TA_Tecnicos t ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(c.IdTecnico))
                WHERE c.IdCotizacion = @IdCotizacion AND ISNULL(c.Baja, 0) = 0;
                """, new { IdCotizacion = idCotizacion }, cancellationToken: token));
            if (header is null)
                return null;

            header.Lineas = (await cn.QueryAsync<CrmCotizacionLineDto>(new CommandDefinition("""
                SELECT IdDetalle, IdCotizacion, Orden, ISNULL(IdArticulo, '') AS IdArticulo, ISNULL(Descripcion, '') AS Descripcion,
                       Cantidad, PrecioUnitarioNeto, TasaIva, PrecioUnitarioConIva, SubtotalNeto, SubtotalIva, Subtotal
                FROM dbo.CRM_COTIZACION_DET
                WHERE IdCotizacion = @IdCotizacion
                ORDER BY Orden, IdDetalle;
                """, new { IdCotizacion = idCotizacion }, cancellationToken: token))).AsList();
            return header;
        }, "No se pudo cargar la cotización.", ct);

    public Task<long> SaveAsync(CrmCotizacionSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveCotizacion", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdOportunidad <= 0)
                throw new InvalidOperationException("La cotización debe pertenecer a una oportunidad.");

            var lineas = request.Lineas
                .Where(x => !string.IsNullOrWhiteSpace(x.Descripcion) && x.Cantidad != 0)
                .ToList();
            if (lineas.Count == 0)
                throw new InvalidOperationException("Agregá al menos un artículo a la cotización.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var pricing = await ResolvePricingContextInternalAsync(cn, request.ClienteCodigo, token, tx);
                var clienteNombre = string.IsNullOrWhiteSpace(request.ClienteNombre) ? pricing.ClienteNombre : request.ClienteNombre.Trim();
                var estado = NormalizeEstado(request.Estado);
                var unegocio = string.IsNullOrWhiteSpace(request.UNegocio) ? null : request.UNegocio.Trim();

                decimal totalNeto = 0m, totalIva = 0m, total = 0m;
                var computed = new List<CrmCotizacionLineDto>();
                foreach (var l in lineas)
                {
                    var tasa = decimal.Round(l.TasaIva, 4);
                    var (neto, conIva) = SplitPrice(l.PrecioUnitario, tasa, pricing.PreciosConIva);
                    var subNeto = decimal.Round(neto * l.Cantidad, 2);
                    var subConIva = decimal.Round(conIva * l.Cantidad, 2);
                    var subIva = decimal.Round(subConIva - subNeto, 2);
                    totalNeto += subNeto;
                    totalIva += subIva;
                    total += subConIva;
                    computed.Add(new CrmCotizacionLineDto
                    {
                        IdArticulo = string.IsNullOrWhiteSpace(l.IdArticulo) ? string.Empty : l.IdArticulo.Trim(),
                        Descripcion = l.Descripcion.Trim(),
                        Cantidad = l.Cantidad,
                        PrecioUnitarioNeto = neto,
                        TasaIva = tasa,
                        PrecioUnitarioConIva = conIva,
                        SubtotalNeto = subNeto,
                        SubtotalIva = subIva,
                        Subtotal = subConIva
                    });
                }

                long id = request.IdCotizacion;
                var header = new
                {
                    request.IdOportunidad,
                    UNegocio = unegocio,
                    ClienteCodigo = string.IsNullOrWhiteSpace(request.ClienteCodigo) ? pricing.ClienteCodigo : request.ClienteCodigo.Trim(),
                    ClienteNombre = clienteNombre,
                    pricing.EsConsumidorFinal,
                    pricing.IdLista,
                    pricing.ClasePrecio,
                    pricing.PreciosConIva,
                    IdTecnico = string.IsNullOrWhiteSpace(request.IdTecnico) ? null : request.IdTecnico.Trim(),
                    Fecha = request.Fecha?.Date ?? DateTime.Today,
                    request.FechaVencimiento,
                    Estado = estado,
                    Observaciones = string.IsNullOrWhiteSpace(request.Observaciones) ? null : request.Observaciones.Trim(),
                    TotalNeto = totalNeto,
                    TotalIva = totalIva,
                    Total = total,
                    Usuario = NormalizeUser(request.UsuarioAccion)
                };

                if (id <= 0)
                {
                    var numero = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                        "SELECT ISNULL(MAX(Numero), 0) + 1 FROM dbo.CRM_COTIZACION WITH (UPDLOCK, HOLDLOCK);",
                        transaction: tx, cancellationToken: token));
                    id = await cn.ExecuteScalarAsync<long>(new CommandDefinition("""
                        INSERT INTO dbo.CRM_COTIZACION
                        (Numero, TC, UNEGOCIO, IdOportunidad, ClienteCodigo, ClienteNombre, EsConsumidorFinal,
                         IdLista, ClasePrecio, PreciosConIva, IdTecnico, Fecha, FechaVencimiento, Estado, Observaciones,
                         TotalNeto, TotalIva, Total, UsuarioAlta, FechaHoraAlta)
                        OUTPUT INSERTED.IdCotizacion
                        VALUES
                        (@Numero, @TC, @UNegocio, @IdOportunidad, @ClienteCodigo, @ClienteNombre, @EsConsumidorFinal,
                         @IdLista, @ClasePrecio, @PreciosConIva, @IdTecnico, @Fecha, @FechaVencimiento, @Estado, @Observaciones,
                         @TotalNeto, @TotalIva, @Total, @Usuario, GETDATE());
                        """, new
                    {
                        Numero = numero,
                        TC = DefaultTc,
                        header.UNegocio,
                        header.IdOportunidad,
                        header.ClienteCodigo,
                        header.ClienteNombre,
                        header.EsConsumidorFinal,
                        header.IdLista,
                        header.ClasePrecio,
                        header.PreciosConIva,
                        header.IdTecnico,
                        header.Fecha,
                        header.FechaVencimiento,
                        header.Estado,
                        header.Observaciones,
                        header.TotalNeto,
                        header.TotalIva,
                        header.Total,
                        header.Usuario
                    }, tx, cancellationToken: token));

                    await AddActivityAsync(cn, tx, request.IdOportunidad, "COTIZACION", $"Cotización {DefaultTc}-{numero:00000000} creada.", request.UsuarioAccion, token);
                }
                else
                {
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.CRM_COTIZACION
                        SET UNEGOCIO = @UNegocio,
                            ClienteCodigo = @ClienteCodigo,
                            ClienteNombre = @ClienteNombre,
                            EsConsumidorFinal = @EsConsumidorFinal,
                            IdLista = @IdLista,
                            ClasePrecio = @ClasePrecio,
                            PreciosConIva = @PreciosConIva,
                            IdTecnico = @IdTecnico,
                            Fecha = @Fecha,
                            FechaVencimiento = @FechaVencimiento,
                            Estado = @Estado,
                            Observaciones = @Observaciones,
                            TotalNeto = @TotalNeto,
                            TotalIva = @TotalIva,
                            Total = @Total,
                            FechaHoraModificacion = GETDATE()
                        WHERE IdCotizacion = @IdCotizacion AND ISNULL(Baja, 0) = 0;
                        """, new
                    {
                        IdCotizacion = id,
                        header.UNegocio,
                        header.ClienteCodigo,
                        header.ClienteNombre,
                        header.EsConsumidorFinal,
                        header.IdLista,
                        header.ClasePrecio,
                        header.PreciosConIva,
                        header.IdTecnico,
                        header.Fecha,
                        header.FechaVencimiento,
                        header.Estado,
                        header.Observaciones,
                        header.TotalNeto,
                        header.TotalIva,
                        header.Total
                    }, tx, cancellationToken: token));

                    await cn.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM dbo.CRM_COTIZACION_DET WHERE IdCotizacion = @IdCotizacion;",
                        new { IdCotizacion = id }, tx, cancellationToken: token));

                    await AddActivityAsync(cn, tx, request.IdOportunidad, "COTIZACION", "Cotización actualizada.", request.UsuarioAccion, token);
                }

                var orden = 0;
                foreach (var l in computed)
                {
                    await cn.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO dbo.CRM_COTIZACION_DET
                        (IdCotizacion, Orden, IdArticulo, Descripcion, Cantidad, PrecioUnitarioNeto, TasaIva, PrecioUnitarioConIva, SubtotalNeto, SubtotalIva, Subtotal)
                        VALUES
                        (@IdCotizacion, @Orden, @IdArticulo, @Descripcion, @Cantidad, @PrecioUnitarioNeto, @TasaIva, @PrecioUnitarioConIva, @SubtotalNeto, @SubtotalIva, @Subtotal);
                        """, new
                    {
                        IdCotizacion = id,
                        Orden = orden++,
                        IdArticulo = string.IsNullOrWhiteSpace(l.IdArticulo) ? null : l.IdArticulo,
                        l.Descripcion,
                        l.Cantidad,
                        l.PrecioUnitarioNeto,
                        l.TasaIva,
                        l.PrecioUnitarioConIva,
                        l.SubtotalNeto,
                        l.SubtotalIva,
                        l.Subtotal
                    }, tx, cancellationToken: token));
                }

                await tx.CommitAsync(token);
                return id;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar la cotización.", ct);

    public Task ChangeEstadoAsync(long idCotizacion, string estado, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("ChangeEstadoCotizacion", async token =>
        {
            if (idCotizacion <= 0)
                throw new InvalidOperationException("Seleccioná una cotización válida.");
            var nuevo = NormalizeEstado(estado);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var idOportunidad = await cn.ExecuteScalarAsync<long?>(new CommandDefinition(
                    "SELECT IdOportunidad FROM dbo.CRM_COTIZACION WHERE IdCotizacion = @Id AND ISNULL(Baja, 0) = 0;",
                    new { Id = idCotizacion }, tx, cancellationToken: token));
                if (idOportunidad is null)
                    throw new InvalidOperationException("La cotización indicada no existe.");

                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.CRM_COTIZACION SET Estado = @Estado, FechaHoraModificacion = GETDATE() WHERE IdCotizacion = @Id;",
                    new { Id = idCotizacion, Estado = nuevo }, tx, cancellationToken: token));
                await AddActivityAsync(cn, tx, idOportunidad.Value, "COTIZACION", $"Cotización marcada como {nuevo}.", usuarioAccion, token);
                await tx.CommitAsync(token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo cambiar el estado de la cotización.", ct);

    public Task DeleteAsync(long idCotizacion, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("DeleteCotizacion", async token =>
        {
            if (idCotizacion <= 0)
                throw new InvalidOperationException("Seleccioná una cotización válida.");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.CRM_COTIZACION SET Baja = 1, FechaHoraModificacion = GETDATE() WHERE IdCotizacion = @Id;",
                new { Id = idCotizacion }, cancellationToken: token));
        }, "No se pudo eliminar la cotización.", ct);

    private static string HeaderSelectSql() => """
        SELECT
            c.IdCotizacion, c.Numero, ISNULL(c.TC, 'CTZ') AS TC, ISNULL(c.UNEGOCIO, '') AS UNegocio,
            c.IdOportunidad, ISNULL(c.ClienteCodigo, '') AS ClienteCodigo, ISNULL(c.ClienteNombre, '') AS ClienteNombre,
            c.EsConsumidorFinal, ISNULL(c.IdLista, '') AS IdLista, c.ClasePrecio, c.PreciosConIva,
            ISNULL(c.IdTecnico, '') AS IdTecnico, ISNULL(t.Nombre, '') AS TecnicoNombre,
            c.Fecha, c.FechaVencimiento, ISNULL(c.Estado, 'BORRADOR') AS Estado, ISNULL(c.Observaciones, '') AS Observaciones,
            c.TotalNeto, c.TotalIva, c.Total, ISNULL(c.UsuarioAlta, '') AS UsuarioAlta, c.FechaHoraAlta, c.FechaHoraModificacion
        """;

    private async Task<CrmCotizacionPricingContextDto> ResolvePricingContextInternalAsync(SqlConnection cn, string? clienteCodigo, CancellationToken ct, SqlTransaction? tx = null)
    {
        var usaListas = ParseBool(await ReadConfigAsync(cn, "UsaListasDePrecios", ct, tx));
        var maestroConIva = ParseBool(await ReadConfigAsync(cn, "MaestroArticuloConIVA", ct, tx));
        var fijaListasConIva = ParseBool(await ReadConfigAsync(cn, "FIJALISTASCONIVA", ct, tx));
        var fijaListasSinIva = ParseBool(await ReadConfigAsync(cn, "FIJALISTASSINIVA", ct, tx));
        var claseDefaultRaw = await ReadConfigAsync(cn, "CLASEPRECIOVENTA", ct, tx);
        if (string.IsNullOrWhiteSpace(claseDefaultRaw))
            claseDefaultRaw = await ReadConfigAsync(cn, "ClaseDePrecioDefault", ct, tx);
        var claseDefault = int.TryParse(claseDefaultRaw, out var cd) && cd is >= 1 and <= 8 ? cd : 1;

        var codigo = (clienteCodigo ?? string.Empty).Trim();
        var esConsumidorFinal = false;
        if (string.IsNullOrWhiteSpace(codigo))
        {
            codigo = (await ReadConfigAsync(cn, "CUENTACONSUMIDORFINAL", ct, tx)).Trim();
            esConsumidorFinal = true;
        }

        var cliente = string.IsNullOrWhiteSpace(codigo)
            ? null
            : await cn.QueryFirstOrDefaultAsync<ClienteListaRow>(new CommandDefinition("""
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(IdLista)), '') AS IdLista,
                    ISNULL(Clase, 0) AS Clase,
                    ISNULL(RAZON_SOCIAL, '') AS Nombre
                FROM dbo.Vt_Clientes
                WHERE LTRIM(RTRIM(Codigo)) = @Codigo;
                """, new { Codigo = codigo }, tx, cancellationToken: ct));

        var idLista = cliente?.IdLista?.Trim() ?? string.Empty;
        var clase = cliente is { Clase: >= 1 and <= 8 } ? cliente.Clase : claseDefault;
        var usaLista = usaListas && !string.IsNullOrWhiteSpace(idLista);
        // Listas: si FIJALISTASCONIVA está prendido, los precios de lista ya incluyen IVA
        // (FIJALISTASSINIVA es el flag opuesto, que deja el default en false). Maestro: MaestroArticuloConIVA.
        _ = fijaListasSinIva;
        var preciosConIva = usaLista ? fijaListasConIva : maestroConIva;

        return new CrmCotizacionPricingContextDto
        {
            ClienteCodigo = codigo,
            ClienteNombre = cliente?.Nombre?.Trim() ?? string.Empty,
            EsConsumidorFinal = esConsumidorFinal,
            IdLista = idLista,
            ClasePrecio = clase,
            PreciosConIva = preciosConIva,
            UsaListas = usaLista
        };
    }

    private static (decimal neto, decimal conIva) SplitPrice(decimal bruto, decimal tasa, bool preciosConIva)
    {
        var factor = 1m + tasa / 100m;
        if (preciosConIva)
        {
            var neto = factor == 0 ? bruto : decimal.Round(bruto / factor, 4);
            return (neto, decimal.Round(bruto, 4));
        }
        return (decimal.Round(bruto, 4), decimal.Round(bruto * factor, 4));
    }

    private async Task<string> ReadConfigAsync(SqlConnection cn, string clave, CancellationToken ct, SqlTransaction? tx = null)
    {
        var value = await cn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition("""
            SELECT TOP (1)
                CASE
                    WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR))
                    WHEN ISNULL(CAST(ValorAux AS nvarchar(150)), '') <> '' THEN LTRIM(RTRIM(CAST(ValorAux AS nvarchar(150))))
                    ELSE ''
                END
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """, new { Clave = clave.ToUpperInvariant() }, tx, cancellationToken: ct));
        return value ?? string.Empty;
    }

    private static bool ParseBool(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();
        return v is "1" or "S" or "SI" or "SÍ" or "TRUE" or "T" or "Y";
    }

    private static string NormalizeEstado(string? estado)
    {
        var v = (estado ?? string.Empty).Trim().ToUpperInvariant();
        return v switch
        {
            CrmCotizacionEstados.Enviada => CrmCotizacionEstados.Enviada,
            CrmCotizacionEstados.Aceptada => CrmCotizacionEstados.Aceptada,
            CrmCotizacionEstados.Rechazada => CrmCotizacionEstados.Rechazada,
            CrmCotizacionEstados.Vencida => CrmCotizacionEstados.Vencida,
            _ => CrmCotizacionEstados.Borrador
        };
    }

    private static string NormalizeUser(string? user)
        => string.IsNullOrWhiteSpace(user) ? "web" : user.Trim();

    private static Task AddActivityAsync(SqlConnection cn, SqlTransaction tx, long idOportunidad, string tipo, string descripcion, string? usuario, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.CRM_ACTIVIDAD (IdOportunidad, TipoActividad, Descripcion, Usuario, FechaHora)
            VALUES (@IdOportunidad, @Tipo, @Descripcion, @Usuario, GETDATE());
            """, new { IdOportunidad = idOportunidad, Tipo = tipo, Descripcion = descripcion, Usuario = NormalizeUser(usuario) }, tx, cancellationToken: ct));

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

    private sealed class ArticuloPrecioRow
    {
        public string IdArticulo { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal TasaIva { get; set; }
        public decimal PrecioMaestro { get; set; }
        public decimal PrecioLista { get; set; }
    }

    private sealed class ClienteListaRow
    {
        public string IdLista { get; set; } = string.Empty;
        public int Clase { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }
}

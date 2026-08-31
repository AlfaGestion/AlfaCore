using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class CrmCotizacionService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IHttpClientFactory httpClientFactory,
    ICentralBasesService centralBasesService) : ICrmCotizacionService
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
            return await SearchArticulosCoreAsync(cn, pricing, texto, take, token);
        }, "No se pudieron cargar los artículos para la cotización.", ct);

    private static async Task<IReadOnlyList<CrmCotizacionArticuloDto>> SearchArticulosCoreAsync(
        SqlConnection cn, CrmCotizacionPricingContextDto pricing, string texto, int take, CancellationToken token)
    {
        var clase = Math.Clamp(pricing.ClasePrecio, 1, 8);
        var limit = Math.Clamp(take, 1, 100);

        var palabras = (texto ?? string.Empty).ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var wordFilters = new StringBuilder();
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

        return result;
    }

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

            var tipo = NormalizeTipo(request.Tipo);
            var cuerpoServicio = string.IsNullOrWhiteSpace(request.CuerpoServicio) ? null : request.CuerpoServicio.Trim();
            var lineas = request.Lineas
                .Where(x => !string.IsNullOrWhiteSpace(x.Descripcion) && x.Cantidad != 0)
                .ToList();
            if (tipo == CrmCotizacionTipos.Articulos && lineas.Count == 0)
                throw new InvalidOperationException("Agregá al menos un artículo a la cotización.");
            if (tipo == CrmCotizacionTipos.Servicio && lineas.Count == 0 && string.IsNullOrWhiteSpace(cuerpoServicio))
                throw new InvalidOperationException("Escribí la propuesta de servicio o agregá al menos una línea.");

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
                    Tipo = tipo,
                    CuerpoServicio = cuerpoServicio,
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
                        (Numero, TC, Tipo, CuerpoServicio, UNEGOCIO, IdOportunidad, ClienteCodigo, ClienteNombre, EsConsumidorFinal,
                         IdLista, ClasePrecio, PreciosConIva, IdTecnico, Fecha, FechaVencimiento, Estado, Observaciones,
                         TotalNeto, TotalIva, Total, UsuarioAlta, FechaHoraAlta)
                        OUTPUT INSERTED.IdCotizacion
                        VALUES
                        (@Numero, @TC, @Tipo, @CuerpoServicio, @UNegocio, @IdOportunidad, @ClienteCodigo, @ClienteNombre, @EsConsumidorFinal,
                         @IdLista, @ClasePrecio, @PreciosConIva, @IdTecnico, @Fecha, @FechaVencimiento, @Estado, @Observaciones,
                         @TotalNeto, @TotalIva, @Total, @Usuario, GETDATE());
                        """, new
                    {
                        Numero = numero,
                        TC = DefaultTc,
                        header.Tipo,
                        header.CuerpoServicio,
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
                        SET Tipo = @Tipo,
                            CuerpoServicio = @CuerpoServicio,
                            UNEGOCIO = @UNegocio,
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
                        header.Tipo,
                        header.CuerpoServicio,
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

    public Task<CrmCotizacionShareDto> EnsureShareAsync(long idCotizacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("EnsureShareCotizacion", async token =>
        {
            if (idCotizacion <= 0)
                throw new InvalidOperationException("Seleccioná una cotización válida.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var actual = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT PublicToken FROM dbo.CRM_COTIZACION WHERE IdCotizacion = @Id AND ISNULL(Baja, 0) = 0;",
                new { Id = idCotizacion }, cancellationToken: token));
            if (actual is null)
                throw new InvalidOperationException("La cotización indicada no existe.");

            var tk = actual.Trim();
            if (string.IsNullOrWhiteSpace(tk))
            {
                tk = Guid.NewGuid().ToString("N");
                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.CRM_COTIZACION SET PublicToken = @Token WHERE IdCotizacion = @Id;",
                    new { Id = idCotizacion, Token = tk }, cancellationToken: token));
            }

            return new CrmCotizacionShareDto
            {
                IdCotizacion = idCotizacion,
                IdBase = sessionService.GetActiveSession()?.BaseId ?? 0,
                Token = tk
            };
        }, "No se pudo preparar el enlace de la cotización.", ct);

    public Task SendByEmailAsync(long idCotizacion, string destinatario, string? publicUrl = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("SendCotizacionByEmail", async token =>
        {
            var to = (destinatario ?? string.Empty).Trim();
            if (to.Length == 0)
                throw new InvalidOperationException("Ingresá un email de destino.");
            _ = new MailAddress(to);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var detail = await LoadDetailAsync(cn, "c.IdCotizacion = @Key", new { Key = idCotizacion }, token)
                         ?? throw new InvalidOperationException("La cotización indicada no existe.");
            var empresa = await LoadEmpresaAsync(cn, token);
            var mail = await ResolveMailConfigAsync(cn, token);
            var html = BuildCotizacionHtml(detail, empresa, publicUrl);

            using var message = new MailMessage
            {
                From = new MailAddress(mail.From),
                Subject = $"Cotización {detail.CodigoVisible} - {empresa.Nombre}",
                Body = html,
                IsBodyHtml = true
            };
            message.To.Add(to);

            using var client = new SmtpClient(mail.Server, mail.Port)
            {
                EnableSsl = mail.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(mail.From, mail.Password)
            };
            await client.SendMailAsync(message, token);

            if (detail.Estado == CrmCotizacionEstados.Borrador)
                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.CRM_COTIZACION SET Estado = @Estado, FechaHoraModificacion = GETDATE() WHERE IdCotizacion = @Id AND Estado = 'BORRADOR';",
                    new { Id = idCotizacion, Estado = CrmCotizacionEstados.Enviada }, cancellationToken: token));
            await AddActivityAsync(cn, null, detail.IdOportunidad, "COTIZACION", $"Cotización {detail.CodigoVisible} enviada por email a {to}.", null, token);
            return true;
        }, "No se pudo enviar la cotización por email.", ct);

    public Task<string?> RenderPublicHtmlAsync(int idBase, string token, CancellationToken ct = default)
        => ExecuteLoggedAsync("RenderPublicCotizacion", async innerCt =>
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

            var detail = await LoadDetailAsync(cn, "c.PublicToken = @Key", new { Key = tk }, innerCt);
            if (detail is null)
                return null;
            var empresa = await LoadEmpresaAsync(cn, innerCt);
            return BuildCotizacionHtml(detail, empresa, null, standalone: true);
        }, "No se pudo mostrar la cotización.", ct);

    private static async Task<CrmCotizacionDetailDto?> LoadDetailAsync(SqlConnection cn, string whereClause, object args, CancellationToken ct)
    {
        var header = await cn.QueryFirstOrDefaultAsync<CrmCotizacionDetailDto>(new CommandDefinition($"""
            {HeaderSelectSql()}
            FROM dbo.CRM_COTIZACION c
            LEFT JOIN dbo.V_TA_Tecnicos t ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(c.IdTecnico))
            WHERE {whereClause} AND ISNULL(c.Baja, 0) = 0;
            """, args, cancellationToken: ct));
        if (header is null)
            return null;

        header.Lineas = (await cn.QueryAsync<CrmCotizacionLineDto>(new CommandDefinition("""
            SELECT IdDetalle, IdCotizacion, Orden, ISNULL(IdArticulo, '') AS IdArticulo, ISNULL(Descripcion, '') AS Descripcion,
                   Cantidad, PrecioUnitarioNeto, TasaIva, PrecioUnitarioConIva, SubtotalNeto, SubtotalIva, Subtotal
            FROM dbo.CRM_COTIZACION_DET
            WHERE IdCotizacion = @Id
            ORDER BY Orden, IdDetalle;
            """, new { Id = header.IdCotizacion }, cancellationToken: ct))).AsList();
        return header;
    }

    private async Task<EmpresaInfo> LoadEmpresaAsync(SqlConnection cn, CancellationToken ct)
    {
        var nombre = await ReadConfigAsync(cn, "Nombre", ct);
        var calle = await ReadConfigAsync(cn, "Calle", ct);
        var localidad = await ReadConfigAsync(cn, "Localidad", ct);
        var cuit = await ReadConfigAsync(cn, "CUIT", ct);
        var telefono = await ReadConfigAsync(cn, "Telefono", ct);
        return new EmpresaInfo(
            string.IsNullOrWhiteSpace(nombre) ? "Cotización" : nombre.Trim(),
            calle.Trim(),
            localidad.Trim(),
            cuit.Trim(),
            telefono.Trim());
    }

    private async Task<MailInfo> ResolveMailConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var server = ConfigOrAppSetting(await ReadConfigAsync(cn, "EMAIL_SERVER", ct), "EMAIL_SERVER");
        var port = ConfigOrAppSetting(await ReadConfigAsync(cn, "EMAIL_PORT", ct), "EMAIL_PORT");
        var account = ConfigOrAppSetting(await ReadConfigAsync(cn, "EMAIL_CTA", ct), "EMAIL_CTA");
        var password = ConfigOrAppSetting(await ReadConfigAsync(cn, "EMAIL_PASS", ct), "EMAIL_PASS");
        var ssl = ConfigOrAppSetting(await ReadConfigAsync(cn, "EMAIL_SSL", ct), "EMAIL_SSL");

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(port)
            || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Falta configurar el correo saliente. Revisá EMAIL_SERVER, EMAIL_PORT, EMAIL_CTA y EMAIL_PASS en Configuración.");
        if (!int.TryParse(port, out var smtpPort) || smtpPort <= 0)
            throw new InvalidOperationException("EMAIL_PORT no tiene un valor válido.");

        var enableSsl = ssl.Equals("SI", StringComparison.OrdinalIgnoreCase)
            || ssl.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || ssl.Equals("1", StringComparison.OrdinalIgnoreCase);
        return new MailInfo(server, smtpPort, account, password, enableSsl);
    }

    private string ConfigOrAppSetting(string dbValue, string key)
        => !string.IsNullOrWhiteSpace(dbValue) ? dbValue.Trim() : (configuration[key] ?? configuration[$"PuntoVenta:{key}"] ?? string.Empty);

    private static string BuildCotizacionHtml(CrmCotizacionDetailDto d, EmpresaInfo empresa, string? publicUrl, bool standalone = false)
    {
        var ar = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
        string Money(decimal v) => v.ToString("C0", ar);
        string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        var sb = new StringBuilder();
        if (standalone)
            sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Cotización ").Append(E(d.CodigoVisible)).Append("</title></head><body style=\"margin:0;background:#f1f5f9;\">");

        sb.Append("<div style=\"max-width:720px;margin:0 auto;padding:24px;font-family:Segoe UI,Arial,sans-serif;color:#0f172a;background:#ffffff;\">");
        sb.Append("<div style=\"display:flex;justify-content:space-between;gap:16px;border-bottom:2px solid #2563eb;padding-bottom:12px;margin-bottom:16px;\">");
        sb.Append("<div><h1 style=\"margin:0;font-size:20px;\">").Append(E(empresa.Nombre)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(empresa.Direccion) || !string.IsNullOrWhiteSpace(empresa.Localidad))
            sb.Append("<div style=\"font-size:12px;color:#64748b;\">").Append(E(string.Join(" · ", new[] { empresa.Direccion, empresa.Localidad }.Where(x => !string.IsNullOrWhiteSpace(x))))).Append("</div>");
        if (!string.IsNullOrWhiteSpace(empresa.Cuit)) sb.Append("<div style=\"font-size:12px;color:#64748b;\">CUIT: ").Append(E(empresa.Cuit)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(empresa.Telefono)) sb.Append("<div style=\"font-size:12px;color:#64748b;\">Tel: ").Append(E(empresa.Telefono)).Append("</div>");
        sb.Append("</div>");
        sb.Append("<div style=\"text-align:right;\"><div style=\"font-size:16px;font-weight:700;\">Cotización</div><div style=\"font-size:14px;\">").Append(E(d.CodigoVisible)).Append("</div>");
        sb.Append("<div style=\"font-size:12px;color:#64748b;\">").Append(d.Fecha.ToString("dd/MM/yyyy", ar)).Append("</div>");
        if (d.FechaVencimiento is { } v) sb.Append("<div style=\"font-size:12px;color:#64748b;\">Válida hasta ").Append(v.ToString("dd/MM/yyyy", ar)).Append("</div>");
        sb.Append("</div></div>");

        if (!string.IsNullOrWhiteSpace(d.ClienteNombre) || !string.IsNullOrWhiteSpace(d.ClienteCodigo))
            sb.Append("<div style=\"margin-bottom:12px;font-size:14px;\"><strong>Cliente:</strong> ").Append(E(string.IsNullOrWhiteSpace(d.ClienteNombre) ? d.ClienteCodigo : d.ClienteNombre)).Append("</div>");

        if (d.EsServicio && !string.IsNullOrWhiteSpace(d.CuerpoServicio))
            sb.Append("<div style=\"font-size:14px;line-height:1.5;margin-bottom:16px;\">").Append(d.CuerpoServicio).Append("</div>");

        if (d.Lineas.Count > 0)
        {
            sb.Append("<table style=\"width:100%;border-collapse:collapse;font-size:13px;margin-bottom:12px;\">");
            sb.Append("<thead><tr style=\"background:#f1f5f9;\">");
            sb.Append("<th style=\"text-align:left;padding:6px 8px;border-bottom:1px solid #e2e8f0;\">Detalle</th>");
            sb.Append("<th style=\"text-align:right;padding:6px 8px;border-bottom:1px solid #e2e8f0;\">Cant.</th>");
            sb.Append("<th style=\"text-align:right;padding:6px 8px;border-bottom:1px solid #e2e8f0;\">P. unit.</th>");
            sb.Append("<th style=\"text-align:right;padding:6px 8px;border-bottom:1px solid #e2e8f0;\">Subtotal</th></tr></thead><tbody>");
            foreach (var l in d.Lineas)
            {
                sb.Append("<tr>");
                sb.Append("<td style=\"padding:6px 8px;border-bottom:1px solid #f1f5f9;\">").Append(E(l.Descripcion)).Append("</td>");
                sb.Append("<td style=\"padding:6px 8px;border-bottom:1px solid #f1f5f9;text-align:right;\">").Append(l.Cantidad.ToString("0.##", ar)).Append("</td>");
                sb.Append("<td style=\"padding:6px 8px;border-bottom:1px solid #f1f5f9;text-align:right;\">").Append(Money(l.PrecioUnitarioConIva)).Append("</td>");
                sb.Append("<td style=\"padding:6px 8px;border-bottom:1px solid #f1f5f9;text-align:right;\">").Append(Money(l.Subtotal)).Append("</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("<div style=\"text-align:right;font-size:14px;\">");
        sb.Append("<div>Neto: <strong>").Append(Money(d.TotalNeto)).Append("</strong></div>");
        sb.Append("<div>IVA: <strong>").Append(Money(d.TotalIva)).Append("</strong></div>");
        sb.Append("<div style=\"font-size:18px;margin-top:4px;\">Total: <strong>").Append(Money(d.Total)).Append("</strong></div>");
        sb.Append("</div>");

        if (!string.IsNullOrWhiteSpace(d.Observaciones))
            sb.Append("<div style=\"margin-top:16px;font-size:12px;color:#64748b;\">").Append(E(d.Observaciones)).Append("</div>");

        if (!string.IsNullOrWhiteSpace(publicUrl))
            sb.Append("<div style=\"margin-top:20px;\"><a href=\"").Append(E(publicUrl)).Append("\" style=\"display:inline-block;background:#2563eb;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none;font-size:14px;\">Ver cotización online</a></div>");

        sb.Append("</div>");
        if (standalone)
            sb.Append("</body></html>");
        return sb.ToString();
    }

    private sealed record EmpresaInfo(string Nombre, string Direccion, string Localidad, string Cuit, string Telefono);
    private sealed record MailInfo(string Server, int Port, string From, string Password, bool EnableSsl);

    public Task<string> GenerateServiceProposalAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("GenerateServiceProposal", async token =>
        {
            var pedido = (prompt ?? string.Empty).Trim();
            if (pedido.Length == 0)
                throw new InvalidOperationException("Escribí qué querés cotizar para que el asistente arme la propuesta.");

            var cliente = string.IsNullOrWhiteSpace(clienteNombre) ? "el cliente" : clienteNombre.Trim();
            var messages = new object[]
            {
                new
                {
                    role = "system",
                    content = """
                        Sos un asistente comercial que redacta propuestas de cotización de servicios,
                        profesionales, claras y persuasivas, en español rioplatense (voseo).
                        Devolvés SOLO HTML simple para insertar en un editor: usá <h3>, <p>, <ul>, <li>,
                        <strong> y <em>. No uses <html>, <head>, <body>, <script> ni estilos inline.
                        Estructura sugerida: título, breve introducción, alcance/qué incluye (lista),
                        y cierre. NO inventes precios ni importes: si hacen falta, dejá un marcador
                        como <strong>[completar importe]</strong>.
                        """
                },
                new
                {
                    role = "user",
                    content = $"Redactá una propuesta de servicio para {cliente}. Pedido: {pedido}"
                }
            };

            var texto = await CallOpenAiChatAsync(messages, 0.5, token);
            return string.IsNullOrWhiteSpace(texto) ? string.Empty : texto.Trim();
        }, "No se pudo generar la propuesta con el asistente de IA.", ct);

    public Task<IReadOnlyList<CrmCotizacionAiLineaSugeridaDto>> SuggestLinesFromPromptAsync(string? clienteCodigo, string prompt, CancellationToken ct = default)
        => ExecuteLoggedAsync("SuggestLinesFromPrompt", async token =>
        {
            var pedido = (prompt ?? string.Empty).Trim();
            if (pedido.Length == 0)
                throw new InvalidOperationException("Escribí qué querés cotizar para que el asistente busque los artículos.");

            var messages = new object[]
            {
                new
                {
                    role = "system",
                    content = """
                        Interpretás un pedido comercial y extraés los ítems a cotizar contra un catálogo
                        de artículos. Devolvés SOLO JSON válido con esta forma exacta:
                        {"items":[{"buscar":"palabra clave para buscar en el catálogo","cantidad":number}]}.
                        'buscar' debe ser un término corto y genérico (ej. "harina", "aceite 900"), NO el
                        pedido entero. Si el pedido menciona varias categorías, generá un item por cada una.
                        Si no se indica cantidad, usá 1. NO inventes códigos, artículos ni precios.
                        """
                },
                new { role = "user", content = pedido }
            };

            var json = await CallOpenAiChatAsync(messages, 0.2, token);
            var intents = ParseIntents(json);
            if (intents.Count == 0)
                return (IReadOnlyList<CrmCotizacionAiLineaSugeridaDto>)Array.Empty<CrmCotizacionAiLineaSugeridaDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var pricing = await ResolvePricingContextInternalAsync(cn, clienteCodigo, token);

            var sugerencias = new List<CrmCotizacionAiLineaSugeridaDto>();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var intent in intents.Take(10))
            {
                if (string.IsNullOrWhiteSpace(intent.Buscar))
                    continue;
                var articulos = await SearchArticulosCoreAsync(cn, pricing, intent.Buscar, 15, token);
                foreach (var art in articulos)
                {
                    if (!vistos.Add(art.IdArticulo))
                        continue;
                    sugerencias.Add(new CrmCotizacionAiLineaSugeridaDto
                    {
                        Termino = intent.Buscar,
                        IdArticulo = art.IdArticulo,
                        Codigo = art.Codigo,
                        Descripcion = art.Descripcion,
                        PrecioUnitarioNeto = art.PrecioUnitarioNeto,
                        PrecioUnitarioConIva = art.PrecioUnitarioConIva,
                        TasaIva = art.TasaIva,
                        Cantidad = intent.Cantidad > 0 ? intent.Cantidad : 1m
                    });
                    if (sugerencias.Count >= 80)
                        break;
                }
                if (sugerencias.Count >= 80)
                    break;
            }

            return (IReadOnlyList<CrmCotizacionAiLineaSugeridaDto>)sugerencias;
        }, "No se pudieron sugerir artículos con el asistente de IA.", ct);

    private async Task<string> CallOpenAiChatAsync(object[] messages, double temperature, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("El asistente de IA no está configurado (falta OPENAI_API_KEY en el servidor).");

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini";

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(40);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = BuildOpenAiChatPayload(model, messages, temperature);
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("El asistente de IA no respondió en este momento. Probá de nuevo en unos segundos.");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private static Dictionary<string, object?> BuildOpenAiChatPayload(string model, object[] messages, double? temperature = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages
        };

        if (temperature.HasValue && SupportsCustomTemperature(model))
            payload["temperature"] = temperature.Value;

        return payload;
    }

    private static bool SupportsCustomTemperature(string? model)
        => string.IsNullOrWhiteSpace(model)
           || !model.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase);

    private static List<(string Buscar, decimal Cantidad)> ParseIntents(string json)
    {
        var result = new List<(string, decimal)>();
        if (string.IsNullOrWhiteSpace(json))
            return result;

        // El modelo a veces envuelve el JSON en ```json ... ```; recortamos al primer objeto.
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
            return result;
        var slice = json.Substring(start, end - start + 1);

        try
        {
            using var doc = JsonDocument.Parse(slice);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var item in items.EnumerateArray())
            {
                var buscar = item.TryGetProperty("buscar", out var b) ? b.GetString()?.Trim() ?? string.Empty : string.Empty;
                decimal cantidad = 1m;
                if (item.TryGetProperty("cantidad", out var c))
                {
                    if (c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out var cd))
                        cantidad = cd;
                    else if (c.ValueKind == JsonValueKind.String && decimal.TryParse(c.GetString(), out var cs))
                        cantidad = cs;
                }
                if (!string.IsNullOrWhiteSpace(buscar))
                    result.Add((buscar, cantidad));
            }
        }
        catch (JsonException)
        {
            // Si el modelo no devolvió JSON válido, no sugerimos nada (el usuario puede reintentar).
        }
        return result;
    }

    private static string HeaderSelectSql() => """
        SELECT
            c.IdCotizacion, c.Numero, ISNULL(c.TC, 'CTZ') AS TC,
            ISNULL(c.Tipo, 'ARTICULOS') AS Tipo, ISNULL(c.CuerpoServicio, '') AS CuerpoServicio,
            ISNULL(c.UNEGOCIO, '') AS UNegocio,
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

    private static string NormalizeTipo(string? tipo)
        => string.Equals((tipo ?? string.Empty).Trim(), CrmCotizacionTipos.Servicio, StringComparison.OrdinalIgnoreCase)
            ? CrmCotizacionTipos.Servicio
            : CrmCotizacionTipos.Articulos;

    private static string NormalizeUser(string? user)
        => string.IsNullOrWhiteSpace(user) ? "web" : user.Trim();

    private static Task AddActivityAsync(SqlConnection cn, SqlTransaction? tx, long idOportunidad, string tipo, string descripcion, string? usuario, CancellationToken ct)
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

using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace AlfaCore.Services;

public sealed class PuntoVentaService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents,
    IWebHostEnvironment env) : IPuntoVentaService
{
    private const string ModuleName = "PuntoVenta";
    private const string DefaultTc = "FC";
    private const string DefaultCaja = "1";
    private const string DefaultSucursal = "0001";
    private const string DefaultClasePrecio = "1";
    private const string ConfigGroup = "PUNTOVENTA";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PuntoVentaContextDto> GetContextAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetContext", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "TA_USUARIOS", token))
                throw new InvalidOperationException("La base activa no tiene TA_USUARIOS. No se puede inicializar el POS.");

            var currentUser = appUserSession.GetCurrentUserName(Environment.UserName).Trim();
            if (string.IsNullOrWhiteSpace(currentUser))
                throw new InvalidOperationException("No se pudo resolver el usuario actual para inicializar el POS.");

            var currentSystem = appUserSession.CurrentUser?.SystemCode?.Trim() ?? string.Empty;
            var hasAdministrador = await ColumnExistsAsync(cn, "TA_USUARIOS", "Administrador", token);

            var userRow = await cn.QuerySingleOrDefaultAsync<UserCajaRow>(new CommandDefinition(
                $"""
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS UserName,
                    ISNULL(LTRIM(RTRIM(IDCAJA)), '') AS IdCaja,
                    {(hasAdministrador ? "CASE WHEN ISNULL(Administrador, 0) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END" : "CAST(0 AS bit)")} AS Administrador
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(NOMBRE))) = @UserName
                  AND (@SystemCode = '' OR UPPER(LTRIM(RTRIM(SISTEMA))) = @SystemCode);
                """,
                new
                {
                    UserName = currentUser.ToUpperInvariant(),
                    SystemCode = currentSystem.ToUpperInvariant()
                },
                cancellationToken: token));

            if (userRow is null)
                throw new InvalidOperationException("El usuario actual no existe en TA_USUARIOS de la base activa.");

            var cajaActual = string.IsNullOrWhiteSpace(userRow.IdCaja) ? DefaultCaja : userRow.IdCaja.Trim();
            var usaCajaDefault = string.IsNullOrWhiteSpace(userRow.IdCaja);

            var tcConfig = await GetTipoComprobanteConfigAsync(cn, token);
            var sucursal = ResolveSucursalDefault(tcConfig);

            return new PuntoVentaContextDto
            {
                UsuarioActual = userRow.UserName,
                SistemaActual = currentSystem,
                SesionSqlActiva = sessionService.GetActiveSession()?.Nombre ?? string.Empty,
                CajaActual = cajaActual,
                EsAdministrador = userRow.Administrador,
                TipoComprobanteDefault = DefaultTc,
                SucursalDefault = sucursal.Code,
                LetrasDisponibles = tcConfig?.Letras?.Trim() ?? string.Empty,
                FuenteSucursal = sucursal.Source,
                UsaCajaDefault = usaCajaDefault
            };
        }, "No se pudo cargar el contexto inicial del punto de venta.", ct);

    public Task<PuntoVentaSettingsDto> GetSettingsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetSettings", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            return new PuntoVentaSettingsDto
            {
                ClasePrecioDefault = await TryReadConfigValueAsync(cn, "CLASEDEPRECIODEFAULT", token) is { Length: > 0 } clase
                    ? clase
                    : DefaultClasePrecio,
                VerificadorRutaImagenes = await TryReadConfigValueAsync(cn, "VERIFICADOR_RUTAIMAGENES", token),
                CuentaConsumidorFinal = await TryReadConfigValueAsync(cn, "CUENTACONSUMIDORFINAL", token),
                RutaImagenesLegacy = await TryReadConfigValueAsync(cn, "RUTAIMAGENES", token),
                EmailServer = await TryReadConfigValueAsync(cn, "EMAIL_SERVER", token),
                EmailPort = await TryReadConfigValueAsync(cn, "EMAIL_PORT", token),
                EmailCuenta = await TryReadConfigValueAsync(cn, "EMAIL_CTA", token),
                EmailPassword = await TryReadConfigValueAsync(cn, "EMAIL_PASS", token),
                EmailSsl = await TryReadConfigValueAsync(cn, "EMAIL_SSL", token)
            };
        }, "No se pudo cargar la configuración del punto de venta.", ct);

    public Task SaveSettingsAsync(PuntoVentaSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveSettings", async token =>
        {
            ArgumentNullException.ThrowIfNull(settings);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "TA_CONFIGURACION", token))
                throw new InvalidOperationException("La base activa no tiene TA_CONFIGURACION. No se puede guardar la configuración del POS.");

            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "CLASEDEPRECIODEFAULT", NormalizeClasePrecio(settings.ClasePrecioDefault), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "VERIFICADOR_RUTAIMAGENES", settings.VerificadorRutaImagenes.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "CUENTACONSUMIDORFINAL", settings.CuentaConsumidorFinal.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_SERVER", settings.EmailServer.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_PORT", settings.EmailPort.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_CTA", settings.EmailCuenta.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_PASS", settings.EmailPassword.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_SSL", settings.EmailSsl.Trim(), token);

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "SaveSettings",
                "TA_CONFIGURACION",
                ConfigGroup,
                "Configuración del punto de venta actualizada.",
                new
                {
                    ClasePrecioDefault = NormalizeClasePrecio(settings.ClasePrecioDefault),
                    VerificadorRutaImagenes = settings.VerificadorRutaImagenes.Trim(),
                    CuentaConsumidorFinal = settings.CuentaConsumidorFinal.Trim(),
                    EmailServer = settings.EmailServer.Trim(),
                    EmailPort = settings.EmailPort.Trim(),
                    EmailCuenta = settings.EmailCuenta.Trim(),
                    EmailSsl = settings.EmailSsl.Trim()
                },
                token);

            return true;
        }, "No se pudo guardar la configuración del punto de venta.", ct);

    public Task<IReadOnlyList<PuntoVentaPaymentMethodDto>> GetPaymentMethodsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPaymentMethods", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "MA_CUENTAS", token))
                return [];

            var rows = (await cn.QueryAsync<PuntoVentaPaymentMethodDto>(new CommandDefinition(
                """
                SELECT
                    LTRIM(RTRIM(CODIGO)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(DESCRIPCION)), '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(MEDIODEPAGO)), '') AS MedioDePago,
                    ISNULL(LTRIM(RTRIM(MONEDA)), '') AS Moneda
                FROM dbo.MA_CUENTAS
                WHERE ISNULL(LTRIM(RTRIM(MEDIODEPAGO)), '') <> ''
                ORDER BY DESCRIPCION, CODIGO;
                """,
                cancellationToken: token))).ToList();

            if (rows.Count > 0)
                return (IReadOnlyList<PuntoVentaPaymentMethodDto>)rows;

            if (!await TableExistsAsync(cn, "TA_CONFIGURACION", token))
                return [];

            rows = (await cn.QueryAsync<PuntoVentaPaymentMethodDto>(new CommandDefinition(
                """
                SELECT TOP (1)
                    LTRIM(RTRIM(b.CODIGO)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(b.DESCRIPCION)), '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(b.MEDIODEPAGO)), '') AS MedioDePago,
                    ISNULL(LTRIM(RTRIM(b.MONEDA)), '') AS Moneda
                FROM dbo.TA_CONFIGURACION a
                INNER JOIN dbo.MA_CUENTAS b
                    ON LTRIM(RTRIM(a.VALOR)) = LTRIM(RTRIM(b.CODIGO))
                WHERE UPPER(LTRIM(RTRIM(a.CLAVE))) = 'CUENTA_CAJA';
                """,
                cancellationToken: token))).ToList();

            return (IReadOnlyList<PuntoVentaPaymentMethodDto>)rows;
        }, "No se pudieron cargar los medios de pago del punto de venta.", ct);

    public Task<IReadOnlyList<PuntoVentaFamilyDto>> GetFamiliasAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetFamilias", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "V_TA_FAMILIAS", token))
                return [];

            var rows = await cn.QueryAsync<PuntoVentaFamilyDto>(new CommandDefinition(
                """
                SELECT
                    LTRIM(RTRIM(IdFamilia)) AS IdFamilia,
                    ISNULL(LTRIM(RTRIM(Descripcion)), '') AS Descripcion
                FROM dbo.V_TA_FAMILIAS
                ORDER BY Descripcion, IdFamilia;
                """,
                cancellationToken: token));

            return (IReadOnlyList<PuntoVentaFamilyDto>)rows.ToList();
        }, "No se pudieron cargar las familias del punto de venta.", ct);

    public Task<PuntoVentaSaleResultDto> CreateSaleAsync(PuntoVentaSaleRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CreateSale", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var items = request.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.IdArticulo) && x.Cantidad > 0)
                .ToList();

            if (items.Count == 0)
                throw new InvalidOperationException("No hay artículos cargados para cobrar.");

            var pagos = request.Pagos
                .Where(x => !string.IsNullOrWhiteSpace(x.CodigoMedioPago) && x.Importe > 0)
                .ToList();

            if (pagos.Count == 0)
                throw new InvalidOperationException("No se informaron medios de pago.");

            var totalItems = decimal.Round(items.Sum(x => x.Subtotal), 2);
            var totalPagos = decimal.Round(pagos.Sum(x => x.Importe), 2);
            if (totalPagos + 0.01m < totalItems)
                throw new InvalidOperationException("El total cobrado debe cubrir al menos el total del carrito.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var context = await GetContextAsync(token);
            var settings = await GetSettingsAsync(token);
            var tcConfig = await GetTipoComprobanteConfigAsync(cn, token);
            var tc = string.IsNullOrWhiteSpace(request.TipoComprobante) ? context.TipoComprobanteDefault : request.TipoComprobante.Trim();
            var sucursal = string.IsNullOrWhiteSpace(request.Sucursal) ? context.SucursalDefault : request.Sucursal.Trim();
            var letra = string.IsNullOrWhiteSpace(request.Letra) ? ResolveLetraDefault(tcConfig, sucursal) : request.Letra.Trim();
            var cliente = !string.IsNullOrWhiteSpace(request.CuentaCliente)
                ? request.CuentaCliente.Trim()
                : settings.CuentaConsumidorFinal.Trim();

            if (string.IsNullOrWhiteSpace(cliente))
                throw new InvalidOperationException("No se pudo resolver la cuenta de consumidor final para grabar la venta.");

            var comprobante = await CreateReceiptAsync(
                cn,
                cliente,
                request.Vendedor.Trim(),
                request.Fecha == default ? DateTime.Today : request.Fecha,
                request.Observaciones.Trim(),
                tc,
                sucursal,
                letra,
                token);

            foreach (var item in items)
                await AddReceiptItemAsync(cn, comprobante.IdComprobante, item, token);

            var idCobranza = await CreateCollectionAsync(cn, comprobante.IdComprobante, token);
            await NormalizeComprobanteKeysAsync(cn, idCobranza, token);
            await CreatePaymentSeedAsync(cn, idCobranza, token);

            foreach (var pago in pagos)
                await CreatePaymentLineAsync(cn, idCobranza, pago, token);

            await CreateCollectionApplicationAsync(cn, idCobranza, comprobante.IdComprobante, token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "CreateSale",
                "V_MV_CPTE",
                comprobante.IdComprobante.ToString(),
                "Venta POS generada con cobranza inmediata.",
                new
                {
                    comprobante.IdComprobante,
                    idCobranza,
                    Cliente = cliente,
                    Total = totalItems,
                    Pagos = pagos.Select(x => new { x.CodigoMedioPago, x.Importe }).ToArray()
                },
                token);

            return new PuntoVentaSaleResultDto
            {
                IdComprobante = comprobante.IdComprobante,
                IdCobranza = idCobranza,
                TipoComprobante = comprobante.Tc,
                Sucursal = comprobante.Sucursal,
                Numero = comprobante.Numero,
                Letra = comprobante.Letra,
                IdComprobanteTexto = comprobante.IdComprobanteTexto,
                Total = totalItems
            };
        }, "No se pudo registrar la venta y su cobranza en el punto de venta.", ct);

    public Task SendReceiptByEmailAsync(PuntoVentaReceiptEmailRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SendReceiptByEmail", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var destinatario = request.Destinatario?.Trim() ?? string.Empty;
            if (destinatario.Length == 0)
                throw new InvalidOperationException("Ingresá un email de destino antes de enviar el comprobante.");

            _ = new MailAddress(destinatario);

            if (request.IdComprobante <= 0 || string.IsNullOrWhiteSpace(request.TipoComprobante) || string.IsNullOrWhiteSpace(request.IdComprobanteTexto))
                throw new InvalidOperationException("No se pudo resolver el comprobante que querés enviar por email.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var mailConfig = await ResolveMailConfigAsync(cn, token);
            var company = await LoadCompanyMailInfoAsync(cn, token);
            var header = await LoadReceiptHeaderAsync(cn, request.IdComprobante, token)
                         ?? throw new InvalidOperationException("No se pudo leer la cabecera del comprobante para enviarlo por email.");

            var detail = (await cn.QueryAsync<ReceiptDetailRow>(new CommandDefinition(
                """
                SELECT
                    ISNULL(LTRIM(RTRIM(descripcion)), '') AS Descripcion,
                    ISNULL(cantidad, 0) AS Cantidad,
                    ISNULL(importe, 0) AS Importe,
                    ISNULL(total, 0) AS Total
                FROM dbo.V_MV_CpteInsumos
                WHERE tc = @Tc
                  AND idcomprobante = @IdComprobante
                ORDER BY secuencia;
                """,
                new { Tc = header.Tc, IdComprobante = header.IdComprobanteTexto },
                cancellationToken: token))).ToList();

            var payments = (await cn.QueryAsync<ReceiptPaymentRow>(new CommandDefinition(
                """
                SELECT
                    ISNULL(c.descripcion, '') AS Descripcion,
                    ISNULL(b.importe, 0) AS Importe
                FROM dbo.MV_APLICACION a
                LEFT JOIN dbo.MV_ASIENTOS b
                    ON a.TC = b.TC
                   AND a.SUCURSAL = b.SUCURSAL
                   AND a.NUMERO = b.NUMERO
                   AND a.LETRA = b.LETRA
                LEFT JOIN dbo.MA_CUENTAS c
                    ON b.CUENTA = c.CODIGO
                WHERE a.TCO_ORIGEN = @Tc
                  AND a.IDComprobante_ORIGEN = @IdComprobante
                  AND b.[DEBE-HABER] = 'D';
                """,
                new { Tc = header.Tc, IdComprobante = header.IdComprobanteTexto },
                cancellationToken: token))).ToList();

            using var message = new MailMessage
            {
                From = new MailAddress(mailConfig.FromAddress),
                Subject = $"Su compra en {company.Nombre}",
                Body = BuildReceiptEmailHtml(company, header, detail, payments),
                IsBodyHtml = true
            };

            message.To.Add(destinatario);

            using var client = new SmtpClient(mailConfig.Server, mailConfig.Port)
            {
                EnableSsl = mailConfig.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(mailConfig.FromAddress, mailConfig.Password)
            };

            await client.SendMailAsync(message, token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "SendReceiptByEmail",
                "V_MV_CPTE",
                request.IdComprobante.ToString(),
                "Comprobante POS enviado por email.",
                new
                {
                    request.IdComprobante,
                    request.TipoComprobante,
                    request.IdComprobanteTexto,
                    Destinatario = destinatario
                },
                token);

            return true;
        }, "No se pudo enviar el comprobante por email desde el punto de venta.", ct);

    public Task<PuntoVentaReceiptContextDto> GetReceiptContextAsync(string cuentaCliente, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetReceiptContext", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var company = await LoadCompanyMailInfoAsync(cn, token);
            var email = await TryResolveCustomerEmailAsync(cn, cuentaCliente?.Trim() ?? string.Empty, token);

            return new PuntoVentaReceiptContextDto
            {
                Empresa = company.Nombre,
                Direccion = string.Join(" · ", new[] { company.Calle, company.Localidad }.Where(x => !string.IsNullOrWhiteSpace(x))),
                Telefono = company.Telefono,
                EmailSugerido = email
            };
        }, "No se pudo cargar el contexto de cierre del comprobante.", ct);

    public Task<PuntoVentaCatalogDto> SearchArticulosAsync(PuntoVentaCatalogFiltersDto filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchArticulos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "V_MA_ARTICULOS", token))
                throw new InvalidOperationException("La base activa no tiene V_MA_ARTICULOS. No se puede consultar el catálogo del POS.");

            var pricing = await ResolvePricingContextAsync(cn, token);
            var clasePrecio = ParseClasePrecio(pricing.ClasePrecioActual);
            var familyFilter = filters.IdFamilia.Trim();
            var search = filters.Texto.Trim();
            var take = filters.TamanioPagina <= 0 ? 24 : Math.Min(filters.TamanioPagina, 100);

            var rows = await cn.QueryAsync<PuntoVentaArticleRow>(new CommandDefinition(
                """
                SELECT TOP (@Take)
                    LTRIM(RTRIM(a.IDARTICULO)) AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.CODIGOBARRA)), '') AS CodigoBarra,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(a.Procedencia)), '') AS Procedencia,
                    ISNULL(LTRIM(RTRIM(a.IdFamilia)), '') AS IdFamilia,
                    ISNULL(LTRIM(RTRIM(f.Descripcion)), '') AS Familia,
                    ISNULL(LTRIM(RTRIM(a.IDUNIDAD)), '') AS IdUnidad,
                    ISNULL(a.TasaIVA, 0) AS TasaIva,
                    ISNULL(a.EXENTO, 0) AS Exento,
                    ISNULL(a.NO_CONTROLA_STOCK, 0) AS NoControlaStock,
                    ISNULL(a.PESABLE, 0) AS Pesable,
                    ISNULL(LTRIM(RTRIM(a.RutaImagen)), '') AS RutaImagen,
                    ISNULL(a.PRECIO1, 0) AS PrecioBase1,
                    ISNULL(p.Precio1, 0) AS PrecioLista1,
                    ISNULL(p.Precio2, 0) AS PrecioLista2,
                    ISNULL(p.Precio3, 0) AS PrecioLista3,
                    ISNULL(p.Precio4, 0) AS PrecioLista4,
                    ISNULL(p.Precio5, 0) AS PrecioLista5,
                    ISNULL(p.Precio6, 0) AS PrecioLista6,
                    ISNULL(p.Precio7, 0) AS PrecioLista7,
                    ISNULL(p.Precio8, 0) AS PrecioLista8
                FROM dbo.V_MA_ARTICULOS a
                LEFT JOIN dbo.V_TA_FAMILIAS f
                    ON LEFT(LTRIM(RTRIM(ISNULL(a.IdFamilia, ''))), 3) = LTRIM(RTRIM(f.IdFamilia))
                LEFT JOIN dbo.V_MA_Precios p
                    ON p.IdArticulo = a.IDARTICULO
                   AND p.IdLista = @IdLista
                   AND p.TipoLista = 'V'
                WHERE ISNULL(a.Suspendido, 0) <> 1
                  AND ISNULL(a.SuspendidoV, 0) <> 1
                  AND (@IdFamilia = '' OR LEFT(LTRIM(RTRIM(ISNULL(a.IdFamilia, ''))), 3) = @IdFamilia)
                  AND (
                        @Texto = ''
                        OR UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(a.DESCRIPCION))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA1, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA2, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA3, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA4, '')))) LIKE @TextoLike
                  )
                ORDER BY a.DESCRIPCION, a.IDARTICULO;
                """,
                new
                {
                    Take = take,
                    IdLista = pricing.ListaPrecioActual,
                    IdFamilia = familyFilter,
                    Texto = search,
                    TextoLike = $"%{search.ToUpperInvariant()}%"
                },
                cancellationToken: token));

            var articles = rows
                .Select(row => new PuntoVentaArticleDto
                {
                    IdArticulo = row.IdArticulo,
                    CodigoBarra = row.CodigoBarra,
                    Descripcion = row.Descripcion,
                    Presentacion = row.Presentacion,
                    Procedencia = row.Procedencia,
                    IdFamilia = row.IdFamilia,
                    Familia = row.Familia,
                    IdUnidad = row.IdUnidad,
                    PrecioUnitario = ResolvePrice(row, clasePrecio),
                    TasaIva = row.TasaIva,
                    Exento = row.Exento,
                    NoControlaStock = row.NoControlaStock,
                    Pesable = row.Pesable,
                    RutaImagen = row.RutaImagen
                })
                .ToList();

            var usaFallback = string.IsNullOrWhiteSpace(pricing.ListaPrecioActual);

            return new PuntoVentaCatalogDto
            {
                Articulos = articles,
                ListaPrecioActual = pricing.ListaPrecioActual,
                NombreListaPrecioActual = pricing.NombreListaPrecioActual,
                ClasePrecioActual = pricing.ClasePrecioActual,
                UsaPrecioFallback = usaFallback
            };
        }, "No se pudieron cargar los artículos del punto de venta.", ct);

    public Task<PuntoVentaArticleImageDto?> GetArticleImageForServeAsync(string idArticulo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetArticleImage", async token =>
        {
            var codigo = idArticulo?.Trim() ?? string.Empty;
            if (codigo.Length == 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "V_MA_ARTICULOS", token))
                return null;

            var rutaImagen = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT TOP (1) ISNULL(LTRIM(RTRIM(RutaImagen)), '')
                FROM dbo.V_MA_ARTICULOS
                WHERE UPPER(LTRIM(RTRIM(IDARTICULO))) = @IdArticulo;
                """,
                new { IdArticulo = codigo.ToUpperInvariant() },
                cancellationToken: token)) ?? string.Empty;

            var verificadorRutaImagenes = await TryReadConfigValueAsync(cn, "VERIFICADOR_RUTAIMAGENES", token);
            var path = ResolveArticleImagePath(codigo, rutaImagen, verificadorRutaImagenes);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            return new PuntoVentaArticleImageDto
            {
                RutaCompleta = path,
                NombreArchivo = Path.GetFileName(path),
                MimeType = InferImageMimeType(path)
            };
        }, "No se pudo resolver la imagen del artículo.", ct);

    private async Task<TipoComprobanteRow?> GetTipoComprobanteConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await ObjectExistsAsync(cn, "V_TA_CPTE", null, ct))
            return null;

        return await cn.QuerySingleOrDefaultAsync<TipoComprobanteRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                ISNULL(LTRIM(RTRIM(LETRAS)), '') AS Letras,
                ISNULL(A_SUC_DEFAULT, 0) AS SucursalA,
                ISNULL(B_SUC_DEFAULT, 0) AS SucursalB,
                ISNULL(C_SUC_DEFAULT, 0) AS SucursalC,
                ISNULL(X_SUC_DEFAULT, 0) AS SucursalX
            FROM dbo.V_TA_CPTE
            WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Tc;
            """,
            new { Tc = DefaultTc },
            cancellationToken: ct));
    }

    private async Task<PricingContext> ResolvePricingContextAsync(SqlConnection cn, CancellationToken ct)
    {
        var clasePrecio = DefaultClasePrecio;
        if (await TableExistsAsync(cn, "TA_CONFIGURACION", ct))
        {
            var claseConfigurada = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT TOP (1)
                    CASE
                        WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR))
                        WHEN ISNULL(CAST(ValorAux AS nvarchar(150)), '') <> '' THEN LTRIM(RTRIM(CAST(ValorAux AS nvarchar(150))))
                        ELSE ''
                    END
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'CLASEDEPRECIODEFAULT';
                """,
                cancellationToken: ct));

            if (!string.IsNullOrWhiteSpace(claseConfigurada))
                clasePrecio = claseConfigurada.Trim();
        }

        if (!await TableExistsAsync(cn, "V_MA_PreciosCab", ct))
        {
            return new PricingContext
            {
                ClasePrecioActual = clasePrecio
            };
        }

        var lista = await cn.QuerySingleOrDefaultAsync<ListaPrecioRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                ISNULL(LTRIM(RTRIM(IdLista)), '') AS IdLista,
                ISNULL(LTRIM(RTRIM(Nombre)), '') AS Nombre
            FROM dbo.V_MA_PreciosCab
            WHERE TipoLista = 'V'
            ORDER BY
                CASE
                    WHEN (VigenciaDesde IS NULL OR VigenciaDesde <= GETDATE())
                     AND (VigenciaHasta IS NULL OR VigenciaHasta >= GETDATE()) THEN 0
                    ELSE 1
                END,
                IdLista;
            """,
            cancellationToken: ct));

        return new PricingContext
        {
            ClasePrecioActual = clasePrecio,
            ListaPrecioActual = lista?.IdLista?.Trim() ?? string.Empty,
            NombreListaPrecioActual = lista?.Nombre?.Trim() ?? string.Empty
        };
    }

    private async Task<ComprobanteCreadoRow> CreateReceiptAsync(
        SqlConnection cn,
        string cliente,
        string vendedor,
        DateTime fecha,
        string observaciones,
        string tc,
        string sucursal,
        string letra,
        CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.sp_web_Alta_Comprobante", cn)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@pCliente", cliente);
        cmd.Parameters.AddWithValue("@pVendedor", string.IsNullOrWhiteSpace(vendedor) ? string.Empty : vendedor);
        cmd.Parameters.AddWithValue("@pFecha", fecha);
        cmd.Parameters.AddWithValue("@pObservaciones", string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones);
        cmd.Parameters.AddWithValue("@pLat", DBNull.Value);
        cmd.Parameters.AddWithValue("@pLng", DBNull.Value);
        cmd.Parameters.AddWithValue("@pTC", tc);
        cmd.Parameters.AddWithValue("@pSucursal", sucursal);
        cmd.Parameters.AddWithValue("@pNumero", DBNull.Value);
        cmd.Parameters.AddWithValue("@pLetra", letra);

        var resultadoParam = new SqlParameter("@pResultado", System.Data.SqlDbType.SmallInt) { Direction = System.Data.ParameterDirection.Output };
        var mensajeParam = new SqlParameter("@pMensaje", System.Data.SqlDbType.VarChar, 255) { Direction = System.Data.ParameterDirection.Output };
        var idParam = new SqlParameter("@pIdComprobanteRES", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        cmd.Parameters.Add(resultadoParam);
        cmd.Parameters.Add(mensajeParam);
        cmd.Parameters.Add(idParam);

        await cmd.ExecuteNonQueryAsync(ct);

        ValidateSpResult(
            ConvertToInt(resultadoParam.Value),
            Convert.ToString(mensajeParam.Value) ?? "No se pudo grabar el comprobante del POS.");

        var idComprobante = ConvertToInt(idParam.Value);
        var row = await LoadComprobanteAsync(cn, idComprobante, ct);
        if (row is null)
            throw new InvalidOperationException("La venta se grabó pero no se pudo releer el comprobante generado.");

        return row;
    }

    private async Task AddReceiptItemAsync(SqlConnection cn, int idComprobante, PuntoVentaCartItemDto item, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.sp_web_CpteInsumos", cn)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@pIdCpte", idComprobante);
        cmd.Parameters.AddWithValue("@pIdArticulo", item.IdArticulo);
        cmd.Parameters.AddWithValue("@pCantidad", item.Cantidad);
        cmd.Parameters.AddWithValue("@pImporteUnitario", item.PrecioUnitario);
        cmd.Parameters.AddWithValue("@pPorcDescuento", "0");

        var resultadoParam = new SqlParameter("@pResultado", System.Data.SqlDbType.SmallInt) { Direction = System.Data.ParameterDirection.Output };
        var mensajeParam = new SqlParameter("@pMensaje", System.Data.SqlDbType.VarChar, 255) { Direction = System.Data.ParameterDirection.Output };
        var idParam = new SqlParameter("@pIdVMVCpteInsumosRES", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        cmd.Parameters.Add(resultadoParam);
        cmd.Parameters.Add(mensajeParam);
        cmd.Parameters.Add(idParam);

        await cmd.ExecuteNonQueryAsync(ct);

        ValidateSpResult(
            ConvertToInt(resultadoParam.Value),
            Convert.ToString(mensajeParam.Value) ?? $"No se pudo grabar el artículo {item.IdArticulo}.");
    }

    private async Task<int> CreateCollectionAsync(SqlConnection cn, int idComprobante, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.sp_web_CreaCobPorFactura", cn)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@pIdCpte", idComprobante);
        var resultadoParam = new SqlParameter("@pResultado", System.Data.SqlDbType.SmallInt) { Direction = System.Data.ParameterDirection.Output };
        var mensajeParam = new SqlParameter("@pMensaje", System.Data.SqlDbType.VarChar, 255) { Direction = System.Data.ParameterDirection.Output };
        var idParam = new SqlParameter("@pIdComprobanteRES", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        cmd.Parameters.Add(resultadoParam);
        cmd.Parameters.Add(mensajeParam);
        cmd.Parameters.Add(idParam);

        await cmd.ExecuteNonQueryAsync(ct);

        ValidateSpResult(
            ConvertToInt(resultadoParam.Value),
            Convert.ToString(mensajeParam.Value) ?? "No se pudo crear la cobranza del POS.");

        return ConvertToInt(idParam.Value);
    }

    private async Task NormalizeComprobanteKeysAsync(SqlConnection cn, int idComprobante, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.V_MV_CPTE
            SET
                SUCURSAL = CASE
                    WHEN ISNULL(LTRIM(RTRIM(SUCURSAL)), '') = '' AND LEN(ISNULL(IDCOMPROBANTE, '')) >= 13
                        THEN SUBSTRING(IDCOMPROBANTE, 1, 4)
                    ELSE SUCURSAL
                END,
                NUMERO = CASE
                    WHEN ISNULL(LTRIM(RTRIM(NUMERO)), '') = '' AND LEN(ISNULL(IDCOMPROBANTE, '')) >= 13
                        THEN SUBSTRING(IDCOMPROBANTE, 5, 8)
                    ELSE NUMERO
                END,
                LETRA = CASE
                    WHEN ISNULL(LTRIM(RTRIM(LETRA)), '') = '' AND LEN(ISNULL(IDCOMPROBANTE, '')) >= 13
                        THEN RIGHT(IDCOMPROBANTE, 1)
                    ELSE LETRA
                END
            WHERE ID = @IdComprobante;
            """;

        await cn.ExecuteAsync(new CommandDefinition(
            sql,
            new { IdComprobante = idComprobante },
            cancellationToken: ct));
    }

    private async Task CreatePaymentSeedAsync(SqlConnection cn, int idCobranza, CancellationToken ct)
    {
        const string sql = """
            BEGIN TRY
                ALTER TABLE dbo.MV_ASIENTOS DISABLE TRIGGER ALL;
                DECLARE @pResultado INT;
                DECLARE @pMensaje NVARCHAR(250);
                EXEC dbo.sp_web_creaLineaAsiento @pIdCobranza, 0, '', 1, NULL, @pResultado OUTPUT, @pMensaje OUTPUT;
                ALTER TABLE dbo.MV_ASIENTOS ENABLE TRIGGER ALL;
                SELECT @pResultado AS Resultado, @pMensaje AS Mensaje;
            END TRY
            BEGIN CATCH
                BEGIN TRY
                    ALTER TABLE dbo.MV_ASIENTOS ENABLE TRIGGER ALL;
                END TRY
                BEGIN CATCH
                END CATCH;
                THROW;
            END CATCH
            """;

        var result = await cn.QuerySingleAsync<SpResultRow>(new CommandDefinition(
            sql,
            new { pIdCobranza = idCobranza },
            cancellationToken: ct));

        ValidateSpResult(result.Resultado, result.Mensaje);
    }

    private async Task CreatePaymentLineAsync(SqlConnection cn, int idCobranza, PuntoVentaPaymentLineDto pago, CancellationToken ct)
    {
        const string sql = """
            BEGIN TRY
                ALTER TABLE dbo.MV_ASIENTOS DISABLE TRIGGER ALL;
                DECLARE @pResultado INT;
                DECLARE @pMensaje NVARCHAR(250);
                EXEC dbo.sp_web_creaLineaAsiento @pIdCobranza, @Importe, @Codigo, 0, NULL, @pResultado OUTPUT, @pMensaje OUTPUT;
                ALTER TABLE dbo.MV_ASIENTOS ENABLE TRIGGER ALL;
                SELECT @pResultado AS Resultado, @pMensaje AS Mensaje;
            END TRY
            BEGIN CATCH
                BEGIN TRY
                    ALTER TABLE dbo.MV_ASIENTOS ENABLE TRIGGER ALL;
                END TRY
                BEGIN CATCH
                END CATCH;
                THROW;
            END CATCH
            """;

        var result = await cn.QuerySingleAsync<SpResultRow>(new CommandDefinition(
            sql,
            new
            {
                pIdCobranza = idCobranza,
                Importe = pago.Importe,
                Codigo = pago.CodigoMedioPago
            },
            cancellationToken: ct));

        ValidateSpResult(result.Resultado, result.Mensaje);
    }

    private async Task CreateCollectionApplicationAsync(SqlConnection cn, int idCobranza, int idComprobante, CancellationToken ct)
    {
        const string sql = """
            BEGIN TRY
                ALTER TABLE dbo.MV_ASIENTOS DISABLE TRIGGER ALL;
                DECLARE @pResultado INT;
                DECLARE @pMensaje NVARCHAR(250);
                EXEC dbo.sp_web_CreaAplicacionCobranzaFactura @pIdCobranza, @pIdComprobante, @pResultado OUTPUT, @pMensaje OUTPUT;
                ALTER TABLE dbo.MV_ASIENTOS ENABLE TRIGGER ALL;
                SELECT @pResultado AS Resultado, @pMensaje AS Mensaje;
            END TRY
            BEGIN CATCH
                BEGIN TRY
                    ALTER TABLE dbo.MV_ASIENTOS ENABLE TRIGGER ALL;
                END TRY
                BEGIN CATCH
                END CATCH;
                THROW;
            END CATCH
            """;

        var result = await cn.QuerySingleAsync<SpResultRow>(new CommandDefinition(
            sql,
            new
            {
                pIdCobranza = idCobranza,
                pIdComprobante = idComprobante
            },
            cancellationToken: ct));

        ValidateSpResult(result.Resultado, result.Mensaje);
    }

    private async Task<ComprobanteCreadoRow?> LoadComprobanteAsync(SqlConnection cn, int idComprobante, CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<ComprobanteCreadoRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                ID AS IdComprobante,
                ISNULL(LTRIM(RTRIM(TC)), '') AS Tc,
                ISNULL(LTRIM(RTRIM(SUCURSAL)), '') AS Sucursal,
                ISNULL(LTRIM(RTRIM(NUMERO)), '') AS Numero,
                ISNULL(LTRIM(RTRIM(LETRA)), '') AS Letra,
                ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto
            FROM dbo.V_MV_CPTE
            WHERE ID = @IdComprobante;
            """,
            new { IdComprobante = idComprobante },
            cancellationToken: ct));

    private static void ValidateSpResult(int resultado, string mensaje)
    {
        if (resultado == 11)
            return;

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(mensaje)
            ? "La operación SQL del punto de venta devolvió un estado inválido."
            : mensaje.Trim());
    }

    private static int ConvertToInt(object? value)
        => value switch
        {
            null or DBNull => 0,
            int intValue => intValue,
            short shortValue => shortValue,
            long longValue => (int)longValue,
            _ when int.TryParse(Convert.ToString(value), out var parsed) => parsed,
            _ => 0
        };

    private static decimal ResolvePrice(PuntoVentaArticleRow row, int clasePrecio)
    {
        var precioLista = clasePrecio switch
        {
            2 => row.PrecioLista2,
            3 => row.PrecioLista3,
            4 => row.PrecioLista4,
            5 => row.PrecioLista5,
            6 => row.PrecioLista6,
            7 => row.PrecioLista7,
            8 => row.PrecioLista8,
            _ => row.PrecioLista1
        };

        if (precioLista > 0)
            return decimal.Round(precioLista, 2);

        return decimal.Round(row.PrecioBase1, 2);
    }

    private static int ParseClasePrecio(string clasePrecio)
        => int.TryParse(clasePrecio, out var value) && value is >= 1 and <= 8 ? value : 1;

    private static string NormalizeClasePrecio(string clasePrecio)
        => ParseClasePrecio(clasePrecio).ToString();

    private async Task<string> TryReadConfigValueAsync(SqlConnection cn, string clave, CancellationToken ct)
    {
        if (!await TableExistsAsync(cn, "TA_CONFIGURACION", ct))
            return string.Empty;

        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Clave", clave.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return string.Empty;

        return ResolveStoredValue(GetString(rd, 0), GetString(rd, 1));
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

    private static async Task SaveConfigValueAsync(
        SqlConnection cn,
        SqlTransaction tx,
        string detailColumn,
        string key,
        string value,
        CancellationToken ct)
    {
        var stored = SplitStoredValue(value);
        var sql = $"""
            UPDATE dbo.TA_CONFIGURACION
            SET
                VALOR = @Valor,
                {detailColumn} = @ValorAux,
                GRUPO = @Grupo
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO)
                VALUES (@Clave, @Valor, @ValorAux, @Grupo);
            END;
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@ClaveNormalizada", key.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Clave", key);
        cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
        cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
        cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (normalized.Length > 150)
            return (string.Empty, normalized);

        return (normalized, string.Empty);
    }

    private static object DbNullable(string value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private async Task<MailConfig> ResolveMailConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var server = await TryReadConfigValueAsync(cn, "EMAIL_SERVER", ct);
        var port = await TryReadConfigValueAsync(cn, "EMAIL_PORT", ct);
        var account = await TryReadConfigValueAsync(cn, "EMAIL_CTA", ct);
        var password = await TryReadConfigValueAsync(cn, "EMAIL_PASS", ct);
        var ssl = await TryReadConfigValueAsync(cn, "EMAIL_SSL", ct);

        server = ResolveConfigOrFallback(server, "EMAIL_SERVER");
        port = ResolveConfigOrFallback(port, "EMAIL_PORT");
        account = ResolveConfigOrFallback(account, "EMAIL_CTA");
        password = ResolveConfigOrFallback(password, "EMAIL_PASS");
        ssl = ResolveConfigOrFallback(ssl, "EMAIL_SSL");

        if (string.IsNullOrWhiteSpace(server)
            || string.IsNullOrWhiteSpace(port)
            || string.IsNullOrWhiteSpace(account)
            || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Falta configurar el correo saliente del POS. Revisá EMAIL_SERVER, EMAIL_PORT, EMAIL_CTA y EMAIL_PASS en Configuración.");
        }

        if (!int.TryParse(port, out var smtpPort) || smtpPort <= 0)
            throw new InvalidOperationException("EMAIL_PORT no tiene un valor válido para enviar comprobantes por mail.");

        return new MailConfig(
            server.Trim(),
            smtpPort,
            account.Trim(),
            password.Trim(),
            ssl.Equals("SI", StringComparison.OrdinalIgnoreCase)
            || ssl.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || ssl.Equals("1", StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveConfigOrFallback(string dbValue, string key)
    {
        if (!string.IsNullOrWhiteSpace(dbValue))
            return dbValue.Trim();

        return configuration[key]
               ?? configuration[$"PuntoVenta:{key}"]
               ?? string.Empty;
    }

    private async Task<ReceiptCompanyRow> LoadCompanyMailInfoAsync(SqlConnection cn, CancellationToken ct)
    {
        var nombre = await TryReadConfigValueAsync(cn, "Nombre", ct);
        var localidad = await TryReadConfigValueAsync(cn, "Localidad", ct);
        var cuit = await TryReadConfigValueAsync(cn, "CUIT", ct);
        var telefono = await TryReadConfigValueAsync(cn, "Telefono", ct);
        var calle = await TryReadConfigValueAsync(cn, "Calle", ct);
        var cpostal = await TryReadConfigValueAsync(cn, "CPOSTAL", ct);
        var condIva = await TryReadConfigValueAsync(cn, "CONDIVA", ct);

        string iva = string.Empty;
        if (!string.IsNullOrWhiteSpace(condIva) && await TableExistsAsync(cn, "TA_CONDIVA", ct))
        {
            iva = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT TOP (1) ISNULL(LTRIM(RTRIM(DESCRIPCION)), '')
                FROM dbo.TA_CONDIVA
                WHERE LTRIM(RTRIM(CODIGO)) = @Codigo;
                """,
                new { Codigo = condIva.Trim() },
                cancellationToken: ct)) ?? string.Empty;
        }

        return new ReceiptCompanyRow
        {
            Nombre = string.IsNullOrWhiteSpace(nombre) ? "Alfa Gestión" : nombre.Trim(),
            Localidad = localidad.Trim(),
            Cuit = cuit.Trim(),
            Telefono = telefono.Trim(),
            Calle = calle.Trim(),
            CodigoPostal = cpostal.Trim(),
            Iva = iva.Trim()
        };
    }

    private async Task<ReceiptHeaderRow?> LoadReceiptHeaderAsync(SqlConnection cn, int idComprobante, CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<ReceiptHeaderRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                ID AS IdComprobante,
                ISNULL(LTRIM(RTRIM(TC)), '') AS Tc,
                ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto,
                ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS Importe,
                FECHA AS Fecha,
                ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS Nombre
            FROM dbo.V_MV_CPTE
            WHERE ID = @IdComprobante;
            """,
            new { IdComprobante = idComprobante },
            cancellationToken: ct));

    private async Task<string> TryResolveCustomerEmailAsync(SqlConnection cn, string cuentaCliente, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cuentaCliente))
            return string.Empty;

        if (await ObjectExistsAsync(cn, "VT_CLIENTES", null, ct) && await ColumnExistsAsync(cn, "VT_CLIENTES", "MAIL", ct))
        {
            var email = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT TOP (1) ISNULL(LTRIM(RTRIM(MAIL)), '')
                FROM dbo.VT_CLIENTES
                WHERE LTRIM(RTRIM(CODIGO)) = @Cuenta;
                """,
                new { Cuenta = cuentaCliente },
                cancellationToken: ct)) ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(email))
                return email.Trim();
        }

        if (await TableExistsAsync(cn, "MA_CUENTASADIC", ct) && await ColumnExistsAsync(cn, "MA_CUENTASADIC", "MAIL", ct))
        {
            var email = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT TOP (1) ISNULL(LTRIM(RTRIM(MAIL)), '')
                FROM dbo.MA_CUENTASADIC
                WHERE LTRIM(RTRIM(CODIGO)) = @Cuenta;
                """,
                new { Cuenta = cuentaCliente },
                cancellationToken: ct)) ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(email))
                return email.Trim();
        }

        return string.Empty;
    }

    private static string BuildReceiptEmailHtml(
        ReceiptCompanyRow company,
        ReceiptHeaderRow header,
        IReadOnlyList<ReceiptDetailRow> detail,
        IReadOnlyList<ReceiptPaymentRow> payments)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        static string M(decimal value) => value.ToString("$ #,##0.00", System.Globalization.CultureInfo.GetCultureInfo("es-AR"));
        static string Q(decimal value) => value.ToString("#,##0.##", System.Globalization.CultureInfo.GetCultureInfo("es-AR"));

        var sb = new StringBuilder();
        sb.Append("""
            <div style="font-family:Segoe UI,Arial,sans-serif;background:#f4f7fb;padding:24px;color:#142133;">
              <div style="max-width:860px;margin:0 auto;background:#ffffff;border:1px solid #d9e3ef;border-radius:18px;overflow:hidden;">
                <div style="padding:24px 28px;background:#0f2138;color:#ffffff;">
        """);
        sb.Append($"<h1 style=\"margin:0 0 8px;font-size:28px;\">{E(company.Nombre)}</h1>");
        sb.Append($"<div style=\"font-size:14px;opacity:.9;\">{E(company.Calle)} · {E(company.Localidad)} · {E(company.Telefono)}</div>");
        if (!string.IsNullOrWhiteSpace(company.Cuit) || !string.IsNullOrWhiteSpace(company.Iva))
            sb.Append($"<div style=\"font-size:13px;opacity:.85;margin-top:6px;\">CUIT: {E(company.Cuit)} · {E(company.Iva)}</div>");
        sb.Append("</div>");
        sb.Append("<div style=\"padding:24px 28px;\">");
        sb.Append($"<h2 style=\"margin:0 0 6px;font-size:22px;color:#12213a;\">{E(header.Tc)} {E(header.IdComprobanteTexto)}</h2>");
        sb.Append($"<div style=\"font-size:14px;color:#4b5d73;margin-bottom:20px;\">Fecha: {header.Fecha:dd/MM/yyyy} · Cliente: {E(header.Nombre)}</div>");
        sb.Append("<table style=\"width:100%;border-collapse:collapse;margin-bottom:20px;\">");
        sb.Append("<thead><tr><th style=\"text-align:left;padding:10px;border-bottom:1px solid #d9e3ef;\">Descripción</th><th style=\"text-align:right;padding:10px;border-bottom:1px solid #d9e3ef;\">Cantidad</th><th style=\"text-align:right;padding:10px;border-bottom:1px solid #d9e3ef;\">Importe</th><th style=\"text-align:right;padding:10px;border-bottom:1px solid #d9e3ef;\">Total</th></tr></thead><tbody>");
        foreach (var item in detail)
        {
            sb.Append("<tr>");
            sb.Append($"<td style=\"padding:10px;border-bottom:1px solid #eef3f8;\">{E(item.Descripcion)}</td>");
            sb.Append($"<td style=\"padding:10px;border-bottom:1px solid #eef3f8;text-align:right;\">{Q(item.Cantidad)}</td>");
            sb.Append($"<td style=\"padding:10px;border-bottom:1px solid #eef3f8;text-align:right;\">{M(item.Importe)}</td>");
            sb.Append($"<td style=\"padding:10px;border-bottom:1px solid #eef3f8;text-align:right;\">{M(item.Total)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        if (payments.Count > 0)
        {
            sb.Append("<h3 style=\"margin:0 0 10px;font-size:18px;color:#12213a;\">Medios de pago</h3>");
            sb.Append("<table style=\"width:100%;border-collapse:collapse;margin-bottom:18px;\">");
            foreach (var payment in payments)
            {
                sb.Append("<tr>");
                sb.Append($"<td style=\"padding:8px 10px;border-bottom:1px solid #eef3f8;\">{E(payment.Descripcion)}</td>");
                sb.Append($"<td style=\"padding:8px 10px;border-bottom:1px solid #eef3f8;text-align:right;\">{M(payment.Importe)}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</table>");
        }

        sb.Append($"<div style=\"text-align:right;font-size:24px;font-weight:800;color:#0f2138;\">TOTAL {M(header.Importe)}</div>");
        sb.Append("</div></div></div>");
        return sb.ToString();
    }

    private string? ResolveArticleImagePath(string idArticulo, string rutaImagen, string verificadorRutaImagenes)
    {
        var candidates = new List<string>();
        var normalizedRuta = NormalizeRelativePath(rutaImagen);
        var normalizedVerificadorBase = NormalizeBasePath(verificadorRutaImagenes);

        if (!string.IsNullOrWhiteSpace(rutaImagen))
        {
            if (Path.IsPathRooted(rutaImagen))
            {
                candidates.Add(rutaImagen.Trim());
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(normalizedVerificadorBase))
                    candidates.Add(Path.Combine(normalizedVerificadorBase, normalizedRuta));

                candidates.Add(Path.Combine(env.ContentRootPath, normalizedRuta));
            }
        }

        return candidates
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .FirstOrDefault(File.Exists);
    }

    private static string NormalizeRelativePath(string path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private string NormalizeBasePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed))
            return trimmed;

        return Path.Combine(env.ContentRootPath, trimmed.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string InferImageMimeType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static (string Code, string Source) ResolveSucursalDefault(TipoComprobanteRow? row)
    {
        if (row is null)
            return (DefaultSucursal, "Default general");

        var letras = row.Letras?.ToUpperInvariant() ?? string.Empty;

        if (letras.Contains('B') && row.SucursalB > 0)
            return (row.SucursalB.ToString("0000"), "V_TA_CPTE · letra B");

        if (letras.Contains('A') && row.SucursalA > 0)
            return (row.SucursalA.ToString("0000"), "V_TA_CPTE · letra A");

        if (letras.Contains('C') && row.SucursalC > 0)
            return (row.SucursalC.ToString("0000"), "V_TA_CPTE · letra C");

        if (letras.Contains('X') && row.SucursalX > 0)
            return (row.SucursalX.ToString("0000"), "V_TA_CPTE · letra X");

        if (row.SucursalB > 0)
            return (row.SucursalB.ToString("0000"), "V_TA_CPTE · sucursal B");

        if (row.SucursalA > 0)
            return (row.SucursalA.ToString("0000"), "V_TA_CPTE · sucursal A");

        if (row.SucursalC > 0)
            return (row.SucursalC.ToString("0000"), "V_TA_CPTE · sucursal C");

        if (row.SucursalX > 0)
            return (row.SucursalX.ToString("0000"), "V_TA_CPTE · sucursal X");

        return (DefaultSucursal, "Default general");
    }

    private static string ResolveLetraDefault(TipoComprobanteRow? row, string sucursal)
    {
        if (row is null)
            return "B";

        if (row.SucursalB > 0 && sucursal == row.SucursalB.ToString("0000"))
            return "B";

        if (row.SucursalA > 0 && sucursal == row.SucursalA.ToString("0000"))
            return "A";

        if (row.SucursalC > 0 && sucursal == row.SucursalC.ToString("0000"))
            return "C";

        if (row.SucursalX > 0 && sucursal == row.SucursalX.ToString("0000"))
            return "X";

        var letras = row.Letras?.ToUpperInvariant() ?? string.Empty;
        foreach (var letra in letras.Where(char.IsLetter))
            return letra.ToString();

        return "B";
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string friendlyMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (AppUserFacingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                module,
                action,
                ex,
                friendlyMessage,
                new
                {
                    Usuario = appUserSession.GetCurrentUserName(Environment.UserName),
                    SesionSql = sessionService.GetActiveSession()?.Nombre
                },
                AppEventSeverity.Warning,
                ct);

            throw new AppUserFacingException(friendlyMessage, incidentId, ex);
        }
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
        => await ObjectExistsAsync(cn, tableName, "U", ct);

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@ObjectName)
              AND LOWER(name) = LOWER(@ColumnName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { ObjectName = $"dbo.{tableName}", ColumnName = columnName },
            cancellationToken: ct));

        return count > 0;
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection cn, string objectName, string? objectType, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE
                WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NOT NULL THEN 1
                WHEN @ObjectType IS NULL AND OBJECT_ID(@ObjectName) IS NOT NULL THEN 1
                ELSE 0
            END;
            """;

        var exists = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { ObjectName = $"dbo.{objectName}", ObjectType = objectType },
            cancellationToken: ct));

        return exists == 1;
    }

    private sealed class UserCajaRow
    {
        public string UserName { get; init; } = string.Empty;
        public string IdCaja { get; init; } = string.Empty;
        public bool Administrador { get; init; }
    }

    private sealed class TipoComprobanteRow
    {
        public string Letras { get; init; } = string.Empty;
        public int SucursalA { get; init; }
        public int SucursalB { get; init; }
        public int SucursalC { get; init; }
        public int SucursalX { get; init; }
    }

    private sealed class ListaPrecioRow
    {
        public string IdLista { get; init; } = string.Empty;
        public string Nombre { get; init; } = string.Empty;
    }

    private sealed class PricingContext
    {
        public string ListaPrecioActual { get; init; } = string.Empty;
        public string NombreListaPrecioActual { get; init; } = string.Empty;
        public string ClasePrecioActual { get; init; } = DefaultClasePrecio;
    }

    private sealed class SpResultRow
    {
        public int Resultado { get; init; }
        public string Mensaje { get; init; } = string.Empty;
    }

    private sealed class ComprobanteCreadoRow
    {
        public int IdComprobante { get; init; }
        public string Tc { get; init; } = string.Empty;
        public string Sucursal { get; init; } = string.Empty;
        public string Numero { get; init; } = string.Empty;
        public string Letra { get; init; } = string.Empty;
        public string IdComprobanteTexto { get; init; } = string.Empty;
    }

    private sealed class PuntoVentaArticleRow
    {
        public string IdArticulo { get; init; } = string.Empty;
        public string CodigoBarra { get; init; } = string.Empty;
        public string Descripcion { get; init; } = string.Empty;
        public string Presentacion { get; init; } = string.Empty;
        public string Procedencia { get; init; } = string.Empty;
        public string IdFamilia { get; init; } = string.Empty;
        public string Familia { get; init; } = string.Empty;
        public string IdUnidad { get; init; } = string.Empty;
        public decimal TasaIva { get; init; }
        public bool Exento { get; init; }
        public bool NoControlaStock { get; init; }
        public bool Pesable { get; init; }
        public string RutaImagen { get; init; } = string.Empty;
        public decimal PrecioBase1 { get; init; }
        public decimal PrecioLista1 { get; init; }
        public decimal PrecioLista2 { get; init; }
        public decimal PrecioLista3 { get; init; }
        public decimal PrecioLista4 { get; init; }
        public decimal PrecioLista5 { get; init; }
        public decimal PrecioLista6 { get; init; }
        public decimal PrecioLista7 { get; init; }
        public decimal PrecioLista8 { get; init; }
    }

    private sealed record MailConfig(string Server, int Port, string FromAddress, string Password, bool EnableSsl);

    private sealed class ReceiptCompanyRow
    {
        public string Nombre { get; init; } = string.Empty;
        public string Localidad { get; init; } = string.Empty;
        public string Cuit { get; init; } = string.Empty;
        public string Telefono { get; init; } = string.Empty;
        public string Calle { get; init; } = string.Empty;
        public string CodigoPostal { get; init; } = string.Empty;
        public string Iva { get; init; } = string.Empty;
    }

    private sealed class ReceiptHeaderRow
    {
        public int IdComprobante { get; init; }
        public string Tc { get; init; } = string.Empty;
        public string IdComprobanteTexto { get; init; } = string.Empty;
        public decimal Importe { get; init; }
        public DateTime Fecha { get; init; }
        public string Nombre { get; init; } = string.Empty;
    }

    private sealed class ReceiptDetailRow
    {
        public string Descripcion { get; init; } = string.Empty;
        public decimal Cantidad { get; init; }
        public decimal Importe { get; init; }
        public decimal Total { get; init; }
    }

    private sealed class ReceiptPaymentRow
    {
        public string Descripcion { get; init; } = string.Empty;
        public decimal Importe { get; init; }
    }
}

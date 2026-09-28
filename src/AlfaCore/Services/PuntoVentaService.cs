using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace AlfaCore.Services;

public sealed class PuntoVentaService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents,
    IWebHostEnvironment env,
    IArcaConfigService arcaConfigService,
    IArcaFacturacionElectronicaService arcaFacturacion,
    ILogger<PuntoVentaService> logger) : IPuntoVentaService
{
    private const string ModuleName = "PuntoVenta";
    private const string DefaultTc = "FC";
    private const string DefaultCaja = "1";
    private const string DefaultSucursal = "0001";
    private const string DefaultClasePrecio = "1";
    private const int SaleCommandTimeoutSeconds = 120;
    private const int ArcaNumeracionTimeoutSeconds = 35;
    private const string SurchargeArticleCode = "RECARGO-TARJETA";
    private const string ConfigGroup = "PUNTOVENTA";
    private bool _requiredSaleProceduresChecked;
    private bool? _lineDetailParameterExists;
    /// <summary>sp_web_Alta_Comprobante graba siempre UNEGOCIO='   1' (literal, confirmado en el SP)
    /// para toda venta de POS -- se usa la misma unidad para resolver el emisor de ARCA.</summary>
    private const string PosUNegocio = "   1";

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
            var hasVerProforma = await ColumnExistsAsync(cn, "TA_USUARIOS", "VerProforma", token);

            var userRow = await cn.QuerySingleOrDefaultAsync<UserCajaRow>(new CommandDefinition(
                $"""
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS UserName,
                    ISNULL(LTRIM(RTRIM(IDCAJA)), '') AS IdCaja,
                    {(hasAdministrador ? "CASE WHEN ISNULL(Administrador, 0) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END" : "CAST(0 AS bit)")} AS Administrador,
                    {(hasVerProforma ? "CASE WHEN ISNULL(VerProforma, 1) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END" : "CAST(1 AS bit)")} AS VerProforma
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

            // Algunas bases legacy tienen SISTEMA vacío o guardan un código distinto
            // al de la sesión central. Si el usuario no apareció con el filtro de
            // sistema, se vuelve a buscar por nombre para no perder la marca de
            // administrador del sistema.
            if (userRow is null && !string.IsNullOrWhiteSpace(currentSystem))
            {
                userRow = await cn.QuerySingleOrDefaultAsync<UserCajaRow>(new CommandDefinition(
                    $"""
                    SELECT TOP (1)
                        ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS UserName,
                        ISNULL(LTRIM(RTRIM(IDCAJA)), '') AS IdCaja,
                        {(hasAdministrador ? "CASE WHEN ISNULL(Administrador, 0) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END" : "CAST(0 AS bit)")} AS Administrador,
                        {(hasVerProforma ? "CASE WHEN ISNULL(VerProforma, 1) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END" : "CAST(1 AS bit)")} AS VerProforma
                    FROM dbo.TA_USUARIOS
                    WHERE UPPER(LTRIM(RTRIM(NOMBRE))) = @UserName;
                    """,
                    new { UserName = currentUser.ToUpperInvariant() },
                    cancellationToken: token));
            }

            if (userRow is null)
                throw new InvalidOperationException("El usuario actual no existe en TA_USUARIOS de la base activa.");

            var cajaActual = string.IsNullOrWhiteSpace(userRow.IdCaja) ? DefaultCaja : userRow.IdCaja.Trim();
            var usaCajaDefault = string.IsNullOrWhiteSpace(userRow.IdCaja);

            var tcConfig = await GetTipoComprobanteConfigAsync(cn, token);
            var comprobanteHabitual = await TryReadConfigValueAsync(
                cn,
                $"{Environment.MachineName.Trim()}_GOUR_CPTE_PC",
                token);
            var usaProforma = ParseBooleanConfig(await TryReadConfigValueAsync(cn, "USAPROFORMA", token), true);
            var verProformaUsuario = userRow.VerProforma;
            var sucursalConfigurada = await TryReadConfigValueAsync(cn, "TPV_SUCURSAL", token);
            var sucursal = !string.IsNullOrWhiteSpace(sucursalConfigurada)
                ? (Code: NormalizeSucursal(sucursalConfigurada), Source: "TA_CONFIGURACION · TPV_SUCURSAL")
                : ResolveSucursalDefault(tcConfig);

            return new PuntoVentaContextDto
            {
                UsuarioActual = userRow.UserName,
                SistemaActual = currentSystem,
                SesionSqlActiva = sessionService.GetActiveSession()?.Nombre ?? string.Empty,
                CajaActual = cajaActual,
                EsAdministrador = userRow.Administrador,
                UsaProforma = usaProforma,
                VerProformaUsuario = verProformaUsuario,
                TipoComprobanteDefault = ResolveComprobanteHabitual(comprobanteHabitual, usaProforma, verProformaUsuario),
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
                ComprobanteHabitual = ResolveComprobanteHabitual(
                    await TryReadConfigValueAsync(cn, BuildComprobanteHabitualConfigKey(), token), true, true),
                SucursalDefault = await TryReadConfigValueAsync(cn, "TPV_SUCURSAL", token) is { Length: > 0 } sucursal
                    ? NormalizeSucursal(sucursal)
                    : string.Empty,
                UsaProforma = ParseBooleanConfig(await TryReadConfigValueAsync(cn, "USAPROFORMA", token), true),
                VerificadorRutaImagenes = await TryReadConfigValueAsync(cn, "VERIFICADOR_RUTAIMAGENES", token),
                CuentaConsumidorFinal = await TryReadConfigValueAsync(cn, "CUENTACONSUMIDORFINAL", token),
                CuentaCaja = await TryReadConfigValueAsync(cn, "CUENTA_CAJA", token),
                CuentaVentasOtrosConceptos = await TryReadConfigValueAsync(cn, "CUENTAVENTASDEFAULTINS", token),
                ClaveCancelar = await TryReadConfigValueAsync(cn, "TPV_ClaveCancelar", token),
                CobranzaPFSoloEfectivo = ParseBooleanConfig(
                    await TryReadConfigValueAsync(cn, "CobranzaPFSoloEfectivo", token), false),
                RutaImagenesLegacy = await TryReadConfigValueAsync(cn, "RUTAIMAGENES", token),
                EmailServer = await TryReadConfigValueAsync(cn, "EMAIL_SERVER", token),
                EmailPort = await TryReadConfigValueAsync(cn, "EMAIL_PORT", token),
                EmailCuenta = await TryReadConfigValueAsync(cn, "EMAIL_CTA", token),
                EmailPassword = await TryReadConfigValueAsync(cn, "EMAIL_PASS", token),
                EmailSsl = await TryReadConfigValueAsync(cn, "EMAIL_SSL", token),
                FtpCodigoCta = await TryReadConfigValueAsync(cn, "FTP_CODIGOCTA", token),
                MedioDePagoContado = await TryReadConfigValueAsync(cn, "MedioDePagoContado", token),
                RecargoTarjetaSinIva = ParseBooleanConfig(
                    await TryReadConfigValueAsync(cn, "cfgRecargoTJTSinIVA", token), false)
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
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, BuildComprobanteHabitualConfigKey(), ResolveComprobanteHabitual(settings.ComprobanteHabitual, settings.UsaProforma, true), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "TPV_SUCURSAL", NormalizeOptionalSucursal(settings.SucursalDefault), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "USAPROFORMA", settings.UsaProforma ? "SI" : "NO", token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "VERIFICADOR_RUTAIMAGENES", settings.VerificadorRutaImagenes.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "CUENTACONSUMIDORFINAL", settings.CuentaConsumidorFinal.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "CUENTA_CAJA", settings.CuentaCaja.Trim(), token);
            var cuentaVentas = settings.CuentaVentasOtrosConceptos.Trim();
            await ValidateCuentaContableAsync(cn, (SqlTransaction)tx, cuentaVentas, token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "CUENTAVENTASDEFAULTINS", cuentaVentas, token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "TPV_ClaveCancelar", settings.ClaveCancelar.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "CobranzaPFSoloEfectivo", settings.CobranzaPFSoloEfectivo ? "SI" : "NO", token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_SERVER", settings.EmailServer.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_PORT", settings.EmailPort.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_CTA", settings.EmailCuenta.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_PASS", settings.EmailPassword.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "EMAIL_SSL", settings.EmailSsl.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "FTP_CODIGOCTA", settings.FtpCodigoCta.Trim(), token);
            await SaveConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, "MedioDePagoContado", settings.MedioDePagoContado.Trim(), token);
            await SaveConfigValueAsync(
                cn,
                (SqlTransaction)tx,
                detailColumn,
                "cfgRecargoTJTSinIVA",
                settings.RecargoTarjetaSinIva ? "SI" : "NO",
                token);

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

    public Task ValidarPrerequisitosCobroAsync(string tipoComprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ValidateSaleBeforePayment", async token =>
        {
            var tc = (tipoComprobante ?? string.Empty).Trim().ToUpperInvariant();
            if (tc is not ("FC" or "NC"))
                return true;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // Misma resolución de emisor y conectividad WSAA que usa CreateSaleAsync,
            // pero antes de iniciar el cobro Point para no aprobar un pago si ARCA no
            // responde luego durante la numeración.
            await arcaFacturacion.ValidarDisponibilidadAsync(cn, PosUNegocio, token);
            return true;
        }, "No se puede iniciar el cobro porque la facturación electrónica no está lista.", ct);

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
                    ISNULL(LTRIM(RTRIM(CodigoOpcional)), '') AS CodigoOpcional,
                    ISNULL(LTRIM(RTRIM(DESCRIPCION)), '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(MEDIODEPAGO)), '') AS MedioDePago,
                    ISNULL(LTRIM(RTRIM(MONEDA)), '') AS Moneda
                FROM dbo.MA_CUENTAS
                WHERE ISNULL(LTRIM(RTRIM(MEDIODEPAGO)), '') <> ''
                  AND ISNULL(LTRIM(RTRIM(CodigoOpcional)), '') <> ''
                ORDER BY DESCRIPCION, CODIGO;
                """,
                cancellationToken: token))).ToList();

            // Paridad con el POS VB6: si hay medios configurados como más utilizados,
            // mostrar esos primero y respetar el orden MpMasUtilizados0..9.
            var masUtilizados = new List<PuntoVentaPaymentMethodDto>();
            var codigosMasUtilizados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i <= 9; i++)
            {
                var codigoOpcional = (await TryReadConfigValueAsync(cn, $"MpMasUtilizados{i}", token)).Trim();
                if (codigoOpcional.Length == 0 || !codigosMasUtilizados.Add(codigoOpcional))
                    continue;

                var metodo = rows.FirstOrDefault(x => string.Equals(
                    x.CodigoOpcional.Trim(), codigoOpcional, StringComparison.OrdinalIgnoreCase));

                if (metodo is null)
                {
                    metodo = await cn.QuerySingleOrDefaultAsync<PuntoVentaPaymentMethodDto>(new CommandDefinition(
                        """
                        SELECT TOP (1)
                            LTRIM(RTRIM(CODIGO)) AS Codigo,
                            ISNULL(LTRIM(RTRIM(CodigoOpcional)), '') AS CodigoOpcional,
                            ISNULL(LTRIM(RTRIM(DESCRIPCION)), '') AS Descripcion,
                            ISNULL(LTRIM(RTRIM(MEDIODEPAGO)), '') AS MedioDePago,
                            ISNULL(LTRIM(RTRIM(MONEDA)), '') AS Moneda
                        FROM dbo.MA_CUENTAS
                        WHERE UPPER(LTRIM(RTRIM(CodigoOpcional))) = UPPER(@CodigoOpcional);
                        """,
                        new { CodigoOpcional = codigoOpcional },
                        cancellationToken: token));
                }

                if (metodo is not null)
                    masUtilizados.Add(metodo);
            }

            if (masUtilizados.Count > 0)
                return masUtilizados;

            if (rows.Count > 0)
                return (IReadOnlyList<PuntoVentaPaymentMethodDto>)rows;

            if (!await TableExistsAsync(cn, "TA_CONFIGURACION", token))
                return [];

            rows = (await cn.QueryAsync<PuntoVentaPaymentMethodDto>(new CommandDefinition(
                """
                SELECT TOP (1)
                    LTRIM(RTRIM(b.CODIGO)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(b.CodigoOpcional)), '') AS CodigoOpcional,
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

    public Task<IReadOnlyList<PuntoVentaRubroDto>> GetRubrosAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetRubros", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await ObjectExistsAsync(cn, "V_TA_Rubros", null, token))
                return [];

            var rows = await cn.QueryAsync<PuntoVentaRubroDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IdRubro)) AS IdRubro,
                       ISNULL(LTRIM(RTRIM(Descripcion)), '') AS Descripcion
                FROM dbo.V_TA_Rubros
                ORDER BY Descripcion, IdRubro;
                """, cancellationToken: token));
            return (IReadOnlyList<PuntoVentaRubroDto>)rows.ToList();
        }, "No se pudieron cargar los rubros del punto de venta.", ct);

    public Task<PuntoVentaSaleResultDto> CreateSaleAsync(PuntoVentaSaleRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CreateSale", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var items = request.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.IdArticulo) && x.Cantidad != 0)
                .ToList();

            if (items.Count == 0)
                throw new InvalidOperationException("No hay artículos cargados para cobrar.");

            var pagos = request.Pagos
                .Where(x => !string.IsNullOrWhiteSpace(x.CodigoMedioPago) && x.Importe > 0)
                .ToList();

            if (pagos.Count == 0)
                throw new InvalidOperationException("No se informaron medios de pago.");

            var totalItems = decimal.Round(items.Sum(x => x.Subtotal), 2);
            var totalRecargos = decimal.Round(pagos.Sum(x => Math.Max(x.Recargo, 0m)), 2);
            var totalFactura = decimal.Round(totalItems + totalRecargos, 2);
            var totalPagos = decimal.Round(pagos.Sum(x => x.Importe), 2);
            if (totalPagos + 0.01m < totalFactura)
                throw new InvalidOperationException("El total cobrado debe cubrir al menos el total del carrito.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // La clave es opcional: si no existe se conserva el comportamiento
            // histórico, interpretando el recargo cobrado como importe final con IVA.
            var recargoTarjetaSinIva = ParseBooleanConfig(
                await TryReadConfigValueAsync(cn, "cfgRecargoTJTSinIVA", token), false);
            var tasaIvaRecargo = ResolverTasaIvaRecargo(items);
            var itemsFactura = items.ToList();
            foreach (var pago in pagos.Where(x => x.Recargo > 0))
            {
                itemsFactura.Add(new PuntoVentaCartItemDto
                {
                    IdArticulo = SurchargeArticleCode,
                    Descripcion = string.IsNullOrWhiteSpace(pago.Observaciones)
                        ? "Recargo tarjeta"
                        : pago.Observaciones.Trim(),
                    Presentacion = string.Empty,
                    PrecioUnitario = decimal.Round(pago.Recargo, 2),
                    Cantidad = 1m,
                    TasaIva = recargoTarjetaSinIva ? 0m : tasaIvaRecargo,
                    NoGravado = recargoTarjetaSinIva,
                    Familia = ""
                });
            }
            await EnsureRequiredSaleProceduresAsync(cn, token);

            // La pantalla ya envía tipo y sucursal. No hace falta volver a
            // cargar el contexto completo del POS ni toda la configuración
            // (incluye usuario, permisos y varias consultas adicionales).
            var cuentaConsumidorFinal = await TryReadConfigValueAsync(cn, "CUENTACONSUMIDORFINAL", token);
            var tcConfig = await GetTipoComprobanteConfigAsync(cn, token);
            var tc = string.IsNullOrWhiteSpace(request.TipoComprobante) ? DefaultTc : request.TipoComprobante.Trim();
            if (tc.Equals("FP", StringComparison.OrdinalIgnoreCase)
                || tc.Equals("NCFP", StringComparison.OrdinalIgnoreCase))
            {
                await ValidarPermisoProformaAsync(cn, token);
                var soloEfectivo = ParseBooleanConfig(
                    await TryReadConfigValueAsync(cn, "CobranzaPFSoloEfectivo", token), false);
                if (soloEfectivo)
                    await ValidarCobranzaProformaSoloEfectivoAsync(cn, pagos, token);
            }
            var sucursalConfigurada = string.IsNullOrWhiteSpace(request.Sucursal)
                ? await TryReadConfigValueAsync(cn, "TPV_SUCURSAL", token)
                : string.Empty;
            var sucursal = !string.IsNullOrWhiteSpace(request.Sucursal)
                ? NormalizeSucursal(request.Sucursal)
                : !string.IsNullOrWhiteSpace(sucursalConfigurada)
                    ? NormalizeSucursal(sucursalConfigurada)
                    : ResolveSucursalDefault(tcConfig).Code;
            var cliente = !string.IsNullOrWhiteSpace(request.CuentaCliente)
                ? request.CuentaCliente.Trim()
                : cuentaConsumidorFinal.Trim();

            if (string.IsNullOrWhiteSpace(cliente))
                throw new InvalidOperationException("No se pudo resolver la cuenta de consumidor final para grabar la venta.");

            var esConsumidorFinal = request.ClienteEventual is not null
                ? EsConsumidorFinalIva(request.ClienteEventual.CondicionIva)
                : string.Equals(
                    cliente,
                    cuentaConsumidorFinal.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            var ivaCliente = request.ClienteEventual is not null
                && !string.IsNullOrWhiteSpace(request.ClienteEventual.CondicionIva)
                    ? request.ClienteEventual.CondicionIva.Trim()
                    : await GetClienteIvaAsync(cn, cliente, token);
            var letra = tc.Equals("FP", StringComparison.OrdinalIgnoreCase)
                || tc.Equals("NCFP", StringComparison.OrdinalIgnoreCase)
                ? "X"
                : ResolveLetraForCliente(
                    tc,
                    request.Letra,
                    esConsumidorFinal,
                    ivaCliente,
                    tcConfig,
                    sucursal);

            var modoFalloCae = await arcaConfigService.ResolveModoFalloCaeAsync(cn, token);

            ArcaNumeracionPrevistaDto? numeracion = null;
            if (TiposDocumentoCore.EsFiscal(TiposDocumentoCore.TipoParaComprobante(tc, letra)))
            {
                if (request.Progreso is not null) await request.Progreso("Conectando con el servicio web de ARCA...");
                // ARCA debe informar el próximo número autorizado. Si se usa solo
                // la numeración local, WSFE rechaza la factura con el error 10016.
                try
                {
                    // La numeración no modifica la base local y se puede cancelar
                    // con seguridad. No dejamos que una demora interna de WSFEv1
                    // (por ejemplo, su pool Oracle agotado) congele el cobro hasta
                    // el timeout general de todo el cliente HTTP.
                    using var arcaNumeracionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    arcaNumeracionCts.CancelAfter(TimeSpan.FromSeconds(ArcaNumeracionTimeoutSeconds));
                    numeracion = await EjecutarEtapaVentaAsync(
                        "consultar la numeración en ARCA",
                        () => arcaFacturacion.ResolverNumeracionAsync(cn, PosUNegocio, letra, arcaNumeracionCts.Token, request.Progreso));
                    if (request.Progreso is not null) await request.Progreso("Numeración confirmada por ARCA. Guardando la cabecera...");
                }
                catch when (modoFalloCae == ArcaModoFalloCae.Degradado)
                {
                    numeracion = null;
                }
            }

            var numeroComprobante = numeracion?.NumeroFormateado;
            // El alta normal siempre crea una factura nueva. La reutilización de
            // un comprobante anterior ocurre en RetryCaeAsync, no en este flujo.
            // Evitamos una consulta previa a V_MV_CPTE que podía quedar bloqueada
            // por la vista y sumar decenas de segundos antes de ejecutar el SP.
            if (request.Progreso is not null) await request.Progreso("Preparando el guardado de la cabecera...");
            // La cabecera recibe una fecha tipada. Además fijamos la sesión
            // en YMD para compatibilidad con procedimientos/objetos antiguos
            // de bases que todavía convierten fechas internamente.
            await SetDateFormatYmdAsync(cn, token);
            if (request.Progreso is not null) await request.Progreso("Guardando la cabecera de la factura...");
            ComprobanteCreadoRow comprobante;
            var comprobanteExistente = false;
            try
            {
                comprobante = await EjecutarEtapaVentaAsync(
                    "crear la cabecera de la factura",
                    () => CreateReceiptAsync(
                        cn,
                        cliente,
                        request.Vendedor.Trim(),
                        request.Fecha == default ? DateTime.Today : request.Fecha,
                        request.Observaciones.Trim(),
                        tc,
                        sucursal,
                        letra,
                        numeroComprobante,
                        token));
            }
            catch (InvalidOperationException ex) when (EsDuplicadoComprobante(ex) && !string.IsNullOrWhiteSpace(numeroComprobante))
            {
                if (request.Progreso is not null)
                    await request.Progreso("La factura ya estaba iniciada. Recuperando el comprobante...");

                comprobante = await LoadComprobanteByKeysAsync(cn, tc, sucursal, numeroComprobante!, letra, token)
                    ?? throw new InvalidOperationException("ARCA devolvió un número que ya existe, pero no se pudo recuperar la cabecera de la factura.", ex);
                comprobanteExistente = true;
            }

            // Si el comprobante quedó creado por un intento anterior que AFIP rechazó,
            // puede conservar los datos del cliente anterior. Se sincroniza la cabecera
            // antes de volver a solicitar el CAE para que documento y condición de IVA
            // coincidan con el cliente seleccionado en la venta actual.
            if (comprobanteExistente)
                await EjecutarEtapaVentaAsync(
                    "actualizar los datos del cliente en la factura",
                    () => RefreshReceiptCustomerDataAsync(cn, comprobante.IdComprobanteTexto, comprobante.Tc, cliente, token));

            // Un cliente eventual usa la cuenta de consumidor final para la
            // imputación, pero sus datos deben quedar en la cabecera del
            // comprobante para que salgan en la factura/PDF y se envíen a ARCA.
            if (request.ClienteEventual is not null)
                await EjecutarEtapaVentaAsync(
                    "actualizar los datos del cliente eventual",
                    () => ApplyEventualCustomerDataAsync(cn, comprobante.IdComprobanteTexto, comprobante.Tc, request.ClienteEventual, token));

            var comprobanteTieneItems = comprobanteExistente
                && await ReceiptHasItemsAsync(cn, comprobante.IdComprobanteTexto, comprobante.Tc, token);
            if (!comprobanteTieneItems)
            {
                if (request.Progreso is not null) await request.Progreso("Guardando los artículos de la factura...");
                foreach (var item in items)
                {
                    await EjecutarEtapaVentaAsync(
                        $"grabar el artículo {item.IdArticulo}",
                        () => AddReceiptItemAsync(cn, comprobante.IdComprobante, item, token));
                }
            }
            if (itemsFactura.Any(x => x.IdArticulo == SurchargeArticleCode)
                && !await ReceiptHasSurchargeObservationAsync(cn, comprobante.IdComprobanteTexto, comprobante.Tc, token))
            {
                foreach (var item in itemsFactura.Where(x => x.IdArticulo == SurchargeArticleCode))
                    await EjecutarEtapaVentaAsync(
                        "grabar la observación del recargo de tarjeta",
                        () => AddReceiptSurchargeObservationAsync(
                            cn,
                            comprobante.Tc,
                            comprobante.IdComprobanteTexto,
                            item,
                            recargoTarjetaSinIva,
                            tasaIvaRecargo,
                            token));
            }

            if (totalRecargos > 0)
            {
                await EjecutarEtapaVentaAsync(
                    "incorporar el recargo al total de la factura",
                    () => UpdateReceiptSurchargeAsync(
                        cn,
                        comprobante.IdComprobante,
                        totalFactura,
                        pagos.Where(x => x.Recargo > 0).Sum(x => Math.Max(x.Recargo, 0m)),
                        recargoTarjetaSinIva,
                        tasaIvaRecargo,
                        token));
            }

            var caeIntento = ArcaCaeIntentoDto.NoAplica;
            if (TiposDocumentoCore.EsFiscal(TiposDocumentoCore.TipoParaComprobante(comprobante.Tc, comprobante.Letra)))
            {
                if (request.Progreso is not null) await request.Progreso("Enviando la factura a ARCA y obteniendo el CAE...");
                var contextoCae = new PuntoVentaCaeContextoDto(
                    comprobante.Tc, comprobante.IdComprobanteTexto, comprobante.Sucursal, comprobante.Numero,
                    comprobante.Letra, PosUNegocio, itemsFactura, totalFactura);
                caeIntento = await EjecutarEtapaVentaAsync(
                    "solicitar y guardar el CAE de ARCA",
                    () => arcaFacturacion.SolicitarCaeYPersistirAsync(cn, contextoCae, token, request.Progreso));

                if (caeIntento.Aplica && !caeIntento.Aprobado)
                {
                    if (modoFalloCae == ArcaModoFalloCae.Estricto)
                        throw new InvalidOperationException($"AFIP no autorizó el comprobante ({caeIntento.Motivo}). La venta no se completó -- el comprobante quedó grabado sin cobranza para reintentar.");

                    caeIntento = caeIntento with { Estado = ArcaCaeEstado.Pendiente };
                }
            }

            if (request.Progreso is not null) await request.Progreso("Generando el asiento de la factura...");
            // MV_Asientos_ValidaFechas es un trigger histórico que convierte
            // valores dd/MM/yyyy de TA_CONFIGURACION usando el DATEFORMAT de
            // la sesión. Fijamos DMY antes del asiento para que las bases
            // antiguas no dependan del idioma de la conexión SQL.
            await SetDateFormatDmyAsync(cn, token);
            await EjecutarEtapaVentaAsync(
                "crear el asiento de la factura",
                () => CreateInvoiceAccountingAsync(cn, comprobante.IdComprobante, token));

            // Las bases antiguas tienen procedimientos de cobranza que convierten
            // temporalmente la fecha a dd/mm/yyyy. Fijamos el formato de sesión
            // antes de ejecutarlos para que no dependan de la configuración regional
            // de la conexión SQL activa.
            if (request.Progreso is not null) await request.Progreso("Preparando la cobranza...");
            await SetDateFormatDmyAsync(cn, token);
            if (request.Progreso is not null) await request.Progreso("Creando la cobranza...");
            var idCobranza = await EjecutarEtapaVentaAsync(
                "crear la cobranza",
                () => CreateCollectionAsync(cn, comprobante.IdComprobante, token));
            if (request.Progreso is not null) await request.Progreso("Normalizando los datos de la cobranza...");
            await EjecutarEtapaVentaAsync(
                "normalizar las claves de la cobranza",
                () => NormalizeComprobanteKeysAsync(cn, idCobranza, token));
            if (request.Progreso is not null) await request.Progreso("Generando el asiento inicial...");
            await EjecutarEtapaVentaAsync(
                "crear el asiento inicial de la cobranza",
                () => CreatePaymentSeedAsync(cn, idCobranza, token));

            foreach (var pago in pagos)
            {
                if (request.Progreso is not null)
                    await request.Progreso($"Registrando el medio de pago {pago.DescripcionMedioPago.Trim()}...");
                await EjecutarEtapaVentaAsync(
                    $"grabar el medio de pago {pago.CodigoMedioPago}",
                    () => CreatePaymentLineAsync(cn, idCobranza, pago, token));
            }

            if (request.Progreso is not null) await request.Progreso("Aplicando la cobranza a la factura...");
            await EjecutarEtapaVentaAsync(
                "aplicar la cobranza a la factura",
                () => CreateCollectionApplicationAsync(cn, idCobranza, comprobante.IdComprobante, token));

            if (request.Progreso is not null) await request.Progreso("Finalizando la operación...");

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
                    Total = totalFactura,
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
                Total = totalFactura,
                CaeEstado = caeIntento.Estado.ToString(),
                Cae = caeIntento.Cae,
                CaeVencimiento = caeIntento.CaeVencimiento,
                CaeMotivo = caeIntento.Motivo
            };
        }, "No se pudo registrar la venta y su cobranza en el punto de venta.", ct);

    public Task<ArcaCaeIntentoDto> RetryCaeAsync(int idComprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "RetryCae", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var comprobante = await LoadComprobanteAsync(cn, idComprobante, token)
                ?? throw new InvalidOperationException("No se encontró el comprobante para reintentar el CAE.");

            if (!TiposDocumentoCore.EsFiscal(TiposDocumentoCore.TipoParaComprobante(comprobante.Tc, comprobante.Letra)))
                throw new InvalidOperationException("El comprobante no es una factura -- no corresponde pedir CAE.");

            var items = await LoadReceiptItemsForRetryAsync(cn, comprobante.Tc, comprobante.IdComprobanteTexto, token);
            var total = await LoadReceiptTotalAsync(cn, comprobante.IdComprobante, token);
            if (total <= 0)
                total = items.Sum(x => x.Subtotal);

            var contextoCae = new PuntoVentaCaeContextoDto(
                comprobante.Tc, comprobante.IdComprobanteTexto, comprobante.Sucursal, comprobante.Numero,
                comprobante.Letra, PosUNegocio, items, total);

            return await arcaFacturacion.SolicitarCaeYPersistirAsync(cn, contextoCae, token);
        }, "No se pudo reintentar la solicitud de CAE.", ct);

    private static async Task<IReadOnlyList<PuntoVentaCartItemDto>> LoadReceiptItemsForRetryAsync(SqlConnection cn, string tc, string idComprobanteTexto, CancellationToken ct)
    {
        var rows = await cn.QueryAsync<(string IdArticulo, decimal TotalFinal, decimal AlicIva, bool Exento)>(new CommandDefinition(
            """
            SELECT ISNULL(LTRIM(RTRIM(IDARTICULO)), '') AS IdArticulo,
                   ISNULL(CONVERT(decimal(15,2), TOTALFINAL), 0) AS TotalFinal,
                   ISNULL(CONVERT(decimal(9,4), ALICIVA), 0) AS AlicIva,
                   CAST(ISNULL(EXENTO, 0) AS bit) AS Exento
            FROM dbo.V_MV_CPTEINSUMOS
            WHERE TC = @Tc AND IDCOMPROBANTE = @IdComprobante;
            """,
            new { Tc = tc, IdComprobante = idComprobanteTexto },
            cancellationToken: ct));

        return rows.Select(r => new PuntoVentaCartItemDto
        {
            IdArticulo = r.IdArticulo,
            Cantidad = 1,
            PrecioUnitario = r.TotalFinal,
            TasaIva = r.AlicIva,
            Exento = r.Exento
        }).ToList();
    }

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

    public Task<IReadOnlyList<PuntoVentaReceiptListItemDto>> GetRecentReceiptsAsync(string tipoComprobante, string? sucursal = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetRecentReceipts", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var tc = string.IsNullOrWhiteSpace(tipoComprobante) ? DefaultTc : tipoComprobante.Trim();
            var rows = await cn.QueryAsync<PuntoVentaReceiptListItemDto>(new CommandDefinition(
                """
                SELECT TOP (10)
                    ID AS IdComprobante,
                    ISNULL(LTRIM(RTRIM(TC)), '') AS TipoComprobante,
                    ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto,
                    ISNULL(FechaHora_Grabacion, FECHA) AS FechaHora,
                    ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS Cliente,
                    ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS Total,
                    CAST(ISNULL(Impreso, 0) AS bit) AS Impreso
                FROM dbo.V_MV_CPTE
                WHERE UPPER(LTRIM(RTRIM(TC))) = @Tc
                  AND (@Sucursal IS NULL OR LTRIM(RTRIM(SUCURSAL)) = @Sucursal)
                  AND (@FechaDesde IS NULL OR ISNULL(FechaHora_Grabacion, FECHA) >= @FechaDesde)
                  AND (@FechaHasta IS NULL OR ISNULL(FechaHora_Grabacion, FECHA) < DATEADD(day, 1, @FechaHasta))
                  AND ISNULL(ANULADA, 0) = 0
                ORDER BY ISNULL(FechaHora_Grabacion, FECHA) DESC, ID DESC;
                """,
                new
                {
                    Tc = tc.ToUpperInvariant(),
                    Sucursal = string.IsNullOrWhiteSpace(sucursal) ? null : NormalizeSucursal(sucursal),
                    FechaDesde = fechaDesde?.Date,
                    FechaHasta = fechaHasta?.Date
                },
                cancellationToken: token));

            return (IReadOnlyList<PuntoVentaReceiptListItemDto>)rows.ToList();
        }, "No se pudieron cargar los comprobantes recientes del punto de venta.", ct);

    public Task<PuntoVentaReceiptDataDto> GetReceiptDataAsync(int idComprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetReceiptData", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var header = await cn.QuerySingleOrDefaultAsync<ReceiptHeaderRow>(new CommandDefinition(
                """
                SELECT TOP (1)
                    ID AS IdComprobante,
                    ISNULL(LTRIM(RTRIM(TC)), '') AS Tc,
                    ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto,
                    ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS Importe,
                    FECHA AS Fecha,
                    ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS Nombre,
                    ISNULL(LTRIM(RTRIM(CUENTA)), '') AS Cuenta,
                    CAST(ISNULL(Impreso, 0) AS bit) AS Impreso,
                    ISNULL(LTRIM(RTRIM(SUCURSAL)), '') AS Sucursal,
                    ISNULL(LTRIM(RTRIM(NUMERO)), '') AS Numero,
                    ISNULL(LTRIM(RTRIM(LETRA)), '') AS Letra
                FROM dbo.V_MV_CPTE
                WHERE ID = @IdComprobante;
                """,
                new { IdComprobante = idComprobante },
                cancellationToken: token));

            if (header is null)
                throw new InvalidOperationException("No se encontró el comprobante solicitado para reimpresión.");

            var items = (await cn.QueryAsync<PuntoVentaCartItemDto>(new CommandDefinition(
                """
                SELECT
                    ISNULL(LTRIM(RTRIM(IDARTICULO)), '') AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(DESCRIPCION)), '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(IDUNIDAD)), '') AS Presentacion,
                    ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS PrecioUnitario,
                    ISNULL(CONVERT(decimal(15,2), CANTIDAD), 0) AS Cantidad
                FROM dbo.V_MV_CpteInsumos
                WHERE IDCOMPROBANTE = @IdComprobanteTexto
                  AND TC = @Tc
                ORDER BY ISNULL(SECUENCIA, 0), ISNULL(ID, 0);
                """,
                new { header.IdComprobanteTexto, header.Tc },
                cancellationToken: token))).ToList();

            var payments = (await cn.QueryAsync<PuntoVentaPaymentLineDto>(new CommandDefinition(
                """
                SELECT
                    ISNULL(b.CUENTA, '') AS CodigoMedioPago,
                    ISNULL(c.DESCRIPCION, ISNULL(b.CUENTA, '')) AS DescripcionMedioPago,
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
                  AND a.IDComprobante_ORIGEN = @IdComprobanteTexto
                  AND b.[DEBE-HABER] = 'D';
                """,
                new { header.Tc, header.IdComprobanteTexto },
                cancellationToken: token))).ToList();

            var context = await GetReceiptContextAsync(header.Cuenta, token);

            return new PuntoVentaReceiptDataDto
            {
                Resultado = new PuntoVentaSaleResultDto
                {
                    IdComprobante = header.IdComprobante,
                    IdCobranza = 0,
                    TipoComprobante = header.Tc,
                    Sucursal = header.Sucursal,
                    Numero = header.Numero,
                    Letra = header.Letra,
                    IdComprobanteTexto = header.IdComprobanteTexto,
                    Total = header.Importe
                },
                Contexto = context,
                Items = items,
                Pagos = payments,
                Impreso = header.Impreso
            };
        }, "No se pudo cargar el comprobante para reimpresión.", ct);

    public Task MarkReceiptPrintedAsync(int idComprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "MarkReceiptPrinted", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.V_MV_CPTE
                SET Impreso = 1
                WHERE ID = @IdComprobante;
                """,
                new { IdComprobante = idComprobante },
                cancellationToken: token));

            return true;
        }, "No se pudo actualizar el estado de impresión del comprobante.", ct);

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
            var rubroFilter = filters.IdRubro.Trim();
            var search = filters.Texto.Trim();
            var take = filters.TamanioPagina <= 0 ? 24 : Math.Min(filters.TamanioPagina, 100);
            var pagina = filters.Pagina <= 0 ? 1 : filters.Pagina;
            var skip = (pagina - 1) * take;
            var fetch = take + 1;

            // Búsqueda multi-palabra: cada palabra debe aparecer en alguno de los campos
            var palabras = search.ToUpperInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var wordFilters = new StringBuilder();
            foreach (var (_, i) in palabras.Select((p, i) => (p, i)))
            {
                wordFilters.Append($"""

                  AND (
                        UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @Like{i}
                        OR UPPER(LTRIM(RTRIM(a.DESCRIPCION))) LIKE @Like{i}
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, '')))) LIKE @Like{i}
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA1, '')))) LIKE @Like{i}
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA2, '')))) LIKE @Like{i}
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA3, '')))) LIKE @Like{i}
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA4, '')))) LIKE @Like{i}
                  )
                """);
            }

            var parameters = new DynamicParameters();
            parameters.Add("Skip", skip);
            parameters.Add("Fetch", fetch);
            parameters.Add("IdLista", pricing.ListaPrecioActual);
            parameters.Add("IdFamilia", familyFilter);
            parameters.Add("IdRubro", rubroFilter);
            for (var i = 0; i < palabras.Length; i++)
                parameters.Add($"Like{i}", $"%{palabras[i]}%");

            var rows = await cn.QueryAsync<PuntoVentaArticleRow>(new CommandDefinition(
                $"""
                SELECT
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
                LEFT JOIN dbo.V_TA_Rubros r
                    ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.V_MA_Precios p
                    ON p.IdArticulo = a.IDARTICULO
                   AND p.IdLista = @IdLista
                   AND p.TipoLista = 'V'
                WHERE ISNULL(a.Suspendido, 0) <> 1
                  AND ISNULL(a.SuspendidoV, 0) <> 1
                  AND (@IdFamilia = '' OR LEFT(LTRIM(RTRIM(ISNULL(a.IdFamilia, ''))), 3) = @IdFamilia)
                  AND (@IdRubro = '' OR LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = @IdRubro)
                  {wordFilters}
                ORDER BY a.DESCRIPCION, a.IDARTICULO
                OFFSET @Skip ROWS
                FETCH NEXT @Fetch ROWS ONLY;
                """,
                parameters,
                cancellationToken: token));

            var materializedRows = rows.ToList();
            var tieneMasResultados = materializedRows.Count > take;
            if (tieneMasResultados)
                materializedRows.RemoveAt(materializedRows.Count - 1);

            var articles = materializedRows
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
                UsaPrecioFallback = usaFallback,
                TieneMasResultados = tieneMasResultados,
                PaginaActual = pagina
            };
        }, "No se pudieron cargar los artículos del punto de venta.", ct);

    public Task<PuntoVentaArticleDto?> GetArticuloPorCodigoAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetArticuloPorCodigo", async token =>
        {
            var buscar = (codigo ?? string.Empty).Trim().ToUpperInvariant();
            if (buscar.Length == 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "V_MA_ARTICULOS", token))
                return null;

            var pricing = await ResolvePricingContextAsync(cn, token);
            var clasePrecio = ParseClasePrecio(pricing.ClasePrecioActual);

            var row = await cn.QuerySingleOrDefaultAsync<PuntoVentaArticleRow>(new CommandDefinition(
                """
                SELECT TOP (1)
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
                  AND (
                        UPPER(LTRIM(RTRIM(a.IDARTICULO))) = @Codigo
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA,  '')))) = @Codigo
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA1, '')))) = @Codigo
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA2, '')))) = @Codigo
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA3, '')))) = @Codigo
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA4, '')))) = @Codigo
                      );
                """,
                new { Codigo = buscar, IdLista = pricing.ListaPrecioActual },
                cancellationToken: token));

            if (row is null)
                return null;

            return new PuntoVentaArticleDto
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
            };
        }, "No se pudo buscar el artículo por código.", ct);

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
            {
                var rutaImagenes = await TryReadConfigValueAsync(cn, "RUTAIMAGENES", token);
                path = ResolveArticleImagePathFromBase(codigo, rutaImagenes);
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            return new PuntoVentaArticleImageDto
            {
                RutaCompleta = path,
                NombreArchivo = Path.GetFileName(path),
                MimeType = InferImageMimeType(path)
            };
        }, "No se pudo resolver la imagen del artículo.", ct);

    public Task<IReadOnlyList<PuntoVentaCuentaImputacionDto>> GetCuentasImputacionAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetCuentasImputacion", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var rows = await cn.QueryAsync<PuntoVentaCuentaImputacionDto>(new CommandDefinition(
                """
                SELECT
                    LTRIM(RTRIM(codigo))      AS Codigo,
                    LTRIM(RTRIM(descripcion)) AS Descripcion
                FROM dbo.MA_CUENTAS
                WHERE (MEDIODEPAGO = '' OR MEDIODEPAGO IS NULL)
                  AND ISNULL(titulo, 0) = 0
                  AND ISNULL(CAJAYBANCO, 0) = 1
                ORDER BY descripcion;
                """,
                cancellationToken: token));

            return (IReadOnlyList<PuntoVentaCuentaImputacionDto>)rows.ToList();
        }, "No se pudieron cargar las cuentas de imputación.", ct);

    public Task<IReadOnlyList<PuntoVentaCuentaImputacionDto>> GetCuentasVentasAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetCuentasVentas", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var rows = await cn.QueryAsync<PuntoVentaCuentaImputacionDto>(new CommandDefinition(
                """
                SELECT
                    LTRIM(RTRIM(CODIGO)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(DESCRIPCION)), '') AS Descripcion
                FROM dbo.MA_CUENTAS
                WHERE ISNULL(TITULO, 0) = 0
                ORDER BY DESCRIPCION, CODIGO;
                """,
                cancellationToken: token));

            return (IReadOnlyList<PuntoVentaCuentaImputacionDto>)rows.ToList();
        }, "No se pudieron cargar las cuentas contables de ventas.", ct);

    public Task<PuntoVentaMovimientoCajaResultDto> CrearMovimientoCajaAsync(PuntoVentaMovimientoCajaRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CrearMovimientoCaja", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Importe <= 0)
                return new PuntoVentaMovimientoCajaResultDto { Ok = false, Mensaje = "El importe debe ser mayor a cero." };

            if (string.IsNullOrWhiteSpace(request.Cuenta))
                return new PuntoVentaMovimientoCajaResultDto { Ok = false, Mensaje = "Debe seleccionar una cuenta de imputación." };

            var tipo = request.Tipo is "I" or "E" ? request.Tipo : "I";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var parameters = new DynamicParameters();
            parameters.Add("pTipo", tipo);
            parameters.Add("pImporte", request.Importe);
            parameters.Add("pDetalle", (request.Detalle ?? string.Empty).Trim());
            parameters.Add("pCuentaImputacion", request.Cuenta.Trim());
            parameters.Add("pUsuario", appUserSession.GetCurrentUserName("SYSTEM").Trim());
            parameters.Add("pRes", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);
            parameters.Add("pMensaje", dbType: System.Data.DbType.String, size: 255, direction: System.Data.ParameterDirection.Output);

            await cn.ExecuteAsync(new CommandDefinition(
                """
                ALTER TABLE MV_ASIENTOS DISABLE TRIGGER ALL;
                SET NOCOUNT ON;
                EXEC sp_web_CreaAsientoIngresoEgreso @pTipo, @pImporte, @pDetalle, @pCuentaImputacion, @pUsuario, @pRes OUTPUT, @pMensaje OUTPUT;
                ALTER TABLE MV_ASIENTOS ENABLE TRIGGER ALL;
                """,
                parameters,
                cancellationToken: token));

            var resultado = parameters.Get<int>("pRes");
            var mensaje = parameters.Get<string>("pMensaje") ?? string.Empty;

            return new PuntoVentaMovimientoCajaResultDto
            {
                Ok = resultado == 11,
                Mensaje = mensaje
            };
        }, "No se pudo registrar el movimiento de caja.", ct);

    public Task<IReadOnlyList<PuntoVentaMovimientoCajaDetalleDto>> GetDetalleCajaHoyAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetDetalleCajaHoy", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // El SP inserta 2 asientos por movimiento: uno en la cuenta de cobro/pago
            // (efectivo, tarjeta, etc.) y otro en la cuenta de imputación.
            // Las cuentas de cobro/pago tienen MEDIODEPAGO con valor; las de imputación no.
            // Filtrando por MEDIODEPAGO vacío/NULL obtenemos solo el lado imputación,
            // sin importar cuántos medios de pago distintos existan.
            var rows = await cn.QueryAsync<PuntoVentaMovimientoCajaDetalleDto>(new CommandDefinition(
                """
                SELECT
                    ISNULL(LTRIM(RTRIM(m.CUENTA)), '')                       AS Cuenta,
                    ISNULL(LTRIM(RTRIM(c.descripcion)), '')                  AS Descripcion,
                    ISNULL(LTRIM(RTRIM(m.DETALLE)), '')                      AS Detalle,
                    ISNULL(m.FECHAHORA_GRABACION, CAST(m.FECHA AS datetime)) AS Fecha,
                    ISNULL(LTRIM(RTRIM(m.TC)), '')                           AS Tc,
                    ISNULL(LTRIM(RTRIM(m.NUMERO)), '')                       AS IdComprobante,
                    ISNULL(m.IMPORTE, 0)                                     AS Importe,
                    ISNULL(LTRIM(RTRIM(m.USUARIO_LOGEADO)), '')              AS Usuario,
                    CASE m.[DEBE-HABER]
                        WHEN 'H' THEN 'I'
                        WHEN 'D' THEN 'E'
                        ELSE ''
                    END                                                      AS TipoMovimiento
                FROM dbo.MV_ASIENTOS m
                INNER JOIN dbo.MA_CUENTAS c ON LTRIM(RTRIM(c.codigo)) = LTRIM(RTRIM(m.CUENTA))
                WHERE m.TC = 'CJA'
                  AND CAST(m.FECHA AS date) = CAST(GETDATE() AS date)
                  AND (c.MEDIODEPAGO = '' OR c.MEDIODEPAGO IS NULL)
                ORDER BY ISNULL(m.FECHAHORA_GRABACION, CAST(m.FECHA AS datetime)) DESC, m.NUMERO DESC;
                """,
                cancellationToken: token));

            return (IReadOnlyList<PuntoVentaMovimientoCajaDetalleDto>)rows.ToList();
        }, "No se pudo cargar el detalle de caja.", ct);

    public Task<IReadOnlyList<PuntoVentaConsolidadoCajaDto>> GetConsolidadoCajaHoyAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetConsolidadoCajaHoy", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // Agrupa todos los movimientos de hoy por cuenta de medio de pago.
            // Las cuentas con MEDIODEPAGO no vacío son efectivo, tarjeta, etc.
            // DEBE = entrada de dinero (cobranza o ingreso CJA), HABER = salida (egreso CJA).
            var rows = await cn.QueryAsync<PuntoVentaConsolidadoCajaDto>(new CommandDefinition(
                """
                SELECT
                    ''                                                                      AS IdCajas,
                    LTRIM(RTRIM(m.CUENTA))                                                  AS Cuenta,
                    ISNULL(LTRIM(RTRIM(c.DESCRIPCION)), '')                                 AS Descripcion,
                    ISNULL(SUM(CASE WHEN m.[DEBE-HABER] = 'D' THEN m.IMPORTE ELSE 0 END), 0) AS Ingresos,
                    ISNULL(SUM(CASE WHEN m.[DEBE-HABER] = 'H' THEN m.IMPORTE ELSE 0 END), 0) AS Egresos,
                    ISNULL(SUM(CASE WHEN m.[DEBE-HABER] = 'D' THEN m.IMPORTE ELSE -m.IMPORTE END), 0) AS Saldo,
                    ISNULL(LTRIM(RTRIM(m.MONEDA)), '   1')                                  AS Moneda,
                    ISNULL(MAX(m.COTIZACION), 1)                                            AS Cotizacion
                FROM dbo.MV_ASIENTOS m
                INNER JOIN dbo.MA_CUENTAS c ON LTRIM(RTRIM(c.codigo)) = LTRIM(RTRIM(m.CUENTA))
                WHERE CAST(m.FECHA AS date) = CAST(GETDATE() AS date)
                  AND (c.MEDIODEPAGO IS NOT NULL AND LTRIM(RTRIM(c.MEDIODEPAGO)) <> '')
                GROUP BY LTRIM(RTRIM(m.CUENTA)), LTRIM(RTRIM(c.DESCRIPCION)), LTRIM(RTRIM(m.MONEDA))
                ORDER BY LTRIM(RTRIM(c.DESCRIPCION));
                """,
                cancellationToken: token));

            return (IReadOnlyList<PuntoVentaConsolidadoCajaDto>)rows.ToList();
        }, "No se pudo cargar el consolidado de caja.", ct);

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
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));
    }

    private async Task<string> GetClienteIvaAsync(SqlConnection cn, string cliente, CancellationToken ct)
    {
        if (!await ObjectExistsAsync(cn, "VT_CLIENTES", null, ct))
            return string.Empty;

        return await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT TOP (1) ISNULL(LTRIM(RTRIM(IVA)), '')
            FROM dbo.VT_CLIENTES
            WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Cliente)));
            """,
            new { Cliente = cliente },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct)) ?? string.Empty;
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
        string? numeroOverride,
        CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.sp_web_Alta_Comprobante", cn)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = SaleCommandTimeoutSeconds
        };

        cmd.Parameters.AddWithValue("@pCliente", cliente);
        cmd.Parameters.AddWithValue("@pVendedor", string.IsNullOrWhiteSpace(vendedor) ? string.Empty : vendedor);
        cmd.Parameters.Add("@pFecha", System.Data.SqlDbType.DateTime).Value = fecha;
        cmd.Parameters.AddWithValue("@pObservaciones", string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones);
        cmd.Parameters.AddWithValue("@pLat", DBNull.Value);
        cmd.Parameters.AddWithValue("@pLng", DBNull.Value);
        cmd.Parameters.AddWithValue("@pTC", tc);
        cmd.Parameters.AddWithValue("@pSucursal", sucursal);
        // Numerado explícito (viene de ARCA/AFIP para facturas electrónicas, ver ResolverNumeracionAsync)
        // o DBNull para que el propio SP autonumere (comprobantes no fiscales o ARCA apagado).
        cmd.Parameters.AddWithValue("@pNumero", (object?)numeroOverride ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pLetra", letra);

        var resultadoParam = new SqlParameter("@pResultado", System.Data.SqlDbType.SmallInt) { Direction = System.Data.ParameterDirection.Output };
        var mensajeParam = new SqlParameter("@pMensaje", System.Data.SqlDbType.VarChar, 255) { Direction = System.Data.ParameterDirection.Output };
        var idParam = new SqlParameter("@pIdComprobanteRES", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        cmd.Parameters.Add(resultadoParam);
        cmd.Parameters.Add(mensajeParam);
        cmd.Parameters.Add(idParam);

        await cmd.ExecuteNonQueryAsync(ct);

        try
        {
            ValidateSpResult(
                ConvertToInt(resultadoParam.Value),
                Convert.ToString(mensajeParam.Value) ?? "No se pudo grabar el comprobante del POS.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"dbo.sp_web_Alta_Comprobante devolvió un error: {ex.Message}",
                ex);
        }

        var idComprobante = ConvertToInt(idParam.Value);
        ComprobanteCreadoRow? row;
        try
        {
            row = await LoadComprobanteAsync(cn, idComprobante, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"La cabecera se creó, pero no se pudo leer V_MV_CPTE: {ex.Message}",
                ex);
        }
        if (row is null)
            throw new InvalidOperationException("La venta se grabó pero no se pudo releer el comprobante generado.");

        return row;
    }

    private static async Task<string> ResolverNumeroLocalAsync(
        SqlConnection cn,
        string tc,
        string sucursal,
        string letra,
        CancellationToken ct)
    {
        const string sql = """
            SELECT ISNULL(MAX(CAST(NUMERO AS int)), 0) + 1
            FROM dbo.V_MV_CPTE
            WHERE ISNUMERIC(NUMERO) = 1
              AND UPPER(LTRIM(RTRIM(TC))) = UPPER(@Tc)
              AND LTRIM(RTRIM(SUCURSAL)) = @Sucursal
              AND UPPER(LTRIM(RTRIM(LETRA))) = UPPER(@Letra);
            """;

        var siguiente = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { Tc = tc, Sucursal = sucursal, Letra = letra },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

        return siguiente.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task RefreshReceiptCustomerDataAsync(
        SqlConnection cn,
        string idComprobante,
        string tc,
        string cliente,
        CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            """
            UPDATE v
               SET v.CUENTA = c.CODIGO,
                   v.NOMBRE = ISNULL(c.RAZON_SOCIAL, ''),
                   v.DOCUMENTOTIPO = c.DOCUMENTO_TIPO,
                   v.DOCUMENTONUMERO = LTRIM(RTRIM(ISNULL(c.NUMERO_DOCUMENTO, ''))),
                   v.CONDICIONIVA = c.IVA
            FROM dbo.V_MV_Cpte v
            INNER JOIN dbo.VT_CLIENTES c
                ON UPPER(LTRIM(RTRIM(c.CODIGO))) = UPPER(LTRIM(RTRIM(@Cliente)))
            WHERE v.TC = @Tc
              AND v.IDCOMPROBANTE = @IdComprobante;
            """, cn)
        {
            CommandTimeout = SaleCommandTimeoutSeconds
        };
        cmd.Parameters.AddWithValue("@IdComprobante", idComprobante);
        cmd.Parameters.AddWithValue("@Tc", tc);
        cmd.Parameters.AddWithValue("@Cliente", cliente);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"El procedimiento dbo.sp_web_Alta_Comprobante no pudo crear la cabecera: {ex.Message}",
                ex);
        }
    }

    private static async Task ApplyEventualCustomerDataAsync(
        SqlConnection cn,
        string idComprobante,
        string tc,
        PuntoVentaClienteEventualDto cliente,
        CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            """
            UPDATE v
               SET v.NOMBRE = COALESCE(NULLIF(@Nombre, ''), v.NOMBRE),
                   v.DOCUMENTOTIPO = CASE
                       WHEN NULLIF(@DocumentoTipo, '') IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM dbo.TA_TIPODOCUMENTO td
                            WHERE LTRIM(RTRIM(td.CODIGO)) = LTRIM(RTRIM(@DocumentoTipo))
                        ) THEN (
                            SELECT TOP (1) td.CODIGO
                            FROM dbo.TA_TIPODOCUMENTO td
                            WHERE LTRIM(RTRIM(td.CODIGO)) = LTRIM(RTRIM(@DocumentoTipo))
                        )
                       ELSE v.DOCUMENTOTIPO
                   END,
                   v.DOCUMENTONUMERO = COALESCE(NULLIF(@DocumentoNumero, ''), v.DOCUMENTONUMERO),
                   v.DOMICILIO = COALESCE(NULLIF(@Domicilio, ''), v.DOMICILIO),
                   v.LOCALIDAD = COALESCE(NULLIF(@Localidad, ''), v.LOCALIDAD),
                   v.IDPROVINCIA = CASE
                       WHEN NULLIF(@Provincia, '') IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM dbo.TA_ESTADOS e
                            WHERE LTRIM(RTRIM(e.CODIGO)) = LTRIM(RTRIM(@Provincia))
                        ) THEN (
                            SELECT TOP (1) e.CODIGO
                            FROM dbo.TA_ESTADOS e
                            WHERE LTRIM(RTRIM(e.CODIGO)) = LTRIM(RTRIM(@Provincia))
                        )
                       ELSE v.IDPROVINCIA
                   END,
                   v.CODIGOPOSTAL = COALESCE(NULLIF(@CodigoPostal, ''), v.CODIGOPOSTAL),
                   v.CONDICIONIVA = CASE
                       WHEN NULLIF(@CondicionIva, '') IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM dbo.TA_CONDIVA ci
                            WHERE LTRIM(RTRIM(ci.CODIGO)) = LTRIM(RTRIM(@CondicionIva))
                        ) THEN (
                            SELECT TOP (1) ci.CODIGO
                            FROM dbo.TA_CONDIVA ci
                            WHERE LTRIM(RTRIM(ci.CODIGO)) = LTRIM(RTRIM(@CondicionIva))
                        )
                       ELSE v.CONDICIONIVA
                   END
            FROM dbo.V_MV_Cpte v
            WHERE v.TC = @Tc
              AND v.IDCOMPROBANTE = @IdComprobante;
            """, cn)
        {
            CommandTimeout = SaleCommandTimeoutSeconds
        };
        cmd.Parameters.AddWithValue("@IdComprobante", idComprobante);
        cmd.Parameters.AddWithValue("@Tc", tc);
        cmd.Parameters.AddWithValue("@Nombre", cliente.RazonSocial.Trim());
        cmd.Parameters.AddWithValue("@DocumentoTipo", cliente.DocumentoTipo.Trim());
        cmd.Parameters.AddWithValue("@DocumentoNumero", cliente.NumeroDocumento.Trim());
        cmd.Parameters.AddWithValue("@Domicilio", cliente.Domicilio.Trim());
        cmd.Parameters.AddWithValue("@Localidad", cliente.Localidad.Trim());
        cmd.Parameters.AddWithValue("@Provincia", cliente.Provincia.Trim());
        cmd.Parameters.AddWithValue("@CodigoPostal", cliente.CodigoPostal.Trim());
        cmd.Parameters.AddWithValue("@CondicionIva", cliente.CondicionIva.Trim());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EnsureRequiredSaleProceduresAsync(SqlConnection cn, CancellationToken ct)
    {
        if (_requiredSaleProceduresChecked)
            return;

        var requiredProcedures = new[]
        {
            "sp_web_Alta_Comprobante",
            "sp_web_CpteInsumos",
            "sp_web_CreaCobPorFactura",
            "sp_web_creaLineaAsiento",
            "sp_web_CreaAplicacionCobranzaFactura",
            "sp_web_CreaAsientoFactura"
        };

        var missing = new List<string>();
        foreach (var procedure in requiredProcedures)
        {
            if (!await ObjectExistsAsync(cn, procedure, "P", ct))
                missing.Add(procedure);
        }

        if (missing.Count == 0)
        {
            _requiredSaleProceduresChecked = true;
            return;
        }

        throw new InvalidOperationException(
            $"La base activa no tiene los procedimientos del POS requeridos: {string.Join(", ", missing)}. Aplicá los updates SQL del punto de venta antes de cobrar.");
    }

    private async Task AddReceiptItemAsync(SqlConnection cn, int idComprobante, PuntoVentaCartItemDto item, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.sp_web_CpteInsumos", cn)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = SaleCommandTimeoutSeconds
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

    private async Task AddReceiptSurchargeObservationAsync(
        SqlConnection cn,
        string tc,
        string idComprobanteTexto,
        PuntoVentaCartItemDto item,
        bool recargoSinIva,
        decimal tasaIvaRecargo,
        CancellationToken ct)
    {
        var importe = decimal.Round(item.Subtotal, 2);
        var neto = recargoSinIva || tasaIvaRecargo <= 0m
            ? importe
            : decimal.Round(importe / (1m + tasaIvaRecargo / 100m), 2, MidpointRounding.AwayFromZero);

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = new SqlCommand(
                """
                INSERT INTO dbo.V_MV_CPTE_OBSERV
                (TC, IDCOMPROBANTE, IDCOMPLEMENTO, TIPO_OBS,
                 OBSERVACION, IMPORTE, IMPORTE_S_IVA, SECUENCIA)
                VALUES
                (@Tc, @IdComprobante, 0, @TipoObs,
                 @Observacion, @Importe, @ImporteSinIva,
                 ISNULL((SELECT MAX(SECUENCIA)
                         FROM dbo.V_MV_CPTE_OBSERV
                         WHERE TC = @Tc
                           AND IDCOMPROBANTE = @IdComprobante
                           AND IDCOMPLEMENTO = 0), -1) + 1);
                """, cn, tx)
            {
                CommandTimeout = SaleCommandTimeoutSeconds
            };
            cmd.Parameters.AddWithValue("@Tc", tc);
            cmd.Parameters.AddWithValue("@IdComprobante", idComprobanteTexto);
            // El circuito legacy guarda los otros conceptos con TIPO_OBS = OC.
            cmd.Parameters.AddWithValue("@TipoObs", "OC");
            cmd.Parameters.Add("@Observacion", System.Data.SqlDbType.NText).Value = item.Descripcion.Trim();
            cmd.Parameters.AddWithValue("@Importe", importe);
            cmd.Parameters.AddWithValue("@ImporteSinIva", neto);
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<int> CreateCollectionAsync(SqlConnection cn, int idComprobante, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.sp_web_CreaCobPorFactura", cn)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = SaleCommandTimeoutSeconds
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

    private static async Task CreateInvoiceAccountingAsync(SqlConnection cn, int idComprobante, CancellationToken ct)
    {
        const string sql = """
            DECLARE @pResultado smallint;
            DECLARE @pMensaje varchar(255);
            EXEC dbo.sp_web_CreaAsientoFactura @pIdCpte, @pResultado OUTPUT, @pMensaje OUTPUT;
            SELECT @pResultado AS Resultado, @pMensaje AS Mensaje;
            """;

        var result = await cn.QuerySingleAsync<SpResultRow>(new CommandDefinition(
            sql,
            new { pIdCpte = idComprobante },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

        ValidateSpResult(result.Resultado, result.Mensaje);
    }

    private static async Task SetDateFormatDmyAsync(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SET DATEFORMAT DMY;", cn)
        {
            CommandTimeout = SaleCommandTimeoutSeconds
        };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task SetDateFormatYmdAsync(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SET DATEFORMAT YMD;", cn)
        {
            CommandTimeout = SaleCommandTimeoutSeconds
        };

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EjecutarEtapaVentaAsync(string etapa, Func<Task> operacion)
    {
        var reloj = Stopwatch.StartNew();
        try
        {
            await operacion();
            logger.LogInformation("POS CreateSale etapa {Etapa}: {DuracionMs} ms", etapa, reloj.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "POS CreateSale etapa {Etapa} falló luego de {DuracionMs} ms", etapa, reloj.ElapsedMilliseconds);
            throw new InvalidOperationException($"Falló la etapa '{etapa}'. {ex.Message}", ex);
        }
    }

    private async Task<T> EjecutarEtapaVentaAsync<T>(string etapa, Func<Task<T>> operacion)
    {
        var reloj = Stopwatch.StartNew();
        try
        {
            var resultado = await operacion();
            logger.LogInformation("POS CreateSale etapa {Etapa}: {DuracionMs} ms", etapa, reloj.ElapsedMilliseconds);
            return resultado;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "POS CreateSale etapa {Etapa} falló luego de {DuracionMs} ms", etapa, reloj.ElapsedMilliseconds);
            throw new InvalidOperationException($"Falló la etapa '{etapa}'. {ex.Message}", ex);
        }
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
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));
    }

    private async Task CreatePaymentSeedAsync(SqlConnection cn, int idCobranza, CancellationToken ct)
    {
        const string sql = """
            DECLARE @pResultado INT;
            DECLARE @pMensaje NVARCHAR(250);
            EXEC dbo.sp_web_creaLineaAsiento @pIdCobranza, 0, '', 1, NULL, @pResultado OUTPUT, @pMensaje OUTPUT;
            SELECT @pResultado AS Resultado, @pMensaje AS Mensaje;
            """;

        var result = await cn.QuerySingleAsync<SpResultRow>(new CommandDefinition(
            sql,
            new { pIdCobranza = idCobranza },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

        ValidateSpResult(result.Resultado, result.Mensaje);
    }

    private async Task CreatePaymentLineAsync(SqlConnection cn, int idCobranza, PuntoVentaPaymentLineDto pago, CancellationToken ct)
    {
        if (_lineDetailParameterExists is null)
            _lineDetailParameterExists = await ProcedureHasParameterAsync(cn, "sp_web_creaLineaAsiento", "@pDetalle", ct);

        var incluirDetalle = !string.IsNullOrWhiteSpace(pago.Observaciones) && _lineDetailParameterExists.Value;
        var sql = !incluirDetalle
            ? """
                DECLARE @pResultado INT;
                DECLARE @pMensaje NVARCHAR(250);
                EXEC dbo.sp_web_creaLineaAsiento @pIdCobranza, @Importe, @Codigo, 0, NULL, @pResultado OUTPUT, @pMensaje OUTPUT;
                SELECT @pResultado AS Resultado, @pMensaje AS Mensaje;
                """
            : """
                DECLARE @pResultado INT;
                DECLARE @pMensaje NVARCHAR(250);
                EXEC dbo.sp_web_creaLineaAsiento @pIdCobranza, @Importe, @Codigo, 0, NULL, @pResultado OUTPUT, @pMensaje OUTPUT, @Detalle;
                SELECT @pResultado AS Resultado, @pMensaje AS Mensaje;
                """;

        var result = await cn.QuerySingleAsync<SpResultRow>(new CommandDefinition(
            sql,
            new
            {
                pIdCobranza = idCobranza,
                Importe = pago.Importe,
                Codigo = pago.CodigoMedioPago,
                Detalle = pago.Observaciones
            },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

        ValidateSpResult(result.Resultado, result.Mensaje);
    }

    private static async Task<bool> ProcedureHasParameterAsync(SqlConnection cn, string procedureName, string parameterName, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.parameters
                WHERE object_id = OBJECT_ID(@ObjectName)
                  AND name = @ParameterName
            ) THEN 1 ELSE 0 END;
            """,
            new { ObjectName = $"dbo.{procedureName}", ParameterName = parameterName },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct)) == 1;

    private async Task CreateCollectionApplicationAsync(SqlConnection cn, int idCobranza, int idComprobante, CancellationToken ct)
    {
        const string sql = """
            DECLARE @pResultado INT;
            DECLARE @pMensaje NVARCHAR(250);
            EXEC dbo.sp_web_CreaAplicacionCobranzaFactura @pIdCobranza, @pIdComprobante, @pResultado OUTPUT, @pMensaje OUTPUT;
            SELECT @pResultado AS Resultado, @pMensaje AS Mensaje;
            """;

        var result = await cn.QuerySingleAsync<SpResultRow>(new CommandDefinition(
            sql,
            new
            {
                pIdCobranza = idCobranza,
                pIdComprobante = idComprobante
            },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

        ValidateSpResult(result.Resultado, result.Mensaje);
    }

    private static async Task ValidarCobranzaProformaSoloEfectivoAsync(
        SqlConnection cn,
        IReadOnlyList<PuntoVentaPaymentLineDto> pagos,
        CancellationToken ct)
    {
        foreach (var pago in pagos)
        {
            var medio = await cn.QuerySingleOrDefaultAsync<PuntoVentaPaymentMethodDto>(new CommandDefinition(
                """
                SELECT TOP (1)
                    LTRIM(RTRIM(CODIGO)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(CodigoOpcional)), '') AS CodigoOpcional,
                    ISNULL(LTRIM(RTRIM(DESCRIPCION)), '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(MEDIODEPAGO)), '') AS MedioDePago
                FROM dbo.MA_CUENTAS
                WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)))
                   OR UPPER(LTRIM(RTRIM(CodigoOpcional))) = UPPER(LTRIM(RTRIM(@Codigo)));
                """,
                new { Codigo = pago.CodigoMedioPago },
                commandTimeout: SaleCommandTimeoutSeconds,
                cancellationToken: ct));

            if (medio is null || !EsMedioEfectivo(medio))
                throw new InvalidOperationException("Las cobranzas de proforma solo pueden realizarse en efectivo.");
        }
    }

    private async Task ValidarPermisoProformaAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await ColumnExistsAsync(cn, "TA_USUARIOS", "VerProforma", ct))
            return;

        var usuario = appUserSession.GetCurrentUserName(Environment.UserName).Trim();
        var sistema = appUserSession.CurrentUser?.SystemCode?.Trim() ?? string.Empty;
        var permitido = await cn.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            """
            SELECT TOP (1)
                CASE WHEN ISNULL(VerProforma, 1) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END
            FROM dbo.TA_USUARIOS
            WHERE UPPER(LTRIM(RTRIM(NOMBRE))) = UPPER(LTRIM(RTRIM(@Usuario)))
              AND (@Sistema = '' OR UPPER(LTRIM(RTRIM(SISTEMA))) = UPPER(LTRIM(RTRIM(@Sistema))));
            """,
            new { Usuario = usuario, Sistema = sistema },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

        if (permitido == false)
            throw new InvalidOperationException("El usuario no está autorizado a realizar comprobantes proforma.");
    }

    private static bool EsMedioEfectivo(PuntoVentaPaymentMethodDto medio)
    {
        var codigo = medio.Codigo?.Trim() ?? string.Empty;
        var codigoOpcional = medio.CodigoOpcional?.Trim() ?? string.Empty;
        var tipo = medio.MedioDePago?.Trim() ?? string.Empty;
        var descripcion = medio.Descripcion?.Trim() ?? string.Empty;

        return string.Equals(codigo, "EF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codigoOpcional, "EF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tipo, "EF", StringComparison.OrdinalIgnoreCase)
            || descripcion.Contains("EFECTIVO", StringComparison.OrdinalIgnoreCase)
            || (descripcion.Contains("CAJA", StringComparison.OrdinalIgnoreCase)
                && descripcion.Contains("EF", StringComparison.OrdinalIgnoreCase));
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
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

    private static async Task<ComprobanteCreadoRow?> LoadComprobanteByKeysAsync(
        SqlConnection cn,
        string tc,
        string sucursal,
        string numero,
        string letra,
        CancellationToken ct)
    {
        // El SP arma IDCOMPROBANTE como sucursal + número + letra. Buscar por
        // esa clave evita aplicar funciones sobre las columnas y permite usar
        // el índice de la cabecera en las bases con muchos comprobantes.
        var idComprobante = $"{sucursal}{numero}{letra}";
        var row = await cn.QuerySingleOrDefaultAsync<ComprobanteCreadoRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                ID AS IdComprobante,
                ISNULL(LTRIM(RTRIM(TC)), '') AS Tc,
                ISNULL(LTRIM(RTRIM(SUCURSAL)), '') AS Sucursal,
                ISNULL(LTRIM(RTRIM(NUMERO)), '') AS Numero,
                ISNULL(LTRIM(RTRIM(LETRA)), '') AS Letra,
                ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto
            FROM dbo.V_MV_CPTE
            WHERE IDCOMPROBANTE = @IdComprobante
            ORDER BY ID DESC;
            """,
            new { IdComprobante = idComprobante },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

        if (row is not null)
            return row;

        // Una venta nueva llega normalmente por aquí. No ejecutar una segunda
        // búsqueda con LTRIM/UPPER sobre toda la vista: en bases grandes esa
        // consulta podía demorar más que la propia alta de la cabecera.
        return null;
    }

    private static async Task<bool> ReceiptHasItemsAsync(
        SqlConnection cn,
        string idComprobante,
        string tc,
        CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.V_MV_CPTEINSUMOS
            WHERE IDCOMPROBANTE = @IdComprobante
              AND UPPER(LTRIM(RTRIM(TC))) = UPPER(@Tc);
            """,
            new { IdComprobante = idComprobante, Tc = tc },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct)) > 0;

    private static async Task<bool> ReceiptHasSurchargeObservationAsync(
        SqlConnection cn,
        string idComprobante,
        string tc,
        CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.V_MV_CPTE_OBSERV
            WHERE IDCOMPROBANTE = @IdComprobante
              AND UPPER(LTRIM(RTRIM(TC))) = UPPER(LTRIM(RTRIM(@Tc)))
              AND IDCOMPLEMENTO = 0
              AND TIPO_OBS = N'OC'
              AND UPPER(CONVERT(nvarchar(max), OBSERVACION)) LIKE N'%RECARGO%';
            """,
            new { IdComprobante = idComprobante, Tc = tc },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct)) > 0;

    private static async Task UpdateReceiptSurchargeAsync(
        SqlConnection cn,
        int idComprobante,
        decimal totalFactura,
        decimal totalRecargo,
        bool recargoSinIva,
        decimal tasaIvaRecargo,
        CancellationToken ct)
    {
        var importeRecargo = decimal.Round(Math.Max(totalRecargo, 0m), 2);
        var importeRecargoSinIva = recargoSinIva || tasaIvaRecargo <= 0m
            ? importeRecargo
            : decimal.Round(importeRecargo / (1m + tasaIvaRecargo / 100m), 2, MidpointRounding.AwayFromZero);
        var ivaRecargo = recargoSinIva || tasaIvaRecargo <= 0m
            ? 0m
            : decimal.Round(importeRecargo - importeRecargoSinIva, 2, MidpointRounding.AwayFromZero);

        const string sql = """
            DECLARE @ImporteActual money,
                    @ImporteSinIvaActual money,
                    @ImporteIvaActual money,
                    @NetoGravadoActual money,
                    @NetoNoGravadoActual money,
                    @OtrosActual money,
                    @RecargoAnterior money,
                    @RecargoAnteriorSinIva money;

            SELECT
                @ImporteActual = ISNULL(IMPORTE, 0),
                @ImporteSinIvaActual = ISNULL(IMPORTE_S_IVA, 0),
                @ImporteIvaActual = ISNULL(ImporteIva, 0),
                @NetoGravadoActual = ISNULL(NetoGravado, 0),
                @NetoNoGravadoActual = ISNULL(NetoNoGravado, 0),
                @OtrosActual = ISNULL(ImporteOtrosConceptos, 0)
            FROM dbo.V_MV_CPTE
            WHERE ID = @IdComprobante;

            -- El flujo de reintento puede volver a pasar por acá. Se descuenta
            -- el recargo ya registrado para que la actualización sea idempotente.
            SELECT
                @RecargoAnterior = ISNULL(SUM(IMPORTE), 0),
                @RecargoAnteriorSinIva = ISNULL(SUM(IMPORTE_S_IVA), 0)
            FROM dbo.V_MV_CPTE_OBSERV
            WHERE TC = (SELECT TOP (1) TC FROM dbo.V_MV_CPTE WHERE ID = @IdComprobante)
              AND IDCOMPROBANTE = (SELECT TOP (1) IDCOMPROBANTE FROM dbo.V_MV_CPTE WHERE ID = @IdComprobante)
              AND IDCOMPLEMENTO = (SELECT TOP (1) ISNULL(IDCOMPLEMENTO, 0) FROM dbo.V_MV_CPTE WHERE ID = @IdComprobante)
              AND TIPO_OBS = N'OC'
              AND UPPER(CONVERT(nvarchar(max), OBSERVACION)) LIKE N'%RECARGO%';

            UPDATE dbo.V_MV_CPTE
            SET IMPORTE = @TotalFactura,
                IMPORTE_S_IVA = @ImporteSinIvaActual - @RecargoAnteriorSinIva + @ImporteRecargoSinIva,
                ImporteIva = @ImporteIvaActual - (@RecargoAnterior - @RecargoAnteriorSinIva) + @IvaRecargo,
                NetoGravado = @NetoGravadoActual
                    - CASE WHEN @RecargoAnteriorSinIva = @RecargoAnterior THEN 0 ELSE @RecargoAnteriorSinIva END
                    + CASE WHEN @RecargoSinIva = 1 THEN 0 ELSE @ImporteRecargoSinIva END,
                NetoNoGravado = @NetoNoGravadoActual
                    - CASE WHEN @RecargoAnteriorSinIva = @RecargoAnterior THEN @RecargoAnteriorSinIva ELSE 0 END
                    + CASE WHEN @RecargoSinIva = 1 THEN @ImporteRecargoSinIva ELSE 0 END,
                ImporteOtrosConceptos = @OtrosActual - @RecargoAnterior + @ImporteRecargo
            WHERE ID = @IdComprobante;
            """;

        await cn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                IdComprobante = idComprobante,
                TotalFactura = totalFactura,
                ImporteRecargo = importeRecargo,
                ImporteRecargoSinIva = importeRecargoSinIva,
                IvaRecargo = ivaRecargo,
                RecargoSinIva = recargoSinIva
            },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));
    }

    private static decimal ResolverTasaIvaRecargo(IReadOnlyList<PuntoVentaCartItemDto> items)
    {
        // Si todos los artículos comparten alícuota, esa es la referencia natural
        // del recargo. Con artículos mixtos se toma la alícuota del grupo con mayor
        // importe gravado, evitando elegir arbitrariamente el primer artículo.
        var grupo = items
            .Where(x => !x.Exento && x.TasaIva > 0m && x.Subtotal > 0m)
            .GroupBy(x => decimal.Round(x.TasaIva, 4))
            .Select(x => new { Tasa = x.Key, Importe = x.Sum(i => i.Subtotal) })
            .OrderByDescending(x => x.Importe)
            .ThenByDescending(x => x.Tasa)
            .FirstOrDefault();

        return grupo?.Tasa ?? 0m;
    }

    private static async Task<decimal> LoadReceiptTotalAsync(
        SqlConnection cn,
        int idComprobante,
        CancellationToken ct)
        => await cn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) FROM dbo.V_MV_CPTE WHERE ID = @IdComprobante;",
            new { IdComprobante = idComprobante },
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

    private static void ValidateSpResult(int resultado, string mensaje)
    {
        if (resultado == 11)
            return;

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(mensaje)
            ? "La operación SQL del punto de venta devolvió un estado inválido."
            : mensaje.Trim());
    }

    private static bool EsDuplicadoComprobante(Exception ex)
    {
        for (Exception? actual = ex; actual is not null; actual = actual.InnerException)
        {
            var mensaje = actual.Message ?? string.Empty;
            if (mensaje.Contains("PK_V_MV_Cpte", StringComparison.OrdinalIgnoreCase)
                || mensaje.Contains("clave duplicada", StringComparison.OrdinalIgnoreCase)
                || mensaje.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

    private static string BuildComprobanteHabitualConfigKey()
        => $"{Environment.MachineName.Trim()}_GOUR_CPTE_PC";

    private static bool ParseBooleanConfig(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return value.Trim().ToUpperInvariant() switch
        {
            "1" or "S" or "SI" or "SÍ" or "Y" or "YES" or "TRUE" or "T" => true,
            "0" or "N" or "NO" or "FALSE" or "F" => false,
            _ => defaultValue
        };
    }

    private static string ResolveComprobanteHabitual(string? value, bool usaProforma, bool verProformaUsuario)
    {
        var comprobante = (value ?? string.Empty).Trim().ToUpperInvariant();
        var esProforma = comprobante is "FP" or "NCFP";
        if (esProforma && (!usaProforma || !verProformaUsuario))
            return DefaultTc;

        return comprobante is "FC" or "NC" or "FP" or "NCFP" ? comprobante : DefaultTc;
    }

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

        await using var cmd = new SqlCommand(sql, cn)
        {
            CommandTimeout = SaleCommandTimeoutSeconds
        };
        cmd.Parameters.AddWithValue("@Clave", clave.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return string.Empty;

        return ResolveStoredValue(GetString(rd, 0), GetString(rd, 1));
    }

    private static async Task ValidateCuentaContableAsync(
        SqlConnection cn,
        SqlTransaction tx,
        string? codigo,
        CancellationToken ct)
    {
        var cuenta = codigo?.Trim() ?? string.Empty;
        if (cuenta.Length == 0)
            return;

        var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.MA_CUENTAS WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)))) THEN 1 ELSE 0 END;",
            new { Codigo = cuenta },
            tx,
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

        if (existe != 1)
            throw new InvalidOperationException("La cuenta de ventas seleccionada no existe en MA_CUENTAS.");
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

        await using var cmd = new SqlCommand(sql, cn)
        {
            CommandTimeout = SaleCommandTimeoutSeconds
        };
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

    private string? ResolveArticleImagePathFromBase(string idArticulo, string rutaImagenes)
    {
        var normalizedBase = NormalizeBasePath(rutaImagenes);
        if (string.IsNullOrWhiteSpace(normalizedBase))
            return null;

        var candidates = new[]
        {
            Path.Combine(normalizedBase, "thumbs4", $"{idArticulo}.jpg"),
            Path.Combine(normalizedBase, "thumbs4", $"{idArticulo}.jpeg"),
            Path.Combine(normalizedBase, "thumbs4", $"{idArticulo}.png"),
            Path.Combine(normalizedBase, "thumbs4", $"{idArticulo}.webp"),
            Path.Combine(normalizedBase, "thumbs4", $"{idArticulo}.gif"),
            Path.Combine(normalizedBase, "thumbs4", $"{idArticulo}.bmp"),
            Path.Combine(normalizedBase, $"{idArticulo}.jpg"),
            Path.Combine(normalizedBase, $"{idArticulo}.jpeg"),
            Path.Combine(normalizedBase, $"{idArticulo}.png"),
            Path.Combine(normalizedBase, $"{idArticulo}.webp"),
            Path.Combine(normalizedBase, $"{idArticulo}.gif"),
            Path.Combine(normalizedBase, $"{idArticulo}.bmp")
        };

        return candidates.FirstOrDefault(File.Exists);
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

    private static string NormalizeSucursal(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return int.TryParse(trimmed, out var number) && number >= 0 && number <= 9999
            ? number.ToString("0000")
            : trimmed.Length <= 4 ? trimmed.PadLeft(4, '0') : trimmed[..4];
    }

    private static string NormalizeOptionalSucursal(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeSucursal(value);

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

    private static string ResolveLetraForCliente(
        string tc,
        string? requestedLetter,
        bool esConsumidorFinal,
        string ivaCliente,
        TipoComprobanteRow? config,
        string sucursal)
    {
        if (tc.Equals("FP", StringComparison.OrdinalIgnoreCase)
            || tc.Equals("NCFP", StringComparison.OrdinalIgnoreCase))
            return "X";

        // Para comprobantes fiscales la letra depende de la condición de IVA del cliente:
        // RI (código 1) requiere A; consumidor final y las demás condiciones requieren B.
        var codigoIva = (ivaCliente ?? string.Empty).Trim().TrimStart('0');
        var letraPreferida = !esConsumidorFinal && codigoIva == "1" ? "A" : "B";
        var letrasDisponibles = config?.Letras?.ToUpperInvariant() ?? string.Empty;

        if (letrasDisponibles.Contains(letraPreferida, StringComparison.Ordinal))
            return letraPreferida;

        // Si la base no informa las letras configuradas, conservamos la selección explícita
        // y finalmente el comportamiento histórico como respaldo.
        if (!string.IsNullOrWhiteSpace(requestedLetter))
            return requestedLetter.Trim().ToUpperInvariant();

        return ResolveLetraDefault(config, sucursal);
    }

    private static bool EsConsumidorFinalIva(string? codigo)
    {
        var valor = (codigo ?? string.Empty).Trim().TrimStart('0');
        return valor.Length == 0 || valor == "3";
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
            commandTimeout: SaleCommandTimeoutSeconds,
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
            commandTimeout: SaleCommandTimeoutSeconds,
            cancellationToken: ct));

        return exists == 1;
    }

    private sealed class UserCajaRow
    {
        public string UserName { get; init; } = string.Empty;
        public string IdCaja { get; init; } = string.Empty;
        public bool Administrador { get; init; }
        public bool VerProforma { get; init; } = true;
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
        public string Cuenta { get; init; } = string.Empty;
        public bool Impreso { get; init; }
        public string Sucursal { get; init; } = string.Empty;
        public string Numero { get; init; } = string.Empty;
        public string Letra { get; init; } = string.Empty;
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

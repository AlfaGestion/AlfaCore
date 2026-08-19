using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Notas de Pedido (NP) del Portal Cliente. Reutiliza dbo.V_MV_Cpte / dbo.V_MV_CpteInsumos, la
// misma fuente que ya usan ComprobanteViewerService y PuntoVentaService para comprobantes de
// venta — no se inventa una tabla ni una vista paralela.
public sealed class PortalClienteService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents) : IPortalClienteService
{
    private const string ModuleName = "PortalCliente";
    private const string TcPedidoWeb = "NP";
    private const string SucursalPedidoWeb = "9999";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<PortalClientePedidoResumenDto>> GetPedidosClienteAsync(PortalClientePedidosFiltroDto filtro, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPedidosCliente", async token =>
        {
            ArgumentNullException.ThrowIfNull(filtro);

            var codigoCliente = (filtro.CodigoCliente ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigoCliente))
                throw new InvalidOperationException("No se pudo identificar al cliente. Volvé a iniciar sesión.");

            var pageNumber = filtro.PageNumber <= 0 ? 1 : filtro.PageNumber;
            var pageSize = filtro.PageSize <= 0 ? 10 : Math.Min(filtro.PageSize, 50);
            var numeroLike = string.IsNullOrWhiteSpace(filtro.Numero) ? null : $"%{filtro.Numero.Trim()}%";
            var fechaHastaExclusiva = filtro.FechaHasta?.Date.AddDays(1);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string whereSql = """
                UPPER(LTRIM(RTRIM(TC))) = @Tc
                AND LTRIM(RTRIM(SUCURSAL)) = @Sucursal
                AND UPPER(LTRIM(RTRIM(CUENTA))) = UPPER(LTRIM(RTRIM(@CodigoCliente)))
                AND (@FechaDesde IS NULL OR FECHA >= @FechaDesde)
                AND (@FechaHastaExclusiva IS NULL OR FECHA < @FechaHastaExclusiva)
                AND (@NumeroLike IS NULL OR LTRIM(RTRIM(IDCOMPROBANTE)) LIKE @NumeroLike)
                """;

            var parametros = new
            {
                Tc = TcPedidoWeb,
                Sucursal = SucursalPedidoWeb,
                CodigoCliente = codigoCliente,
                filtro.FechaDesde,
                FechaHastaExclusiva = fechaHastaExclusiva,
                NumeroLike = numeroLike,
                Offset = (pageNumber - 1) * pageSize,
                PageSize = pageSize
            };

            var total = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(*) FROM dbo.V_MV_Cpte WHERE {whereSql};",
                parametros, cancellationToken: token));

            var filas = await cn.QueryAsync<PedidoRow>(new CommandDefinition(
                $"""
                SELECT
                    ID AS IdComprobante,
                    ISNULL(LTRIM(RTRIM(TC)), '') AS Tc,
                    ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto,
                    FECHA AS Fecha,
                    ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS Total,
                    CAST(ISNULL(ANULADA, 0) AS bit) AS Anulada,
                    CAST(ISNULL(COMENTARIOS, '') AS nvarchar(max)) AS Comentarios
                FROM dbo.V_MV_Cpte
                WHERE {whereSql}
                ORDER BY FECHA DESC, ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
                parametros, cancellationToken: token));

            var items = filas.Select(MapResumen).ToList();

            return new PagedResult<PortalClientePedidoResumenDto>
            {
                Items = items,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar los pedidos.", ct);

    public Task<PortalClientePedidoDetalleDto?> GetPedidoClienteDetalleAsync(string codigoCliente, int idComprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPedidoClienteDetalle", async token =>
        {
            var codigo = (codigoCliente ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigo) || idComprobante <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var cabecera = await cn.QuerySingleOrDefaultAsync<CabeceraRow>(new CommandDefinition(
                """
                SELECT TOP (1)
                    ID AS IdComprobante,
                    ISNULL(LTRIM(RTRIM(TC)), '') AS Tc,
                    ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto,
                    FECHA AS Fecha,
                    ISNULL(LTRIM(RTRIM(CUENTA)), '') AS CodigoCliente,
                    ISNULL(LTRIM(RTRIM(NOMBRE)), '') AS RazonSocial,
                    ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS Total,
                    CAST(ISNULL(ANULADA, 0) AS bit) AS Anulada,
                    CAST(ISNULL(COMENTARIOS, '') AS nvarchar(max)) AS Comentarios
                FROM dbo.V_MV_Cpte
                WHERE ID = @IdComprobante
                  AND UPPER(LTRIM(RTRIM(TC))) = @Tc;
                """,
                new { IdComprobante = idComprobante, Tc = TcPedidoWeb },
                cancellationToken: token));

            // Nunca se distingue "no existe" de "es de otro cliente": en ambos casos se devuelve null,
            // para no confirmarle a quien manipule la URL que el comprobante existe.
            if (cabecera is null || !string.Equals(cabecera.CodigoCliente.Trim(), codigo, StringComparison.OrdinalIgnoreCase))
                return null;

            var lineas = await cn.QueryAsync<PortalClientePedidoLineaDto>(new CommandDefinition(
                """
                SELECT
                    ISNULL(LTRIM(RTRIM(IDARTICULO)), '') AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(DESCRIPCION)), '') AS Descripcion,
                    ISNULL(CONVERT(decimal(15,2), CANTIDAD), 0) AS Cantidad,
                    ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS PrecioUnitario,
                    ISNULL(CONVERT(decimal(15,2), TOTAL), 0) AS Subtotal
                FROM dbo.V_MV_CpteInsumos
                WHERE UPPER(LTRIM(RTRIM(IDCOMPROBANTE))) = UPPER(LTRIM(RTRIM(@IdComprobanteTexto)))
                  AND UPPER(LTRIM(RTRIM(TC))) = @Tc
                ORDER BY ISNULL(SECUENCIA, 0), ISNULL(ID, 0);
                """,
                new { cabecera.IdComprobanteTexto, Tc = TcPedidoWeb },
                cancellationToken: token));

            var (esWeb, idCatalogo) = ParseOrigenWeb(cabecera.Comentarios);

            return new PortalClientePedidoDetalleDto
            {
                IdComprobante = cabecera.IdComprobante,
                Tc = cabecera.Tc,
                IdComprobanteTexto = cabecera.IdComprobanteTexto,
                Fecha = cabecera.Fecha,
                CodigoCliente = cabecera.CodigoCliente,
                RazonSocial = cabecera.RazonSocial,
                Total = cabecera.Total,
                Anulada = cabecera.Anulada,
                EsPedidoWeb = esWeb,
                IdCatalogoWeb = idCatalogo,
                Lineas = lineas.ToList()
            };
        }, "No se pudo cargar el detalle del pedido.", ct);

    // Cuenta corriente: se apoya en dbo.VE_CPTES_SALDOS_VENTAS (saldos pendientes de venta, ya con
    // el signo resuelto por DEBE/HABER contable — no se reinterpretan TC como FC/NC/ND) y
    // dbo.VE_COBRANZAS_REALIZADAS (cobranzas ya filtradas por TC IN ('CB','CBFP','CBCT')). Ambas
    // son vistas oficiales existentes; no se agrega ninguna tabla ni lógica de cálculo de saldo nueva.
    public Task<PortalClienteCuentaCorrienteResumenDto> GetResumenCuentaCorrienteAsync(string codigoCliente, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetResumenCuentaCorriente", async token =>
        {
            var codigo = (codigoCliente ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se pudo identificar al cliente. Volvé a iniciar sesión.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            return await ConsultarResumenAsync(cn, codigo, token);
        }, "No pudimos consultar tu cuenta corriente en este momento.", ct);

    public Task<PortalClienteCuentaCorrienteDto> GetCuentaCorrienteAsync(PortalClienteCuentaCorrienteFiltroDto filtro, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetCuentaCorriente", async token =>
        {
            ArgumentNullException.ThrowIfNull(filtro);

            var codigo = (filtro.CodigoCliente ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se pudo identificar al cliente. Volvé a iniciar sesión.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var resumen = await ConsultarResumenAsync(cn, codigo, token);

            bool? soloVencidos = filtro.Filtro switch
            {
                PortalClienteComprobantesPendientesFiltro.Vencidos => true,
                PortalClienteComprobantesPendientesFiltro.AVencer => false,
                _ => null
            };

            var pendientes = await cn.QueryAsync<PendienteRow>(new CommandDefinition(
                """
                SELECT TOP (200)
                    ISNULL(LTRIM(RTRIM(s.TC)), '') AS Tc,
                    ISNULL(td.DESCRIPCION, '') AS TcDescripcion,
                    ISNULL(LTRIM(RTRIM(s.SUCURSAL)), '') AS Sucursal,
                    ISNULL(LTRIM(RTRIM(s.NUMERO)), '') AS Numero,
                    ISNULL(LTRIM(RTRIM(s.LETRA)), '') AS Letra,
                    s.FECHA AS Fecha,
                    s.VENCIMIENTO AS Vencimiento,
                    CONVERT(decimal(15,2), s.SALDO) AS Saldo,
                    cpte.ID AS IdComprobante,
                    CONVERT(decimal(15,2), cpte.IMPORTE) AS ImporteOriginal
                FROM dbo.VE_CPTES_SALDOS_VENTAS s
                LEFT JOIN dbo.V_TA_Cpte td ON UPPER(LTRIM(RTRIM(td.CODIGO))) = UPPER(LTRIM(RTRIM(s.TC)))
                LEFT JOIN dbo.V_MV_Cpte cpte
                    ON UPPER(LTRIM(RTRIM(cpte.TC))) = UPPER(LTRIM(RTRIM(s.TC)))
                   AND LTRIM(RTRIM(cpte.SUCURSAL)) = LTRIM(RTRIM(s.SUCURSAL))
                   AND LTRIM(RTRIM(cpte.NUMERO)) = LTRIM(RTRIM(s.NUMERO))
                   AND LTRIM(RTRIM(cpte.LETRA)) = LTRIM(RTRIM(s.LETRA))
                WHERE UPPER(LTRIM(RTRIM(s.CUENTA))) = UPPER(LTRIM(RTRIM(@CodigoCliente)))
                  AND (@SoloVencidos IS NULL
                       OR (@SoloVencidos = 1 AND CAST(s.VENCIMIENTO AS date) < CAST(@Hoy AS date))
                       OR (@SoloVencidos = 0 AND CAST(s.VENCIMIENTO AS date) >= CAST(@Hoy AS date)))
                  AND (@FechaDesde IS NULL OR s.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusiva IS NULL OR s.FECHA < @FechaHastaExclusiva)
                ORDER BY
                    CASE WHEN CAST(s.VENCIMIENTO AS date) < CAST(@Hoy AS date) THEN 0 ELSE 1 END,
                    s.VENCIMIENTO ASC;
                """,
                new
                {
                    CodigoCliente = codigo,
                    SoloVencidos = soloVencidos,
                    Hoy = DateTime.Today,
                    filtro.FechaDesde,
                    FechaHastaExclusiva = filtro.FechaHasta?.Date.AddDays(1)
                },
                cancellationToken: token));

            var hoy = DateTime.Today;
            var pendientesDto = pendientes.Select(p => new PortalClienteComprobantePendienteDto
            {
                Tc = p.Tc,
                TcDescripcion = p.TcDescripcion,
                Sucursal = p.Sucursal,
                Numero = p.Numero,
                Letra = p.Letra,
                Fecha = p.Fecha,
                Vencimiento = p.Vencimiento,
                Saldo = p.Saldo,
                ImporteOriginal = p.ImporteOriginal,
                IdComprobante = p.IdComprobante,
                EstaVencido = p.Vencimiento.Date < hoy
            }).ToList();

            var cobranzas = await cn.QueryAsync<PortalClienteCobranzaDto>(new CommandDefinition(
                """
                SELECT TOP (10)
                    FECHA AS Fecha,
                    ISNULL(LTRIM(RTRIM(TC)), '') AS Tc,
                    ISNULL(idcomprobante, '') AS IdComprobante,
                    ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS Importe,
                    ISNULL(LTRIM(RTRIM(DETALLE)), '') AS Detalle
                FROM dbo.VE_COBRANZAS_REALIZADAS
                WHERE UPPER(LTRIM(RTRIM(CUENTA))) = UPPER(LTRIM(RTRIM(@CodigoCliente)))
                ORDER BY FECHA DESC, FechaHora_Grabacion DESC;
                """,
                new { CodigoCliente = codigo },
                cancellationToken: token));

            return new PortalClienteCuentaCorrienteDto
            {
                Resumen = resumen,
                Pendientes = pendientesDto,
                Cobranzas = cobranzas.ToList()
            };
        }, "No pudimos consultar tu cuenta corriente en este momento.", ct);

    public Task<PortalClienteComprobantePendienteDetalleDto?> GetComprobanteClienteDetalleAsync(string codigoCliente, int idComprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetComprobanteClienteDetalle", async token =>
        {
            var codigo = (codigoCliente ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigo) || idComprobante <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var cabecera = await cn.QuerySingleOrDefaultAsync<ComprobanteCabeceraRow>(new CommandDefinition(
                """
                SELECT TOP (1)
                    v.ID AS IdComprobante,
                    ISNULL(LTRIM(RTRIM(v.TC)), '') AS Tc,
                    ISNULL(td.DESCRIPCION, '') AS TcDescripcion,
                    ISNULL(LTRIM(RTRIM(v.IDCOMPROBANTE)), '') AS IdComprobanteTexto,
                    v.FECHA AS Fecha,
                    ISNULL(LTRIM(RTRIM(v.CUENTA)), '') AS CodigoCliente,
                    ISNULL(LTRIM(RTRIM(v.NOMBRE)), '') AS RazonSocial,
                    ISNULL(CONVERT(decimal(15,2), v.IMPORTE), 0) AS ImporteOriginal,
                    s.VENCIMIENTO AS Vencimiento,
                    CONVERT(decimal(15,2), s.SALDO) AS SaldoPendiente
                FROM dbo.V_MV_Cpte v
                LEFT JOIN dbo.V_TA_Cpte td ON UPPER(LTRIM(RTRIM(td.CODIGO))) = UPPER(LTRIM(RTRIM(v.TC)))
                LEFT JOIN dbo.VE_CPTES_SALDOS_VENTAS s
                    ON UPPER(LTRIM(RTRIM(s.TC))) = UPPER(LTRIM(RTRIM(v.TC)))
                   AND LTRIM(RTRIM(s.SUCURSAL)) = LTRIM(RTRIM(v.SUCURSAL))
                   AND LTRIM(RTRIM(s.NUMERO)) = LTRIM(RTRIM(v.NUMERO))
                   AND LTRIM(RTRIM(s.LETRA)) = LTRIM(RTRIM(v.LETRA))
                WHERE v.ID = @IdComprobante;
                """,
                new { IdComprobante = idComprobante },
                cancellationToken: token));

            // Igual que en pedidos: nunca se distingue "no existe" de "es de otro cliente".
            if (cabecera is null || !string.Equals(cabecera.CodigoCliente.Trim(), codigo, StringComparison.OrdinalIgnoreCase))
                return null;

            var lineas = await cn.QueryAsync<PortalClientePedidoLineaDto>(new CommandDefinition(
                """
                SELECT
                    ISNULL(LTRIM(RTRIM(IDARTICULO)), '') AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(DESCRIPCION)), '') AS Descripcion,
                    ISNULL(CONVERT(decimal(15,2), CANTIDAD), 0) AS Cantidad,
                    ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS PrecioUnitario,
                    ISNULL(CONVERT(decimal(15,2), TOTAL), 0) AS Subtotal
                FROM dbo.V_MV_CpteInsumos
                WHERE UPPER(LTRIM(RTRIM(IDCOMPROBANTE))) = UPPER(LTRIM(RTRIM(@IdComprobanteTexto)))
                  AND UPPER(LTRIM(RTRIM(TC))) = UPPER(LTRIM(RTRIM(@Tc)))
                ORDER BY ISNULL(SECUENCIA, 0), ISNULL(ID, 0);
                """,
                new { cabecera.IdComprobanteTexto, cabecera.Tc },
                cancellationToken: token));

            return new PortalClienteComprobantePendienteDetalleDto
            {
                IdComprobante = cabecera.IdComprobante,
                Tc = cabecera.Tc,
                TcDescripcion = cabecera.TcDescripcion,
                IdComprobanteTexto = cabecera.IdComprobanteTexto,
                Fecha = cabecera.Fecha,
                Vencimiento = cabecera.Vencimiento,
                CodigoCliente = cabecera.CodigoCliente,
                RazonSocial = cabecera.RazonSocial,
                ImporteOriginal = cabecera.ImporteOriginal,
                SaldoPendiente = cabecera.SaldoPendiente,
                Lineas = lineas.ToList()
            };
        }, "No pudimos consultar el comprobante en este momento.", ct);

    // Mi cuenta: fuente oficial dbo.VT_CLIENTES (misma vista que ya usan Cuenta corriente, Lista de
    // precios y Pedidos). Lista/Vendedor/Condición se resuelven contra las mismas vistas de
    // referencia que ya usa CuentasComercialesService (V_MA_PreciosCab, V_TA_VENDEDORES,
    // V_TA_Cpra_Vta) — no se inventa una fuente nueva.
    public Task<PortalClienteMiCuentaDto?> GetMiCuentaAsync(string codigoCliente, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetMiCuenta", async token =>
        {
            var codigo = (codigoCliente ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigo))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var fila = await cn.QuerySingleOrDefaultAsync<MiCuentaRow>(new CommandDefinition(
                """
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(cli.CODIGO)), '') AS CodigoCliente,
                    ISNULL(LTRIM(RTRIM(cli.RAZON_SOCIAL)), '') AS RazonSocial,
                    ISNULL(LTRIM(RTRIM(cli.NUMERO_DOCUMENTO)), '') AS NumeroDocumento,
                    ISNULL(LTRIM(RTRIM(cli.CALLE)), '') AS Calle,
                    ISNULL(LTRIM(RTRIM(cli.NUMERO)), '') AS NumeroCalle,
                    ISNULL(LTRIM(RTRIM(cli.PISO)), '') AS Piso,
                    ISNULL(LTRIM(RTRIM(cli.DEPARTAMENTO)), '') AS Departamento,
                    ISNULL(LTRIM(RTRIM(cli.LOCALIDAD)), '') AS Localidad,
                    ISNULL(LTRIM(RTRIM(cli.PROVINCIA)), '') AS Provincia,
                    ISNULL(LTRIM(RTRIM(cli.TELEFONO)), '') AS Telefono,
                    ISNULL(LTRIM(RTRIM(cli.MAIL)), '') AS Email,
                    ISNULL(cli.Clase, 0) AS Clase,
                    ISNULL(LTRIM(RTRIM(lista.Nombre)), '') AS NombreLista,
                    ISNULL(LTRIM(RTRIM(cv.Descripcion)), '') AS CondicionVenta,
                    ISNULL(LTRIM(RTRIM(vd.Nombre)), '') AS Vendedor
                FROM dbo.VT_CLIENTES cli
                LEFT JOIN dbo.V_MA_PreciosCab lista
                    ON UPPER(LTRIM(RTRIM(lista.IdLista))) = UPPER(LTRIM(RTRIM(ISNULL(cli.IdLista, ''))))
                LEFT JOIN dbo.V_TA_Cpra_Vta cv
                    ON UPPER(LTRIM(RTRIM(cv.IDCond_Cpra_Vta))) = UPPER(LTRIM(RTRIM(ISNULL(cli.idCond_Cpra_Vta, ''))))
                LEFT JOIN dbo.V_TA_VENDEDORES vd
                    ON UPPER(LTRIM(RTRIM(vd.IdVendedor))) = UPPER(LTRIM(RTRIM(ISNULL(cli.IdVendedor, ''))))
                WHERE UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM(@CodigoCliente)))
                  AND ISNULL(cli.Dada_De_Baja, 0) = 0;
                """,
                new { CodigoCliente = codigo },
                cancellationToken: token));

            if (fila is null)
                return null;

            var domicilioPartes = new[] { fila.Calle, fila.NumeroCalle }.Where(p => !string.IsNullOrWhiteSpace(p));
            var domicilio = string.Join(" ", domicilioPartes).Trim();
            var pisoDepto = string.Join(" ", new[] { fila.Piso, fila.Departamento }.Where(p => !string.IsNullOrWhiteSpace(p)));
            if (!string.IsNullOrWhiteSpace(pisoDepto))
                domicilio = string.IsNullOrWhiteSpace(domicilio) ? pisoDepto : $"{domicilio}, {pisoDepto}";

            return new PortalClienteMiCuentaDto
            {
                CodigoCliente = fila.CodigoCliente,
                RazonSocial = fila.RazonSocial,
                NumeroDocumento = fila.NumeroDocumento,
                Domicilio = domicilio,
                Localidad = fila.Localidad,
                Provincia = fila.Provincia,
                Telefono = fila.Telefono,
                Email = fila.Email,
                Clase = fila.Clase,
                NombreLista = fila.NombreLista,
                CondicionVenta = fila.CondicionVenta,
                Vendedor = fila.Vendedor
            };
        }, "No pudimos consultar los datos de tu cuenta en este momento.", ct);

    // Actualiza únicamente MA_CUENTASADIC.MAIL (misma tabla/columna que ya lee VT_CLIENTES.MAIL).
    // No toca ningún otro campo del cliente: ni razón social, ni domicilio, ni condiciones
    // comerciales. Si el email no cambió, no ejecuta ningún UPDATE.
    public Task<PortalClienteActualizarEmailResultDto> ActualizarEmailAsync(PortalClienteActualizarEmailRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ActualizarEmail", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var codigo = (request.CodigoCliente ?? string.Empty).Trim();
            var email = (request.Email ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se pudo identificar al cliente. Volvé a iniciar sesión.");

            if (string.IsNullOrWhiteSpace(email) || !EsEmailValido(email))
                return new PortalClienteActualizarEmailResultDto { Exito = false, Mensaje = "Ingresá un email válido." };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var emailActual = await cn.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT TOP (1) ISNULL(LTRIM(RTRIM(MAIL)), '') FROM dbo.VT_CLIENTES WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));",
                new { Codigo = codigo },
                cancellationToken: token)) ?? string.Empty;

            if (string.Equals(emailActual.Trim(), email, StringComparison.OrdinalIgnoreCase))
                return new PortalClienteActualizarEmailResultDto { Exito = true, SinCambios = true, Mensaje = "El email ya estaba actualizado.", Email = emailActual };

            var actualizadas = await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.MA_CUENTASADIC SET MAIL = @Email WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));",
                new { Email = email, Codigo = codigo },
                cancellationToken: token));

            if (actualizadas == 0)
                return new PortalClienteActualizarEmailResultDto { Exito = false, Mensaje = "No pudimos actualizar tu email. Contactate con la empresa." };

            await appEvents.LogAuditAsync(
                ModuleName, "ActualizarEmail", "MA_CUENTASADIC", codigo,
                "El cliente actualizó su email de contacto desde el Portal Cliente.",
                new { CodigoCliente = codigo }, token);

            return new PortalClienteActualizarEmailResultDto { Exito = true, Mensaje = "Email actualizado correctamente.", Email = email };
        }, "No pudimos actualizar tu email en este momento.", ct);

    // Cambio de contraseña autenticado: exige la contraseña actual (misma lógica de comparación que
    // usa el login — CLAVE si existe, si no NUMERO_DOCUMENTO) antes de escribir. Distinto del flujo
    // de recuperación por email (PortalClienteRecuperarClaveService), que no requiere sesión.
    public Task<PortalClienteCambiarClaveResultDto> CambiarClaveAsync(PortalClienteCambiarClaveRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CambiarClave", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var codigo = (request.CodigoCliente ?? string.Empty).Trim();
            var actual = request.ClaveActual ?? string.Empty;
            var nueva = request.ClaveNueva ?? string.Empty;
            var confirmar = request.ConfirmarClaveNueva ?? string.Empty;

            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se pudo identificar al cliente. Volvé a iniciar sesión.");

            if (nueva.Length < ClaveLongitudMinima)
                return new PortalClienteCambiarClaveResultDto { Exito = false, Mensaje = $"La contraseña debe tener al menos {ClaveLongitudMinima} caracteres." };

            if (nueva.Length > ClaveLongitudMaxima)
                return new PortalClienteCambiarClaveResultDto { Exito = false, Mensaje = $"La contraseña no puede tener más de {ClaveLongitudMaxima} caracteres." };

            if (!string.Equals(nueva, confirmar, StringComparison.Ordinal))
                return new PortalClienteCambiarClaveResultDto { Exito = false, Mensaje = "Las contraseñas no coinciden." };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var adic = await cn.QuerySingleOrDefaultAsync<ClaveRow>(new CommandDefinition(
                """
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(CLAVE)), '') AS Clave,
                    ISNULL(LTRIM(RTRIM(NUMERO_DOCUMENTO)), '') AS NumeroDocumento
                FROM dbo.MA_CUENTASADIC
                WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));
                """,
                new { Codigo = codigo },
                cancellationToken: token));

            // Mismo criterio que el login: CLAVE si está seteada, si no NUMERO_DOCUMENTO.
            var claveActualGuardada = adic is not null && !string.IsNullOrWhiteSpace(adic.Clave)
                ? adic.Clave
                : adic?.NumeroDocumento ?? string.Empty;

            if (string.IsNullOrWhiteSpace(claveActualGuardada) || !string.Equals(claveActualGuardada, actual, StringComparison.Ordinal))
                return new PortalClienteCambiarClaveResultDto { Exito = false, Mensaje = "La contraseña actual no es correcta." };

            // Solo CLAVE: nunca se toca NUMERO_DOCUMENTO ni ninguna otra columna.
            var actualizadas = await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.MA_CUENTASADIC SET CLAVE = @Clave WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));",
                new { Clave = nueva, Codigo = codigo },
                cancellationToken: token));

            if (actualizadas == 0)
                return new PortalClienteCambiarClaveResultDto { Exito = false, Mensaje = "No pudimos actualizar tu contraseña. Contactate con la empresa." };

            await appEvents.LogAuditAsync(
                ModuleName, "CambiarClave", "MA_CUENTASADIC", codigo,
                "El cliente cambió su contraseña desde Mi cuenta (Portal Cliente).",
                new { CodigoCliente = codigo }, token);

            return new PortalClienteCambiarClaveResultDto { Exito = true, Mensaje = "Contraseña actualizada correctamente." };
        }, "No pudimos actualizar tu contraseña en este momento.", ct);

    private static bool EsEmailValido(string email)
    {
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Mismos límites que PortalClienteRecuperarClaveService (misma columna MA_CUENTASADIC.CLAVE).
    private const int ClaveLongitudMinima = 4;
    private const int ClaveLongitudMaxima = 15;

    private sealed class MiCuentaRow
    {
        public string CodigoCliente { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string NumeroDocumento { get; set; } = string.Empty;
        public string Calle { get; set; } = string.Empty;
        public string NumeroCalle { get; set; } = string.Empty;
        public string Piso { get; set; } = string.Empty;
        public string Departamento { get; set; } = string.Empty;
        public string Localidad { get; set; } = string.Empty;
        public string Provincia { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Clase { get; set; }
        public string NombreLista { get; set; } = string.Empty;
        public string CondicionVenta { get; set; } = string.Empty;
        public string Vendedor { get; set; } = string.Empty;
    }

    private sealed class ClaveRow
    {
        public string Clave { get; set; } = string.Empty;
        public string NumeroDocumento { get; set; } = string.Empty;
    }

    private static async Task<PortalClienteCuentaCorrienteResumenDto> ConsultarResumenAsync(SqlConnection cn, string codigoCliente, CancellationToken ct)
    {
        var fila = await cn.QuerySingleOrDefaultAsync<ResumenRow>(new CommandDefinition(
            """
            SELECT
                ISNULL(SUM(SALDO), 0) AS SaldoTotal,
                ISNULL(SUM(CASE WHEN CAST(VENCIMIENTO AS date) < CAST(@Hoy AS date) THEN SALDO ELSE 0 END), 0) AS Vencido,
                ISNULL(SUM(CASE WHEN CAST(VENCIMIENTO AS date) >= CAST(@Hoy AS date) THEN SALDO ELSE 0 END), 0) AS AVencer,
                COUNT(*) AS CantidadPendientes
            FROM dbo.VE_CPTES_SALDOS_VENTAS
            WHERE UPPER(LTRIM(RTRIM(CUENTA))) = UPPER(LTRIM(RTRIM(@CodigoCliente)));
            """,
            new { CodigoCliente = codigoCliente, Hoy = DateTime.Today },
            cancellationToken: ct));

        return new PortalClienteCuentaCorrienteResumenDto
        {
            SaldoTotal = fila?.SaldoTotal ?? 0,
            Vencido = fila?.Vencido ?? 0,
            AVencer = fila?.AVencer ?? 0,
            CantidadPendientes = fila?.CantidadPendientes ?? 0
        };
    }

    private sealed class ResumenRow
    {
        public decimal SaldoTotal { get; set; }
        public decimal Vencido { get; set; }
        public decimal AVencer { get; set; }
        public int CantidadPendientes { get; set; }
    }

    private sealed class PendienteRow
    {
        public string Tc { get; set; } = string.Empty;
        public string TcDescripcion { get; set; } = string.Empty;
        public string Sucursal { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string Letra { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public DateTime Vencimiento { get; set; }
        public decimal Saldo { get; set; }
        public int? IdComprobante { get; set; }
        public decimal? ImporteOriginal { get; set; }
    }

    private sealed class ComprobanteCabeceraRow
    {
        public int IdComprobante { get; set; }
        public string Tc { get; set; } = string.Empty;
        public string TcDescripcion { get; set; } = string.Empty;
        public string IdComprobanteTexto { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public string CodigoCliente { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public decimal ImporteOriginal { get; set; }
        public DateTime? Vencimiento { get; set; }
        public decimal? SaldoPendiente { get; set; }
    }

    private static PortalClientePedidoResumenDto MapResumen(PedidoRow row)
    {
        var (esWeb, idCatalogo) = ParseOrigenWeb(row.Comentarios);
        return new PortalClientePedidoResumenDto
        {
            IdComprobante = row.IdComprobante,
            Tc = row.Tc,
            IdComprobanteTexto = row.IdComprobanteTexto,
            Fecha = row.Fecha,
            Total = row.Total,
            Anulada = row.Anulada,
            EsPedidoWeb = esWeb,
            IdCatalogoWeb = idCatalogo
        };
    }

    private static (bool EsWeb, int? IdCatalogo) ParseOrigenWeb(string? comentarios)
        => PedidoWebOrigenHelper.Parse(comentarios);

    private sealed class PedidoRow
    {
        public int IdComprobante { get; set; }
        public string Tc { get; set; } = string.Empty;
        public string IdComprobanteTexto { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public bool Anulada { get; set; }
        public string Comentarios { get; set; } = string.Empty;
    }

    private sealed class CabeceraRow
    {
        public int IdComprobante { get; set; }
        public string Tc { get; set; } = string.Empty;
        public string IdComprobanteTexto { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public string CodigoCliente { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public bool Anulada { get; set; }
        public string Comentarios { get; set; } = string.Empty;
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
        catch (InvalidOperationException validationEx)
        {
            await appEvents.LogErrorAsync(
                module, action, validationEx, validationEx.Message,
                new { Usuario = appUserSession.GetCurrentUserName(Environment.UserName), SesionSql = sessionService.GetActiveSession()?.Nombre },
                AppEventSeverity.Warning, ct);
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
}

using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class CargaViajesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    ICargaViajesValidator validator,
    ILogger<CargaViajesService> logger) : ICargaViajesService
{
    private const string ModuleName = "CargaViajes";
    private const string ConfigGroup = "VIAJES";
    private const string ViewConfigPrefix = "USUVIEW-VIAJES-";
    private const string TipoVehiculoViewConfigPrefix = "USUVIEW-TIPOVEHICULO-";
    private const string DefaultTc = "VJ";
    private const string SucursalConfigKey = "VIAJES-SUCURSAL";
    private const string LegacySucursalConfigKey = "CARGA_VIAJES_SUCURSAL";
    private const string LetraConfigKey = "VIAJES-LETRA";
    private const string ChoferGeneralConfigKey = "VIAJES-CHOFER-GENERAL";
    private const string PorcentajesAdicionalesHabilitadosConfigKey = "VIAJES-PORCENTAJES-ADICIONALES-HABILITADOS";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configurÃ³ la cadena de conexiÃ³n 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<CargaViajesGridItemDto>> SearchViajesAsync(CargaViajesFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchViajes", async token =>
        {
            filters ??= new CargaViajesFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string viajeTable = "MV_VIAJES_CARGA";
            var clienteCodeExpr = "ISNULL(v.IDCLIENTE, '')";
            var destinoCodeExpr = "ISNULL(v.IDDESTINO, '')";
            var choferCodeExpr = "ISNULL(v.IDCHOFER, '')";
            var tipoVehiculoCodeExpr = "ISNULL(v.IDTIPOVEHICULO, '')";
            var clienteJoin = $"LEFT JOIN dbo.Vt_Clientes cli ON UPPER(LTRIM(RTRIM(ISNULL(cli.CODIGO, '')))) = UPPER(LTRIM(RTRIM({clienteCodeExpr})))";
            var destinoJoin = $"LEFT JOIN dbo.TA_DESTINOS d ON UPPER(LTRIM(RTRIM(ISNULL(d.CODIGO, '')))) = UPPER(LTRIM(RTRIM({destinoCodeExpr})))";
            var choferJoin = $"LEFT JOIN dbo.TA_CHOFERES ch ON UPPER(LTRIM(RTRIM(ISNULL(ch.CODIGO, '')))) = UPPER(LTRIM(RTRIM({choferCodeExpr})))";
            var vehiculoJoin = $"LEFT JOIN dbo.TA_TIPOVEHICULO tv ON UPPER(LTRIM(RTRIM(ISNULL(tv.CODIGO, '')))) = UPPER(LTRIM(RTRIM({tipoVehiculoCodeExpr})))";
            var destinoDescExpr = "ISNULL(d.DESCRIPCION, ISNULL(v.DESCRIPCIONDESTINO, ''))";
            var choferNameExpr = "ISNULL(ch.NOMBRES, v.NOMBRE_CHOFER)";
            var vehiculoExpr = "ISNULL(tv.DESCRIPCION, '')";
            var totalFleteExpr = "ISNULL(v.TOTAL_FLETE, 0)";
            var altaExpr = "ISNULL(v.FECHAHORA_ALTA, GETDATE())";
            var viajesOrderBy = BuildOrderByClause(
                filters.SortBy,
                filters.SortDescending,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fecha"] = $"v.FECHA {{dir}}, v.ID {{dir}}",
                    ["idcomprobante"] = $"ISNULL(v.IDCOMPROBANTE, '') {{dir}}, v.ID {{dir}}",
                    ["cliente"] = $"ISNULL(cli.RAZON_SOCIAL, '') {{dir}}, {clienteCodeExpr} {{dir}}, v.ID {{dir}}",
                    ["destino"] = $" {destinoDescExpr} {{dir}}, {destinoCodeExpr} {{dir}}, v.ID {{dir}}",
                    ["chofer"] = $" {choferNameExpr} {{dir}}, {choferCodeExpr} {{dir}}, v.ID {{dir}}",
                    ["tipovehiculo"] = $" {vehiculoExpr} {{dir}}, {tipoVehiculoCodeExpr} {{dir}}, v.ID {{dir}}",
                    ["totalcliente"] = $"ISNULL(v.TOTAL_IMPORTE, 0) {{dir}}, v.ID {{dir}}",
                    ["totalfletero"] = $"{totalFleteExpr} {{dir}}, v.ID {{dir}}",
                    ["estado"] = $"ISNULL(v.ESTADO, N'PENDIENTE') {{dir}}, v.ID {{dir}}",
                    ["usuario"] = $"ISNULL(v.USUARIO, '') {{dir}}, v.ID {{dir}}",
                    ["alta"] = $"{altaExpr} {{dir}}, v.ID {{dir}}",
                    ["id"] = $"v.ID {{dir}}"
                },
                "v.FECHA DESC, v.ID DESC");
            var sql = $"""
                SELECT
                    v.ID AS Id,
                    ISNULL(v.TC, @Tc) AS Tc,
                    ISNULL(v.IDCOMPROBANTE, '') AS IdComprobante,
                    v.FECHA AS Fecha,
                    {clienteCodeExpr} AS ClienteCodigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
                    {destinoCodeExpr} AS DestinoCodigo,
                    {destinoDescExpr} AS DestinoDescripcion,
                    {choferCodeExpr} AS ChoferCodigo,
                    {choferNameExpr} AS ChoferNombre,
                    {tipoVehiculoCodeExpr} AS TipoVehiculoCodigo,
                    {vehiculoExpr} AS TipoVehiculoDescripcion,
                    ISNULL(v.TOTAL_IMPORTE, 0) AS TotalCliente,
                    {totalFleteExpr} AS TotalFletero,
                    ISNULL(v.ESTADO, N'PENDIENTE') AS Estado,
                    ISNULL(v.USUARIO, '') AS Usuario,
                    {altaExpr} AS FechaHoraAlta
                FROM dbo.{viajeTable} v
                {clienteJoin}
                {destinoJoin}
                {choferJoin}
                {vehiculoJoin}
                WHERE (
                        @FechaDesde IS NULL OR v.FECHA >= @FechaDesde
                      )
                  AND (
                        @FechaHasta IS NULL OR v.FECHA < DATEADD(DAY, 1, @FechaHasta)
                      )
                  AND (@Cliente = '' OR UPPER(LTRIM(RTRIM({clienteCodeExpr}))) = @Cliente)
                  AND (@Chofer = '' OR UPPER(LTRIM(RTRIM({choferCodeExpr}))) = @Chofer)
                  AND (@Destino = '' OR UPPER(LTRIM(RTRIM({destinoCodeExpr}))) = @Destino)
                  AND (@TipoVehiculo = '' OR UPPER(LTRIM(RTRIM({tipoVehiculoCodeExpr}))) = @TipoVehiculo)
                  AND (@Estado = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.ESTADO, N'PENDIENTE')))) = @Estado)
                  AND (@Usuario = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.USUARIO, '')))) = @Usuario)
                  AND (@IdComprobante = '' OR ISNULL(v.IDCOMPROBANTE, '') = @IdComprobante)
                ORDER BY {viajesOrderBy}
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.{viajeTable} v
                {clienteJoin}
                {destinoJoin}
                {choferJoin}
                {vehiculoJoin}
                WHERE (
                        @FechaDesde IS NULL OR v.FECHA >= @FechaDesde
                      )
                  AND (
                        @FechaHasta IS NULL OR v.FECHA < DATEADD(DAY, 1, @FechaHasta)
                      )
                  AND (@Cliente = '' OR UPPER(LTRIM(RTRIM({clienteCodeExpr}))) = @Cliente)
                  AND (@Chofer = '' OR UPPER(LTRIM(RTRIM({choferCodeExpr}))) = @Chofer)
                  AND (@Destino = '' OR UPPER(LTRIM(RTRIM({destinoCodeExpr}))) = @Destino)
                  AND (@TipoVehiculo = '' OR UPPER(LTRIM(RTRIM({tipoVehiculoCodeExpr}))) = @TipoVehiculo)
                  AND (@Estado = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.ESTADO, N'PENDIENTE')))) = @Estado)
                  AND (@Usuario = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.USUARIO, '')))) = @Usuario)
                  AND (@IdComprobante = '' OR ISNULL(v.IDCOMPROBANTE, '') = @IdComprobante);
                """;

            var rows = (await cn.QueryAsync<CargaViajesGridItemDto>(new CommandDefinition(sql, new
            {
                Tc = DefaultTc,
                filters.FechaDesde,
                filters.FechaHasta,
                Cliente = TrimUpper(filters.Cliente),
                Chofer = TrimUpper(filters.Chofer),
                Destino = TrimUpper(filters.Destino),
                TipoVehiculo = TrimUpper(filters.TipoVehiculo),
                Estado = TrimUpper(filters.Estado),
                Usuario = TrimUpper(filters.Usuario),
                IdComprobante = (filters.IdComprobante ?? string.Empty).Trim(),
                Skip = skip,
                PageSize = pageSize
            }, cancellationToken: token))).ToList();

            var total = await cn.ExecuteScalarAsync<int>(new CommandDefinition($"""
                SELECT COUNT(*)
                FROM dbo.{viajeTable} v
                {clienteJoin}
                WHERE (
                        @FechaDesde IS NULL OR v.FECHA >= @FechaDesde
                      )
                  AND (
                        @FechaHasta IS NULL OR v.FECHA < DATEADD(DAY, 1, @FechaHasta)
                      )
                  AND (@Cliente = '' OR UPPER(LTRIM(RTRIM({clienteCodeExpr}))) = @Cliente)
                  AND (@Chofer = '' OR UPPER(LTRIM(RTRIM({choferCodeExpr}))) = @Chofer)
                  AND (@Destino = '' OR UPPER(LTRIM(RTRIM({destinoCodeExpr}))) = @Destino)
                  AND (@TipoVehiculo = '' OR UPPER(LTRIM(RTRIM({tipoVehiculoCodeExpr}))) = @TipoVehiculo)
                  AND (@Estado = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.ESTADO, N'PENDIENTE')))) = @Estado)
                  AND (@Usuario = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.USUARIO, '')))) = @Usuario)
                  AND (@IdComprobante = '' OR ISNULL(v.IDCOMPROBANTE, '') = @IdComprobante);
                """, new
            {
                filters.FechaDesde,
                filters.FechaHasta,
                Cliente = TrimUpper(filters.Cliente),
                Chofer = TrimUpper(filters.Chofer),
                Destino = TrimUpper(filters.Destino),
                TipoVehiculo = TrimUpper(filters.TipoVehiculo),
                Estado = TrimUpper(filters.Estado),
                Usuario = TrimUpper(filters.Usuario),
                IdComprobante = (filters.IdComprobante ?? string.Empty).Trim()
            }, cancellationToken: token));

            return new PagedResult<CargaViajesGridItemDto>
            {
                Items = rows,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar los viajes.", ct);

    public Task<CargaViajesDetailDto?> GetViajeByIdAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetViajeById", async token =>
        {
            if (id <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string viajeTable = "MV_VIAJES_CARGA";
            var vehiculoTable = await ResolveExistingTableAsync(cn, token, "TA_TIPOVEHICULO");
            var columns = await LoadColumnsAsync(cn, viajeTable, token);
            var clienteColumn = "IDCLIENTE";
            var destinoColumn = "IDDESTINO";
            var choferColumn = "IDCHOFER";
            var vehiculoColumn = "IDTIPOVEHICULO";
            var estadoColumn = "ESTADO";
            var totalImporteColumn = "TOTAL_IMPORTE";
            var totalFleteColumn = "TOTAL_FLETE";
            var peajeColumn = "TOTAL_PEAJE";
            var fechaAltaColumn = "FECHAHORA_ALTA";
            var idListaColumn = "IDLISTA";
            var cantidadViajesColumn = "TOTAL_VIAJES";
            var destinoDescColumn = "DESCRIPCIONDESTINO";
            var choferNameColumn = "NOMBRE_CHOFER";
            var clienteCodeExpr = $"ISNULL(v.{clienteColumn}, '')";
            var destinoCodeExpr = $"ISNULL(v.{destinoColumn}, '')";
            var choferCodeExpr = $"ISNULL(v.{choferColumn}, '')";
            var tipoVehiculoCodeExpr = $"ISNULL(v.{vehiculoColumn}, '')";
            var clienteJoin = $"LEFT JOIN dbo.Vt_Clientes cli ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM({clienteCodeExpr})))";
            var destinoDescExpr = $"ISNULL(v.{destinoDescColumn}, '')";
            var choferNameExpr = $"ISNULL(v.{choferNameColumn}, '')";
            var tipoVehiculoDescExpr = $"""ISNULL((SELECT TOP (1) LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) FROM dbo.{vehiculoTable} t WHERE UPPER(LTRIM(RTRIM(ISNULL(t.CODIGO, '')))) = UPPER(LTRIM(RTRIM({tipoVehiculoCodeExpr})))), '')""";
            var totalFleteExpr = $"ISNULL(v.{totalFleteColumn}, 0)";
            var altaExpr = $"ISNULL(v.{fechaAltaColumn}, GETDATE())";
            var listaExpr = $"ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(50), v.{idListaColumn}))), '')";
            var listaNombreExpr = $"""
                CASE
                    WHEN ISNULL(lista.Nombre, '') = '' THEN {listaExpr}
                    ELSE CONCAT({listaExpr}, ' - ', LTRIM(RTRIM(lista.Nombre)))
                END
                """;
            var peajeExpr = $"ISNULL(v.{peajeColumn}, 0)";
            var cantidadViajesExpr = $"ISNULL(v.{cantidadViajesColumn}, 1)";
            var hasAdicionalFijo1Descripcion = columns.Contains("adicional_fijo1_descripcion");
            var hasAdicionalFijo1Importe = columns.Contains("adicional_fijo1_importe");
            var hasAdicionalFijo1Aplicado = columns.Contains("adicional_fijo1_aplicado");
            var hasAdicionalFijo2Descripcion = columns.Contains("adicional_fijo2_descripcion");
            var hasAdicionalFijo2Importe = columns.Contains("adicional_fijo2_importe");
            var hasAdicionalFijo2Aplicado = columns.Contains("adicional_fijo2_aplicado");
            var hasAdicionalFijo3Descripcion = columns.Contains("adicional_fijo3_descripcion");
            var hasAdicionalFijo3Importe = columns.Contains("adicional_fijo3_importe");
            var hasAdicionalFijo3Aplicado = columns.Contains("adicional_fijo3_aplicado");
            var hasTotalAdicionalesFijos = columns.Contains("total_adicionales_fijos");
            var sql = $"""
                SELECT TOP (1)
                    v.ID AS Id,
                    ISNULL(v.TC, @Tc) AS Tc,
                    ISNULL(v.IDCOMPROBANTE, '') AS IdComprobante,
                    v.FECHA AS Fecha,
                    {clienteCodeExpr} AS ClienteCodigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
                    {destinoCodeExpr} AS DestinoCodigo,
                    {destinoDescExpr} AS DestinoDescripcion,
                    {choferCodeExpr} AS ChoferCodigo,
                    {choferNameExpr} AS ChoferNombre,
                    {tipoVehiculoCodeExpr} AS TipoVehiculoCodigo,
                    {tipoVehiculoDescExpr} AS TipoVehiculoDescripcion,
                    ISNULL(v.{totalImporteColumn}, 0) AS TotalCliente,
                    {totalFleteExpr} AS TotalFletero,
                    ISNULL(v.{estadoColumn}, N'PENDIENTE') AS Estado,
                    ISNULL(v.USUARIO, '') AS Usuario,
                    {altaExpr} AS FechaHoraAlta,
                    {listaExpr} AS Lista,
                    {listaNombreExpr} AS ListaDescripcion,
                    {peajeExpr} AS Peaje,
                    {cantidadViajesExpr} AS CantidadViajes,
                    ISNULL(v.PORCENTAJE_ADIC, 0) AS PorcentajeAdic,
                    ISNULL(v.PORCENTAJE_ADIC1, 0) AS PorcentajeAdic1,
                    ISNULL(v.PORCENTAJE_ADIC2, 0) AS PorcentajeAdic2,
                    ISNULL(v.PORCENTAJE_ADIC3, 0) AS PorcentajeAdic3,
                    ISNULL(v.PORCENTAJE_ADIC4, 0) AS PorcentajeAdic4,
                    ISNULL(v.{FirstExistingColumn(columns, "TOTAL_ADIC", "TOTAL_ADICIONALES")}, 0) AS TotalAdic,
                    ISNULL(v.{FirstExistingColumn(columns, "TOTAL_ADIC1")}, 0) AS TotalAdic1,
                    ISNULL(v.{FirstExistingColumn(columns, "TOTAL_ADIC2")}, 0) AS TotalAdic2,
                    ISNULL(v.{FirstExistingColumn(columns, "TOTAL_ADIC3")}, 0) AS TotalAdic3,
                    ISNULL(v.{FirstExistingColumn(columns, "TOTAL_ADIC4")}, 0) AS TotalAdic4,
                    ISNULL(v.{FirstExistingColumn(columns, "TOTAL_ADICIONALES")}, 0) AS TotalAdicionales,
                    {(hasAdicionalFijo1Descripcion ? "ISNULL(v.ADICIONAL_FIJO1_DESCRIPCION, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo1Descripcion,
                    {(hasAdicionalFijo1Importe ? "ISNULL(v.ADICIONAL_FIJO1_IMPORTE, 0)" : "CAST(0 AS money)")} AS AdicionalFijo1Importe,
                    {(hasAdicionalFijo1Aplicado ? "ISNULL(v.ADICIONAL_FIJO1_APLICADO, 0)" : "CAST(0 AS bit)")} AS AdicionalFijo1Aplicado,
                    {(hasAdicionalFijo2Descripcion ? "ISNULL(v.ADICIONAL_FIJO2_DESCRIPCION, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo2Descripcion,
                    {(hasAdicionalFijo2Importe ? "ISNULL(v.ADICIONAL_FIJO2_IMPORTE, 0)" : "CAST(0 AS money)")} AS AdicionalFijo2Importe,
                    {(hasAdicionalFijo2Aplicado ? "ISNULL(v.ADICIONAL_FIJO2_APLICADO, 0)" : "CAST(0 AS bit)")} AS AdicionalFijo2Aplicado,
                    {(hasAdicionalFijo3Descripcion ? "ISNULL(v.ADICIONAL_FIJO3_DESCRIPCION, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo3Descripcion,
                    {(hasAdicionalFijo3Importe ? "ISNULL(v.ADICIONAL_FIJO3_IMPORTE, 0)" : "CAST(0 AS money)")} AS AdicionalFijo3Importe,
                    {(hasAdicionalFijo3Aplicado ? "ISNULL(v.ADICIONAL_FIJO3_APLICADO, 0)" : "CAST(0 AS bit)")} AS AdicionalFijo3Aplicado,
                    {(hasTotalAdicionalesFijos ? "ISNULL(v.TOTAL_ADICIONALES_FIJOS, 0)" : "CAST(0 AS money)")} AS TotalAdicionalesFijos,
                    ISNULL(v.{FirstExistingColumn(columns, "OBSERVACIONES")}, '') AS Observaciones
                FROM dbo.{viajeTable} v
                {clienteJoin}
                OUTER APPLY (
                    SELECT TOP (1)
                        lista.Nombre
                    FROM dbo.TA_TARIFA lista
                    WHERE UPPER(LTRIM(RTRIM(ISNULL(lista.IdLista, '')))) = UPPER(LTRIM(RTRIM({listaExpr})))
                    ORDER BY lista.Nombre, lista.IdLista
                ) lista
                WHERE v.ID = @Id;
                """;

            var item = await cn.QuerySingleOrDefaultAsync<CargaViajesDetailDto>(new CommandDefinition(sql, new { Id = id, Tc = DefaultTc }, cancellationToken: token));
            if (item is null)
                return null;

            if (string.IsNullOrWhiteSpace(item.Lista))
                item.Lista = (await GetListaRMTRFAsync(cn, item.ClienteCodigo, token)).ListaCodigo;

            if (string.IsNullOrWhiteSpace(item.ListaDescripcion) && !string.IsNullOrWhiteSpace(item.Lista))
            {
                var lista = await GetListaTextoAsync(cn, item.Lista, token);
                item.ListaDescripcion = lista.ListaTexto;
            }

            return item;
        }, "No se pudo cargar el viaje seleccionado.", ct);

    public Task<int> SaveViajeAsync(CargaViajeSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveViaje", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Tc = string.IsNullOrWhiteSpace(request.Tc) ? DefaultTc : request.Tc.Trim().ToUpperInvariant();
            if (!string.Equals(request.Tc, DefaultTc, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El TC del mÃ³dulo de carga de viajes es fijo y debe ser VJ.");

            var cliente = (request.Cliente ?? string.Empty).Trim();
            var chofer = (request.Chofer ?? string.Empty).Trim();
            var destino = (request.Destino ?? string.Empty).Trim();
            var tipoVehiculo = (request.TipoVehiculo ?? string.Empty).Trim();
            var listaRequest = (request.Lista ?? string.Empty).Trim();
            var observaciones = (request.Observaciones ?? string.Empty).Trim();
            var estado = string.IsNullOrWhiteSpace(request.Estado) ? CargaViajeEstadoKeys.Pendiente : request.Estado.Trim().ToUpperInvariant();
            var destinoDisplay = (request.DestinoDisplay ?? string.Empty).Trim();
            var choferDisplay = (request.ChoferDisplay ?? string.Empty).Trim();

            request.Cliente = cliente;
            request.Chofer = chofer;
            request.Destino = destino;
            request.TipoVehiculo = tipoVehiculo;
            request.Lista = listaRequest;
            request.Observaciones = observaciones;
            request.Estado = estado;
            request.DestinoDisplay = destinoDisplay;
            request.ChoferDisplay = choferDisplay;
            request.IdComprobante = string.IsNullOrWhiteSpace(request.IdComprobante) ? null : request.IdComprobante.Trim();

            logger.LogInformation(
                "SaveViaje start Id={Id} IdComprobante={IdComprobante} Cliente={Cliente} Chofer={Chofer} Destino={Destino} TipoVehiculo={TipoVehiculo} Fecha={Fecha:yyyy-MM-dd}",
                request.Id,
                request.IdComprobante,
                cliente,
                chofer,
                destino,
                tipoVehiculo,
                request.Fecha);

            var validation = await validator.ValidateViajeForSaveAsync(request, token);
            logger.LogInformation(
                "SaveViaje validation result IsValid={IsValid} Cliente={Cliente} Chofer={Chofer} Destino={Destino} TipoVehiculo={TipoVehiculo} Lista={Lista} ImporteCliente={ImporteCliente} Fecha={Fecha:yyyy-MM-dd} Issues={Issues}",
                validation.IsValid,
                cliente,
                chofer,
                destino,
                tipoVehiculo,
                listaRequest,
                request.ImporteCliente,
                request.Fecha,
                string.Join(" | ", validation.Issues.Select(issue => $"{issue.FieldKey}={issue.Message}")));
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            #pragma warning disable 162
            return await SaveViajeExplicitAsync(request, token);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string viajeTable = "MV_VIAJES_CARGA";
            var columns = await LoadColumnsAsync(cn, viajeTable, token);
            var hasIdLista = columns.Contains("idlista");
            var hasCliente = columns.Contains("idcliente");
            var hasDestino = columns.Contains("iddestino");
            var hasChofer = columns.Contains("idchofer");
            var hasVehiculo = columns.Contains("idtipovehiculo");
            var hasUsuario = columns.Contains("usuario");
            var hasTotalFlete = columns.Contains("total_flete");
            var hasCantidadViajes = columns.Contains("total_viajes");
            var hasAdics = columns.Contains("porcentaje_adic") || columns.Contains("total_adic") || columns.Contains("total_adicionales");
            var hasEstado = columns.Contains("estado");
            var hasGrabacion = columns.Contains("fechahora_grabacion") || columns.Contains("fechahora_alta");
            var hasDescripDestino = columns.Contains("descripciondestino");
            var hasNombreChofer = columns.Contains("nombre_chofer");

            var listaCodigo = (request.Lista ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(listaCodigo))
            {
                var clienteLista = await GetListaRMTRFAsync(cn, cliente, token);
                listaCodigo = clienteLista.ListaCodigo;
            }

            var listaTexto = string.Empty;
            if (!string.IsNullOrWhiteSpace(listaCodigo))
                listaTexto = (await GetListaTextoAsync(cn, listaCodigo, token)).ListaTexto;
            var nextIdComp = string.IsNullOrWhiteSpace(request.IdComprobante)
                ? await GetNextIdComprobanteAsync(token)
                : request.IdComprobante.Trim();
            if (string.IsNullOrWhiteSpace(nextIdComp))
                throw new InvalidOperationException("No se pudo generar el IDCOMPROBANTE del viaje.");

            logger.LogInformation(
                "SaveViaje comprobante generado IdComprobante={IdComprobante} Lista={Lista} ListaTexto={ListaTexto} Tabla={Tabla}",
                nextIdComp,
                listaCodigo,
                listaTexto,
                viajeTable);
            var totals = CalculateTotals(request);
            logger.LogInformation(
                "SaveViaje payload Tc={Tc} IdComprobante={IdComprobante} IdCliente={IdCliente} IdDestino={IdDestino} IdTipoVehiculo={IdTipoVehiculo} IdChofer={IdChofer} IdLista={IdLista} TotalImporte={TotalImporte} TotalFlete={TotalFlete} Peaje={Peaje} CantidadViajes={CantidadViajes}",
                request.Tc,
                nextIdComp,
                cliente,
                destino,
                tipoVehiculo,
                chofer,
                listaCodigo,
                totals.TotalImporte,
                totals.TotalFlete,
                request.Peaje,
                request.CantidadViajes);

            var isNew = !request.Id.HasValue || request.Id.Value <= 0;
            var parameters = new DynamicParameters();
            parameters.Add("@Id", request.Id);
            parameters.Add("@Tc", request.Tc);
            parameters.Add("@IdComprobante", nextIdComp);
            parameters.Add("@Fecha", request.Fecha);
            parameters.Add("@Cliente", cliente);
            parameters.Add("@Destino", destino);
            parameters.Add("@Chofer", chofer);
            parameters.Add("@TipoVehiculo", tipoVehiculo);
            parameters.Add("@IdLista", listaCodigo);
            parameters.Add("@DescripcionDestino", ExtractDescripcionFromDisplay(destinoDisplay));
            parameters.Add("@NombreChofer", ExtractDescripcionFromDisplay(choferDisplay));
            parameters.Add("@ImporteCliente", request.ImporteCliente);
            parameters.Add("@ImporteFletero", request.ImporteFletero);
            parameters.Add("@Peaje", request.Peaje);
            parameters.Add("@CantidadViajes", request.CantidadViajes);
            parameters.Add("@PorcentajeAdic", request.PorcentajeAdic);
            parameters.Add("@PorcentajeAdic1", request.PorcentajeAdic1);
            parameters.Add("@PorcentajeAdic2", request.PorcentajeAdic2);
            parameters.Add("@PorcentajeAdic3", request.PorcentajeAdic3);
            parameters.Add("@PorcentajeAdic4", request.PorcentajeAdic4);
            parameters.Add("@TotalAdic", totals.TotalAdic);
            parameters.Add("@TotalAdic1", totals.TotalAdic1);
            parameters.Add("@TotalAdic2", totals.TotalAdic2);
            parameters.Add("@TotalAdic3", totals.TotalAdic3);
            parameters.Add("@TotalAdic4", totals.TotalAdic4);
            parameters.Add("@TotalAdicionales", totals.TotalAdicionales);
            parameters.Add("@TotalImporte", totals.TotalImporte);
            parameters.Add("@TotalFlete", totals.TotalFlete);
            parameters.Add("@Observaciones", observaciones);
            parameters.Add("@Estado", estado);
            parameters.Add("@Usuario", NormalizeUser(request.UsuarioAccion));

            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                if (isNew)
                {
                    var insertColumns = new List<string>();
                    var insertValues = new List<string>();
                    AddColumnPair(insertColumns, insertValues, "TC", "@Tc");
                    AddColumnPair(insertColumns, insertValues, "IDCOMPROBANTE", "@IdComprobante");
                    AddColumnPair(insertColumns, insertValues, "FECHA", "@Fecha");
                    AddColumnPair(insertColumns, insertValues, hasCliente ? "IDCLIENTE" : null, "@Cliente");
                    AddColumnPair(insertColumns, insertValues, hasDestino ? "IDDESTINO" : null, "@Destino");
                    AddColumnPair(insertColumns, insertValues, hasChofer ? "IDCHOFER" : null, "@Chofer");
                    AddColumnPair(insertColumns, insertValues, hasVehiculo ? "IDTIPOVEHICULO" : null, "@TipoVehiculo");
                    AddColumnPair(insertColumns, insertValues, hasIdLista ? "IDLISTA" : null, "@IdLista");
                    AddColumnPair(insertColumns, insertValues, hasDescripDestino ? "DESCRIPCIONDESTINO" : null, "@DescripcionDestino");
                    AddColumnPair(insertColumns, insertValues, hasNombreChofer ? "NOMBRE_CHOFER" : null, "@NombreChofer");
                    AddColumnPair(insertColumns, insertValues, hasUsuario ? "USUARIO" : null, "@Usuario");
                    AddColumnPair(insertColumns, insertValues, "TOTAL_IMPORTE", "@TotalImporte");
                    AddColumnPair(insertColumns, insertValues, hasTotalFlete ? "TOTAL_FLETE" : null, "@TotalFlete");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("total_peaje") ? "TOTAL_PEAJE" : null, "@Peaje");
                    AddColumnPair(insertColumns, insertValues, hasCantidadViajes ? "TOTAL_VIAJES" : null, "@CantidadViajes");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("porcentaje_adic") ? "PORCENTAJE_ADIC" : null, "@PorcentajeAdic");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("porcentaje_adic1") ? "PORCENTAJE_ADIC1" : null, "@PorcentajeAdic1");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("porcentaje_adic2") ? "PORCENTAJE_ADIC2" : null, "@PorcentajeAdic2");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("porcentaje_adic3") ? "PORCENTAJE_ADIC3" : null, "@PorcentajeAdic3");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("porcentaje_adic4") ? "PORCENTAJE_ADIC4" : null, "@PorcentajeAdic4");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("total_adic") ? "TOTAL_ADIC" : null, "@TotalAdic");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("total_adic1") ? "TOTAL_ADIC1" : null, "@TotalAdic1");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("total_adic2") ? "TOTAL_ADIC2" : null, "@TotalAdic2");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("total_adic3") ? "TOTAL_ADIC3" : null, "@TotalAdic3");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("total_adic4") ? "TOTAL_ADIC4" : null, "@TotalAdic4");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("total_adicionales") ? "TOTAL_ADICIONALES" : null, "@TotalAdicionales");
                    AddColumnPair(insertColumns, insertValues, hasEstado ? "ESTADO" : null, "@Estado");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("anulado") ? "ANULADO" : null, "0");
                    AddColumnPair(insertColumns, insertValues, hasGrabacion ? "FECHAHORA_ALTA" : null, "GETDATE()");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("fechahora_modificacion") ? "FECHAHORA_MODIFICACION" : null, "GETDATE()");
                    AddColumnPair(insertColumns, insertValues, columns.Contains("observaciones") ? "OBSERVACIONES" : null, "@Observaciones");

                    var insertSql = $"""
                        INSERT INTO dbo.{viajeTable}
                        ({string.Join(", ", insertColumns)})
                        VALUES
                        ({string.Join(", ", insertValues)});
                        SELECT CAST(SCOPE_IDENTITY() AS int);
                        """;
                    logger.LogInformation("SaveViaje SQL INSERT {Sql}", insertSql);
                    var insertedId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, parameters, transaction: (SqlTransaction)tx, cancellationToken: token));
                    await tx.CommitAsync(token);
                    logger.LogInformation("SaveViaje insert OK Id={Id} IdComprobante={IdComprobante} InsertedId={InsertedId} Rows=1", request.Id, nextIdComp, insertedId);
                    return insertedId;
                }

                var updateParts = new List<string>();
                AddUpdatePart(updateParts, hasCliente ? "IDCLIENTE" : null, "@Cliente");
                AddUpdatePart(updateParts, hasDestino ? "IDDESTINO" : null, "@Destino");
                AddUpdatePart(updateParts, hasChofer ? "IDCHOFER" : null, "@Chofer");
                AddUpdatePart(updateParts, hasVehiculo ? "IDTIPOVEHICULO" : null, "@TipoVehiculo");
                AddUpdatePart(updateParts, hasIdLista ? "IDLISTA" : null, "@IdLista");
                AddUpdatePart(updateParts, hasDescripDestino ? "DESCRIPCIONDESTINO" : null, "@DescripcionDestino");
                AddUpdatePart(updateParts, hasNombreChofer ? "NOMBRE_CHOFER" : null, "@NombreChofer");
                AddUpdatePart(updateParts, "FECHA", "@Fecha");
                AddUpdatePart(updateParts, "TOTAL_IMPORTE", "@TotalImporte");
                AddUpdatePart(updateParts, hasTotalFlete ? "TOTAL_FLETE" : null, "@TotalFlete");
                AddUpdatePart(updateParts, columns.Contains("total_peaje") ? "TOTAL_PEAJE" : null, "@Peaje");
                AddUpdatePart(updateParts, hasCantidadViajes ? "TOTAL_VIAJES" : null, "@CantidadViajes");
                AddUpdatePart(updateParts, columns.Contains("porcentaje_adic") ? "PORCENTAJE_ADIC" : null, "@PorcentajeAdic");
                AddUpdatePart(updateParts, columns.Contains("porcentaje_adic1") ? "PORCENTAJE_ADIC1" : null, "@PorcentajeAdic1");
                AddUpdatePart(updateParts, columns.Contains("porcentaje_adic2") ? "PORCENTAJE_ADIC2" : null, "@PorcentajeAdic2");
                AddUpdatePart(updateParts, columns.Contains("porcentaje_adic3") ? "PORCENTAJE_ADIC3" : null, "@PorcentajeAdic3");
                AddUpdatePart(updateParts, columns.Contains("porcentaje_adic4") ? "PORCENTAJE_ADIC4" : null, "@PorcentajeAdic4");
                AddUpdatePart(updateParts, columns.Contains("total_adic") ? "TOTAL_ADIC" : null, "@TotalAdic");
                AddUpdatePart(updateParts, columns.Contains("total_adic1") ? "TOTAL_ADIC1" : null, "@TotalAdic1");
                AddUpdatePart(updateParts, columns.Contains("total_adic2") ? "TOTAL_ADIC2" : null, "@TotalAdic2");
                AddUpdatePart(updateParts, columns.Contains("total_adic3") ? "TOTAL_ADIC3" : null, "@TotalAdic3");
                AddUpdatePart(updateParts, columns.Contains("total_adic4") ? "TOTAL_ADIC4" : null, "@TotalAdic4");
                AddUpdatePart(updateParts, columns.Contains("total_adicionales") ? "TOTAL_ADICIONALES" : null, "@TotalAdicionales");
                AddUpdatePart(updateParts, hasEstado ? "ESTADO" : null, "@Estado");
                AddUpdatePart(updateParts, columns.Contains("anulado") ? "ANULADO" : null, "0");
                AddUpdatePart(updateParts, hasGrabacion ? "FECHAHORA_MODIFICACION" : null, "GETDATE()", rawSql: true);
                AddUpdatePart(updateParts, columns.Contains("observaciones") ? "OBSERVACIONES" : null, "@Observaciones");

                var updateSql = $"""
                    UPDATE dbo.{viajeTable}
                    SET {string.Join(", ", updateParts)}
                    WHERE ID = @Id;
                    """;
                logger.LogInformation("SaveViaje SQL UPDATE {Sql}", updateSql);
                var affected = await cn.ExecuteAsync(new CommandDefinition(updateSql, parameters, transaction: (SqlTransaction)tx, cancellationToken: token));
                if (affected <= 0)
                    throw new InvalidOperationException("No se encontrÃ³ el viaje para actualizar.");

                await tx.CommitAsync(token);
                logger.LogInformation("SaveViaje update OK Id={Id} IdComprobante={IdComprobante} Rows={Rows}", request.Id, nextIdComp, affected);
                return request.Id!.Value;
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(token);
                }
                catch (Exception rollbackEx)
                {
                    logger.LogWarning(rollbackEx, "SaveViaje rollback fallÃ³");
                }

                throw;
            }
        }, "No se pudo guardar el viaje.", ct);

    #pragma warning restore 162

    public Task AnularViajeAsync(int id, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AnularViaje", async token =>
        {
            if (id <= 0)
                throw new InvalidOperationException("No se recibiÃ³ el viaje a anular.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string viajeTable = "MV_VIAJES_CARGA";
            var columns = await LoadColumnsAsync(cn, viajeTable, token);
            var hasEstado = columns.Contains("estado");
            var grabacionColumn = columns.Contains("fechahora_modificacion") || columns.Contains("fechahora_grabacion") || columns.Contains("fechahora_alta")
                ? FirstExistingColumn(columns, "FECHAHORA_MODIFICACION", "FECHAHORA_GRABACION", "FECHAHORA_ALTA", "FECHAHORAALTA")
                : null;
            var updateParts = new List<string>();
            if (hasEstado)
                updateParts.Add("ESTADO = @Estado");
            if (columns.Contains("anulado"))
                updateParts.Add("ANULADO = 1");
            if (columns.Contains("fechahora_modificacion"))
                updateParts.Add("FECHAHORA_MODIFICACION = GETDATE()");
            if (grabacionColumn is not null)
                updateParts.Add($"{grabacionColumn} = GETDATE()");

            if (updateParts.Count == 0)
                throw new InvalidOperationException("La tabla de viajes no tiene columnas editables para anular el registro.");

            var sql = $"UPDATE dbo.{viajeTable} SET {string.Join(", ", updateParts)} WHERE ID = @Id;";
            await cn.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Estado = CargaViajeEstadoKeys.Anulado }, cancellationToken: token));
        }, "No se pudo anular el viaje.", ct);

    public Task<PagedResult<CargaViajeTarifaGridItemDto>> SearchTarifasAsync(CargaViajesFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchTarifas", async token =>
        {
            filters ??= new CargaViajesFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                return new PagedResult<CargaViajeTarifaGridItemDto> { Items = [], Total = 0, PageNumber = pageNumber, PageSize = pageSize };

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idColumn = columns.Contains("id") ? "ID"
                : columns.Contains("idtarifa") ? "IDTARIFA"
                : columns.Contains("id_tarifa") ? "ID_TARIFA"
                : null;
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            var tarifaFleteroFiltro = ParseNullableBitFilter(filters.TarifaFletero);
            var activoFiltro = ParseNullableBitFilter(filters.Activo);
            var textoLike = SearchTextHelper.LikeContains(filters.Texto);
            var clienteCodeExpr = $"LTRIM(RTRIM(ISNULL(t.{clienteColumn}, '')))";
            var choferCodeExpr = $"LTRIM(RTRIM(ISNULL(t.{choferColumn}, '')))";
            var destinoCodeExpr = $"LTRIM(RTRIM(ISNULL(t.{destinoColumn}, '')))";
            var tipoVehiculoCodeExpr = $"LTRIM(RTRIM(ISNULL(t.{tipoVehiculoColumn}, '')))";
            var clienteDisplayExpr = $"""
                CASE
                    WHEN ISNULL(cli.Valor, '') = '' THEN {clienteCodeExpr}
                    ELSE {clienteCodeExpr} + ' - ' + cli.Valor
                END
                """;
            var choferDisplayExpr = $"""
                CASE
                    WHEN ISNULL(ch.Valor, '') = '' THEN {choferCodeExpr}
                    ELSE {choferCodeExpr} + ' - ' + ch.Valor
                END
                """;
            var destinoDisplayExpr = $"""
                CASE
                    WHEN ISNULL(d.Valor, '') = '' THEN {destinoCodeExpr}
                    ELSE {destinoCodeExpr} + ' - ' + d.Valor
                END
                """;
            var tipoVehiculoDisplayExpr = $"""
                CASE
                    WHEN ISNULL(tv.Valor, '') = '' THEN {tipoVehiculoCodeExpr}
                    ELSE {tipoVehiculoCodeExpr} + ' - ' + tv.Valor
                END
                """;
            var hasAdicionalFijo1Descripcion = columns.Contains("adicionalfijo1descripcion");
            var hasAdicionalFijo1Importe = columns.Contains("adicionalfijo1importe");
            var hasAdicionalFijo2Descripcion = columns.Contains("adicionalfijo2descripcion");
            var hasAdicionalFijo2Importe = columns.Contains("adicionalfijo2importe");
            var hasAdicionalFijo3Descripcion = columns.Contains("adicionalfijo3descripcion");
            var hasAdicionalFijo3Importe = columns.Contains("adicionalfijo3importe");
            var idSelectExpr = string.IsNullOrWhiteSpace(idColumn) ? "CAST(0 AS int) AS Id" : $"ISNULL(t.{idColumn}, 0) AS Id";
            var idOrderExpr = string.IsNullOrWhiteSpace(idColumn) ? "0" : $"ISNULL(t.{idColumn}, 0)";
            var tarifasOrderBy = BuildOrderByClause(
                filters.SortBy,
                filters.SortDescending,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nombre"] = $"ISNULL(t.Nombre, '') {{dir}}, LTRIM(RTRIM(ISNULL(t.{idListaColumn}, ''))) {{dir}}, {idOrderExpr} {{dir}}",
                    ["tipotarifa"] = $"ISNULL(t.TarifaFletero, 0) {{dir}}, ISNULL(t.Nombre, '') {{dir}}, {idOrderExpr} {{dir}}",
                    ["cliente"] = $"{clienteDisplayExpr} {{dir}}, {idOrderExpr} {{dir}}",
                    ["chofer"] = $"{choferDisplayExpr} {{dir}}, {idOrderExpr} {{dir}}",
                    ["destino"] = $"{destinoDisplayExpr} {{dir}}, {idOrderExpr} {{dir}}",
                    ["tipovehiculo"] = $"{tipoVehiculoDisplayExpr} {{dir}}, {idOrderExpr} {{dir}}",
                    ["importe"] = $"ISNULL(t.Importe, 0) {{dir}}, {idOrderExpr} {{dir}}",
                    ["activo"] = $"ISNULL(t.Activo, 1) {{dir}}, ISNULL(t.Nombre, '') {{dir}}, {idOrderExpr} {{dir}}",
                    ["id"] = $"{idOrderExpr} {{dir}}",
                    ["idlista"] = $"LTRIM(RTRIM(ISNULL(t.{idListaColumn}, ''))) {{dir}}, {idOrderExpr} {{dir}}"
                },
                $"ISNULL(t.Nombre, '') ASC, LTRIM(RTRIM(ISNULL(t.{idListaColumn}, ''))) ASC, {idOrderExpr} ASC");

            var sql = $"""
                ;WITH base_rows AS (
                    SELECT
                        ROW_NUMBER() OVER (
                            ORDER BY {tarifasOrderBy}
                        ) AS RowNum,
                        {idSelectExpr},
                        LTRIM(RTRIM(ISNULL(t.{idListaColumn}, ''))) AS IdLista,
                        ISNULL(t.Nombre, '') AS Nombre,
                        ISNULL(t.Importe, 0) AS Importe,
                        {clienteDisplayExpr} AS Cliente,
                        {choferDisplayExpr} AS Chofer,
                        {destinoDisplayExpr} AS Destino,
                        {tipoVehiculoDisplayExpr} AS TipoVehiculo,
                        ISNULL(t.TarifaFletero, 0) AS TarifaFletero,
                        ISNULL(t.PorcentajeAdic, 0) AS PorcentajeAdic,
                        ISNULL(t.PorcentajeAdic1, 0) AS PorcentajeAdic1,
                        ISNULL(t.PorcentajeAdic2, 0) AS PorcentajeAdic2,
                        ISNULL(t.PorcentajeAdic3, 0) AS PorcentajeAdic3,
                        ISNULL(t.PorcentajeAdic4, 0) AS PorcentajeAdic4,
                        {(hasAdicionalFijo1Descripcion ? "ISNULL(t.AdicionalFijo1Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo1Descripcion,
                        {(hasAdicionalFijo1Importe ? "ISNULL(t.AdicionalFijo1Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo1Importe,
                        {(hasAdicionalFijo2Descripcion ? "ISNULL(t.AdicionalFijo2Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo2Descripcion,
                        {(hasAdicionalFijo2Importe ? "ISNULL(t.AdicionalFijo2Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo2Importe,
                        {(hasAdicionalFijo3Descripcion ? "ISNULL(t.AdicionalFijo3Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo3Descripcion,
                        {(hasAdicionalFijo3Importe ? "ISNULL(t.AdicionalFijo3Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo3Importe,
                        ISNULL(t.Activo, 1) AS Activo
                    FROM dbo.TA_TARIFA t
                    OUTER APPLY (
                        SELECT TOP (1) LTRIM(RTRIM(ISNULL(cli.RAZON_SOCIAL, ''))) AS Valor
                        FROM dbo.Vt_Clientes cli
                        WHERE UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM({clienteCodeExpr})))
                    ) cli
                    OUTER APPLY (
                        SELECT TOP (1) LTRIM(RTRIM(ISNULL(ch.NOMBRES, ''))) AS Valor
                        FROM dbo.TA_CHOFERES ch
                        WHERE UPPER(LTRIM(RTRIM(ch.CODIGO))) = UPPER(LTRIM(RTRIM({choferCodeExpr})))
                    ) ch
                    OUTER APPLY (
                        SELECT TOP (1) LTRIM(RTRIM(ISNULL(d.DESCRIPCION, ''))) AS Valor
                        FROM dbo.TA_DESTINOS d
                        WHERE UPPER(LTRIM(RTRIM(d.CODIGO))) = UPPER(LTRIM(RTRIM({destinoCodeExpr})))
                    ) d
                    OUTER APPLY (
                        SELECT TOP (1) LTRIM(RTRIM(ISNULL(tv.DESCRIPCION, ''))) AS Valor
                        FROM dbo.TA_TIPOVEHICULO tv
                        WHERE UPPER(LTRIM(RTRIM(tv.CODIGO))) = UPPER(LTRIM(RTRIM({tipoVehiculoCodeExpr})))
                    ) tv
                    WHERE (
                            @TextoLike = ''
                            OR t.{idListaColumn} LIKE @TextoLike
                            OR t.Nombre COLLATE Latin1_General_CI_AI LIKE @TextoLike
                            OR {clienteCodeExpr} LIKE @TextoLike
                            OR {choferCodeExpr} LIKE @TextoLike
                            OR {destinoCodeExpr} LIKE @TextoLike
                            OR {tipoVehiculoCodeExpr} LIKE @TextoLike
                        )
                      AND (@Cliente = '' OR {clienteCodeExpr} LIKE @ClienteLike)
                      AND (@Chofer = '' OR {choferCodeExpr} LIKE @ChoferLike)
                      AND (@Destino = '' OR {destinoCodeExpr} LIKE @DestinoLike)
                      AND (@TipoVehiculo = '' OR {tipoVehiculoCodeExpr} LIKE @TipoVehiculoLike)
                      AND (@TarifaFletero IS NULL OR ISNULL(t.TarifaFletero, 0) = @TarifaFletero)
                      AND (@Activo IS NULL OR ISNULL(t.Activo, 1) = @Activo)
                )
                SELECT *
                FROM base_rows
                ORDER BY RowNum
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.TA_TARIFA t
                WHERE (
                        @TextoLike = ''
                        OR t.{idListaColumn} LIKE @TextoLike
                        OR t.Nombre COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR LTRIM(RTRIM(ISNULL(t.{clienteColumn}, ''))) LIKE @TextoLike
                        OR LTRIM(RTRIM(ISNULL(t.{choferColumn}, ''))) LIKE @TextoLike
                        OR LTRIM(RTRIM(ISNULL(t.{destinoColumn}, ''))) LIKE @TextoLike
                        OR LTRIM(RTRIM(ISNULL(t.{tipoVehiculoColumn}, ''))) LIKE @TextoLike
                    )
                  AND (@Cliente = '' OR LTRIM(RTRIM(ISNULL(t.{clienteColumn}, ''))) LIKE @ClienteLike)
                  AND (@Chofer = '' OR LTRIM(RTRIM(ISNULL(t.{choferColumn}, ''))) LIKE @ChoferLike)
                  AND (@Destino = '' OR LTRIM(RTRIM(ISNULL(t.{destinoColumn}, ''))) LIKE @DestinoLike)
                  AND (@TipoVehiculo = '' OR LTRIM(RTRIM(ISNULL(t.{tipoVehiculoColumn}, ''))) LIKE @TipoVehiculoLike)
                  AND (@TarifaFletero IS NULL OR ISNULL(t.TarifaFletero, 0) = @TarifaFletero)
                  AND (@Activo IS NULL OR ISNULL(t.Activo, 1) = @Activo);
                """;

            var rows = new List<CargaViajeTarifaGridItemDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@TextoLike", textoLike);
            cmd.Parameters.AddWithValue("@Cliente", (filters.Cliente ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("@ClienteLike", SearchTextHelper.LikeContains(filters.Cliente));
            cmd.Parameters.AddWithValue("@Chofer", (filters.Chofer ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("@ChoferLike", SearchTextHelper.LikeContains(filters.Chofer));
            cmd.Parameters.AddWithValue("@Destino", (filters.Destino ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("@DestinoLike", SearchTextHelper.LikeContains(filters.Destino));
            cmd.Parameters.AddWithValue("@TipoVehiculo", (filters.TipoVehiculo ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("@TipoVehiculoLike", SearchTextHelper.LikeContains(filters.TipoVehiculo));
            cmd.Parameters.AddWithValue("@TarifaFletero", (object?)tarifaFleteroFiltro ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Activo", (object?)activoFiltro ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);
            cmd.CommandTimeout = 60;
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                rows.Add(new CargaViajeTarifaGridItemDto
                {
                    Id = GetInt(rd, 1),
                    IdLista = GetString(rd, 2),
                    Nombre = GetString(rd, 3),
                    Importe = GetDecimal(rd, 4),
                    Cliente = GetString(rd, 5),
                    Chofer = GetString(rd, 6),
                    Destino = GetString(rd, 7),
                    TipoVehiculo = GetString(rd, 8),
                    TarifaFletero = GetBool(rd, 9),
                    PorcentajeAdic = GetDecimal(rd, 10),
                    PorcentajeAdic1 = GetDecimal(rd, 11),
                    PorcentajeAdic2 = GetDecimal(rd, 12),
                    PorcentajeAdic3 = GetDecimal(rd, 13),
                    PorcentajeAdic4 = GetDecimal(rd, 14),
                    AdicionalFijo1Descripcion = GetString(rd, 15),
                    AdicionalFijo1Importe = GetDecimal(rd, 16),
                    AdicionalFijo2Descripcion = GetString(rd, 17),
                    AdicionalFijo2Importe = GetDecimal(rd, 18),
                    AdicionalFijo3Descripcion = GetString(rd, 19),
                    AdicionalFijo3Importe = GetDecimal(rd, 20),
                    Activo = GetBool(rd, 21)
                });
            }

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
                total = GetInt(rd, 0);

            return new PagedResult<CargaViajeTarifaGridItemDto> { Items = rows, Total = total, PageNumber = pageNumber, PageSize = pageSize };
        }, "No se pudieron cargar las tarifas.", ct);

    public Task<CargaViajeTarifaGridItemDto?> GetTarifaByIdAsync(string idLista, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTarifaById", async token =>
        {
            if (string.IsNullOrWhiteSpace(idLista))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                return null;
            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idColumn = columns.Contains("id") ? "ID"
                : columns.Contains("idtarifa") ? "IDTARIFA"
                : columns.Contains("id_tarifa") ? "ID_TARIFA"
                : null;
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            var hasAdicionalFijo1Descripcion = columns.Contains("adicionalfijo1descripcion");
            var hasAdicionalFijo1Importe = columns.Contains("adicionalfijo1importe");
            var hasAdicionalFijo2Descripcion = columns.Contains("adicionalfijo2descripcion");
            var hasAdicionalFijo2Importe = columns.Contains("adicionalfijo2importe");
            var hasAdicionalFijo3Descripcion = columns.Contains("adicionalfijo3descripcion");
            var hasAdicionalFijo3Importe = columns.Contains("adicionalfijo3importe");
            var sql = $"""
                SELECT TOP (1)
                    {(string.IsNullOrWhiteSpace(idColumn) ? "CAST(0 AS int)" : $"ISNULL({idColumn}, 0)") } AS Id,
                    LTRIM(RTRIM(ISNULL(t.{idListaColumn}, ''))) AS IdLista,
                    ISNULL(t.Nombre, '') AS Nombre,
                    ISNULL(t.Importe, 0) AS Importe,
                    CASE WHEN ISNULL(cli.RAZON_SOCIAL, '') = '' THEN LTRIM(RTRIM(ISNULL(t.{clienteColumn}, ''))) ELSE LTRIM(RTRIM(ISNULL(t.{clienteColumn}, ''))) + ' - ' + LTRIM(RTRIM(cli.RAZON_SOCIAL)) END AS Cliente,
                    CASE WHEN ISNULL(ch.NOMBRES, '') = '' THEN LTRIM(RTRIM(ISNULL(t.{choferColumn}, ''))) ELSE LTRIM(RTRIM(ISNULL(t.{choferColumn}, ''))) + ' - ' + LTRIM(RTRIM(ch.NOMBRES)) END AS Chofer,
                    CASE WHEN ISNULL(d.DESCRIPCION, '') = '' THEN LTRIM(RTRIM(ISNULL(t.{destinoColumn}, ''))) ELSE LTRIM(RTRIM(ISNULL(t.{destinoColumn}, ''))) + ' - ' + LTRIM(RTRIM(d.DESCRIPCION)) END AS Destino,
                    CASE WHEN ISNULL(tv.DESCRIPCION, '') = '' THEN LTRIM(RTRIM(ISNULL(t.{tipoVehiculoColumn}, ''))) ELSE LTRIM(RTRIM(ISNULL(t.{tipoVehiculoColumn}, ''))) + ' - ' + LTRIM(RTRIM(tv.DESCRIPCION)) END AS TipoVehiculo,
                    ISNULL(t.TarifaFletero, 0) AS TarifaFletero,
                    ISNULL(t.PorcentajeAdic, 0) AS PorcentajeAdic,
                    ISNULL(t.PorcentajeAdic1, 0) AS PorcentajeAdic1,
                    ISNULL(t.PorcentajeAdic2, 0) AS PorcentajeAdic2,
                    ISNULL(t.PorcentajeAdic3, 0) AS PorcentajeAdic3,
                    ISNULL(t.PorcentajeAdic4, 0) AS PorcentajeAdic4,
                    {(hasAdicionalFijo1Descripcion ? "ISNULL(t.AdicionalFijo1Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo1Descripcion,
                    {(hasAdicionalFijo1Importe ? "ISNULL(t.AdicionalFijo1Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo1Importe,
                    {(hasAdicionalFijo2Descripcion ? "ISNULL(t.AdicionalFijo2Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo2Descripcion,
                    {(hasAdicionalFijo2Importe ? "ISNULL(t.AdicionalFijo2Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo2Importe,
                    {(hasAdicionalFijo3Descripcion ? "ISNULL(t.AdicionalFijo3Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo3Descripcion,
                    {(hasAdicionalFijo3Importe ? "ISNULL(t.AdicionalFijo3Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo3Importe,
                    ISNULL(t.Activo, 1) AS Activo
                FROM dbo.TA_TARIFA t
                LEFT JOIN dbo.Vt_Clientes cli ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(t.{clienteColumn}, ''))))
                LEFT JOIN dbo.TA_CHOFERES ch ON UPPER(LTRIM(RTRIM(ch.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(t.{choferColumn}, ''))))
                LEFT JOIN dbo.TA_DESTINOS d ON UPPER(LTRIM(RTRIM(d.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(t.{destinoColumn}, ''))))
                LEFT JOIN dbo.TA_TIPOVEHICULO tv ON UPPER(LTRIM(RTRIM(tv.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(t.{tipoVehiculoColumn}, ''))))
                WHERE UPPER(LTRIM(RTRIM(t.{idListaColumn}))) = @IdLista;
                """;

            var row = await cn.QuerySingleOrDefaultAsync<CargaViajeTarifaGridItemDto>(new CommandDefinition(sql, new { IdLista = idLista.Trim().ToUpperInvariant() }, cancellationToken: token));
            return row;
        }, "No se pudo cargar la tarifa seleccionada.", ct);

    public Task<CargaViajeTarifaGridItemDto?> GetTarifaByIdAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTarifaByInternalId", async token =>
        {
            if (id <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                return null;

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idColumn = columns.Contains("id") ? "ID"
                : columns.Contains("idtarifa") ? "IDTARIFA"
                : columns.Contains("id_tarifa") ? "ID_TARIFA"
                : null;
            if (string.IsNullOrWhiteSpace(idColumn))
                return null;
            var hasAdicionalFijo1Descripcion = columns.Contains("adicionalfijo1descripcion");
            var hasAdicionalFijo1Importe = columns.Contains("adicionalfijo1importe");
            var hasAdicionalFijo2Descripcion = columns.Contains("adicionalfijo2descripcion");
            var hasAdicionalFijo2Importe = columns.Contains("adicionalfijo2importe");
            var hasAdicionalFijo3Descripcion = columns.Contains("adicionalfijo3descripcion");
            var hasAdicionalFijo3Importe = columns.Contains("adicionalfijo3importe");

            var row = await cn.QuerySingleOrDefaultAsync<CargaViajeTarifaGridItemDto>(new CommandDefinition($"""
                SELECT TOP (1)
                    ISNULL({idColumn}, 0) AS Id,
                    LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDLISTA", "ID_LISTA")}, ''))) AS IdLista,
                    ISNULL(t.Nombre, '') AS Nombre,
                    ISNULL(t.Importe, 0) AS Importe,
                    CASE WHEN ISNULL(cli.RAZON_SOCIAL, '') = '' THEN LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE")}, ''))) ELSE LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE")}, ''))) + ' - ' + LTRIM(RTRIM(cli.RAZON_SOCIAL)) END AS Cliente,
                    CASE WHEN ISNULL(ch.NOMBRES, '') = '' THEN LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDCHOFER", "CHOFER")}, ''))) ELSE LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDCHOFER", "CHOFER")}, ''))) + ' - ' + LTRIM(RTRIM(ch.NOMBRES)) END AS Chofer,
                    CASE WHEN ISNULL(d.DESCRIPCION, '') = '' THEN LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDDESTINO", "DESTINO")}, ''))) ELSE LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDDESTINO", "DESTINO")}, ''))) + ' - ' + LTRIM(RTRIM(d.DESCRIPCION)) END AS Destino,
                    CASE WHEN ISNULL(tv.DESCRIPCION, '') = '' THEN LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO")}, ''))) ELSE LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO")}, ''))) + ' - ' + LTRIM(RTRIM(tv.DESCRIPCION)) END AS TipoVehiculo,
                    ISNULL(t.TarifaFletero, 0) AS TarifaFletero,
                    ISNULL(t.PorcentajeAdic, 0) AS PorcentajeAdic,
                    ISNULL(t.PorcentajeAdic1, 0) AS PorcentajeAdic1,
                    ISNULL(t.PorcentajeAdic2, 0) AS PorcentajeAdic2,
                    ISNULL(t.PorcentajeAdic3, 0) AS PorcentajeAdic3,
                    ISNULL(t.PorcentajeAdic4, 0) AS PorcentajeAdic4,
                    {(hasAdicionalFijo1Descripcion ? "ISNULL(t.AdicionalFijo1Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo1Descripcion,
                    {(hasAdicionalFijo1Importe ? "ISNULL(t.AdicionalFijo1Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo1Importe,
                    {(hasAdicionalFijo2Descripcion ? "ISNULL(t.AdicionalFijo2Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo2Descripcion,
                    {(hasAdicionalFijo2Importe ? "ISNULL(t.AdicionalFijo2Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo2Importe,
                    {(hasAdicionalFijo3Descripcion ? "ISNULL(t.AdicionalFijo3Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo3Descripcion,
                    {(hasAdicionalFijo3Importe ? "ISNULL(t.AdicionalFijo3Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo3Importe,
                    ISNULL(t.Activo, 1) AS Activo
                FROM dbo.TA_TARIFA t
                LEFT JOIN dbo.Vt_Clientes cli ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE")}, ''))))
                LEFT JOIN dbo.TA_CHOFERES ch ON UPPER(LTRIM(RTRIM(ch.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDCHOFER", "CHOFER")}, ''))))
                LEFT JOIN dbo.TA_DESTINOS d ON UPPER(LTRIM(RTRIM(d.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDDESTINO", "DESTINO")}, ''))))
                LEFT JOIN dbo.TA_TIPOVEHICULO tv ON UPPER(LTRIM(RTRIM(tv.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(t.{FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO")}, ''))))
                WHERE {idColumn} = @Id;
                """, new { Id = id }, cancellationToken: token));
            return row;
        }, "No se pudo cargar la tarifa seleccionada.", ct);

    public Task ActualizarTarifaImporteAsync(int id, string idLista, decimal importe, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ActualizarTarifaImporte", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                throw new InvalidOperationException("La tabla TA_TARIFA no existe en la base activa.");

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idColumn = columns.Contains("id") ? "ID"
                : columns.Contains("idtarifa") ? "IDTARIFA"
                : columns.Contains("id_tarifa") ? "ID_TARIFA"
                : null;
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");

            if (id > 0 && !string.IsNullOrWhiteSpace(idColumn))
            {
                var sqlById = $"UPDATE dbo.TA_TARIFA SET Importe = @Importe WHERE {idColumn} = @Id;";
                var affectedById = await cn.ExecuteAsync(new CommandDefinition(sqlById, new { Id = id, Importe = importe }, cancellationToken: token));
                if (affectedById <= 0)
                    throw new InvalidOperationException("No se pudo actualizar el importe de la tarifa seleccionada.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(idListaColumn) && !string.IsNullOrWhiteSpace(idLista))
            {
                var sqlByLista = $"""
                    UPDATE dbo.TA_TARIFA
                    SET Importe = @Importe
                    WHERE UPPER(LTRIM(RTRIM({idListaColumn}))) = @IdLista;
                    """;
                var affectedByLista = await cn.ExecuteAsync(new CommandDefinition(sqlByLista, new { IdLista = idLista.Trim().ToUpperInvariant(), Importe = importe }, cancellationToken: token));
                if (affectedByLista <= 0)
                    throw new InvalidOperationException("No se pudo actualizar el importe de la tarifa seleccionada.");
                return;
            }

            throw new InvalidOperationException("No se pudo identificar la tarifa seleccionada.");
        }, "No se pudo actualizar el importe de la tarifa.", ct);

    public Task<string> SaveTarifaAsync(CargaViajeTarifaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveTarifa", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var validation = await validator.ValidateTarifaForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                throw new InvalidOperationException("La tabla TA_TARIFA no existe en la base activa.");

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idColumn = columns.Contains("id") ? "ID"
                : columns.Contains("idtarifa") ? "IDTARIFA"
                : columns.Contains("id_tarifa") ? "ID_TARIFA"
                : null;
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            var hasAdicFijo1Desc = columns.Contains("adicional_fijo1_descripcion");
            var hasAdicFijo1Importe = columns.Contains("adicional_fijo1_importe");
            var hasAdicFijo2Desc = columns.Contains("adicional_fijo2_descripcion");
            var hasAdicFijo2Importe = columns.Contains("adicional_fijo2_importe");
            var hasAdicFijo3Desc = columns.Contains("adicional_fijo3_descripcion");
            var hasAdicFijo3Importe = columns.Contains("adicional_fijo3_importe");

            var idLista = request.IdLista.Trim().ToUpperInvariant();
            var originalIdLista = string.IsNullOrWhiteSpace(request.OriginalIdLista)
                ? idLista
                : request.OriginalIdLista.Trim().ToUpperInvariant();
            var tarifaId = request.Id;
            var cliente = request.TarifaFletero ? null : string.IsNullOrWhiteSpace(request.Cliente) ? null : request.Cliente.Trim();
            var chofer = request.TarifaFletero ? string.IsNullOrWhiteSpace(request.Chofer) ? null : request.Chofer.Trim() : null;
            var destino = string.IsNullOrWhiteSpace(request.Destino) ? null : request.Destino.Trim();
            var tipoVehiculo = string.IsNullOrWhiteSpace(request.TipoVehiculo) ? null : request.TipoVehiculo.Trim();
            var existsSql = tarifaId > 0 && !string.IsNullOrWhiteSpace(idColumn)
                ? $"""
                   SELECT TOP (1) 1
                   FROM dbo.TA_TARIFA
                   WHERE {idColumn} = @Id;
                   """
                : $"""
                   SELECT TOP (1) 1
                   FROM dbo.TA_TARIFA
                   WHERE UPPER(LTRIM(RTRIM(ISNULL({idListaColumn}, '')))) = @IdLista;
                   """;
            var exists = tarifaId > 0 && !string.IsNullOrWhiteSpace(idColumn)
                ? await cn.ExecuteScalarAsync<int?>(new CommandDefinition(existsSql, new { Id = tarifaId }, cancellationToken: token))
                : await cn.ExecuteScalarAsync<int?>(new CommandDefinition(existsSql, new { IdLista = originalIdLista }, cancellationToken: token));
            var isNew = !exists.HasValue;
            var insertColumns = new List<string>
            {
                idListaColumn,
                "Nombre",
                "Importe",
                clienteColumn,
                choferColumn,
                destinoColumn,
                tipoVehiculoColumn,
                "TarifaFletero",
                "PorcentajeAdic",
                "PorcentajeAdic1",
                "PorcentajeAdic2",
                "PorcentajeAdic3",
                "PorcentajeAdic4"
            };
            var insertValues = new List<string>
            {
                "@IdLista",
                "@Nombre",
                "@Importe",
                "@Cliente",
                "@Chofer",
                "@Destino",
                "@TipoVehiculo",
                "@TarifaFletero",
                "@PorcentajeAdic",
                "@PorcentajeAdic1",
                "@PorcentajeAdic2",
                "@PorcentajeAdic3",
                "@PorcentajeAdic4"
            };
            AddColumnPair(insertColumns, insertValues, hasAdicFijo1Desc ? "AdicionalFijo1Descripcion" : null, "@AdicionalFijo1Descripcion");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo1Importe ? "AdicionalFijo1Importe" : null, "@AdicionalFijo1Importe");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo2Desc ? "AdicionalFijo2Descripcion" : null, "@AdicionalFijo2Descripcion");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo2Importe ? "AdicionalFijo2Importe" : null, "@AdicionalFijo2Importe");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo3Desc ? "AdicionalFijo3Descripcion" : null, "@AdicionalFijo3Descripcion");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo3Importe ? "AdicionalFijo3Importe" : null, "@AdicionalFijo3Importe");
            insertColumns.Add("Activo");
            insertValues.Add("@Activo");

            var updateParts = new List<string>
            {
                "Nombre = @Nombre",
                "Importe = @Importe",
                $"{clienteColumn} = @Cliente",
                $"{choferColumn} = @Chofer",
                $"{destinoColumn} = @Destino",
                $"{tipoVehiculoColumn} = @TipoVehiculo",
                "TarifaFletero = @TarifaFletero",
                "PorcentajeAdic = @PorcentajeAdic",
                "PorcentajeAdic1 = @PorcentajeAdic1",
                "PorcentajeAdic2 = @PorcentajeAdic2",
                "PorcentajeAdic3 = @PorcentajeAdic3",
                "PorcentajeAdic4 = @PorcentajeAdic4"
            };
            AddUpdatePart(updateParts, hasAdicFijo1Desc ? "AdicionalFijo1Descripcion" : null, "@AdicionalFijo1Descripcion");
            AddUpdatePart(updateParts, hasAdicFijo1Importe ? "AdicionalFijo1Importe" : null, "@AdicionalFijo1Importe");
            AddUpdatePart(updateParts, hasAdicFijo2Desc ? "AdicionalFijo2Descripcion" : null, "@AdicionalFijo2Descripcion");
            AddUpdatePart(updateParts, hasAdicFijo2Importe ? "AdicionalFijo2Importe" : null, "@AdicionalFijo2Importe");
            AddUpdatePart(updateParts, hasAdicFijo3Desc ? "AdicionalFijo3Descripcion" : null, "@AdicionalFijo3Descripcion");
            AddUpdatePart(updateParts, hasAdicFijo3Importe ? "AdicionalFijo3Importe" : null, "@AdicionalFijo3Importe");
            updateParts.Add("Activo = @Activo");

            var sql = isNew
                ? $"""
                INSERT INTO dbo.TA_TARIFA
                (
                    {string.Join(", ", insertColumns)}
                )
                VALUES
                (
                    {string.Join(", ", insertValues)}
                );
                """
                : $"""
                UPDATE dbo.TA_TARIFA
                SET {string.Join(", ", updateParts)}
                WHERE {(tarifaId > 0 && !string.IsNullOrWhiteSpace(idColumn) ? $"{idColumn} = @Id" : $"UPPER(LTRIM(RTRIM({idListaColumn}))) = @OriginalIdLista")};
                """;

            var parameters = new Dapper.DynamicParameters();
            parameters.Add("@Id", tarifaId);
            parameters.Add("@IdLista", idLista);
            parameters.Add("@OriginalIdLista", originalIdLista);
            parameters.Add("@Nombre", request.Nombre.Trim());
            parameters.Add("@Importe", request.Importe);
            parameters.Add("@Cliente", cliente);
            parameters.Add("@Chofer", chofer);
            parameters.Add("@Destino", destino);
            parameters.Add("@TipoVehiculo", tipoVehiculo);
            parameters.Add("@TarifaFletero", request.TarifaFletero);
            parameters.Add("@PorcentajeAdic", request.PorcentajeAdic);
            parameters.Add("@PorcentajeAdic1", request.PorcentajeAdic1);
            parameters.Add("@PorcentajeAdic2", request.PorcentajeAdic2);
            parameters.Add("@PorcentajeAdic3", request.PorcentajeAdic3);
            parameters.Add("@PorcentajeAdic4", request.PorcentajeAdic4);
            if (hasAdicFijo1Desc) parameters.Add("@AdicionalFijo1Descripcion", (request.AdicionalFijo1Descripcion ?? string.Empty).Trim());
            if (hasAdicFijo1Importe) parameters.Add("@AdicionalFijo1Importe", request.AdicionalFijo1Importe);
            if (hasAdicFijo2Desc) parameters.Add("@AdicionalFijo2Descripcion", (request.AdicionalFijo2Descripcion ?? string.Empty).Trim());
            if (hasAdicFijo2Importe) parameters.Add("@AdicionalFijo2Importe", request.AdicionalFijo2Importe);
            if (hasAdicFijo3Desc) parameters.Add("@AdicionalFijo3Descripcion", (request.AdicionalFijo3Descripcion ?? string.Empty).Trim());
            if (hasAdicFijo3Importe) parameters.Add("@AdicionalFijo3Importe", request.AdicionalFijo3Importe);
            parameters.Add("@Activo", request.Activo);

            await cn.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: token));

            return idLista;
        }, "No se pudo guardar la tarifa.", ct);

    public Task BajaTarifaAsync(string idLista, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "BajaTarifa", async token =>
        {
            if (string.IsNullOrWhiteSpace(idLista))
                throw new InvalidOperationException("No se recibiÃ³ la tarifa a dar de baja.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                throw new InvalidOperationException("La tabla TA_TARIFA no existe en la base activa.");
            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var sql = $"""
                UPDATE dbo.TA_TARIFA
                SET Activo = 0
                WHERE UPPER(LTRIM(RTRIM({idListaColumn}))) = @IdLista;
                """;
            var affected = await cn.ExecuteAsync(new CommandDefinition(sql, new { IdLista = idLista.Trim().ToUpperInvariant() }, cancellationToken: token));
            if (affected == 0)
                throw new InvalidOperationException("La tarifa seleccionada ya no existe en la base activa.");
        }, "No se pudo dar de baja la tarifa.", ct);

    public Task BajaTarifaAsync(int id, string idLista, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "BajaTarifaById", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                throw new InvalidOperationException("La tabla TA_TARIFA no existe en la base activa.");

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idColumn = columns.Contains("id") ? "ID"
                : columns.Contains("idtarifa") ? "IDTARIFA"
                : columns.Contains("id_tarifa") ? "ID_TARIFA"
                : null;
            if (id > 0 && !string.IsNullOrWhiteSpace(idColumn))
            {
                var affectedById = await cn.ExecuteAsync(new CommandDefinition($"UPDATE dbo.TA_TARIFA SET Activo = 0 WHERE {idColumn} = @Id;", new { Id = id }, cancellationToken: token));
                if (affectedById == 0)
                    throw new InvalidOperationException("La tarifa seleccionada ya no existe en la base activa.");
                return;
            }

            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var affectedByLista = await cn.ExecuteAsync(new CommandDefinition($"""
                UPDATE dbo.TA_TARIFA
                SET Activo = 0
                WHERE UPPER(LTRIM(RTRIM({idListaColumn}))) = @IdLista;
                """, new { IdLista = (idLista ?? string.Empty).Trim().ToUpperInvariant() }, cancellationToken: token));
            if (affectedByLista == 0)
                throw new InvalidOperationException("La tarifa seleccionada ya no existe en la base activa.");
        }, "No se pudo dar de baja la tarifa.", ct);

    public Task<PagedResult<CargaViajeChoferGridItemDto>> SearchChoferesAsync(CargaViajesFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchChoferes", async token =>
        {
            filters ??= new CargaViajesFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_CHOFERES";
            var columns = await LoadColumnsAsync(cn, table, token);
            var nombreExpr = "LTRIM(RTRIM(ISNULL(NOMBRES, '')))";
            var esFleteroColumn = columns.Contains("es_fletero") ? "ES_FLETERO" : null;
            var disponibleColumn = columns.Contains("disponible") ? "DISPONIBLE" : null;
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            var whereClauses = new List<string>
            {
                @"(
                        @TextoLike = ''
                        OR CODIGO LIKE @TextoLike
                        OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    )"
            };
            if (columns.Contains("activo"))
                whereClauses.Add("(@Activo IS NULL OR ISNULL(ACTIVO, 1) = @Activo)");
            if (disponibleColumn is not null)
                whereClauses.Add("(@Disponible IS NULL OR ISNULL(DISPONIBLE, 0) = @Disponible)");
            if (esFleteroColumn is not null)
                whereClauses.Add("(@EsFletero IS NULL OR ISNULL(ES_FLETERO, 0) = @EsFletero)");
            if (tipoVehiculoColumn is not null)
                whereClauses.Add($"(@TipoVehiculo = '' OR UPPER(LTRIM(RTRIM(ISNULL({tipoVehiculoColumn}, '')))) LIKE @TipoVehiculoLike)");

            var orderBy = BuildOrderByClause(
                filters.SortBy,
                filters.SortDescending,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["codigo"] = "LTRIM(RTRIM(ISNULL(CODIGO, ''))) {dir}",
                    ["nombre"] = "LTRIM(RTRIM(ISNULL(NOMBRES, ''))) {dir}, LTRIM(RTRIM(ISNULL(CODIGO, ''))) {dir}",
                    ["activo"] = "ISNULL(ACTIVO, 1) {dir}, LTRIM(RTRIM(ISNULL(NOMBRES, ''))) {dir}",
                    ["disponible"] = $"ISNULL({(disponibleColumn is not null ? disponibleColumn : "0")}, 0) {{dir}}, LTRIM(RTRIM(ISNULL(NOMBRES, ''))) {{dir}}",
                    ["esfletero"] = $"ISNULL({(esFleteroColumn is not null ? esFleteroColumn : "0")}, 0) {{dir}}, LTRIM(RTRIM(ISNULL(NOMBRES, ''))) {{dir}}"
                },
                "LTRIM(RTRIM(ISNULL(NOMBRES, ''))) ASC, LTRIM(RTRIM(ISNULL(CODIGO, ''))) ASC");

            var sql = $"""
                SELECT
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    {nombreExpr} AS Nombre,
                    ISNULL(ACTIVO, 1) AS Activo,
                    {(disponibleColumn is not null ? "ISNULL(DISPONIBLE, 0)" : "CAST(0 AS bit)")} AS Disponible,
                    {(esFleteroColumn is not null ? "ISNULL(ES_FLETERO, 0)" : "CAST(0 AS bit)")} AS EsFletero
                FROM dbo.{table}
                WHERE {string.Join(Environment.NewLine + "  AND ", whereClauses)}
                ORDER BY {orderBy}
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.{table}
                WHERE {string.Join(Environment.NewLine + "  AND ", whereClauses)};
                """;

            var rows = new List<CargaViajeChoferGridItemDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@TextoLike", SearchTextHelper.LikeContains(filters.Texto));
            cmd.Parameters.AddWithValue("@Activo", (object?)ParseNullableBitFilter(filters.Activo) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Disponible", (object?)ParseNullableBitFilter(filters.Disponible) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EsFletero", (object?)ParseNullableBitFilter(filters.EsFletero) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TipoVehiculo", (filters.TipoVehiculo ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("@TipoVehiculoLike", SearchTextHelper.LikeContains(filters.TipoVehiculo));
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                rows.Add(new CargaViajeChoferGridItemDto
                {
                    Codigo = GetString(rd, 0),
                    Nombre = GetString(rd, 1),
                    Activo = GetBool(rd, 2),
                    Disponible = GetBool(rd, 3),
                    EsFletero = GetBool(rd, 4)
                });
            }

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
                total = GetInt(rd, 0);

            return new PagedResult<CargaViajeChoferGridItemDto> { Items = rows, Total = total, PageNumber = pageNumber, PageSize = pageSize };
        }, "No se pudieron cargar los choferes.", ct);

    public Task<CargaViajeChoferGridItemDto?> GetChoferByIdAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetChoferById", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_CHOFERES";
            var columns = await LoadColumnsAsync(cn, table, token);
            var disponibleExpr = columns.Contains("disponible") ? "ISNULL(DISPONIBLE, 0)" : "CAST(0 AS bit)";
            var esFleteroExpr = columns.Contains("es_fletero") ? "ISNULL(ES_FLETERO, 0)" : "CAST(0 AS bit)";
            var sql = $"""
                SELECT TOP (1)
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(NOMBRES, ''))) AS Nombre,
                    ISNULL(ACTIVO, 1) AS Activo,
                    {disponibleExpr} AS Disponible,
                    {esFleteroExpr} AS EsFletero
                FROM dbo.{table}
                WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                """;

            return await cn.QuerySingleOrDefaultAsync<CargaViajeChoferGridItemDto>(new CommandDefinition(sql, new { Codigo = codigo.Trim().ToUpperInvariant() }, cancellationToken: token));
        }, "No se pudo cargar el chofer seleccionado.", ct);

    public Task<string> SaveChoferAsync(CargaViajeChoferSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveChofer", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var validation = await validator.ValidateChoferForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_CHOFERES";
            var isNew = !await ExistsByCodeAsync(cn, table, "CODIGO", request.Codigo, token);
            var hasEsFletero = await ColumnExistsAsync(cn, table, "ES_FLETERO", token);
            var sql = isNew
                ? $"""
                    INSERT INTO dbo.{table} (CODIGO, NOMBRES, ACTIVO{(hasEsFletero ? ", ES_FLETERO" : string.Empty)})
                    VALUES (@Codigo, @Nombre, @Activo{(hasEsFletero ? ", @EsFletero" : string.Empty)});
                    """
                : $"""
                    UPDATE dbo.{table}
                    SET NOMBRES = @Nombre, ACTIVO = @Activo{(hasEsFletero ? ", ES_FLETERO = @EsFletero" : string.Empty)}
                    WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                    """;

            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Codigo = request.Codigo.Trim().ToUpperInvariant(),
                Nombre = request.Nombre.Trim(),
                request.Activo,
                EsFletero = hasEsFletero && request.EsFletero
            }, cancellationToken: token));

            return request.Codigo.Trim().ToUpperInvariant();
        }, "No se pudo guardar el chofer.", ct);

    public Task<string> GetNextCodigoChoferAsync(CancellationToken ct = default)
        => GetNextCodigoFromTableAsync("TA_CHOFERES", "CODIGO", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchFleterosLookupAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchFleterosLookup", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            if (search.Length < 1)
                return (IReadOnlyList<CargaViajeLookupOptionDto>)Array.Empty<CargaViajeLookupOptionDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_CHOFERES";
            if (!await ColumnExistsAsync(cn, table, "ES_FLETERO", token))
                return (IReadOnlyList<CargaViajeLookupOptionDto>)Array.Empty<CargaViajeLookupOptionDto>();
            var sql = $"""
                SELECT TOP (12)
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(NOMBRES, ''))) AS Titulo,
                    '' AS Subtitulo
                FROM dbo.{table}
                WHERE ISNULL(ES_FLETERO, 0) = 1
                  AND (CODIGO LIKE @Search OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @Search)
                ORDER BY NOMBRES, CODIGO;
                """;

            var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = SearchTextHelper.LikeContains(search) }, cancellationToken: token))).ToList();
            return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
        }, "No se pudieron buscar fleteros.", ct);

    public Task BajaChoferAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "BajaChofer", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se recibiÃ³ el chofer a dar de baja.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_CHOFERES";
            var sql = $"UPDATE dbo.{table} SET ACTIVO = 0 WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;";

            var affected = await cn.ExecuteAsync(new CommandDefinition(sql, new { Codigo = codigo.Trim().ToUpperInvariant() }, cancellationToken: token));
            if (affected == 0)
                throw new InvalidOperationException("El chofer seleccionado ya no existe en la base activa.");
        }, "No se pudo dar de baja el chofer.", ct);

    public Task<PagedResult<CargaViajeDestinoGridItemDto>> SearchDestinosAsync(CargaViajesFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchDestinos", async token =>
        {
            filters ??= new CargaViajesFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_DESTINOS";
            var codeColumn = "CODIGO";
            var textoLike = SearchTextHelper.LikeContains(filters.Texto);
            var activoFiltro = ParseNullableBitFilter(filters.Activo);
            var orderBy = BuildOrderByClause(
                filters.SortBy,
                filters.SortDescending,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["codigo"] = "LTRIM(RTRIM(ISNULL(CODIGO, ''))) {dir}",
                    ["descripcion"] = "LTRIM(RTRIM(ISNULL(Descripcion, ''))) {dir}, LTRIM(RTRIM(ISNULL(CODIGO, ''))) {dir}",
                    ["activo"] = "ISNULL(Activo, 1) {dir}, LTRIM(RTRIM(ISNULL(Descripcion, ''))) {dir}"
                },
                "LTRIM(RTRIM(ISNULL(Descripcion, ''))) ASC, LTRIM(RTRIM(ISNULL(CODIGO, ''))) ASC");
            var sql = $"""
                SELECT
                    LTRIM(RTRIM(ISNULL({codeColumn}, ''))),
                    LTRIM(RTRIM(ISNULL(Descripcion, ''))),
                    ISNULL(Activo, 1)
                FROM dbo.{table}
                WHERE (
                        @TextoLike = ''
                        OR {codeColumn} LIKE @TextoLike
                        OR Descripcion COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    )
                  AND (@Activo IS NULL OR ISNULL(Activo, 1) = @Activo)
                ORDER BY {orderBy}
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.{table}
                WHERE (
                        @TextoLike = ''
                        OR {codeColumn} LIKE @TextoLike
                        OR Descripcion COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    )
                  AND (@Activo IS NULL OR ISNULL(Activo, 1) = @Activo);
                """;

            var rows = new List<CargaViajeDestinoGridItemDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@TextoLike", textoLike);
            cmd.Parameters.AddWithValue("@Activo", (object?)activoFiltro ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                rows.Add(new CargaViajeDestinoGridItemDto
                {
                    Codigo = GetString(rd, 0),
                    Descripcion = GetString(rd, 1),
                    Activo = GetBool(rd, 2)
                });
            }

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
                total = GetInt(rd, 0);

            return new PagedResult<CargaViajeDestinoGridItemDto> { Items = rows, Total = total, PageNumber = pageNumber, PageSize = pageSize };
        }, "No se pudieron cargar los destinos.", ct);

    public Task<CargaViajeDestinoGridItemDto?> GetDestinoByIdAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetDestinoById", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_DESTINOS";
            var codeColumn = "CODIGO";
            var sql = $"""
                SELECT TOP (1)
                    LTRIM(RTRIM(ISNULL({codeColumn}, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(Descripcion, ''))) AS Descripcion,
                    ISNULL(Activo, 1) AS Activo
                FROM dbo.{table}
                WHERE UPPER(LTRIM(RTRIM({codeColumn}))) = @Codigo;
                """;

            return await cn.QuerySingleOrDefaultAsync<CargaViajeDestinoGridItemDto>(new CommandDefinition(sql, new { Codigo = codigo.Trim().ToUpperInvariant() }, cancellationToken: token));
        }, "No se pudo cargar el destino seleccionado.", ct);

    public Task<string> SaveDestinoAsync(CargaViajeDestinoSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveDestino", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var validation = await validator.ValidateDestinoForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_DESTINOS";
            var codeColumn = "CODIGO";
            var isNew = !await ExistsByCodeAsync(cn, table, codeColumn, request.Codigo, token);
            var sql = isNew
                ? $"""
                    INSERT INTO dbo.{table} ({codeColumn}, Descripcion, Activo)
                    VALUES (@Codigo, @Descripcion, @Activo);
                    """
                : $"""
                    UPDATE dbo.{table}
                    SET Descripcion = @Descripcion, Activo = @Activo
                    WHERE UPPER(LTRIM(RTRIM({codeColumn}))) = @Codigo;
                    """;

            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Codigo = request.Codigo.Trim().ToUpperInvariant(),
                Descripcion = request.Descripcion.Trim(),
                request.Activo
            }, cancellationToken: token));

            return request.Codigo.Trim().ToUpperInvariant();
        }, "No se pudo guardar el destino.", ct);

    public Task<string> GetNextCodigoDestinoAsync(CancellationToken ct = default)
        => GetNextCodigoFromTableAsync("TA_DESTINOS", "CODIGO", ct);

    public Task BajaDestinoAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "BajaDestino", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se recibiÃ³ el destino a dar de baja.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_DESTINOS";
            var codeColumn = "CODIGO";
            if (!await ColumnExistsAsync(cn, table, "Activo", token))
                throw new InvalidOperationException($"La tabla {table} no tiene columna Activo para hacer baja lÃ³gica.");

            var affected = await cn.ExecuteAsync(new CommandDefinition($"""
                UPDATE dbo.{table}
                SET Activo = 0
                WHERE UPPER(LTRIM(RTRIM({codeColumn}))) = @Codigo;
                """, new { Codigo = codigo.Trim().ToUpperInvariant() }, cancellationToken: token));
            if (affected == 0)
                throw new InvalidOperationException("El destino seleccionado ya no existe en la base activa.");
        }, "No se pudo dar de baja el destino.", ct);

    public Task<PagedResult<CargaViajeTipoVehiculoGridItemDto>> SearchTipoVehiculosAsync(CargaViajesFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchTipoVehiculos", async token =>
        {
            filters ??= new CargaViajesFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TIPOVEHICULO", token))
                throw new InvalidOperationException("La tabla TA_TIPOVEHICULO no existe en la base activa.");

            var hasActivo = await ColumnExistsAsync(cn, "TA_TIPOVEHICULO", "ACTIVO", token);
            var activoExpr = hasActivo ? "ISNULL(ACTIVO, 1)" : "1";
            var activoSelect = hasActivo ? "CAST(ISNULL(ACTIVO, 1) AS bit)" : "CAST(NULL AS bit)";
            var orderBy = BuildOrderByClause(
                filters.SortBy,
                filters.SortDescending,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["codigo"] = "LTRIM(RTRIM(ISNULL(CODIGO, ''))) {dir}",
                    ["descripcion"] = "LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) {dir}, LTRIM(RTRIM(ISNULL(CODIGO, ''))) {dir}",
                    ["activo"] = $"{activoExpr} {{dir}}, LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) {{dir}}"
                },
                "LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) ASC, LTRIM(RTRIM(ISNULL(CODIGO, ''))) ASC");
            var sql = $"""
                SELECT
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) AS Descripcion,
                    {activoSelect} AS Activo
                FROM dbo.TA_TIPOVEHICULO
                WHERE (
                        @TextoLike = ''
                        OR CODIGO LIKE @TextoLike
                        OR DESCRIPCION COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    )
                  AND (@Activo IS NULL OR {activoExpr} = @Activo)
                ORDER BY {orderBy}
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.TA_TIPOVEHICULO
                WHERE (
                        @TextoLike = ''
                        OR CODIGO LIKE @TextoLike
                        OR DESCRIPCION COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    )
                  AND (@Activo IS NULL OR {activoExpr} = @Activo);
                """;

            var rows = (await cn.QueryAsync<CargaViajeTipoVehiculoGridItemDto>(new CommandDefinition(sql, new
            {
                TextoLike = SearchTextHelper.LikeContains(filters.Texto),
                Activo = ParseNullableBitFilter(filters.Activo),
                Skip = skip,
                PageSize = pageSize
            }, cancellationToken: token))).ToList();

            var total = await cn.ExecuteScalarAsync<int>(new CommandDefinition($"""
                SELECT COUNT(*)
                FROM dbo.TA_TIPOVEHICULO
                WHERE (
                        @TextoLike = ''
                        OR CODIGO LIKE @TextoLike
                        OR DESCRIPCION COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    )
                  AND (@Activo IS NULL OR {activoExpr} = @Activo);
                """, new
            {
                TextoLike = SearchTextHelper.LikeContains(filters.Texto),
                Activo = ParseNullableBitFilter(filters.Activo)
            }, cancellationToken: token));

            return new PagedResult<CargaViajeTipoVehiculoGridItemDto>
            {
                Items = rows,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar los tipos de vehÃ­culo.", ct);

    public Task<CargaViajeTipoVehiculoGridItemDto?> GetTipoVehiculoByIdAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTipoVehiculoById", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TIPOVEHICULO", token))
                throw new InvalidOperationException("La tabla TA_TIPOVEHICULO no existe en la base activa.");

            var hasActivo = await ColumnExistsAsync(cn, "TA_TIPOVEHICULO", "ACTIVO", token);
            var activoSelect = hasActivo ? "CAST(ISNULL(ACTIVO, 1) AS bit)" : "CAST(NULL AS bit)";
            var sql = $"""
                SELECT TOP (1)
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) AS Descripcion,
                    {activoSelect} AS Activo
                FROM dbo.TA_TIPOVEHICULO
                WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                """;

            return await cn.QuerySingleOrDefaultAsync<CargaViajeTipoVehiculoGridItemDto>(new CommandDefinition(sql, new { Codigo = codigo.Trim().ToUpperInvariant() }, cancellationToken: token));
        }, "No se pudo cargar el tipo de vehÃ­culo seleccionado.", ct);

    public Task<string> SaveTipoVehiculoAsync(CargaViajeTipoVehiculoSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveTipoVehiculo", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Codigo))
                throw new InvalidOperationException("El cÃ³digo del tipo de vehÃ­culo es obligatorio.");
            if (string.IsNullOrWhiteSpace(request.Descripcion))
                throw new InvalidOperationException("La descripciÃ³n del tipo de vehÃ­culo es obligatoria.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TIPOVEHICULO", token))
                throw new InvalidOperationException("La tabla TA_TIPOVEHICULO no existe en la base activa.");

            var hasActivo = await ColumnExistsAsync(cn, "TA_TIPOVEHICULO", "ACTIVO", token);
            var isNew = !await ExistsByCodeAsync(cn, "TA_TIPOVEHICULO", "CODIGO", request.Codigo, token);
            var sql = isNew
                ? hasActivo
                    ? """
                        INSERT INTO dbo.TA_TIPOVEHICULO (CODIGO, DESCRIPCION, ACTIVO)
                        VALUES (@Codigo, @Descripcion, @Activo);
                        """
                    : """
                        INSERT INTO dbo.TA_TIPOVEHICULO (CODIGO, DESCRIPCION)
                        VALUES (@Codigo, @Descripcion);
                        """
                : hasActivo
                    ? """
                        UPDATE dbo.TA_TIPOVEHICULO
                        SET DESCRIPCION = @Descripcion, ACTIVO = @Activo
                        WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                        """
                    : """
                        UPDATE dbo.TA_TIPOVEHICULO
                        SET DESCRIPCION = @Descripcion
                        WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                        """;

            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Codigo = request.Codigo.Trim().ToUpperInvariant(),
                Descripcion = request.Descripcion.Trim(),
                request.Activo
            }, cancellationToken: token));

            return request.Codigo.Trim().ToUpperInvariant();
        }, "No se pudo guardar el tipo de vehÃ­culo.", ct);

    public Task<string> GetNextCodigoTipoVehiculoAsync(CancellationToken ct = default)
        => GetNextCodigoFromTableAsync("TA_TIPOVEHICULO", "CODIGO", ct);

    public Task BajaTipoVehiculoAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "BajaTipoVehiculo", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se recibiÃ³ el tipo de vehÃ­culo a dar de baja.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TIPOVEHICULO", token))
                throw new InvalidOperationException("La tabla TA_TIPOVEHICULO no existe en la base activa.");

            var hasActivo = await ColumnExistsAsync(cn, "TA_TIPOVEHICULO", "ACTIVO", token);
            var sql = hasActivo
                ? """
                    UPDATE dbo.TA_TIPOVEHICULO
                    SET ACTIVO = 0
                    WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                    """
                : """
                    DELETE FROM dbo.TA_TIPOVEHICULO
                    WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                    """;

            var affected = await cn.ExecuteAsync(new CommandDefinition(sql, new { Codigo = codigo.Trim().ToUpperInvariant() }, cancellationToken: token));
            if (affected == 0)
                throw new InvalidOperationException("El tipo de vehÃ­culo seleccionado ya no existe en la base activa.");
        }, "No se pudo dar de baja el tipo de vehÃ­culo.", ct);

    public Task<bool> TipoVehiculoTieneActivoAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "TipoVehiculoTieneActivo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await ColumnExistsAsync(cn, "TA_TIPOVEHICULO", "ACTIVO", token);
        }, "No se pudo verificar la estructura del tipo de vehÃ­culo.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchClientes", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            var searchLike = SearchTextHelper.LikeContains(search);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string fallbackSql = """
                SELECT TOP (50)
                    LTRIM(RTRIM(ISNULL(cli.CODIGO, ''))) AS Codigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS Titulo,
                    LTRIM(RTRIM(ISNULL(cli.IdListaRMTRF, ''))) AS Lista,
                    CAST('' AS nvarchar(100)) AS Subtitulo
                FROM dbo.Vt_Clientes cli
                WHERE LTRIM(RTRIM(ISNULL(cli.CODIGO, ''))) <> ''
                  AND (
                        @Search = ''
                        OR cli.CODIGO LIKE @Search
                        OR cli.RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @Search
                        OR ISNULL(cli.IdListaRMTRF, '') LIKE @Search
                      )
                ORDER BY cli.RAZON_SOCIAL, cli.CODIGO;
                """;

            try
            {
                var hasTarifaTable = await TableExistsAsync(cn, "TA_TARIFA", token);
                var sql = hasTarifaTable
                    ? """
                        SELECT TOP (50)
                            LTRIM(RTRIM(ISNULL(cli.CODIGO, ''))) AS Codigo,
                            ISNULL(cli.RAZON_SOCIAL, '') AS Titulo,
                            LTRIM(RTRIM(ISNULL(cli.IdListaRMTRF, ''))) AS Lista,
                            CASE
                                WHEN ISNULL(t.Nombre, '') = '' THEN ''
                                ELSE LTRIM(RTRIM(t.Nombre))
                            END AS Subtitulo
                        FROM dbo.Vt_Clientes cli
                        OUTER APPLY (
                            SELECT TOP (1)
                                t.Nombre
                            FROM dbo.TA_TARIFA t
                            WHERE UPPER(LTRIM(RTRIM(ISNULL(t.IdLista, '')))) = UPPER(LTRIM(RTRIM(ISNULL(cli.IdListaRMTRF, ''))))
                            ORDER BY t.Nombre, t.IdLista
                        ) t
                        WHERE LTRIM(RTRIM(ISNULL(cli.CODIGO, ''))) <> ''
                          AND (
                                @Search = ''
                                OR cli.CODIGO LIKE @Search
                                OR cli.RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @Search
                                OR ISNULL(cli.IdListaRMTRF, '') LIKE @Search
                                OR ISNULL(t.Nombre, '') COLLATE Latin1_General_CI_AI LIKE @Search
                              )
                        ORDER BY cli.RAZON_SOCIAL, cli.CODIGO;
                        """
                    : fallbackSql;

                var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = searchLike }, cancellationToken: token))).ToList();
                return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812 or 207 or 4104)
            {
                logger.LogWarning(ex, "SearchClientesAsync: fallback sin TA_TARIFA. Se devuelve solo Vt_Clientes.");
                var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(fallbackSql, new { Search = searchLike }, cancellationToken: token))).ToList();
                return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
            }
        }, "No se pudieron buscar clientes.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchChoferLookupAsync(string texto, CancellationToken ct = default)
        => SearchChoferLookupAsync(texto, true, true, ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchChoferLookupAsync(string texto, bool incluirChoferes, bool incluirFleteros, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchChoferLookup", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            var searchLike = SearchTextHelper.LikeContains(search);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_CHOFERES";
            var hasEsFletero = await ColumnExistsAsync(cn, table, "ES_FLETERO", token);
            var filterClause = incluirChoferes && incluirFleteros
                ? string.Empty
                : incluirFleteros
                    ? hasEsFletero ? "AND ISNULL(ES_FLETERO, 0) = 1" : "AND 1 = 0"
                    : incluirChoferes
                        ? hasEsFletero ? "AND ISNULL(ES_FLETERO, 0) = 0" : string.Empty
                        : "AND 1 = 0";
            var sql = $"""
                SELECT TOP (50)
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(NOMBRES, ''))) AS Titulo,
                    '' AS Subtitulo
                FROM dbo.{table}
                WHERE (
                        @Search = ''
                        OR CODIGO LIKE @Search
                        OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @Search
                      )
                  {filterClause}
                ORDER BY NOMBRES, CODIGO;
                """;

            var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = searchLike }, cancellationToken: token))).ToList();
            return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
        }, "No se pudieron buscar choferes.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchDestinosLookupAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchDestinosLookup", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            var searchLike = SearchTextHelper.LikeContains(search);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_DESTINOS";
            var codeColumn = "CODIGO";
            var sql = $"""
                SELECT TOP (50)
                    LTRIM(RTRIM(ISNULL({codeColumn}, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(Descripcion, ''))) AS Titulo,
                    '' AS Subtitulo
                FROM dbo.{table}
                WHERE (
                        @Search = ''
                        OR {codeColumn} LIKE @Search
                        OR Descripcion COLLATE Latin1_General_CI_AI LIKE @Search
                      )
                ORDER BY Descripcion, {codeColumn};
                """;

            var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = searchLike }, cancellationToken: token))).ToList();
            return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
        }, "No se pudieron buscar destinos.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchTipoVehiculosLookupAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchTipoVehiculosLookup", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            var searchLike = SearchTextHelper.LikeContains(search);

            const string sql = """
                SELECT TOP (50)
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) AS Titulo,
                    '' AS Subtitulo
                FROM dbo.TA_TIPOVEHICULO
                WHERE (
                        @Search = ''
                        OR CODIGO LIKE @Search
                        OR DESCRIPCION COLLATE Latin1_General_CI_AI LIKE @Search
                      )
                ORDER BY DESCRIPCION, CODIGO;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = searchLike }, cancellationToken: token))).ToList();
            return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
        }, "No se pudieron buscar tipos de vehÃ­culo.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchTarifasLookupAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchTarifasLookup", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            if (search.Length < 1)
                return (IReadOnlyList<CargaViajeLookupOptionDto>)Array.Empty<CargaViajeLookupOptionDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                return Array.Empty<CargaViajeLookupOptionDto>();

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");

            var sql = $"""
                SELECT TOP (12)
                    LTRIM(RTRIM(ISNULL(t.{idListaColumn}, ''))) AS Codigo,
                    ISNULL(t.Nombre, '') AS Titulo,
                    CASE
                        WHEN ISNULL(cli.RAZON_SOCIAL, '') = ''
                         AND ISNULL(ch.NOMBRES, '') = ''
                         AND ISNULL(d.DESCRIPCION, '') = ''
                         AND ISNULL(tv.DESCRIPCION, '') = '' THEN ''
                        ELSE CONCAT(
                            CASE WHEN ISNULL(cli.RAZON_SOCIAL, '') = '' THEN '' ELSE LTRIM(RTRIM(cli.RAZON_SOCIAL)) END,
                            CASE WHEN ISNULL(ch.NOMBRES, '') = '' THEN '' ELSE CONCAT(' | ', LTRIM(RTRIM(ch.NOMBRES))) END,
                            CASE WHEN ISNULL(d.DESCRIPCION, '') = '' THEN '' ELSE CONCAT(' | ', LTRIM(RTRIM(d.DESCRIPCION))) END,
                            CASE WHEN ISNULL(tv.DESCRIPCION, '') = '' THEN '' ELSE CONCAT(' | ', LTRIM(RTRIM(tv.DESCRIPCION))) END
                        )
                    END AS Subtitulo
                FROM dbo.TA_TARIFA t
                LEFT JOIN dbo.Vt_Clientes cli ON UPPER(LTRIM(RTRIM(ISNULL(cli.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(t.{clienteColumn}, ''))))
                LEFT JOIN dbo.TA_CHOFERES ch ON UPPER(LTRIM(RTRIM(ISNULL(ch.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(t.{choferColumn}, ''))))
                LEFT JOIN dbo.TA_DESTINOS d ON UPPER(LTRIM(RTRIM(ISNULL(d.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(t.{destinoColumn}, ''))))
                LEFT JOIN dbo.TA_TIPOVEHICULO tv ON UPPER(LTRIM(RTRIM(ISNULL(tv.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(t.{tipoVehiculoColumn}, ''))))
                WHERE (
                        t.{idListaColumn} LIKE @Search
                        OR t.Nombre COLLATE Latin1_General_CI_AI LIKE @Search
                        OR ISNULL(cli.RAZON_SOCIAL, '') COLLATE Latin1_General_CI_AI LIKE @Search
                        OR ISNULL(ch.NOMBRES, '') COLLATE Latin1_General_CI_AI LIKE @Search
                        OR ISNULL(d.DESCRIPCION, '') COLLATE Latin1_General_CI_AI LIKE @Search
                        OR ISNULL(tv.DESCRIPCION, '') COLLATE Latin1_General_CI_AI LIKE @Search
                    )
                ORDER BY t.Nombre, t.{idListaColumn};
                """;

            var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = SearchTextHelper.LikeContains(search) }, cancellationToken: token))).ToList();
            return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
        }, "No se pudieron buscar tarifas.", ct);

    public Task<IReadOnlyList<CargaViajeTarifaClienteResumenDto>> GetTarifasPorClienteAsync(string clienteCodigo, string? texto = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTarifasPorCliente", async token =>
        {
            var cliente = TrimUpper(clienteCodigo);
            if (string.IsNullOrWhiteSpace(cliente))
                return (IReadOnlyList<CargaViajeTarifaClienteResumenDto>)Array.Empty<CargaViajeTarifaClienteResumenDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                return Array.Empty<CargaViajeTarifaClienteResumenDto>();

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idColumn = columns.Contains("id") ? "ID"
                : columns.Contains("idtarifa") ? "IDTARIFA"
                : columns.Contains("id_tarifa") ? "ID_TARIFA"
                : null;
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            var searchLike = SearchTextHelper.LikeContains(texto);

            var sql = $"""
                SELECT
                    {(string.IsNullOrWhiteSpace(idColumn) ? "CAST(0 AS int)" : $"ISNULL(t.{idColumn}, 0)") } AS IdTarifa,
                    LTRIM(RTRIM(ISNULL(t.{clienteColumn}, ''))) AS ClienteCodigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
                    LTRIM(RTRIM(ISNULL(t.{destinoColumn}, ''))) AS DestinoCodigo,
                    ISNULL(d.DESCRIPCION, '') AS DestinoNombre,
                    LTRIM(RTRIM(ISNULL(t.{tipoVehiculoColumn}, ''))) AS TipoVehiculoCodigo,
                    ISNULL(tv.DESCRIPCION, '') AS TipoVehiculoNombre,
                    LTRIM(RTRIM(ISNULL(t.{idListaColumn}, ''))) AS ListaCodigo,
                    ISNULL(t.Nombre, '') AS ListaNombre,
                    LTRIM(RTRIM(ISNULL(t.{choferColumn}, ''))) AS FleteroCodigoSugerido,
                    ISNULL(ch.NOMBRES, '') AS FleteroNombreSugerido,
                    ISNULL(t.Importe, 0) AS ImporteBase,
                    ISNULL(t.TarifaFletero, 0) AS TarifaFletero,
                    ISNULL(t.Activo, 1) AS Activo
                FROM dbo.TA_TARIFA t
                LEFT JOIN dbo.Vt_Clientes cli ON UPPER(LTRIM(RTRIM(ISNULL(cli.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(t.{clienteColumn}, ''))))
                LEFT JOIN dbo.TA_CHOFERES ch ON UPPER(LTRIM(RTRIM(ISNULL(ch.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(t.{choferColumn}, ''))))
                LEFT JOIN dbo.TA_DESTINOS d ON UPPER(LTRIM(RTRIM(ISNULL(d.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(t.{destinoColumn}, ''))))
                LEFT JOIN dbo.TA_TIPOVEHICULO tv ON UPPER(LTRIM(RTRIM(ISNULL(tv.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(t.{tipoVehiculoColumn}, ''))))
                WHERE UPPER(LTRIM(RTRIM(ISNULL(t.{clienteColumn}, '')))) = @Cliente
                  AND (
                        @SearchLike = ''
                        OR LTRIM(RTRIM(ISNULL(t.{idListaColumn}, ''))) LIKE @SearchLike
                        OR t.Nombre COLLATE Latin1_General_CI_AI LIKE @SearchLike
                        OR ISNULL(d.DESCRIPCION, '') COLLATE Latin1_General_CI_AI LIKE @SearchLike
                        OR ISNULL(tv.DESCRIPCION, '') COLLATE Latin1_General_CI_AI LIKE @SearchLike
                        OR ISNULL(ch.NOMBRES, '') COLLATE Latin1_General_CI_AI LIKE @SearchLike
                    )
                ORDER BY ISNULL(t.Nombre, ''), LTRIM(RTRIM(ISNULL(t.{idListaColumn}, '')));
                """;

            var rawRows = (await cn.QueryAsync<TarifaClienteLookupRow>(new CommandDefinition(sql, new
            {
                Cliente = cliente,
                SearchLike = searchLike
            }, cancellationToken: token))).ToList();

            if (rawRows.Count == 0)
                return Array.Empty<CargaViajeTarifaClienteResumenDto>();

            var config = BuildConfiguracion(await LoadConfiguracionAsync(cn, token));
            var result = new List<CargaViajeTarifaClienteResumenDto>(rawRows.Count);
            foreach (var row in rawRows)
            {
                var clienteNombre = string.IsNullOrWhiteSpace(row.ClienteNombre) ? row.ClienteCodigo : row.ClienteNombre.Trim();
                var destinoNombre = string.IsNullOrWhiteSpace(row.DestinoNombre) ? row.DestinoCodigo : row.DestinoNombre.Trim();
                var tipoVehiculoNombre = string.IsNullOrWhiteSpace(row.TipoVehiculoNombre) ? row.TipoVehiculoCodigo : row.TipoVehiculoNombre.Trim();
                var fleteroCodigo = row.FleteroCodigoSugerido.Trim();
                var fleteroNombre = string.IsNullOrWhiteSpace(row.FleteroNombreSugerido) ? fleteroCodigo : row.FleteroNombreSugerido.Trim();
                var importeCliente = row.TarifaFletero
                    ? await ResolveTarifaImporteAsync(cn, clienteColumn, row.ClienteCodigo, destinoColumn, row.DestinoCodigo, tipoVehiculoColumn, row.TipoVehiculoCodigo, 0, config.ChoferGeneral, token)
                    : row.ImporteBase;
                var importeFletero = row.TarifaFletero
                    ? row.ImporteBase
                    : string.IsNullOrWhiteSpace(fleteroCodigo)
                        ? 0m
                        : await ResolveTarifaImporteAsync(cn, choferColumn, fleteroCodigo, destinoColumn, row.DestinoCodigo, tipoVehiculoColumn, row.TipoVehiculoCodigo, 1, config.ChoferGeneral, token);

                result.Add(new CargaViajeTarifaClienteResumenDto
                {
                    IdTarifa = row.IdTarifa,
                    ClienteCodigo = row.ClienteCodigo,
                    ClienteNombre = clienteNombre,
                    DestinoCodigo = row.DestinoCodigo,
                    DestinoNombre = destinoNombre,
                    TipoVehiculoCodigo = row.TipoVehiculoCodigo,
                    TipoVehiculoNombre = tipoVehiculoNombre,
                    ListaCodigo = row.ListaCodigo,
                    ListaNombre = row.ListaNombre,
                    ImporteCliente = importeCliente,
                    ImporteFletero = importeFletero,
                    FleteroCodigoSugerido = fleteroCodigo,
                    FleteroNombreSugerido = fleteroNombre,
                    Activo = row.Activo,
                    TarifaFletero = row.TarifaFletero
                });
            }

            return (IReadOnlyList<CargaViajeTarifaClienteResumenDto>)result;
        }, "No se pudieron cargar las tarifas del cliente.", ct);

    public Task<CargaViajeLookupOptionDto> CreateDestinoRapidoAsync(string descripcion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CreateDestinoRapido", async token =>
        {
            var normalized = SearchTextHelper.Normalize(descripcion);
            var validation = new ValidationResult();
            if (string.IsNullOrWhiteSpace(normalized))
                validation.Add("Descripcion", "Debe ingresar un destino.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_DESTINOS";
            if (!await TableExistsAsync(cn, table, token))
                throw new InvalidOperationException("La tabla TA_DESTINOS no existe en la base activa.");

            var maxLength = await GetColumnCharacterLengthAsync(cn, table, "DESCRIPCION", token, 100);
            if (maxLength > 0 && normalized.Length > maxLength)
                validation.Add("Descripcion", $"El destino no puede superar {maxLength} caracteres.");

            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            var existing = await FindLookupByDescriptionAsync(cn, table, "CODIGO", "DESCRIPCION", normalized, token);
            if (existing is not null)
                return existing;

            var codigo = await GetNextCodigoDisponibleAsync(cn, table, "CODIGO", token);
            var hasActivo = await ColumnExistsAsync(cn, table, "ACTIVO", token);
            var sql = BuildCreateMaestraSql(table, "CODIGO", "DESCRIPCION", hasActivo);
            var affected = await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Codigo = codigo,
                Descripcion = normalized,
                Activo = true
            }, cancellationToken: token));
            if (affected <= 0)
                throw new InvalidOperationException("No se pudo crear el destino.");

            logger.LogInformation("CreateDestinoRapido OK Codigo={Codigo} Descripcion={Descripcion}", codigo, normalized);
            return new CargaViajeLookupOptionDto { Codigo = codigo, Titulo = normalized };
        }, "No se pudo crear el destino.", ct);

    public Task<CargaViajeLookupOptionDto> CreateTipoVehiculoRapidoAsync(string descripcion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CreateTipoVehiculoRapido", async token =>
        {
            var normalized = SearchTextHelper.Normalize(descripcion);
            var validation = new ValidationResult();
            if (string.IsNullOrWhiteSpace(normalized))
                validation.Add("Descripcion", "Debe ingresar un tipo de vehículo.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_TIPOVEHICULO";
            if (!await TableExistsAsync(cn, table, token))
                throw new InvalidOperationException("La tabla TA_TIPOVEHICULO no existe en la base activa.");

            var maxLength = await GetColumnCharacterLengthAsync(cn, table, "DESCRIPCION", token, 100);
            if (maxLength > 0 && normalized.Length > maxLength)
                validation.Add("Descripcion", $"El tipo de vehículo no puede superar {maxLength} caracteres.");

            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            var existing = await FindLookupByDescriptionAsync(cn, table, "CODIGO", "DESCRIPCION", normalized, token);
            if (existing is not null)
                return existing;

            var codigo = await GetNextCodigoDisponibleAsync(cn, table, "CODIGO", token);
            var hasActivo = await ColumnExistsAsync(cn, table, "ACTIVO", token);
            var sql = BuildCreateMaestraSql(table, "CODIGO", "DESCRIPCION", hasActivo);
            var affected = await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Codigo = codigo,
                Descripcion = normalized,
                Activo = true
            }, cancellationToken: token));
            if (affected <= 0)
                throw new InvalidOperationException("No se pudo crear el tipo de vehículo.");

            logger.LogInformation("CreateTipoVehiculoRapido OK Codigo={Codigo} Descripcion={Descripcion}", codigo, normalized);
            return new CargaViajeLookupOptionDto { Codigo = codigo, Titulo = normalized };
        }, "No se pudo crear el tipo de vehículo.", ct);

    public Task<CargaViajeLookupOptionDto> CreateChoferRapidoAsync(string nombre, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CreateChoferRapido", async token =>
        {
            var normalized = SearchTextHelper.Normalize(nombre);
            var validation = new ValidationResult();
            if (string.IsNullOrWhiteSpace(normalized))
                validation.Add("Nombre", "Debe ingresar un chofer.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string table = "TA_CHOFERES";
            if (!await TableExistsAsync(cn, table, token))
                throw new InvalidOperationException("La tabla TA_CHOFERES no existe en la base activa.");

            var maxLength = await GetColumnCharacterLengthAsync(cn, table, "NOMBRES", token, 100);
            if (maxLength > 0 && normalized.Length > maxLength)
                validation.Add("Nombre", $"El chofer no puede superar {maxLength} caracteres.");

            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            var existing = await FindLookupByDescriptionAsync(cn, table, "CODIGO", "NOMBRES", normalized, token);
            if (existing is not null)
                return existing;

            var codigo = await GetNextCodigoDisponibleAsync(cn, table, "CODIGO", token);
            var hasActivo = await ColumnExistsAsync(cn, table, "ACTIVO", token);
            var hasDisponible = await ColumnExistsAsync(cn, table, "DISPONIBLE", token);
            var hasEsFletero = await ColumnExistsAsync(cn, table, "ES_FLETERO", token);
            var sql = BuildCreateChoferSql(table, hasActivo, hasDisponible, hasEsFletero);
            var affected = await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Codigo = codigo,
                Nombre = normalized,
                Activo = true,
                Disponible = true,
                EsFletero = false
            }, cancellationToken: token));
            if (affected <= 0)
                throw new InvalidOperationException("No se pudo crear el chofer.");

            logger.LogInformation("CreateChoferRapido OK Codigo={Codigo} Nombre={Nombre}", codigo, normalized);
            return new CargaViajeLookupOptionDto { Codigo = codigo, Titulo = normalized };
        }, "No se pudo crear el chofer.", ct);

    public Task<CargaViajeTarifaGridItemDto?> GetTarifaClienteAsync(string cliente, string destino, string tipoVehiculo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTarifaCliente", async token =>
        {
            if (string.IsNullOrWhiteSpace(destino) || string.IsNullOrWhiteSpace(tipoVehiculo))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                return null;

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var result = await ResolveTarifaDetalleAsync(
                cn,
                columns,
                principalColumnCandidates: ["IDCLIENTE", "CLIENTE"],
                principalValue: cliente,
                destinoColumnCandidates: ["IDDESTINO", "DESTINO"],
                destinoValue: destino,
                tipoVehiculoColumnCandidates: ["IDTIPOVEHICULO", "TIPOVEHICULO"],
                tipoVehiculoValue: tipoVehiculo,
                tarifaFletero: 0,
                token);

            return result;
        }, "No se pudo calcular la tarifa del cliente.", ct);

    public Task<decimal> GetTarifaFleteroAsync(string chofer, string destino, string tipoVehiculo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTarifaFletero", async token =>
        {
            if (string.IsNullOrWhiteSpace(destino) || string.IsNullOrWhiteSpace(tipoVehiculo))
                return 0m;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                return 0m;

            var config = BuildConfiguracion(await LoadConfiguracionAsync(cn, token));
            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            return await ResolveTarifaImporteAsync(cn, choferColumn, chofer, destinoColumn, destino, tipoVehiculoColumn, tipoVehiculo, 1, config.ChoferGeneral, token);
        }, "No se pudo calcular la tarifa del fletero.", ct);

    public Task<CargaViajeTarifaGridItemDto?> ClonarTarifaParaViajeAsync(string idListaBase, CargaViajeSaveRequest viaje, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ClonarTarifaParaViaje", async token =>
        {
            var baseIdLista = (idListaBase ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseIdLista))
                throw new InvalidOperationException("Debe seleccionar una tarifa base.");

            ArgumentNullException.ThrowIfNull(viaje);

            var clienteCodigo = (viaje.Cliente ?? string.Empty).Trim();
            var choferCodigo = (viaje.Chofer ?? string.Empty).Trim();
            var destinoCodigo = (viaje.Destino ?? string.Empty).Trim();
            var tipoVehiculoCodigo = (viaje.TipoVehiculo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(clienteCodigo) || string.IsNullOrWhiteSpace(choferCodigo) || string.IsNullOrWhiteSpace(destinoCodigo) || string.IsNullOrWhiteSpace(tipoVehiculoCodigo))
                throw new InvalidOperationException("Debe completar cliente, chofer, destino y tipo vehículo antes de duplicar la tarifa.");

            var clienteNombre = ExtractDescripcionFromDisplay(viaje.ClienteDisplay);
            if (string.IsNullOrWhiteSpace(clienteNombre))
            {
                await using var lookupConnection = new SqlConnection(ConnectionString);
                await lookupConnection.OpenAsync(token);
                clienteNombre = await lookupConnection.ExecuteScalarAsync<string?>(new CommandDefinition("""
                    SELECT TOP (1) LTRIM(RTRIM(ISNULL(RAZON_SOCIAL, '')))
                    FROM dbo.Vt_Clientes
                    WHERE UPPER(LTRIM(RTRIM(ISNULL(CODIGO, '')))) = @Codigo;
                    """, new { Codigo = clienteCodigo.ToUpperInvariant() }, cancellationToken: token)) ?? string.Empty;
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                throw new InvalidOperationException("La tabla TA_TARIFA no existe en la base activa.");

            var source = await GetTarifaByIdAsync(baseIdLista, token);
            if (source is null)
                throw new InvalidOperationException("No se encontró la tarifa base seleccionada.");

            var derivedIdLista = BuildListaDesdeCliente(clienteCodigo);
            if (string.IsNullOrWhiteSpace(derivedIdLista))
                throw new InvalidOperationException("No se pudo construir la lista destino del viaje.");

            var normalizedNombre = string.IsNullOrWhiteSpace(clienteNombre) ? clienteCodigo : clienteNombre.Trim();
            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idColumn = columns.Contains("id") ? "ID"
                : columns.Contains("idtarifa") ? "IDTARIFA"
                : columns.Contains("id_tarifa") ? "ID_TARIFA"
                : null;
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            var hasImporte = columns.Contains("importe");
            var hasNombre = columns.Contains("nombre");
            var hasTarifaFletero = columns.Contains("tarifafletero");
            var hasActivo = columns.Contains("activo");
            var hasPorc0 = columns.Contains("porcentaje_adic");
            var hasPorc1 = columns.Contains("porcentaje_adic1");
            var hasPorc2 = columns.Contains("porcentaje_adic2");
            var hasPorc3 = columns.Contains("porcentaje_adic3");
            var hasPorc4 = columns.Contains("porcentaje_adic4");
            var hasAdicFijo1Desc = columns.Contains("adicionalfijo1descripcion");
            var hasAdicFijo1Importe = columns.Contains("adicionalfijo1importe");
            var hasAdicFijo2Desc = columns.Contains("adicionalfijo2descripcion");
            var hasAdicFijo2Importe = columns.Contains("adicionalfijo2importe");
            var hasAdicFijo3Desc = columns.Contains("adicionalfijo3descripcion");
            var hasAdicFijo3Importe = columns.Contains("adicionalfijo3importe");

            var parameters = new DynamicParameters();
            parameters.Add("@IdLista", derivedIdLista);
            parameters.Add("@Nombre", normalizedNombre);
            parameters.Add("@Importe", source.Importe);
            parameters.Add("@Cliente", clienteCodigo);
            parameters.Add("@Chofer", choferCodigo);
            parameters.Add("@Destino", destinoCodigo);
            parameters.Add("@TipoVehiculo", tipoVehiculoCodigo);
            parameters.Add("@TarifaFletero", source.TarifaFletero);
            parameters.Add("@PorcentajeAdic", source.PorcentajeAdic);
            parameters.Add("@PorcentajeAdic1", source.PorcentajeAdic1);
            parameters.Add("@PorcentajeAdic2", source.PorcentajeAdic2);
            parameters.Add("@PorcentajeAdic3", source.PorcentajeAdic3);
            parameters.Add("@PorcentajeAdic4", source.PorcentajeAdic4);
            parameters.Add("@AdicionalFijo1Descripcion", (source.AdicionalFijo1Descripcion ?? string.Empty).Trim());
            parameters.Add("@AdicionalFijo1Importe", source.AdicionalFijo1Importe);
            parameters.Add("@AdicionalFijo2Descripcion", (source.AdicionalFijo2Descripcion ?? string.Empty).Trim());
            parameters.Add("@AdicionalFijo2Importe", source.AdicionalFijo2Importe);
            parameters.Add("@AdicionalFijo3Descripcion", (source.AdicionalFijo3Descripcion ?? string.Empty).Trim());
            parameters.Add("@AdicionalFijo3Importe", source.AdicionalFijo3Importe);
            parameters.Add("@Activo", true);
            var existing = await GetTarifaByIdAsync(derivedIdLista, token);
            var insertColumns = new List<string>();
            var insertValues = new List<string>();
            AddColumnPair(insertColumns, insertValues, idListaColumn, "@IdLista");
            AddColumnPair(insertColumns, insertValues, hasNombre ? "Nombre" : null, "@Nombre");
            AddColumnPair(insertColumns, insertValues, hasImporte ? "Importe" : null, "@Importe");
            AddColumnPair(insertColumns, insertValues, clienteColumn, "@Cliente");
            AddColumnPair(insertColumns, insertValues, choferColumn, "@Chofer");
            AddColumnPair(insertColumns, insertValues, destinoColumn, "@Destino");
            AddColumnPair(insertColumns, insertValues, tipoVehiculoColumn, "@TipoVehiculo");
            AddColumnPair(insertColumns, insertValues, hasTarifaFletero ? "TarifaFletero" : null, "@TarifaFletero");
            AddColumnPair(insertColumns, insertValues, hasPorc0 ? "PorcentajeAdic" : null, "@PorcentajeAdic");
            AddColumnPair(insertColumns, insertValues, hasPorc1 ? "PorcentajeAdic1" : null, "@PorcentajeAdic1");
            AddColumnPair(insertColumns, insertValues, hasPorc2 ? "PorcentajeAdic2" : null, "@PorcentajeAdic2");
            AddColumnPair(insertColumns, insertValues, hasPorc3 ? "PorcentajeAdic3" : null, "@PorcentajeAdic3");
            AddColumnPair(insertColumns, insertValues, hasPorc4 ? "PorcentajeAdic4" : null, "@PorcentajeAdic4");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo1Desc ? "AdicionalFijo1Descripcion" : null, "@AdicionalFijo1Descripcion");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo1Importe ? "AdicionalFijo1Importe" : null, "@AdicionalFijo1Importe");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo2Desc ? "AdicionalFijo2Descripcion" : null, "@AdicionalFijo2Descripcion");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo2Importe ? "AdicionalFijo2Importe" : null, "@AdicionalFijo2Importe");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo3Desc ? "AdicionalFijo3Descripcion" : null, "@AdicionalFijo3Descripcion");
            AddColumnPair(insertColumns, insertValues, hasAdicFijo3Importe ? "AdicionalFijo3Importe" : null, "@AdicionalFijo3Importe");
            AddColumnPair(insertColumns, insertValues, hasActivo ? "Activo" : null, "@Activo");

            var updateParts = new List<string>();
            AddUpdatePart(updateParts, idListaColumn, "@IdLista");
            AddUpdatePart(updateParts, hasNombre ? "Nombre" : null, "@Nombre");
            AddUpdatePart(updateParts, hasImporte ? "Importe" : null, "@Importe");
            AddUpdatePart(updateParts, clienteColumn, "@Cliente");
            AddUpdatePart(updateParts, choferColumn, "@Chofer");
            AddUpdatePart(updateParts, destinoColumn, "@Destino");
            AddUpdatePart(updateParts, tipoVehiculoColumn, "@TipoVehiculo");
            AddUpdatePart(updateParts, hasTarifaFletero ? "TarifaFletero" : null, "@TarifaFletero");
            AddUpdatePart(updateParts, hasPorc0 ? "PorcentajeAdic" : null, "@PorcentajeAdic");
            AddUpdatePart(updateParts, hasPorc1 ? "PorcentajeAdic1" : null, "@PorcentajeAdic1");
            AddUpdatePart(updateParts, hasPorc2 ? "PorcentajeAdic2" : null, "@PorcentajeAdic2");
            AddUpdatePart(updateParts, hasPorc3 ? "PorcentajeAdic3" : null, "@PorcentajeAdic3");
            AddUpdatePart(updateParts, hasPorc4 ? "PorcentajeAdic4" : null, "@PorcentajeAdic4");
            AddUpdatePart(updateParts, hasAdicFijo1Desc ? "AdicionalFijo1Descripcion" : null, "@AdicionalFijo1Descripcion");
            AddUpdatePart(updateParts, hasAdicFijo1Importe ? "AdicionalFijo1Importe" : null, "@AdicionalFijo1Importe");
            AddUpdatePart(updateParts, hasAdicFijo2Desc ? "AdicionalFijo2Descripcion" : null, "@AdicionalFijo2Descripcion");
            AddUpdatePart(updateParts, hasAdicFijo2Importe ? "AdicionalFijo2Importe" : null, "@AdicionalFijo2Importe");
            AddUpdatePart(updateParts, hasAdicFijo3Desc ? "AdicionalFijo3Descripcion" : null, "@AdicionalFijo3Descripcion");
            AddUpdatePart(updateParts, hasAdicFijo3Importe ? "AdicionalFijo3Importe" : null, "@AdicionalFijo3Importe");
            AddUpdatePart(updateParts, hasActivo ? "Activo" : null, "@Activo");

            var sql = existing is null
                ? $"""
                    INSERT INTO dbo.TA_TARIFA
                    ({string.Join(", ", insertColumns)})
                    VALUES
                    ({string.Join(", ", insertValues)});
                    """
                : $"""
                    UPDATE dbo.TA_TARIFA
                    SET {string.Join(", ", updateParts)}
                    WHERE UPPER(LTRIM(RTRIM(ISNULL({idListaColumn}, '')))) = @IdLista;
                    """;

            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                await cn.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: (SqlTransaction)tx, cancellationToken: token));
                await tx.CommitAsync(token);
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(token);
                }
                catch (Exception rollbackEx)
                {
                    logger.LogWarning(rollbackEx, "ClonarTarifaParaViaje rollback fallÃ³");
                }

                throw;
            }

            var cloned = await GetTarifaByIdAsync(derivedIdLista, token);
            if (cloned is null)
                throw new InvalidOperationException("No se pudo recuperar la tarifa duplicada.");

            logger.LogInformation(
                "ClonarTarifaParaViaje OK Base={Base} NuevaLista={NuevaLista} Cliente={Cliente} Chofer={Chofer} Destino={Destino} TipoVehiculo={TipoVehiculo}",
                baseIdLista,
                derivedIdLista,
                clienteCodigo,
                choferCodigo,
                destinoCodigo,
                tipoVehiculoCodigo);
            return (CargaViajeTarifaGridItemDto?)cloned;
        }, "No se pudo duplicar la tarifa para el viaje.", ct);

    public Task<IReadOnlyList<CargaViajeReporteLiquidacionRowDto>> SearchLiquidacionChoferesAsync(CargaViajesReporteLiquidacionFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchLiquidacionChoferes", async token =>
        {
            filters ??= new CargaViajesReporteLiquidacionFilters();
            var fechaDesde = filters.FechaDesde?.Date ?? DateTime.Today;
            var fechaHasta = filters.FechaHasta?.Date ?? fechaDesde;
            if (fechaHasta < fechaDesde)
                (fechaDesde, fechaHasta) = (fechaHasta, fechaDesde);

            logger.LogInformation(
                "SearchLiquidacionChoferes start desde={Desde:yyyy-MM-dd} hasta={Hasta:yyyy-MM-dd} choferes={Choferes} fleteros={Fleteros} chofer={Chofer}",
                fechaDesde,
                fechaHasta,
                filters.IncluirChoferes,
                filters.IncluirFleteros,
                filters.ChoferCodigo);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string viajeTable = "MV_VIAJES_CARGA";
            var choferJoin = "LEFT JOIN dbo.TA_CHOFERES ch ON UPPER(LTRIM(RTRIM(ISNULL(ch.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDCHOFER, ''))))";
            var clienteJoin = "LEFT JOIN dbo.Vt_Clientes cli ON UPPER(LTRIM(RTRIM(ISNULL(cli.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDCLIENTE, ''))))";
            var destinoJoin = "LEFT JOIN dbo.TA_DESTINOS d ON UPPER(LTRIM(RTRIM(ISNULL(d.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDDESTINO, ''))))";
            var vehiculoJoin = "LEFT JOIN dbo.TA_TIPOVEHICULO tv ON UPPER(LTRIM(RTRIM(ISNULL(tv.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDTIPOVEHICULO, ''))))";
            var hasEsFletero = await ColumnExistsAsync(cn, "TA_CHOFERES", "ES_FLETERO", token);
            var esFleteroExpr = hasEsFletero ? "CAST(ISNULL(ch.ES_FLETERO, 0) AS bit)" : "CAST(0 AS bit)";
            var reporteFilter = """
                AND (
                        (@IncluirChoferes = 1 AND ISNULL(ch.ES_FLETERO, 0) = 0)
                     OR (@IncluirFleteros = 1 AND ISNULL(ch.ES_FLETERO, 0) = 1)
                    )
                AND (@ChoferCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.IDCHOFER, '')))) = @ChoferCodigo)
                """;
            var sql = $"""
                SELECT
                    v.ID AS Id,
                    v.FECHA AS Fecha,
                    ISNULL(v.IDCOMPROBANTE, '') AS IdComprobante,
                    LTRIM(RTRIM(ISNULL(v.IDCHOFER, ''))) AS ChoferCodigo,
                    ISNULL(ch.NOMBRES, v.NOMBRE_CHOFER) AS ChoferNombre,
                    {esFleteroExpr} AS EsFletero,
                    LTRIM(RTRIM(ISNULL(v.IDCLIENTE, ''))) AS ClienteCodigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
                    LTRIM(RTRIM(ISNULL(v.IDDESTINO, ''))) AS DestinoCodigo,
                    ISNULL(d.DESCRIPCION, v.DESCRIPCIONDESTINO) AS DestinoDescripcion,
                    LTRIM(RTRIM(ISNULL(v.IDTIPOVEHICULO, ''))) AS TipoVehiculoCodigo,
                    ISNULL(tv.DESCRIPCION, '') AS TipoVehiculoDescripcion,
                    ISNULL(v.TOTAL_VIAJES, 1) AS CantidadViajes,
                    ISNULL(v.TOTAL_FLETE, 0) AS TotalFlete
                FROM dbo.{viajeTable} v
                {choferJoin}
                {clienteJoin}
                {destinoJoin}
                {vehiculoJoin}
                WHERE ISNULL(v.ANULADO, 0) = 0
                  AND v.FECHA >= @FechaDesde
                  AND v.FECHA < DATEADD(DAY, 1, @FechaHasta)
                  {reporteFilter}
                ORDER BY v.FECHA DESC, v.ID DESC;
                """;

            var rows = (await cn.QueryAsync<CargaViajeReporteLiquidacionRowDto>(new CommandDefinition(sql, new
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                IncluirChoferes = filters.IncluirChoferes ? 1 : 0,
                IncluirFleteros = filters.IncluirFleteros ? 1 : 0,
                ChoferCodigo = TrimUpper(filters.ChoferCodigo)
            }, cancellationToken: token, commandTimeout: 60))).ToList();

            logger.LogInformation("SearchLiquidacionChoferes end rows={Rows}", rows.Count);

            return (IReadOnlyList<CargaViajeReporteLiquidacionRowDto>)rows;
        }, "No se pudo generar la liquidación de choferes y fleteros.", ct);

    public Task<IReadOnlyList<CargaViajeLiquidacionRowDto>> SearchLiquidacionesFletesAsync(CargaViajesLiquidacionFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchLiquidacionesFletes", async token =>
        {
            filters ??= new CargaViajesLiquidacionFilters();
            var fechaDesde = filters.FechaDesde?.Date ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var fechaHasta = filters.FechaHasta?.Date ?? fechaDesde.AddMonths(1).AddDays(-1);
            if (fechaHasta < fechaDesde)
                (fechaDesde, fechaHasta) = (fechaHasta, fechaDesde);

            var incluirChoferes = filters.IncluirChoferes;
            var incluirFleteros = filters.IncluirFleteros;
            if (!incluirChoferes && !incluirFleteros)
                return Array.Empty<CargaViajeLiquidacionRowDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string viajeTable = "MV_VIAJES_CARGA";
            var hasEsFletero = await ColumnExistsAsync(cn, "TA_CHOFERES", "ES_FLETERO", token);
            var choferJoin = "LEFT JOIN dbo.TA_CHOFERES ch ON UPPER(LTRIM(RTRIM(ISNULL(ch.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDCHOFER, ''))))";
            var clienteJoin = "LEFT JOIN dbo.Vt_Clientes cli ON UPPER(LTRIM(RTRIM(ISNULL(cli.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDCLIENTE, ''))))";
            var destinoJoin = "LEFT JOIN dbo.TA_DESTINOS d ON UPPER(LTRIM(RTRIM(ISNULL(d.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDDESTINO, ''))))";
            var vehiculoJoin = "LEFT JOIN dbo.TA_TIPOVEHICULO tv ON UPPER(LTRIM(RTRIM(ISNULL(tv.CODIGO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(v.IDTIPOVEHICULO, ''))))";
            var esFleteroExpr = hasEsFletero ? "CAST(ISNULL(ch.ES_FLETERO, 0) AS bit)" : "CAST(0 AS bit)";
            var tipoFilterClause = hasEsFletero
                ? """
                  AND (
                        (@IncluirChoferes = 1 AND ISNULL(ch.ES_FLETERO, 0) = 0)
                     OR (@IncluirFleteros = 1 AND ISNULL(ch.ES_FLETERO, 0) = 1)
                  )
                  """
                : incluirFleteros && !incluirChoferes
                    ? "AND 1 = 0"
                    : string.Empty;

            var sql = $"""
                SELECT
                    v.ID AS Id,
                    v.FECHA AS Fecha,
                    ISNULL(v.IDCOMPROBANTE, '') AS Comprobante,
                    LTRIM(RTRIM(ISNULL(v.IDCHOFER, ''))) AS ChoferCodigo,
                    ISNULL(ch.NOMBRES, v.NOMBRE_CHOFER) AS ChoferNombre,
                    {esFleteroExpr} AS EsFletero,
                    LTRIM(RTRIM(ISNULL(v.IDCLIENTE, ''))) AS ClienteCodigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS ClienteNombre,
                    LTRIM(RTRIM(ISNULL(v.IDDESTINO, ''))) AS DestinoCodigo,
                    ISNULL(d.DESCRIPCION, v.DESCRIPCIONDESTINO) AS DestinoDescripcion,
                    LTRIM(RTRIM(ISNULL(v.IDTIPOVEHICULO, ''))) AS TipoVehiculoCodigo,
                    ISNULL(tv.DESCRIPCION, '') AS TipoVehiculoDescripcion,
                    ISNULL(v.TOTAL_VIAJES, 1) AS CantidadViajes,
                    ISNULL(v.TOTAL_FLETE, 0) AS TotalFlete,
                    CAST(ISNULL(v.FLETE_PAGADO, 0) AS bit) AS FletePagado,
                    v.FECHA_PAGO_FLETE AS FechaPagoFlete,
                    ISNULL(v.USUARIO_PAGO_FLETE, '') AS UsuarioPagoFlete,
                    ISNULL(v.OBSERVACION_PAGO_FLETE, '') AS ObservacionPagoFlete
                FROM dbo.{viajeTable} v
                {choferJoin}
                {clienteJoin}
                {destinoJoin}
                {vehiculoJoin}
                WHERE ISNULL(v.ANULADO, 0) = 0
                  AND v.FECHA >= @FechaDesde
                  AND v.FECHA < DATEADD(DAY, 1, @FechaHasta)
                  {tipoFilterClause}
                  AND (@ChoferCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.IDCHOFER, '')))) = @ChoferCodigo)
                  AND (
                        @EstadoPago = N'{CargaViajesLiquidacionEstadoPagoKeys.Todos.ToUpperInvariant()}'
                     OR (@EstadoPago = N'{CargaViajesLiquidacionEstadoPagoKeys.Pendientes.ToUpperInvariant()}' AND ISNULL(v.FLETE_PAGADO, 0) = 0)
                     OR (@EstadoPago = N'{CargaViajesLiquidacionEstadoPagoKeys.Pagados.ToUpperInvariant()}' AND ISNULL(v.FLETE_PAGADO, 0) = 1)
                  )
                ORDER BY ISNULL(ch.ES_FLETERO, 0) DESC, v.FECHA DESC, v.ID DESC;
                """;

            var rows = (await cn.QueryAsync<CargaViajeLiquidacionRowDto>(new CommandDefinition(sql, new
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                IncluirChoferes = incluirChoferes ? 1 : 0,
                IncluirFleteros = incluirFleteros ? 1 : 0,
                ChoferCodigo = TrimUpper(filters.ChoferCodigo),
                EstadoPago = NormalizeLiquidacionEstadoPago(filters.EstadoPago)
            }, cancellationToken: token, commandTimeout: 60))).ToList();

            return (IReadOnlyList<CargaViajeLiquidacionRowDto>)rows;
        }, "No se pudieron cargar las liquidaciones de fletes.", ct);

    public Task<int> MarcarFletesPagadosAsync(CargaViajesMarcarPagadoRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "MarcarFletesPagados", async token =>
        {
            if (request is null || request.Ids.Count == 0)
                return 0;

            var ids = request.Ids.Where(x => x > 0).Distinct().ToList();
            if (ids.Count == 0)
                return 0;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sql = """
                UPDATE dbo.MV_VIAJES_CARGA
                SET
                    FLETE_PAGADO = 1,
                    FECHA_PAGO_FLETE = @FechaPago,
                    USUARIO_PAGO_FLETE = @Usuario,
                    OBSERVACION_PAGO_FLETE = @Observacion,
                    FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID IN @Ids
                  AND ISNULL(FLETE_PAGADO, 0) = 0;
                """;

            var rows = await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Ids = ids,
                FechaPago = request.FechaPago.Date,
                Usuario = string.IsNullOrWhiteSpace(request.Usuario) ? null : request.Usuario.Trim(),
                Observacion = DbNullable(TrimToLength(request.Observacion, 250))
            }, cancellationToken: token, commandTimeout: 60));

            await appEvents.LogAuditAsync(
                ModuleName,
                "MarcarFletesPagados",
                "MV_VIAJES_CARGA",
                string.Join(",", ids),
                "Fletes marcados como pagados.",
                new { request.Usuario, request.Observacion, request.FechaPago, TotalIds = ids.Count, Actualizados = rows },
                token);

            return rows;
        }, "No se pudieron marcar los fletes como pagados.", ct);

    public async Task<CargaViajesLookupDto> GetLookupsAsync(CancellationToken ct = default)
    {
        var result = new CargaViajesLookupDto
        {
            Estados = [.. CargaViajeEstadoKeys.All]
        };
        return await ExecuteLoggedAsync(ModuleName, "GetLookups", async token =>
        {
            result.Clientes = (await SearchClientesAsync(string.Empty, token)).ToList();
            result.Choferes = (await SearchChoferLookupAsync(string.Empty, token)).ToList();
            result.Destinos = (await SearchDestinosLookupAsync(string.Empty, token)).ToList();
            result.TipoVehiculos = (await SearchTipoVehiculosLookupAsync(string.Empty, token)).ToList();
            return result;
        }, "No se pudieron cargar los datos auxiliares del mÃ³dulo.", ct);
    }

    public Task<CargaViajesViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default)
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

            var row = await cn.QuerySingleOrDefaultAsync<(string Value, string Aux)>(new CommandDefinition(sql, new { Clave = configKey.ToUpperInvariant() }, cancellationToken: token));
            var raw = ResolveStoredValue(row.Value, row.Aux);
            if (string.IsNullOrWhiteSpace(raw))
                return CreateDefaultViewSettings();

            return NormalizeViewSettings(JsonSerializer.Deserialize<CargaViajesViewSettingsDto>(raw, JsonOptions));
        }, "No se pudo cargar la configuraciÃ³n de vista.", ct);

    public Task SaveViewSettingsAsync(string userName, CargaViajesViewSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la vista.");

            var normalized = NormalizeViewSettings(settings);
            var serialized = JsonSerializer.Serialize(normalized, JsonOptions);
            var stored = SplitStoredValue(serialized);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
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

            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                ClaveNormalizada = configKey.ToUpperInvariant(),
                Clave = configKey,
                Valor = DbNullable(stored.Value),
                ValorAux = DbNullable(stored.AuxValue),
                Grupo = ConfigGroup
            }, cancellationToken: token));

            await appEvents.LogAuditAsync(ModuleName, "SaveViewSettings", "TA_CONFIGURACION", configKey, "ConfiguraciÃ³n de vista de viajes actualizada.", new { UserName = userName.Trim(), normalized.AgruparPor }, token);
        }, "No se pudo guardar la configuraciÃ³n de vista.", ct);

    public Task<CargaViajeTipoVehiculoViewSettingsDto> GetTipoVehiculoViewSettingsAsync(string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTipoVehiculoViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return CreateDefaultTipoVehiculoViewSettings();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildTipoVehiculoViewConfigKey(userName);
            var sql = $"""
                SELECT TOP (1)
                    ISNULL(VALOR, ''),
                    ISNULL({detailColumn}, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """;

            var row = await cn.QuerySingleOrDefaultAsync<(string Value, string Aux)>(new CommandDefinition(sql, new { Clave = configKey.ToUpperInvariant() }, cancellationToken: token));
            var raw = ResolveStoredValue(row.Value, row.Aux);
            if (string.IsNullOrWhiteSpace(raw))
                return CreateDefaultTipoVehiculoViewSettings();

            return NormalizeTipoVehiculoViewSettings(JsonSerializer.Deserialize<CargaViajeTipoVehiculoViewSettingsDto>(raw, JsonOptions));
        }, "No se pudo cargar la configuraciÃ³n de vista del tipo de vehÃ­culo.", ct);

    public Task SaveTipoVehiculoViewSettingsAsync(string userName, CargaViajeTipoVehiculoViewSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveTipoVehiculoViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la vista.");

            var normalized = NormalizeTipoVehiculoViewSettings(settings);
            var serialized = JsonSerializer.Serialize(normalized, JsonOptions);
            var stored = SplitStoredValue(serialized);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildTipoVehiculoViewConfigKey(userName);
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

            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                ClaveNormalizada = configKey.ToUpperInvariant(),
                Clave = configKey,
                Valor = DbNullable(stored.Value),
                ValorAux = DbNullable(stored.AuxValue),
                Grupo = "TIPOVEHICULO"
            }, cancellationToken: token));

            await appEvents.LogAuditAsync(ModuleName, "SaveTipoVehiculoViewSettings", "TA_CONFIGURACION", configKey, "ConfiguraciÃ³n de vista de tipo de vehÃ­culo actualizada.", new { UserName = userName.Trim(), normalized.AgruparPor }, token);
        }, "No se pudo guardar la configuraciÃ³n de vista del tipo de vehÃ­culo.", ct);

    public Task<CargaViajesConfigDto> GetConfiguracionAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetConfiguracion", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_CONFIGURACION", token))
                return CreateDefaultConfiguracion();

            var map = await LoadConfiguracionAsync(cn, token);
            return BuildConfiguracion(map);
        }, "No se pudo cargar la configuraciÃ³n del mÃ³dulo de viajes.", ct);

    public Task SaveConfiguracionAsync(CargaViajesConfigDto config, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveConfiguracion", async token =>
        {
            ArgumentNullException.ThrowIfNull(config);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_CONFIGURACION", token))
                throw new InvalidOperationException("La tabla TA_CONFIGURACION no existe en la base activa.");

            var normalized = NormalizeConfiguracion(config);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var item in BuildConfiguracionItems(normalized))
                await UpsertConfigValueAsync(cn, (SqlTransaction)tx, detailColumn, item.Key, item.Value, ConfigGroup, token);

            await tx.CommitAsync(token);
            await appEvents.LogAuditAsync(ModuleName, "SaveConfiguracion", "TA_CONFIGURACION", ConfigGroup, "ConfiguraciÃ³n del mÃ³dulo de viajes actualizada.", normalized, token);
        }, "No se pudo guardar la configuraciÃ³n del mÃ³dulo de viajes.", ct);

    public Task<string> GetNextIdComprobanteAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetNextIdComprobante", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var config = await LoadConfiguracionAsync(cn, token);
            var sucursal = ResolveSucursal(config);
            var letra = ResolveLetra(config);
            if (string.IsNullOrWhiteSpace(sucursal) || string.IsNullOrWhiteSpace(letra))
                throw new InvalidOperationException("Falta configurar VIAJES-SUCURSAL o VIAJES-LETRA.");
            sucursal = sucursal.Trim();
            letra = letra.Trim();
            if (sucursal.Length != 4 || letra.Length != 1)
                throw new InvalidOperationException("VIAJES-SUCURSAL debe tener 4 caracteres y VIAJES-LETRA debe tener 1 carácter.");

            const string viajeTable = "MV_VIAJES_CARGA";
            var sql = $"""
                SELECT ISNULL(MAX(CAST(SUBSTRING(IDCOMPROBANTE, 5, 8) AS int)), 0) + 1
                FROM dbo.{viajeTable}
                WHERE TC = @Tc
                  AND LEFT(ISNULL(IDCOMPROBANTE, ''), 4) = @Sucursal
                  AND LEN(ISNULL(IDCOMPROBANTE, '')) >= 13
                  AND SUBSTRING(IDCOMPROBANTE, 5, 8) NOT LIKE '%[^0-9]%';
                """;
            var next = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Tc = DefaultTc, Sucursal = sucursal }, cancellationToken: token));
            if (next <= 0)
                next = 1;
            logger.LogInformation("GetNextIdComprobante OK Tabla={Tabla} Sucursal={Sucursal} Letra={Letra} Next={Next}", viajeTable, sucursal, letra, next);
            return $"{sucursal}{next.ToString().PadLeft(8, '0')}{letra}";
        }, "No se pudo obtener la numeraciÃ³n del viaje.", ct);

    private static async Task<CargaViajeLookupOptionDto?> FindLookupByDescriptionAsync(
        SqlConnection cn,
        string table,
        string codeColumn,
        string descriptionColumn,
        string description,
        CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                LTRIM(RTRIM(ISNULL({codeColumn}, ''))) AS Codigo,
                LTRIM(RTRIM(ISNULL({descriptionColumn}, ''))) AS Titulo,
                '' AS Subtitulo
            FROM dbo.{table}
            WHERE LTRIM(RTRIM(ISNULL({descriptionColumn}, ''))) COLLATE Latin1_General_CI_AI = @Descripcion;
            """;

        return await cn.QuerySingleOrDefaultAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Descripcion = description }, cancellationToken: ct));
    }

    private static async Task<string> GetNextCodigoDisponibleAsync(SqlConnection cn, string table, string column, CancellationToken ct)
    {
        var next = await GetNextCodigoFromTableCoreAsync(cn, table, column, ct);
        var attempts = 0;
        while (await ExistsByCodeAsync(cn, table, column, next, ct))
        {
            attempts++;
            if (attempts > 10000)
                throw new InvalidOperationException($"No se pudo calcular un código libre para {table}.");

            var parsed = int.Parse(next, System.Globalization.CultureInfo.InvariantCulture);
            next = (parsed + 1).ToString().PadLeft(4, '0');
        }

        return next;
    }

    private static async Task<string> GetNextCodigoFromTableCoreAsync(SqlConnection cn, string table, string column, CancellationToken ct)
    {
        var sql = $"""
            SELECT ISNULL(MAX(CAST(LTRIM(RTRIM({column})) AS int)), 0) + 1
            FROM dbo.{table}
            WHERE {column} IS NOT NULL
              AND LTRIM(RTRIM({column})) <> ''
              AND LTRIM(RTRIM({column})) NOT LIKE '%[^0-9]%';
            """;

        var next = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
        return next <= 0 ? "0001" : next.ToString().PadLeft(4, '0');
    }

    private static string BuildCreateMaestraSql(string table, string codeColumn, string descriptionColumn, bool hasActivoDefault)
    {
        if (!hasActivoDefault)
            return $"""
                INSERT INTO dbo.{table} ({codeColumn}, {descriptionColumn})
                VALUES (@Codigo, @Descripcion);
                """;

        return $"""
            INSERT INTO dbo.{table} ({codeColumn}, {descriptionColumn}, ACTIVO)
            VALUES (@Codigo, @Descripcion, @Activo);
            """;
    }

    private static string BuildCreateChoferSql(string table, bool hasActivo, bool hasDisponible, bool hasEsFletero)
    {
        var columns = new List<string> { "CODIGO", "NOMBRES" };
        var values = new List<string> { "@Codigo", "@Nombre" };
        if (hasActivo)
        {
            columns.Add("ACTIVO");
            values.Add("@Activo");
        }
        if (hasDisponible)
        {
            columns.Add("DISPONIBLE");
            values.Add("@Disponible");
        }
        if (hasEsFletero)
        {
            columns.Add("ES_FLETERO");
            values.Add("@EsFletero");
        }

        return $"""
            INSERT INTO dbo.{table} ({string.Join(", ", columns)})
            VALUES ({string.Join(", ", values)});
            """;
    }

    private static async Task<int> GetColumnCharacterLengthAsync(SqlConnection cn, string table, string column, CancellationToken ct, int defaultValue)
    {
        const string sql = """
            SELECT TOP (1)
                CASE
                    WHEN c.max_length = -1 THEN -1
                    WHEN t.name LIKE 'n%' THEN c.max_length / 2
                    ELSE c.max_length
                END AS MaxChars
            FROM sys.columns c
            INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@FullName)
              AND UPPER(c.name) = UPPER(@ColumnName);
            """;

        var raw = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new { FullName = $"dbo.{table}", ColumnName = column }, cancellationToken: ct));
        if (raw is null)
            return defaultValue;

        return raw.Value < 0 ? 4000 : Math.Max(1, raw.Value);
    }

    private Task<string> GetNextCodigoFromTableAsync(string table, string column, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, $"GetNextCodigo_{table}", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var formatted = await GetNextCodigoFromTableCoreAsync(cn, table, column, token);
            var next = int.Parse(formatted, System.Globalization.CultureInfo.InvariantCulture);
            logger.LogInformation("GetNextCodigo OK Tabla={Tabla} Columna={Columna} Next={Next} Formatted={Formatted}", table, column, next, formatted);
            return formatted;
        }, $"No se pudo obtener la próxima numeración de {table}.", ct);

    public Task<string> GetSucursalConfiguradaAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetSucursalConfigurada", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var config = await LoadConfiguracionAsync(cn, token);
            return ResolveSucursal(config);
        }, "No se pudo leer la sucursal configurada.", ct);

    public Task EnsureViajesSchemaAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "EnsureViajesSchema", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var hasViajesTable = await TableExistsAsync(cn, "MV_VIAJES_CARGA", token);
            if (!hasViajesTable)
                logger.LogWarning("EnsureViajesSchema: no existe MV_VIAJES_CARGA en la base activa. Se omite la validación/actualización de esa vista.");

            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
            {
                logger.LogWarning("EnsureViajesSchema: no existe TA_TARIFA en la base activa. Se omite la actualización de esa tabla.");
                return;
            }

            var createdColumns = new List<string>();
            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                if (hasViajesTable)
                {
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "FLETE_PAGADO", "bit NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_FLETE_PAGADO DEFAULT (0) WITH VALUES", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "FECHA_PAGO_FLETE", "smalldatetime NULL", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "USUARIO_PAGO_FLETE", "nvarchar(50) NULL", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "OBSERVACION_PAGO_FLETE", "nvarchar(250) NULL", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "USUARIO", "nvarchar(50) NULL", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "ADICIONAL_FIJO1_DESCRIPCION", "nvarchar(100) NULL", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "ADICIONAL_FIJO1_IMPORTE", "money NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_ADICIONAL_FIJO1_IMPORTE DEFAULT (0) WITH VALUES", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "ADICIONAL_FIJO1_APLICADO", "bit NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_ADICIONAL_FIJO1_APLICADO DEFAULT (0) WITH VALUES", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "ADICIONAL_FIJO2_DESCRIPCION", "nvarchar(100) NULL", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "ADICIONAL_FIJO2_IMPORTE", "money NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_ADICIONAL_FIJO2_IMPORTE DEFAULT (0) WITH VALUES", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "ADICIONAL_FIJO2_APLICADO", "bit NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_ADICIONAL_FIJO2_APLICADO DEFAULT (0) WITH VALUES", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "ADICIONAL_FIJO3_DESCRIPCION", "nvarchar(100) NULL", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "ADICIONAL_FIJO3_IMPORTE", "money NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_ADICIONAL_FIJO3_IMPORTE DEFAULT (0) WITH VALUES", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "ADICIONAL_FIJO3_APLICADO", "bit NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_ADICIONAL_FIJO3_APLICADO DEFAULT (0) WITH VALUES", createdColumns, token);
                    await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "MV_VIAJES_CARGA", "TOTAL_ADICIONALES_FIJOS", "money NOT NULL CONSTRAINT DF_MV_VIAJES_CARGA_TOTAL_ADICIONALES_FIJOS DEFAULT (0) WITH VALUES", createdColumns, token);
                }

                await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "TA_TARIFA", "AdicionalFijo1Descripcion", "nvarchar(100) NULL", createdColumns, token);
                await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "TA_TARIFA", "AdicionalFijo1Importe", "money NOT NULL CONSTRAINT DF_TA_TARIFA_AdicionalFijo1Importe DEFAULT (0) WITH VALUES", createdColumns, token);
                await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "TA_TARIFA", "AdicionalFijo2Descripcion", "nvarchar(100) NULL", createdColumns, token);
                await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "TA_TARIFA", "AdicionalFijo2Importe", "money NOT NULL CONSTRAINT DF_TA_TARIFA_AdicionalFijo2Importe DEFAULT (0) WITH VALUES", createdColumns, token);
                await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "TA_TARIFA", "AdicionalFijo3Descripcion", "nvarchar(100) NULL", createdColumns, token);
                await EnsureSchemaColumnAsync(cn, (SqlTransaction)tx, "TA_TARIFA", "AdicionalFijo3Importe", "money NOT NULL CONSTRAINT DF_TA_TARIFA_AdicionalFijo3Importe DEFAULT (0) WITH VALUES", createdColumns, token);

                await tx.CommitAsync(token);
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(token);
                }
                catch (Exception rollbackEx)
                {
                    logger.LogWarning(rollbackEx, "EnsureViajesSchema rollback falló");
                }

                throw;
            }

            if (createdColumns.Count == 0)
            {
                logger.LogInformation("EnsureViajesSchema sin cambios. Las tablas del módulo Viajes ya estaban actualizadas.");
                return;
            }

            logger.LogInformation("EnsureViajesSchema creó columnas: {Columns}", string.Join(", ", createdColumns));
        }, "No se pudo verificar la estructura del módulo Viajes.", ct);

    private static async Task<Dictionary<string, string>> LoadConfiguracionAsync(SqlConnection cn, CancellationToken ct)
    {
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var keys = new[]
        {
            SucursalConfigKey,
            LegacySucursalConfigKey,
            LetraConfigKey,
            PorcentajesAdicionalesHabilitadosConfigKey,
            "VIAJES-ADIC-NOMBRE-0",
            "VIAJES-ADIC-NOMBRE-1",
            "VIAJES-ADIC-NOMBRE-2",
            "VIAJES-ADIC-NOMBRE-3",
            "VIAJES-ADIC-NOMBRE-4",
            "VIAJES-ADIC-PORC-0",
            "VIAJES-ADIC-PORC-1",
            "VIAJES-ADIC-PORC-2",
            "VIAJES-ADIC-PORC-3",
            "VIAJES-ADIC-PORC-4"
        };

        var sql = $"""
            SELECT
                UPPER(LTRIM(RTRIM(CLAVE))) AS Clave,
                ISNULL(VALOR, '') AS Valor,
                ISNULL({detailColumn}, '') AS ValorAux
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ({string.Join(", ", keys.Select((_, index) => $"@K{index}"))});
            """;

        var args = new DynamicParameters();
        for (var i = 0; i < keys.Length; i++)
            args.Add($"@K{i}", keys[i].ToUpperInvariant());

        var rows = await cn.QueryAsync<(string Clave, string Valor, string ValorAux)>(new CommandDefinition(sql, args, cancellationToken: ct));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = row.Clave.Trim().ToUpperInvariant();
            var value = ResolveStoredValue(row.Valor, row.ValorAux);
            if (key == LegacySucursalConfigKey && result.ContainsKey(SucursalConfigKey))
                continue;

            result[key] = value;
        }

        return result;
    }

    private async Task<int> SaveViajeExplicitAsync(CargaViajeSaveRequest request, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        const string viajeTable = "MV_VIAJES_CARGA";
        if (!await TableExistsAsync(cn, viajeTable, ct))
            throw new InvalidOperationException("La tabla MV_VIAJES_CARGA no existe en la base activa.");

        var tc = string.IsNullOrWhiteSpace(request.Tc) ? DefaultTc : request.Tc.Trim().ToUpperInvariant();
        var cliente = (request.Cliente ?? string.Empty).Trim();
        var chofer = (request.Chofer ?? string.Empty).Trim();
        var destino = (request.Destino ?? string.Empty).Trim();
        var tipoVehiculo = (request.TipoVehiculo ?? string.Empty).Trim();
        var listaCodigo = (request.Lista ?? string.Empty).Trim();
        var observaciones = (request.Observaciones ?? string.Empty).Trim();
        var estado = string.IsNullOrWhiteSpace(request.Estado) ? CargaViajeEstadoKeys.Pendiente : request.Estado.Trim().ToUpperInvariant();
        var destinoDisplay = (request.DestinoDisplay ?? string.Empty).Trim();
        var choferDisplay = (request.ChoferDisplay ?? string.Empty).Trim();

        request.Tc = tc;
        request.Cliente = cliente;
        request.Chofer = chofer;
        request.Destino = destino;
        request.TipoVehiculo = tipoVehiculo;
        request.Lista = listaCodigo;
        request.Observaciones = observaciones;
        request.Estado = estado;

        if (string.IsNullOrWhiteSpace(listaCodigo))
        {
            var clienteLista = await GetListaRMTRFAsync(cn, cliente, ct);
            listaCodigo = clienteLista.ListaCodigo;
        }

        var listaTexto = string.Empty;
        if (!string.IsNullOrWhiteSpace(listaCodigo))
        {
            var listaInfo = await GetListaTextoAsync(cn, listaCodigo, ct);
            listaCodigo = listaInfo.ListaCodigo;
            listaTexto = listaInfo.ListaTexto;
        }

        request.ListaDisplay = listaTexto;

        var nextIdComp = string.IsNullOrWhiteSpace(request.IdComprobante)
            ? await GetNextIdComprobanteAsync(ct)
            : request.IdComprobante.Trim();

        if (string.IsNullOrWhiteSpace(nextIdComp))
            throw new InvalidOperationException("Falta configurar VIAJES-SUCURSAL o VIAJES-LETRA.");

        request.IdComprobante = nextIdComp;

        var totals = CalculateTotals(request);
        var totalAdicionalesFijos = CalculateTotalAdicionalesFijos(request);
        logger.LogInformation(
            "SaveViaje before insert Tc={Tc} IdComprobante={IdComprobante} IdCliente={IdCliente} IdDestino={IdDestino} IdTipoVehiculo={IdTipoVehiculo} IdChofer={IdChofer} IdLista={IdLista} TotalImporte={TotalImporte} TotalFlete={TotalFlete} TotalPeaje={TotalPeaje} TotalViajes={TotalViajes}",
            tc,
            nextIdComp,
            cliente,
            destino,
            tipoVehiculo,
            chofer,
            listaCodigo,
            totals.TotalImporte,
            totals.TotalFlete,
            request.Peaje,
            request.CantidadViajes);

        var isNew = !request.Id.HasValue || request.Id.Value <= 0;
        var sql = isNew
            ? """
                INSERT INTO dbo.MV_VIAJES_CARGA
                (
                    TC,
                    IDCLIENTE,
                    IDCOMPROBANTE,
                    FECHA,
                    IDDESTINO,
                    DESCRIPCIONDESTINO,
                    IDTIPOVEHICULO,
                    IDLISTA,
                    IDCHOFER,
                    NOMBRE_CHOFER,
                    OBSERVACIONES,
                    ANULADO,
                    ESTADO,
                    TOTAL_IMPORTE,
                    TOTAL_FLETE,
                    TOTAL_PEAJE,
                    TOTAL_VIAJES,
                    PORCENTAJE_ADIC,
                    PORCENTAJE_ADIC1,
                    PORCENTAJE_ADIC2,
                    PORCENTAJE_ADIC3,
                    PORCENTAJE_ADIC4,
                    TOTAL_ADIC,
                    TOTAL_ADIC1,
                    TOTAL_ADIC2,
                    TOTAL_ADIC3,
                    TOTAL_ADIC4,
                    TOTAL_ADICIONALES,
                    ADICIONAL_FIJO1_DESCRIPCION,
                    ADICIONAL_FIJO1_IMPORTE,
                    ADICIONAL_FIJO1_APLICADO,
                    ADICIONAL_FIJO2_DESCRIPCION,
                    ADICIONAL_FIJO2_IMPORTE,
                    ADICIONAL_FIJO2_APLICADO,
                    ADICIONAL_FIJO3_DESCRIPCION,
                    ADICIONAL_FIJO3_IMPORTE,
                    ADICIONAL_FIJO3_APLICADO,
                    TOTAL_ADICIONALES_FIJOS,
                    FECHAHORA_ALTA,
                    FECHAHORA_MODIFICACION
                )
                VALUES
                (
                    @Tc,
                    @IdCliente,
                    @IdComprobante,
                    @Fecha,
                    @IdDestino,
                    @DescripcionDestino,
                    @IdTipoVehiculo,
                    @IdLista,
                    @IdChofer,
                    @NombreChofer,
                    @Observaciones,
                    0,
                    @Estado,
                    @TotalImporte,
                    @TotalFlete,
                    @TotalPeaje,
                    @TotalViajes,
                    @PorcentajeAdic,
                    @PorcentajeAdic1,
                    @PorcentajeAdic2,
                    @PorcentajeAdic3,
                    @PorcentajeAdic4,
                    @TotalAdic,
                    @TotalAdic1,
                    @TotalAdic2,
                    @TotalAdic3,
                    @TotalAdic4,
                    @TotalAdicionales,
                    @AdicionalFijo1Descripcion,
                    @AdicionalFijo1Importe,
                    @AdicionalFijo1Aplicado,
                    @AdicionalFijo2Descripcion,
                    @AdicionalFijo2Importe,
                    @AdicionalFijo2Aplicado,
                    @AdicionalFijo3Descripcion,
                    @AdicionalFijo3Importe,
                    @AdicionalFijo3Aplicado,
                    @TotalAdicionalesFijos,
                    GETDATE(),
                    GETDATE()
                );
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """
            : """
                UPDATE dbo.MV_VIAJES_CARGA
                SET
                    TC = @Tc,
                    IDCLIENTE = @IdCliente,
                    IDCOMPROBANTE = @IdComprobante,
                    FECHA = @Fecha,
                    IDDESTINO = @IdDestino,
                    DESCRIPCIONDESTINO = @DescripcionDestino,
                    IDTIPOVEHICULO = @IdTipoVehiculo,
                    IDLISTA = @IdLista,
                    IDCHOFER = @IdChofer,
                    NOMBRE_CHOFER = @NombreChofer,
                    USUARIO = @Usuario,
                    OBSERVACIONES = @Observaciones,
                    ANULADO = 0,
                    ESTADO = @Estado,
                    TOTAL_IMPORTE = @TotalImporte,
                    TOTAL_FLETE = @TotalFlete,
                    TOTAL_PEAJE = @TotalPeaje,
                    TOTAL_VIAJES = @TotalViajes,
                    PORCENTAJE_ADIC = @PorcentajeAdic,
                    PORCENTAJE_ADIC1 = @PorcentajeAdic1,
                    PORCENTAJE_ADIC2 = @PorcentajeAdic2,
                    PORCENTAJE_ADIC3 = @PorcentajeAdic3,
                    PORCENTAJE_ADIC4 = @PorcentajeAdic4,
                    TOTAL_ADIC = @TotalAdic,
                    TOTAL_ADIC1 = @TotalAdic1,
                    TOTAL_ADIC2 = @TotalAdic2,
                    TOTAL_ADIC3 = @TotalAdic3,
                    TOTAL_ADIC4 = @TotalAdic4,
                    TOTAL_ADICIONALES = @TotalAdicionales,
                    ADICIONAL_FIJO1_DESCRIPCION = @AdicionalFijo1Descripcion,
                    ADICIONAL_FIJO1_IMPORTE = @AdicionalFijo1Importe,
                    ADICIONAL_FIJO1_APLICADO = @AdicionalFijo1Aplicado,
                    ADICIONAL_FIJO2_DESCRIPCION = @AdicionalFijo2Descripcion,
                    ADICIONAL_FIJO2_IMPORTE = @AdicionalFijo2Importe,
                    ADICIONAL_FIJO2_APLICADO = @AdicionalFijo2Aplicado,
                    ADICIONAL_FIJO3_DESCRIPCION = @AdicionalFijo3Descripcion,
                    ADICIONAL_FIJO3_IMPORTE = @AdicionalFijo3Importe,
                    ADICIONAL_FIJO3_APLICADO = @AdicionalFijo3Aplicado,
                    TOTAL_ADICIONALES_FIJOS = @TotalAdicionalesFijos,
                    FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                SELECT CAST(@Id AS int);
                """;

        logger.LogInformation("SaveViaje SQL {Operacion} {Sql}", isNew ? "INSERT" : "UPDATE", sql);

        await using var tx = await cn.BeginTransactionAsync(ct);
        try
        {
            var affectedId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
            {
                request.Id,
                Tc = tc,
                IdCliente = cliente,
                IdComprobante = nextIdComp,
                Fecha = request.Fecha,
                IdDestino = destino,
                DescripcionDestino = ExtractDescripcionFromDisplay(destinoDisplay),
                IdTipoVehiculo = tipoVehiculo,
                IdLista = string.IsNullOrWhiteSpace(listaCodigo) ? null : listaCodigo,
                IdChofer = chofer,
                NombreChofer = ExtractDescripcionFromDisplay(choferDisplay),
                Usuario = NormalizeUser(request.UsuarioAccion),
                Observaciones = observaciones,
                Estado = estado,
                TotalImporte = totals.TotalImporte,
                TotalFlete = totals.TotalFlete,
                TotalPeaje = request.Peaje,
                TotalViajes = Math.Max(1, request.CantidadViajes),
                PorcentajeAdic = request.PorcentajeAdic,
                PorcentajeAdic1 = request.PorcentajeAdic1,
                PorcentajeAdic2 = request.PorcentajeAdic2,
                PorcentajeAdic3 = request.PorcentajeAdic3,
                PorcentajeAdic4 = request.PorcentajeAdic4,
                TotalAdic = totals.TotalAdic,
                TotalAdic1 = totals.TotalAdic1,
                TotalAdic2 = totals.TotalAdic2,
                TotalAdic3 = totals.TotalAdic3,
                TotalAdic4 = totals.TotalAdic4,
                TotalAdicionales = totals.TotalAdicionales,
                AdicionalFijo1Descripcion = (request.AdicionalFijo1Descripcion ?? string.Empty).Trim(),
                AdicionalFijo1Importe = request.AdicionalFijo1Importe,
                AdicionalFijo1Aplicado = request.AdicionalFijo1Aplicado,
                AdicionalFijo2Descripcion = (request.AdicionalFijo2Descripcion ?? string.Empty).Trim(),
                AdicionalFijo2Importe = request.AdicionalFijo2Importe,
                AdicionalFijo2Aplicado = request.AdicionalFijo2Aplicado,
                AdicionalFijo3Descripcion = (request.AdicionalFijo3Descripcion ?? string.Empty).Trim(),
                AdicionalFijo3Importe = request.AdicionalFijo3Importe,
                AdicionalFijo3Aplicado = request.AdicionalFijo3Aplicado,
                TotalAdicionalesFijos = totalAdicionalesFijos
            }, transaction: (SqlTransaction)tx, cancellationToken: ct));

            if (isNew && affectedId <= 0)
                throw new InvalidOperationException("No se pudo obtener el ID generado del viaje.");

            await tx.CommitAsync(ct);
            logger.LogInformation("SaveViaje {Operacion} OK Id={Id} IdComprobante={IdComprobante} InsertedId={InsertedId} Rows=1", isNew ? "insert" : "update", request.Id, nextIdComp, affectedId);
            return isNew ? affectedId : request.Id!.Value;
        }
        catch
        {
            try
            {
                await tx.RollbackAsync(ct);
            }
            catch (Exception rollbackEx)
            {
                logger.LogWarning(rollbackEx, "SaveViaje rollback fallÃ³");
            }

            throw;
        }
    }

    private async Task<(string ListaCodigo, string ListaTexto)> GetListaRMTRFAsync(SqlConnection cn, string clienteCodigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clienteCodigo))
            return (string.Empty, string.Empty);

        const string sql = """
            SELECT TOP (1)
                ISNULL(LTRIM(RTRIM(ISNULL(cli.IdListaRMTRF, ''))), '') AS ListaCodigo,
                LTRIM(RTRIM(ISNULL(t.Nombre, ''))) AS ListaTexto
            FROM dbo.Vt_Clientes cli
            OUTER APPLY (
                SELECT TOP (1)
                    t.Nombre
                FROM dbo.TA_TARIFA t
                WHERE UPPER(LTRIM(RTRIM(ISNULL(t.IdLista, '')))) = UPPER(LTRIM(RTRIM(ISNULL(cli.IdListaRMTRF, ''))))
                ORDER BY t.Nombre, t.IdLista
                ) t
            WHERE UPPER(LTRIM(RTRIM(cli.CODIGO))) = @Codigo
            """;

        var row = await cn.QuerySingleOrDefaultAsync<ListaTextoRow>(new CommandDefinition(sql, new { Codigo = clienteCodigo.Trim().ToUpperInvariant() }, cancellationToken: ct));
        if (row is null)
            return (string.Empty, string.Empty);

        return ((row.ListaCodigo ?? string.Empty).Trim(), (row.ListaTexto ?? string.Empty).Trim());
    }

    private async Task<(string ListaCodigo, string ListaTexto)> GetListaTextoAsync(SqlConnection cn, string listaCodigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(listaCodigo))
            return (string.Empty, string.Empty);

        const string sql = """
            SELECT TOP (1)
                ISNULL(LTRIM(RTRIM(IdLista)), '') AS ListaCodigo,
                LTRIM(RTRIM(ISNULL(Nombre, ''))) AS ListaTexto
            FROM dbo.TA_TARIFA
            WHERE UPPER(LTRIM(RTRIM(ISNULL(IdLista, '')))) = @Codigo
            ORDER BY Nombre, IdLista;
            """;

        var row = await cn.QuerySingleOrDefaultAsync<ListaTextoRow>(new CommandDefinition(sql, new { Codigo = listaCodigo.Trim().ToUpperInvariant() }, cancellationToken: ct));
        if (row is null)
            return (string.Empty, string.Empty);

        return ((row.ListaCodigo ?? string.Empty).Trim(), (row.ListaTexto ?? string.Empty).Trim());
    }

    private static decimal ParseDecimal(string? value)
        => decimal.TryParse(value, out var parsed) ? parsed : 0m;

    private static string BuildValidationMessage(ValidationResult validation)
    {
        var issues = validation.Issues
            .Select(issue => (issue.Message ?? string.Empty).Trim())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return issues.Count == 0
            ? "Revisá los campos marcados antes de guardar."
            : string.Join(Environment.NewLine, issues);
    }

    private sealed class TarifaClienteLookupRow
    {
        public int IdTarifa { get; set; }
        public string ClienteCodigo { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;
        public string DestinoCodigo { get; set; } = string.Empty;
        public string DestinoNombre { get; set; } = string.Empty;
        public string TipoVehiculoCodigo { get; set; } = string.Empty;
        public string TipoVehiculoNombre { get; set; } = string.Empty;
        public string ListaCodigo { get; set; } = string.Empty;
        public string ListaNombre { get; set; } = string.Empty;
        public string FleteroCodigoSugerido { get; set; } = string.Empty;
        public string FleteroNombreSugerido { get; set; } = string.Empty;
        public decimal ImporteBase { get; set; }
        public bool TarifaFletero { get; set; }
        public bool Activo { get; set; }
    }

    private static async Task<decimal> ResolveTarifaImporteAsync(
        SqlConnection cn,
        string campoPrincipal,
        string codigo,
        string campoDestino,
        string destino,
        string campoTipoVehiculo,
        string tipoVehiculo,
        int tarifaFletero,
        string? choferGeneral,
        CancellationToken ct)
    {
        // Las tarifas sin chofer se consideran generales y pueden aplicar como fallback.
        var allowGeneralFallback = true;
        var sql = $"""
            SELECT TOP (1)
                ISNULL(Importe, 0)
            FROM (
                SELECT
                    Importe,
                    IdLista,
                    CASE
                        WHEN UPPER(LTRIM(RTRIM(ISNULL({campoPrincipal}, '')))) = @Codigo THEN 0
                        WHEN @AllowGeneralFallback = 1 AND LTRIM(RTRIM(ISNULL({campoPrincipal}, ''))) = '' THEN 1
                        ELSE 2
                    END AS MatchOrder
                FROM dbo.TA_TARIFA
                WHERE ISNULL(Activo, 1) = 1
                  AND ISNULL(TarifaFletero, 0) = @TarifaFletero
                  AND (
                        UPPER(LTRIM(RTRIM(ISNULL({campoPrincipal}, '')))) = @Codigo
                        OR (@AllowGeneralFallback = 1 AND LTRIM(RTRIM(ISNULL({campoPrincipal}, ''))) = '')
                      )
                  AND UPPER(LTRIM(RTRIM(ISNULL({campoDestino}, '')))) = @Destino
                  AND UPPER(LTRIM(RTRIM(ISNULL({campoTipoVehiculo}, '')))) = @TipoVehiculo
            ) t
            ORDER BY t.MatchOrder, t.IdLista;
            """;

        var result = await cn.ExecuteScalarAsync<decimal?>(new CommandDefinition(sql, new
        {
            TarifaFletero = tarifaFletero,
            Codigo = codigo,
            Destino = TrimUpper(destino),
            TipoVehiculo = TrimUpper(tipoVehiculo),
            AllowGeneralFallback = allowGeneralFallback
        }, cancellationToken: ct));

        return result ?? 0m;
    }

    private async Task<CargaViajeTarifaGridItemDto?> ResolveTarifaDetalleAsync(
        SqlConnection cn,
        HashSet<string> columns,
        IReadOnlyList<string> principalColumnCandidates,
        string principalValue,
        IReadOnlyList<string> destinoColumnCandidates,
        string destinoValue,
        IReadOnlyList<string> tipoVehiculoColumnCandidates,
        string tipoVehiculoValue,
        int tarifaFletero,
        CancellationToken ct)
    {
        var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
        var principalColumn = FirstExistingColumn(columns, principalColumnCandidates.ToArray());
        var destinoColumn = FirstExistingColumn(columns, destinoColumnCandidates.ToArray());
        var tipoVehiculoColumn = FirstExistingColumn(columns, tipoVehiculoColumnCandidates.ToArray());
        var hasAdicionalFijo1Descripcion = columns.Contains("adicionalfijo1descripcion");
        var hasAdicionalFijo1Importe = columns.Contains("adicionalfijo1importe");
        var hasAdicionalFijo2Descripcion = columns.Contains("adicionalfijo2descripcion");
        var hasAdicionalFijo2Importe = columns.Contains("adicionalfijo2importe");
        var hasAdicionalFijo3Descripcion = columns.Contains("adicionalfijo3descripcion");
        var hasAdicionalFijo3Importe = columns.Contains("adicionalfijo3importe");

        var principal = (principalValue ?? string.Empty).Trim().ToUpperInvariant();
        var destino = (destinoValue ?? string.Empty).Trim().ToUpperInvariant();
        var tipoVehiculo = (tipoVehiculoValue ?? string.Empty).Trim().ToUpperInvariant();
        var queries = new List<(string Sql, object Params)>();

        string BuildSql(string? principalCondition)
        {
            var sql = $"""
                SELECT TOP (1)
                    LTRIM(RTRIM(ISNULL({idListaColumn}, ''))) AS IdLista,
                    ISNULL(Nombre, '') AS Nombre,
                    ISNULL(Importe, 0) AS Importe,
                    ISNULL({principalColumn}, '') AS Cliente,
                    '' AS Chofer,
                    ISNULL({destinoColumn}, '') AS Destino,
                    ISNULL({tipoVehiculoColumn}, '') AS TipoVehiculo,
                    ISNULL(TarifaFletero, 0) AS TarifaFletero,
                    ISNULL(PorcentajeAdic, 0) AS PorcentajeAdic,
                    ISNULL(PorcentajeAdic1, 0) AS PorcentajeAdic1,
                    ISNULL(PorcentajeAdic2, 0) AS PorcentajeAdic2,
                    ISNULL(PorcentajeAdic3, 0) AS PorcentajeAdic3,
                    ISNULL(PorcentajeAdic4, 0) AS PorcentajeAdic4,
                    {(hasAdicionalFijo1Descripcion ? "ISNULL(AdicionalFijo1Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo1Descripcion,
                    {(hasAdicionalFijo1Importe ? "ISNULL(AdicionalFijo1Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo1Importe,
                    {(hasAdicionalFijo2Descripcion ? "ISNULL(AdicionalFijo2Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo2Descripcion,
                    {(hasAdicionalFijo2Importe ? "ISNULL(AdicionalFijo2Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo2Importe,
                    {(hasAdicionalFijo3Descripcion ? "ISNULL(AdicionalFijo3Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo3Descripcion,
                    {(hasAdicionalFijo3Importe ? "ISNULL(AdicionalFijo3Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo3Importe,
                    ISNULL(Activo, 1) AS Activo
                FROM dbo.TA_TARIFA
                WHERE ISNULL(Activo, 1) = 1
                  AND ISNULL(TarifaFletero, 0) = @TarifaFletero
                  AND UPPER(LTRIM(RTRIM(ISNULL({destinoColumn}, '')))) = @Destino
                  AND UPPER(LTRIM(RTRIM(ISNULL({tipoVehiculoColumn}, '')))) = @TipoVehiculo
                """;

            if (!string.IsNullOrWhiteSpace(principalCondition))
            {
                sql += Environment.NewLine + principalCondition + Environment.NewLine;
            }

            sql += $"""
                ORDER BY Nombre, {idListaColumn};
                """;
            return sql;
        }

        if (!string.IsNullOrWhiteSpace(principal))
        {
            queries.Add((
                BuildSql($"""
                    AND UPPER(LTRIM(RTRIM(ISNULL({principalColumn}, '')))) = @Principal
                    """),
                new
                {
                    TarifaFletero = tarifaFletero,
                    Principal = principal,
                    Destino = destino,
                    TipoVehiculo = tipoVehiculo
                }));
        }

        queries.Add((
            BuildSql($"""
                AND LTRIM(RTRIM(ISNULL({principalColumn}, ''))) = ''
                """),
            new
            {
                TarifaFletero = tarifaFletero,
                Destino = destino,
                TipoVehiculo = tipoVehiculo
            }));

        queries.Add((
            $"""
                SELECT TOP (1)
                    LTRIM(RTRIM(ISNULL({idListaColumn}, ''))) AS IdLista,
                    ISNULL(Nombre, '') AS Nombre,
                    ISNULL(Importe, 0) AS Importe,
                    ISNULL({principalColumn}, '') AS Cliente,
                    '' AS Chofer,
                    ISNULL({destinoColumn}, '') AS Destino,
                    ISNULL({tipoVehiculoColumn}, '') AS TipoVehiculo,
                    ISNULL(TarifaFletero, 0) AS TarifaFletero,
                    ISNULL(PorcentajeAdic, 0) AS PorcentajeAdic,
                    ISNULL(PorcentajeAdic1, 0) AS PorcentajeAdic1,
                    ISNULL(PorcentajeAdic2, 0) AS PorcentajeAdic2,
                    ISNULL(PorcentajeAdic3, 0) AS PorcentajeAdic3,
                    ISNULL(PorcentajeAdic4, 0) AS PorcentajeAdic4,
                    {(hasAdicionalFijo1Descripcion ? "ISNULL(AdicionalFijo1Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo1Descripcion,
                    {(hasAdicionalFijo1Importe ? "ISNULL(AdicionalFijo1Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo1Importe,
                    {(hasAdicionalFijo2Descripcion ? "ISNULL(AdicionalFijo2Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo2Descripcion,
                    {(hasAdicionalFijo2Importe ? "ISNULL(AdicionalFijo2Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo2Importe,
                    {(hasAdicionalFijo3Descripcion ? "ISNULL(AdicionalFijo3Descripcion, '')" : "CAST('' AS nvarchar(100))")} AS AdicionalFijo3Descripcion,
                    {(hasAdicionalFijo3Importe ? "ISNULL(AdicionalFijo3Importe, 0)" : "CAST(0 AS money)")} AS AdicionalFijo3Importe,
                    ISNULL(Activo, 1) AS Activo
                FROM dbo.TA_TARIFA
                WHERE ISNULL(Activo, 1) = 1
                  AND ISNULL(TarifaFletero, 0) = @TarifaFletero
                  AND UPPER(LTRIM(RTRIM(ISNULL({destinoColumn}, '')))) = @Destino
                  AND UPPER(LTRIM(RTRIM(ISNULL({tipoVehiculoColumn}, '')))) = @TipoVehiculo
                ORDER BY
                    CASE WHEN LTRIM(RTRIM(ISNULL({principalColumn}, ''))) = '' THEN 0 ELSE 1 END,
                    Nombre,
                    {idListaColumn};
                """,
            new
            {
                TarifaFletero = tarifaFletero,
                Destino = destino,
                TipoVehiculo = tipoVehiculo
            }));

        foreach (var query in queries)
        {
            var row = await cn.QuerySingleOrDefaultAsync<CargaViajeTarifaGridItemDto>(
                new CommandDefinition(query.Sql, query.Params, cancellationToken: ct));
            if (row is not null)
                return row;
        }

        return null;
    }

    private static (decimal TotalImporte, decimal TotalFlete, decimal TotalAdic, decimal TotalAdic1, decimal TotalAdic2, decimal TotalAdic3, decimal TotalAdic4, decimal TotalAdicionales) CalculateTotals(CargaViajeSaveRequest request)
    {
        var cantidad = Math.Max(1, request.CantidadViajes);
        var totalImporte = request.ImporteCliente * cantidad;
        var totalFlete = request.ImporteFletero * cantidad;
        var totalAdic = totalImporte * request.PorcentajeAdic / 100m;
        var totalAdic1 = totalImporte * request.PorcentajeAdic1 / 100m;
        var totalAdic2 = totalImporte * request.PorcentajeAdic2 / 100m;
        var totalAdic3 = totalImporte * request.PorcentajeAdic3 / 100m;
        var totalAdic4 = totalImporte * request.PorcentajeAdic4 / 100m;
        var totalAdicionales = totalAdic + totalAdic1 + totalAdic2 + totalAdic3 + totalAdic4;

        return (totalImporte, totalFlete, totalAdic, totalAdic1, totalAdic2, totalAdic3, totalAdic4, totalAdicionales);
    }

    private static decimal CalculateTotalAdicionalesFijos(CargaViajeSaveRequest request)
    {
        var total = 0m;
        if (request.AdicionalFijo1Aplicado)
            total += Math.Max(0m, request.AdicionalFijo1Importe);
        if (request.AdicionalFijo2Aplicado)
            total += Math.Max(0m, request.AdicionalFijo2Importe);
        if (request.AdicionalFijo3Aplicado)
            total += Math.Max(0m, request.AdicionalFijo3Importe);
        return total;
    }

    private static decimal ResolvePercentOrDefault(decimal value, decimal defaultValue)
        => value > 0m ? value : defaultValue;

    private static string ExtractDescripcionFromDisplay(string? display)
    {
        var value = (display ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var separator = value.IndexOf(" - ", StringComparison.Ordinal);
        if (separator < 0)
            return value;

        return value[(separator + 3)..].Trim();
    }

    private static string BuildListaDesdeCliente(string clienteCodigo)
    {
        var code = (clienteCodigo ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var lastFour = code.Length <= 4 ? code : code[^4..];
        return lastFour.PadLeft(4, '0');
    }

    private static string BuildChoferNombreSql(string tableName, string alias)
        => $"LTRIM(RTRIM(ISNULL({alias}.NOMBRES, '')))";

    private static string BuildDestinoDescripcionSql(string tableName, string alias)
        => $"LTRIM(RTRIM(ISNULL({alias}.Descripcion, '')))";

    private static string BuildTipoVehiculoDescripcionSql(string tableName, string alias)
        => $"LTRIM(RTRIM(ISNULL({alias}.DESCRIPCION, '')))";

    private static CargaViajesConfigDto CreateDefaultConfiguracion()
        => new()
        {
            Sucursal = "0001",
            Letra = "X",
            ChoferGeneral = string.Empty,
            PorcentajesAdicionalesHabilitados = 3,
            NombresAdicionales = ["Adicional 1", "Adicional 2", "Adicional 3", "Adicional 4", "Adicional 5"],
            PorcentajesAdicionales = [0m, 0m, 0m, 0m, 0m]
        };

    private static CargaViajesConfigDto BuildConfiguracion(Dictionary<string, string> values)
    {
        var dto = CreateDefaultConfiguracion();
        dto.Sucursal = ResolveSucursal(values);
        dto.Letra = ResolveLetra(values);
        dto.ChoferGeneral = ResolveChoferGeneral(values);
        dto.PorcentajesAdicionalesHabilitados = ResolveAdicionalesHabilitados(values);

        for (var i = 0; i < 5; i++)
        {
            var nameKey = $"VIAJES-ADIC-NOMBRE-{i}";
            var percKey = $"VIAJES-ADIC-PORC-{i}";
            if (values.TryGetValue(nameKey, out var name) && !string.IsNullOrWhiteSpace(name))
                dto.NombresAdicionales[i] = name.Trim();
            if (values.TryGetValue(percKey, out var perc) && decimal.TryParse(perc, out var parsed))
                dto.PorcentajesAdicionales[i] = parsed;
        }

        return dto;
    }

    private static IEnumerable<(string Key, string Value)> BuildConfiguracionItems(CargaViajesConfigDto config)
    {
        yield return (SucursalConfigKey, config.Sucursal);
        yield return (LetraConfigKey, config.Letra);
        yield return (ChoferGeneralConfigKey, config.ChoferGeneral ?? string.Empty);
        yield return (PorcentajesAdicionalesHabilitadosConfigKey, ResolveAdicionalesHabilitados(config.PorcentajesAdicionalesHabilitados).ToString(System.Globalization.CultureInfo.InvariantCulture));

        for (var i = 0; i < 5; i++)
        {
            yield return ($"VIAJES-ADIC-NOMBRE-{i}", config.NombresAdicionales.ElementAtOrDefault(i) ?? string.Empty);
            yield return ($"VIAJES-ADIC-PORC-{i}", config.PorcentajesAdicionales.ElementAtOrDefault(i).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static CargaViajesConfigDto NormalizeConfiguracion(CargaViajesConfigDto config)
    {
        var defaults = CreateDefaultConfiguracion();
        return new CargaViajesConfigDto
        {
            Sucursal = ResolveSucursal(config.Sucursal),
            Letra = ResolveLetra(config.Letra),
            ChoferGeneral = ResolveChoferGeneral(config.ChoferGeneral),
            PorcentajesAdicionalesHabilitados = ResolveAdicionalesHabilitados(config.PorcentajesAdicionalesHabilitados),
            NombresAdicionales = Enumerable.Range(0, 5)
                .Select(i =>
                {
                    var value = config.NombresAdicionales.ElementAtOrDefault(i);
                    return string.IsNullOrWhiteSpace(value) ? defaults.NombresAdicionales[i] : value.Trim();
                })
                .ToList(),
            PorcentajesAdicionales = Enumerable.Range(0, 5)
                .Select(i => config.PorcentajesAdicionales.ElementAtOrDefault(i))
                .ToList()
        };
    }

    private static int ResolveAdicionalesHabilitados(Dictionary<string, string> values)
    {
        if (values.TryGetValue(PorcentajesAdicionalesHabilitadosConfigKey, out var raw))
            return ResolveAdicionalesHabilitados(raw);

        return 3;
    }

    private static int ResolveAdicionalesHabilitados(int value)
        => value > 0 ? Math.Min(5, value) : 3;

    private static int ResolveAdicionalesHabilitados(string? value)
    {
        if (int.TryParse((value ?? string.Empty).Trim(), out var parsed))
            return ResolveAdicionalesHabilitados(parsed);

        return 3;
    }

    private static string ResolveSucursal(Dictionary<string, string> values)
    {
        if (values.TryGetValue(SucursalConfigKey, out var sucursal) && !string.IsNullOrWhiteSpace(sucursal))
            return ResolveSucursal(sucursal);
        if (values.TryGetValue(LegacySucursalConfigKey, out var legacy) && !string.IsNullOrWhiteSpace(legacy))
            return ResolveSucursal(legacy);
        return "0001";
    }

    private static string ResolveSucursal(string? value)
        => string.IsNullOrWhiteSpace(value) ? "0001" : value.Trim().PadLeft(4, '0')[..4];

    private static string ResolveLetra(Dictionary<string, string> values)
        => values.TryGetValue(LetraConfigKey, out var letra) ? ResolveLetra(letra) : "X";

    private static string ResolveLetra(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "X" : value.Trim().ToUpperInvariant();
        return normalized[..1];
    }

    private static string ResolveChoferGeneral(Dictionary<string, string> values)
        => values.TryGetValue(ChoferGeneralConfigKey, out var choferGeneral) ? ResolveChoferGeneral(choferGeneral) : string.Empty;

    private static string ResolveChoferGeneral(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static int? ParseNullableBitFilter(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "" => null,
            "1" or "SI" or "S" or "TRUE" or "T" => 1,
            "0" or "NO" or "N" or "FALSE" or "F" => 0,
            _ => null
        };
    }

    private static string BuildViewConfigKey(string userName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant())));
        return $"{ViewConfigPrefix}{hash[..24]}";
    }

    private static string BuildTipoVehiculoViewConfigKey(string userName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant())));
        return $"{TipoVehiculoViewConfigPrefix}{hash[..24]}";
    }

    private static CargaViajesViewSettingsDto CreateDefaultViewSettings()
        => new()
        {
            AgruparPor = CargaViajesViewGroupKeys.None,
            Columnas =
            [
                new() { Key = CargaViajesViewColumnKeys.Fecha, Label = "Fecha", Visible = true, Order = 0 },
                new() { Key = CargaViajesViewColumnKeys.IdComprobante, Label = "IdComprobante", Visible = true, Order = 1 },
                new() { Key = CargaViajesViewColumnKeys.Cliente, Label = "Cliente", Visible = true, Order = 2 },
                new() { Key = CargaViajesViewColumnKeys.Destino, Label = "Destino", Visible = true, Order = 3 },
                new() { Key = CargaViajesViewColumnKeys.Chofer, Label = "Chofer", Visible = true, Order = 4 },
                new() { Key = CargaViajesViewColumnKeys.TipoVehiculo, Label = "Tipo vehÃ­culo", Visible = true, Order = 5 },
                new() { Key = CargaViajesViewColumnKeys.TotalCliente, Label = "Total cliente", Visible = true, Order = 6 },
                new() { Key = CargaViajesViewColumnKeys.TotalFletero, Label = "Total fletero", Visible = true, Order = 7 },
                new() { Key = CargaViajesViewColumnKeys.Estado, Label = "Estado", Visible = true, Order = 8 },
                new() { Key = CargaViajesViewColumnKeys.Usuario, Label = "Usuario", Visible = true, Order = 9 },
                new() { Key = CargaViajesViewColumnKeys.Alta, Label = "FechaHora alta", Visible = false, Order = 10 }
            ]
        };

    private static CargaViajesViewSettingsDto NormalizeViewSettings(CargaViajesViewSettingsDto? settings)
    {
        var defaults = CreateDefaultViewSettings();
        if (settings is null)
            return defaults;

        var incoming = settings.Columnas
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        var normalized = new CargaViajesViewSettingsDto
        {
            AgruparPor = settings.AgruparPor?.Trim() switch
            {
                CargaViajesViewGroupKeys.Estado => CargaViajesViewGroupKeys.Estado,
                CargaViajesViewGroupKeys.Usuario => CargaViajesViewGroupKeys.Usuario,
                _ => CargaViajesViewGroupKeys.None
            },
            Columnas = defaults.Columnas
                .Select(defaultCol =>
                {
                    if (!incoming.TryGetValue(defaultCol.Key, out var source))
                        return new CargaViajesViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = defaultCol.Visible, Order = defaultCol.Order };

                    return new CargaViajesViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = source.Visible, Order = source.Order };
                })
                .OrderBy(c => c.Order)
                .Select((col, idx) => { col.Order = idx; return col; })
                .ToList()
        };

        if (!normalized.Columnas.Any(c => c.Visible))
            normalized.Columnas[0].Visible = true;

        return normalized;
    }

    private static CargaViajeTipoVehiculoViewSettingsDto CreateDefaultTipoVehiculoViewSettings()
        => new()
        {
            AgruparPor = CargaViajeTipoVehiculoViewGroupKeys.None,
            Columnas =
            [
                new() { Key = CargaViajeTipoVehiculoViewColumnKeys.Codigo, Label = "CÃ³digo", Visible = true, Order = 0 },
                new() { Key = CargaViajeTipoVehiculoViewColumnKeys.Descripcion, Label = "DescripciÃ³n", Visible = true, Order = 1 },
                new() { Key = CargaViajeTipoVehiculoViewColumnKeys.Activo, Label = "Activo", Visible = true, Order = 2 }
            ]
        };

    private static CargaViajeTipoVehiculoViewSettingsDto NormalizeTipoVehiculoViewSettings(CargaViajeTipoVehiculoViewSettingsDto? settings)
    {
        var defaults = CreateDefaultTipoVehiculoViewSettings();
        if (settings is null)
            return defaults;

        var incoming = settings.Columnas
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        var normalized = new CargaViajeTipoVehiculoViewSettingsDto
        {
            AgruparPor = settings.AgruparPor?.Trim() switch
            {
                CargaViajeTipoVehiculoViewGroupKeys.Activo => CargaViajeTipoVehiculoViewGroupKeys.Activo,
                _ => CargaViajeTipoVehiculoViewGroupKeys.None
            },
            Columnas = defaults.Columnas
                .Select(defaultCol =>
                {
                    if (!incoming.TryGetValue(defaultCol.Key, out var source))
                        return new CargaViajeTipoVehiculoViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = defaultCol.Visible, Order = defaultCol.Order };

                    return new CargaViajeTipoVehiculoViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = source.Visible, Order = source.Order };
                })
                .OrderBy(c => c.Order)
                .Select((col, idx) => { col.Order = idx; return col; })
                .ToList()
        };

        if (!normalized.Columnas.Any(c => c.Visible))
            normalized.Columnas[0].Visible = true;

        return normalized;
    }

    private static string ResolveStoredValue(string? value, string? auxValue)
        => !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : (auxValue ?? string.Empty).Trim();

    private static async Task UpsertConfigValueAsync(SqlConnection cn, SqlTransaction tx, string detailColumn, string key, string value, string group, CancellationToken ct)
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
        cmd.Parameters.AddWithValue("@Grupo", group);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length > 150 ? (string.Empty, normalized) : (normalized, string.Empty);
    }

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private static string TrimUpper(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string BuildOrderByClause(
        string? sortBy,
        bool descending,
        IReadOnlyDictionary<string, string> map,
        string defaultClause)
    {
        var key = (sortBy ?? string.Empty).Trim().ToLowerInvariant();
        if (!map.TryGetValue(key, out var template))
            return defaultClause;

        var dir = descending ? "DESC" : "ASC";
        return template.Replace("{dir}", dir, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLiquidacionEstadoPago(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? CargaViajesLiquidacionEstadoPagoKeys.Pendientes.ToUpperInvariant()
            : value.Trim().ToUpperInvariant() switch
            {
                var v when string.Equals(v, CargaViajesLiquidacionEstadoPagoKeys.Pagados.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) => CargaViajesLiquidacionEstadoPagoKeys.Pagados.ToUpperInvariant(),
                var v when string.Equals(v, CargaViajesLiquidacionEstadoPagoKeys.Todos.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) => CargaViajesLiquidacionEstadoPagoKeys.Todos.ToUpperInvariant(),
                _ => CargaViajesLiquidacionEstadoPagoKeys.Pendientes.ToUpperInvariant()
            };

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END, name;
            """;

        var column = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, cancellationToken: ct));
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(@FullName, 'U') IS NULL THEN 0 ELSE 1 END;", new { FullName = $"dbo.{tableName}" }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct, SqlTransaction? tx = null)
    {
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(@FullName) AND UPPER(name) = UPPER(@ColumnName);",
            new { FullName = $"dbo.{tableName}", ColumnName = columnName },
            transaction: tx,
            cancellationToken: ct));
        return count > 0;
    }

    private static async Task<HashSet<string>> LoadColumnsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT LOWER(COLUMN_NAME)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = @TableName;
            """;
        var rows = await cn.QueryAsync<string>(new CommandDefinition(sql, new { TableName = tableName }, cancellationToken: ct));
        return rows.Select(x => x.Trim().ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string> ResolveExistingTableAsync(SqlConnection cn, CancellationToken ct, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (await TableExistsAsync(cn, candidate, ct))
                return candidate;
        }

        throw new InvalidOperationException($"No se encontrÃ³ ninguna de las tablas esperadas: {string.Join(", ", candidates)}.");
    }

    private static string FirstExistingColumn(HashSet<string> columns, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (columns.Contains(candidate.Trim().ToLowerInvariant()))
                return candidate;
        }

        throw new InvalidOperationException($"No se encontrÃ³ ninguna de las columnas esperadas: {string.Join(", ", candidates)}.");
    }

    private static async Task EnsureSchemaColumnAsync(SqlConnection cn, SqlTransaction tx, string tableName, string columnName, string columnDefinition, ICollection<string> createdColumns, CancellationToken ct)
    {
        if (await ColumnExistsAsync(cn, tableName, columnName, ct, tx))
            return;

        var sql = $"""
            ALTER TABLE dbo.{tableName}
            ADD {columnName} {columnDefinition};
            """;

        await cn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, cancellationToken: ct));
        createdColumns.Add($"{tableName}.{columnName}");
    }

    private static void AddColumnPair(List<string> columns, List<string> values, string? columnName, string valueSql)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return;

        columns.Add(columnName);
        values.Add(valueSql);
    }

    private static void AddUpdatePart(List<string> parts, string? columnName, string valueSql, bool rawSql = false)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return;

        parts.Add(rawSql ? $"{columnName} = {valueSql}" : $"{columnName} = {valueSql}");
    }

    private static async Task<bool> ExistsByCodeAsync(SqlConnection cn, string table, string column, string code, CancellationToken ct)
    {
        var sql = $"SELECT COUNT(1) FROM dbo.{table} WHERE UPPER(LTRIM(RTRIM({column}))) = @Code;";
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Code = code.Trim().ToUpperInvariant() }, cancellationToken: ct));
        return count > 0;
    }

    private static (string Apellido, string Nombres) SplitFullName(string nombre)
    {
        var value = (nombre ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return (string.Empty, string.Empty);

        var parts = value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 1
            ? (parts[0], string.Empty)
            : (parts[0], parts[1]);
    }

    private static decimal GetDecimal(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0m : Convert.ToDecimal(rd.GetValue(index));

    private static int GetInt(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt32(rd.GetValue(index));

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index));

    private async Task<T> ExecuteLoggedAsync<T>(string module, string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Operacion cancelada {Module}.{Action}", module, action);
            throw;
        }
        catch (AppValidationException)
        {
            throw;
        }
        catch (AppUserFacingException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private async Task ExecuteLoggedAsync(string module, string action, Func<CancellationToken, Task> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Operacion cancelada {Module}.{Action}", module, action);
            throw;
        }
        catch (AppValidationException)
        {
            throw;
        }
        catch (AppUserFacingException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private sealed class ListaTextoRow
    {
        public string ListaCodigo { get; set; } = string.Empty;
        public string ListaTexto { get; set; } = string.Empty;
    }
}





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
    ICargaViajesValidator validator) : ICargaViajesService
{
    private const string ModuleName = "CargaViajes";
    private const string ConfigGroup = "VIAJES";
    private const string ViewConfigPrefix = "USUVIEW-VIAJES-";
    private const string DefaultTc = "VJ";
    private const string SucursalConfigKey = "VIAJES-SUCURSAL";
    private const string LegacySucursalConfigKey = "CARGA_VIAJES_SUCURSAL";
    private const string LetraConfigKey = "VIAJES-LETRA";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<CargaViajesGridItemDto>> SearchViajesAsync(CargaViajesFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchViajes", async token =>
        {
            filters ??= new CargaViajesFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var viajeTable = await ResolveExistingTableAsync(cn, token, "MV_VIAJES_CARGA", "MV_VIAJES");
            var clienteTable = "Vt_Clientes";
            var choferTable = await ResolveExistingTableAsync(cn, token, "TA_CHOFERES", "MA_CHOFERES");
            var destinoTable = await ResolveExistingTableAsync(cn, token, "TA_DESTINOS", "V_TA_DESTINO");
            var vehiculoTable = await ResolveExistingTableAsync(cn, token, "TA_TIPOVEHICULO");
            var columns = await LoadColumnsAsync(cn, viajeTable, token);
            var isCarga = viajeTable.Equals("MV_VIAJES_CARGA", StringComparison.OrdinalIgnoreCase);
            var clienteCodeExpr = isCarga ? "ISNULL(v.IDCLIENTE, '')" : "ISNULL(v.Cliente, '')";
            var destinoCodeExpr = isCarga ? "ISNULL(v.IDDESTINO, '')" : "ISNULL(v.UNEGOCIO_DESTINO, '')";
            var choferCodeExpr = isCarga ? "ISNULL(v.IDCHOFER, '')" : "ISNULL(v.CHOFER, '')";
            var tipoVehiculoCodeExpr = isCarga ? "ISNULL(v.IDTIPOVEHICULO, '')" : "ISNULL(v.VEHICULO, '')";
            var destinoDescExpr = isCarga
                ? "ISNULL(v.DESCRIPCIONDESTINO, '')"
                : "ISNULL(v.UNEGOCIO_DESTINO, '')";
            var choferNameExpr = isCarga
                ? "ISNULL(v.NOMBRE_CHOFER, '')"
                : "ISNULL(v.NOMBRE_CHOFER, ISNULL(v.CHOFER, ''))";
            var vehiculoExpr = isCarga
                ? $"""ISNULL((SELECT TOP (1) LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) FROM dbo.{vehiculoTable} t WHERE UPPER(LTRIM(RTRIM(ISNULL(t.CODIGO, '')))) = UPPER(LTRIM(RTRIM({tipoVehiculoCodeExpr})))), '')"""
                : "ISNULL(v.VEHICULO, '')";
            var clienteJoin = $"LEFT JOIN dbo.{clienteTable} cli ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM({clienteCodeExpr})))";
            var totalFleteExpr = columns.Contains("total_flete")
                ? "ISNULL(v.TOTAL_FLETE, 0)"
                : columns.Contains("total_fletero")
                    ? "ISNULL(v.TOTAL_FLETERO, 0)"
                    : "0";
            var altaExpr = isCarga && (columns.Contains("fechahora_alta") || columns.Contains("fechahoraalta"))
                ? "ISNULL(v.FECHAHORA_ALTA, GETDATE())"
                : columns.Contains("fechahora_grabacion")
                    ? "ISNULL(v.FECHAHORA_GRABACION, GETDATE())"
                    : "GETDATE()";
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
                ORDER BY v.FECHA DESC, v.ID DESC
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

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
                  AND (@Estado = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.Estado, N'PENDIENTE')))) = @Estado)
                  AND (@Usuario = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.Usuario, '')))) = @Usuario)
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
            var viajeTable = await ResolveExistingTableAsync(cn, token, "MV_VIAJES_CARGA", "MV_VIAJES");
            var choferTable = await ResolveExistingTableAsync(cn, token, "TA_CHOFERES", "MA_CHOFERES");
            var vehiculoTable = await ResolveExistingTableAsync(cn, token, "TA_TIPOVEHICULO");
            var columns = await LoadColumnsAsync(cn, viajeTable, token);
            var clienteCodeExpr = columns.Contains("idcliente") ? "ISNULL(v.IDCLIENTE, '')" : "ISNULL(v.Cliente, '')";
            var destinoCodeExpr = columns.Contains("iddestino") ? "ISNULL(v.IDDESTINO, '')" : "ISNULL(v.Destino, '')";
            var choferCodeExpr = columns.Contains("idchofer") ? "ISNULL(v.IDCHOFER, '')" : "ISNULL(v.Chofer, '')";
            var tipoVehiculoCodeExpr = columns.Contains("idtipovehiculo") ? "ISNULL(v.IDTIPOVEHICULO, '')" : "ISNULL(v.TipoVehiculo, '')";
            var clienteJoin = $"LEFT JOIN dbo.Vt_Clientes cli ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM({clienteCodeExpr})))";
            var destinoDescExpr = columns.Contains("descripciondestino")
                ? "ISNULL(v.DESCRIPCIONDESTINO, '')"
                : "''";
            var choferNameExpr = columns.Contains("nombre_chofer")
                ? "ISNULL(v.NOMBRE_CHOFER, '')"
                : $"ISNULL(({BuildChoferNombreSql(choferTable, "v")}), '')";
            var tipoVehiculoDescExpr = $"""ISNULL((SELECT TOP (1) LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) FROM dbo.{vehiculoTable} t WHERE UPPER(LTRIM(RTRIM(ISNULL(t.CODIGO, '')))) = UPPER(LTRIM(RTRIM({tipoVehiculoCodeExpr})))), '')""";
            var totalFleteExpr = columns.Contains("total_flete")
                ? "ISNULL(v.TOTAL_FLETE, 0)"
                : columns.Contains("total_fletero")
                    ? "ISNULL(v.TOTAL_FLETERO, 0)"
                    : "0";
            var altaExpr = columns.Contains("fechahora_alta")
                ? "ISNULL(v.FECHAHORA_ALTA, GETDATE())"
                : columns.Contains("fechahora_grabacion")
                    ? "ISNULL(v.FECHAHORA_GRABACION, GETDATE())"
                    : "GETDATE()";
            var listaExpr = columns.Contains("idlista")
                ? "ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(50), v.IDLISTA))), '')"
                : columns.Contains("idlistarmtrf")
                    ? "ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(50), v.IDLISTARMTRF))), '')"
                    : "''";
            var peajeExpr = columns.Contains("total_peaje")
                ? "ISNULL(v.TOTAL_PEAJE, 0)"
                : columns.Contains("peaje")
                    ? "ISNULL(v.PEAJE, 0)"
                    : "0";
            var cantidadViajesExpr = columns.Contains("total_viajes")
                ? "ISNULL(v.TOTAL_VIAJES, 1)"
                : columns.Contains("cantidad_viajes")
                    ? "ISNULL(v.CANTIDAD_VIAJES, 1)"
                    : columns.Contains("cantidadviajes")
                        ? "ISNULL(v.CANTIDADVIAJES, 1)"
                        : "1";
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
                    ISNULL(v.TOTAL_IMPORTE, 0) AS TotalCliente,
                    {totalFleteExpr} AS TotalFletero,
                    ISNULL(v.ESTADO, N'PENDIENTE') AS Estado,
                    ISNULL(v.USUARIO, '') AS Usuario,
                    {altaExpr} AS FechaHoraAlta,
                    {listaExpr} AS Lista,
                    {peajeExpr} AS Peaje,
                    {cantidadViajesExpr} AS CantidadViajes,
                    ISNULL(v.PORCENTAJE_ADIC, 0) AS PorcentajeAdic,
                    ISNULL(v.PORCENTAJE_ADIC1, 0) AS PorcentajeAdic1,
                    ISNULL(v.PORCENTAJE_ADIC2, 0) AS PorcentajeAdic2,
                    ISNULL(v.PORCENTAJE_ADIC3, 0) AS PorcentajeAdic3,
                    ISNULL(v.PORCENTAJE_ADIC4, 0) AS PorcentajeAdic4,
                    ISNULL(v.TOTAL_ADIC, 0) AS TotalAdic,
                    ISNULL(v.TOTAL_ADIC1, 0) AS TotalAdic1,
                    ISNULL(v.TOTAL_ADIC2, 0) AS TotalAdic2,
                    ISNULL(v.TOTAL_ADIC3, 0) AS TotalAdic3,
                    ISNULL(v.TOTAL_ADIC4, 0) AS TotalAdic4,
                    ISNULL(v.TOTAL_ADICIONALES, 0) AS TotalAdicionales,
                    ISNULL(v.OBSERVACIONES, '') AS Observaciones
                FROM dbo.{viajeTable} v
                {clienteJoin}
                WHERE v.ID = @Id;
                """;

            var item = await cn.QuerySingleOrDefaultAsync<CargaViajesDetailDto>(new CommandDefinition(sql, new { Id = id, Tc = DefaultTc }, cancellationToken: token));
            if (item is null)
                return null;

            item.IdListaRMTRF = (await GetListaRMTRFAsync(cn, item.ClienteCodigo, token)).ListaCodigo;
            item.Lista = item.IdListaRMTRF > 0 ? item.IdListaRMTRF.ToString("0") : string.Empty;
            return item;
        }, "No se pudo cargar el viaje seleccionado.", ct);

    public Task<int> SaveViajeAsync(CargaViajeSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveViaje", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Tc = string.IsNullOrWhiteSpace(request.Tc) ? DefaultTc : request.Tc.Trim().ToUpperInvariant();
            if (!string.Equals(request.Tc, DefaultTc, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El TC del módulo de carga de viajes es fijo y debe ser VJ.");

            var validation = await validator.ValidateViajeForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException("Revisá los datos del viaje antes de guardar.", validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var viajeTable = await ResolveExistingTableAsync(cn, token, "MV_VIAJES_CARGA", "MV_VIAJES");
            var columns = await LoadColumnsAsync(cn, viajeTable, token);
            var hasId = columns.Contains("id");
            var hasIdLista = columns.Contains("idlista");
            var hasIdListaAlt = columns.Contains("idlistarmtrf");
            var hasCliente = columns.Contains("cliente") || columns.Contains("idcliente");
            var hasDestino = columns.Contains("destino") || columns.Contains("iddestino");
            var hasChofer = columns.Contains("chofer") || columns.Contains("idchofer");
            var hasVehiculo = columns.Contains("tipovehiculo") || columns.Contains("idtipovehiculo");
            var hasTotalFlete = columns.Contains("total_flete") || columns.Contains("total_fletero");
            var hasCantidadViajes = columns.Contains("total_viajes") || columns.Contains("cantidad_viajes") || columns.Contains("cantidadviajes");
            var hasAdics = columns.Contains("porcentaje_adic") || columns.Contains("total_adic") || columns.Contains("total_adicionales");
            var hasEstado = columns.Contains("estado");
            var hasUsuario = columns.Contains("usuario");
            var hasGrabacion = columns.Contains("fechahora_grabacion") || columns.Contains("fechahoraalta") || columns.Contains("fechahora_alta");

            var clienteLista = await GetListaRMTRFAsync(cn, request.Cliente, token);
            var nextIdComp = string.IsNullOrWhiteSpace(request.IdComprobante)
                ? await GetNextIdComprobanteAsync(token)
                : request.IdComprobante.Trim();
            var totals = CalculateTotals(request);

            var isNew = !request.Id.HasValue || request.Id.Value <= 0;
            var parameters = new DynamicParameters();
            parameters.Add("@Id", request.Id);
            parameters.Add("@Tc", request.Tc);
            parameters.Add("@IdComprobante", nextIdComp);
            parameters.Add("@Fecha", request.Fecha);
            parameters.Add("@Cliente", request.Cliente.Trim());
            parameters.Add("@Destino", request.Destino.Trim());
            parameters.Add("@Chofer", request.Chofer.Trim());
            parameters.Add("@TipoVehiculo", request.TipoVehiculo.Trim());
            parameters.Add("@IdLista", clienteLista.ListaCodigo);
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
            parameters.Add("@Observaciones", (request.Observaciones ?? string.Empty).Trim());
            parameters.Add("@Estado", string.IsNullOrWhiteSpace(request.Estado) ? CargaViajeEstadoKeys.Pendiente : request.Estado.Trim().ToUpperInvariant());
            parameters.Add("@Usuario", NormalizeUser(request.UsuarioAccion));

            if (isNew)
            {
                var insertColumns = new List<string>();
                var insertValues = new List<string>();
                AddColumnPair(insertColumns, insertValues, hasId ? "ID" : null, "@Id");
                AddColumnPair(insertColumns, insertValues, "TC", "@Tc");
                AddColumnPair(insertColumns, insertValues, "IDCOMPROBANTE", "@IdComprobante");
                AddColumnPair(insertColumns, insertValues, "FECHA", "@Fecha");
                AddColumnPair(insertColumns, insertValues, hasCliente ? FirstExistingColumn(columns, "CLIENTE", "IDCLIENTE") : null, "@Cliente");
                AddColumnPair(insertColumns, insertValues, hasDestino ? FirstExistingColumn(columns, "DESTINO", "IDDESTINO") : null, "@Destino");
                AddColumnPair(insertColumns, insertValues, hasChofer ? FirstExistingColumn(columns, "CHOFER", "IDCHOFER") : null, "@Chofer");
                AddColumnPair(insertColumns, insertValues, hasVehiculo ? FirstExistingColumn(columns, "TIPOVEHICULO", "IDTIPOVEHICULO") : null, "@TipoVehiculo");
                AddColumnPair(insertColumns, insertValues, hasIdLista ? "IDLISTA" : hasIdListaAlt ? "IDLISTARMTRF" : null, "@IdLista");
                AddColumnPair(insertColumns, insertValues, "TOTAL_IMPORTE", "@TotalImporte");
                AddColumnPair(insertColumns, insertValues, hasTotalFlete ? FirstExistingColumn(columns, "TOTAL_FLETE", "TOTAL_FLETERO") : null, "@TotalFlete");
                AddColumnPair(insertColumns, insertValues, hasAdics ? FirstExistingColumn(columns, "TOTAL_PEAJE", "PEAJE") : null, "@Peaje");
                AddColumnPair(insertColumns, insertValues, hasCantidadViajes ? FirstExistingColumn(columns, "TOTAL_VIAJES", "CANTIDAD_VIAJES", "CANTIDADVIAJES") : null, "@CantidadViajes");
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
                AddColumnPair(insertColumns, insertValues, hasUsuario ? "USUARIO" : null, "@Usuario");
                AddColumnPair(insertColumns, insertValues, hasGrabacion ? FirstExistingColumn(columns, "FECHAHORA_GRABACION", "FECHAHORA_ALTA", "FECHAHORAALTA", "FECHAHORA_MODIFICACION") : null, "GETDATE()");
                AddColumnPair(insertColumns, insertValues, columns.Contains("observaciones") ? "OBSERVACIONES" : null, "@Observaciones");

                var insertSql = $"""
                    INSERT INTO dbo.{viajeTable}
                    ({string.Join(", ", insertColumns)})
                    VALUES
                    ({string.Join(", ", insertValues)});
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                return await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, parameters, cancellationToken: token));
            }

            var updateParts = new List<string>();
            AddUpdatePart(updateParts, hasCliente ? FirstExistingColumn(columns, "CLIENTE", "IDCLIENTE") : null, "@Cliente");
            AddUpdatePart(updateParts, hasDestino ? FirstExistingColumn(columns, "DESTINO", "IDDESTINO") : null, "@Destino");
            AddUpdatePart(updateParts, hasChofer ? FirstExistingColumn(columns, "CHOFER", "IDCHOFER") : null, "@Chofer");
            AddUpdatePart(updateParts, hasVehiculo ? FirstExistingColumn(columns, "TIPOVEHICULO", "IDTIPOVEHICULO") : null, "@TipoVehiculo");
            AddUpdatePart(updateParts, hasIdLista ? "IDLISTA" : hasIdListaAlt ? "IDLISTARMTRF" : null, "@IdLista");
            AddUpdatePart(updateParts, "FECHA", "@Fecha");
            AddUpdatePart(updateParts, "TOTAL_IMPORTE", "@TotalImporte");
            AddUpdatePart(updateParts, hasTotalFlete ? FirstExistingColumn(columns, "TOTAL_FLETE", "TOTAL_FLETERO") : null, "@TotalFlete");
            AddUpdatePart(updateParts, columns.Contains("total_peaje") ? "TOTAL_PEAJE" : columns.Contains("peaje") ? "PEAJE" : null, "@Peaje");
            AddUpdatePart(updateParts, columns.Contains("total_viajes") ? "TOTAL_VIAJES" : columns.Contains("cantidad_viajes") ? "CANTIDAD_VIAJES" : columns.Contains("cantidadviajes") ? "CANTIDADVIAJES" : null, "@CantidadViajes");
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
            AddUpdatePart(updateParts, hasUsuario ? "USUARIO" : null, "@Usuario");
            AddUpdatePart(updateParts, columns.Contains("observaciones") ? "OBSERVACIONES" : null, "@Observaciones");
            if (hasGrabacion)
                AddUpdatePart(updateParts, FirstExistingColumn(columns, "FECHAHORA_MODIFICACION", "FECHAHORA_GRABACION", "FECHAHORA_ALTA", "FECHAHORAALTA"), "GETDATE()", rawSql: true);

            var updateSql = $"""
                UPDATE dbo.{viajeTable}
                SET {string.Join(", ", updateParts)}
                WHERE ID = @Id;
                """;
            await cn.ExecuteAsync(new CommandDefinition(updateSql, parameters, cancellationToken: token));
            return request.Id!.Value;
        }, "No se pudo guardar el viaje.", ct);

    public Task AnularViajeAsync(int id, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AnularViaje", async token =>
        {
            if (id <= 0)
                throw new InvalidOperationException("No se recibió el viaje a anular.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var viajeTable = await ResolveExistingTableAsync(cn, token, "MV_VIAJES_CARGA", "MV_VIAJES");
            var columns = await LoadColumnsAsync(cn, viajeTable, token);
            var hasEstado = columns.Contains("estado");
            var hasUsuario = columns.Contains("usuario");
            var grabacionColumn = columns.Contains("fechahora_modificacion") || columns.Contains("fechahora_grabacion") || columns.Contains("fechahora_alta")
                ? FirstExistingColumn(columns, "FECHAHORA_MODIFICACION", "FECHAHORA_GRABACION", "FECHAHORA_ALTA", "FECHAHORAALTA")
                : null;
            var updateParts = new List<string>();
            if (hasEstado)
                updateParts.Add("ESTADO = @Estado");
            if (hasUsuario)
                updateParts.Add("USUARIO = @Usuario");
            if (columns.Contains("anulado"))
                updateParts.Add("ANULADO = 1");
            if (columns.Contains("fechahora_modificacion"))
                updateParts.Add("FECHAHORA_MODIFICACION = GETDATE()");
            if (grabacionColumn is not null)
                updateParts.Add($"{grabacionColumn} = GETDATE()");

            var sql = $"UPDATE dbo.{viajeTable} SET {string.Join(", ", updateParts)} WHERE ID = @Id;";
            await cn.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Estado = CargaViajeEstadoKeys.Anulado, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
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
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            var tarifaFleteroFiltro = ParseNullableBitFilter(filters.TarifaFletero);
            var activoFiltro = ParseNullableBitFilter(filters.Activo);
            var textoLike = SearchTextHelper.LikeContains(filters.Texto);

            var sql = $"""
                SELECT
                    LTRIM(RTRIM(ISNULL({idListaColumn}, ''))),
                    ISNULL(Nombre, ''),
                    ISNULL(Importe, 0),
                    ISNULL({clienteColumn}, ''),
                    ISNULL({choferColumn}, ''),
                    ISNULL({destinoColumn}, ''),
                    ISNULL({tipoVehiculoColumn}, ''),
                    ISNULL(TarifaFletero, 0),
                    ISNULL(PorcentajeAdic, 0),
                    ISNULL(PorcentajeAdic1, 0),
                    ISNULL(PorcentajeAdic2, 0),
                    ISNULL(PorcentajeAdic3, 0),
                    ISNULL(PorcentajeAdic4, 0),
                    ISNULL(Activo, 1)
                FROM dbo.TA_TARIFA
                WHERE (
                        @TextoLike = ''
                        OR {idListaColumn} LIKE @TextoLike
                        OR Nombre COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR {clienteColumn} LIKE @TextoLike
                        OR {choferColumn} LIKE @TextoLike
                        OR {destinoColumn} LIKE @TextoLike
                        OR {tipoVehiculoColumn} LIKE @TextoLike
                    )
                  AND (@Cliente = '' OR {clienteColumn} LIKE @ClienteLike)
                  AND (@Chofer = '' OR {choferColumn} LIKE @ChoferLike)
                  AND (@Destino = '' OR {destinoColumn} LIKE @DestinoLike)
                  AND (@TipoVehiculo = '' OR {tipoVehiculoColumn} LIKE @TipoVehiculoLike)
                  AND (@TarifaFletero IS NULL OR ISNULL(TarifaFletero, 0) = @TarifaFletero)
                  AND (@Activo IS NULL OR ISNULL(Activo, 1) = @Activo)
                ORDER BY Nombre, {idListaColumn}
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.TA_TARIFA
                WHERE (
                        @TextoLike = ''
                        OR {idListaColumn} LIKE @TextoLike
                        OR Nombre COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR {clienteColumn} LIKE @TextoLike
                        OR {choferColumn} LIKE @TextoLike
                        OR {destinoColumn} LIKE @TextoLike
                        OR {tipoVehiculoColumn} LIKE @TextoLike
                    )
                  AND (@Cliente = '' OR {clienteColumn} LIKE @ClienteLike)
                  AND (@Chofer = '' OR {choferColumn} LIKE @ChoferLike)
                  AND (@Destino = '' OR {destinoColumn} LIKE @DestinoLike)
                  AND (@TipoVehiculo = '' OR {tipoVehiculoColumn} LIKE @TipoVehiculoLike)
                  AND (@TarifaFletero IS NULL OR ISNULL(TarifaFletero, 0) = @TarifaFletero)
                  AND (@Activo IS NULL OR ISNULL(Activo, 1) = @Activo);
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
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                rows.Add(new CargaViajeTarifaGridItemDto
                {
                    IdLista = GetString(rd, 0),
                    Nombre = GetString(rd, 1),
                    Importe = GetDecimal(rd, 2),
                    Cliente = GetString(rd, 3),
                    Chofer = GetString(rd, 4),
                    Destino = GetString(rd, 5),
                    TipoVehiculo = GetString(rd, 6),
                    TarifaFletero = GetBool(rd, 7),
                    PorcentajeAdic = GetDecimal(rd, 8),
                    PorcentajeAdic1 = GetDecimal(rd, 9),
                    PorcentajeAdic2 = GetDecimal(rd, 10),
                    PorcentajeAdic3 = GetDecimal(rd, 11),
                    PorcentajeAdic4 = GetDecimal(rd, 12),
                    Activo = GetBool(rd, 13)
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
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            var sql = $"""
                SELECT TOP (1)
                    LTRIM(RTRIM(ISNULL({idListaColumn}, ''))),
                    ISNULL(Nombre, ''),
                    ISNULL(Importe, 0),
                    ISNULL({clienteColumn}, ''),
                    ISNULL({choferColumn}, ''),
                    ISNULL({destinoColumn}, ''),
                    ISNULL({tipoVehiculoColumn}, ''),
                    ISNULL(TarifaFletero, 0),
                    ISNULL(PorcentajeAdic, 0),
                    ISNULL(PorcentajeAdic1, 0),
                    ISNULL(PorcentajeAdic2, 0),
                    ISNULL(PorcentajeAdic3, 0),
                    ISNULL(PorcentajeAdic4, 0),
                    ISNULL(Activo, 1)
                FROM dbo.TA_TARIFA
                WHERE UPPER(LTRIM(RTRIM({idListaColumn}))) = @IdLista;
                """;

            var row = await cn.QuerySingleOrDefaultAsync<CargaViajeTarifaGridItemDto>(new CommandDefinition(sql, new { IdLista = idLista.Trim().ToUpperInvariant() }, cancellationToken: token));
            return row;
        }, "No se pudo cargar la tarifa seleccionada.", ct);

    public Task<string> SaveTarifaAsync(CargaViajeTarifaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveTarifa", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var validation = await validator.ValidateTarifaForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException("Revisá los datos de la tarifa antes de guardar.", validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                throw new InvalidOperationException("La tabla TA_TARIFA no existe en la base activa.");

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var idListaColumn = FirstExistingColumn(columns, "IDLISTA", "ID_LISTA");
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");

            var isNew = string.IsNullOrWhiteSpace(request.IdLista);
            var idLista = request.IdLista.Trim().ToUpperInvariant();
            var sql = isNew
                ? $"""
                INSERT INTO dbo.TA_TARIFA
                (
                    {idListaColumn}, Nombre, Importe, {clienteColumn}, {choferColumn}, {destinoColumn}, {tipoVehiculoColumn},
                    TarifaFletero, PorcentajeAdic, PorcentajeAdic1, PorcentajeAdic2, PorcentajeAdic3, PorcentajeAdic4, Activo
                )
                VALUES
                (
                    @IdLista, @Nombre, @Importe, @Cliente, @Chofer, @Destino, @TipoVehiculo,
                    @TarifaFletero, @PorcentajeAdic, @PorcentajeAdic1, @PorcentajeAdic2, @PorcentajeAdic3, @PorcentajeAdic4, @Activo
                );
                """
                : $"""
                UPDATE dbo.TA_TARIFA
                SET
                    Nombre = @Nombre,
                    Importe = @Importe,
                    {clienteColumn} = @Cliente,
                    {choferColumn} = @Chofer,
                    {destinoColumn} = @Destino,
                    {tipoVehiculoColumn} = @TipoVehiculo,
                    TarifaFletero = @TarifaFletero,
                    PorcentajeAdic = @PorcentajeAdic,
                    PorcentajeAdic1 = @PorcentajeAdic1,
                    PorcentajeAdic2 = @PorcentajeAdic2,
                    PorcentajeAdic3 = @PorcentajeAdic3,
                    PorcentajeAdic4 = @PorcentajeAdic4,
                    Activo = @Activo
                WHERE UPPER(LTRIM(RTRIM({idListaColumn}))) = @IdLista;
                """;

            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                IdLista = idLista,
                Nombre = request.Nombre.Trim(),
                request.Importe,
                Cliente = request.Cliente.Trim(),
                Chofer = request.Chofer.Trim(),
                Destino = request.Destino.Trim(),
                TipoVehiculo = request.TipoVehiculo.Trim(),
                TarifaFletero = request.TarifaFletero,
                request.PorcentajeAdic,
                request.PorcentajeAdic1,
                request.PorcentajeAdic2,
                request.PorcentajeAdic3,
                request.PorcentajeAdic4,
                request.Activo
            }, cancellationToken: token));

            return idLista;
        }, "No se pudo guardar la tarifa.", ct);

    public Task BajaTarifaAsync(string idLista, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "BajaTarifa", async token =>
        {
            if (string.IsNullOrWhiteSpace(idLista))
                throw new InvalidOperationException("No se recibió la tarifa a dar de baja.");

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

    public Task<PagedResult<CargaViajeChoferGridItemDto>> SearchChoferesAsync(CargaViajesFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchChoferes", async token =>
        {
            filters ??= new CargaViajesFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var table = await ResolveExistingTableAsync(cn, token, "TA_CHOFERES", "MA_CHOFERES");
            var sql = table.Equals("MA_CHOFERES", StringComparison.OrdinalIgnoreCase)
                ? $"""
                    SELECT
                        LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                        LTRIM(RTRIM(CONCAT(ISNULL(APELLIDO, ''), CASE WHEN ISNULL(NOMBRES, '') = '' THEN '' ELSE ' ' + NOMBRES END))) AS Nombre,
                        ISNULL(ACTIVO, 1) AS Activo
                    FROM dbo.{table}
                    WHERE (
                            @TextoLike = ''
                            OR CODIGO LIKE @TextoLike
                            OR APELLIDO COLLATE Latin1_General_CI_AI LIKE @TextoLike
                            OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        )
                    ORDER BY APELLIDO, NOMBRES, CODIGO
                    OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                    SELECT COUNT(*)
                    FROM dbo.{table}
                    WHERE (
                            @TextoLike = ''
                            OR CODIGO LIKE @TextoLike
                            OR APELLIDO COLLATE Latin1_General_CI_AI LIKE @TextoLike
                            OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        );
                    """
                : $"""
                    SELECT
                        LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                        LTRIM(RTRIM(ISNULL(NOMBRES, ''))) AS Nombre,
                        ISNULL(ACTIVO, 1) AS Activo
                    FROM dbo.{table}
                    WHERE (
                            @TextoLike = ''
                            OR CODIGO LIKE @TextoLike
                            OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        )
                    ORDER BY NOMBRES, CODIGO
                    OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                    SELECT COUNT(*)
                    FROM dbo.{table}
                    WHERE (
                            @TextoLike = ''
                            OR CODIGO LIKE @TextoLike
                            OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        );
                    """;

            var rows = new List<CargaViajeChoferGridItemDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@TextoLike", SearchTextHelper.LikeContains(filters.Chofer));
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                rows.Add(new CargaViajeChoferGridItemDto
                {
                    Codigo = GetString(rd, 0),
                    Nombre = GetString(rd, 1),
                    Activo = GetBool(rd, 2)
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
            var table = await ResolveExistingTableAsync(cn, token, "TA_CHOFERES", "MA_CHOFERES");
            var sql = table.Equals("MA_CHOFERES", StringComparison.OrdinalIgnoreCase)
                ? $"""
                    SELECT TOP (1)
                        LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                        LTRIM(RTRIM(CONCAT(ISNULL(APELLIDO, ''), CASE WHEN ISNULL(NOMBRES, '') = '' THEN '' ELSE ' ' + NOMBRES END))) AS Nombre,
                        ISNULL(ACTIVO, 1) AS Activo
                    FROM dbo.{table}
                    WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                    """
                : $"""
                    SELECT TOP (1)
                        LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                        LTRIM(RTRIM(ISNULL(NOMBRES, ''))) AS Nombre,
                        ISNULL(ACTIVO, 1) AS Activo
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
                throw new AppValidationException("Revisá los datos del chofer antes de guardar.", validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var table = await ResolveExistingTableAsync(cn, token, "TA_CHOFERES", "MA_CHOFERES");
            var isMaestro = table.Equals("MA_CHOFERES", StringComparison.OrdinalIgnoreCase);
            var isNew = !await ExistsByCodeAsync(cn, table, "CODIGO", request.Codigo, token);

            var nameParts = SplitFullName(request.Nombre);
            var sql = isNew
                ? isMaestro
                    ? $"""
                        INSERT INTO dbo.{table} (CODIGO, APELLIDO, NOMBRES, ACTIVO, DISPONIBLE)
                        VALUES (@Codigo, @Apellido, @Nombres, @Activo, 0);
                        """
                    : $"""
                        INSERT INTO dbo.{table} (CODIGO, NOMBRES, ACTIVO)
                        VALUES (@Codigo, @Nombre, @Activo);
                        """
                : isMaestro
                    ? $"""
                        UPDATE dbo.{table}
                        SET APELLIDO = @Apellido, NOMBRES = @Nombres, ACTIVO = @Activo
                        WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                        """
                    : $"""
                        UPDATE dbo.{table}
                        SET NOMBRES = @Nombre, ACTIVO = @Activo
                        WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
                        """;

            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Codigo = request.Codigo.Trim().ToUpperInvariant(),
                Nombre = request.Nombre.Trim(),
                Apellido = nameParts.Apellido,
                Nombres = nameParts.Nombres,
                request.Activo
            }, cancellationToken: token));

            return request.Codigo.Trim().ToUpperInvariant();
        }, "No se pudo guardar el chofer.", ct);

    public Task BajaChoferAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "BajaChofer", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se recibió el chofer a dar de baja.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var table = await ResolveExistingTableAsync(cn, token, "TA_CHOFERES", "MA_CHOFERES");
            var sql = table.Equals("MA_CHOFERES", StringComparison.OrdinalIgnoreCase)
                ? $"UPDATE dbo.{table} SET ACTIVO = 0 WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;"
                : $"UPDATE dbo.{table} SET ACTIVO = 0 WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;";

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
            var table = await ResolveExistingTableAsync(cn, token, "TA_DESTINOS", "V_TA_DESTINO");
            var codeColumn = table.Equals("TA_DESTINOS", StringComparison.OrdinalIgnoreCase) ? "CODIGO" : "IdDestino";
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
                ORDER BY Descripcion, {codeColumn}
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
            cmd.Parameters.AddWithValue("@TextoLike", SearchTextHelper.LikeContains(filters.Cliente));
            cmd.Parameters.AddWithValue("@Activo", DBNull.Value);
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
            var table = await ResolveExistingTableAsync(cn, token, "TA_DESTINOS", "V_TA_DESTINO");
            var codeColumn = table.Equals("TA_DESTINOS", StringComparison.OrdinalIgnoreCase) ? "CODIGO" : "IdDestino";
            var sql = $"""
                SELECT TOP (1)
                    LTRIM(RTRIM(ISNULL({codeColumn}, ''))),
                    LTRIM(RTRIM(ISNULL(Descripcion, ''))),
                    ISNULL(Activo, 1)
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
                throw new AppValidationException("Revisá los datos del destino antes de guardar.", validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var table = await ResolveExistingTableAsync(cn, token, "TA_DESTINOS", "V_TA_DESTINO");
            var codeColumn = table.Equals("TA_DESTINOS", StringComparison.OrdinalIgnoreCase) ? "CODIGO" : "IdDestino";
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

    public Task BajaDestinoAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "BajaDestino", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se recibió el destino a dar de baja.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var table = await ResolveExistingTableAsync(cn, token, "TA_DESTINOS", "V_TA_DESTINO");
            var codeColumn = table.Equals("TA_DESTINOS", StringComparison.OrdinalIgnoreCase) ? "CODIGO" : "IdDestino";
            if (!await ColumnExistsAsync(cn, table, "Activo", token))
                throw new InvalidOperationException($"La tabla {table} no tiene columna Activo para hacer baja lógica.");

            var affected = await cn.ExecuteAsync(new CommandDefinition($"""
                UPDATE dbo.{table}
                SET Activo = 0
                WHERE UPPER(LTRIM(RTRIM({codeColumn}))) = @Codigo;
                """, new { Codigo = codigo.Trim().ToUpperInvariant() }, cancellationToken: token));
            if (affected == 0)
                throw new InvalidOperationException("El destino seleccionado ya no existe en la base activa.");
        }, "No se pudo dar de baja el destino.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchClientes", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            if (search.Length < 2)
                return (IReadOnlyList<CargaViajeLookupOptionDto>)Array.Empty<CargaViajeLookupOptionDto>();

            const string sql = """
                SELECT TOP (12)
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))),
                    ISNULL(RAZON_SOCIAL, ''),
                    ISNULL(IdListaRMTRF, '')
                FROM dbo.Vt_Clientes
                WHERE LTRIM(RTRIM(ISNULL(CODIGO, ''))) <> ''
                  AND (
                        CODIGO LIKE @Search
                        OR RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @Search
                      )
                ORDER BY RAZON_SOCIAL;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = SearchTextHelper.LikeContains(search) }, cancellationToken: token))).ToList();
            return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
        }, "No se pudieron buscar clientes.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchChoferLookupAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchChoferLookup", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            if (search.Length < 1)
                return (IReadOnlyList<CargaViajeLookupOptionDto>)Array.Empty<CargaViajeLookupOptionDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var table = await ResolveExistingTableAsync(cn, token, "TA_CHOFERES", "MA_CHOFERES");
            var sql = table.Equals("MA_CHOFERES", StringComparison.OrdinalIgnoreCase)
                ? $"""
                    SELECT TOP (12)
                        LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                        LTRIM(RTRIM(CONCAT(ISNULL(APELLIDO, ''), CASE WHEN ISNULL(NOMBRES, '') = '' THEN '' ELSE ' ' + NOMBRES END))) AS Titulo,
                        '' AS Subtitulo
                    FROM dbo.{table}
                    WHERE CODIGO LIKE @Search
                       OR APELLIDO COLLATE Latin1_General_CI_AI LIKE @Search
                       OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @Search
                    ORDER BY APELLIDO, NOMBRES, CODIGO;
                    """
                : $"""
                    SELECT TOP (12)
                        LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                        LTRIM(RTRIM(ISNULL(NOMBRES, ''))) AS Titulo,
                        '' AS Subtitulo
                    FROM dbo.{table}
                    WHERE CODIGO LIKE @Search
                       OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @Search
                    ORDER BY NOMBRES, CODIGO;
                    """;

            var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = SearchTextHelper.LikeContains(search) }, cancellationToken: token))).ToList();
            return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
        }, "No se pudieron buscar choferes.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchDestinosLookupAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchDestinosLookup", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            if (search.Length < 1)
                return (IReadOnlyList<CargaViajeLookupOptionDto>)Array.Empty<CargaViajeLookupOptionDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var table = await ResolveExistingTableAsync(cn, token, "TA_DESTINOS", "V_TA_DESTINO");
            var codeColumn = table.Equals("TA_DESTINOS", StringComparison.OrdinalIgnoreCase) ? "CODIGO" : "IdDestino";
            var sql = $"""
                SELECT TOP (12)
                    LTRIM(RTRIM(ISNULL({codeColumn}, ''))),
                    LTRIM(RTRIM(ISNULL(Descripcion, ''))),
                    ''
                FROM dbo.{table}
                WHERE {codeColumn} LIKE @Search
                   OR Descripcion COLLATE Latin1_General_CI_AI LIKE @Search
                ORDER BY Descripcion, {codeColumn};
                """;

            var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = SearchTextHelper.LikeContains(search) }, cancellationToken: token))).ToList();
            return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
        }, "No se pudieron buscar destinos.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchTipoVehiculosLookupAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchTipoVehiculosLookup", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            if (search.Length < 1)
                return (IReadOnlyList<CargaViajeLookupOptionDto>)Array.Empty<CargaViajeLookupOptionDto>();

            const string sql = """
                SELECT TOP (12)
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))),
                    LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))),
                    ''
                FROM dbo.TA_TIPOVEHICULO
                WHERE CODIGO LIKE @Search
                   OR DESCRIPCION COLLATE Latin1_General_CI_AI LIKE @Search
                ORDER BY DESCRIPCION, CODIGO;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = (await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition(sql, new { Search = SearchTextHelper.LikeContains(search) }, cancellationToken: token))).ToList();
            return (IReadOnlyList<CargaViajeLookupOptionDto>)rows;
        }, "No se pudieron buscar tipos de vehículo.", ct);

    public Task<decimal> GetTarifaClienteAsync(string cliente, string destino, string tipoVehiculo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTarifaCliente", async token =>
        {
            if (string.IsNullOrWhiteSpace(cliente) || string.IsNullOrWhiteSpace(destino) || string.IsNullOrWhiteSpace(tipoVehiculo))
                return 0m;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                return 0m;

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var clienteColumn = FirstExistingColumn(columns, "IDCLIENTE", "CLIENTE");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            return await ResolveTarifaImporteAsync(cn, clienteColumn, cliente, destinoColumn, destino, tipoVehiculoColumn, tipoVehiculo, 0, token);
        }, "No se pudo calcular la tarifa del cliente.", ct);

    public Task<decimal> GetTarifaFleteroAsync(string chofer, string destino, string tipoVehiculo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTarifaFletero", async token =>
        {
            if (string.IsNullOrWhiteSpace(chofer) || string.IsNullOrWhiteSpace(destino) || string.IsNullOrWhiteSpace(tipoVehiculo))
                return 0m;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                return 0m;

            var columns = await LoadColumnsAsync(cn, "TA_TARIFA", token);
            var choferColumn = FirstExistingColumn(columns, "IDCHOFER", "CHOFER");
            var destinoColumn = FirstExistingColumn(columns, "IDDESTINO", "DESTINO");
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            return await ResolveTarifaImporteAsync(cn, choferColumn, chofer, destinoColumn, destino, tipoVehiculoColumn, tipoVehiculo, 1, token);
        }, "No se pudo calcular la tarifa del fletero.", ct);

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
        }, "No se pudieron cargar los datos auxiliares del módulo.", ct);
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
        }, "No se pudo cargar la configuración de vista.", ct);

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

            await appEvents.LogAuditAsync(ModuleName, "SaveViewSettings", "TA_CONFIGURACION", configKey, "Configuración de vista de viajes actualizada.", new { UserName = userName.Trim(), normalized.AgruparPor }, token);
        }, "No se pudo guardar la configuración de vista.", ct);

    public Task<CargaViajesConfigDto> GetConfiguracionAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetConfiguracion", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_CONFIGURACION", token))
                return CreateDefaultConfiguracion();

            var map = await LoadConfiguracionAsync(cn, token);
            return BuildConfiguracion(map);
        }, "No se pudo cargar la configuración del módulo de viajes.", ct);

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
            await appEvents.LogAuditAsync(ModuleName, "SaveConfiguracion", "TA_CONFIGURACION", ConfigGroup, "Configuración del módulo de viajes actualizada.", normalized, token);
        }, "No se pudo guardar la configuración del módulo de viajes.", ct);

    public Task<string> GetNextIdComprobanteAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetNextIdComprobante", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var config = await LoadConfiguracionAsync(cn, token);
            var sucursal = ResolveSucursal(config);
            var letra = ResolveLetra(config);
            var sql = """
                SELECT ISNULL(MAX(TRY_CONVERT(int, SUBSTRING(IDCOMPROBANTE, 5, 8))), 0) + 1
                FROM dbo.MV_VIAJES_CARGA
                WHERE TC = @Tc
                  AND LEFT(ISNULL(IDCOMPROBANTE, ''), 4) = @Sucursal;
                """;
            var next = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Tc = DefaultTc, Sucursal = sucursal }, cancellationToken: token));
            return $"{sucursal}{next:00000000}{letra}";
        }, "No se pudo obtener la numeración del viaje.", ct);

    public Task<string> GetSucursalConfiguradaAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetSucursalConfigurada", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var config = await LoadConfiguracionAsync(cn, token);
            return ResolveSucursal(config);
        }, "No se pudo leer la sucursal configurada.", ct);

    private static async Task<Dictionary<string, string>> LoadConfiguracionAsync(SqlConnection cn, CancellationToken ct)
    {
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var keys = new[]
        {
            SucursalConfigKey,
            LegacySucursalConfigKey,
            LetraConfigKey,
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

    private static decimal GetListaRMTRFAsyncValue(CargaViajeLookupOptionDto? row)
        => decimal.TryParse(row?.Lista, out var parsed) ? parsed : 0m;

    private async Task<(decimal ListaCodigo, string ListaTexto)> GetListaRMTRFAsync(SqlConnection cn, string clienteCodigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clienteCodigo))
            return (0m, string.Empty);

        const string sql = """
            SELECT TOP (1)
                ISNULL(IdListaRMTRF, ''),
                ISNULL(RAZON_SOCIAL, '')
            FROM dbo.Vt_Clientes
            WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
            """;

        var row = await cn.QuerySingleOrDefaultAsync<(string Lista, string Texto)>(new CommandDefinition(sql, new { Codigo = clienteCodigo.Trim().ToUpperInvariant() }, cancellationToken: ct));
        return (ParseDecimal(row.Lista), row.Texto);
    }

    private static decimal ParseDecimal(string? value)
        => decimal.TryParse(value, out var parsed) ? parsed : 0m;

    private static async Task<decimal> ResolveTarifaImporteAsync(
        SqlConnection cn,
        string campoPrincipal,
        string codigo,
        string campoDestino,
        string destino,
        string campoTipoVehiculo,
        string tipoVehiculo,
        int tarifaFletero,
        CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                ISNULL(Importe, 0)
            FROM dbo.TA_TARIFA
            WHERE ISNULL(Activo, 1) = 1
              AND ISNULL(TarifaFletero, 0) = @TarifaFletero
              AND UPPER(LTRIM(RTRIM(ISNULL({campoPrincipal}, '')))) = @Codigo
              AND UPPER(LTRIM(RTRIM(ISNULL({campoDestino}, '')))) = @Destino
              AND UPPER(LTRIM(RTRIM(ISNULL({campoTipoVehiculo}, '')))) = @TipoVehiculo
            ORDER BY IdLista;
            """;

        var result = await cn.ExecuteScalarAsync<decimal?>(new CommandDefinition(sql, new
        {
            TarifaFletero = tarifaFletero,
            Codigo = codigo,
            Destino = TrimUpper(destino),
            TipoVehiculo = TrimUpper(tipoVehiculo)
        }, cancellationToken: ct));

        return result ?? 0m;
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

    private static string BuildChoferNombreSql(string tableName, string alias)
        => tableName.Equals("MA_CHOFERES", StringComparison.OrdinalIgnoreCase)
            ? $"LTRIM(RTRIM(CONCAT(ISNULL({alias}.APELLIDO, ''), CASE WHEN ISNULL({alias}.NOMBRES, '') = '' THEN '' ELSE ' ' + {alias}.NOMBRES END)))"
            : $"LTRIM(RTRIM(ISNULL({alias}.NOMBRES, '')))";

    private static string BuildDestinoDescripcionSql(string tableName, string alias)
        => $"LTRIM(RTRIM(ISNULL({alias}.Descripcion, '')))";

    private static string BuildTipoVehiculoDescripcionSql(string tableName, string alias)
        => $"LTRIM(RTRIM(ISNULL({alias}.DESCRIPCION, '')))";

    private static CargaViajesConfigDto CreateDefaultConfiguracion()
        => new()
        {
            Sucursal = "0001",
            Letra = "X",
            NombresAdicionales = ["Adicional 1", "Adicional 2", "Adicional 3", "Adicional 4", "Adicional 5"],
            PorcentajesAdicionales = [0m, 0m, 0m, 0m, 0m]
        };

    private static CargaViajesConfigDto BuildConfiguracion(Dictionary<string, string> values)
    {
        var dto = CreateDefaultConfiguracion();
        dto.Sucursal = ResolveSucursal(values);
        dto.Letra = ResolveLetra(values);

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
                new() { Key = CargaViajesViewColumnKeys.TipoVehiculo, Label = "Tipo vehículo", Visible = true, Order = 5 },
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

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

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
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(1) FROM sys.objects WHERE object_id = OBJECT_ID(@FullName);", new { FullName = $"dbo.{tableName}" }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct)
    {
        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(@FullName) AND UPPER(name) = UPPER(@ColumnName);", new { FullName = $"dbo.{tableName}", ColumnName = columnName }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<HashSet<string>> LoadColumnsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@FullName);
            """;
        var rows = await cn.QueryAsync<string>(new CommandDefinition(sql, new { FullName = $"dbo.{tableName}" }, cancellationToken: ct));
        return rows.Select(x => x.Trim().ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string> ResolveExistingTableAsync(SqlConnection cn, CancellationToken ct, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (await TableExistsAsync(cn, candidate, ct))
                return candidate;
        }

        throw new InvalidOperationException($"No se encontró ninguna de las tablas esperadas: {string.Join(", ", candidates)}.");
    }

    private static string FirstExistingColumn(HashSet<string> columns, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (columns.Contains(candidate.Trim().ToLowerInvariant()))
                return candidate;
        }

        return candidates[0];
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

    private sealed record ListaInfo(decimal ListaCodigo, string ListaTexto);
}

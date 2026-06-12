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
            const string viajeTable = "MV_VIAJES_CARGA";
            var clienteTable = "Vt_Clientes";
            var choferTable = await ResolveExistingTableAsync(cn, token, "TA_CHOFERES", "MA_CHOFERES");
            var destinoTable = await ResolveExistingTableAsync(cn, token, "TA_DESTINOS", "V_TA_DESTINO");
            var vehiculoTable = await ResolveExistingTableAsync(cn, token, "TA_TIPOVEHICULO");
            var clienteColumn = "IDCLIENTE";
            var destinoColumn = "IDDESTINO";
            var choferColumn = "IDCHOFER";
            var vehiculoColumn = "IDTIPOVEHICULO";
            var estadoColumn = "ESTADO";
            var totalImporteColumn = "TOTAL_IMPORTE";
            var totalFleteColumn = "TOTAL_FLETE";
            var fechaAltaColumn = "FECHAHORA_ALTA";
            var destinoDescColumn = "DESCRIPCIONDESTINO";
            var choferNameColumn = "NOMBRE_CHOFER";
            var clienteCodeExpr = $"ISNULL(v.{clienteColumn}, '')";
            var destinoCodeExpr = $"ISNULL(v.{destinoColumn}, '')";
            var choferCodeExpr = $"ISNULL(v.{choferColumn}, '')";
            var tipoVehiculoCodeExpr = $"ISNULL(v.{vehiculoColumn}, '')";
            var destinoDescExpr = $"ISNULL(v.{destinoDescColumn}, '')";
            var choferNameExpr = $"ISNULL(v.{choferNameColumn}, '')";
            var vehiculoExpr = $"""ISNULL((SELECT TOP (1) LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) FROM dbo.{vehiculoTable} t WHERE UPPER(LTRIM(RTRIM(ISNULL(t.CODIGO, '')))) = UPPER(LTRIM(RTRIM({tipoVehiculoCodeExpr})))), '')""";
            var clienteJoin = $"LEFT JOIN dbo.{clienteTable} cli ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM({clienteCodeExpr})))";
            var totalFleteExpr = $"ISNULL(v.{totalFleteColumn}, 0)";
            var altaExpr = $"ISNULL(v.{fechaAltaColumn}, GETDATE())";
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
                    ISNULL(v.{totalImporteColumn}, 0) AS TotalCliente,
                    {totalFleteExpr} AS TotalFletero,
                    ISNULL(v.{estadoColumn}, N'PENDIENTE') AS Estado,
                    '' AS Usuario,
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
                  AND (@Estado = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.{estadoColumn}, N'PENDIENTE')))) = @Estado)
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
                  AND (@Estado = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.{estadoColumn}, N'PENDIENTE')))) = @Estado)
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
                  AND (@Estado = '' OR UPPER(LTRIM(RTRIM(ISNULL(v.{estadoColumn}, N'PENDIENTE')))) = @Estado)
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
            var choferTable = await ResolveExistingTableAsync(cn, token, "TA_CHOFERES", "MA_CHOFERES");
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
                    '' AS Usuario,
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
                throw new InvalidOperationException("El TC del módulo de carga de viajes es fijo y debe ser VJ.");

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
            if (!validation.IsValid)
                throw new AppValidationException("Revisá los datos del viaje antes de guardar.", validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string viajeTable = "MV_VIAJES_CARGA";
            var columns = await LoadColumnsAsync(cn, viajeTable, token);
            var hasIdLista = columns.Contains("idlista");
            var hasCliente = columns.Contains("idcliente");
            var hasDestino = columns.Contains("iddestino");
            var hasChofer = columns.Contains("idchofer");
            var hasVehiculo = columns.Contains("idtipovehiculo");
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
                    throw new InvalidOperationException("No se encontró el viaje para actualizar.");

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
                    logger.LogWarning(rollbackEx, "SaveViaje rollback falló");
                }

                throw;
            }
        }, "No se pudo guardar el viaje.", ct);

    public Task AnularViajeAsync(int id, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AnularViaje", async token =>
        {
            if (id <= 0)
                throw new InvalidOperationException("No se recibió el viaje a anular.");

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
            var columns = await LoadColumnsAsync(cn, table, token);
            var isMaestro = table.Equals("MA_CHOFERES", StringComparison.OrdinalIgnoreCase);
            var nombreExpr = isMaestro
                ? "LTRIM(RTRIM(CONCAT(ISNULL(APELLIDO, ''), CASE WHEN ISNULL(NOMBRES, '') = '' THEN '' ELSE ' ' + NOMBRES END)))"
                : "LTRIM(RTRIM(ISNULL(NOMBRES, '')))";
            var orderExpr = isMaestro ? "APELLIDO, NOMBRES, CODIGO" : "NOMBRES, CODIGO";
            var disponibleColumn = columns.Contains("disponible") ? "DISPONIBLE" : null;
            var tipoVehiculoColumn = FirstExistingColumn(columns, "IDTIPOVEHICULO", "TIPOVEHICULO");
            var whereClauses = new List<string>
            {
                isMaestro
                    ? @"(
                            @TextoLike = ''
                            OR CODIGO LIKE @TextoLike
                            OR APELLIDO COLLATE Latin1_General_CI_AI LIKE @TextoLike
                            OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        )"
                    : @"(
                            @TextoLike = ''
                            OR CODIGO LIKE @TextoLike
                            OR NOMBRES COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        )"
            };
            if (columns.Contains("activo"))
                whereClauses.Add("(@Activo IS NULL OR ISNULL(ACTIVO, 1) = @Activo)");
            if (disponibleColumn is not null)
                whereClauses.Add("(@Disponible IS NULL OR ISNULL(DISPONIBLE, 0) = @Disponible)");
            if (tipoVehiculoColumn is not null)
                whereClauses.Add($"(@TipoVehiculo = '' OR UPPER(LTRIM(RTRIM(ISNULL({tipoVehiculoColumn}, '')))) LIKE @TipoVehiculoLike)");

            var sql = $"""
                SELECT
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    {nombreExpr} AS Nombre,
                    ISNULL(ACTIVO, 1) AS Activo,
                    {(disponibleColumn is not null ? "ISNULL(DISPONIBLE, 0)" : "CAST(0 AS bit)")} AS Disponible
                FROM dbo.{table}
                WHERE {string.Join(Environment.NewLine + "  AND ", whereClauses)}
                ORDER BY {orderExpr}
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
                    Disponible = GetBool(rd, 3)
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
            var textoLike = SearchTextHelper.LikeContains(filters.Texto);
            var activoFiltro = ParseNullableBitFilter(filters.Activo);
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
                ORDER BY DESCRIPCION, CODIGO
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
        }, "No se pudieron cargar los tipos de vehículo.", ct);

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
        }, "No se pudo cargar el tipo de vehículo seleccionado.", ct);

    public Task<string> SaveTipoVehiculoAsync(CargaViajeTipoVehiculoSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveTipoVehiculo", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Codigo))
                throw new InvalidOperationException("El código del tipo de vehículo es obligatorio.");
            if (string.IsNullOrWhiteSpace(request.Descripcion))
                throw new InvalidOperationException("La descripción del tipo de vehículo es obligatoria.");

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
        }, "No se pudo guardar el tipo de vehículo.", ct);

    public Task BajaTipoVehiculoAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "BajaTipoVehiculo", async token =>
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new InvalidOperationException("No se recibió el tipo de vehículo a dar de baja.");

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
                throw new InvalidOperationException("El tipo de vehículo seleccionado ya no existe en la base activa.");
        }, "No se pudo dar de baja el tipo de vehículo.", ct);

    public Task<bool> TipoVehiculoTieneActivoAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "TipoVehiculoTieneActivo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await ColumnExistsAsync(cn, "TA_TIPOVEHICULO", "ACTIVO", token);
        }, "No se pudo verificar la estructura del tipo de vehículo.", ct);

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> SearchClientesAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchClientes", async token =>
        {
            var search = SearchTextHelper.Normalize(texto);
            if (search.Length < 2)
                return (IReadOnlyList<CargaViajeLookupOptionDto>)Array.Empty<CargaViajeLookupOptionDto>();

            const string sql = """
                SELECT TOP (12)
                    LTRIM(RTRIM(ISNULL(cli.CODIGO, ''))) AS Codigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS Titulo,
                    LTRIM(RTRIM(ISNULL(cli.IdListaRMTRF, ''))) AS Lista,
                    CASE
                        WHEN ISNULL(t.Nombre, '') = '' THEN ''
                        ELSE CONCAT(LTRIM(RTRIM(ISNULL(cli.IdListaRMTRF, ''))), ' - ', LTRIM(RTRIM(t.Nombre)))
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
                        cli.CODIGO LIKE @Search
                        OR cli.RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @Search
                        OR ISNULL(cli.IdListaRMTRF, '') LIKE @Search
                        OR ISNULL(t.Nombre, '') COLLATE Latin1_General_CI_AI LIKE @Search
                      )
                ORDER BY cli.RAZON_SOCIAL, cli.CODIGO;
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
                    LTRIM(RTRIM(ISNULL({codeColumn}, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(Descripcion, ''))) AS Titulo,
                    '' AS Subtitulo
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
                    LTRIM(RTRIM(ISNULL(CODIGO, ''))) AS Codigo,
                    LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) AS Titulo,
                    '' AS Subtitulo
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
        }, "No se pudo cargar la configuración de vista del tipo de vehículo.", ct);

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

            await appEvents.LogAuditAsync(ModuleName, "SaveTipoVehiculoViewSettings", "TA_CONFIGURACION", configKey, "Configuración de vista de tipo de vehículo actualizada.", new { UserName = userName.Trim(), normalized.AgruparPor }, token);
        }, "No se pudo guardar la configuración de vista del tipo de vehículo.", ct);

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
            if (string.IsNullOrWhiteSpace(sucursal) || string.IsNullOrWhiteSpace(letra))
                throw new InvalidOperationException("Falta configurar la sucursal o la letra de viajes.");

            const string viajeTable = "MV_VIAJES_CARGA";
            var sql = $"""
                SELECT ISNULL(MAX(TRY_CONVERT(int, SUBSTRING(IDCOMPROBANTE, 5, 8))), 0) + 1
                FROM dbo.{viajeTable}
                WHERE TC = @Tc
                  AND LEFT(ISNULL(IDCOMPROBANTE, ''), 4) = @Sucursal;
                """;
            var next = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Tc = DefaultTc, Sucursal = sucursal }, cancellationToken: token));
            logger.LogInformation("GetNextIdComprobante OK Tabla={Tabla} Sucursal={Sucursal} Letra={Letra} Next={Next}", viajeTable, sucursal, letra, next);
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

    private async Task<(string ListaCodigo, string ListaTexto)> GetListaRMTRFAsync(SqlConnection cn, string clienteCodigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clienteCodigo))
            return (string.Empty, string.Empty);

        const string sql = """
            SELECT TOP (1)
                ISNULL(LTRIM(RTRIM(ISNULL(cli.IdListaRMTRF, ''))), '') AS ListaCodigo,
                CASE
                    WHEN ISNULL(t.Nombre, '') = '' THEN ''
                    ELSE CONCAT(LTRIM(RTRIM(ISNULL(cli.IdListaRMTRF, ''))), ' - ', LTRIM(RTRIM(t.Nombre)))
                END AS ListaTexto
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
                CASE
                    WHEN ISNULL(Nombre, '') = '' THEN ''
                    ELSE CONCAT(LTRIM(RTRIM(IdLista)), ' - ', LTRIM(RTRIM(Nombre)))
                END AS ListaTexto
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

    private static CargaViajeTipoVehiculoViewSettingsDto CreateDefaultTipoVehiculoViewSettings()
        => new()
        {
            AgruparPor = CargaViajeTipoVehiculoViewGroupKeys.None,
            Columnas =
            [
                new() { Key = CargaViajeTipoVehiculoViewColumnKeys.Codigo, Label = "Código", Visible = true, Order = 0 },
                new() { Key = CargaViajeTipoVehiculoViewColumnKeys.Descripcion, Label = "Descripción", Visible = true, Order = 1 },
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

        throw new InvalidOperationException($"No se encontró ninguna de las columnas esperadas: {string.Join(", ", candidates)}.");
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

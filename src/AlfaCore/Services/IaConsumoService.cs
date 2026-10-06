using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IIaConsumoService
{
    /// <summary>Hay conexión central configurada (SaaS). Sin ella no hay medición ni cobro.</summary>
    bool Disponible { get; }

    /// <summary>Consumo del mes de la base activa y el plan de créditos de su cliente. Null si no hay datos centrales.</summary>
    Task<IaConsumoBaseDto?> GetResumenBaseActivaAsync(int anio, int mes, CancellationToken ct = default);

    /// <summary>Consumo del mes por cliente (solo superadmin).</summary>
    Task<IaConsumoCentralDto> GetResumenCentralAsync(int anio, int mes, CancellationToken ct = default);

    /// <summary>Compara el costo medido con el que informa OpenAI (requiere OPENAI_ADMIN_KEY). Solo superadmin.</summary>
    Task<IaConciliacionDto> GetConciliacionAsync(int anio, int mes, CancellationToken ct = default);

    /// <summary>Genera (idempotente) el cargo del mes de cada cliente con plan de créditos IA. Solo superadmin.</summary>
    Task<IaCargosResultadoDto> GenerarCargosAsync(int anio, int mes, CancellationToken ct = default);

    /// <summary>
    /// Estado del tope mensual del cliente de la base activa (cacheado 2 minutos). Null si no hay tope,
    /// central o esquema. Nunca lanza: ante cualquier falla devuelve null y el asistente sigue.
    /// </summary>
    Task<IaTopeEstadoDto?> GetTopeEstadoBaseActivaAsync(CancellationToken ct = default);

    /// <summary>
    /// Tope manual del cliente. Solo superadmin. <paramref name="topeCreditos"/> null = usar el tope
    /// automático del plan; 0 = sin tope (anula el del plan); mayor a 0 = ese tope.
    /// </summary>
    Task SaveTopeAsync(string idCliente, int? topeCreditos, int avisoPorcentaje, CancellationToken ct = default);

    /// <summary>Configuración de créditos y planes CREDITOS del catálogo. Solo superadmin.</summary>
    Task<IaConfigCreditosDto> GetConfigAsync(CancellationToken ct = default);

    /// <summary>Guarda la configuración de créditos (IA_CONFIG). Solo superadmin.</summary>
    Task SaveConfigAsync(IaConfigCreditosDto config, CancellationToken ct = default);

    /// <summary>Planes que puede pedir el cliente de la base activa, su plan actual y el pedido pendiente. Null si no aplica.</summary>
    Task<IaPlanesClienteDto?> GetPlanesBaseActivaAsync(CancellationToken ct = default);

    /// <summary>Pide el cambio al plan indicado para el cliente de la base activa (reemplaza un pedido pendiente).</summary>
    Task SolicitarPlanAsync(int idPlan, CancellationToken ct = default);

    /// <summary>Pedidos de cambio de plan pendientes. Solo superadmin.</summary>
    Task<IReadOnlyList<IaSolicitudPlanDto>> GetSolicitudesPlanAsync(CancellationToken ct = default);

    /// <summary>Aprueba (contrata o cambia el plan del módulo IA_CREDITOS) o rechaza un pedido. Solo superadmin.</summary>
    Task DecidirSolicitudPlanAsync(int idSolicitud, bool aprobar, CancellationToken ct = default);
}

/// <summary>
/// Consumo y cobro de IA (2026-10-05). Lee ALFA_CENTRAL.dbo.IA_USO. 1 crédito = USD_POR_CREDITO de costo
/// de OpenAI (IA_CONFIG, por defecto 0,001); el margen va en el precio del plan. El cargo mensual por
/// cliente sale del plan de tipo CREDITOS asignado al módulo IA_CREDITOS: abono fijo + créditos
/// excedentes × precio de excedente (por cada 1.000 créditos). Los clientes sin plan asignado usan el
/// plan por defecto (2026-10-06); el tope vigente es el manual o, si no hay, el automático del plan.
/// </summary>
public sealed class IaConsumoService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IHttpClientFactory httpClientFactory,
    IAppEventService appEvents,
    ICentralAdminService? centralAdmin = null) : IIaConsumoService
{
    private const string ModuleName = "ConsumoIA";
    internal const decimal UsdPorCreditoPorDefecto = 0.001m;
    internal const string CodigoModulo = "IA_CREDITOS";
    internal const decimal CreditosPorPrecioExcedente = 1000m;
    private static readonly TimeSpan DuracionCacheTope = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<int, (DateTime HastaUtc, IaTopeEstadoDto? Estado)> TopeCache = new();

    public bool Disponible => !string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral"));

    private string CentralConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No está configurada la conexión central.");

    public async Task<IaConsumoBaseDto?> GetResumenBaseActivaAsync(int anio, int mes, CancellationToken ct = default)
    {
        var idBase = sessionService.GetActiveSession()?.BaseId ?? 0;
        if (!Disponible || idBase <= 0)
            return null;

        try
        {
            await using var cn = new SqlConnection(CentralConnectionString);
            await cn.OpenAsync(ct);
            if (!await EsquemaListoAsync(cn, ct))
                return null;

            var (desde, hasta) = RangoUtc(anio, mes);
            var config = await LeerConfigAsync(cn, ct);
            var usdPorCredito = config.UsdPorCredito;
            var idCliente = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(idcliente)) FROM dbo.bases WHERE id = @IdBase;", new { IdBase = idBase }, cancellationToken: ct)) ?? string.Empty;

            var porFuncion = (await cn.QueryAsync<FilaFuncion>(new CommandDefinition("""
                SELECT Funcion, COUNT_BIG(*) AS Llamadas, SUM(ISNULL(CostoUsd, 0)) AS CostoUsd,
                       SUM(CASE WHEN CostoUsd IS NULL THEN 1 ELSE 0 END) AS LlamadasSinPrecio
                FROM dbo.IA_USO
                WHERE IdBase = @IdBase AND FechaHoraUtc >= @Desde AND FechaHoraUtc < @Hasta
                GROUP BY Funcion;
                """, new { IdBase = idBase, Desde = desde, Hasta = hasta }, cancellationToken: ct))).ToList();

            var costoCliente = idCliente.Length == 0 ? 0m : await cn.ExecuteScalarAsync<decimal>(new CommandDefinition("""
                SELECT ISNULL(SUM(CostoUsd), 0) FROM dbo.IA_USO
                WHERE IdCliente = @IdCliente AND FechaHoraUtc >= @Desde AND FechaHoraUtc < @Hasta;
                """, new { IdCliente = idCliente, Desde = desde, Hasta = hasta }, cancellationToken: ct));

            var costoBase = porFuncion.Sum(f => f.CostoUsd);
            var plan = idCliente.Length == 0 ? null : await GetPlanEfectivoAsync(cn, idCliente, config, ct);
            return new IaConsumoBaseDto
            {
                Anio = anio,
                Mes = mes,
                CostoUsdBase = costoBase,
                CreditosBase = Creditos(costoBase, usdPorCredito),
                CreditosCliente = Creditos(costoCliente, usdPorCredito),
                Plan = plan,
                Tope = idCliente.Length == 0 ? null : await ResolverTopeAsync(cn, idCliente, Creditos(costoCliente, usdPorCredito), plan, config, ct),
                PorFuncion = porFuncion
                    .Select(f => f.ToDto(usdPorCredito))
                    .OrderByDescending(f => f.CostoUsd)
                    .ToList()
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El resumen es informativo: si la central no responde, la pantalla lo oculta.
            await appEvents.LogErrorAsync(ModuleName, "ResumenBase", ex, "No se pudo leer el consumo de IA de la base.", new { idBase }, AppEventSeverity.Warning, ct);
            return null;
        }
    }

    public async Task<IaConsumoCentralDto> GetResumenCentralAsync(int anio, int mes, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        RequerirEsquema(await EsquemaListoAsync(cn, ct));

        var (desde, hasta) = RangoUtc(anio, mes);
        var config = await LeerConfigAsync(cn, ct);
        var usdPorCredito = config.UsdPorCredito;
        var filas = (await cn.QueryAsync<FilaClienteFuncion>(new CommandDefinition("""
            SELECT ISNULL(u.IdCliente, N'') AS IdCliente, ISNULL(MAX(c.nombre), N'') AS Cliente, u.Funcion,
                   COUNT_BIG(*) AS Llamadas, SUM(ISNULL(u.CostoUsd, 0)) AS CostoUsd,
                   SUM(CASE WHEN u.CostoUsd IS NULL THEN 1 ELSE 0 END) AS LlamadasSinPrecio
            FROM dbo.IA_USO u
            LEFT JOIN dbo.Clientes c ON LTRIM(RTRIM(c.idcliente)) = u.IdCliente
            WHERE u.FechaHoraUtc >= @Desde AND u.FechaHoraUtc < @Hasta
            GROUP BY ISNULL(u.IdCliente, N''), u.Funcion;
            """, new { Desde = desde, Hasta = hasta }, cancellationToken: ct))).ToList();

        var bases = (await cn.QueryAsync<(string IdCliente, int Bases)>(new CommandDefinition("""
            SELECT ISNULL(IdCliente, N'') AS IdCliente, COUNT(DISTINCT IdBase) AS Bases
            FROM dbo.IA_USO
            WHERE FechaHoraUtc >= @Desde AND FechaHoraUtc < @Hasta
            GROUP BY ISNULL(IdCliente, N'');
            """, new { Desde = desde, Hasta = hasta }, cancellationToken: ct))).ToDictionary(x => x.IdCliente, x => x.Bases);

        var planes = (await GetPlanesAsync(cn, null, ct))
            .GroupBy(p => p.IdCliente)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var planDefault = await GetPlanDefaultAsync(cn, config.PlanDefaultCodigo, ct);
        var topes = (await cn.QueryAsync<(string IdCliente, int TopeCreditos, int AvisoPorcentaje)>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(IdCliente)), TopeCreditos, AvisoPorcentaje FROM dbo.IA_TOPE_CREDITOS WHERE Activo = 1;",
                cancellationToken: ct)))
            .ToDictionary(t => t.IdCliente, t => ((int Tope, int Aviso)?)(t.TopeCreditos, t.AvisoPorcentaje), StringComparer.OrdinalIgnoreCase);

        var clientes = filas
            .GroupBy(f => f.IdCliente)
            .Select(g =>
            {
                var costo = g.Sum(x => x.CostoUsd);
                var creditos = Creditos(costo, usdPorCredito);
                var plan = planes.GetValueOrDefault(g.Key) ?? (g.Key.Length == 0 ? null : ComoPlanDefault(planDefault, g.Key));
                var (excedentes, importe) = CalcularCargo(creditos, plan);
                var manual = topes.GetValueOrDefault(g.Key);
                var tope = ResolverTope(g.Key, manual, plan, config.TopeFactorExcedentes, config.AvisoPorcentaje, creditos);
                return new IaConsumoClienteDto
                {
                    IdCliente = g.Key,
                    Cliente = g.Key.Length == 0 ? "Sin cliente (sin base identificada)" : FirstNonEmpty(g.First().Cliente, g.Key),
                    Bases = bases.GetValueOrDefault(g.Key),
                    Llamadas = g.Sum(x => x.Llamadas),
                    CostoUsd = costo,
                    Creditos = creditos,
                    LlamadasSinPrecio = g.Sum(x => x.LlamadasSinPrecio),
                    Plan = plan,
                    CreditosExcedentes = excedentes,
                    ImporteEstimado = importe,
                    TopeCreditos = tope?.TopeCreditos,
                    AvisoPorcentaje = manual?.Aviso ?? config.AvisoPorcentaje,
                    TopeOrigen = tope?.Origen ?? string.Empty,
                    SinTopeManual = manual is { Tope: <= 0 },
                    PorFuncion = g.Select(x => x.ToDto(usdPorCredito)).OrderByDescending(x => x.CostoUsd).ToList()
                };
            })
            .OrderByDescending(c => c.CostoUsd)
            .ToList();

        return new IaConsumoCentralDto
        {
            Anio = anio,
            Mes = mes,
            UsdPorCredito = usdPorCredito,
            CostoUsdTotal = clientes.Sum(c => c.CostoUsd),
            CreditosTotal = clientes.Sum(c => c.Creditos),
            LlamadasSinPrecio = clientes.Sum(c => c.LlamadasSinPrecio),
            LlamadasSinCliente = clientes.Where(c => c.IdCliente.Length == 0).Sum(c => c.Llamadas),
            Clientes = clientes
        };
    }

    public async Task<IaConciliacionDto> GetConciliacionAsync(int anio, int mes, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        var (desde, hasta) = RangoUtc(anio, mes);
        decimal medido;
        await using (var cn = new SqlConnection(CentralConnectionString))
        {
            await cn.OpenAsync(ct);
            RequerirEsquema(await EsquemaListoAsync(cn, ct));
            medido = await cn.ExecuteScalarAsync<decimal>(new CommandDefinition("""
                SELECT ISNULL(SUM(CostoUsd), 0) FROM dbo.IA_USO WHERE FechaHoraUtc >= @Desde AND FechaHoraUtc < @Hasta;
                """, new { Desde = desde, Hasta = hasta }, cancellationToken: ct));
        }

        var adminKey = (Environment.GetEnvironmentVariable("OPENAI_ADMIN_KEY") ?? string.Empty).Trim();
        if (adminKey.Length == 0)
        {
            return new IaConciliacionDto
            {
                CostoMedidoUsd = medido,
                Mensaje = "Para comparar con lo que factura OpenAI hace falta la variable OPENAI_ADMIN_KEY (clave de administración de la organización) en el servidor."
            };
        }

        try
        {
            var total = 0m;
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminKey);
            string? page = null;
            for (var i = 0; i < 10; i++)
            {
                var url = $"https://api.openai.com/v1/organization/costs?start_time={new DateTimeOffset(desde).ToUnixTimeSeconds()}&end_time={new DateTimeOffset(hasta).ToUnixTimeSeconds()}&bucket_width=1d&limit=31"
                          + (page is null ? string.Empty : $"&page={Uri.EscapeDataString(page)}");
                using var response = await client.GetAsync(url, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    return new IaConciliacionDto { CostoMedidoUsd = medido, Mensaje = $"OpenAI respondió {(int)response.StatusCode} al pedir los costos de la organización." };

                var (suma, siguiente) = SumarCostosOpenAi(body);
                total += suma;
                page = siguiente;
                if (page is null)
                    break;
            }

            return new IaConciliacionDto { Disponible = true, CostoOpenAiUsd = total, CostoMedidoUsd = medido };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "Conciliacion", ex, "No se pudo consultar los costos de OpenAI.", new { anio, mes }, AppEventSeverity.Warning, ct);
            return new IaConciliacionDto { CostoMedidoUsd = medido, Mensaje = "No se pudo consultar los costos de OpenAI. El detalle quedó registrado." };
        }
    }

    public async Task<IaCargosResultadoDto> GenerarCargosAsync(int anio, int mes, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        var periodoDesde = new DateTime(anio, mes, 1);
        var periodoHasta = periodoDesde.AddMonths(1).AddDays(-1);
        if (periodoHasta >= DateTime.Today)
            throw new InvalidOperationException("Solo se pueden generar cargos de meses terminados.");

        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        RequerirEsquema(await EsquemaListoAsync(cn, ct));

        var (desde, hasta) = RangoUtc(anio, mes);
        var usdPorCredito = await GetUsdPorCreditoAsync(cn, ct);
        var resultado = new IaCargosResultadoDto();
        TopeCache.Clear();

        foreach (var plan in await GetPlanesAsync(cn, null, ct))
        {
            var costo = await cn.ExecuteScalarAsync<decimal>(new CommandDefinition("""
                SELECT ISNULL(SUM(CostoUsd), 0) FROM dbo.IA_USO
                WHERE IdCliente = @IdCliente AND FechaHoraUtc >= @Desde AND FechaHoraUtc < @Hasta;
                """, new { plan.IdCliente, Desde = desde, Hasta = hasta }, cancellationToken: ct));
            var creditos = Creditos(costo, usdPorCredito);
            var (excedentes, importe) = CalcularCargo(creditos, plan);
            if (importe <= 0)
            {
                resultado.SinImporte++;
                continue;
            }

            var existe = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT TOP (1) Id FROM dbo.Cargos WHERE IdClienteModulo = @IdClienteModulo AND PeriodoDesde = @PeriodoDesde;",
                new { plan.IdClienteModulo, PeriodoDesde = periodoDesde }, cancellationToken: ct));
            if (existe is not null)
            {
                resultado.YaExistentes++;
                continue;
            }

            var concepto = $"Créditos IA {mes:00}/{anio}: {creditos:N0} usados, {plan.CreditosIncluidos:N0} incluidos, {excedentes:N0} excedentes";
            try
            {
                var id = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    INSERT INTO dbo.Cargos (IdCliente, IdClienteModulo, Concepto, PeriodoDesde, PeriodoHasta, Importe, Moneda, FechaVencimiento, Estado)
                    OUTPUT INSERTED.Id
                    VALUES (@IdCliente, @IdClienteModulo, @Concepto, @PeriodoDesde, @PeriodoHasta, @Importe, @Moneda, @FechaVencimiento, @Estado);
                    """, new
                {
                    plan.IdCliente,
                    plan.IdClienteModulo,
                    Concepto = concepto,
                    PeriodoDesde = periodoDesde,
                    PeriodoHasta = periodoHasta,
                    Importe = importe,
                    plan.Moneda,
                    FechaVencimiento = periodoHasta.AddDays(10),
                    Estado = CargoEstados.Pendiente
                }, cancellationToken: ct));

                resultado.Generados++;
                resultado.Detalle.Add($"{plan.IdCliente}: {concepto} → {importe:N2} {plan.Moneda}");
                await appEvents.LogAuditAsync(ModuleName, "GenerarCargoCreditosIA", "Cargos", id.ToString(CultureInfo.InvariantCulture),
                    $"Cargo de créditos IA {mes:00}/{anio} para {plan.IdCliente}.",
                    new { plan.IdCliente, plan.IdClienteModulo, creditos, excedentes, importe, plan.Moneda, costo, usdPorCredito }, ct);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                resultado.YaExistentes++;
            }
        }

        return resultado;
    }

    public async Task<IaTopeEstadoDto?> GetTopeEstadoBaseActivaAsync(CancellationToken ct = default)
    {
        var idBase = sessionService.GetActiveSession()?.BaseId ?? 0;
        if (!Disponible || idBase <= 0)
            return null;

        if (TopeCache.TryGetValue(idBase, out var cache) && cache.HastaUtc > DateTime.UtcNow)
            return cache.Estado;

        IaTopeEstadoDto? estado = null;
        try
        {
            await using var cn = new SqlConnection(CentralConnectionString);
            await cn.OpenAsync(ct);
            if (await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT CASE WHEN OBJECT_ID(N'dbo.IA_TOPE_CREDITOS', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.IA_USO', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.IA_CONFIG', N'U') IS NOT NULL THEN 1 ELSE 0 END;",
                    cancellationToken: ct)) == 1)
            {
                var idCliente = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                    "SELECT LTRIM(RTRIM(idcliente)) FROM dbo.bases WHERE id = @IdBase;", new { IdBase = idBase }, cancellationToken: ct)) ?? string.Empty;
                if (idCliente.Length > 0)
                {
                    var hoy = DateTime.Today;
                    var (desde, hasta) = RangoUtc(hoy.Year, hoy.Month);
                    var costo = await cn.ExecuteScalarAsync<decimal>(new CommandDefinition(
                        "SELECT ISNULL(SUM(CostoUsd), 0) FROM dbo.IA_USO WHERE IdCliente = @IdCliente AND FechaHoraUtc >= @Desde AND FechaHoraUtc < @Hasta;",
                        new { IdCliente = idCliente, Desde = desde, Hasta = hasta }, cancellationToken: ct));
                    var config = await LeerConfigAsync(cn, ct);
                    var plan = await GetPlanEfectivoAsync(cn, idCliente, config, ct);
                    estado = await ResolverTopeAsync(cn, idCliente, Creditos(costo, config.UsdPorCredito), plan, config, ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El tope nunca bloquea al asistente por una falla de la central.
            estado = null;
        }

        TopeCache[idBase] = (DateTime.UtcNow.Add(DuracionCacheTope), estado);
        return estado;
    }

    public async Task SaveTopeAsync(string idCliente, int? topeCreditos, int avisoPorcentaje, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        var cliente = (idCliente ?? string.Empty).Trim();
        if (cliente.Length == 0)
            throw new InvalidOperationException("Elegí un cliente.");
        if (topeCreditos is < 0)
            throw new InvalidOperationException("El tope no puede ser negativo.");
        var aviso = Math.Clamp(avisoPorcentaje, 1, 100);

        const string desactivar = "UPDATE dbo.IA_TOPE_CREDITOS SET Activo = 0, FechaModificacion = GETDATE(), UsuarioModificacion = @Usuario WHERE IdCliente = @IdCliente;";
        const string guardar = """
            UPDATE dbo.IA_TOPE_CREDITOS
               SET TopeCreditos = @Tope, AvisoPorcentaje = @Aviso, Activo = 1, FechaModificacion = GETDATE(), UsuarioModificacion = @Usuario
             WHERE IdCliente = @IdCliente;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.IA_TOPE_CREDITOS (IdCliente, TopeCreditos, AvisoPorcentaje, Activo, UsuarioModificacion)
                VALUES (@IdCliente, @Tope, @Aviso, 1, @Usuario);
            """;

        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        RequerirEsquema(await EsquemaListoAsync(cn, ct));
        await cn.ExecuteAsync(new CommandDefinition(topeCreditos is null ? desactivar : guardar,
            new { IdCliente = cliente, Tope = topeCreditos ?? 0, Aviso = aviso, Usuario = appUserSession.GetCurrentUserName("SYSTEM") },
            cancellationToken: ct));
        TopeCache.Clear();
        await appEvents.LogAuditAsync(ModuleName, "SaveTopeCreditosIA", "IA_TOPE_CREDITOS", cliente,
            topeCreditos switch
            {
                null => "Se volvió al tope automático del plan.",
                0 => "Se dejó al cliente sin tope de créditos IA.",
                _ => "Se definió el tope mensual de créditos IA."
            },
            new { cliente, topeCreditos, aviso }, ct);
    }

    public async Task<IaConfigCreditosDto> GetConfigAsync(CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        RequerirEsquema(await EsquemaListoAsync(cn, ct));
        var config = await LeerConfigAsync(cn, ct);
        config.Planes = await GetPlanesCatalogoAsync(cn, soloVisibles: false, ct);
        config.IdModulo = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT TOP (1) Id FROM dbo.Modulos WHERE UPPER(LTRIM(RTRIM(Codigo))) = @Codigo;",
            new { Codigo = CodigoModulo }, cancellationToken: ct)) ?? 0;
        return config;
    }

    public async Task SaveConfigAsync(IaConfigCreditosDto config, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        ArgumentNullException.ThrowIfNull(config);
        if (config.UsdPorCredito <= 0)
            throw new InvalidOperationException("El valor del crédito tiene que ser mayor a cero.");
        if (config.TopeFactorExcedentes < 0)
            throw new InvalidOperationException("El factor del tope no puede ser negativo.");

        var codigo = (config.PlanDefaultCodigo ?? string.Empty).Trim();
        var aviso = Math.Clamp(config.AvisoPorcentaje, 1, 100);
        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        RequerirEsquema(await EsquemaListoAsync(cn, ct));
        if (codigo.Length > 0 && (await GetPlanesCatalogoAsync(cn, soloVisibles: false, ct)).All(p => !string.Equals(p.Codigo, codigo, StringComparison.OrdinalIgnoreCase) || !p.Activo))
            throw new InvalidOperationException("El plan por defecto tiene que ser un plan de créditos activo.");

        var valores = new (string Clave, string Valor)[]
        {
            ("USD_POR_CREDITO", config.UsdPorCredito.ToString("0.########", CultureInfo.InvariantCulture)),
            ("PLAN_DEFAULT_CODIGO", codigo),
            ("TOPE_FACTOR_EXCEDENTES", config.TopeFactorExcedentes.ToString("0.##", CultureInfo.InvariantCulture)),
            ("AVISO_PORCENTAJE", aviso.ToString(CultureInfo.InvariantCulture))
        };
        foreach (var (clave, valor) in valores)
        {
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.IA_CONFIG SET Valor = @Valor WHERE Clave = @Clave;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.IA_CONFIG (Clave, Valor) VALUES (@Clave, @Valor);
                """, new { Clave = clave, Valor = valor }, cancellationToken: ct));
        }

        TopeCache.Clear();
        await appEvents.LogAuditAsync(ModuleName, "SaveConfigCreditosIA", "IA_CONFIG", "CREDITOS",
            "Se actualizó la configuración de créditos IA.", valores.ToDictionary(v => v.Clave, v => v.Valor), ct);
    }

    public async Task<IaPlanesClienteDto?> GetPlanesBaseActivaAsync(CancellationToken ct = default)
    {
        var idBase = sessionService.GetActiveSession()?.BaseId ?? 0;
        if (!Disponible || idBase <= 0)
            return null;

        try
        {
            await using var cn = new SqlConnection(CentralConnectionString);
            await cn.OpenAsync(ct);
            if (!await EsquemaListoAsync(cn, ct) || !await TablaExisteAsync(cn, "IA_SOLICITUD_PLAN", ct))
                return null;

            var idCliente = await GetIdClienteDeBaseAsync(cn, idBase, ct);
            if (idCliente.Length == 0)
                return null;

            var config = await LeerConfigAsync(cn, ct);
            var planes = await GetPlanesCatalogoAsync(cn, soloVisibles: true, ct);
            if (planes.Count == 0)
                return null;

            return new IaPlanesClienteDto
            {
                IdPlanActual = (await GetPlanEfectivoAsync(cn, idCliente, config, ct))?.IdPlan,
                Planes = planes,
                Pendiente = (await GetSolicitudesAsync(cn, idCliente, ct)).FirstOrDefault()
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "PlanesBase", ex, "No se pudieron leer los planes de créditos IA.", new { idBase }, AppEventSeverity.Warning, ct);
            return null;
        }
    }

    public async Task SolicitarPlanAsync(int idPlan, CancellationToken ct = default)
    {
        var session = sessionService.GetActiveSession();
        var idBase = session?.BaseId ?? 0;
        if (!Disponible || idBase <= 0)
            throw new InvalidOperationException("No hay una base activa para pedir el plan.");

        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        RequerirEsquema(await EsquemaListoAsync(cn, ct) && await TablaExisteAsync(cn, "IA_SOLICITUD_PLAN", ct));
        var idCliente = await GetIdClienteDeBaseAsync(cn, idBase, ct);
        if (idCliente.Length == 0)
            throw new InvalidOperationException("La base activa no tiene un cliente asociado.");

        var plan = (await GetPlanesCatalogoAsync(cn, soloVisibles: true, ct)).FirstOrDefault(p => p.IdPlan == idPlan)
            ?? throw new InvalidOperationException("El plan elegido no está disponible.");
        var actual = await GetPlanEfectivoAsync(cn, idCliente, await LeerConfigAsync(cn, ct), ct);
        if (actual?.IdPlan == idPlan && actual.PorDefecto == false)
            throw new InvalidOperationException("Ya tenés ese plan.");

        var usuario = appUserSession.GetCurrentUserName("SYSTEM");
        var solicitadoPor = $"{usuario} · {FirstNonEmpty(session?.Nombre, $"Base {idBase}")}";
        var id = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            UPDATE dbo.IA_SOLICITUD_PLAN SET Estado = N'CANCELADA', DecididoUtc = GETUTCDATE(), DecididoPor = @Usuario
            WHERE IdCliente = @IdCliente AND Estado = N'PENDIENTE';
            INSERT INTO dbo.IA_SOLICITUD_PLAN (IdCliente, IdPlan, SolicitadoPor) OUTPUT INSERTED.Id
            VALUES (@IdCliente, @IdPlan, @SolicitadoPor);
            """, new { IdCliente = idCliente, IdPlan = idPlan, Usuario = usuario, SolicitadoPor = Recortar(solicitadoPor, 120) }, cancellationToken: ct));

        await appEvents.LogAuditAsync(ModuleName, "SolicitarPlanCreditosIA", "IA_SOLICITUD_PLAN", id.ToString(CultureInfo.InvariantCulture),
            $"El cliente {idCliente} pidió el plan {plan.Nombre}.", new { idCliente, idPlan, plan.Codigo, idBase }, ct);
    }

    public async Task<IReadOnlyList<IaSolicitudPlanDto>> GetSolicitudesPlanAsync(CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        if (!await TablaExisteAsync(cn, "IA_SOLICITUD_PLAN", ct))
            return [];
        return await GetSolicitudesAsync(cn, null, ct);
    }

    public async Task DecidirSolicitudPlanAsync(int idSolicitud, bool aprobar, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        await using var cn = new SqlConnection(CentralConnectionString);
        await cn.OpenAsync(ct);
        RequerirEsquema(await TablaExisteAsync(cn, "IA_SOLICITUD_PLAN", ct));

        var solicitud = await cn.QuerySingleOrDefaultAsync<(string IdCliente, int IdPlan)?>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(IdCliente)), IdPlan FROM dbo.IA_SOLICITUD_PLAN WHERE Id = @Id AND Estado = N'PENDIENTE';",
            new { Id = idSolicitud }, cancellationToken: ct))
            ?? throw new InvalidOperationException("El pedido ya no está pendiente.");
        var usuario = appUserSession.GetCurrentUserName("SYSTEM");

        if (aprobar)
        {
            if (centralAdmin is null)
                throw new InvalidOperationException("No está disponible la administración central para cambiar el plan.");

            var moduloActual = await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
                SELECT TOP (1) cm.IdModulo
                FROM dbo.ClienteModulos cm
                INNER JOIN dbo.Modulos m ON m.Id = cm.IdModulo
                WHERE LTRIM(RTRIM(cm.IdCliente)) = @IdCliente
                  AND UPPER(LTRIM(RTRIM(m.Codigo))) = @Codigo
                  AND cm.Estado IN (N'Activo', N'Prueba');
                """, new { solicitud.IdCliente, Codigo = CodigoModulo }, cancellationToken: ct));

            // Sin prorrateo: el plan nuevo rige para el cargo del mes en que se aprueba.
            if (moduloActual is int idModulo)
                await centralAdmin.CambiarPlanAsync(solicitud.IdCliente, idModulo, solicitud.IdPlan, usuario, ct);
            else
                await centralAdmin.ContratarPlanAsync(solicitud.IdCliente, solicitud.IdPlan, usuario, ct);
        }

        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.IA_SOLICITUD_PLAN SET Estado = @Estado, DecididoUtc = GETUTCDATE(), DecididoPor = @Usuario
            WHERE Id = @Id AND Estado = N'PENDIENTE';
            """, new { Id = idSolicitud, Estado = aprobar ? "APROBADA" : "RECHAZADA", Usuario = usuario }, cancellationToken: ct));
        TopeCache.Clear();
        await appEvents.LogAuditAsync(ModuleName, aprobar ? "AprobarPlanCreditosIA" : "RechazarPlanCreditosIA", "IA_SOLICITUD_PLAN",
            idSolicitud.ToString(CultureInfo.InvariantCulture),
            aprobar ? "Se aprobó un cambio de plan de créditos IA." : "Se rechazó un cambio de plan de créditos IA.",
            new { solicitud.IdCliente, solicitud.IdPlan }, ct);
    }

    // ---------------------------------------------------------------- planes, configuración y tope

    private async Task<IaTopeEstadoDto?> ResolverTopeAsync(SqlConnection cn, string idCliente, long creditosUsados,
        IaPlanCreditosDto? plan, IaConfigCreditosDto config, CancellationToken ct)
    {
        (int Tope, int Aviso)? manual = null;
        if (await TablaExisteAsync(cn, "IA_TOPE_CREDITOS", ct))
        {
            var filas = await cn.QueryAsync<(int TopeCreditos, int AvisoPorcentaje)>(new CommandDefinition(
                "SELECT TopeCreditos, AvisoPorcentaje FROM dbo.IA_TOPE_CREDITOS WHERE LTRIM(RTRIM(IdCliente)) = @IdCliente AND Activo = 1;",
                new { IdCliente = idCliente }, cancellationToken: ct));
            foreach (var f in filas)
                manual = (f.TopeCreditos, f.AvisoPorcentaje);
        }

        return ResolverTope(idCliente, manual, plan, config.TopeFactorExcedentes, config.AvisoPorcentaje, creditosUsados);
    }

    private static async Task<IaConfigCreditosDto> LeerConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var valores = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition(
                "SELECT Clave, Valor FROM dbo.IA_CONFIG;", cancellationToken: ct)))
            .ToDictionary(v => v.Clave.Trim(), v => (v.Valor ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
        return ConfigDesde(valores);
    }

    /// <summary>Lee IA_CONFIG con valores por defecto para claves faltantes o inválidas.</summary>
    internal static IaConfigCreditosDto ConfigDesde(IReadOnlyDictionary<string, string> valores)
    {
        static decimal Num(IReadOnlyDictionary<string, string> v, string clave, decimal porDefecto, decimal minimo)
            => v.TryGetValue(clave, out var t) && decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= minimo ? d : porDefecto;

        return new IaConfigCreditosDto
        {
            UsdPorCredito = Num(valores, "USD_POR_CREDITO", UsdPorCreditoPorDefecto, 0.00000001m),
            // Sin la clave (centrales anteriores a 2026-10-06) no hay plan por defecto.
            PlanDefaultCodigo = valores.TryGetValue("PLAN_DEFAULT_CODIGO", out var codigo) ? codigo : string.Empty,
            TopeFactorExcedentes = Num(valores, "TOPE_FACTOR_EXCEDENTES", 2m, 0m),
            AvisoPorcentaje = (int)Math.Clamp(Num(valores, "AVISO_PORCENTAJE", 80m, 1m), 1m, 100m)
        };
    }

    private async Task<IaPlanCreditosDto?> GetPlanEfectivoAsync(SqlConnection cn, string idCliente, IaConfigCreditosDto config, CancellationToken ct)
        => (await GetPlanesAsync(cn, idCliente, ct)).FirstOrDefault()
           ?? ComoPlanDefault(await GetPlanDefaultAsync(cn, config.PlanDefaultCodigo, ct), idCliente);

    private static IaPlanCreditosDto? ComoPlanDefault(IaPlanCreditosDto? planDefault, string idCliente)
        => planDefault is null ? null : new IaPlanCreditosDto
        {
            IdCliente = idCliente,
            IdPlan = planDefault.IdPlan,
            Codigo = planDefault.Codigo,
            PlanNombre = planDefault.PlanNombre,
            PorDefecto = true,
            Precio = planDefault.Precio,
            Moneda = planDefault.Moneda,
            CreditosIncluidos = planDefault.CreditosIncluidos,
            PermiteExcedentes = planDefault.PermiteExcedentes,
            PrecioExcedente = planDefault.PrecioExcedente
        };

    private static async Task<IaPlanCreditosDto?> GetPlanDefaultAsync(SqlConnection cn, string codigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return null;
        return (await GetPlanesCatalogoAsync(cn, soloVisibles: false, ct))
            .Where(p => p.Activo && string.Equals(p.Codigo, codigo.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(p => new IaPlanCreditosDto
            {
                IdPlan = p.IdPlan,
                Codigo = p.Codigo,
                PlanNombre = p.Nombre,
                Precio = p.Precio,
                Moneda = p.Moneda,
                CreditosIncluidos = p.CreditosIncluidos,
                PermiteExcedentes = p.PermiteExcedentes,
                PrecioExcedente = p.PrecioExcedente
            })
            .FirstOrDefault();
    }

    private static async Task<List<IaPlanOpcionDto>> GetPlanesCatalogoAsync(SqlConnection cn, bool soloVisibles, CancellationToken ct)
    {
        if (!await TablaExisteAsync(cn, "Planes", ct) || !await TablaExisteAsync(cn, "Modulos", ct))
            return [];

        return (await cn.QueryAsync<IaPlanOpcionDto>(new CommandDefinition("""
            SELECT p.Id AS IdPlan, LTRIM(RTRIM(p.Codigo)) AS Codigo, p.Nombre, ISNULL(p.Descripcion, N'') AS Descripcion,
                   p.Precio, p.Moneda, ISNULL(p.CantidadIncluida, 0) AS CreditosIncluidos,
                   p.PermiteExcedentes, ISNULL(p.PrecioExcedente, 0) AS PrecioExcedente, p.Activo, p.VisibleCatalogo
            FROM dbo.Planes p
            INNER JOIN dbo.Modulos m ON m.Id = p.IdModulo
            WHERE UPPER(LTRIM(RTRIM(m.Codigo))) = @Codigo
              AND p.TipoFacturacion = N'CREDITOS'
              AND (@SoloVisibles = 0 OR (p.Activo = 1 AND p.VisibleCatalogo = 1))
            ORDER BY ISNULL(p.CantidadIncluida, 0), p.Precio, p.Nombre;
            """, new { Codigo = CodigoModulo, SoloVisibles = soloVisibles }, cancellationToken: ct))).ToList();
    }

    private static async Task<List<IaSolicitudPlanDto>> GetSolicitudesAsync(SqlConnection cn, string? idCliente, CancellationToken ct)
        => (await cn.QueryAsync<IaSolicitudPlanDto>(new CommandDefinition("""
            SELECT s.Id, LTRIM(RTRIM(s.IdCliente)) AS IdCliente, ISNULL(MAX(c.nombre), N'') AS Cliente, s.IdPlan,
                   ISNULL(MAX(p.Nombre), N'') AS PlanNombre, ISNULL(MAX(p.CantidadIncluida), 0) AS PlanCreditos,
                   ISNULL(s.SolicitadoPor, N'') AS SolicitadoPor, s.SolicitadoUtc
            FROM dbo.IA_SOLICITUD_PLAN s
            LEFT JOIN dbo.Clientes c ON LTRIM(RTRIM(c.idcliente)) = LTRIM(RTRIM(s.IdCliente))
            LEFT JOIN dbo.Planes p ON p.Id = s.IdPlan
            WHERE s.Estado = N'PENDIENTE' AND (@IdCliente IS NULL OR LTRIM(RTRIM(s.IdCliente)) = @IdCliente)
            GROUP BY s.Id, s.IdCliente, s.IdPlan, s.SolicitadoPor, s.SolicitadoUtc
            ORDER BY s.SolicitadoUtc DESC;
            """, new { IdCliente = idCliente }, cancellationToken: ct))).ToList();

    private static async Task<string> GetIdClienteDeBaseAsync(SqlConnection cn, int idBase, CancellationToken ct)
        => await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(idcliente)) FROM dbo.bases WHERE id = @IdBase;", new { IdBase = idBase }, cancellationToken: ct)) ?? string.Empty;

    private static async Task<bool> TablaExisteAsync(SqlConnection cn, string tabla, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.' + @Tabla, N'U') IS NULL THEN 0 ELSE 1 END;", new { Tabla = tabla }, cancellationToken: ct)) == 1;

    private static string Recortar(string texto, int max) => texto.Length <= max ? texto : texto[..max];

    // ---------------------------------------------------------------- reglas (testeables)

    /// <summary>
    /// Tope vigente: el manual si existe (0 = sin tope, anula el del plan); si no, el automático del
    /// plan con el porcentaje de aviso general. Null = sin tope.
    /// </summary>
    internal static IaTopeEstadoDto? ResolverTope(string idCliente, (int Tope, int Aviso)? manual, IaPlanCreditosDto? plan,
        decimal factorExcedentes, int avisoPorcentaje, long creditosUsados)
    {
        if (manual is { } m)
            return m.Tope <= 0 ? null : EvaluarTope(idCliente, m.Tope, m.Aviso, creditosUsados);

        if (TopeAutomatico(plan, factorExcedentes) is not int tope)
            return null;

        var estado = EvaluarTope(idCliente, tope, avisoPorcentaje, creditosUsados);
        estado.Origen = IaTopeOrigenes.Plan;
        return estado;
    }

    /// <summary>
    /// Tope automático del plan: sin excedentes, los créditos incluidos; con excedentes, incluidos ×
    /// factor (0 = sin tope automático). Null si el plan no incluye créditos.
    /// </summary>
    internal static int? TopeAutomatico(IaPlanCreditosDto? plan, decimal factorExcedentes)
    {
        if (plan is null || plan.CreditosIncluidos <= 0)
            return null;
        if (!plan.PermiteExcedentes)
            return plan.CreditosIncluidos;
        if (factorExcedentes <= 0)
            return null;
        return (int)Math.Min(int.MaxValue, Math.Ceiling(plan.CreditosIncluidos * factorExcedentes));
    }

    /// <summary>Tope alcanzado si los créditos del mes llegan al tope; aviso desde el porcentaje configurado.</summary>
    internal static IaTopeEstadoDto EvaluarTope(string idCliente, int topeCreditos, int avisoPorcentaje, long creditosUsados)
    {
        var porcentaje = topeCreditos <= 0 ? 100 : (int)Math.Min(999, Math.Floor(creditosUsados * 100d / topeCreditos));
        var alcanzado = creditosUsados >= topeCreditos;
        return new IaTopeEstadoDto
        {
            IdCliente = idCliente,
            TopeCreditos = topeCreditos,
            AvisoPorcentaje = avisoPorcentaje,
            CreditosUsados = creditosUsados,
            Porcentaje = porcentaje,
            Alcanzado = alcanzado,
            EnAviso = !alcanzado && porcentaje >= avisoPorcentaje
        };
    }

    /// <summary>Créditos de un costo: se redondea hacia arriba (un uso mínimo cuenta como 1 crédito).</summary>
    internal static long Creditos(decimal costoUsd, decimal usdPorCredito)
        => costoUsd <= 0 || usdPorCredito <= 0 ? 0 : (long)Math.Ceiling(costoUsd / usdPorCredito);

    /// <summary>
    /// Cargo del mes: abono fijo del plan + créditos excedentes × precio de excedente por cada 1.000
    /// créditos (si el plan lo permite). Por 1.000 porque Planes.PrecioExcedente tiene 2 decimales y el
    /// precio de un solo crédito es de milésimas de dólar.
    /// </summary>
    internal static (long Excedentes, decimal Importe) CalcularCargo(long creditosUsados, IaPlanCreditosDto? plan)
    {
        if (plan is null)
            return (0, 0);

        var excedentes = Math.Max(0, creditosUsados - Math.Max(0, plan.CreditosIncluidos));
        var importe = Math.Max(0, plan.Precio)
            + (plan.PermiteExcedentes ? excedentes * Math.Max(0, plan.PrecioExcedente) / CreditosPorPrecioExcedente : 0);
        return (excedentes, Math.Round(importe, 2));
    }

    /// <summary>Suma <c>amount.value</c> de la respuesta de /v1/organization/costs y devuelve la página siguiente.</summary>
    internal static (decimal Total, string? Siguiente) SumarCostosOpenAi(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var total = 0m;
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var bucket in data.EnumerateArray())
            {
                if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var r in results.EnumerateArray())
                {
                    if (r.TryGetProperty("amount", out var amount) && amount.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number)
                        total += value.GetDecimal();
                }
            }
        }

        var hasMore = doc.RootElement.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True;
        var next = hasMore && doc.RootElement.TryGetProperty("next_page", out var np) && np.ValueKind == JsonValueKind.String ? np.GetString() : null;
        return (total, next);
    }

    /// <summary>Mes calendario de Argentina (hora local del servidor) expresado en UTC.</summary>
    internal static (DateTime Desde, DateTime Hasta) RangoUtc(int anio, int mes)
    {
        var inicio = DateTime.SpecifyKind(new DateTime(anio, mes, 1), DateTimeKind.Unspecified);
        var zona = BuscarZonaArgentina();
        return (TimeZoneInfo.ConvertTimeToUtc(inicio, zona), TimeZoneInfo.ConvertTimeToUtc(inicio.AddMonths(1), zona));
    }

    private static TimeZoneInfo BuscarZonaArgentina()
    {
        foreach (var id in new[] { "Argentina Standard Time", "America/Argentina/Buenos_Aires" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        }
        return TimeZoneInfo.Local;
    }

    // ---------------------------------------------------------------- SQL

    private static async Task<bool> EsquemaListoAsync(SqlConnection cn, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.IA_USO', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.IA_CONFIG', N'U') IS NOT NULL THEN 1 ELSE 0 END;",
            cancellationToken: ct)) == 1;

    private static void RequerirEsquema(bool listo)
    {
        if (!listo)
            throw new InvalidOperationException("Falta aplicar en ALFA_CENTRAL el script 2026-10-05-002__alfa_central_ia_uso.sql.");
    }

    private static async Task<decimal> GetUsdPorCreditoAsync(SqlConnection cn, CancellationToken ct)
    {
        var valor = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT Valor FROM dbo.IA_CONFIG WHERE Clave = N'USD_POR_CREDITO';", cancellationToken: ct));
        return decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : UsdPorCreditoPorDefecto;
    }

    private static async Task<List<IaPlanCreditosDto>> GetPlanesAsync(SqlConnection cn, string? idCliente, CancellationToken ct)
    {
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.ClienteModulos', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Planes', N'U') IS NOT NULL THEN 1 ELSE 0 END;",
                cancellationToken: ct)) == 0)
            return [];

        return (await cn.QueryAsync<IaPlanCreditosDto>(new CommandDefinition("""
            SELECT cm.Id AS IdClienteModulo, LTRIM(RTRIM(cm.IdCliente)) AS IdCliente, p.Id AS IdPlan,
                   LTRIM(RTRIM(p.Codigo)) AS Codigo, ISNULL(p.Nombre, N'') AS PlanNombre,
                   COALESCE(cm.PrecioContratado, p.Precio, 0) AS Precio,
                   COALESCE(NULLIF(LTRIM(RTRIM(cm.MonedaContratada)), N''), p.Moneda, N'ARS') AS Moneda,
                   ISNULL(p.CantidadIncluida, 0) AS CreditosIncluidos,
                   ISNULL(p.PermiteExcedentes, 0) AS PermiteExcedentes,
                   ISNULL(p.PrecioExcedente, 0) AS PrecioExcedente
            FROM dbo.ClienteModulos cm
            INNER JOIN dbo.Modulos m ON m.Id = cm.IdModulo
            INNER JOIN dbo.Planes p ON p.Id = cm.IdPlan
            WHERE UPPER(LTRIM(RTRIM(m.Codigo))) = @Codigo
              AND cm.Estado IN (N'Activo', N'Prueba')
              AND p.TipoFacturacion = N'CREDITOS'
              AND (@IdCliente IS NULL OR LTRIM(RTRIM(cm.IdCliente)) = @IdCliente)
            ORDER BY cm.Id DESC;
            """, new { Codigo = CodigoModulo, IdCliente = idCliente }, cancellationToken: ct))).ToList();
    }

    private void EnsureSuperAdmin()
    {
        if (appUserSession.CurrentUser?.SuperAdmin != true)
            throw new InvalidOperationException("Solo un administrador central puede ver y cobrar el consumo de IA.");
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private sealed class FilaFuncion
    {
        public string Funcion { get; set; } = string.Empty;
        public long Llamadas { get; set; }
        public decimal CostoUsd { get; set; }
        public long LlamadasSinPrecio { get; set; }

        public IaConsumoFuncionDto ToDto(decimal usdPorCredito) => new()
        {
            Funcion = Funcion,
            Llamadas = Llamadas,
            CostoUsd = CostoUsd,
            Creditos = Creditos(CostoUsd, usdPorCredito),
            LlamadasSinPrecio = LlamadasSinPrecio
        };
    }

    private sealed class FilaClienteFuncion
    {
        public string IdCliente { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public string Funcion { get; set; } = string.Empty;
        public long Llamadas { get; set; }
        public decimal CostoUsd { get; set; }
        public long LlamadasSinPrecio { get; set; }

        public IaConsumoFuncionDto ToDto(decimal usdPorCredito) => new()
        {
            Funcion = Funcion,
            Llamadas = Llamadas,
            CostoUsd = CostoUsd,
            Creditos = Creditos(CostoUsd, usdPorCredito),
            LlamadasSinPrecio = LlamadasSinPrecio
        };
    }
}

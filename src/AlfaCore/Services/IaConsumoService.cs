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

    /// <summary>Define (o quita, con <paramref name="topeCreditos"/> null) el tope mensual del cliente. Solo superadmin.</summary>
    Task SaveTopeAsync(string idCliente, int? topeCreditos, int avisoPorcentaje, CancellationToken ct = default);
}

/// <summary>
/// Consumo y cobro de IA (2026-10-05). Lee ALFA_CENTRAL.dbo.IA_USO. 1 crédito = USD_POR_CREDITO de costo
/// de OpenAI (IA_CONFIG, por defecto 0,001); el margen va en el precio del plan. El cargo mensual por
/// cliente sale del plan de tipo CREDITOS asignado al módulo IA_CREDITOS: abono fijo + créditos
/// excedentes × precio de excedente.
/// </summary>
public sealed class IaConsumoService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IHttpClientFactory httpClientFactory,
    IAppEventService appEvents) : IIaConsumoService
{
    private const string ModuleName = "ConsumoIA";
    internal const decimal UsdPorCreditoPorDefecto = 0.001m;
    internal const string CodigoModulo = "IA_CREDITOS";
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
            var usdPorCredito = await GetUsdPorCreditoAsync(cn, ct);
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
            return new IaConsumoBaseDto
            {
                Anio = anio,
                Mes = mes,
                CostoUsdBase = costoBase,
                CreditosBase = Creditos(costoBase, usdPorCredito),
                CreditosCliente = Creditos(costoCliente, usdPorCredito),
                Plan = idCliente.Length == 0 ? null : (await GetPlanesAsync(cn, idCliente, ct)).FirstOrDefault(),
                Tope = idCliente.Length == 0 ? null : await LeerTopeAsync(cn, idCliente, Creditos(costoCliente, usdPorCredito), ct),
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
        var usdPorCredito = await GetUsdPorCreditoAsync(cn, ct);
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
        var topes = (await cn.QueryAsync<(string IdCliente, int TopeCreditos, int AvisoPorcentaje)>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(IdCliente)), TopeCreditos, AvisoPorcentaje FROM dbo.IA_TOPE_CREDITOS WHERE Activo = 1;",
                cancellationToken: ct)))
            .ToDictionary(t => t.IdCliente, t => t, StringComparer.OrdinalIgnoreCase);

        var clientes = filas
            .GroupBy(f => f.IdCliente)
            .Select(g =>
            {
                var costo = g.Sum(x => x.CostoUsd);
                var creditos = Creditos(costo, usdPorCredito);
                planes.TryGetValue(g.Key, out var plan);
                var (excedentes, importe) = CalcularCargo(creditos, plan);
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
                    TopeCreditos = topes.TryGetValue(g.Key, out var tope) ? tope.TopeCreditos : null,
                    AvisoPorcentaje = topes.TryGetValue(g.Key, out var tope2) ? tope2.AvisoPorcentaje : 80,
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
                    "SELECT CASE WHEN OBJECT_ID(N'dbo.IA_TOPE_CREDITOS', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.IA_USO', N'U') IS NOT NULL THEN 1 ELSE 0 END;",
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
                    estado = await LeerTopeAsync(cn, idCliente, Creditos(costo, await GetUsdPorCreditoAsync(cn, ct)), ct);
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
            topeCreditos is null ? "Se quitó el tope mensual de créditos IA." : "Se definió el tope mensual de créditos IA.",
            new { cliente, topeCreditos, aviso }, ct);
    }

    private static async Task<IaTopeEstadoDto?> LeerTopeAsync(SqlConnection cn, string idCliente, long creditosUsados, CancellationToken ct)
    {
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.IA_TOPE_CREDITOS', N'U') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 0)
            return null;

        var filas = await cn.QueryAsync<(int TopeCreditos, int AvisoPorcentaje)>(new CommandDefinition(
            "SELECT TopeCreditos, AvisoPorcentaje FROM dbo.IA_TOPE_CREDITOS WHERE LTRIM(RTRIM(IdCliente)) = @IdCliente AND Activo = 1;",
            new { IdCliente = idCliente }, cancellationToken: ct));
        foreach (var f in filas)
            return EvaluarTope(idCliente, f.TopeCreditos, f.AvisoPorcentaje, creditosUsados);
        return null;
    }

    // ---------------------------------------------------------------- reglas (testeables)

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

    /// <summary>Cargo del mes: abono fijo del plan + créditos excedentes × precio de excedente (si el plan lo permite).</summary>
    internal static (long Excedentes, decimal Importe) CalcularCargo(long creditosUsados, IaPlanCreditosDto? plan)
    {
        if (plan is null)
            return (0, 0);

        var excedentes = Math.Max(0, creditosUsados - Math.Max(0, plan.CreditosIncluidos));
        var importe = Math.Max(0, plan.Precio) + (plan.PermiteExcedentes ? excedentes * Math.Max(0, plan.PrecioExcedente) : 0);
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
            SELECT cm.Id AS IdClienteModulo, LTRIM(RTRIM(cm.IdCliente)) AS IdCliente, ISNULL(p.Nombre, N'') AS PlanNombre,
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

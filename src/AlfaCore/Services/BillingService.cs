using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

/// <summary>
/// Implementación del motor de facturación de suscripciones. Acceso Dapper directo a
/// ALFA_CENTRAL, mismo patrón de connection string que <see cref="CentralAdminService"/>. Ver
/// docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md, Fase 3.
/// </summary>
public sealed class BillingService(
    IConfiguration configuration,
    IAppEventService appEvents,
    IPaymentProvider paymentProvider,
    ISessionService sessionService) : IBillingService
{
    private static readonly JsonSerializerOptions ViewSettingsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    // La configuración de vista (regla 26 de CODEX_RULES.md) es una preferencia por usuario que
    // vive en dbo.TA_CONFIGURACION de la base de cliente activa en la sesión — igual que
    // ContactosService/UsuariosService — NO en ALFA_CENTRAL (que no tiene esa tabla).
    private string TenantConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<CargoDto?> GenerarCargoAsync(int idClienteModulo, DateTime periodoDesde, DateTime periodoHasta, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var clienteModulo = await GetClienteModuloContratoAsync(cn, idClienteModulo, ct).ConfigureAwait(false);
        if (clienteModulo is null)
            throw new AppUserFacingException("El módulo contratado no existe.", "BILLING_CLIENTEMODULO_NO_EXISTE");
        if (clienteModulo.IdPlan is null)
            throw new AppUserFacingException("El módulo contratado no tiene un plan asignado.", "BILLING_SIN_PLAN");

        var plan = await GetPlanRowAsync(cn, clienteModulo.IdPlan.Value, ct).ConfigureAwait(false);
        if (plan is null)
            throw new AppUserFacingException("El plan contratado ya no existe.", "BILLING_PLAN_NO_EXISTE");

        var periodoDesdeDate = periodoDesde.Date;
        var periodoHastaDate = periodoHasta.Date;

        // Idempotencia: la constraint UNIQUE(IdClienteModulo, PeriodoDesde) protege a nivel de
        // base, pero primero se chequea acá para no depender siempre de capturar la excepción.
        var existenteId = await FindCargoIdAsync(cn, idClienteModulo, periodoDesdeDate, ct).ConfigureAwait(false);
        if (existenteId is not null)
            return await GetCargoByIdAsync(cn, existenteId.Value, ct).ConfigureAwait(false);

        var importe = clienteModulo.PrecioContratado ?? plan.Precio;
        var moneda = string.IsNullOrWhiteSpace(clienteModulo.MonedaContratada) ? plan.Moneda : clienteModulo.MonedaContratada;
        var concepto = $"{plan.Nombre} ({periodoDesdeDate:dd/MM/yyyy} a {periodoHastaDate:dd/MM/yyyy})";

        const string insertSql = """
            INSERT INTO dbo.Cargos (IdCliente, IdClienteModulo, Concepto, PeriodoDesde, PeriodoHasta, Importe, Moneda, FechaVencimiento, Estado)
            OUTPUT INSERTED.Id
            VALUES (@IdCliente, @IdClienteModulo, @Concepto, @PeriodoDesde, @PeriodoHasta, @Importe, @Moneda, @FechaVencimiento, @Estado);
            """;

        try
        {
            var id = await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
            {
                clienteModulo.IdCliente,
                IdClienteModulo = idClienteModulo,
                Concepto = concepto,
                PeriodoDesde = periodoDesdeDate,
                PeriodoHasta = periodoHastaDate,
                Importe = importe,
                Moneda = moneda,
                FechaVencimiento = periodoHastaDate,
                Estado = CargoEstados.Pendiente
            }, cancellationToken: ct)).ConfigureAwait(false);

            await appEvents.LogAuditAsync(
                "Billing", "GenerarCargo", "Cargos", id.ToString(),
                $"Cargo generado para el módulo contratado {idClienteModulo}, período {periodoDesdeDate:yyyy-MM-dd}.",
                new { idClienteModulo, periodoDesdeDate, periodoHastaDate, importe, moneda }, ct).ConfigureAwait(false);

            return await GetCargoByIdAsync(cn, id, ct).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Carrera con otro proceso que generó el mismo cargo entre el chequeo y el insert —
            // se resuelve con gracia (se devuelve el cargo ya existente), no como error.
            var idExistente = await FindCargoIdAsync(cn, idClienteModulo, periodoDesdeDate, ct).ConfigureAwait(false);
            return idExistente is null ? null : await GetCargoByIdAsync(cn, idExistente.Value, ct).ConfigureAwait(false);
        }
    }

    public async Task RegistrarPagoManualAsync(RegistrarPagoManualRequest request, string registradoPor, CancellationToken ct = default)
    {
        var idCliente = NormalizeText(request.IdCliente);
        if (string.IsNullOrWhiteSpace(idCliente))
            throw new AppUserFacingException("El cliente es obligatorio.", "BILLING_PAGO_CLIENTE");
        if (request.IdCargo <= 0)
            throw new AppUserFacingException("El cargo es obligatorio.", "BILLING_PAGO_CARGO");
        if (request.Importe <= 0)
            throw new AppUserFacingException("El importe debe ser mayor a cero.", "BILLING_PAGO_IMPORTE");
        if (!MedioPagoValores.Todos.Contains(request.MedioPago))
            throw new AppUserFacingException("El medio de pago no es válido.", "BILLING_PAGO_MEDIO");
        if (!MonedaValores.Todas.Contains(request.Moneda))
            throw new AppUserFacingException("La moneda no es válida.", "BILLING_PAGO_MONEDA");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = cn.BeginTransaction();

        try
        {
            const string cargoSql = """
                SELECT Id, IdCliente, IdClienteModulo, Estado, PeriodoHasta
                FROM dbo.Cargos
                WHERE Id = @IdCargo;
                """;
            var cargo = await cn.QuerySingleOrDefaultAsync<CargoRow>(new CommandDefinition(cargoSql, new { request.IdCargo }, tx, cancellationToken: ct)).ConfigureAwait(false);
            if (cargo is null)
                throw new AppUserFacingException("El cargo indicado no existe.", "BILLING_PAGO_CARGO_NO_EXISTE");
            if (!string.Equals(cargo.IdCliente, idCliente, StringComparison.OrdinalIgnoreCase))
                throw new AppUserFacingException("El cargo no pertenece al cliente indicado.", "BILLING_PAGO_CARGO_CLIENTE_MISMATCH");
            if (string.Equals(cargo.Estado, CargoEstados.Pagado, StringComparison.OrdinalIgnoreCase))
                throw new AppUserFacingException("Ese cargo ya está pagado.", "BILLING_PAGO_CARGO_YA_PAGADO");
            if (string.Equals(cargo.Estado, CargoEstados.Anulado, StringComparison.OrdinalIgnoreCase))
                throw new AppUserFacingException("Ese cargo está anulado, no admite pagos.", "BILLING_PAGO_CARGO_ANULADO");

            await paymentProvider.RegistrarPagoAsync(request, NormalizeText(registradoPor), cn, tx, ct).ConfigureAwait(false);

            const string cargoUpdateSql = "UPDATE dbo.Cargos SET Estado = @Estado, ModificadoUtc = GETUTCDATE() WHERE Id = @IdCargo;";
            await cn.ExecuteAsync(new CommandDefinition(cargoUpdateSql, new { Estado = CargoEstados.Pagado, request.IdCargo }, tx, cancellationToken: ct)).ConfigureAwait(false);

            var clienteModulo = await GetClienteModuloContratoAsync(cn, cargo.IdClienteModulo, ct, tx).ConfigureAwait(false);
            if (clienteModulo is not null)
            {
                // El próximo cobro se calcula desde el FIN del período ya pagado (PeriodoHasta + 1
                // día), no desde la fecha del pago — si se tomara la fecha del pago, un pago
                // atrasado (dentro de la gracia) correría el aniversario de facturación para
                // siempre y el ciclo se iría desalineando con cada mora.
                DateTime? nuevaFechaProximoCobro = null;
                if (clienteModulo.IdPlan is not null && cargo.PeriodoHasta is not null)
                {
                    var plan = await GetPlanRowAsync(cn, clienteModulo.IdPlan.Value, ct, tx).ConfigureAwait(false);
                    if (plan is not null && PlanBillingHelper.EsRecurrente(plan.TipoFacturacion))
                        nuevaFechaProximoCobro = cargo.PeriodoHasta.Value.Date.AddDays(1);
                }

                const string clienteModuloUpdateSql = """
                    UPDATE dbo.ClienteModulos
                    SET FechaProximoCobro = @FechaProximoCobro,
                        Estado = CASE WHEN Estado = N'Suspendido' THEN N'Activo' ELSE Estado END
                    WHERE Id = @Id;
                    """;
                await cn.ExecuteAsync(new CommandDefinition(clienteModuloUpdateSql, new { FechaProximoCobro = nuevaFechaProximoCobro, clienteModulo.Id }, tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        await appEvents.LogAuditAsync(
            "Billing", "RegistrarPagoManual", "Cargos", request.IdCargo.ToString(),
            $"Pago manual registrado para el cargo {request.IdCargo} del cliente {idCliente}.",
            new { idCliente, request.IdCargo, request.Importe, request.Moneda, request.MedioPago, registradoPor = NormalizeText(registradoPor) },
            ct).ConfigureAwait(false);
    }

    public async Task ProcesarVencimientosAsync(CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT cm.Id AS IdClienteModulo, cm.FechaProximoCobro, p.TipoFacturacion, p.CantidadIncluida
            FROM dbo.ClienteModulos cm
            JOIN dbo.Planes p ON p.Id = cm.IdPlan
            WHERE cm.Estado = N'Activo'
              AND cm.IdPlan IS NOT NULL
              AND cm.FechaProximoCobro IS NOT NULL
              AND cm.FechaProximoCobro <= GETUTCDATE()
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.Cargos c
                  WHERE c.IdClienteModulo = cm.Id
                    AND c.PeriodoDesde = CAST(cm.FechaProximoCobro AS date)
                    AND c.Estado IN (N'PENDIENTE', N'VENCIDO')
              );
            """;
        var candidatos = (await cn.QueryAsync<VencimientoCandidatoRow>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false)).ToArray();

        foreach (var candidato in candidatos)
        {
            try
            {
                var periodoDesde = candidato.FechaProximoCobro.Date;
                var proximoCiclo = PlanBillingHelper.CalcularProximoCobro(candidato.TipoFacturacion, periodoDesde, candidato.CantidadIncluida);
                var periodoHasta = proximoCiclo?.Date.AddDays(-1) ?? periodoDesde;

                await GenerarCargoAsync(candidato.IdClienteModulo, periodoDesde, periodoHasta, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await appEvents.LogErrorAsync(
                    "Billing", "ProcesarVencimientos", ex,
                    "No se pudo generar el cargo de un módulo vencido.",
                    new { candidato.IdClienteModulo }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
            }
        }
    }

    public async Task ProcesarGraciaYSuspensionAsync(CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT c.Id AS IdCargo, c.IdClienteModulo, cm.IdCliente, cm.IdModulo
            FROM dbo.Cargos c
            JOIN dbo.ClienteModulos cm ON cm.Id = c.IdClienteModulo
            JOIN dbo.Planes p ON p.Id = cm.IdPlan
            WHERE c.Estado = N'PENDIENTE'
              AND DATEADD(day, p.DiasGracia, c.FechaVencimiento) < GETUTCDATE();
            """;
        var candidatos = (await cn.QueryAsync<GraciaCandidatoRow>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false)).ToArray();

        foreach (var candidato in candidatos)
        {
            await using var tx = cn.BeginTransaction();
            try
            {
                const string cargoUpdateSql = "UPDATE dbo.Cargos SET Estado = N'VENCIDO', ModificadoUtc = GETUTCDATE() WHERE Id = @IdCargo;";
                await cn.ExecuteAsync(new CommandDefinition(cargoUpdateSql, new { candidato.IdCargo }, tx, cancellationToken: ct)).ConfigureAwait(false);

                // Réplica puntual (un solo UPDATE) de lo que hace
                // CentralAdminService.SuspenderModuloAsync — no se llama a ese servicio acá para no
                // salir de esta transacción a mitad de camino (abriría su propia conexión aparte).
                const string suspenderSql = "UPDATE dbo.ClienteModulos SET Estado = N'Suspendido' WHERE Id = @IdClienteModulo AND Estado = N'Activo';";
                await cn.ExecuteAsync(new CommandDefinition(suspenderSql, new { candidato.IdClienteModulo }, tx, cancellationToken: ct)).ConfigureAwait(false);

                tx.Commit();

                await appEvents.LogAuditAsync(
                    "Billing", "ProcesarGraciaYSuspension", "ClienteModulos", candidato.IdCliente,
                    $"Cargo {candidato.IdCargo} vencido fuera de gracia — módulo {candidato.IdModulo} suspendido.",
                    new { candidato.IdCargo, candidato.IdClienteModulo, candidato.IdModulo }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                await appEvents.LogErrorAsync(
                    "Billing", "ProcesarGraciaYSuspension", ex,
                    "No se pudo procesar la gracia/suspensión de un cargo vencido.",
                    new { candidato.IdCargo, candidato.IdClienteModulo }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
            }
        }
    }

    public async Task<PagedResult<CargoDto>> SearchCargosAsync(CargosFilters filters, CancellationToken ct = default)
    {
        filters ??= new CargosFilters();
        var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
        var pageNumber = Math.Max(1, filters.PageNumber);
        var skip = (pageNumber - 1) * pageSize;
        var idCliente = NormalizeText(filters.IdCliente);
        var estado = NormalizeText(filters.Estado);

        const string whereSql = """
            WHERE (@IdCliente = '' OR c.IdCliente = @IdCliente)
              AND (@Estado = '' OR c.Estado = @Estado)
              AND (@FechaVencimientoDesde IS NULL OR c.FechaVencimiento >= @FechaVencimientoDesde)
              AND (@FechaVencimientoHasta IS NULL OR c.FechaVencimiento <= @FechaVencimientoHasta)
            """;

        var sql = $"""
            SELECT c.Id, c.IdCliente, c.IdClienteModulo, c.Concepto, c.PeriodoDesde, c.PeriodoHasta,
                   c.Importe, c.Moneda, c.FechaEmision, c.FechaVencimiento, c.Estado, c.CreadoUtc, c.ModificadoUtc,
                   ISNULL(cl.nombre, '') AS ClienteNombre
            FROM dbo.Cargos c
            LEFT JOIN dbo.Clientes cl ON cl.idcliente = c.IdCliente
            {whereSql}
            ORDER BY c.FechaVencimiento DESC, c.Id DESC
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT COUNT(*)
            FROM dbo.Cargos c
            {whereSql};
            """;

        var parameters = new
        {
            IdCliente = idCliente,
            Estado = estado,
            filters.FechaVencimientoDesde,
            filters.FechaVencimientoHasta,
            Skip = skip,
            PageSize = pageSize
        };

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        var items = (await multi.ReadAsync<CargoDto>().ConfigureAwait(false)).ToArray();
        var total = await multi.ReadSingleAsync<int>().ConfigureAwait(false);

        return new PagedResult<CargoDto>
        {
            Items = items,
            Total = total,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<PagoDto>> SearchPagosAsync(PagosFilters filters, CancellationToken ct = default)
    {
        filters ??= new PagosFilters();
        var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
        var pageNumber = Math.Max(1, filters.PageNumber);
        var skip = (pageNumber - 1) * pageSize;
        var idCliente = NormalizeText(filters.IdCliente);
        var estado = NormalizeText(filters.Estado);
        var medioPago = NormalizeText(filters.MedioPago);

        const string whereSql = """
            WHERE (@IdCliente = '' OR p.IdCliente = @IdCliente)
              AND (@Estado = '' OR p.Estado = @Estado)
              AND (@MedioPago = '' OR p.MedioPago = @MedioPago)
              AND (@FechaDesde IS NULL OR p.Fecha >= @FechaDesde)
              AND (@FechaHasta IS NULL OR p.Fecha <= @FechaHasta)
            """;

        var sql = $"""
            SELECT p.Id, p.IdCliente, p.IdCargo, p.Fecha, p.Importe, p.Moneda, p.Estado, p.MedioPago,
                   p.Provider, p.ProviderPaymentId, p.ProviderTransactionId, p.Referencia, p.Observaciones,
                   p.RegistradoPor, p.FechaAprobacion, p.CreadoUtc, p.ModificadoUtc,
                   ISNULL(cl.nombre, '') AS ClienteNombre
            FROM dbo.Pagos p
            LEFT JOIN dbo.Clientes cl ON cl.idcliente = p.IdCliente
            {whereSql}
            ORDER BY p.Fecha DESC, p.Id DESC
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT COUNT(*)
            FROM dbo.Pagos p
            {whereSql};
            """;

        var parameters = new
        {
            IdCliente = idCliente,
            Estado = estado,
            MedioPago = medioPago,
            filters.FechaDesde,
            filters.FechaHasta,
            Skip = skip,
            PageSize = pageSize
        };

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        var items = (await multi.ReadAsync<PagoDto>().ConfigureAwait(false)).ToArray();
        var total = await multi.ReadSingleAsync<int>().ConfigureAwait(false);

        return new PagedResult<PagoDto>
        {
            Items = items,
            Total = total,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public Task<CargosViewSettingsDto> GetCargosViewSettingsAsync(string userName, CancellationToken ct = default)
        => GetViewSettingsAsync(userName, "USUVIEW-CARGOS-", CreateDefaultCargosViewSettings, NormalizeCargosViewSettings, ct);

    public Task SaveCargosViewSettingsAsync(string userName, CargosViewSettingsDto settings, CancellationToken ct = default)
        => SaveViewSettingsAsync(userName, settings, "USUVIEW-CARGOS-", "CARGOS", NormalizeCargosViewSettings, ct);

    public Task<PagosViewSettingsDto> GetPagosViewSettingsAsync(string userName, CancellationToken ct = default)
        => GetViewSettingsAsync(userName, "USUVIEW-PAGOS-", CreateDefaultPagosViewSettings, NormalizePagosViewSettings, ct);

    public Task SavePagosViewSettingsAsync(string userName, PagosViewSettingsDto settings, CancellationToken ct = default)
        => SaveViewSettingsAsync(userName, settings, "USUVIEW-PAGOS-", "PAGOS", NormalizePagosViewSettings, ct);

    /// <summary>
    /// Núcleo genérico de lectura de configuración de vista — misma mecánica que
    /// <c>ContactosService</c>/<c>UsuariosService</c> (clave hasheada por usuario, columna de
    /// detalle resuelta según el esquema de <c>TA_CONFIGURACION</c> de la base activa).
    /// </summary>
    private async Task<TSettings> GetViewSettingsAsync<TSettings>(
        string userName, string keyPrefix, Func<TSettings> createDefault, Func<TSettings?, TSettings> normalize, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return createDefault();

        await using var cn = new SqlConnection(TenantConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct).ConfigureAwait(false);
        var configKey = BuildViewConfigKey(keyPrefix, userName);

        var sql = $"""
            SELECT TOP (1) ISNULL(VALOR, '') AS Valor, ISNULL({detailColumn}, '') AS ValorAux
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """;

        var row = await cn.QuerySingleOrDefaultAsync<ViewSettingsRow>(
            new CommandDefinition(sql, new { Clave = configKey.ToUpperInvariant() }, cancellationToken: ct)).ConfigureAwait(false);

        var raw = row is null ? string.Empty : ResolveStoredValue(row.Valor, row.ValorAux);
        if (string.IsNullOrWhiteSpace(raw))
            return createDefault();

        var parsed = JsonSerializer.Deserialize<TSettings>(raw, ViewSettingsJsonOptions);
        return normalize(parsed);
    }

    private async Task SaveViewSettingsAsync<TSettings>(
        string userName, TSettings settings, string keyPrefix, string grupo, Func<TSettings?, TSettings> normalize, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new AppUserFacingException("No hay un usuario logueado para guardar la vista.", "BILLING_VIEWSETTINGS_SIN_USUARIO");

        var normalized = normalize(settings);
        var serialized = JsonSerializer.Serialize(normalized, ViewSettingsJsonOptions);

        await using var cn = new SqlConnection(TenantConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct).ConfigureAwait(false);
        var (valor, valorAux) = SplitStoredValue(serialized);
        var configKey = BuildViewConfigKey(keyPrefix, userName);

        var sql = $"""
            UPDATE dbo.TA_CONFIGURACION
            SET VALOR = @Valor, {detailColumn} = @ValorAux, GRUPO = @Grupo, FechaHora_Modificacion = GETDATE()
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO, FechaHora_Grabacion, FechaHora_Modificacion)
                VALUES (@Clave, @Valor, @ValorAux, @Grupo, GETDATE(), GETDATE());
            END;
            """;

        await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            ClaveNormalizada = configKey.ToUpperInvariant(),
            Clave = configKey,
            Valor = valor,
            ValorAux = valorAux,
            Grupo = grupo
        }, cancellationToken: ct)).ConfigureAwait(false);

        await appEvents.LogAuditAsync(
            "Billing", "SaveViewSettings", "TA_CONFIGURACION", configKey,
            $"Configuración de vista de {grupo.ToLowerInvariant()} actualizada.",
            new { UserName = userName.Trim() }, ct).ConfigureAwait(false);
    }

    private static CargosViewSettingsDto CreateDefaultCargosViewSettings() => new()
    {
        AgruparPor = CargosViewGroupKeys.None,
        Columnas =
        [
            new() { Key = CargosViewColumnKeys.Cliente,     Label = "Cliente",     Visible = true, Order = 0 },
            new() { Key = CargosViewColumnKeys.Concepto,    Label = "Concepto",    Visible = true, Order = 1 },
            new() { Key = CargosViewColumnKeys.Periodo,     Label = "Período",     Visible = true, Order = 2 },
            new() { Key = CargosViewColumnKeys.Importe,     Label = "Importe",     Visible = true, Order = 3 },
            new() { Key = CargosViewColumnKeys.Moneda,      Label = "Moneda",      Visible = true, Order = 4 },
            new() { Key = CargosViewColumnKeys.Vencimiento, Label = "Vencimiento", Visible = true, Order = 5 },
            new() { Key = CargosViewColumnKeys.Estado,      Label = "Estado",      Visible = true, Order = 6 }
        ]
    };

    private static CargosViewSettingsDto NormalizeCargosViewSettings(CargosViewSettingsDto? settings)
    {
        var defaults = CreateDefaultCargosViewSettings();
        if (settings is null || settings.Columnas.Count == 0)
            return defaults;

        var normalized = new CargosViewSettingsDto
        {
            AgruparPor = settings.AgruparPor == CargosViewGroupKeys.Estado ? CargosViewGroupKeys.Estado : CargosViewGroupKeys.None,
            Columnas = defaults.Columnas.Select(defaultCol =>
            {
                var source = settings.Columnas.FirstOrDefault(c => string.Equals(c.Key, defaultCol.Key, StringComparison.OrdinalIgnoreCase));
                return source is null
                    ? new CargosViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = defaultCol.Visible, Order = defaultCol.Order }
                    : new CargosViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = source.Visible, Order = source.Order };
            }).OrderBy(c => c.Order).ToList()
        };

        if (normalized.Columnas.All(c => !c.Visible))
            normalized.Columnas[0].Visible = true;

        return normalized;
    }

    private static PagosViewSettingsDto CreateDefaultPagosViewSettings() => new()
    {
        AgruparPor = PagosViewGroupKeys.None,
        Columnas =
        [
            new() { Key = PagosViewColumnKeys.Cliente,    Label = "Cliente",        Visible = true, Order = 0 },
            new() { Key = PagosViewColumnKeys.Fecha,      Label = "Fecha",          Visible = true, Order = 1 },
            new() { Key = PagosViewColumnKeys.Importe,    Label = "Importe",        Visible = true, Order = 2 },
            new() { Key = PagosViewColumnKeys.Moneda,     Label = "Moneda",         Visible = true, Order = 3 },
            new() { Key = PagosViewColumnKeys.MedioPago,  Label = "Medio de pago",  Visible = true, Order = 4 },
            new() { Key = PagosViewColumnKeys.Estado,     Label = "Estado",         Visible = true, Order = 5 },
            new() { Key = PagosViewColumnKeys.Referencia, Label = "Referencia",     Visible = false, Order = 6 }
        ]
    };

    private static PagosViewSettingsDto NormalizePagosViewSettings(PagosViewSettingsDto? settings)
    {
        var defaults = CreateDefaultPagosViewSettings();
        if (settings is null || settings.Columnas.Count == 0)
            return defaults;

        var normalized = new PagosViewSettingsDto
        {
            AgruparPor = settings.AgruparPor == PagosViewGroupKeys.Estado ? PagosViewGroupKeys.Estado : PagosViewGroupKeys.None,
            Columnas = defaults.Columnas.Select(defaultCol =>
            {
                var source = settings.Columnas.FirstOrDefault(c => string.Equals(c.Key, defaultCol.Key, StringComparison.OrdinalIgnoreCase));
                return source is null
                    ? new PagosViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = defaultCol.Visible, Order = defaultCol.Order }
                    : new PagosViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = source.Visible, Order = source.Order };
            }).OrderBy(c => c.Order).ToList()
        };

        if (normalized.Columnas.All(c => !c.Visible))
            normalized.Columnas[0].Visible = true;

        return normalized;
    }

    private static string BuildViewConfigKey(string prefix, string userName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant())));
        return $"{prefix}{hash[..24]}";
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
        var column = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length > 150 ? (string.Empty, normalized) : (normalized, string.Empty);
    }

    private static async Task<ClienteModuloContratoRow?> GetClienteModuloContratoAsync(SqlConnection cn, int idClienteModulo, CancellationToken ct, SqlTransaction? tx = null)
    {
        const string sql = """
            SELECT Id, IdCliente, IdModulo, Estado, IdPlan, PrecioContratado, MonedaContratada, FechaProximoCobro
            FROM dbo.ClienteModulos
            WHERE Id = @IdClienteModulo;
            """;
        return await cn.QuerySingleOrDefaultAsync<ClienteModuloContratoRow>(new CommandDefinition(sql, new { IdClienteModulo = idClienteModulo }, tx, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task<PlanRow?> GetPlanRowAsync(SqlConnection cn, int idPlan, CancellationToken ct, SqlTransaction? tx = null)
    {
        const string sql = """
            SELECT Id, Codigo, Nombre, TipoFacturacion, Precio, Moneda, DiasGracia, CantidadIncluida
            FROM dbo.Planes
            WHERE Id = @IdPlan;
            """;
        return await cn.QuerySingleOrDefaultAsync<PlanRow>(new CommandDefinition(sql, new { IdPlan = idPlan }, tx, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task<int?> FindCargoIdAsync(SqlConnection cn, int idClienteModulo, DateTime periodoDesde, CancellationToken ct)
    {
        const string sql = "SELECT TOP (1) Id FROM dbo.Cargos WHERE IdClienteModulo = @IdClienteModulo AND PeriodoDesde = @PeriodoDesde;";
        return await cn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new { IdClienteModulo = idClienteModulo, PeriodoDesde = periodoDesde }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task<CargoDto?> GetCargoByIdAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        const string sql = """
            SELECT Id, IdCliente, IdClienteModulo, Concepto, PeriodoDesde, PeriodoHasta, Importe, Moneda,
                   FechaEmision, FechaVencimiento, Estado, CreadoUtc, ModificadoUtc
            FROM dbo.Cargos
            WHERE Id = @Id;
            """;
        return await cn.QuerySingleOrDefaultAsync<CargoDto>(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private sealed class ClienteModuloContratoRow
    {
        public int Id { get; set; }
        public string IdCliente { get; set; } = string.Empty;
        public int IdModulo { get; set; }
        public string Estado { get; set; } = string.Empty;
        public int? IdPlan { get; set; }
        public decimal? PrecioContratado { get; set; }
        public string? MonedaContratada { get; set; }
        public DateTime? FechaProximoCobro { get; set; }
    }

    private sealed class PlanRow
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string TipoFacturacion { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public string Moneda { get; set; } = string.Empty;
        public int DiasGracia { get; set; }
        public int? CantidadIncluida { get; set; }
    }

    private sealed class CargoRow
    {
        public int Id { get; set; }
        public string IdCliente { get; set; } = string.Empty;
        public int IdClienteModulo { get; set; }
        public string Estado { get; set; } = string.Empty;
        public DateTime? PeriodoHasta { get; set; }
    }

    private sealed class VencimientoCandidatoRow
    {
        public int IdClienteModulo { get; set; }
        public DateTime FechaProximoCobro { get; set; }
        public string TipoFacturacion { get; set; } = string.Empty;
        public int? CantidadIncluida { get; set; }
    }

    private sealed class GraciaCandidatoRow
    {
        public int IdCargo { get; set; }
        public int IdClienteModulo { get; set; }
        public string IdCliente { get; set; } = string.Empty;
        public int IdModulo { get; set; }
    }

    private sealed class ViewSettingsRow
    {
        public string Valor { get; set; } = string.Empty;
        public string ValorAux { get; set; } = string.Empty;
    }
}

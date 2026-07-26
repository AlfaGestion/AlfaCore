using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaTurnoService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IPuntoVentaTurnoValidator validator,
    ILogger<PuntoVentaTurnoService> logger) : IPuntoVentaTurnoService
{
    private const string ModuleName = "PuntoVentaTurno";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<IReadOnlyList<PuntoVentaDenominacionDto>> GetDenominacionesAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetDenominaciones", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var rows = await cn.QueryAsync<PuntoVentaDenominacionDto>(new CommandDefinition("""
                SELECT ID, VALOR AS Valor, TIPO AS Tipo, ORDEN AS Orden
                FROM dbo.POS_DENOMINACION
                WHERE ACTIVO = 1
                ORDER BY ORDEN;
                """, cancellationToken: token));

            return (IReadOnlyList<PuntoVentaDenominacionDto>)rows.ToList();
        }, "No se pudieron cargar las denominaciones.", ct);

    public Task<PuntoVentaTurnoDto?> GetTurnoAbiertoAsync(int idPuntoVenta, string idCaja, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTurnoAbierto", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var id = await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
                SELECT TOP (1) ID
                FROM dbo.POS_TURNO
                WHERE IDPUNTOVENTA = @IdPuntoVenta AND IDCAJA = @IdCaja AND ESTADO = @Estado
                ORDER BY ID DESC;
                """,
                new { IdPuntoVenta = idPuntoVenta, IdCaja = idCaja, Estado = PuntoVentaTurnoEstadoKeys.Abierto },
                cancellationToken: token));

            if (id is null)
                return null;

            return await LoadTurnoAsync(cn, id.Value, token);
        }, "No se pudo verificar el turno de caja abierto.", ct);

    public Task<PuntoVentaTurnoDto?> GetTurnoByIdAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTurnoById", async token =>
        {
            if (id <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            return await LoadTurnoAsync(cn, id, token);
        }, "No se pudo cargar el turno de caja.", ct);

    private static async Task<PuntoVentaTurnoDto?> LoadTurnoAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        const string headerSql = """
            SELECT
                ID, IDPUNTOVENTA, ISNULL(IDCAJA, '') AS IdCaja, FECHAOPERATIVA AS FechaOperativa,
                FECHAHORA_APERTURA AS FechaHoraApertura, FECHAHORA_CIERRE AS FechaHoraCierre,
                ISNULL(USUARIO_APERTURA, '') AS UsuarioApertura, ISNULL(USUARIO_CIERRE, '') AS UsuarioCierre,
                SALDOINICIAL AS SaldoInicial, SALDOFINAL_DECLARADO AS SaldoFinalDeclarado,
                SALDOFINAL_TEORICO AS SaldoFinalTeorico, DIFERENCIA AS Diferencia,
                ESTADO AS Estado, ISNULL(OBSERVACIONES, '') AS Observaciones
            FROM dbo.POS_TURNO
            WHERE ID = @Id;
            """;

        var turno = await cn.QuerySingleOrDefaultAsync<PuntoVentaTurnoDto>(new CommandDefinition(headerSql, new { Id = id }, cancellationToken: ct));
        if (turno is null)
            return null;

        const string billetesSql = """
            SELECT b.MOMENTO, b.IDDENOMINACION AS IdDenominacion, d.VALOR AS Valor, d.TIPO AS Tipo, b.CANTIDAD AS Cantidad
            FROM dbo.POS_TURNO_BILLETES b
            INNER JOIN dbo.POS_DENOMINACION d ON d.ID = b.IDDENOMINACION
            WHERE b.IDTURNO = @Id
            ORDER BY d.ORDEN;
            """;

        var rows = await cn.QueryAsync(new CommandDefinition(billetesSql, new { Id = id }, cancellationToken: ct));
        foreach (var row in rows)
        {
            var billete = new PuntoVentaTurnoBilleteDto
            {
                IdDenominacion = (int)row.IdDenominacion,
                Valor = (decimal)row.Valor,
                Tipo = (string)row.Tipo,
                Cantidad = (int)row.Cantidad
            };

            if (string.Equals((string)row.MOMENTO, PuntoVentaTurnoMomentoKeys.Apertura, StringComparison.OrdinalIgnoreCase))
                turno.BilletesApertura.Add(billete);
            else
                turno.BilletesCierre.Add(billete);
        }

        return turno;
    }

    public Task<int> AbrirTurnoAsync(PuntoVentaTurnoAperturaRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AbrirTurno", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Observaciones = (request.Observaciones ?? string.Empty).Trim();

            var validation = await validator.ValidateAperturaAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            try
            {
                var usuario = NormalizeUser(request.UsuarioAccion);

                const string insertSql = """
                    INSERT INTO dbo.POS_TURNO
                        (IDPUNTOVENTA, IDCAJA, FECHAOPERATIVA, FECHAHORA_APERTURA, USUARIO_APERTURA,
                         SALDOINICIAL, ESTADO, OBSERVACIONES, FECHAHORA_ALTA)
                    VALUES
                        (@IdPuntoVenta, @IdCaja, CAST(GETDATE() AS date), GETDATE(), @Usuario,
                         @SaldoInicial, @Estado, @Observaciones, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;

                var idTurno = await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                {
                    request.IdPuntoVenta,
                    request.IdCaja,
                    Usuario = usuario,
                    request.SaldoInicial,
                    Estado = PuntoVentaTurnoEstadoKeys.Abierto,
                    request.Observaciones
                }, transaction: (SqlTransaction)tx, cancellationToken: token));

                await InsertBilletesAsync(cn, tx, idTurno, PuntoVentaTurnoMomentoKeys.Apertura, request.Billetes, token);

                await tx.CommitAsync(token);
                return idTurno;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo abrir el turno de caja.", ct);

    public Task CerrarTurnoAsync(PuntoVentaTurnoCierreRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CerrarTurno", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Observaciones = (request.Observaciones ?? string.Empty).Trim();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var turno = await LoadTurnoAsync(cn, request.IdTurno, token)
                ?? throw new InvalidOperationException("No se encontró el turno indicado.");

            var validation = await validator.ValidateCierreAsync(request, turno, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            var saldoTeorico = await GetSaldoTeoricoActualInternalAsync(cn, request.IdTurno, token);
            var diferencia = request.SaldoFinalDeclarado - saldoTeorico;
            var usuario = NormalizeUser(request.UsuarioAccion);

            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                var affected = await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.POS_TURNO
                    SET FECHAHORA_CIERRE = GETDATE(),
                        USUARIO_CIERRE = @Usuario,
                        SALDOFINAL_DECLARADO = @SaldoFinalDeclarado,
                        SALDOFINAL_TEORICO = @SaldoFinalTeorico,
                        DIFERENCIA = @Diferencia,
                        ESTADO = @Estado,
                        OBSERVACIONES = @Observaciones,
                        FECHAHORA_MODIFICACION = GETDATE()
                    WHERE ID = @Id AND ESTADO = @EstadoAbierto;
                    """,
                    new
                    {
                        Id = request.IdTurno,
                        Usuario = usuario,
                        request.SaldoFinalDeclarado,
                        SaldoFinalTeorico = saldoTeorico,
                        Diferencia = diferencia,
                        Estado = PuntoVentaTurnoEstadoKeys.Cerrado,
                        request.Observaciones,
                        EstadoAbierto = PuntoVentaTurnoEstadoKeys.Abierto
                    },
                    transaction: (SqlTransaction)tx, cancellationToken: token));

                if (affected <= 0)
                    throw new InvalidOperationException("El turno ya estaba cerrado.");

                await InsertBilletesAsync(cn, tx, request.IdTurno, PuntoVentaTurnoMomentoKeys.Cierre, request.Billetes, token);

                await tx.CommitAsync(token);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo cerrar el turno de caja.", ct);

    public Task<decimal> GetSaldoTeoricoActualAsync(int idTurno, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetSaldoTeoricoActual", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await GetSaldoTeoricoActualInternalAsync(cn, idTurno, token);
        }, "No se pudo calcular el saldo teórico del turno.", ct);

    private static async Task<decimal> GetSaldoTeoricoActualInternalAsync(SqlConnection cn, int idTurno, CancellationToken ct)
    {
        var saldoInicial = await cn.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition(
            "SELECT SALDOINICIAL FROM dbo.POS_TURNO WHERE ID = @Id;", new { Id = idTurno }, cancellationToken: ct)) ?? 0;

        var totalEfectivo = await cn.QuerySingleAsync<decimal>(new CommandDefinition(
            "SELECT ISNULL(SUM(IMPORTEEFECTIVO), 0) FROM dbo.POS_TURNO_MOVIMIENTO WHERE IDTURNO = @Id;",
            new { Id = idTurno }, cancellationToken: ct));

        return saldoInicial + totalEfectivo;
    }

    public Task RegistrarMovimientoAsync(int idTurno, string tc, string idComprobante, decimal importeTotal, decimal importeEfectivo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "RegistrarMovimiento", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.POS_TURNO_MOVIMIENTO (IDTURNO, TC, IDCOMPROBANTE, IMPORTETOTAL, IMPORTEEFECTIVO, FECHAHORA)
                VALUES (@IdTurno, @Tc, @IdComprobante, @ImporteTotal, @ImporteEfectivo, GETDATE());
                """,
                new { IdTurno = idTurno, Tc = tc, IdComprobante = idComprobante, ImporteTotal = importeTotal, ImporteEfectivo = importeEfectivo },
                cancellationToken: token));

            return true;
        }, "No se pudo registrar el movimiento del turno de caja.", ct);

    public Task RegistrarCorteXAsync(PuntoVentaTurnoCorteXRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "RegistrarCorteX", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Observaciones = (request.Observaciones ?? string.Empty).Trim();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var turno = await LoadTurnoAsync(cn, request.IdTurno, token)
                ?? throw new InvalidOperationException("No se encontró el turno indicado.");

            var validation = await validator.ValidateCorteXAsync(request, turno, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            var saldoTeorico = await GetSaldoTeoricoActualInternalAsync(cn, request.IdTurno, token);
            var diferencia = request.SaldoContado.HasValue ? request.SaldoContado.Value - saldoTeorico : (decimal?)null;
            var usuario = NormalizeUser(request.UsuarioAccion);

            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                const string insertSql = """
                    INSERT INTO dbo.POS_TURNO_CORTE_X
                        (IDTURNO, USUARIO, FECHAHORA, SALDOTEORICO, SALDOCONTADO, DIFERENCIA, OBSERVACIONES)
                    VALUES
                        (@IdTurno, @Usuario, GETDATE(), @SaldoTeorico, @SaldoContado, @Diferencia, @Observaciones);
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;

                var idCorte = await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                {
                    request.IdTurno,
                    Usuario = usuario,
                    SaldoTeorico = saldoTeorico,
                    request.SaldoContado,
                    Diferencia = diferencia,
                    request.Observaciones
                }, transaction: (SqlTransaction)tx, cancellationToken: token));

                const string insertBilleteSql = """
                    INSERT INTO dbo.POS_TURNO_CORTE_X_BILLETES (IDCORTE, IDDENOMINACION, CANTIDAD)
                    VALUES (@IdCorte, @IdDenominacion, @Cantidad);
                    """;

                foreach (var billete in request.Billetes.Where(b => b.Cantidad > 0))
                {
                    await cn.ExecuteAsync(new CommandDefinition(insertBilleteSql, new
                    {
                        IdCorte = idCorte,
                        billete.IdDenominacion,
                        billete.Cantidad
                    }, transaction: (SqlTransaction)tx, cancellationToken: token));
                }

                await tx.CommitAsync(token);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo registrar el corte X.", ct);

    public Task<IReadOnlyList<PuntoVentaTurnoCorteXDto>> GetCortesXAsync(int idTurno, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetCortesX", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var rows = await cn.QueryAsync<PuntoVentaTurnoCorteXDto>(new CommandDefinition("""
                SELECT
                    ID, IDTURNO, ISNULL(USUARIO, '') AS Usuario, FECHAHORA,
                    SALDOTEORICO AS SaldoTeorico, SALDOCONTADO AS SaldoContado, DIFERENCIA AS Diferencia,
                    ISNULL(OBSERVACIONES, '') AS Observaciones
                FROM dbo.POS_TURNO_CORTE_X
                WHERE IDTURNO = @IdTurno
                ORDER BY FECHAHORA DESC;
                """, new { IdTurno = idTurno }, cancellationToken: token));

            return (IReadOnlyList<PuntoVentaTurnoCorteXDto>)rows.ToList();
        }, "No se pudieron cargar los cortes X del turno.", ct);

    public Task<IReadOnlyList<PuntoVentaCajaSaldoDto>> GetSaldosPorCajaAsync(int idPuntoVenta, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetSaldosPorCaja", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var turnos = await cn.QueryAsync(new CommandDefinition("""
                SELECT ID, ISNULL(IDCAJA, '') AS IdCaja, ISNULL(USUARIO_APERTURA, '') AS UsuarioApertura,
                       FECHAHORA_APERTURA AS FechaHoraApertura, SALDOINICIAL AS SaldoInicial
                FROM dbo.POS_TURNO
                WHERE IDPUNTOVENTA = @IdPuntoVenta AND ESTADO = @Estado
                ORDER BY IDCAJA;
                """,
                new { IdPuntoVenta = idPuntoVenta, Estado = PuntoVentaTurnoEstadoKeys.Abierto },
                cancellationToken: token));

            var items = new List<PuntoVentaCajaSaldoDto>();
            foreach (var row in turnos)
            {
                int idTurno = row.ID;
                decimal saldoInicial = row.SaldoInicial;
                var saldoTeorico = await GetSaldoTeoricoActualInternalAsync(cn, idTurno, token);

                items.Add(new PuntoVentaCajaSaldoDto
                {
                    IdTurno = idTurno,
                    IdCaja = (string)row.IdCaja,
                    UsuarioApertura = (string)row.UsuarioApertura,
                    FechaHoraApertura = (DateTime)row.FechaHoraApertura,
                    SaldoInicial = saldoInicial,
                    SaldoTeoricoActual = saldoTeorico
                });
            }

            return (IReadOnlyList<PuntoVentaCajaSaldoDto>)items;
        }, "No se pudieron cargar los saldos por caja.", ct);

    private static async Task InsertBilletesAsync(
        SqlConnection cn,
        System.Data.Common.DbTransaction tx,
        int idTurno,
        string momento,
        IEnumerable<PuntoVentaTurnoBilleteDetalleRequest> billetes,
        CancellationToken ct)
    {
        const string insertSql = """
            INSERT INTO dbo.POS_TURNO_BILLETES (IDTURNO, MOMENTO, IDDENOMINACION, CANTIDAD)
            VALUES (@IdTurno, @Momento, @IdDenominacion, @Cantidad);
            """;

        foreach (var billete in billetes.Where(b => b.Cantidad > 0))
        {
            await cn.ExecuteAsync(new CommandDefinition(insertSql, new
            {
                IdTurno = idTurno,
                Momento = momento,
                billete.IdDenominacion,
                billete.Cantidad
            }, transaction: (SqlTransaction)tx, cancellationToken: ct));
        }
    }

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private static string BuildValidationMessage(ValidationResult validation)
    {
        var issues = validation.Issues
            .Select(issue => (issue.Message ?? string.Empty).Trim())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return issues.Count == 0
            ? "Revisá los campos marcados antes de continuar."
            : string.Join(" ", issues);
    }

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
}

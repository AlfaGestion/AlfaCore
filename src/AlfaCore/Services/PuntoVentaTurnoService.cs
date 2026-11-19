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

    public Task<PuntoVentaTurnoCierreResultDto> CerrarTurnoAsync(PuntoVentaTurnoCierreRequest request, CancellationToken ct = default)
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
            var resultado = new PuntoVentaTurnoCierreResultDto { Diferencia = diferencia };

            var lineasTransferencia = request.Transferencia
                .Where(l => !string.IsNullOrWhiteSpace(l.Cuenta) && l.Importe != 0)
                .ToList();
            var cajaDestino = (request.IdCajaDestino ?? string.Empty).Trim();
            if (lineasTransferencia.Count > 0 && cajaDestino.Length == 0)
                throw new InvalidOperationException("Elegí la caja a la que se transfiere.");
            if (lineasTransferencia.Count > 0 && string.Equals(cajaDestino, turno.IdCaja.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La caja destino tiene que ser distinta de la caja del turno.");
            if (request.FondoQueda is < 0)
                throw new InvalidOperationException("El fondo que queda en la caja no puede ser negativo.");

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
                        FONDO_QUEDA = @FondoQueda,
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
                        request.FondoQueda,
                        EstadoAbierto = PuntoVentaTurnoEstadoKeys.Abierto
                    },
                    transaction: (SqlTransaction)tx, cancellationToken: token));

                if (affected <= 0)
                    throw new InvalidOperationException("El turno ya estaba cerrado.");

                await InsertBilletesAsync(cn, tx, request.IdTurno, PuntoVentaTurnoMomentoKeys.Cierre, request.Billetes, token);

                if (lineasTransferencia.Count > 0)
                {
                    var (comprobante, total, descDestino) = await GrabarTransferenciaAsync(
                        cn, (SqlTransaction)tx, turno.IdCaja.Trim(), cajaDestino, lineasTransferencia, usuario, token);

                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.POS_TURNO
                        SET TRANSF_IDCAJA = @Destino, TRANSF_CPTE = @Comprobante, TRANSF_IMPORTE = @Total
                        WHERE ID = @Id;
                        """,
                        new { Id = request.IdTurno, Destino = cajaDestino, Comprobante = comprobante, Total = total },
                        transaction: (SqlTransaction)tx, cancellationToken: token));

                    resultado.Comprobante = comprobante;
                    resultado.CajaDestino = descDestino;
                    resultado.ImporteTransferido = total;
                }

                await tx.CommitAsync(token);
                return resultado;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo cerrar el turno de caja.", ct);

    // ------------------------------------------------------------ transferencia al cerrar
    // Misma logica que CybTrans.frm (legacy, llamado desde el cierre de comandas con @DESDECOMANDA):
    // saldos D-H por cuenta de medio de pago (CH/DB/EF/TJ) de la caja en el periodo del turno, caja
    // destino propuesta = TA_CONFIGURACION.TPV_CAJADESTINO, TC = EQU_CYB_Transferencias (CJA).

    private const string MediosTransferibles = "'CH','DB','EF','TJ'";

    public Task<PuntoVentaTurnoCierrePreviewDto> GetCierrePreviewAsync(int idTurno, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetCierrePreview", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var turno = await LoadTurnoAsync(cn, idTurno, token)
                ?? throw new InvalidOperationException("No se encontró el turno indicado.");
            var caja = turno.IdCaja.Trim();

            var preview = new PuntoVentaTurnoCierrePreviewDto
            {
                SaldoTeorico = await GetSaldoTeoricoActualInternalAsync(cn, idTurno, token)
            };

            if (await ObjectExistsAsync(cn, "dbo.V_Ta_Cajas", token))
            {
                preview.Cajas = (await cn.QueryAsync<PuntoVentaCajaOpcionDto>(new CommandDefinition("""
                    SELECT LTRIM(RTRIM(IdCajas)) AS IdCaja, LTRIM(RTRIM(ISNULL(Descripcion, ''))) AS Descripcion
                    FROM dbo.V_Ta_Cajas
                    WHERE LTRIM(RTRIM(IdCajas)) <> @Caja
                    ORDER BY Descripcion;
                    """, new { Caja = caja }, cancellationToken: token))).ToList();
            }

            var sugerida = (await ReadConfigAsync(cn, "TPV_CAJADESTINO", token)).Trim();
            if (preview.Cajas.Any(c => string.Equals(c.IdCaja, sugerida, StringComparison.OrdinalIgnoreCase)))
                preview.IdCajaDestinoSugerida = sugerida;

            var cuentaCaja = (await ReadConfigAsync(cn, "CUENTA_CAJA", token)).Trim();
            preview.Saldos = (await cn.QueryAsync<PuntoVentaTurnoSaldoMedioDto>(new CommandDefinition($"""
                SELECT LTRIM(RTRIM(c.CODIGO)) AS Cuenta,
                       LTRIM(RTRIM(ISNULL(c.DESCRIPCION, ''))) AS Descripcion,
                       LTRIM(RTRIM(c.MedioDePago)) AS MedioDePago,
                       ISNULL(a.MONEDA, '   1') AS Moneda,
                       CAST(ISNULL(NULLIF(a.COTIZACION, 0), 1) AS decimal(18, 6)) AS Cotizacion,
                       CAST(SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE -a.IMPORTE END) AS decimal(18, 2)) AS Saldo
                FROM dbo.MV_ASIENTOS a
                INNER JOIN dbo.MA_CUENTAS c ON c.CODIGO = a.CUENTA
                WHERE LTRIM(RTRIM(a.IDCAJAS)) = @Caja
                  AND a.FECHA >= @Desde AND a.FECHA < DATEADD(day, 1, CAST(GETDATE() AS date))
                  AND c.MedioDePago IN ({MediosTransferibles})
                GROUP BY c.CODIGO, c.DESCRIPCION, c.MedioDePago, a.MONEDA, a.COTIZACION
                HAVING SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE -a.IMPORTE END) <> 0
                ORDER BY CASE WHEN c.MedioDePago = 'EF' THEN 0 ELSE 1 END, c.DESCRIPCION;
                """,
                new { Caja = caja, Desde = turno.FechaOperativa.Date }, cancellationToken: token))).ToList();

            foreach (var s in preview.Saldos)
                s.EsEfectivoLocal = s.MedioDePago == "EF"
                    && (cuentaCaja.Length == 0 || string.Equals(s.Cuenta, cuentaCaja, StringComparison.OrdinalIgnoreCase))
                    && s.Moneda.Trim() is "" or "1";

            return preview;
        }, "No se pudieron calcular los saldos de la caja.", ct);

    public Task<decimal?> GetFondoSugeridoAsync(int idPuntoVenta, string idCaja, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetFondoSugerido", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await cn.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
                SELECT TOP (1) FONDO_QUEDA
                FROM dbo.POS_TURNO
                WHERE IDPUNTOVENTA = @IdPuntoVenta AND LTRIM(RTRIM(ISNULL(IDCAJA, ''))) = @Caja
                  AND ESTADO = @Cerrado
                ORDER BY FECHAHORA_CIERRE DESC, ID DESC;
                """,
                new { IdPuntoVenta = idPuntoVenta, Caja = (idCaja ?? string.Empty).Trim(), Cerrado = PuntoVentaTurnoEstadoKeys.Cerrado },
                cancellationToken: token));
        }, "No se pudo obtener el fondo del turno anterior.", ct);

    private static async Task<(string Comprobante, decimal Total, string DescripcionDestino)> GrabarTransferenciaAsync(
        SqlConnection cn, SqlTransaction tx, string cajaOrigen, string cajaDestino,
        IReadOnlyList<PuntoVentaTurnoTransferenciaLineaRequest> lineas, string usuario, CancellationToken ct)
    {
        var tc = (await ReadConfigAsync(cn, "EQU_CYB_Transferencias", ct, tx)).Trim();
        if (tc.Length == 0)
            tc = "CJA";
        const string sucursal = "0001";
        const string letra = "X";

        var unegocioConfig = (await ReadConfigAsync(cn, "UNEGOCIO", ct, tx)).Trim();
        var cajas = (await cn.QueryAsync<(string IdCaja, string Descripcion, string UNegocio)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(IdCajas)), LTRIM(RTRIM(ISNULL(Descripcion, ''))), LTRIM(RTRIM(ISNULL(UNIDADNEGOCIO, '')))
            FROM dbo.V_Ta_Cajas
            WHERE LTRIM(RTRIM(IdCajas)) IN @Cajas;
            """, new { Cajas = new[] { cajaOrigen, cajaDestino } }, transaction: tx, cancellationToken: ct)))
            .ToDictionary(c => c.IdCaja, StringComparer.OrdinalIgnoreCase);

        if (!cajas.TryGetValue(cajaDestino, out var destino))
            throw new InvalidOperationException($"La caja destino {cajaDestino} no existe.");
        cajas.TryGetValue(cajaOrigen, out var origen);

        string Fmt(string valor, int largo) => (valor ?? string.Empty).Trim().PadLeft(largo);
        var unOrigen = Fmt(string.IsNullOrWhiteSpace(origen.UNegocio) ? (unegocioConfig.Length > 0 ? unegocioConfig : "1") : origen.UNegocio, 4);
        var unDestino = Fmt(string.IsNullOrWhiteSpace(destino.UNegocio) ? (unegocioConfig.Length > 0 ? unegocioConfig : "1") : destino.UNegocio, 4);

        // Numeracion y numero de asiento con el mismo criterio que sp_web_CreaAsientoIngresoEgreso
        // (secuencia de MV_ASIENTOS para TC/0001/X y TIPO_REG CJA), bloqueando para que dos cierres
        // simultaneos no tomen el mismo numero.
        var ultimoNumero = await cn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition("""
            SELECT MAX(NUMERO)
            FROM dbo.MV_ASIENTOS WITH (UPDLOCK, HOLDLOCK)
            WHERE TC = @Tc AND SUCURSAL = @Sucursal AND LETRA = @Letra;
            """, new { Tc = tc, Sucursal = sucursal, Letra = letra }, transaction: tx, cancellationToken: ct));
        var numero = (int.TryParse((ultimoNumero ?? string.Empty).Trim(), out var ultimo) ? ultimo : 0) + 1;
        var numeroTexto = numero.ToString("00000000", System.Globalization.CultureInfo.InvariantCulture);

        var fecha = DateTime.Today;
        var periodo = await cn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition("""
            SELECT TOP (1) PERIODO FROM dbo.MV_EJERCICIOS WHERE [FECHA DESDE] <= @Fecha AND [FECHA HASTA] >= @Fecha;
            """, new { Fecha = fecha }, transaction: tx, cancellationToken: ct)) ?? "0";

        var nroAsiento = await cn.QuerySingleAsync<int>(new CommandDefinition("""
            SELECT ISNULL(MAX([NUMERO ASIENTO]), 0) + 1
            FROM dbo.MV_ASIENTOS WITH (UPDLOCK, HOLDLOCK)
            WHERE MES_OPERATIVO = @Mes AND LTRIM(RTRIM(TIPO_REG)) = 'CJA' AND LTRIM(RTRIM(PERIODO)) = @Periodo;
            """, new { Mes = fecha.Month, Periodo = periodo.Trim() }, transaction: tx, cancellationToken: ct));

        var total = lineas.Sum(l => Math.Abs(l.Importe));
        var detalle = $"Transferencias ({cajaOrigen}-{cajaDestino}) cierre de caja";
        var secuencia = 0;

        foreach (var linea in lineas)
        {
            var importe = Math.Abs(linea.Importe);
            // Saldo positivo: sale de la caja origen (H) y entra en la destino (D). Negativo: al reves.
            var origenHaber = linea.Importe > 0;
            var moneda = string.IsNullOrWhiteSpace(linea.Moneda) ? "   1" : Fmt(linea.Moneda, 4);
            var cotizacion = linea.Cotizacion <= 0 ? 1 : linea.Cotizacion;

            foreach (var (caja, un, dh) in new[]
            {
                (cajaDestino, unDestino, origenHaber ? "D" : "H"),
                (cajaOrigen, unOrigen, origenHaber ? "H" : "D")
            })
            {
                secuencia++;
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.MV_ASIENTOS
                        (CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE, TC, SUCURSAL, NUMERO, LETRA,
                         [DEBE-HABER], IMPORTE, MONEDA, COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO,
                         FECHAHORA_GRABACION, FechaSubdiario, USUARIO_LOGEADO, IDCAJAS, UNEGOCIO, CPTE_ORIGEN)
                    VALUES
                        (@Cuenta, @Secuencia, @Mes, @NroAsiento, @Fecha, @Detalle, @Tc, @Sucursal, @Numero, @Letra,
                         @Dh, @Importe, @Moneda, @Cotizacion, @Periodo, @Total, 'CJA', 0,
                         GETDATE(), @Fecha, @Usuario, @Caja, @UNegocio, 'TRANSF:');
                    """,
                    new
                    {
                        Cuenta = linea.Cuenta.Trim(),
                        Secuencia = secuencia,
                        Mes = fecha.Month,
                        NroAsiento = nroAsiento,
                        Fecha = fecha,
                        Detalle = detalle,
                        Tc = tc,
                        Sucursal = sucursal,
                        Numero = numeroTexto,
                        Letra = letra,
                        Dh = dh,
                        Importe = importe,
                        Moneda = moneda,
                        Cotizacion = cotizacion,
                        Periodo = periodo.Trim(),
                        Total = total,
                        Usuario = usuario,
                        Caja = Fmt(caja, 4),
                        UNegocio = un
                    },
                    transaction: tx, cancellationToken: ct));
            }
        }

        var descDestino = string.IsNullOrWhiteSpace(destino.Descripcion) ? $"Caja {cajaDestino}" : destino.Descripcion;
        return ($"{tc} {sucursal}-{numeroTexto}{letra}", total, descDestino);
    }

    private static async Task<string> ReadConfigAsync(SqlConnection cn, string clave, CancellationToken ct, SqlTransaction? tx = null)
        => await cn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
               "SELECT TOP (1) LTRIM(RTRIM(ISNULL(VALOR, ''))) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);",
               new { Clave = clave }, transaction: tx, cancellationToken: ct)) ?? string.Empty;

    private static async Task<bool> ObjectExistsAsync(SqlConnection cn, string nombre, CancellationToken ct)
        => await cn.QuerySingleAsync<int>(new CommandDefinition(
               "SELECT CASE WHEN OBJECT_ID(@Nombre) IS NULL THEN 0 ELSE 1 END;",
               new { Nombre = nombre }, cancellationToken: ct)) == 1;

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

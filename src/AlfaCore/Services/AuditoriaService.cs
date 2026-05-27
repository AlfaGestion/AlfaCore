using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class AuditoriaService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IAuditoriaService
{
    private const string ConfigGroup = "AUDITORIA";
    private const string HoraInicioLaboralKey = "AUDITORIA-USUARIOS-HORA-INICIO-LABORAL";
    private const string HoraFinLaboralKey = "AUDITORIA-USUARIOS-HORA-FIN-LABORAL";
    private const string DiasLaboralesKey = "AUDITORIA-USUARIOS-DIAS-LABORALES";
    private const string ControlarPcHabitualKey = "AUDITORIA-USUARIOS-CONTROLAR-PC-HABITUAL";
    private const string DiasRmSinFacturaKey = "AUDITORIA-USUARIOS-DIAS-RM-SIN-FACTURA";
    private const string UmbralModificacionesKey = "AUDITORIA-USUARIOS-UMBRAL-MODIFICACIONES";
    private const string DiasDuplicadosCompraKey = "AUDITORIA-USUARIOS-DIAS-DUPLICADOS-COMPRA";
    private const string SoloSucursalDuplicadosKey = "AUDITORIA-USUARIOS-SOLO-SUCURSAL-DUPLICADOS";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<AuditoriaResumenDto> GetResumenAsync(CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Auditoria", "GetResumen", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var today = await ScalarIntAsync("""
                SELECT COUNT(*)
                FROM dbo.AUX_ERR
                WHERE CAST(Fecha AS date) = CAST(GETDATE() AS date)
                """, cn, token);

            var last7 = await ScalarIntAsync("""
                SELECT COUNT(*)
                FROM dbo.AUX_ERR
                WHERE Fecha >= DATEADD(day, -7, GETDATE())
                """, cn, token);

            var series = await QueryRankingAsync("""
                SELECT TOP (14)
                    CONVERT(varchar(10), CAST(Fecha AS date), 103) AS Label,
                    COUNT(*) AS Value
                FROM dbo.AUX_ERR
                WHERE Fecha >= DATEADD(day, -14, GETDATE())
                GROUP BY CAST(Fecha AS date)
                ORDER BY CAST(Fecha AS date)
                """, cn, token);

            var processes = await QueryRankingAsync("""
                SELECT TOP (8)
                    ISNULL(NULLIF(LTRIM(RTRIM(Proceso)), ''), '(sin proceso)') AS Label,
                    COUNT(*) AS Value
                FROM dbo.AUX_ERR
                WHERE Fecha >= DATEADD(day, -30, GETDATE())
                GROUP BY Proceso
                ORDER BY COUNT(*) DESC, Proceso
                """, cn, token);

            var users = await QueryRankingAsync("""
                SELECT TOP (8)
                    ISNULL(NULLIF(LTRIM(RTRIM(Usuario)), ''), '(sin usuario)') AS Label,
                    COUNT(*) AS Value
                FROM dbo.AUX_ERR
                WHERE Fecha >= DATEADD(day, -30, GETDATE())
                GROUP BY Usuario
                ORDER BY COUNT(*) DESC, Usuario
                """, cn, token);

            return new AuditoriaResumenDto
            {
                ErrorsToday = today,
                ErrorsLast7Days = last7,
                TopProcess = processes.FirstOrDefault()?.Label ?? "—",
                TopUser = users.FirstOrDefault()?.Label ?? "—",
                ErrorsByDay = series.Select(x => new AuditoriaSerieDto { Label = x.Label, Value = x.Value }).ToList(),
                TopProcesses = processes,
                TopUsers = users
            };
        }, "No se pudo cargar el resumen de auditoría.", ct);
    }

    public async Task<IReadOnlyList<AuditoriaErrorRowDto>> SearchErrorsAsync(AuditoriaErrorFilterDto filter, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Auditoria", "SearchErrors", async token =>
        {
            var sql = $"""
                SELECT TOP ({Math.Max(1, filter.MaxRows)})
                    ID,
                    Fecha,
                    ISNULL(Proceso, ''),
                    ISNULL(Error, 0),
                    ISNULL(Descripcion, ''),
                    ISNULL(Sql, ''),
                    ISNULL(Pc, ''),
                    ISNULL(Usuario, '')
                FROM dbo.AUX_ERR
                WHERE (@Desde IS NULL OR Fecha >= @Desde)
                  AND (@Hasta IS NULL OR Fecha < DATEADD(day, 1, @Hasta))
                  AND (@Usuario = '' OR ISNULL(Usuario, '') = @Usuario)
                  AND (@Proceso = '' OR ISNULL(Proceso, '') = @Proceso)
                  AND (@Pc = '' OR ISNULL(Pc, '') LIKE '%' + @Pc + '%')
                  AND (@Error IS NULL OR ISNULL(Error, 0) = @Error)
                  AND (
                        @Texto = ''
                        OR ISNULL(Descripcion, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(Sql, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(Proceso, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(Usuario, '') LIKE '%' + @Texto + '%'
                      )
                ORDER BY Fecha DESC, ID DESC
                """;

            var items = new List<AuditoriaErrorRowDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Desde", (object?)filter.Desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta", (object?)filter.Hasta ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Usuario", filter.Usuario ?? string.Empty);
            cmd.Parameters.AddWithValue("@Proceso", filter.Proceso ?? string.Empty);
            cmd.Parameters.AddWithValue("@Pc", filter.Pc ?? string.Empty);
            cmd.Parameters.AddWithValue("@Error", (object?)filter.ErrorCodigo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Texto", filter.Texto ?? string.Empty);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(MapError(rd));
            }

            return (IReadOnlyList<AuditoriaErrorRowDto>)items;
        }, "No se pudieron buscar errores de auditoría.", ct);
    }

    public async Task<AuditoriaErrorRowDto?> GetErrorByIdAsync(int id, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Auditoria", "GetErrorById", async token =>
        {
            const string sql = """
                SELECT
                    ID,
                    Fecha,
                    ISNULL(Proceso, ''),
                    ISNULL(Error, 0),
                    ISNULL(Descripcion, ''),
                    ISNULL(Sql, ''),
                    ISNULL(Pc, ''),
                    ISNULL(Usuario, '')
                FROM dbo.AUX_ERR
                WHERE ID = @Id
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            return await rd.ReadAsync(token) ? MapError(rd) : null;
        }, "No se pudo cargar el error solicitado.", ct);
    }

    public async Task<IReadOnlyList<string>> GetUsuariosAsync(CancellationToken ct = default)
    {
        return await QueryDistinctAsync("Usuario", ct);
    }

    public async Task<IReadOnlyList<string>> GetProcesosAsync(CancellationToken ct = default)
    {
        return await QueryDistinctAsync("Proceso", ct);
    }

    public async Task<AuditoriaUsuarioLookupsDto> GetUserAuditLookupsAsync(DateTime? desde = null, DateTime? hasta = null, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Auditoria", "GetUserAuditLookups", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var defaults = await ReadUserAuditDefaultsAsync(cn, token);
            var usuarios = await QueryStringListAsync(
                """
                SELECT DISTINCT TOP (200)
                    LTRIM(RTRIM(ISNULL(USUARIO, ''))) AS Usuario
                FROM (
                    SELECT USUARIO
                    FROM dbo.V_MV_CpteAcciones
                    WHERE (@Desde IS NULL OR FECHAHORA >= @Desde)
                      AND (@Hasta IS NULL OR FECHAHORA < DATEADD(day, 1, @Hasta))
                    UNION ALL
                    SELECT USUARIO
                    FROM dbo.CONTROL_ACCESO
                    WHERE (@Desde IS NULL OR INGRESO >= @Desde)
                      AND (@Hasta IS NULL OR INGRESO < DATEADD(day, 1, @Hasta))
                ) AS src
                WHERE ISNULL(USUARIO, '') <> ''
                ORDER BY LTRIM(RTRIM(ISNULL(USUARIO, '')));
                """,
                cn,
                desde,
                hasta,
                token);

            var pcs = await QueryStringListAsync(
                """
                SELECT DISTINCT TOP (200)
                    LTRIM(RTRIM(ISNULL(PC, ''))) AS Pc
                FROM dbo.V_MV_CpteAcciones
                WHERE (@Desde IS NULL OR FECHAHORA >= @Desde)
                  AND (@Hasta IS NULL OR FECHAHORA < DATEADD(day, 1, @Hasta))
                  AND ISNULL(PC, '') <> ''
                ORDER BY LTRIM(RTRIM(ISNULL(PC, '')));
                """,
                cn,
                desde,
                hasta,
                token);

            var tiposComprobante = await QueryStringListAsync(
                """
                SELECT DISTINCT TOP (50)
                    LTRIM(RTRIM(ISNULL(TC, ''))) AS TC
                FROM dbo.V_MV_CpteAcciones
                WHERE (@Desde IS NULL OR FECHAHORA >= @Desde)
                  AND (@Hasta IS NULL OR FECHAHORA < DATEADD(day, 1, @Hasta))
                  AND ISNULL(TC, '') <> ''
                ORDER BY LTRIM(RTRIM(ISNULL(TC, '')));
                """,
                cn,
                desde,
                hasta,
                token);

            var cuentasClientes = await QueryStringListAsync(
                """
                SELECT DISTINCT TOP (200)
                    LTRIM(RTRIM(
                        CASE
                            WHEN ISNULL(CUENTA, '') <> '' AND ISNULL(NOMBRE, '') <> '' THEN CUENTA + N' · ' + NOMBRE
                            WHEN ISNULL(CUENTA, '') <> '' THEN CUENTA
                            ELSE ISNULL(NOMBRE, '')
                        END
                    )) AS ClienteCuenta
                FROM dbo.V_MV_Cpte
                WHERE (@Desde IS NULL OR FECHA >= @Desde)
                  AND (@Hasta IS NULL OR FECHA < DATEADD(day, 1, @Hasta))
                  AND (ISNULL(CUENTA, '') <> '' OR ISNULL(NOMBRE, '') <> '')
                ORDER BY LTRIM(RTRIM(
                    CASE
                        WHEN ISNULL(CUENTA, '') <> '' AND ISNULL(NOMBRE, '') <> '' THEN CUENTA + N' · ' + NOMBRE
                        WHEN ISNULL(CUENTA, '') <> '' THEN CUENTA
                        ELSE ISNULL(NOMBRE, '')
                    END
                ));
                """,
                cn,
                desde,
                hasta,
                token);

            return new AuditoriaUsuarioLookupsDto
            {
                Usuarios = usuarios,
                Pcs = pcs,
                TiposComprobante = tiposComprobante,
                CuentasClientes = cuentasClientes,
                TiposControl =
                [
                    new AuditoriaLookupItemDto { Value = "IN_NO_GRABADO", Label = "Comprobantes iniciados y no grabados" },
                    new AuditoriaLookupItemDto { Value = "RM_PARCIAL", Label = "Remitos facturados parcialmente" },
                    new AuditoriaLookupItemDto { Value = "RM_SIN_FACTURA", Label = "Remitos sin factura" },
                    new AuditoriaLookupItemDto { Value = "RM_DE_MAS", Label = "Remitos facturados de más" },
                    new AuditoriaLookupItemDto { Value = "CPRA_DUPLICADO", Label = "Posibles comprobantes duplicados" },
                    new AuditoriaLookupItemDto { Value = "MOD_EXCESIVAS", Label = "Modificaciones excesivas" },
                    new AuditoriaLookupItemDto { Value = "BAJAS", Label = "Bajas de comprobantes" },
                    new AuditoriaLookupItemDto { Value = "FUERA_HORARIO", Label = "Actividad fuera de horario" },
                    new AuditoriaLookupItemDto { Value = "INGRESO_SIN_FINAL", Label = "Ingreso sensible sin operación final" }
                ],
                Riesgos =
                [
                    new AuditoriaLookupItemDto { Value = "ALTO", Label = "Alto" },
                    new AuditoriaLookupItemDto { Value = "MEDIO", Label = "Medio" },
                    new AuditoriaLookupItemDto { Value = "BAJO", Label = "Bajo" }
                ],
                DiasMinimosSinFacturaDefault = defaults.DiasMinimosSinFactura,
                UmbralModificacionesDefault = defaults.UmbralModificaciones,
                DiasToleranciaDuplicadosDefault = defaults.DiasToleranciaDuplicados,
                SoloDiferenciasSucursalDefault = defaults.SoloDiferenciasSucursal
            };
        }, "No se pudieron cargar las opciones de auditoría de usuarios.", ct);
    }

    public async Task<AuditoriaComprobanteLookupsDto> GetComprobanteAuditLookupsAsync(DateTime? desde = null, DateTime? hasta = null, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Auditoria", "GetComprobanteAuditLookups", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var tiposComprobante = await QueryStringListAsync(
                """
                SELECT DISTINCT TOP (50)
                    LTRIM(RTRIM(ISNULL(TC, ''))) AS TC
                FROM dbo.V_MV_Cpte
                WHERE (@Desde IS NULL OR FECHA >= @Desde)
                  AND (@Hasta IS NULL OR FECHA < DATEADD(day, 1, @Hasta))
                  AND ISNULL(TC, '') <> ''
                ORDER BY LTRIM(RTRIM(ISNULL(TC, '')));
                """,
                cn,
                desde,
                hasta,
                token);

            var cuentasClientes = await QueryStringListAsync(
                """
                SELECT DISTINCT TOP (200)
                    LTRIM(RTRIM(
                        CASE
                            WHEN ISNULL(CUENTA, '') <> '' AND ISNULL(NOMBRE, '') <> '' THEN CUENTA + N' · ' + NOMBRE
                            WHEN ISNULL(CUENTA, '') <> '' THEN CUENTA
                            ELSE ISNULL(NOMBRE, '')
                        END
                    )) AS ClienteCuenta
                FROM dbo.V_MV_Cpte
                WHERE (@Desde IS NULL OR FECHA >= @Desde)
                  AND (@Hasta IS NULL OR FECHA < DATEADD(day, 1, @Hasta))
                  AND (ISNULL(CUENTA, '') <> '' OR ISNULL(NOMBRE, '') <> '')
                ORDER BY LTRIM(RTRIM(
                    CASE
                        WHEN ISNULL(CUENTA, '') <> '' AND ISNULL(NOMBRE, '') <> '' THEN CUENTA + N' · ' + NOMBRE
                        WHEN ISNULL(CUENTA, '') <> '' THEN CUENTA
                        ELSE ISNULL(NOMBRE, '')
                    END
                ));
                """,
                cn,
                desde,
                hasta,
                token);

            return new AuditoriaComprobanteLookupsDto
            {
                TiposComprobante = tiposComprobante,
                CuentasClientes = cuentasClientes,
                TiposControl =
                [
                    new AuditoriaLookupItemDto { Value = "DUPLICADO_POTENCIAL", Label = "Duplicados potenciales" },
                    new AuditoriaLookupItemDto { Value = "DETALLE_CABECERA", Label = "Cabecera vs detalle" }
                ],
                Riesgos =
                [
                    new AuditoriaLookupItemDto { Value = "ALTO", Label = "Alto" },
                    new AuditoriaLookupItemDto { Value = "MEDIO", Label = "Medio" }
                ]
            };
        }, "No se pudieron cargar los filtros de auditoría de comprobantes.", ct);
    }

    public async Task<AuditoriaUsuarioSettingsDto> GetUserAuditSettingsAsync(CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Auditoria", "GetUserAuditSettings", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var inferred = await InferUserAuditSettingsAsync(cn, token);
            return await ReadUserAuditSettingsAsync(cn, inferred, token);
        }, "No se pudo cargar la configuración de auditoría de usuarios.", ct);
    }

    public async Task SaveUserAuditSettingsAsync(AuditoriaUsuarioSettingsDto settings, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Auditoria", "SaveUserAuditSettings", async token =>
        {
            ArgumentNullException.ThrowIfNull(settings);

            var inicio = Math.Clamp(settings.HoraInicioLaboral, 0, 23);
            var fin = Math.Clamp(settings.HoraFinLaboral, 1, 24);
            if (fin <= inicio)
            {
                var validation = new ValidationResult();
                validation.Add(nameof(settings.HoraFinLaboral), "La hora de fin laboral debe ser mayor que la hora de inicio.");
                throw new AppValidationException("Revisá la configuración horaria antes de guardar.", validation);
            }

            var dias = NormalizeDays(settings.DiasLaborales);
            if (dias.Count == 0)
            {
                var validation = new ValidationResult();
                validation.Add(nameof(settings.DiasLaborales), "Debés seleccionar al menos un día laboral.");
                throw new AppValidationException("Revisá la configuración de días laborales antes de guardar.", validation);
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);

            await UpsertConfigValueAsync(cn, detailColumn, HoraInicioLaboralKey, inicio.ToString("00"), token);
            await UpsertConfigValueAsync(cn, detailColumn, HoraFinLaboralKey, fin.ToString("00"), token);
            await UpsertConfigValueAsync(cn, detailColumn, DiasLaboralesKey, string.Join(",", dias), token);
            await UpsertConfigValueAsync(cn, detailColumn, ControlarPcHabitualKey, settings.ControlarPcHabitual ? "SI" : "NO", token);

            await appEvents.LogAuditAsync(
                "Auditoria",
                "SaveUserAuditSettings",
                "TA_CONFIGURACION",
                "AUDITORIA-USUARIOS-HORARIO",
                "Configuración de auditoría de usuarios actualizada.",
                new
                {
                    HoraInicioLaboral = inicio,
                    HoraFinLaboral = fin,
                    DiasLaborales = string.Join(",", dias),
                    settings.ControlarPcHabitual
                },
                token);

            return true;
        }, "No se pudo guardar la configuración de auditoría de usuarios.", ct);
    }

    private static async Task<IReadOnlyList<string>> QueryStringListAsync(
        string sql,
        SqlConnection cn,
        DateTime? desde,
        DateTime? hasta,
        CancellationToken ct)
    {
        var items = new List<string>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Desde", (object?)desde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Hasta", (object?)hasta ?? DBNull.Value);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var value = ReadString(rd, 0).Trim();
            if (!string.IsNullOrWhiteSpace(value))
                items.Add(value);
        }

        return items;
    }

    public async Task<AuditoriaUsuarioResultDto> SearchUserAuditAsync(AuditoriaUsuarioFilterDto filter, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Auditoria", "SearchUserAudit", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var defaults = await ReadUserAuditDefaultsAsync(cn, token);
            var inferredSettings = await InferUserAuditSettingsAsync(cn, token);
            var auditSettings = await ReadUserAuditSettingsAsync(cn, inferredSettings, token);
            var diasMinimos = filter.DiasMinimosSinFactura.GetValueOrDefault(defaults.DiasMinimosSinFactura);
            var umbralModificaciones = filter.UmbralModificaciones.GetValueOrDefault(defaults.UmbralModificaciones);
            var diasToleranciaDuplicados = Math.Max(0, filter.DiasToleranciaDuplicados.GetValueOrDefault(defaults.DiasToleranciaDuplicados));
            var soloDiferenciasSucursal = filter.SoloDiferenciasSucursal;
            var pagina = Math.Max(1, filter.Pagina);
            var tamanio = Math.Clamp(filter.TamanioPagina, 10, 200);
            var offset = (pagina - 1) * tamanio;
            var orderBy = ResolveUserAuditOrderBy(filter.OrdenCampo, filter.OrdenDireccion);

            var sql = $"""
                SET DATEFIRST 1;

                IF OBJECT_ID('tempdb..#AuditUser') IS NOT NULL DROP TABLE #AuditUser;
                IF OBJECT_ID('tempdb..#AccionesPeriodo') IS NOT NULL DROP TABLE #AccionesPeriodo;
                IF OBJECT_ID('tempdb..#Acciones90') IS NOT NULL DROP TABLE #Acciones90;
                IF OBJECT_ID('tempdb..#ControlPeriodo') IS NOT NULL DROP TABLE #ControlPeriodo;
                IF OBJECT_ID('tempdb..#RemitosPeriodo') IS NOT NULL DROP TABLE #RemitosPeriodo;
                IF OBJECT_ID('tempdb..#AplicacionesRm') IS NOT NULL DROP TABLE #AplicacionesRm;
                IF OBJECT_ID('tempdb..#FacturasRm') IS NOT NULL DROP TABLE #FacturasRm;
                IF OBJECT_ID('tempdb..#EstadoRemitos') IS NOT NULL DROP TABLE #EstadoRemitos;
                IF OBJECT_ID('tempdb..#HabitualPc') IS NOT NULL DROP TABLE #HabitualPc;
                IF OBJECT_ID('tempdb..#AccesoDia') IS NOT NULL DROP TABLE #AccesoDia;
                IF OBJECT_ID('tempdb..#ComprasPeriodo') IS NOT NULL DROP TABLE #ComprasPeriodo;
                IF OBJECT_ID('tempdb..#ComprasDuplicados') IS NOT NULL DROP TABLE #ComprasDuplicados;
                IF OBJECT_ID('tempdb..#ComprasAsientos') IS NOT NULL DROP TABLE #ComprasAsientos;

                CREATE TABLE #AuditUser
                (
                    TipoControl nvarchar(50) NOT NULL,
                    TipoControlLabel nvarchar(150) NOT NULL,
                    Riesgo nvarchar(10) NOT NULL,
                    RiesgoOrden int NOT NULL,
                    FechaHora datetime NOT NULL,
                    FechaHoraHasta datetime NULL,
                    Usuario nvarchar(255) NOT NULL,
                    Pc nvarchar(255) NOT NULL,
                    TipoComprobante nvarchar(100) NOT NULL,
                    IdComprobante nvarchar(250) NOT NULL,
                    Cuenta nvarchar(15) NOT NULL,
                    Cliente nvarchar(150) NOT NULL,
                    ImporteOriginal money NULL,
                    ImporteAplicado money NULL,
                    Diferencia money NULL,
                    CantidadOcurrencias int NOT NULL,
                    Proceso nvarchar(255) NOT NULL,
                    Formulario nvarchar(150) NOT NULL,
                    FacturaRelacionada nvarchar(400) NOT NULL,
                    EstadoFacturacion nvarchar(50) NOT NULL,
                    SystemUserDetalle nvarchar(500) NOT NULL,
                    Motivo nvarchar(250) NOT NULL,
                    Observaciones nvarchar(1000) NOT NULL,
                    DiasPendientes int NOT NULL,
                    NumeroNormalizado nvarchar(50) NOT NULL,
                    SucursalesDetectadas nvarchar(400) NOT NULL,
                    UsuariosDetectados nvarchar(400) NOT NULL,
                    ImpactoContable nvarchar(150) NOT NULL,
                    ComprobantesInvolucrados int NOT NULL,
                    ContablesDuplicados int NOT NULL,
                    DetalleGrupo nvarchar(max) NOT NULL
                );

                SELECT
                    a.ID,
                    a.TC,
                    a.IDCOMPROBANTE,
                    a.IDCOMPLEMENTO,
                    a.TIPO_ACCION,
                    a.FECHAHORA,
                    ISNULL(a.USUARIO, '') AS USUARIO,
                    ISNULL(a.PC, '') AS PC,
                    ISNULL(a.Cuenta, '') AS Cuenta,
                    ISNULL(a.Proceso, '') AS Proceso,
                    ISNULL(a.SYSTEMUSER, '') AS SystemUser
                INTO #AccionesPeriodo
                FROM dbo.V_MV_CpteAcciones a
                WHERE (@Desde IS NULL OR a.FECHAHORA >= @Desde)
                  AND (@Hasta IS NULL OR a.FECHAHORA < DATEADD(day, 1, @Hasta));

                SELECT
                    a.USUARIO,
                    a.PC,
                    a.FECHAHORA
                INTO #Acciones90
                FROM dbo.V_MV_CpteAcciones a
                WHERE a.FECHAHORA >= DATEADD(day, -90, GETDATE())
                  AND ISNULL(a.USUARIO, '') <> ''
                  AND ISNULL(a.PC, '') <> '';

                SELECT
                    ca.ID,
                    ISNULL(ca.USUARIO, '') AS USUARIO,
                    ISNULL(ca.MAQUINA, '') AS MAQUINA,
                    ISNULL(ca.FORMULARIO, '') AS FORMULARIO,
                    ISNULL(ca.TAREA, '') AS TAREA,
                    ca.INGRESO,
                    ca.EGRESO
                INTO #ControlPeriodo
                FROM dbo.CONTROL_ACCESO ca
                WHERE (@Desde IS NULL OR ca.INGRESO >= @Desde)
                  AND (@Hasta IS NULL OR ca.INGRESO < DATEADD(day, 1, @Hasta));

                SELECT
                    r.TC,
                    r.IDCOMPROBANTE,
                    r.FECHA,
                    ISNULL(r.Usuario, '') AS Usuario,
                    ISNULL(r.CUENTA, '') AS CUENTA,
                    ISNULL(r.NOMBRE, '') AS NOMBRE,
                    ISNULL(r.IMPORTE, 0) AS IMPORTE,
                    ISNULL(r.ANULADA, 0) AS ANULADA
                INTO #RemitosPeriodo
                FROM dbo.V_MV_Cpte r
                WHERE r.TC = 'RM'
                  AND (@Desde IS NULL OR r.FECHA >= @Desde)
                  AND (@Hasta IS NULL OR r.FECHA < DATEADD(day, 1, @Hasta));

                SELECT
                    ap.IDComprobante_Origen,
                    SUM(CASE WHEN ap.TC IN ('FC','FP','TK','TKFC') THEN ISNULL(ap.IMPORTE, 0) ELSE 0 END) AS ImporteAplicado
                INTO #AplicacionesRm
                FROM dbo.MV_APLICACION ap
                INNER JOIN #RemitosPeriodo r ON r.IDCOMPROBANTE = ap.IDComprobante_Origen
                WHERE ap.TCO_ORIGEN = 'RM'
                GROUP BY ap.IDComprobante_Origen;

                SELECT
                    ap.IDComprobante_Origen,
                    STUFF((
                        SELECT DISTINCT ', ' + ap2.TC + ' ' + ISNULL(ap2.IDComprobante, '')
                        FROM dbo.MV_APLICACION ap2
                        WHERE ap2.TCO_ORIGEN = 'RM'
                          AND ap2.IDComprobante_Origen = ap.IDComprobante_Origen
                          AND ap2.TC IN ('FC','FP','TK','TKFC')
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 2, '') AS Facturas
                INTO #FacturasRm
                FROM dbo.MV_APLICACION ap
                INNER JOIN #RemitosPeriodo r ON r.IDCOMPROBANTE = ap.IDComprobante_Origen
                WHERE ap.TCO_ORIGEN = 'RM'
                  AND ap.TC IN ('FC','FP','TK','TKFC')
                GROUP BY ap.IDComprobante_Origen;

                SELECT
                    r.TC,
                    r.IDCOMPROBANTE,
                    r.FECHA,
                    r.Usuario,
                    r.CUENTA,
                    r.NOMBRE,
                    r.IMPORTE,
                    ROUND(ISNULL(ar.ImporteAplicado, 0), 2) AS ImporteAplicado,
                    ROUND(r.IMPORTE - ISNULL(ar.ImporteAplicado, 0), 2) AS Diferencia,
                    ISNULL(fr.Facturas, '') AS FacturasRelacionadas,
                    CASE
                        WHEN ROUND(ISNULL(ar.ImporteAplicado, 0), 2) = 0 THEN 'SIN FACTURAR'
                        WHEN ROUND(ISNULL(ar.ImporteAplicado, 0), 2) < ROUND(r.IMPORTE, 2) THEN 'FACTURADO PARCIAL'
                        WHEN ROUND(ISNULL(ar.ImporteAplicado, 0), 2) = ROUND(r.IMPORTE, 2) THEN 'FACTURADO TOTAL'
                        WHEN ROUND(ISNULL(ar.ImporteAplicado, 0), 2) > ROUND(r.IMPORTE, 2) THEN 'FACTURADO DE MAS'
                        ELSE 'REVISAR'
                    END AS EstadoFacturacion
                INTO #EstadoRemitos
                FROM #RemitosPeriodo r
                LEFT JOIN #AplicacionesRm ar ON ar.IDComprobante_Origen = r.IDCOMPROBANTE
                LEFT JOIN #FacturasRm fr ON fr.IDComprobante_Origen = r.IDCOMPROBANTE
                WHERE r.ANULADA = 0;

                ;WITH HabitualPcBase AS
                (
                    SELECT
                        USUARIO,
                        PC,
                        COUNT(*) AS Cantidad,
                        MAX(FECHAHORA) AS UltimaFecha
                    FROM #Acciones90
                    GROUP BY USUARIO, PC
                )
                SELECT
                    USUARIO,
                    PC,
                    ROW_NUMBER() OVER (PARTITION BY USUARIO ORDER BY Cantidad DESC, UltimaFecha DESC) AS rn
                INTO #HabitualPc
                FROM HabitualPcBase;

                SELECT
                    USUARIO,
                    MAQUINA,
                    CAST(INGRESO AS date) AS FechaDia,
                    MAX(FORMULARIO) AS FORMULARIO,
                    MAX(TAREA) AS TAREA
                INTO #AccesoDia
                FROM #ControlPeriodo
                GROUP BY USUARIO, MAQUINA, CAST(INGRESO AS date);

                INSERT INTO #AuditUser
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora, FechaHoraHasta, Usuario, Pc,
                    TipoComprobante, IdComprobante, Cuenta, Cliente, ImporteOriginal, ImporteAplicado, Diferencia,
                    CantidadOcurrencias, Proceso, Formulario, FacturaRelacionada, EstadoFacturacion, SystemUserDetalle, Motivo, Observaciones, DiasPendientes,
                    NumeroNormalizado, SucursalesDetectadas, UsuariosDetectados, ImpactoContable, ComprobantesInvolucrados, ContablesDuplicados, DetalleGrupo
                )
                SELECT
                    'IN_NO_GRABADO',
                    'Comprobantes iniciados y no grabados',
                    'ALTO',
                    3,
                    MIN(a.FECHAHORA),
                    MAX(a.FECHAHORA),
                    a.USUARIO,
                    a.PC,
                    a.TC,
                    a.IDCOMPROBANTE,
                    a.Cuenta,
                    ISNULL(c.DESCRIPCION, ''),
                    NULL,
                    NULL,
                    NULL,
                    COUNT(*),
                    MAX(a.Proceso),
                    '',
                    '',
                    '',
                    MAX(a.SystemUser),
                    'Inicio de comprobante sin registro final equivalente.',
                    'Ocurrencias de acción IN sobre comprobantes comerciales sensibles.',
                    0,
                    '',
                    '',
                    '',
                    '',
                    0,
                    0,
                    ''
                FROM #AccionesPeriodo a
                LEFT JOIN dbo.MA_CUENTAS c ON a.Cuenta = c.CODIGO
                WHERE a.TIPO_ACCION = 'IN'
                  AND a.TC IN ('FC','FP','TK','TKFC')
                GROUP BY a.USUARIO, a.PC, a.TC, a.IDCOMPROBANTE, a.Cuenta, c.DESCRIPCION;

                INSERT INTO #AuditUser
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora, FechaHoraHasta, Usuario, Pc,
                    TipoComprobante, IdComprobante, Cuenta, Cliente, ImporteOriginal, ImporteAplicado, Diferencia,
                    CantidadOcurrencias, Proceso, Formulario, FacturaRelacionada, EstadoFacturacion, SystemUserDetalle, Motivo, Observaciones, DiasPendientes,
                    NumeroNormalizado, SucursalesDetectadas, UsuariosDetectados, ImpactoContable, ComprobantesInvolucrados, ContablesDuplicados, DetalleGrupo
                )
                SELECT
                    'RM_PARCIAL',
                    'Remitos facturados parcialmente',
                    'ALTO',
                    3,
                    r.FECHA,
                    NULL,
                    r.Usuario,
                    '',
                    r.TC,
                    r.IDCOMPROBANTE,
                    r.CUENTA,
                    ISNULL(NULLIF(r.NOMBRE, ''), ISNULL(c.DESCRIPCION, '')),
                    r.IMPORTE,
                    r.ImporteAplicado,
                    r.Diferencia,
                    1,
                    '',
                    '',
                    r.FacturasRelacionadas,
                    r.EstadoFacturacion,
                    '',
                    'Importe aplicado menor al importe original del remito.',
                    'Remito con facturación parcial detectada desde MV_APLICACION.',
                    0,
                    '',
                    '',
                    '',
                    '',
                    0,
                    0,
                    ''
                FROM #EstadoRemitos r
                LEFT JOIN dbo.MA_CUENTAS c ON r.CUENTA = c.CODIGO
                WHERE r.EstadoFacturacion = 'FACTURADO PARCIAL';

                INSERT INTO #AuditUser
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora, FechaHoraHasta, Usuario, Pc,
                    TipoComprobante, IdComprobante, Cuenta, Cliente, ImporteOriginal, ImporteAplicado, Diferencia,
                    CantidadOcurrencias, Proceso, Formulario, FacturaRelacionada, EstadoFacturacion, SystemUserDetalle, Motivo, Observaciones, DiasPendientes,
                    NumeroNormalizado, SucursalesDetectadas, UsuariosDetectados, ImpactoContable, ComprobantesInvolucrados, ContablesDuplicados, DetalleGrupo
                )
                SELECT
                    'RM_SIN_FACTURA',
                    'Remitos sin factura',
                    CASE WHEN DATEDIFF(day, CAST(r.FECHA AS date), CAST(GETDATE() AS date)) >= 7 THEN 'ALTO' ELSE 'MEDIO' END,
                    CASE WHEN DATEDIFF(day, CAST(r.FECHA AS date), CAST(GETDATE() AS date)) >= 7 THEN 3 ELSE 2 END,
                    r.FECHA,
                    NULL,
                    r.Usuario,
                    '',
                    r.TC,
                    r.IDCOMPROBANTE,
                    r.CUENTA,
                    ISNULL(NULLIF(r.NOMBRE, ''), ISNULL(c.DESCRIPCION, '')),
                    r.IMPORTE,
                    0,
                    r.IMPORTE,
                    1,
                    '',
                    '',
                    '',
                    r.EstadoFacturacion,
                    '',
                    'Remito sin aplicación posterior registrada.',
                    'Remito pendiente de facturación según MV_APLICACION.',
                    DATEDIFF(day, CAST(r.FECHA AS date), CAST(GETDATE() AS date)),
                    '',
                    '',
                    '',
                    '',
                    0,
                    0,
                    ''
                FROM #EstadoRemitos r
                LEFT JOIN dbo.MA_CUENTAS c ON r.CUENTA = c.CODIGO
                WHERE r.EstadoFacturacion = 'SIN FACTURAR'
                  AND DATEDIFF(day, CAST(r.FECHA AS date), CAST(GETDATE() AS date)) >= @DiasMinimosSinFactura;

                INSERT INTO #AuditUser
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora, FechaHoraHasta, Usuario, Pc,
                    TipoComprobante, IdComprobante, Cuenta, Cliente, ImporteOriginal, ImporteAplicado, Diferencia,
                    CantidadOcurrencias, Proceso, Formulario, FacturaRelacionada, EstadoFacturacion, SystemUserDetalle, Motivo, Observaciones, DiasPendientes,
                    NumeroNormalizado, SucursalesDetectadas, UsuariosDetectados, ImpactoContable, ComprobantesInvolucrados, ContablesDuplicados, DetalleGrupo
                )
                SELECT
                    'RM_DE_MAS',
                    'Remitos facturados de más',
                    'ALTO',
                    3,
                    r.FECHA,
                    NULL,
                    r.Usuario,
                    '',
                    r.TC,
                    r.IDCOMPROBANTE,
                    r.CUENTA,
                    ISNULL(NULLIF(r.NOMBRE, ''), ISNULL(c.DESCRIPCION, '')),
                    r.IMPORTE,
                    r.ImporteAplicado,
                    r.Diferencia,
                    1,
                    '',
                    '',
                    r.FacturasRelacionadas,
                    r.EstadoFacturacion,
                    '',
                    'Importe aplicado mayor al importe original del remito.',
                    'Remito con aplicaciones superiores al importe del comprobante.',
                    0,
                    '',
                    '',
                    '',
                    '',
                    0,
                    0,
                    ''
                FROM #EstadoRemitos r
                LEFT JOIN dbo.MA_CUENTAS c ON r.CUENTA = c.CODIGO
                WHERE r.EstadoFacturacion = 'FACTURADO DE MAS';

                INSERT INTO #AuditUser
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora, FechaHoraHasta, Usuario, Pc,
                    TipoComprobante, IdComprobante, Cuenta, Cliente, ImporteOriginal, ImporteAplicado, Diferencia,
                    CantidadOcurrencias, Proceso, Formulario, FacturaRelacionada, EstadoFacturacion, SystemUserDetalle, Motivo, Observaciones, DiasPendientes,
                    NumeroNormalizado, SucursalesDetectadas, UsuariosDetectados, ImpactoContable, ComprobantesInvolucrados, ContablesDuplicados, DetalleGrupo
                )
                SELECT
                    'MOD_EXCESIVAS',
                    'Modificaciones excesivas',
                    CASE WHEN COUNT(*) >= (@UmbralModificaciones * 2) THEN 'ALTO' ELSE 'MEDIO' END,
                    CASE WHEN COUNT(*) >= (@UmbralModificaciones * 2) THEN 3 ELSE 2 END,
                    MIN(a.FECHAHORA),
                    MAX(a.FECHAHORA),
                    a.USUARIO,
                    a.PC,
                    a.TC,
                    a.IDCOMPROBANTE,
                    a.Cuenta,
                    ISNULL(c.DESCRIPCION, ''),
                    NULL,
                    NULL,
                    NULL,
                    COUNT(*),
                    MAX(a.Proceso),
                    '',
                    '',
                    '',
                    '',
                    'Cantidad de modificaciones superior al umbral configurado.',
                    'Comprobante comercial modificado reiteradamente por el mismo usuario.',
                    0,
                    '',
                    '',
                    '',
                    '',
                    0,
                    0,
                    ''
                FROM #AccionesPeriodo a
                LEFT JOIN dbo.MA_CUENTAS c ON a.Cuenta = c.CODIGO
                WHERE a.TIPO_ACCION = 'M'
                  AND a.TC IN ('FC','FP','TK','TKFC')
                GROUP BY a.USUARIO, a.PC, a.TC, a.IDCOMPROBANTE, a.Cuenta, c.DESCRIPCION
                HAVING COUNT(*) >= @UmbralModificaciones;

                INSERT INTO #AuditUser
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora, FechaHoraHasta, Usuario, Pc,
                    TipoComprobante, IdComprobante, Cuenta, Cliente, ImporteOriginal, ImporteAplicado, Diferencia,
                    CantidadOcurrencias, Proceso, Formulario, FacturaRelacionada, EstadoFacturacion, SystemUserDetalle, Motivo, Observaciones, DiasPendientes,
                    NumeroNormalizado, SucursalesDetectadas, UsuariosDetectados, ImpactoContable, ComprobantesInvolucrados, ContablesDuplicados, DetalleGrupo
                )
                SELECT
                    'BAJAS',
                    'Bajas de comprobantes',
                    'ALTO',
                    3,
                    MIN(a.FECHAHORA),
                    MAX(a.FECHAHORA),
                    a.USUARIO,
                    a.PC,
                    a.TC,
                    a.IDCOMPROBANTE,
                    a.Cuenta,
                    ISNULL(c.DESCRIPCION, ''),
                    NULL,
                    NULL,
                    NULL,
                    COUNT(*),
                    MAX(a.Proceso),
                    '',
                    '',
                    '',
                    '',
                    'Anulación o baja detectada en comprobante comercial.',
                    'Acción B registrada en V_MV_CpteAcciones.',
                    0,
                    '',
                    '',
                    '',
                    '',
                    0,
                    0,
                    ''
                FROM #AccionesPeriodo a
                LEFT JOIN dbo.MA_CUENTAS c ON a.Cuenta = c.CODIGO
                WHERE a.TIPO_ACCION = 'B'
                  AND a.TC IN ('FC','FP','TK','TKFC','RM')
                GROUP BY a.USUARIO, a.PC, a.TC, a.IDCOMPROBANTE, a.Cuenta, c.DESCRIPCION;

                INSERT INTO #AuditUser
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora, FechaHoraHasta, Usuario, Pc,
                    TipoComprobante, IdComprobante, Cuenta, Cliente, ImporteOriginal, ImporteAplicado, Diferencia,
                    CantidadOcurrencias, Proceso, Formulario, FacturaRelacionada, EstadoFacturacion, SystemUserDetalle, Motivo, Observaciones, DiasPendientes,
                    NumeroNormalizado, SucursalesDetectadas, UsuariosDetectados, ImpactoContable, ComprobantesInvolucrados, ContablesDuplicados, DetalleGrupo
                )
                SELECT
                    'FUERA_HORARIO',
                    'Actividad fuera de horario',
                    'MEDIO',
                    2,
                    a.FECHAHORA,
                    NULL,
                    a.USUARIO,
                    a.PC,
                    a.TC,
                    a.IDCOMPROBANTE,
                    a.Cuenta,
                    ISNULL(c.DESCRIPCION, ''),
                    NULL,
                    NULL,
                    NULL,
                    1,
                    a.Proceso,
                    ISNULL(ad.FORMULARIO, ''),
                    '',
                    '',
                    '',
                    CASE
                        WHEN CHARINDEX(',' + CONVERT(varchar(2), DATEPART(weekday, a.FECHAHORA)) + ',', ',' + @DiasLaborales + ',') = 0
                             AND @ControlarPcHabitual = 1
                             AND ISNULL(hp.PC, '') <> '' AND hp.PC <> a.PC
                            THEN 'Actividad registrada en un día no habitual y desde una PC distinta a la habitual.'
                        WHEN CHARINDEX(',' + CONVERT(varchar(2), DATEPART(weekday, a.FECHAHORA)) + ',', ',' + @DiasLaborales + ',') = 0
                            THEN 'Actividad registrada en un día no considerado laboral.'
                        WHEN (DATEPART(hour, a.FECHAHORA) < @HoraInicioLaboral OR DATEPART(hour, a.FECHAHORA) >= @HoraFinLaboral)
                             AND @ControlarPcHabitual = 1
                             AND ISNULL(hp.PC, '') <> '' AND hp.PC <> a.PC
                            THEN 'Actividad fuera del horario habitual y desde una PC distinta a la habitual.'
                        WHEN (DATEPART(hour, a.FECHAHORA) < @HoraInicioLaboral OR DATEPART(hour, a.FECHAHORA) >= @HoraFinLaboral)
                            THEN 'Actividad fuera del horario laboral habitual.'
                        ELSE 'Actividad registrada desde una PC distinta a la habitual del usuario.'
                    END,
                    CASE
                        WHEN ISNULL(ad.TAREA, '') <> '' THEN 'Tarea asociada: ' + ad.TAREA
                        ELSE 'Sin acceso correlacionado claro en CONTROL_ACCESO.'
                    END,
                    0,
                    '',
                    '',
                    '',
                    '',
                    0,
                    0,
                    ''
                FROM #AccionesPeriodo a
                LEFT JOIN dbo.MA_CUENTAS c ON a.Cuenta = c.CODIGO
                LEFT JOIN #HabitualPc hp ON hp.USUARIO = a.USUARIO AND hp.rn = 1
                LEFT JOIN #AccesoDia ad ON ad.USUARIO = a.USUARIO AND ad.MAQUINA = a.PC AND ad.FechaDia = CAST(a.FECHAHORA AS date)
                WHERE a.TC IN ('FC','FP','TK','TKFC','RM')
                  AND (
                        CHARINDEX(',' + CONVERT(varchar(2), DATEPART(weekday, a.FECHAHORA)) + ',', ',' + @DiasLaborales + ',') = 0
                        OR DATEPART(hour, a.FECHAHORA) < @HoraInicioLaboral
                        OR DATEPART(hour, a.FECHAHORA) >= @HoraFinLaboral
                        OR (@ControlarPcHabitual = 1 AND ISNULL(hp.PC, '') <> '' AND hp.PC <> a.PC)
                      );

                ;WITH AccesosSensibles AS
                (
                    SELECT *
                    FROM #ControlPeriodo
                    WHERE
                        UPPER(FORMULARIO) LIKE '%FACT%'
                        OR UPPER(FORMULARIO) LIKE '%CAJA%'
                        OR UPPER(FORMULARIO) LIKE '%TICKET%'
                        OR UPPER(FORMULARIO) LIKE '%REMIT%'
                        OR UPPER(TAREA) LIKE '%FACT%'
                        OR UPPER(TAREA) LIKE '%CAJA%'
                        OR UPPER(TAREA) LIKE '%TICKET%'
                        OR UPPER(TAREA) LIKE '%REMIT%'
                ),
                AccionesPorAcceso AS
                (
                    SELECT
                        ca.ID,
                        SUM(CASE WHEN a.TIPO_ACCION = 'IN' THEN 1 ELSE 0 END) AS Inicios,
                        COUNT(a.ID) AS TotalAcciones,
                        MAX(CASE WHEN a.TIPO_ACCION = 'IN' THEN a.TC ELSE '' END) AS TipoComprobante,
                        MAX(CASE WHEN a.TIPO_ACCION = 'IN' THEN a.IDCOMPROBANTE ELSE '' END) AS IdComprobante
                    FROM AccesosSensibles ca
                    LEFT JOIN #AccionesPeriodo a
                        ON a.USUARIO = ca.USUARIO
                       AND (a.PC = ca.MAQUINA OR ca.MAQUINA = '')
                       AND a.FECHAHORA >= DATEADD(minute, -10, ca.INGRESO)
                       AND a.FECHAHORA <= DATEADD(minute, 10, ISNULL(ca.EGRESO, DATEADD(hour, 2, ca.INGRESO)))
                    GROUP BY ca.ID
                )
                INSERT INTO #AuditUser
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora, FechaHoraHasta, Usuario, Pc,
                    TipoComprobante, IdComprobante, Cuenta, Cliente, ImporteOriginal, ImporteAplicado, Diferencia,
                    CantidadOcurrencias, Proceso, Formulario, FacturaRelacionada, EstadoFacturacion, SystemUserDetalle, Motivo, Observaciones, DiasPendientes,
                    NumeroNormalizado, SucursalesDetectadas, UsuariosDetectados, ImpactoContable, ComprobantesInvolucrados, ContablesDuplicados, DetalleGrupo
                )
                SELECT
                    'INGRESO_SIN_FINAL',
                    'Ingreso sensible sin operación final',
                    CASE WHEN ISNULL(a.TotalAcciones, 0) = ISNULL(a.Inicios, 0) THEN 'ALTO' ELSE 'MEDIO' END,
                    CASE WHEN ISNULL(a.TotalAcciones, 0) = ISNULL(a.Inicios, 0) THEN 3 ELSE 2 END,
                    ca.INGRESO,
                    ca.EGRESO,
                    ca.USUARIO,
                    ca.MAQUINA,
                    ISNULL(a.TipoComprobante, ''),
                    ISNULL(a.IdComprobante, ''),
                    '',
                    '',
                    NULL,
                    NULL,
                    NULL,
                    ISNULL(a.TotalAcciones, 0),
                    '',
                    ca.FORMULARIO,
                    '',
                    '',
                    '',
                    'Ingreso a opción sensible con inicio de operación pero sin cierre registrado.',
                    'Tarea: ' + ca.TAREA + ' · Inicios: ' + CONVERT(nvarchar(20), ISNULL(a.Inicios, 0)) + ' · Total acciones: ' + CONVERT(nvarchar(20), ISNULL(a.TotalAcciones, 0)),
                    0,
                    '',
                    '',
                    '',
                    '',
                    0,
                    0,
                    ''
                FROM AccesosSensibles ca
                LEFT JOIN AccionesPorAcceso a ON a.ID = ca.ID
                WHERE ISNULL(a.Inicios, 0) > 0
                  AND ISNULL(a.TotalAcciones, 0) = ISNULL(a.Inicios, 0);

                SELECT
                    UPPER(LTRIM(RTRIM(ISNULL(c.TC, '')))) AS TC,
                    UPPER(LTRIM(RTRIM(ISNULL(c.IDCOMPROBANTE, '')))) AS IDCOMPROBANTE,
                    UPPER(LTRIM(RTRIM(ISNULL(c.CUENTA, '')))) AS CUENTA,
                    CAST(ISNULL(c.FECHA, GETDATE()) AS datetime) AS FECHA,
                    CAST(ISNULL(c.FECHA, GETDATE()) AS date) AS FECHADIA,
                    ISNULL(NULLIF(LTRIM(RTRIM(c.NOMBRE)), ''), ISNULL(mc.DESCRIPCION, '')) AS NOMBRE,
                    ROUND(ISNULL(c.IMPORTE, 0), 2) AS IMPORTE,
                    ISNULL(NULLIF(LTRIM(RTRIM(c.USUARIO)), ''), '') AS USUARIO,
                    ISNULL(c.FechaHora_Grabacion, CAST(ISNULL(c.FECHA, GETDATE()) AS datetime)) AS FechaHora_Grabacion,
                    UPPER(LTRIM(RTRIM(ISNULL(c.SUCURSAL, '')))) AS SUCURSAL,
                    UPPER(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(c.NUMERO, ''))), '-', ''), '.', ''), ' ', '')) AS NUMERO_NORMALIZADO,
                    UPPER(LTRIM(RTRIM(ISNULL(c.LETRA, '')))) AS LETRA
                INTO #ComprasPeriodo
                FROM dbo.C_MV_Cpte c
                LEFT JOIN dbo.MA_CUENTAS mc ON mc.CODIGO = c.CUENTA
                WHERE (@Desde IS NULL OR c.FECHA >= @Desde)
                  AND (@Hasta IS NULL OR c.FECHA < DATEADD(day, 1, @Hasta))
                  AND ISNULL(c.ANULADA, 0) = 0
                  AND ISNULL(c.CUENTA, '') <> ''
                  AND ROUND(ISNULL(c.IMPORTE, 0), 2) <> 0
                  AND ISNULL(c.NUMERO, '') <> '';

                SELECT
                    c.TC,
                    c.CUENTA,
                    c.NOMBRE,
                    c.NUMERO_NORMALIZADO,
                    c.IMPORTE,
                    MIN(c.FECHA) AS FechaMinima,
                    MAX(c.FECHA) AS FechaMaxima,
                    MIN(c.FechaHora_Grabacion) AS FechaHoraGrupo,
                    COUNT(*) AS CantidadCoincidencias,
                    COUNT(DISTINCT c.IDCOMPROBANTE) AS CantidadComprobantes,
                    COUNT(DISTINCT NULLIF(c.SUCURSAL, '')) AS CantidadSucursales,
                    COUNT(DISTINCT NULLIF(c.USUARIO, '')) AS CantidadUsuarios,
                    STUFF((
                        SELECT DISTINCT ', ' + c2.SUCURSAL
                        FROM #ComprasPeriodo c2
                        WHERE c2.TC = c.TC
                          AND c2.CUENTA = c.CUENTA
                          AND c2.NUMERO_NORMALIZADO = c.NUMERO_NORMALIZADO
                          AND c2.IMPORTE = c.IMPORTE
                        ORDER BY ', ' + c2.SUCURSAL
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 2, '') AS SucursalesDetectadas,
                    STUFF((
                        SELECT DISTINCT ', ' + c2.USUARIO
                        FROM #ComprasPeriodo c2
                        WHERE c2.TC = c.TC
                          AND c2.CUENTA = c.CUENTA
                          AND c2.NUMERO_NORMALIZADO = c.NUMERO_NORMALIZADO
                          AND c2.IMPORTE = c.IMPORTE
                        ORDER BY ', ' + c2.USUARIO
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 2, '') AS UsuariosDetectados,
                    STUFF((
                        SELECT DISTINCT ' | ' + c2.IDCOMPROBANTE
                        FROM #ComprasPeriodo c2
                        WHERE c2.TC = c.TC
                          AND c2.CUENTA = c.CUENTA
                          AND c2.NUMERO_NORMALIZADO = c.NUMERO_NORMALIZADO
                          AND c2.IMPORTE = c.IMPORTE
                        ORDER BY ' | ' + c2.IDCOMPROBANTE
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 3, '') AS IdsRelacionados,
                    STUFF((
                        SELECT CHAR(10)
                            + CONVERT(nvarchar(10), CAST(c2.FECHA AS date), 103)
                            + ' | ' + c2.TC
                            + ' | Suc ' + ISNULL(c2.SUCURSAL, '')
                            + ' | Nro ' + ISNULL(c2.NUMERO_NORMALIZADO, '')
                            + ' | Letra ' + ISNULL(c2.LETRA, '')
                            + ' | ID ' + ISNULL(c2.IDCOMPROBANTE, '')
                            + ' | Importe ' + CONVERT(nvarchar(50), c2.IMPORTE)
                            + ' | Usuario ' + ISNULL(c2.USUARIO, '')
                            + ' | Grabación ' + CONVERT(nvarchar(16), c2.FechaHora_Grabacion, 120)
                        FROM #ComprasPeriodo c2
                        WHERE c2.TC = c.TC
                          AND c2.CUENTA = c.CUENTA
                          AND c2.NUMERO_NORMALIZADO = c.NUMERO_NORMALIZADO
                          AND c2.IMPORTE = c.IMPORTE
                        ORDER BY c2.FECHA, c2.IDCOMPROBANTE
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 1, '') AS DetalleGrupo
                INTO #ComprasDuplicados
                FROM #ComprasPeriodo c
                GROUP BY c.TC, c.CUENTA, c.NOMBRE, c.NUMERO_NORMALIZADO, c.IMPORTE
                HAVING COUNT(*) > 1
                   AND COUNT(DISTINCT c.IDCOMPROBANTE) > 1
                   AND DATEDIFF(day, MIN(c.FECHA), MAX(c.FECHA)) <= @DiasToleranciaDuplicados
                   AND (@SoloDiferenciasSucursal = 0 OR COUNT(DISTINCT NULLIF(c.SUCURSAL, '')) > 1);

                SELECT
                    UPPER(LTRIM(RTRIM(ISNULL(a.TC, '')))) AS TC,
                    UPPER(LTRIM(RTRIM(ISNULL(a.SUCURSAL, '')))) AS SUCURSAL,
                    UPPER(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(a.NUMERO, ''))), '-', ''), '.', ''), ' ', '')) AS NUMERO_NORMALIZADO,
                    UPPER(LTRIM(RTRIM(ISNULL(a.LETRA, '')))) AS LETRA,
                    COUNT(*) AS CantidadAsientos
                INTO #ComprasAsientos
                FROM dbo.MV_ASIENTOS a
                INNER JOIN #ComprasPeriodo c
                    ON c.TC = UPPER(LTRIM(RTRIM(ISNULL(a.TC, ''))))
                   AND c.SUCURSAL = UPPER(LTRIM(RTRIM(ISNULL(a.SUCURSAL, ''))))
                   AND c.NUMERO_NORMALIZADO = UPPER(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(a.NUMERO, ''))), '-', ''), '.', ''), ' ', ''))
                   AND c.LETRA = UPPER(LTRIM(RTRIM(ISNULL(a.LETRA, ''))))
                GROUP BY
                    UPPER(LTRIM(RTRIM(ISNULL(a.TC, '')))),
                    UPPER(LTRIM(RTRIM(ISNULL(a.SUCURSAL, '')))),
                    UPPER(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(a.NUMERO, ''))), '-', ''), '.', ''), ' ', '')),
                    UPPER(LTRIM(RTRIM(ISNULL(a.LETRA, ''))));

                INSERT INTO #AuditUser
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora, FechaHoraHasta, Usuario, Pc,
                    TipoComprobante, IdComprobante, Cuenta, Cliente, ImporteOriginal, ImporteAplicado, Diferencia,
                    CantidadOcurrencias, Proceso, Formulario, FacturaRelacionada, EstadoFacturacion, SystemUserDetalle, Motivo, Observaciones, DiasPendientes,
                    NumeroNormalizado, SucursalesDetectadas, UsuariosDetectados, ImpactoContable, ComprobantesInvolucrados, ContablesDuplicados, DetalleGrupo
                )
                SELECT
                    'CPRA_DUPLICADO',
                    'Posibles comprobantes duplicados',
                    CASE
                        WHEN Score.ScoreTotal >= 80 THEN 'ALTO'
                        WHEN Score.ScoreTotal >= 60 THEN 'MEDIO'
                        ELSE 'BAJO'
                    END,
                    CASE
                        WHEN Score.ScoreTotal >= 80 THEN 3
                        WHEN Score.ScoreTotal >= 60 THEN 2
                        ELSE 1
                    END,
                    d.FechaHoraGrupo,
                    d.FechaMaxima,
                    d.UsuariosDetectados,
                    '',
                    d.TC,
                    d.IdsRelacionados,
                    d.CUENTA,
                    d.NOMBRE,
                    d.IMPORTE,
                    NULL,
                    NULL,
                    d.CantidadCoincidencias,
                    'C_MV_Cpte',
                    '',
                    d.SucursalesDetectadas,
                    CONCAT(COALESCE(AsientoStats.ConAsiento, 0), '/', d.CantidadComprobantes, ' comprobante(s) con asiento'),
                    '',
                    CASE
                        WHEN d.CantidadSucursales > 1 THEN 'Mismo proveedor, mismo número e importe con sucursales distintas.'
                        ELSE 'Mismo proveedor, mismo número e importe cargado más de una vez. Requiere revisión humana.'
                    END,
                    CONCAT(
                        'Se comparó C_MV_Cpte por proveedor, TC exacto, número normalizado e importe. ',
                        'MV_ASIENTOS se usa solo para informar impacto contable. Score: ', CONVERT(nvarchar(10), Score.ScoreTotal), '.'
                    ),
                    DATEDIFF(day, d.FechaMinima, d.FechaMaxima),
                    d.NUMERO_NORMALIZADO,
                    d.SucursalesDetectadas,
                    d.UsuariosDetectados,
                    CONCAT(COALESCE(AsientoStats.ConAsiento, 0), '/', d.CantidadComprobantes, ' con asiento'),
                    d.CantidadComprobantes,
                    COALESCE(AsientoStats.ConAsiento, 0),
                    d.DetalleGrupo
                FROM #ComprasDuplicados d
                CROSS APPLY
                (
                    SELECT
                        40
                        + 30
                        + 20
                        + CASE WHEN DATEDIFF(day, d.FechaMinima, d.FechaMaxima) = 0 THEN 10
                               WHEN DATEDIFF(day, d.FechaMinima, d.FechaMaxima) <= @DiasToleranciaDuplicados THEN 5
                               ELSE 0
                          END
                        + CASE WHEN d.CantidadSucursales > 1 THEN 25 ELSE 0 END
                        + CASE WHEN d.CantidadUsuarios > 1 THEN 10 ELSE 0 END AS ScoreTotal
                ) Score
                CROSS APPLY
                (
                    SELECT
                        COUNT(*) AS ConAsiento
                    FROM #ComprasPeriodo c
                    LEFT JOIN #ComprasAsientos a
                        ON a.TC = c.TC
                       AND a.SUCURSAL = c.SUCURSAL
                       AND a.NUMERO_NORMALIZADO = c.NUMERO_NORMALIZADO
                       AND a.LETRA = c.LETRA
                    WHERE c.TC = d.TC
                      AND c.CUENTA = d.CUENTA
                      AND c.NUMERO_NORMALIZADO = d.NUMERO_NORMALIZADO
                      AND c.IMPORTE = d.IMPORTE
                      AND ISNULL(a.CantidadAsientos, 0) > 0
                ) AsientoStats;

                SELECT
                    COUNT(*) AS TotalRegistros,
                    SUM(CASE WHEN au.Riesgo = 'ALTO' THEN 1 ELSE 0 END) AS RiesgoAlto,
                    SUM(CASE WHEN au.Riesgo = 'MEDIO' THEN 1 ELSE 0 END) AS RiesgoMedio,
                    COUNT(DISTINCT au.TipoControl) AS ControlesDetectados,
                    SUM(ISNULL(au.ComprobantesInvolucrados, 0)) AS ComprobantesInvolucrados,
                    SUM(ISNULL(au.ContablesDuplicados, 0)) AS ContablesDuplicados,
                    COUNT(DISTINCT CASE WHEN au.TipoControl = 'IN_NO_GRABADO' AND NULLIF(au.Usuario, '') IS NOT NULL THEN au.Usuario END) AS UsuariosInvolucrados,
                    AVG(CASE
                        WHEN au.TipoControl = 'IN_NO_GRABADO'
                         AND Parsed.CancelacionFecha IS NOT NULL
                         AND Parsed.CancelacionFecha >= au.FechaHora
                        THEN DATEDIFF(second, au.FechaHora, Parsed.CancelacionFecha) / 60.0
                    END) AS PromedioMinutosCancelacion,
                    MAX(CASE
                        WHEN au.TipoControl = 'IN_NO_GRABADO'
                         AND Parsed.CancelacionFecha IS NOT NULL
                         AND Parsed.CancelacionFecha >= au.FechaHora
                        THEN DATEDIFF(minute, au.FechaHora, Parsed.CancelacionFecha)
                    END) AS MaximoMinutosCancelacion,
                    SUM(CASE
                        WHEN au.TipoControl = 'IN_NO_GRABADO'
                        THEN ISNULL(Parsed.ImporteCancelacion, 0)
                        ELSE 0
                    END) AS ImporteTotalCancelado
                FROM #AuditUser au
                OUTER APPLY
                (
                    SELECT
                        CASE
                            WHEN au.TipoControl = 'IN_NO_GRABADO' AND LEN(au.SystemUserDetalle) >= 19
                            THEN SUBSTRING(au.SystemUserDetalle, 7, 4) + '-'
                               + SUBSTRING(au.SystemUserDetalle, 4, 2) + '-'
                               + SUBSTRING(au.SystemUserDetalle, 1, 2) + ' '
                               + SUBSTRING(au.SystemUserDetalle, 12, 8)
                            ELSE ''
                        END AS CancelacionFechaTexto,
                        CASE
                            WHEN au.TipoControl = 'IN_NO_GRABADO' AND CHARINDEX('$', au.SystemUserDetalle) > 0
                            THEN REPLACE(REPLACE(LTRIM(RTRIM(
                                CASE
                                    WHEN CHARINDEX(' cancel', LOWER(au.SystemUserDetalle), CHARINDEX('$', au.SystemUserDetalle)) > 0
                                    THEN SUBSTRING(
                                        au.SystemUserDetalle,
                                        CHARINDEX('$', au.SystemUserDetalle) + 1,
                                        CHARINDEX(' cancel', LOWER(au.SystemUserDetalle), CHARINDEX('$', au.SystemUserDetalle)) - CHARINDEX('$', au.SystemUserDetalle) - 1)
                                    ELSE SUBSTRING(au.SystemUserDetalle, CHARINDEX('$', au.SystemUserDetalle) + 1, 64)
                                END
                            )), ',', ''), ' ', '')
                            ELSE ''
                        END AS ImporteCancelacionTexto
                ) ParsedRaw
                OUTER APPLY
                (
                    SELECT
                        CASE
                            WHEN ParsedRaw.CancelacionFechaTexto <> '' AND ISDATE(ParsedRaw.CancelacionFechaTexto) = 1
                            THEN CAST(ParsedRaw.CancelacionFechaTexto AS datetime)
                            ELSE NULL
                        END AS CancelacionFecha,
                        CASE
                            WHEN ParsedRaw.ImporteCancelacionTexto <> ''
                             AND PATINDEX('%[^0-9.]%', ParsedRaw.ImporteCancelacionTexto) = 0
                            THEN CAST(ParsedRaw.ImporteCancelacionTexto AS decimal(18, 2))
                            ELSE NULL
                        END AS ImporteCancelacion
                ) Parsed
                WHERE (@Usuario = '' OR au.Usuario = @Usuario)
                  AND (@Pc = '' OR au.Pc LIKE '%' + @Pc + '%')
                  AND (@TipoComprobante = '' OR au.TipoComprobante LIKE '%' + @TipoComprobante + '%')
                  AND (@Riesgo = '' OR au.Riesgo = @Riesgo)
                  AND (@TipoControl = '' OR au.TipoControl = @TipoControl)
                  AND (@CuentaCliente = '' OR au.Cuenta LIKE '%' + @CuentaCliente + '%' OR au.Cliente LIKE '%' + @CuentaCliente + '%')
                  AND (
                        @Texto = ''
                        OR au.TipoControlLabel LIKE '%' + @Texto + '%'
                        OR au.Motivo LIKE '%' + @Texto + '%'
                        OR au.Observaciones LIKE '%' + @Texto + '%'
                        OR au.Usuario LIKE '%' + @Texto + '%'
                        OR au.Pc LIKE '%' + @Texto + '%'
                        OR au.TipoComprobante LIKE '%' + @Texto + '%'
                        OR au.IdComprobante LIKE '%' + @Texto + '%'
                        OR au.NumeroNormalizado LIKE '%' + @Texto + '%'
                        OR au.Cliente LIKE '%' + @Texto + '%'
                        OR au.Cuenta LIKE '%' + @Texto + '%'
                        OR au.EstadoFacturacion LIKE '%' + @Texto + '%'
                        OR au.SucursalesDetectadas LIKE '%' + @Texto + '%'
                        OR au.UsuariosDetectados LIKE '%' + @Texto + '%'
                        OR au.ImpactoContable LIKE '%' + @Texto + '%'
                      );

                SELECT
                    TipoControl,
                    TipoControlLabel,
                    Riesgo,
                    FechaHora,
                    FechaHoraHasta,
                    Usuario,
                    Pc,
                    TipoComprobante,
                    IdComprobante,
                    Cuenta,
                    Cliente,
                    ImporteOriginal,
                    ImporteAplicado,
                    Diferencia,
                    CantidadOcurrencias,
                    Proceso,
                    Formulario,
                    FacturaRelacionada,
                    EstadoFacturacion,
                    SystemUserDetalle,
                    Motivo,
                    Observaciones,
                    DiasPendientes,
                    NumeroNormalizado,
                    SucursalesDetectadas,
                    UsuariosDetectados,
                    ImpactoContable,
                    ComprobantesInvolucrados,
                    ContablesDuplicados,
                    DetalleGrupo
                FROM #AuditUser
                WHERE (@Usuario = '' OR Usuario = @Usuario)
                  AND (@Pc = '' OR Pc LIKE '%' + @Pc + '%')
                  AND (@TipoComprobante = '' OR TipoComprobante LIKE '%' + @TipoComprobante + '%')
                  AND (@Riesgo = '' OR Riesgo = @Riesgo)
                  AND (@TipoControl = '' OR TipoControl = @TipoControl)
                  AND (@CuentaCliente = '' OR Cuenta LIKE '%' + @CuentaCliente + '%' OR Cliente LIKE '%' + @CuentaCliente + '%')
                  AND (
                        @Texto = ''
                        OR TipoControlLabel LIKE '%' + @Texto + '%'
                        OR Motivo LIKE '%' + @Texto + '%'
                        OR Observaciones LIKE '%' + @Texto + '%'
                        OR Usuario LIKE '%' + @Texto + '%'
                        OR Pc LIKE '%' + @Texto + '%'
                        OR TipoComprobante LIKE '%' + @Texto + '%'
                        OR IdComprobante LIKE '%' + @Texto + '%'
                        OR NumeroNormalizado LIKE '%' + @Texto + '%'
                        OR Cliente LIKE '%' + @Texto + '%'
                        OR Cuenta LIKE '%' + @Texto + '%'
                        OR EstadoFacturacion LIKE '%' + @Texto + '%'
                        OR SucursalesDetectadas LIKE '%' + @Texto + '%'
                        OR UsuariosDetectados LIKE '%' + @Texto + '%'
                        OR ImpactoContable LIKE '%' + @Texto + '%'
                      )
                ORDER BY {orderBy}
                OFFSET @Offset ROWS FETCH NEXT @Tamanio ROWS ONLY;
                """;

            var items = new List<AuditoriaUsuarioRowDto>();
            var stats = new AuditoriaUsuarioStatsDto();
            var total = 0;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.CommandTimeout = 90;
            cmd.Parameters.AddWithValue("@Desde", (object?)filter.Desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta", (object?)filter.Hasta ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Texto", filter.Texto ?? string.Empty);
            cmd.Parameters.AddWithValue("@Usuario", filter.Usuario ?? string.Empty);
            cmd.Parameters.AddWithValue("@Pc", filter.Pc ?? string.Empty);
            cmd.Parameters.AddWithValue("@TipoComprobante", filter.TipoComprobante ?? string.Empty);
            cmd.Parameters.AddWithValue("@Riesgo", filter.Riesgo ?? string.Empty);
            cmd.Parameters.AddWithValue("@TipoControl", filter.TipoControl ?? string.Empty);
            cmd.Parameters.AddWithValue("@CuentaCliente", filter.CuentaCliente ?? string.Empty);
            cmd.Parameters.AddWithValue("@DiasMinimosSinFactura", diasMinimos);
            cmd.Parameters.AddWithValue("@UmbralModificaciones", umbralModificaciones);
            cmd.Parameters.AddWithValue("@DiasToleranciaDuplicados", diasToleranciaDuplicados);
            cmd.Parameters.AddWithValue("@SoloDiferenciasSucursal", soloDiferenciasSucursal ? 1 : 0);
            cmd.Parameters.AddWithValue("@HoraInicioLaboral", auditSettings.HoraInicioLaboral);
            cmd.Parameters.AddWithValue("@HoraFinLaboral", auditSettings.HoraFinLaboral);
            cmd.Parameters.AddWithValue("@DiasLaborales", string.Join(",", auditSettings.DiasLaborales));
            cmd.Parameters.AddWithValue("@ControlarPcHabitual", auditSettings.ControlarPcHabitual ? 1 : 0);
            cmd.Parameters.AddWithValue("@Offset", offset);
            cmd.Parameters.AddWithValue("@Tamanio", tamanio);

            await using var rd = await cmd.ExecuteReaderAsync(token);
            if (await rd.ReadAsync(token))
            {
                total = ReadInt(rd, 0);
                stats = new AuditoriaUsuarioStatsDto
                {
                    TotalAlertas = total,
                    RiesgoAlto = ReadInt(rd, 1),
                    RiesgoMedio = ReadInt(rd, 2),
                    ControlesDetectados = ReadInt(rd, 3),
                    ComprobantesInvolucrados = ReadInt(rd, 4),
                    ContablesDuplicados = ReadInt(rd, 5),
                    UsuariosInvolucrados = ReadInt(rd, 6),
                    PromedioMinutosCancelacion = ReadDecimal(rd, 7),
                    MaximoMinutosCancelacion = ReadInt(rd, 8),
                    ImporteTotalCancelado = ReadDecimal(rd, 9)
                };
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    items.Add(new AuditoriaUsuarioRowDto
                    {
                        TipoControl = ReadString(rd, 0),
                        TipoControlLabel = ReadString(rd, 1),
                        Riesgo = ReadString(rd, 2),
                        FechaHora = ReadDateTime(rd, 3) ?? DateTime.MinValue,
                        FechaHoraHasta = ReadDateTime(rd, 4),
                        Usuario = ReadString(rd, 5),
                        Pc = ReadString(rd, 6),
                        TipoComprobante = ReadString(rd, 7),
                        IdComprobante = ReadString(rd, 8),
                        Cuenta = ReadString(rd, 9),
                        Cliente = ReadString(rd, 10),
                        ImporteOriginal = ReadDecimal(rd, 11),
                        ImporteAplicado = ReadDecimal(rd, 12),
                        Diferencia = ReadDecimal(rd, 13),
                        CantidadOcurrencias = ReadInt(rd, 14),
                        Proceso = ReadString(rd, 15),
                        Formulario = ReadString(rd, 16),
                        FacturaRelacionada = ReadString(rd, 17),
                        EstadoFacturacion = ReadString(rd, 18),
                        SystemUserDetalle = ReadString(rd, 19),
                        Motivo = ReadString(rd, 20),
                        Observaciones = ReadString(rd, 21),
                        DiasPendientes = ReadInt(rd, 22),
                        NumeroNormalizado = ReadString(rd, 23),
                        SucursalesDetectadas = ReadString(rd, 24),
                        UsuariosDetectados = ReadString(rd, 25),
                        ImpactoContable = ReadString(rd, 26),
                        ComprobantesInvolucrados = ReadInt(rd, 27),
                        ContablesDuplicados = ReadInt(rd, 28),
                        DetalleGrupo = ReadString(rd, 29)
                    });
                }
            }

            return new AuditoriaUsuarioResultDto
            {
                Items = items,
                Stats = stats,
                TotalRegistros = total,
                Pagina = pagina,
                TamanioPagina = tamanio,
                DiasMinimosSinFacturaDefault = defaults.DiasMinimosSinFactura,
                UmbralModificacionesDefault = defaults.UmbralModificaciones,
                DiasToleranciaDuplicadosDefault = defaults.DiasToleranciaDuplicados,
                SoloDiferenciasSucursalDefault = defaults.SoloDiferenciasSucursal
            };
        }, "No se pudo cargar la auditoría de usuarios.", ct);
    }

    public async Task<AuditoriaComprobanteResultDto> SearchComprobanteAuditAsync(AuditoriaComprobanteFilterDto filter, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Auditoria", "SearchComprobanteAudit", async token =>
        {
            filter ??= new AuditoriaComprobanteFilterDto();
            var pagina = Math.Max(1, filter.Pagina);
            var tamanio = Math.Clamp(filter.TamanioPagina, 10, 200);
            var offset = (pagina - 1) * tamanio;
            var orderBy = ResolveComprobanteAuditOrderBy(filter.OrdenCampo, filter.OrdenDireccion);

            var sql = $"""
                IF OBJECT_ID('tempdb..#VentasPeriodo') IS NOT NULL DROP TABLE #VentasPeriodo;
                IF OBJECT_ID('tempdb..#Duplicados') IS NOT NULL DROP TABLE #Duplicados;
                IF OBJECT_ID('tempdb..#InsumosAgg') IS NOT NULL DROP TABLE #InsumosAgg;
                IF OBJECT_ID('tempdb..#TareasAgg') IS NOT NULL DROP TABLE #TareasAgg;
                IF OBJECT_ID('tempdb..#ObservAgg') IS NOT NULL DROP TABLE #ObservAgg;
                IF OBJECT_ID('tempdb..#AlertasCmp') IS NOT NULL DROP TABLE #AlertasCmp;

                SELECT
                    ISNULL(c.ID, 0) AS ID,
                    ISNULL(c.TC, '') AS TC,
                    ISNULL(c.IDCOMPROBANTE, '') AS IDCOMPROBANTE,
                    ISNULL(c.IDCOMPLEMENTO, 0) AS IDCOMPLEMENTO,
                    ISNULL(c.CUENTA, '') AS CUENTA,
                    ISNULL(c.NOMBRE, '') AS NOMBRE,
                    ISNULL(c.IMPORTE, 0) AS IMPORTE,
                    CAST(ISNULL(c.FECHA, GETDATE()) AS datetime) AS FECHA,
                    ISNULL(c.FechaHora_Grabacion, CAST(ISNULL(c.FECHA, GETDATE()) AS datetime)) AS FechaHora_Grabacion,
                    ISNULL(c.ANULADA, 0) AS ANULADA
                INTO #VentasPeriodo
                FROM dbo.V_MV_Cpte c
                WHERE (@Desde IS NULL OR c.FECHA >= @Desde)
                  AND (@Hasta IS NULL OR c.FECHA < DATEADD(day, 1, @Hasta));

                SELECT
                    UPPER(LTRIM(RTRIM(TC))) AS TC,
                    UPPER(LTRIM(RTRIM(CUENTA))) AS CUENTA,
                    CAST(FECHA AS date) AS FECHA,
                    ROUND(ISNULL(IMPORTE, 0), 2) AS IMPORTE,
                    COUNT(*) AS Cantidad,
                    STUFF((
                        SELECT ', ' + v2.IDCOMPROBANTE
                        FROM #VentasPeriodo v2
                        WHERE UPPER(LTRIM(RTRIM(v2.TC))) = UPPER(LTRIM(RTRIM(v.TC)))
                          AND UPPER(LTRIM(RTRIM(v2.CUENTA))) = UPPER(LTRIM(RTRIM(v.CUENTA)))
                          AND CAST(v2.FECHA AS date) = CAST(v.FECHA AS date)
                          AND ROUND(ISNULL(v2.IMPORTE, 0), 2) = ROUND(ISNULL(v.IMPORTE, 0), 2)
                        ORDER BY v2.IDCOMPROBANTE
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 2, '') AS ComprobantesRelacionados
                INTO #Duplicados
                FROM #VentasPeriodo v
                WHERE ISNULL(v.ANULADA, 0) = 0
                  AND ISNULL(v.CUENTA, '') <> ''
                  AND ROUND(ISNULL(v.IMPORTE, 0), 2) <> 0
                GROUP BY
                    UPPER(LTRIM(RTRIM(TC))),
                    UPPER(LTRIM(RTRIM(CUENTA))),
                    CAST(FECHA AS date),
                    ROUND(ISNULL(IMPORTE, 0), 2)
                HAVING COUNT(*) > 1;

                SELECT
                    UPPER(LTRIM(RTRIM(TC))) AS TC,
                    UPPER(LTRIM(RTRIM(IDCOMPROBANTE))) AS IDCOMPROBANTE,
                    ISNULL(IDCOMPLEMENTO, 0) AS IDCOMPLEMENTO,
                    ROUND(SUM(ISNULL(TOTAL, 0)), 2) AS TotalInsumos,
                    COUNT(*) AS CantidadLineas
                INTO #InsumosAgg
                FROM dbo.V_MV_CpteInsumos
                GROUP BY UPPER(LTRIM(RTRIM(TC))), UPPER(LTRIM(RTRIM(IDCOMPROBANTE))), ISNULL(IDCOMPLEMENTO, 0);

                SELECT
                    UPPER(LTRIM(RTRIM(TC))) AS TC,
                    UPPER(LTRIM(RTRIM(IDCOMPROBANTE))) AS IDCOMPROBANTE,
                    ISNULL(IDCOMPLEMENTO, 0) AS IDCOMPLEMENTO,
                    ROUND(SUM(ISNULL(TOTAL, 0)), 2) AS TotalTareas,
                    COUNT(*) AS CantidadLineas
                INTO #TareasAgg
                FROM dbo.V_MV_CpteTareas
                GROUP BY UPPER(LTRIM(RTRIM(TC))), UPPER(LTRIM(RTRIM(IDCOMPROBANTE))), ISNULL(IDCOMPLEMENTO, 0);

                SELECT
                    UPPER(LTRIM(RTRIM(TC))) AS TC,
                    UPPER(LTRIM(RTRIM(IDCOMPROBANTE))) AS IDCOMPROBANTE,
                    ISNULL(IDCOMPLEMENTO, 0) AS IDCOMPLEMENTO,
                    ROUND(SUM(ISNULL(IMPORTE, 0)), 2) AS TotalObserv,
                    COUNT(*) AS CantidadLineas
                INTO #ObservAgg
                FROM dbo.V_MV_CPTE_OBSERV
                GROUP BY UPPER(LTRIM(RTRIM(TC))), UPPER(LTRIM(RTRIM(IDCOMPROBANTE))), ISNULL(IDCOMPLEMENTO, 0);

                CREATE TABLE #AlertasCmp
                (
                    TipoControl nvarchar(50) NOT NULL,
                    TipoControlLabel nvarchar(120) NOT NULL,
                    Riesgo nvarchar(20) NOT NULL,
                    RiesgoOrden int NOT NULL,
                    FechaHora datetime NOT NULL,
                    TipoComprobante nvarchar(20) NOT NULL,
                    IdComprobante nvarchar(40) NOT NULL,
                    IdComplemento int NOT NULL,
                    Cuenta nvarchar(50) NOT NULL,
                    Cliente nvarchar(150) NOT NULL,
                    ImporteCabecera decimal(18, 2) NULL,
                    ImporteDetalle decimal(18, 2) NULL,
                    Diferencia decimal(18, 2) NULL,
                    CantidadOcurrencias int NOT NULL,
                    Motivo nvarchar(max) NOT NULL,
                    Observaciones nvarchar(max) NOT NULL
                );

                INSERT INTO #AlertasCmp
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora,
                    TipoComprobante, IdComprobante, IdComplemento, Cuenta, Cliente,
                    ImporteCabecera, ImporteDetalle, Diferencia, CantidadOcurrencias,
                    Motivo, Observaciones
                )
                SELECT
                    'DUPLICADO_POTENCIAL',
                    'Duplicados potenciales',
                    'ALTO',
                    2,
                    v.FechaHora_Grabacion,
                    v.TC,
                    v.IDCOMPROBANTE,
                    v.IDCOMPLEMENTO,
                    v.CUENTA,
                    v.NOMBRE,
                    ROUND(v.IMPORTE, 2),
                    NULL,
                    NULL,
                    d.Cantidad,
                    'Se detectó más de un comprobante con mismo TC, cuenta, fecha e importe.',
                    'Comprobantes del grupo: ' + ISNULL(d.ComprobantesRelacionados, '')
                FROM #VentasPeriodo v
                INNER JOIN #Duplicados d
                    ON d.TC = UPPER(LTRIM(RTRIM(v.TC)))
                   AND d.CUENTA = UPPER(LTRIM(RTRIM(v.CUENTA)))
                   AND d.FECHA = CAST(v.FECHA AS date)
                   AND d.IMPORTE = ROUND(ISNULL(v.IMPORTE, 0), 2);

                INSERT INTO #AlertasCmp
                (
                    TipoControl, TipoControlLabel, Riesgo, RiesgoOrden, FechaHora,
                    TipoComprobante, IdComprobante, IdComplemento, Cuenta, Cliente,
                    ImporteCabecera, ImporteDetalle, Diferencia, CantidadOcurrencias,
                    Motivo, Observaciones
                )
                SELECT
                    'DETALLE_CABECERA',
                    'Cabecera vs detalle',
                    CASE
                        WHEN (ISNULL(i.CantidadLineas, 0) + ISNULL(t.CantidadLineas, 0) + ISNULL(o.CantidadLineas, 0)) = 0 THEN 'ALTO'
                        WHEN ABS(ROUND(v.IMPORTE, 2) - ROUND(ISNULL(i.TotalInsumos, 0) + ISNULL(t.TotalTareas, 0) + ISNULL(o.TotalObserv, 0), 2)) >= 1 THEN 'ALTO'
                        ELSE 'MEDIO'
                    END,
                    CASE
                        WHEN (ISNULL(i.CantidadLineas, 0) + ISNULL(t.CantidadLineas, 0) + ISNULL(o.CantidadLineas, 0)) = 0 THEN 2
                        WHEN ABS(ROUND(v.IMPORTE, 2) - ROUND(ISNULL(i.TotalInsumos, 0) + ISNULL(t.TotalTareas, 0) + ISNULL(o.TotalObserv, 0), 2)) >= 1 THEN 2
                        ELSE 1
                    END,
                    v.FechaHora_Grabacion,
                    v.TC,
                    v.IDCOMPROBANTE,
                    v.IDCOMPLEMENTO,
                    v.CUENTA,
                    v.NOMBRE,
                    ROUND(v.IMPORTE, 2),
                    ROUND(ISNULL(i.TotalInsumos, 0) + ISNULL(t.TotalTareas, 0) + ISNULL(o.TotalObserv, 0), 2),
                    ROUND(v.IMPORTE - (ISNULL(i.TotalInsumos, 0) + ISNULL(t.TotalTareas, 0) + ISNULL(o.TotalObserv, 0)), 2),
                    1,
                    CASE
                        WHEN (ISNULL(i.CantidadLineas, 0) + ISNULL(t.CantidadLineas, 0) + ISNULL(o.CantidadLineas, 0)) = 0 THEN 'La cabecera tiene importe pero no se encontraron líneas de detalle.'
                        ELSE 'La suma del detalle no coincide con el importe de cabecera.'
                    END,
                    CONCAT(
                        'Insumos: ', CONVERT(nvarchar(50), ROUND(ISNULL(i.TotalInsumos, 0), 2)),
                        ' · Tareas: ', CONVERT(nvarchar(50), ROUND(ISNULL(t.TotalTareas, 0), 2)),
                        ' · Observaciones: ', CONVERT(nvarchar(50), ROUND(ISNULL(o.TotalObserv, 0), 2))
                    )
                FROM #VentasPeriodo v
                LEFT JOIN #InsumosAgg i
                    ON i.TC = UPPER(LTRIM(RTRIM(v.TC)))
                   AND i.IDCOMPROBANTE = UPPER(LTRIM(RTRIM(v.IDCOMPROBANTE)))
                   AND i.IDCOMPLEMENTO = v.IDCOMPLEMENTO
                LEFT JOIN #TareasAgg t
                    ON t.TC = UPPER(LTRIM(RTRIM(v.TC)))
                   AND t.IDCOMPROBANTE = UPPER(LTRIM(RTRIM(v.IDCOMPROBANTE)))
                   AND t.IDCOMPLEMENTO = v.IDCOMPLEMENTO
                LEFT JOIN #ObservAgg o
                    ON o.TC = UPPER(LTRIM(RTRIM(v.TC)))
                   AND o.IDCOMPROBANTE = UPPER(LTRIM(RTRIM(v.IDCOMPROBANTE)))
                   AND o.IDCOMPLEMENTO = v.IDCOMPLEMENTO
                WHERE ISNULL(v.ANULADA, 0) = 0
                  AND ROUND(ISNULL(v.IMPORTE, 0), 2) <> ROUND(ISNULL(i.TotalInsumos, 0) + ISNULL(t.TotalTareas, 0) + ISNULL(o.TotalObserv, 0), 2);

                SELECT
                    TipoControl,
                    TipoControlLabel,
                    Riesgo,
                    FechaHora,
                    TipoComprobante,
                    IdComprobante,
                    IdComplemento,
                    Cuenta,
                    Cliente,
                    ImporteCabecera,
                    ImporteDetalle,
                    Diferencia,
                    CantidadOcurrencias,
                    Motivo,
                    Observaciones
                FROM #AlertasCmp
                WHERE (@TipoComprobante = '' OR TipoComprobante LIKE '%' + @TipoComprobante + '%')
                  AND (@Riesgo = '' OR Riesgo = @Riesgo)
                  AND (@TipoControl = '' OR TipoControl = @TipoControl)
                  AND (
                        @CuentaCliente = ''
                        OR Cuenta LIKE '%' + @CuentaCliente + '%'
                        OR Cliente LIKE '%' + @CuentaCliente + '%'
                      )
                  AND (
                        @Texto = ''
                        OR TipoComprobante LIKE '%' + @Texto + '%'
                        OR IdComprobante LIKE '%' + @Texto + '%'
                        OR Cuenta LIKE '%' + @Texto + '%'
                        OR Cliente LIKE '%' + @Texto + '%'
                        OR Motivo LIKE '%' + @Texto + '%'
                        OR Observaciones LIKE '%' + @Texto + '%'
                      )
                ORDER BY {orderBy}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT
                    COUNT(*) AS TotalAlertas,
                    SUM(CASE WHEN Riesgo = 'ALTO' THEN 1 ELSE 0 END) AS RiesgoAlto,
                    SUM(CASE WHEN Riesgo = 'MEDIO' THEN 1 ELSE 0 END) AS RiesgoMedio,
                    COUNT(DISTINCT TipoControl) AS ControlesDetectados
                FROM #AlertasCmp
                WHERE (@TipoComprobante = '' OR TipoComprobante LIKE '%' + @TipoComprobante + '%')
                  AND (@Riesgo = '' OR Riesgo = @Riesgo)
                  AND (@TipoControl = '' OR TipoControl = @TipoControl)
                  AND (
                        @CuentaCliente = ''
                        OR Cuenta LIKE '%' + @CuentaCliente + '%'
                        OR Cliente LIKE '%' + @CuentaCliente + '%'
                      )
                  AND (
                        @Texto = ''
                        OR TipoComprobante LIKE '%' + @Texto + '%'
                        OR IdComprobante LIKE '%' + @Texto + '%'
                        OR Cuenta LIKE '%' + @Texto + '%'
                        OR Cliente LIKE '%' + @Texto + '%'
                        OR Motivo LIKE '%' + @Texto + '%'
                        OR Observaciones LIKE '%' + @Texto + '%'
                      );
                """;

            var items = new List<AuditoriaComprobanteRowDto>();
            var stats = new AuditoriaComprobanteStatsDto();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Desde", (object?)filter.Desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta", (object?)filter.Hasta ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Texto", filter.Texto ?? string.Empty);
            cmd.Parameters.AddWithValue("@TipoComprobante", filter.TipoComprobante ?? string.Empty);
            cmd.Parameters.AddWithValue("@Riesgo", filter.Riesgo ?? string.Empty);
            cmd.Parameters.AddWithValue("@TipoControl", filter.TipoControl ?? string.Empty);
            cmd.Parameters.AddWithValue("@CuentaCliente", filter.CuentaCliente ?? string.Empty);
            cmd.Parameters.AddWithValue("@Offset", offset);
            cmd.Parameters.AddWithValue("@PageSize", tamanio);
            cmd.CommandTimeout = 90;

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new AuditoriaComprobanteRowDto
                {
                    TipoControl = ReadString(rd, 0),
                    TipoControlLabel = ReadString(rd, 1),
                    Riesgo = ReadString(rd, 2),
                    FechaHora = ReadDateTime(rd, 3) ?? DateTime.MinValue,
                    TipoComprobante = ReadString(rd, 4),
                    IdComprobante = ReadString(rd, 5),
                    IdComplemento = ReadInt(rd, 6),
                    Cuenta = ReadString(rd, 7),
                    Cliente = ReadString(rd, 8),
                    ImporteCabecera = ReadDecimal(rd, 9),
                    ImporteDetalle = ReadDecimal(rd, 10),
                    Diferencia = ReadDecimal(rd, 11),
                    CantidadOcurrencias = ReadInt(rd, 12),
                    Motivo = ReadString(rd, 13),
                    Observaciones = ReadString(rd, 14)
                });
            }

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
            {
                total = ReadInt(rd, 0);
                stats = new AuditoriaComprobanteStatsDto
                {
                    TotalAlertas = total,
                    RiesgoAlto = ReadInt(rd, 1),
                    RiesgoMedio = ReadInt(rd, 2),
                    ControlesDetectados = ReadInt(rd, 3)
                };
            }

            return new AuditoriaComprobanteResultDto
            {
                Items = items,
                Stats = stats,
                TotalRegistros = total,
                Pagina = pagina,
                TamanioPagina = tamanio
            };
        }, "No se pudo cargar la auditoría de comprobantes.", ct);
    }

    private async Task<IReadOnlyList<string>> QueryDistinctAsync(string field, CancellationToken ct)
    {
        return await ExecuteLoggedAsync("Auditoria", $"GetDistinct{field}", async token =>
        {
            var sql = $"""
                SELECT DISTINCT TOP (200)
                    LTRIM(RTRIM(ISNULL({field}, '')))
                FROM dbo.AUX_ERR
                WHERE ISNULL({field}, '') <> ''
                ORDER BY LTRIM(RTRIM(ISNULL({field}, '')))
                """;

            var items = new List<string>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(rd.GetString(0));
            }

            return (IReadOnlyList<string>)items;
        }, $"No se pudo cargar la lista de {field.ToLowerInvariant()}.", ct);
    }

    private static async Task<(int DiasMinimosSinFactura, int UmbralModificaciones, int DiasToleranciaDuplicados, bool SoloDiferenciasSucursal)> ReadUserAuditDefaultsAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT
                CLAVE,
                CASE
                    WHEN ISNULL(VALOR, '') <> '' THEN VALOR
                    ELSE CONVERT(nvarchar(150), ValorAux)
                END AS Valor
            FROM dbo.TA_CONFIGURACION
            WHERE CLAVE IN (
                'AUDITORIA-USUARIOS-DIAS-RM-SIN-FACTURA',
                'AUDITORIA-USUARIOS-UMBRAL-MODIFICACIONES',
                'AUDITORIA-USUARIOS-DIAS-DUPLICADOS-COMPRA',
                'AUDITORIA-USUARIOS-SOLO-SUCURSAL-DUPLICADOS'
            );
            """;

        var dias = 2;
        var umbral = 3;
        var diasDuplicados = 7;
        var soloSucursal = true;
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var clave = rd.IsDBNull(0) ? string.Empty : rd.GetString(0);
            var valor = rd.IsDBNull(1) ? string.Empty : rd.GetString(1);
            if (string.Equals(clave, DiasRmSinFacturaKey, StringComparison.OrdinalIgnoreCase) && int.TryParse(valor, out var parsedDias))
            {
                dias = Math.Max(1, parsedDias);
            }
            else if (string.Equals(clave, UmbralModificacionesKey, StringComparison.OrdinalIgnoreCase) && int.TryParse(valor, out var parsedUmbral))
            {
                umbral = Math.Max(2, parsedUmbral);
            }
            else if (string.Equals(clave, DiasDuplicadosCompraKey, StringComparison.OrdinalIgnoreCase) && int.TryParse(valor, out var parsedDuplicados))
            {
                diasDuplicados = Math.Max(0, parsedDuplicados);
            }
            else if (string.Equals(clave, SoloSucursalDuplicadosKey, StringComparison.OrdinalIgnoreCase))
            {
                soloSucursal = IsTrueValue(valor);
            }
        }

        return (dias, umbral, diasDuplicados, soloSucursal);
    }

    private static async Task<AuditoriaUsuarioSettingsDto> ReadUserAuditSettingsAsync(
        SqlConnection cn,
        AuditoriaUsuarioSettingsDto inferred,
        CancellationToken ct)
    {
        const string sql = """
            SELECT
                CLAVE,
                CASE
                    WHEN ISNULL(VALOR, '') <> '' THEN VALOR
                    ELSE CONVERT(nvarchar(150), ValorAux)
                END AS Valor
            FROM dbo.TA_CONFIGURACION
            WHERE CLAVE IN (
                'AUDITORIA-USUARIOS-HORA-INICIO-LABORAL',
                'AUDITORIA-USUARIOS-HORA-FIN-LABORAL',
                'AUDITORIA-USUARIOS-DIAS-LABORALES',
                'AUDITORIA-USUARIOS-CONTROLAR-PC-HABITUAL'
            );
            """;

        var inicio = inferred.HoraInicioSugerida;
        var fin = inferred.HoraFinSugerida;
        var dias = inferred.DiasLaboralesSugeridos.ToList();
        var controlarPc = inferred.ControlarPcHabitualSugerido;
        var hasAny = false;

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var clave = ReadString(rd, 0);
            var valor = ReadString(rd, 1);
            if (string.IsNullOrWhiteSpace(valor))
                continue;

            hasAny = true;
            if (string.Equals(clave, HoraInicioLaboralKey, StringComparison.OrdinalIgnoreCase) && int.TryParse(valor, out var parsedInicio))
            {
                inicio = Math.Clamp(parsedInicio, 0, 23);
            }
            else if (string.Equals(clave, HoraFinLaboralKey, StringComparison.OrdinalIgnoreCase) && int.TryParse(valor, out var parsedFin))
            {
                fin = Math.Clamp(parsedFin, 1, 24);
            }
            else if (string.Equals(clave, DiasLaboralesKey, StringComparison.OrdinalIgnoreCase))
            {
                var parsedDays = NormalizeDays(valor.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(static x => int.TryParse(x, out var n) ? n : 0));
                if (parsedDays.Count > 0)
                    dias = parsedDays;
            }
            else if (string.Equals(clave, ControlarPcHabitualKey, StringComparison.OrdinalIgnoreCase))
            {
                controlarPc = IsTrueValue(valor);
            }
        }

        if (fin <= inicio)
            fin = Math.Min(24, inicio + 1);

        return new AuditoriaUsuarioSettingsDto
        {
            HoraInicioLaboral = inicio,
            HoraFinLaboral = fin,
            DiasLaborales = dias,
            ControlarPcHabitual = controlarPc,
            HoraInicioSugerida = inferred.HoraInicioSugerida,
            HoraFinSugerida = inferred.HoraFinSugerida,
            DiasLaboralesSugeridos = inferred.DiasLaboralesSugeridos,
            ControlarPcHabitualSugerido = inferred.ControlarPcHabitualSugerido,
            DiasAnalizados = inferred.DiasAnalizados,
            AccionesAnalizadas = inferred.AccionesAnalizadas,
            UsaValoresInferidos = !hasAny,
            ResumenInferencia = inferred.ResumenInferencia
        };
    }

    private static async Task<AuditoriaUsuarioSettingsDto> InferUserAuditSettingsAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT FECHAHORA
            FROM dbo.V_MV_CpteAcciones
            WHERE FECHAHORA >= DATEADD(day, -90, GETDATE())
              AND TC IN ('FC','FP','TK','TKFC','RM');
            """;

        var timestamps = new List<DateTime>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            timestamps.Add(rd.GetDateTime(0));
        }

        if (timestamps.Count == 0)
        {
            return new AuditoriaUsuarioSettingsDto
            {
                HoraInicioSugerida = 6,
                HoraFinSugerida = 21,
                DiasLaboralesSugeridos = [1, 2, 3, 4, 5],
                ControlarPcHabitualSugerido = true,
                DiasAnalizados = 90,
                AccionesAnalizadas = 0,
                ResumenInferencia = "Sin actividad relevante en los últimos 90 días. Se usó la sugerencia estándar Lunes a Viernes de 06:00 a 21:00."
            };
        }

        var hourCounts = timestamps
            .GroupBy(static x => x.Hour)
            .ToDictionary(g => g.Key, g => g.Count());
        var dayCounts = timestamps
            .GroupBy(static x => ToIsoDayOfWeek(x.DayOfWeek))
            .ToDictionary(g => g.Key, g => g.Count());

        var maxHourCount = hourCounts.Values.Max();
        var hourThreshold = Math.Max(3, (int)Math.Ceiling(maxHourCount * 0.15m));
        var significantHours = hourCounts
            .Where(x => x.Value >= hourThreshold)
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToList();

        var startHour = significantHours.Count > 0 ? significantHours.Min() : 6;
        var endHour = significantHours.Count > 0 ? Math.Min(24, significantHours.Max() + 1) : 21;
        if (endHour <= startHour)
            endHour = Math.Min(24, startHour + 1);

        var maxDayCount = dayCounts.Values.Max();
        var dayThreshold = Math.Max(2, (int)Math.Ceiling(maxDayCount * 0.20m));
        var activeDays = NormalizeDays(dayCounts.Where(x => x.Value >= dayThreshold).Select(x => x.Key));
        if (activeDays.Count == 0)
            activeDays = [1, 2, 3, 4, 5];

        var uniqueDates = timestamps.Select(static x => x.Date).Distinct().Count();
        return new AuditoriaUsuarioSettingsDto
        {
            HoraInicioSugerida = startHour,
            HoraFinSugerida = endHour,
            DiasLaboralesSugeridos = activeDays,
            ControlarPcHabitualSugerido = true,
            DiasAnalizados = Math.Min(90, uniqueDates),
            AccionesAnalizadas = timestamps.Count,
            ResumenInferencia = $"Sugerencia calculada con {timestamps.Count:N0} acción(es) de los últimos 90 días: {FormatDays(activeDays)} de {startHour:00}:00 a {endHour:00}:00."
        };
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

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var column = Convert.ToString(result) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static async Task UpsertConfigValueAsync(SqlConnection cn, string detailColumn, string key, string value, CancellationToken ct)
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

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@ClaveNormalizada", key.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Clave", key);
        cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
        cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
        cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static (string Value, string AuxValue) SplitStoredValue(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length <= 150)
            return (normalized, string.Empty);

        return (string.Empty, normalized);
    }

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static List<int> NormalizeDays(IEnumerable<int> days)
        => days.Where(static x => x is >= 1 and <= 7).Distinct().OrderBy(static x => x).ToList();

    private static bool IsTrueValue(string value)
        => string.Equals(value, "SI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

    private static int ToIsoDayOfWeek(DayOfWeek day)
        => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static string FormatDays(IReadOnlyList<int> days)
    {
        var labels = days.Select(static day => day switch
        {
            1 => "Lun",
            2 => "Mar",
            3 => "Mié",
            4 => "Jue",
            5 => "Vie",
            6 => "Sáb",
            7 => "Dom",
            _ => string.Empty
        }).Where(static x => !string.IsNullOrWhiteSpace(x));

        return string.Join(", ", labels);
    }

    private static string ResolveUserAuditOrderBy(string? field, string? direction)
    {
        var descending = !string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
        var sqlField = (field ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "control" => "TipoControlLabel",
            "riesgo" => "RiesgoOrden",
            "usuario" => "Usuario",
            "pc" => "Pc",
            "tc" => "TipoComprobante",
            "comprobante" => "IdComprobante",
            "cliente" => "Cliente",
            "numero" => "NumeroNormalizado",
            "impactocontable" => "ImpactoContable",
            "estadofacturacion" => "EstadoFacturacion",
            "importe" => "ISNULL(ImporteOriginal, 0)",
            "aplicado" => "ISNULL(ImporteAplicado, 0)",
            "diferencia" => "ISNULL(Diferencia, 0)",
            "cantidad" => "CantidadOcurrencias",
            "involucrados" => "ComprobantesInvolucrados",
            _ => "FechaHora"
        };

        var suffix = descending ? "DESC" : "ASC";
        return sqlField switch
        {
            "RiesgoOrden" => $"RiesgoOrden {suffix}, FechaHora DESC, TipoControlLabel ASC",
            "FechaHora" => $"FechaHora {suffix}, TipoControlLabel ASC",
            _ => $"{sqlField} {suffix}, FechaHora DESC"
        };
    }

    private static string ResolveComprobanteAuditOrderBy(string? field, string? direction)
    {
        var descending = !string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
        var sqlField = (field ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "control" => "TipoControlLabel",
            "riesgo" => "RiesgoOrden",
            "tc" => "TipoComprobante",
            "comprobante" => "IdComprobante",
            "cliente" => "Cliente",
            "cuenta" => "Cuenta",
            "importecabecera" => "ISNULL(ImporteCabecera, 0)",
            "importedetalle" => "ISNULL(ImporteDetalle, 0)",
            "diferencia" => "ISNULL(Diferencia, 0)",
            "cantidad" => "CantidadOcurrencias",
            _ => "FechaHora"
        };

        var suffix = descending ? "DESC" : "ASC";
        return sqlField switch
        {
            "RiesgoOrden" => $"RiesgoOrden {suffix}, FechaHora DESC, TipoControlLabel ASC",
            "FechaHora" => $"FechaHora {suffix}, TipoControlLabel ASC",
            _ => $"{sqlField} {suffix}, FechaHora DESC"
        };
    }

    private static async Task<int> ScalarIntAsync(string sql, SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static string ReadString(SqlDataReader rd, int ordinal)
        => rd.IsDBNull(ordinal) ? string.Empty : Convert.ToString(rd.GetValue(ordinal)) ?? string.Empty;

    private static int ReadInt(SqlDataReader rd, int ordinal)
        => rd.IsDBNull(ordinal) ? 0 : Convert.ToInt32(rd.GetValue(ordinal));

    private static DateTime? ReadDateTime(SqlDataReader rd, int ordinal)
        => rd.IsDBNull(ordinal) ? null : Convert.ToDateTime(rd.GetValue(ordinal));

    private static decimal? ReadDecimal(SqlDataReader rd, int ordinal)
        => rd.IsDBNull(ordinal) ? null : Convert.ToDecimal(rd.GetValue(ordinal));

    private static async Task<List<AuditoriaRankingItemDto>> QueryRankingAsync(string sql, SqlConnection cn, CancellationToken ct)
    {
        var items = new List<AuditoriaRankingItemDto>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new AuditoriaRankingItemDto
            {
                Label = rd.GetString(0),
                Value = rd.GetInt32(1)
            });
        }

        return items;
    }

    private static AuditoriaErrorRowDto MapError(SqlDataReader rd)
    {
        return new AuditoriaErrorRowDto
        {
            Id = Convert.ToInt32(rd.GetValue(0)),
            Fecha = rd.GetDateTime(1),
            Proceso = rd.GetString(2),
            ErrorCodigo = rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd.GetValue(3)),
            Descripcion = rd.GetString(4),
            Sql = rd.GetString(5),
            Pc = rd.GetString(6),
            Usuario = rd.GetString(7)
        };
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new InvalidOperationException("La tabla AUX_ERR no existe en esta base de datos activa.");
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

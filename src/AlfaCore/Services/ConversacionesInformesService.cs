using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class ConversacionesInformesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IConversacionesInformesService
{
    private const string ModuleName = "Conversaciones";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    // Agregación del período: una fila por cliente (agrupando sus contactos) o por contacto sin
    // cliente. Incluye clientes con abono activo aunque no tengan actividad; excluye no-abonados sin
    // movimientos y el Chat interno. @Desde/@Hasta se pasan desde C# (evita DATEFROMPARTS por compat).
    private const string AggregateSql = """
        WITH ConvMes AS (
            SELECT c.IdConversacion, m.FechaHora, m.Direction,
                CASE WHEN LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N''))) <> N'' THEN N'C:' + LTRIM(RTRIM(c.ClienteCodigo))
                     WHEN c.IdContacto IS NOT NULL THEN N'K:' + CONVERT(nvarchar(20), c.IdContacto)
                     ELSE N'S:' END AS GKey
            FROM dbo.CONV_MENSAJES m
            INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
            WHERE m.FechaHora >= @Desde AND m.FechaHora < @Hasta
              AND ISNULL(c.Canal, N'') <> N'INTERNO'
              AND m.Direction IN (N'ENTRANTE', N'SALIENTE')
        ),
        MsgAgg AS (
            SELECT GKey,
                COUNT(DISTINCT IdConversacion) AS CantConversaciones,
                COUNT(DISTINCT CAST(FechaHora AS date)) AS CantDias,
                COUNT(*) AS CantMensajes,
                SUM(CASE WHEN Direction = N'ENTRANTE' THEN 1 ELSE 0 END) AS CantMensajesEntrantes,
                SUM(CASE WHEN Direction = N'SALIENTE' THEN 1 ELSE 0 END) AS CantMensajesSalientes
            FROM ConvMes GROUP BY GKey
        ),
        PartesRaw AS (
            SELECT
                CASE WHEN LTRIM(RTRIM(ISNULL(p.ClienteCodigo, N''))) <> N'' THEN N'C:' + LTRIM(RTRIM(p.ClienteCodigo))
                     WHEN p.IdContacto IS NOT NULL THEN N'K:' + CONVERT(nvarchar(20), p.IdContacto)
                     ELSE N'S:' END AS GKey,
                p.Minutos, p.Facturable
            FROM dbo.ALFACORE_PARTES_HORAS p
            WHERE p.Fecha >= @Desde AND p.Fecha < @Hasta
        ),
        PartesAgg AS (
            SELECT GKey, SUM(Minutos) AS MinutosTotales,
                SUM(CASE WHEN Facturable = 1 THEN Minutos ELSE 0 END) AS MinutosFacturables
            FROM PartesRaw GROUP BY GKey
        ),
        AbonoAgg AS (
            SELECT N'C:' + LTRIM(RTRIM(a.Cuenta)) AS GKey
            FROM dbo.MA_CUENTAS_AUTOCPTES a
            WHERE ISNULL(a.Activo, 0) = 1
              AND a.FechaUltMov >= DATEADD(MONTH, -3, GETDATE())
              AND LTRIM(RTRIM(ISNULL(a.Cuenta, N''))) <> N''
            GROUP BY LTRIM(RTRIM(a.Cuenta))
        ),
        Keys AS (
            SELECT GKey FROM MsgAgg
            UNION SELECT GKey FROM PartesAgg
            UNION SELECT GKey FROM AbonoAgg
        ),
        KeysParsed AS (
            SELECT GKey,
                CASE WHEN GKey LIKE N'C:%' THEN N'CLIENTE' WHEN GKey LIKE N'K:%' THEN N'CONTACTO' ELSE N'SININDENT' END AS TipoFila,
                CASE WHEN GKey LIKE N'C:%' THEN SUBSTRING(GKey, 3, 20) ELSE N'' END AS ClienteCodigo,
                CASE WHEN GKey LIKE N'K:%' THEN CONVERT(int, SUBSTRING(GKey, 3, 20)) ELSE NULL END AS IdContacto
            FROM Keys
        )
        SELECT
            k.TipoFila,
            k.ClienteCodigo,
            k.IdContacto,
            COALESCE(NULLIF(LTRIM(RTRIM(cli.RAZON_SOCIAL)), N''), NULLIF(LTRIM(RTRIM(con.Nombre_y_Apellido)), N''), N'Sin identificar') AS NombreMostrar,
            ISNULL(LTRIM(RTRIM(cli.Clasificacion)), N'') AS ClasificacionCodigo,
            ISNULL(clsd.Descripcion, N'') AS ClasificacionDesc,
            ISNULL(catd.Descripcion, N'') AS CategoriaDesc,
            ISNULL(ab.Estado, N'SinAbono') AS EstadoAbono,
            ISNULL(ma.CantConversaciones, 0) AS CantConversaciones,
            ISNULL(ma.CantDias, 0) AS CantDias,
            ISNULL(ma.CantMensajes, 0) AS CantMensajes,
            ISNULL(ma.CantMensajesEntrantes, 0) AS CantMensajesEntrantes,
            ISNULL(ma.CantMensajesSalientes, 0) AS CantMensajesSalientes,
            ISNULL(pa.MinutosTotales, 0) AS MinutosTotales,
            ISNULL(pa.MinutosFacturables, 0) AS MinutosFacturables
        FROM KeysParsed k
        LEFT JOIN MsgAgg ma ON ma.GKey = k.GKey
        LEFT JOIN PartesAgg pa ON pa.GKey = k.GKey
        LEFT JOIN dbo.VT_CLIENTES cli ON k.ClienteCodigo <> N'' AND UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(k.ClienteCodigo)
        LEFT JOIN dbo.TA_CLASIFICACIONES clsd ON clsd.Codigo = cli.Clasificacion
        LEFT JOIN dbo.v_ta_categoria catd ON catd.IdCategoria = cli.IDCategoria
        LEFT JOIN dbo.MA_CONTACTOS con ON con.id = k.IdContacto
        OUTER APPLY (
            SELECT TOP (1) CASE
                    WHEN ISNULL(auto.Activo, 0) = 0 THEN N'Suspendido'
                    WHEN auto.FechaUltMov IS NULL OR auto.FechaUltMov < DATEADD(MONTH, -3, GETDATE()) THEN N'Suspendido'
                    ELSE N'Abonado' END AS Estado
            FROM dbo.MA_CUENTAS_AUTOCPTES auto
            WHERE k.ClienteCodigo <> N'' AND UPPER(LTRIM(RTRIM(auto.Cuenta))) = UPPER(k.ClienteCodigo)
            ORDER BY auto.FechaUltMov DESC
        ) ab
        WHERE ISNULL(ab.Estado, N'SinAbono') = N'Abonado'
           OR ISNULL(ma.CantMensajes, 0) > 0
           OR ISNULL(pa.MinutosTotales, 0) > 0
        ORDER BY ISNULL(ma.CantMensajes, 0) + ISNULL(pa.MinutosTotales, 0) DESC, NombreMostrar;
        """;

    public Task<ConversacionInformeMensualDto> GenerarAsync(int anio, int mes, string? usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync("GenerarInforme", async token =>
        {
            if (mes < 1 || mes > 12)
                throw new ArgumentOutOfRangeException(nameof(mes), "El mes debe estar entre 1 y 12.");

            var desde = new DateTime(anio, mes, 1);
            var hasta = desde.AddMonths(1);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var filas = (await cn.QueryAsync<ConversacionInformeFilaDto>(new CommandDefinition(
                AggregateSql, new { Desde = desde, Hasta = hasta }, cancellationToken: token))).ToList();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);

            // Reemplazar el informe del período si ya existía.
            var idExistente = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT IdInforme FROM dbo.CONV_INFORME_MENSUAL WHERE Anio = @Anio AND Mes = @Mes;",
                new { Anio = anio, Mes = mes }, tx, cancellationToken: token));

            if (idExistente.HasValue)
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.CONV_INFORME_MENSUAL_DET WHERE IdInforme = @Id; DELETE FROM dbo.CONV_INFORME_MENSUAL WHERE IdInforme = @Id;",
                    new { Id = idExistente.Value }, tx, cancellationToken: token));
            }

            var idInforme = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO dbo.CONV_INFORME_MENSUAL (Anio, Mes, FechaGeneracion, UsuarioGeneracion, Estado)
                VALUES (@Anio, @Mes, GETDATE(), @Usuario, N'GENERADO');
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, new { Anio = anio, Mes = mes, Usuario = (usuario ?? string.Empty).Trim() }, tx, cancellationToken: token));

            foreach (var f in filas)
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.CONV_INFORME_MENSUAL_DET
                    (IdInforme, TipoFila, ClienteCodigo, IdContacto, NombreMostrar, ClasificacionCodigo, ClasificacionDesc,
                     CategoriaDesc, EstadoAbono, CantConversaciones, CantDias, CantMensajes, CantMensajesEntrantes,
                     CantMensajesSalientes, MinutosTotales, MinutosFacturables)
                    VALUES
                    (@IdInforme, @TipoFila, @ClienteCodigo, @IdContacto, @NombreMostrar, @ClasificacionCodigo, @ClasificacionDesc,
                     @CategoriaDesc, @EstadoAbono, @CantConversaciones, @CantDias, @CantMensajes, @CantMensajesEntrantes,
                     @CantMensajesSalientes, @MinutosTotales, @MinutosFacturables);
                    """, new
                {
                    IdInforme = idInforme,
                    f.TipoFila,
                    ClienteCodigo = string.IsNullOrWhiteSpace(f.ClienteCodigo) ? null : f.ClienteCodigo,
                    f.IdContacto,
                    f.NombreMostrar,
                    ClasificacionCodigo = string.IsNullOrWhiteSpace(f.ClasificacionCodigo) ? null : f.ClasificacionCodigo,
                    ClasificacionDesc = string.IsNullOrWhiteSpace(f.ClasificacionDesc) ? null : f.ClasificacionDesc,
                    CategoriaDesc = string.IsNullOrWhiteSpace(f.CategoriaDesc) ? null : f.CategoriaDesc,
                    f.EstadoAbono,
                    f.CantConversaciones,
                    f.CantDias,
                    f.CantMensajes,
                    f.CantMensajesEntrantes,
                    f.CantMensajesSalientes,
                    f.MinutosTotales,
                    f.MinutosFacturables
                }, tx, cancellationToken: token));
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(ModuleName, "GenerarInforme", "CONV_INFORME_MENSUAL",
                idInforme.ToString(), "Informe mensual de conversaciones generado.",
                new { anio, mes, filas = filas.Count }, token);

            return (await GetAsync(idInforme, token))!;
        }, ct);

    public Task<IReadOnlyList<ConversacionInformeListItemDto>> ListarAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("ListarInformes", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var items = (await cn.QueryAsync<ConversacionInformeListItemDto>(new CommandDefinition("""
                SELECT h.IdInforme, h.Anio, h.Mes, h.FechaGeneracion,
                    (SELECT COUNT(1) FROM dbo.CONV_INFORME_MENSUAL_DET d WHERE d.IdInforme = h.IdInforme) AS CantFilas
                FROM dbo.CONV_INFORME_MENSUAL h
                ORDER BY h.Anio DESC, h.Mes DESC;
                """, cancellationToken: token))).ToList();
            return (IReadOnlyList<ConversacionInformeListItemDto>)items;
        }, ct);

    public Task<ConversacionInformeMensualDto?> GetAsync(int idInforme, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetInforme", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var cab = await cn.QuerySingleOrDefaultAsync<ConversacionInformeMensualDto>(new CommandDefinition(
                "SELECT IdInforme, Anio, Mes, FechaGeneracion, ISNULL(UsuarioGeneracion, N'') AS UsuarioGeneracion, Estado FROM dbo.CONV_INFORME_MENSUAL WHERE IdInforme = @Id;",
                new { Id = idInforme }, cancellationToken: token));
            if (cab is null)
                return null;

            cab.Filas = (await cn.QueryAsync<ConversacionInformeFilaDto>(new CommandDefinition(
                DetalleSelectSql, new { Id = idInforme }, cancellationToken: token))).ToList();
            return cab;
        }, ct);

    public Task<ConversacionInformeMensualDto?> GetByPeriodoAsync(int anio, int mes, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetInformePeriodo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var id = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT IdInforme FROM dbo.CONV_INFORME_MENSUAL WHERE Anio = @Anio AND Mes = @Mes;",
                new { Anio = anio, Mes = mes }, cancellationToken: token));
            return id.HasValue ? await GetAsync(id.Value, token) : null;
        }, ct);

    private const string DetalleSelectSql = """
        SELECT IdDetalle, IdInforme, TipoFila, ISNULL(ClienteCodigo, N'') AS ClienteCodigo, IdContacto,
            NombreMostrar, ISNULL(ClasificacionCodigo, N'') AS ClasificacionCodigo, ISNULL(ClasificacionDesc, N'') AS ClasificacionDesc,
            ISNULL(CategoriaDesc, N'') AS CategoriaDesc, EstadoAbono, CantConversaciones, CantDias, CantMensajes,
            CantMensajesEntrantes, CantMensajesSalientes, MinutosTotales, MinutosFacturables,
            ISNULL(ResumenBorrador, N'') AS ResumenBorrador, ISNULL(ResumenEditado, N'') AS ResumenEditado,
            ResumenGeneradoIA, EstadoEnvio, FechaEnvio, ISNULL(CanalEnvio, N'') AS CanalEnvio
        FROM dbo.CONV_INFORME_MENSUAL_DET
        WHERE IdInforme = @Id
        ORDER BY (CantMensajes + MinutosTotales) DESC, NombreMostrar;
        """;

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, action, ex,
                "No se pudo completar la operación de informes de conversaciones.",
                null, AppEventSeverity.Error, ct);
            throw;
        }
    }
}

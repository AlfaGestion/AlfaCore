using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Clientes que comparten tarifas: un cliente principal (ej. The Faraway) tiene clientes vinculados
// (ej. otras razones sociales). Cada alta/modificación/baja de una tarifa de cliente del principal se
// duplica o actualiza en los vinculados, de modo que el cálculo de viajes sigue resolviendo por IDCLIENTE.
public sealed partial class CargaViajesService
{
    private const string TarifaVinculosTable = "TA_TARIFA_CLIENTES_VINCULADOS";

    private static readonly string[] TarifaPropagacionColumnas =
    [
        "id", "idlista", "nombre", "importe", "idcliente", "idchofer", "iddestino", "idtipovehiculo", "tarifafletero",
        "porcentajeadic", "porcentajeadic1", "porcentajeadic2", "porcentajeadic3", "porcentajeadic4", "activo",
        "fechahora_alta", "fechahora_modificacion", "usuariomodificacion",
        "adicionalfijo1descripcion", "adicionalfijo1importe", "adicionalfijo1pidecantidad",
        "adicionalfijo2descripcion", "adicionalfijo2importe", "adicionalfijo2pidecantidad",
        "adicionalfijo3descripcion", "adicionalfijo3importe", "adicionalfijo3pidecantidad",
        "idtarifaorigen"
    ];

    public Task<IReadOnlyList<CargaViajeLookupOptionDto>> GetClientesVinculadosTarifaAsync(string clientePrincipal, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetClientesVinculadosTarifa", async token =>
        {
            var principal = (clientePrincipal ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(principal))
                return (IReadOnlyList<CargaViajeLookupOptionDto>)Array.Empty<CargaViajeLookupOptionDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, TarifaVinculosTable, token))
                return Array.Empty<CargaViajeLookupOptionDto>();

            var rows = await cn.QueryAsync<CargaViajeLookupOptionDto>(new CommandDefinition($"""
                SELECT
                    LTRIM(RTRIM(v.ClienteVinculado)) AS Codigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS Titulo
                FROM dbo.{TarifaVinculosTable} v
                OUTER APPLY (
                    SELECT TOP (1) c.RAZON_SOCIAL
                    FROM dbo.Vt_Clientes c
                    WHERE UPPER(LTRIM(RTRIM(c.CODIGO))) = UPPER(LTRIM(RTRIM(v.ClienteVinculado)))
                ) cli
                WHERE UPPER(LTRIM(RTRIM(v.ClientePrincipal))) = @Principal
                ORDER BY cli.RAZON_SOCIAL, v.ClienteVinculado;
                """, new { Principal = principal }, cancellationToken: token));
            return rows.ToList();
        }, "No se pudieron cargar los clientes que comparten la tarifa.", ct);

    public Task<CargaViajeLookupOptionDto?> GetClientePrincipalTarifaAsync(string clienteVinculado, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetClientePrincipalTarifa", async token =>
        {
            var vinculado = (clienteVinculado ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(vinculado))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, TarifaVinculosTable, token))
                return null;

            return await cn.QueryFirstOrDefaultAsync<CargaViajeLookupOptionDto>(new CommandDefinition($"""
                SELECT TOP (1)
                    LTRIM(RTRIM(v.ClientePrincipal)) AS Codigo,
                    ISNULL(cli.RAZON_SOCIAL, '') AS Titulo
                FROM dbo.{TarifaVinculosTable} v
                OUTER APPLY (
                    SELECT TOP (1) c.RAZON_SOCIAL
                    FROM dbo.Vt_Clientes c
                    WHERE UPPER(LTRIM(RTRIM(c.CODIGO))) = UPPER(LTRIM(RTRIM(v.ClientePrincipal)))
                ) cli
                WHERE UPPER(LTRIM(RTRIM(v.ClienteVinculado))) = @Vinculado;
                """, new { Vinculado = vinculado }, cancellationToken: token));
        }, "No se pudo verificar si el cliente comparte tarifas con otro.", ct);

    /// <summary>
    /// Reemplaza los clientes vinculados de <paramref name="clientePrincipal"/> y copia todas sus tarifas de cliente
    /// a cada vinculado (alta si no existe la combinación destino + tipo de vehículo, actualización si existe).
    /// Al quitar un vínculo, las tarifas ya copiadas quedan en el cliente como tarifas propias.
    /// </summary>
    public Task<int> GuardarClientesVinculadosTarifaAsync(string clientePrincipal, IReadOnlyCollection<string> clientesVinculados, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GuardarClientesVinculadosTarifa", async token =>
        {
            var principal = (clientePrincipal ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(principal))
                throw new InvalidOperationException("Seleccioná el cliente de la tarifa antes de vincular otros clientes.");

            var vinculados = (clientesVinculados ?? Array.Empty<string>())
                .Select(codigo => (codigo ?? string.Empty).Trim())
                .Where(codigo => !string.IsNullOrWhiteSpace(codigo))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (vinculados.Any(codigo => string.Equals(codigo, principal, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("El cliente no puede compartir tarifas consigo mismo.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await TableExistsAsync(cn, "TA_TARIFA", token))
                throw new InvalidOperationException("La tabla TA_TARIFA no existe en la base activa.");

            await EnsureTarifasVinculadasSchemaAsync(cn, token);

            var principalUpper = principal.ToUpperInvariant();
            var vinculadosUpper = vinculados.Select(codigo => codigo.ToUpperInvariant()).ToList();

            var principalEsVinculado = await cn.ExecuteScalarAsync<string?>(new CommandDefinition($"""
                SELECT TOP (1) LTRIM(RTRIM(ClientePrincipal))
                FROM dbo.{TarifaVinculosTable}
                WHERE UPPER(LTRIM(RTRIM(ClienteVinculado))) = @Principal;
                """, new { Principal = principalUpper }, cancellationToken: token));
            if (!string.IsNullOrWhiteSpace(principalEsVinculado) && vinculados.Count > 0)
                throw new InvalidOperationException($"El cliente {principal} ya usa las tarifas del cliente {principalEsVinculado}. Vinculá los clientes directamente a {principalEsVinculado}.");

            if (vinculadosUpper.Count > 0)
            {
                var inexistentes = (await cn.QueryAsync<string>(new CommandDefinition("""
                    SELECT v.Codigo
                    FROM (SELECT value AS Codigo FROM STRING_SPLIT(@Codigos, '|')) v
                    WHERE NOT EXISTS (
                        SELECT 1 FROM dbo.Vt_Clientes c
                        WHERE UPPER(LTRIM(RTRIM(c.CODIGO))) = v.Codigo
                    );
                    """, new { Codigos = string.Join('|', vinculadosUpper) }, cancellationToken: token))).ToList();
                if (inexistentes.Count > 0)
                    throw new InvalidOperationException($"No existen los clientes: {string.Join(", ", inexistentes)}.");

                var enOtroPrincipal = (await cn.QueryAsync<string>(new CommandDefinition($"""
                    SELECT LTRIM(RTRIM(ClienteVinculado)) + ' (comparte con ' + LTRIM(RTRIM(ClientePrincipal)) + ')'
                    FROM dbo.{TarifaVinculosTable}
                    WHERE UPPER(LTRIM(RTRIM(ClienteVinculado))) IN @Vinculados
                      AND UPPER(LTRIM(RTRIM(ClientePrincipal))) <> @Principal;
                    """, new { Vinculados = vinculadosUpper, Principal = principalUpper }, cancellationToken: token))).ToList();
                if (enOtroPrincipal.Count > 0)
                    throw new InvalidOperationException($"Estos clientes ya comparten tarifas con otro cliente: {string.Join(", ", enOtroPrincipal)}.");

                var sonPrincipales = (await cn.QueryAsync<string>(new CommandDefinition($"""
                    SELECT DISTINCT LTRIM(RTRIM(ClientePrincipal))
                    FROM dbo.{TarifaVinculosTable}
                    WHERE UPPER(LTRIM(RTRIM(ClientePrincipal))) IN @Vinculados;
                    """, new { Vinculados = vinculadosUpper }, cancellationToken: token))).ToList();
                if (sonPrincipales.Count > 0)
                    throw new InvalidOperationException($"Estos clientes ya tienen sus propios clientes vinculados y no pueden usar las tarifas de otro: {string.Join(", ", sonPrincipales)}.");
            }

            var usuarioActual = NormalizeUser(Environment.UserName);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                await cn.ExecuteAsync(new CommandDefinition($"""
                    DELETE FROM dbo.{TarifaVinculosTable}
                    WHERE UPPER(LTRIM(RTRIM(ClientePrincipal))) = @Principal
                      AND UPPER(LTRIM(RTRIM(ClienteVinculado))) NOT IN @Vinculados;
                    """, new { Principal = principalUpper, Vinculados = vinculadosUpper.Count > 0 ? vinculadosUpper : [string.Empty] }, transaction: tx, cancellationToken: token));

                foreach (var vinculado in vinculados)
                {
                    await cn.ExecuteAsync(new CommandDefinition($"""
                        IF NOT EXISTS (
                            SELECT 1 FROM dbo.{TarifaVinculosTable}
                            WHERE UPPER(LTRIM(RTRIM(ClienteVinculado))) = UPPER(@Vinculado)
                        )
                            INSERT INTO dbo.{TarifaVinculosTable} (ClientePrincipal, ClienteVinculado, FECHAHORA_ALTA, USUARIO)
                            VALUES (@Principal, @Vinculado, GETDATE(), @Usuario);
                        """, new { Principal = principal, Vinculado = vinculado, Usuario = usuarioActual }, transaction: tx, cancellationToken: token));
                }

                var propagadas = vinculados.Count == 0
                    ? 0
                    : await PropagarTarifasClientesVinculadosAsync(cn, tx, tarifaIds: null, clientePrincipal: principal, token);

                await tx.CommitAsync(token);
                return propagadas;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudieron guardar los clientes que comparten la tarifa.", ct);

    /// <summary>
    /// Copia a los clientes vinculados las tarifas indicadas (por ID) o todas las tarifas del cliente principal.
    /// Devuelve la cantidad de filas insertadas + actualizadas. Si la estructura no está disponible no hace nada.
    /// </summary>
    private async Task<int> PropagarTarifasClientesVinculadosAsync(
        SqlConnection cn,
        SqlTransaction? tx,
        IReadOnlyCollection<int>? tarifaIds,
        string? clientePrincipal,
        CancellationToken ct)
    {
        if (tarifaIds is { Count: 0 })
            return 0;

        var hasVinculos = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT CASE WHEN OBJECT_ID(N'dbo.{TarifaVinculosTable}', 'U') IS NULL THEN 0 ELSE 1 END;",
            transaction: tx,
            cancellationToken: ct));
        if (hasVinculos == 0)
            return 0;

        var columns = (await cn.QueryAsync<string>(new CommandDefinition("""
            SELECT LOWER(COLUMN_NAME)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'TA_TARIFA';
            """, transaction: tx, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var faltantes = TarifaPropagacionColumnas.Where(column => !columns.Contains(column)).ToList();
        if (faltantes.Count > 0)
        {
            logger.LogWarning("No se propagan tarifas a clientes vinculados: faltan columnas en TA_TARIFA ({Columns}).", string.Join(", ", faltantes));
            return 0;
        }

        var filtroOrigen = tarifaIds is not null
            ? "s.ID IN @Ids"
            : "UPPER(LTRIM(RTRIM(s.IDCLIENTE))) = @Principal";

        // La fila del vinculado se identifica por IdTarifaOrigen; si todavía no fue vinculada (datos previos),
        // por la misma combinación chofer + destino + tipo de vehículo.
        const string matchDestino = """
            UPPER(LTRIM(RTRIM(d.IDCLIENTE))) = x.ClienteVinculadoUpper
            AND ISNULL(d.TarifaFletero, 0) = 0
            AND (
                d.IdTarifaOrigen = s.ID
                OR (
                    d.IdTarifaOrigen IS NULL
                    AND UPPER(LTRIM(RTRIM(ISNULL(d.IDDESTINO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(s.IDDESTINO, ''))))
                    AND UPPER(LTRIM(RTRIM(ISNULL(d.IDTIPOVEHICULO, '')))) = UPPER(LTRIM(RTRIM(ISNULL(s.IDTIPOVEHICULO, ''))))
                    AND UPPER(LTRIM(RTRIM(ISNULL(d.IDCHOFER, '')))) = UPPER(LTRIM(RTRIM(ISNULL(s.IDCHOFER, ''))))
                )
            )
            """;

        var sql = $"""
            SET NOCOUNT ON;

            IF OBJECT_ID('tempdb..#TarifaVinculosOrigen') IS NOT NULL
                DROP TABLE #TarifaVinculosOrigen;

            SELECT
                s.ID AS IdOrigen,
                LTRIM(RTRIM(v.ClienteVinculado)) AS ClienteVinculado,
                UPPER(LTRIM(RTRIM(v.ClienteVinculado))) AS ClienteVinculadoUpper
            INTO #TarifaVinculosOrigen
            FROM dbo.TA_TARIFA s
            JOIN dbo.{TarifaVinculosTable} v
                ON UPPER(LTRIM(RTRIM(v.ClientePrincipal))) = UPPER(LTRIM(RTRIM(s.IDCLIENTE)))
            WHERE ISNULL(s.TarifaFletero, 0) = 0
              AND LTRIM(RTRIM(ISNULL(s.IDCLIENTE, ''))) <> ''
              AND {filtroOrigen};

            DECLARE @Afectadas int = 0;

            UPDATE d
            SET d.IdTarifaOrigen = s.ID,
                d.Importe = s.Importe,
                d.IDCHOFER = s.IDCHOFER,
                d.IDDESTINO = s.IDDESTINO,
                d.IDTIPOVEHICULO = s.IDTIPOVEHICULO,
                d.PorcentajeAdic = s.PorcentajeAdic,
                d.PorcentajeAdic1 = s.PorcentajeAdic1,
                d.PorcentajeAdic2 = s.PorcentajeAdic2,
                d.PorcentajeAdic3 = s.PorcentajeAdic3,
                d.PorcentajeAdic4 = s.PorcentajeAdic4,
                d.AdicionalFijo1Descripcion = s.AdicionalFijo1Descripcion,
                d.AdicionalFijo1Importe = s.AdicionalFijo1Importe,
                d.AdicionalFijo1PideCantidad = s.AdicionalFijo1PideCantidad,
                d.AdicionalFijo2Descripcion = s.AdicionalFijo2Descripcion,
                d.AdicionalFijo2Importe = s.AdicionalFijo2Importe,
                d.AdicionalFijo2PideCantidad = s.AdicionalFijo2PideCantidad,
                d.AdicionalFijo3Descripcion = s.AdicionalFijo3Descripcion,
                d.AdicionalFijo3Importe = s.AdicionalFijo3Importe,
                d.AdicionalFijo3PideCantidad = s.AdicionalFijo3PideCantidad,
                d.Activo = s.Activo,
                d.FECHAHORA_MODIFICACION = GETDATE(),
                d.USUARIOMODIFICACION = @Usuario
            FROM #TarifaVinculosOrigen x
            JOIN dbo.TA_TARIFA s ON s.ID = x.IdOrigen
            JOIN dbo.TA_TARIFA d ON {matchDestino};

            SET @Afectadas = @@ROWCOUNT;

            INSERT INTO dbo.TA_TARIFA
            (
                IdLista, Nombre, Importe, IDCLIENTE, IDCHOFER, IDDESTINO, IDTIPOVEHICULO, TarifaFletero,
                PorcentajeAdic, PorcentajeAdic1, PorcentajeAdic2, PorcentajeAdic3, PorcentajeAdic4,
                AdicionalFijo1Descripcion, AdicionalFijo1Importe, AdicionalFijo1PideCantidad,
                AdicionalFijo2Descripcion, AdicionalFijo2Importe, AdicionalFijo2PideCantidad,
                AdicionalFijo3Descripcion, AdicionalFijo3Importe, AdicionalFijo3PideCantidad,
                Activo, FECHAHORA_ALTA, USUARIOMODIFICACION, IdTarifaOrigen
            )
            SELECT
                ISNULL(lista.IdLista, RIGHT(REPLICATE('0', 4) + RIGHT(x.ClienteVinculadoUpper, 4), 4)),
                LEFT(ISNULL(NULLIF(lista.Nombre, ''), ISNULL(NULLIF(LTRIM(RTRIM(cli.RAZON_SOCIAL)), ''), x.ClienteVinculado)), 50),
                s.Importe, x.ClienteVinculado, s.IDCHOFER, s.IDDESTINO, s.IDTIPOVEHICULO, 0,
                s.PorcentajeAdic, s.PorcentajeAdic1, s.PorcentajeAdic2, s.PorcentajeAdic3, s.PorcentajeAdic4,
                s.AdicionalFijo1Descripcion, s.AdicionalFijo1Importe, s.AdicionalFijo1PideCantidad,
                s.AdicionalFijo2Descripcion, s.AdicionalFijo2Importe, s.AdicionalFijo2PideCantidad,
                s.AdicionalFijo3Descripcion, s.AdicionalFijo3Importe, s.AdicionalFijo3PideCantidad,
                s.Activo, GETDATE(), @Usuario, s.ID
            FROM #TarifaVinculosOrigen x
            JOIN dbo.TA_TARIFA s ON s.ID = x.IdOrigen
            OUTER APPLY (
                SELECT TOP (1)
                    LTRIM(RTRIM(t.IdLista)) AS IdLista,
                    LTRIM(RTRIM(ISNULL(t.Nombre, ''))) AS Nombre
                FROM dbo.TA_TARIFA t
                WHERE UPPER(LTRIM(RTRIM(t.IDCLIENTE))) = x.ClienteVinculadoUpper
                  AND ISNULL(t.TarifaFletero, 0) = 0
                  AND LTRIM(RTRIM(ISNULL(t.IdLista, ''))) <> ''
                ORDER BY t.ID
            ) lista
            OUTER APPLY (
                SELECT TOP (1) c.RAZON_SOCIAL
                FROM dbo.Vt_Clientes c
                WHERE UPPER(LTRIM(RTRIM(c.CODIGO))) = x.ClienteVinculadoUpper
            ) cli
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.TA_TARIFA d WHERE {matchDestino}
            );

            SET @Afectadas = @Afectadas + @@ROWCOUNT;

            DROP TABLE #TarifaVinculosOrigen;

            SELECT @Afectadas;
            """;

        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new
            {
                Ids = tarifaIds?.ToArray() ?? Array.Empty<int>(),
                Principal = (clientePrincipal ?? string.Empty).Trim().ToUpperInvariant(),
                Usuario = NormalizeUser(Environment.UserName)
            },
            transaction: tx,
            cancellationToken: ct));
    }

    /// <summary>
    /// Propaga a los vinculados las tarifas de cliente cuyos IDs se indican, después de que la operación principal ya grabó.
    /// Si falla, se registra en AUX_ERR y se informa con un mensaje claro.
    /// </summary>
    private async Task PropagarTarifasVinculadasDespuesDeGrabarAsync(SqlConnection cn, IReadOnlyCollection<int> tarifaIds, CancellationToken ct)
    {
        try
        {
            await PropagarTarifasClientesVinculadosAsync(cn, tx: null, tarifaIds, clientePrincipal: null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            const string message = "La tarifa se grabó, pero no se pudo copiar a los clientes que la comparten. Volvé a guardarla para reintentar.";
            var incidentId = await appEvents.LogErrorAsync(ModuleName, "PropagarTarifasVinculadas", ex, message, new { TarifaIds = tarifaIds }, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(message, incidentId, ex);
        }
    }

    private static async Task<IReadOnlyCollection<int>> LoadTarifaIdsByListaAsync(SqlConnection cn, string idColumn, string idListaColumn, string idLista, CancellationToken ct)
    {
        var lista = (idLista ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(lista))
            return Array.Empty<int>();

        var ids = await cn.QueryAsync<int>(new CommandDefinition(
            $"SELECT {idColumn} FROM dbo.TA_TARIFA WHERE UPPER(LTRIM(RTRIM({idListaColumn}))) = @IdLista;",
            new { IdLista = lista },
            cancellationToken: ct));
        return ids.ToList();
    }

    private static async Task EnsureTarifasVinculadasSchemaAsync(SqlConnection cn, CancellationToken ct, SqlTransaction? tx = null)
    {
        await cn.ExecuteAsync(new CommandDefinition($"""
            IF OBJECT_ID(N'dbo.{TarifaVinculosTable}', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.{TarifaVinculosTable}
                (
                    ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_{TarifaVinculosTable} PRIMARY KEY,
                    ClientePrincipal nvarchar(15) NOT NULL,
                    ClienteVinculado nvarchar(15) NOT NULL,
                    FECHAHORA_ALTA datetime NOT NULL CONSTRAINT DF_{TarifaVinculosTable}_FECHAHORA_ALTA DEFAULT (GETDATE()),
                    USUARIO nvarchar(50) NULL,
                    CONSTRAINT UQ_{TarifaVinculosTable}_ClienteVinculado UNIQUE (ClienteVinculado)
                );
            END;

            IF OBJECT_ID(N'dbo.TA_TARIFA', 'U') IS NOT NULL AND COL_LENGTH(N'dbo.TA_TARIFA', N'IdTarifaOrigen') IS NULL
                ALTER TABLE dbo.TA_TARIFA ADD IdTarifaOrigen int NULL;
            """, transaction: tx, cancellationToken: ct));
    }
}

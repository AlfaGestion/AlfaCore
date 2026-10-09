using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Text;

namespace AlfaCore.Services;

public sealed class ConsultasService(IConfiguration configuration, ILogger<ConsultasService> logger, ISessionService sessionService) : IConsultasService
{
    private const int MaxFilasBrowser = 500;
    private const int TimeoutCargaSegundos = 15;
    private const int TimeoutEjecucionSegundos = 60;

    private readonly ISessionService _sessionService = sessionService;
    private string _connectionString => _sessionService.GetConnectionString().Length > 0
        ? _sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlyList<GrupoConsultasDto>> GetGruposAsync(CancellationToken ct = default)
    {
        // Columnas reales: ID, CLAVE, GRUPO, DESCRIPCION, Marca
        // V_TA_SCRIPT_CFG: IdScript, EsParametro
        const string sql = """
            SELECT s.ID,
                   LTRIM(RTRIM(ISNULL(s.CLAVE, ''))),
                   LTRIM(RTRIM(ISNULL(s.GRUPO, 'Sin grupo'))),
                   LTRIM(RTRIM(ISNULL(s.DESCRIPCION, ''))),
                   CASE WHEN EXISTS (
                       SELECT 1 FROM V_TA_SCRIPT_CFG c
                       WHERE c.IdScript = s.ID AND c.EsParametro = 1
                   ) THEN 1 ELSE 0 END AS TieneParametros
            FROM V_TA_SCRIPT s
            WHERE s.Marca = 'CL'
            ORDER BY s.GRUPO, s.CLAVE
            """;

        var lista = new List<ConsultaResumenDto>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = TimeoutCargaSegundos };
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        while (await rd.ReadAsync(ct))
        {
            lista.Add(new ConsultaResumenDto
            {
                Id = rd.GetInt32(0),
                Clave = rd.GetString(1),
                Grupo = rd.GetString(2),
                Descripcion = rd.GetString(3),
                TieneParametros = rd.GetInt32(4) == 1
            });
        }

        return lista
            .GroupBy(c => c.Grupo)
            .Select(g => new GrupoConsultasDto { Nombre = g.Key, Consultas = g.ToList() })
            .ToList();
    }

    public async Task<IReadOnlyList<NodoArbolDto>> GetArbolAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT s.ID,
                   LTRIM(RTRIM(ISNULL(s.CLAVE, ''))),
                   LTRIM(RTRIM(ISNULL(s.GRUPO, ''))),
                   LTRIM(RTRIM(ISNULL(s.DESCRIPCION, ''))),
                   CASE WHEN s.SQL IS NOT NULL
                        AND LTRIM(RTRIM(CAST(s.SQL AS nvarchar(100)))) <> ''
                        THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM V_TA_SCRIPT_CFG c
                       WHERE c.IdScript = s.ID AND c.EsParametro = 1
                   ) THEN 1 ELSE 0 END
            FROM V_TA_SCRIPT s
            WHERE s.Marca = 'CL' AND LTRIM(RTRIM(ISNULL(s.CLAVE, ''))) <> ''
            ORDER BY s.CLAVE
            """;

        var dict = new Dictionary<string, NodoArbolDto>(StringComparer.OrdinalIgnoreCase);
        var raices = new List<NodoArbolDto>();

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = TimeoutCargaSegundos };
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        while (await rd.ReadAsync(ct))
        {
            var clave = rd.GetString(1);
            if (string.IsNullOrWhiteSpace(clave)) continue;

            var nodo = new NodoArbolDto
            {
                Id = rd.GetInt32(0),
                Clave = clave,
                Nombre = rd.GetString(2),
                Descripcion = rd.GetString(3),
                TieneSql = rd.GetInt32(4) == 1,
                TieneParametros = rd.GetInt32(5) == 1
            };
            dict[clave] = nodo;

            if (clave.Length > 2)
            {
                var parentClave = clave[..^2];
                if (dict.TryGetValue(parentClave, out var parent))
                    parent.Hijos.Add(nodo);
                else
                    raices.Add(nodo); // huérfano → nodo raíz
            }
            else
            {
                raices.Add(nodo);
            }
        }

        return raices;
    }

    // V_TA_SCRIPT es compartida con Desktop y las bases viejas no tienen CamposOrdenar (se sumó
    // después): sin este chequeo, abrir o guardar una consulta rompía la pantalla con
    // "El nombre de columna 'CamposOrdenar' no es válido". Si falta, se lee vacío y no se graba.
    private static async Task<bool> TieneCamposOrdenarAsync(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('V_TA_SCRIPT', 'CamposOrdenar') IS NULL THEN 0 ELSE 1 END", cn)
        {
            CommandTimeout = TimeoutCargaSegundos
        };
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<ConsultaGuardadaDto?> GetConsultaAsync(int id, CancellationToken ct = default)
    {
        const string sqlConsultaBase = """
            SELECT s.ID,
                   LTRIM(RTRIM(ISNULL(s.CLAVE, ''))),
                   LTRIM(RTRIM(ISNULL(s.GRUPO, ''))),
                   LTRIM(RTRIM(ISNULL(s.DESCRIPCION, ''))),
                   ISNULL(CAST(s.SQL AS nvarchar(max)), ''),
                   LTRIM(RTRIM(ISNULL(s.TABLA, ''))),
                   LTRIM(RTRIM(ISNULL(s.CamposGrupo, ''))),
                   LTRIM(RTRIM(ISNULL(s.CamposTotaliza, ''))),
                   {0}
            FROM V_TA_SCRIPT s
            WHERE s.ID = @Id AND s.Marca = 'CL'
            """;

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        var sqlConsulta = string.Format(sqlConsultaBase, await TieneCamposOrdenarAsync(cn, ct)
            ? "LTRIM(RTRIM(ISNULL(s.CamposOrdenar, '')))"
            : "CAST('' AS nvarchar(255))");

        int idVal; string clave, grupo, descripcion, sqlTexto, tabla, camposGrupo, camposTotaliza, camposOrdenar;

        await using (var cmd = new SqlCommand(sqlConsulta, cn) { CommandTimeout = TimeoutCargaSegundos })
        {
            cmd.Parameters.AddWithValue("@Id", id);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct)) return null;

            idVal = rd.GetInt32(0);
            clave = rd.GetString(1);
            grupo = rd.GetString(2);
            descripcion = rd.GetString(3);
            sqlTexto = rd.GetString(4);
            tabla = rd.GetString(5);
            camposGrupo = rd.GetString(6);
            camposTotaliza = rd.GetString(7);
            camposOrdenar = rd.GetString(8);
        }

        // Los parámetros son campos con EsParametro = 1.
        // CampoSel es la etiqueta del parámetro para el usuario.
        const string sqlParams = """
            SELECT cfg.ID,
                   LTRIM(RTRIM(ISNULL(cfg.CampoSel, '')))
            FROM V_TA_SCRIPT_CFG cfg
            WHERE cfg.IdScript = @Id AND cfg.EsParametro = 1
            ORDER BY cfg.ID
            """;

        var parametros = new List<ParametroConsultaDto>();
        await using (var cmd2 = new SqlCommand(sqlParams, cn) { CommandTimeout = TimeoutCargaSegundos })
        {
            cmd2.Parameters.AddWithValue("@Id", id);
            await using var rd2 = await cmd2.ExecuteReaderAsync(ct);
            int orden = 0;
            while (await rd2.ReadAsync(ct))
            {
                var campo = rd2.GetString(1);
                parametros.Add(new ParametroConsultaDto
                {
                    Orden = orden++,
                    Campo = campo,
                    Tipo = DetectarTipo(campo)
                });
            }
        }

        return new ConsultaGuardadaDto
        {
            Id = idVal,
            Clave = clave,
            Grupo = grupo,
            Descripcion = descripcion,
            Sql = sqlTexto,
            Tabla = tabla,
            CamposGrupo = camposGrupo,
            CamposTotaliza = camposTotaliza,
            CamposOrdenar = camposOrdenar,
            Parametros = parametros
        };
    }

    public async Task<ConsultaResultadoDto> EjecutarAsync(EjecutarConsultaRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var consulta = await GetConsultaAsync(request.ConsultaId, ct);
        if (consulta is null)
            return Failure("La consulta no existe o no está disponible.", sw);

        if (string.IsNullOrWhiteSpace(consulta.Sql))
            return Failure("La consulta no tiene SQL definido.", sw);

        if (!ConsultasSqlValidator.TryValidate(consulta.Sql, out var validMsg))
        {
            logger.LogWarning("Consulta {Id} rechazada por validador: {Motivo}", request.ConsultaId, validMsg);
            return Failure(validMsg, sw);
        }

        string sqlFinal;
        try
        {
            sqlFinal = SustituirParametros(consulta.Sql, request.ValoresParametros);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error sustituyendo parámetros en consulta {Id}", request.ConsultaId);
            return Failure("Error al procesar los parámetros de la consulta.", sw);
        }

        var maxFilas = request.MaxFilas > 0 ? request.MaxFilas : MaxFilasBrowser;

        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sqlFinal, cn) { CommandTimeout = TimeoutEjecucionSegundos };
            await using var rd = await cmd.ExecuteReaderAsync(ct);

            var columnas = new List<string>();
            for (int i = 0; i < rd.FieldCount; i++)
                columnas.Add(rd.GetName(i));

            var filas = new List<string[]>();
            int totalLeidas = 0;
            bool tieneMas = false;

            while (await rd.ReadAsync(ct))
            {
                totalLeidas++;
                if (totalLeidas > maxFilas)
                {
                    tieneMas = true;
                    while (await rd.ReadAsync(ct)) totalLeidas++;
                    break;
                }
                var valores = new string[rd.FieldCount];
                for (int i = 0; i < rd.FieldCount; i++)
                    valores[i] = rd.IsDBNull(i) ? string.Empty : Convert.ToString(rd.GetValue(i)) ?? string.Empty;
                filas.Add(valores);
            }

            sw.Stop();
            return new ConsultaResultadoDto
            {
                Exitoso = true,
                Columnas = columnas,
                Filas = filas,
                TotalFilas = totalLeidas,
                TieneMasFilas = tieneMas,
                TiempoEjecucion = sw.Elapsed,
                EjecutadoEn = DateTime.Now
            };
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            logger.LogWarning("Consulta {Id} superó el timeout de {Seg}s", request.ConsultaId, TimeoutEjecucionSegundos);
            return Failure($"La consulta tardó más de {TimeoutEjecucionSegundos} segundos. Ajustá los filtros para reducir el período o el volumen de datos.", sw);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error ejecutando consulta {Id}: {Sql}", request.ConsultaId, sqlFinal);
            return Failure("Ocurrió un error al ejecutar la consulta. Verificá los parámetros e intentá nuevamente.", sw);
        }
    }

    private static string SustituirParametros(string sql, List<string> valores)
    {
        if (valores.Count == 0) return sql;

        var sb = new StringBuilder(sql);
        foreach (var valor in valores)
        {
            var texto = sb.ToString();
            var idx = texto.IndexOf("<P>", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;
            sb.Remove(idx, 3).Insert(idx, EscaparValor(valor));
        }
        return sb.ToString();
    }

    private static string EscaparValor(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "NULL";
        if (decimal.TryParse(valor, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out _))
            return valor;
        if (DateTime.TryParse(valor, out var fecha))
            return $"'{fecha:yyyy-MM-dd}'";
        return $"'{valor.Replace("'", "''")}'";
    }

    private static TipoParametro DetectarTipo(string campo)
    {
        var c = campo.ToLowerInvariant();
        if (c.Contains("fecha") || c.Contains("date") || c.Contains("desde") ||
            c.Contains("hasta") || c.StartsWith("fh_") || c == "fh")
            return TipoParametro.Fecha;
        if (c.Contains("importe") || c.Contains("monto") || c.Contains("precio") ||
            c.Contains("cant") || c.Contains("nro") || c.Contains("numero") ||
            c == "id" || c.EndsWith("_id"))
            return TipoParametro.Numero;
        return TipoParametro.Texto;
    }

    public async Task<IReadOnlyList<string>> GetGruposNombresAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT LTRIM(RTRIM(ISNULL(GRUPO, '')))
            FROM V_TA_SCRIPT
            WHERE Marca = 'CL' AND GRUPO IS NOT NULL AND LTRIM(RTRIM(GRUPO)) <> ''
            ORDER BY 1
            """;
        var lista = new List<string>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = TimeoutCargaSegundos };
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            lista.Add(rd.GetString(0));
        return lista;
    }

    public async Task<IReadOnlyList<string>> GetVistasDisponiblesAsync(string? filtro = null, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 60 TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE IN ('VIEW', 'BASE TABLE')
              AND (@Filtro IS NULL OR TABLE_NAME LIKE '%' + @Filtro + '%')
            ORDER BY TABLE_TYPE DESC, TABLE_NAME
            """;
        var lista = new List<string>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = TimeoutCargaSegundos };
        cmd.Parameters.AddWithValue("@Filtro", string.IsNullOrWhiteSpace(filtro) ? (object)DBNull.Value : filtro.Trim());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            lista.Add(rd.GetString(0));
        return lista;
    }

    public async Task<IReadOnlyList<ColumnaDto>> GetColumnasAsync(string tabla, CancellationToken ct = default)
    {
        const string sql = """
            SELECT COLUMN_NAME, DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @Tabla
            ORDER BY ORDINAL_POSITION
            """;
        var lista = new List<ColumnaDto>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = TimeoutCargaSegundos };
        cmd.Parameters.AddWithValue("@Tabla", tabla);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            lista.Add(new ColumnaDto { Nombre = rd.GetString(0), Tipo = rd.GetString(1) });
        return lista;
    }

    public async Task<int> GuardarConsultaAsync(GuardarConsultaRequest request, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);

        var tieneCamposOrdenar = await TieneCamposOrdenarAsync(cn, ct);

        int id;
        if (request.Id.HasValue)
        {
            var sqlUpdate = $"""
                UPDATE V_TA_SCRIPT
                SET CLAVE = @Clave, GRUPO = @Grupo, DESCRIPCION = @Descripcion, SQL = @Sql,
                    TABLA = @Tabla, CamposGrupo = @CamposGrupo, CamposTotaliza = @CamposTotaliza{(tieneCamposOrdenar ? ", CamposOrdenar = @CamposOrdenar" : string.Empty)}
                WHERE ID = @Id AND Marca = 'CL'
                """;
            await using var cmdU = new SqlCommand(sqlUpdate, cn) { CommandTimeout = TimeoutCargaSegundos };
            cmdU.Parameters.AddWithValue("@Id", request.Id.Value);
            cmdU.Parameters.AddWithValue("@Clave", request.Clave);
            cmdU.Parameters.AddWithValue("@Grupo", request.Grupo);
            cmdU.Parameters.AddWithValue("@Descripcion", request.Descripcion);
            cmdU.Parameters.AddWithValue("@Sql", request.Sql);
            cmdU.Parameters.AddWithValue("@Tabla", (object?)request.Tabla ?? DBNull.Value);
            cmdU.Parameters.AddWithValue("@CamposGrupo", (object?)request.CamposGrupo ?? DBNull.Value);
            cmdU.Parameters.AddWithValue("@CamposTotaliza", (object?)request.CamposTotaliza ?? DBNull.Value);
            cmdU.Parameters.AddWithValue("@CamposOrdenar", (object?)request.CamposOrdenar ?? DBNull.Value);
            await cmdU.ExecuteNonQueryAsync(ct);
            id = request.Id.Value;
        }
        else
        {
            var sqlInsert = $"""
                INSERT INTO V_TA_SCRIPT (CLAVE, GRUPO, DESCRIPCION, SQL, Marca, TABLA, CamposGrupo, CamposTotaliza{(tieneCamposOrdenar ? ", CamposOrdenar" : string.Empty)})
                VALUES (@Clave, @Grupo, @Descripcion, @Sql, 'CL', @Tabla, @CamposGrupo, @CamposTotaliza{(tieneCamposOrdenar ? ", @CamposOrdenar" : string.Empty)})
                SELECT CAST(SCOPE_IDENTITY() AS int)
                """;
            await using var cmdI = new SqlCommand(sqlInsert, cn) { CommandTimeout = TimeoutCargaSegundos };
            cmdI.Parameters.AddWithValue("@Clave", request.Clave);
            cmdI.Parameters.AddWithValue("@Grupo", request.Grupo);
            cmdI.Parameters.AddWithValue("@Descripcion", request.Descripcion);
            cmdI.Parameters.AddWithValue("@Sql", request.Sql);
            cmdI.Parameters.AddWithValue("@Tabla", (object?)request.Tabla ?? DBNull.Value);
            cmdI.Parameters.AddWithValue("@CamposGrupo", (object?)request.CamposGrupo ?? DBNull.Value);
            cmdI.Parameters.AddWithValue("@CamposTotaliza", (object?)request.CamposTotaliza ?? DBNull.Value);
            cmdI.Parameters.AddWithValue("@CamposOrdenar", (object?)request.CamposOrdenar ?? DBNull.Value);
            var result = await cmdI.ExecuteScalarAsync(ct);
            id = Convert.ToInt32(result);
        }

        const string sqlDelParams = "DELETE FROM V_TA_SCRIPT_CFG WHERE IdScript = @Id AND EsParametro = 1";
        await using var cmdDel = new SqlCommand(sqlDelParams, cn) { CommandTimeout = TimeoutCargaSegundos };
        cmdDel.Parameters.AddWithValue("@Id", id);
        await cmdDel.ExecuteNonQueryAsync(ct);

        foreach (var etiqueta in request.EtiquetasParametros)
        {
            const string sqlParam = """
                INSERT INTO V_TA_SCRIPT_CFG (IdScript, CampoSel, EsParametro)
                VALUES (@IdScript, @CampoSel, 1)
                """;
            await using var cmdP = new SqlCommand(sqlParam, cn) { CommandTimeout = TimeoutCargaSegundos };
            cmdP.Parameters.AddWithValue("@IdScript", id);
            cmdP.Parameters.AddWithValue("@CampoSel", etiqueta);
            await cmdP.ExecuteNonQueryAsync(ct);
        }

        return id;
    }

    public async Task<string> GetSiguienteClaveAsync(string? parentClave, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);

        if (string.IsNullOrWhiteSpace(parentClave))
        {
            // Nivel raíz: CLAVEs de 2 dígitos
            const string sql = """
                SELECT MAX(CAST(CLAVE AS int))
                FROM V_TA_SCRIPT
                WHERE Marca = 'CL' AND LEN(LTRIM(RTRIM(CLAVE))) = 2 AND ISNUMERIC(CLAVE) = 1
                """;
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = TimeoutCargaSegundos };
            var result = await cmd.ExecuteScalarAsync(ct);
            if (result is DBNull || result is null) return "10";
            return (Convert.ToInt32(result) + 10).ToString();
        }
        else
        {
            // Hijos del parentClave: CLAVEs con longitud = parentClave.Length + 2 que empiezan con parentClave
            int longitudHija = parentClave.Length + 2;
            const string sql = """
                SELECT MAX(CAST(CLAVE AS int))
                FROM V_TA_SCRIPT
                WHERE Marca = 'CL'
                  AND LEN(LTRIM(RTRIM(CLAVE))) = @Len
                  AND CLAVE LIKE @Prefix + '%'
                  AND ISNUMERIC(CLAVE) = 1
                """;
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = TimeoutCargaSegundos };
            cmd.Parameters.AddWithValue("@Len", longitudHija);
            cmd.Parameters.AddWithValue("@Prefix", parentClave);
            var result = await cmd.ExecuteScalarAsync(ct);
            if (result is DBNull || result is null) return parentClave + "01";
            return (Convert.ToInt32(result) + 1).ToString();
        }
    }

    public async Task EliminarConsultaAsync(int id, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);

        await using var cmd1 = new SqlCommand("DELETE FROM V_TA_SCRIPT_CFG WHERE IdScript = @Id", cn) { CommandTimeout = TimeoutCargaSegundos };
        cmd1.Parameters.AddWithValue("@Id", id);
        await cmd1.ExecuteNonQueryAsync(ct);

        await using var cmd2 = new SqlCommand("DELETE FROM V_TA_SCRIPT WHERE ID = @Id AND Marca = 'CL'", cn) { CommandTimeout = TimeoutCargaSegundos };
        cmd2.Parameters.AddWithValue("@Id", id);
        await cmd2.ExecuteNonQueryAsync(ct);
    }

    private static ConsultaResultadoDto Failure(string mensaje, Stopwatch sw)
    {
        sw.Stop();
        return new ConsultaResultadoDto
        {
            Exitoso = false,
            MensajeError = mensaje,
            TiempoEjecucion = sw.Elapsed,
            EjecutadoEn = DateTime.Now
        };
    }
}

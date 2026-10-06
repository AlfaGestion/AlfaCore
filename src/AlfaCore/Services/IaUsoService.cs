using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Cola en memoria de consumos de IA (singleton). Registrar nunca bloquea ni falla: si la cola se
/// llena (central caída mucho tiempo), se descartan los más viejos.
/// </summary>
public sealed class IaUsoCola
{
    private readonly Channel<IaUsoRegistro> _canal = Channel.CreateBounded<IaUsoRegistro>(
        new BoundedChannelOptions(20_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Encolar(IaUsoRegistro registro) => _canal.Writer.TryWrite(registro);

    internal ChannelReader<IaUsoRegistro> Lector => _canal.Reader;
}

public interface IIaUsoRecorder
{
    /// <summary>Registra el consumo de una respuesta de OpenAI para la base activa. Nunca lanza excepción.</summary>
    void Registrar(string funcion, JsonElement respuesta, string? modeloPedido, string? referencia = null);

    /// <summary>Registra búsquedas en archivos (file_search) o almacenamiento de un vector store.</summary>
    void RegistrarArchivos(string funcion, int busquedas, long bytesAlmacenados, string? referencia = null);
}

/// <summary>
/// Registrador por solicitud: toma la base de la sesión activa (incluida la base resuelta por un
/// webhook) en el momento de la llamada y deja el consumo en la cola.
/// </summary>
public sealed class IaUsoRecorder(ISessionService sessionService, IaUsoCola cola) : IIaUsoRecorder
{
    public void Registrar(string funcion, JsonElement respuesta, string? modeloPedido, string? referencia = null)
    {
        try
        {
            var tokens = IaUsoTokens.Desde(respuesta);
            if (tokens.Vacio)
                return;

            var (idBase, baseDatos) = BaseActual();
            cola.Encolar(new IaUsoRegistro(
                DateTime.UtcNow, idBase, baseDatos, funcion, IaUsoTokens.Modelo(respuesta, modeloPedido),
                tokens.Entrada, tokens.EntradaCacheada, tokens.Salida, 0, 0, Recortar(referencia)));
        }
        catch
        {
            // La medición nunca interrumpe la función que consumió IA.
        }
    }

    public void RegistrarArchivos(string funcion, int busquedas, long bytesAlmacenados, string? referencia = null)
    {
        try
        {
            if (busquedas <= 0 && bytesAlmacenados <= 0)
                return;

            var (idBase, baseDatos) = BaseActual();
            var modelo = funcion == IaUsoFunciones.ArchivosAlmacenamiento ? "vector_store" : "file_search";
            cola.Encolar(new IaUsoRegistro(
                DateTime.UtcNow, idBase, baseDatos, funcion, modelo, 0, 0, 0,
                Math.Max(0, busquedas), Math.Max(0, bytesAlmacenados), Recortar(referencia)));
        }
        catch
        {
        }
    }

    private (int? IdBase, string BaseDatos) BaseActual()
    {
        try
        {
            var sesion = sessionService.GetActiveSession();
            return (sesion?.BaseId is > 0 ? sesion.BaseId : null, (sesion?.BaseDatos ?? string.Empty).Trim());
        }
        catch
        {
            return (null, string.Empty);
        }
    }

    private static string Recortar(string? valor)
    {
        var texto = (valor ?? string.Empty).Trim();
        return texto.Length <= 120 ? texto : texto[..120];
    }
}

/// <summary>
/// Graba la cola en ALFA_CENTRAL.dbo.IA_USO cada pocos segundos, por lotes. El costo en USD se calcula
/// al insertar con el precio vigente del modelo (IA_PRECIO_MODELO, coincidencia por prefijo más larga).
/// Si la central no está configurada, la tabla no existe o falla la grabación, el lote se guarda en
/// App_Data/ia-uso para no perderlo y se registra el error en el log de la app.
/// </summary>
public sealed class IaUsoFlushService(
    IaUsoCola cola,
    IConfiguration configuration,
    IWebHostEnvironment env,
    ILogger<IaUsoFlushService> logger) : BackgroundService
{
    internal const int MaxFilasPorInsert = 150;
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReintentoEsquema = TimeSpan.FromMinutes(10);
    private DateTime _ultimoAvisoUtc = DateTime.MinValue;
    private DateTime _ultimoIntentoEsquemaUtc = DateTime.MinValue;
    private bool _esquemaListo;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await FlushAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }

        // Al cerrar se intenta grabar lo que quedó.
        await FlushAsync(CancellationToken.None);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        var lote = new List<IaUsoRegistro>();
        while (lote.Count < 2000 && cola.Lector.TryRead(out var registro))
            lote.Add(registro);

        var connectionString = configuration.GetConnectionString("AlfaCentral");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Instalación sin central (no SaaS): no hay medición central; se conserva localmente.
            if (lote.Count > 0)
                GuardarPendientes(lote);
            return;
        }

        if (lote.Count == 0 && _esquemaListo && !HayPendientesLocales())
            return;

        try
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct);
            if (!await AsegurarEsquemaAsync(cn, ct))
                throw new InvalidOperationException("Faltan las tablas de medición de IA en ALFA_CENTRAL (IA_USO) y no se pudieron crear.");

            await InsertarAsync(cn, null, lote, ct);
            await ReimportarPendientesAsync(cn, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (lote.Count > 0)
                GuardarPendientes(lote);
            if (DateTime.UtcNow - _ultimoAvisoUtc > TimeSpan.FromHours(1))
            {
                _ultimoAvisoUtc = DateTime.UtcNow;
                logger.LogWarning(ex, "No se pudo grabar el consumo de IA en ALFA_CENTRAL; {Count} registros quedaron en App_Data/ia-uso.", lote.Count);
            }
        }
    }

    /// <summary>
    /// Aplica <see cref="CentralIaSchema"/> si faltan las tablas (idempotente). Si el usuario central no
    /// tiene permisos, se reintenta cada 10 minutos y mientras tanto el consumo queda en el archivo local.
    /// </summary>
    private async Task<bool> AsegurarEsquemaAsync(SqlConnection cn, CancellationToken ct)
    {
        if (_esquemaListo)
            return true;

        await using (var check = new SqlCommand(CentralIaSchema.VerificacionSql, cn))
        {
            if (Convert.ToInt32(await check.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) == 1)
                return _esquemaListo = true;
        }

        if (DateTime.UtcNow - _ultimoIntentoEsquemaUtc < ReintentoEsquema)
            return false;

        _ultimoIntentoEsquemaUtc = DateTime.UtcNow;
        try
        {
            foreach (var lote in CentralIaSchema.Lotes())
            {
                await using var cmd = new SqlCommand(lote, cn) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync(ct);
            }

            logger.LogInformation("Se aplicó en ALFA_CENTRAL el esquema de medición y cobro de IA (IA_USO, IA_PRECIO_MODELO, IA_CONFIG, IA_TOPE_CREDITOS).");
            return _esquemaListo = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo aplicar en ALFA_CENTRAL el esquema de medición de IA; se reintenta en {Minutos} minutos.", ReintentoEsquema.TotalMinutes);
            return false;
        }
    }

    private static async Task InsertarAsync(SqlConnection cn, SqlTransaction? tx, IReadOnlyList<IaUsoRegistro> registros, CancellationToken ct)
    {
        foreach (var parte in registros.Chunk(MaxFilasPorInsert))
        {
            await using var cmd = new SqlCommand(BuildInsertSql(parte.Length), cn, tx);
            AgregarParametros(cmd, parte);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private string DirectorioPendientes => Path.Combine(env.ContentRootPath, "App_Data", "ia-uso");

    private bool HayPendientesLocales()
        => Directory.Exists(DirectorioPendientes)
           && Directory.EnumerateFiles(DirectorioPendientes, "ia-uso-pendiente-*.jsonl").Any();

    /// <summary>
    /// Sube a la central los consumos que quedaron en archivos locales (central caída o sin esquema).
    /// Cada archivo se graba en una transacción y después se renombra a ".importado" para no repetirlo.
    /// </summary>
    private async Task ReimportarPendientesAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!HayPendientesLocales())
            return;

        foreach (var archivo in Directory.EnumerateFiles(DirectorioPendientes, "ia-uso-pendiente-*.jsonl").OrderBy(f => f).ToList())
        {
            var registros = LeerPendientes(File.ReadAllLines(archivo));
            await using (var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct))
            {
                await InsertarAsync(cn, tx, registros, ct);
                await tx.CommitAsync(ct);
            }

            File.Move(archivo, Path.ChangeExtension(archivo, $".importado-{DateTime.UtcNow:yyyyMMddHHmmss}.jsonl"));
            logger.LogInformation("Se reimportaron {Count} consumos de IA pendientes desde {Archivo}.", registros.Count, Path.GetFileName(archivo));
        }
    }

    /// <summary>Lee las líneas JSON de un archivo de pendientes; ignora líneas dañadas.</summary>
    internal static IReadOnlyList<IaUsoRegistro> LeerPendientes(IEnumerable<string> lineas)
    {
        var registros = new List<IaUsoRegistro>();
        foreach (var linea in lineas)
        {
            if (string.IsNullOrWhiteSpace(linea))
                continue;
            try
            {
                if (JsonSerializer.Deserialize<IaUsoRegistro>(linea) is { } r)
                    registros.Add(r);
            }
            catch (JsonException)
            {
            }
        }
        return registros;
    }

    /// <summary>INSERT de N filas: calcula IdCliente (bases) y el costo con el precio vigente del modelo.</summary>
    internal static string BuildInsertSql(int filas)
    {
        var valores = new StringBuilder();
        for (var i = 0; i < filas; i++)
        {
            if (i > 0)
                valores.Append(",\n        ");
            valores.Append($"(@F{i}, @B{i}, @D{i}, @Fn{i}, @M{i}, @E{i}, @C{i}, @S{i}, @Q{i}, @Y{i}, @R{i}, @K{i})");
        }

        return $"""
            INSERT INTO dbo.IA_USO
                (FechaHoraUtc, IdBase, IdCliente, BaseDatos, Funcion, Modelo, TokensEntrada, TokensEntradaCacheados,
                 TokensSalida, Busquedas, BytesAlmacenados, CostoUsd, Referencia, ServidorApp)
            SELECT v.FechaHoraUtc, v.IdBase, COALESCE(NULLIF(LTRIM(RTRIM(b.idcliente)), ''), NULLIF(LTRIM(RTRIM(v.IdCliente)), '')), v.BaseDatos, v.Funcion, v.Modelo,
                   v.TokensEntrada, v.TokensEntradaCacheados, v.TokensSalida, v.Busquedas, v.BytesAlmacenados,
                   CASE WHEN p.IdPrecio IS NULL THEN NULL ELSE
                       (CAST(v.TokensEntrada - v.TokensEntradaCacheados AS decimal(18, 6)) * p.UsdPorMillonEntrada
                        + CAST(v.TokensEntradaCacheados AS decimal(18, 6)) * p.UsdPorMillonEntradaCacheada
                        + CAST(v.TokensSalida AS decimal(18, 6)) * p.UsdPorMillonSalida) / 1000000
                       + v.Busquedas * p.UsdPorBusqueda
                       + CAST(v.BytesAlmacenados AS decimal(18, 6)) / 1073741824 * p.UsdPorGbDia
                   END,
                   v.Referencia, @Servidor
            FROM (VALUES
                    {valores}
                 ) v (FechaHoraUtc, IdBase, BaseDatos, Funcion, Modelo, TokensEntrada, TokensEntradaCacheados,
                      TokensSalida, Busquedas, BytesAlmacenados, Referencia, IdCliente)
            LEFT JOIN dbo.bases b ON b.id = v.IdBase
            OUTER APPLY (
                SELECT TOP (1) pm.*
                FROM dbo.IA_PRECIO_MODELO pm
                WHERE v.Modelo LIKE pm.Modelo + N'%'
                  AND pm.VigenteDesde <= CAST(v.FechaHoraUtc AS date)
                ORDER BY LEN(pm.Modelo) DESC, pm.VigenteDesde DESC
            ) p;
            """;
    }

    private static void AgregarParametros(SqlCommand cmd, IReadOnlyList<IaUsoRegistro> filas)
    {
        cmd.Parameters.AddWithValue("@Servidor", Environment.MachineName);
        for (var i = 0; i < filas.Count; i++)
        {
            var r = filas[i];
            cmd.Parameters.Add(new SqlParameter($"@F{i}", System.Data.SqlDbType.DateTime2) { Value = r.FechaHoraUtc });
            cmd.Parameters.Add(new SqlParameter($"@B{i}", System.Data.SqlDbType.Int) { Value = (object?)r.IdBase ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter($"@D{i}", System.Data.SqlDbType.NVarChar, 128) { Value = r.BaseDatos });
            cmd.Parameters.Add(new SqlParameter($"@Fn{i}", System.Data.SqlDbType.NVarChar, 40) { Value = r.Funcion });
            cmd.Parameters.Add(new SqlParameter($"@M{i}", System.Data.SqlDbType.NVarChar, 80) { Value = r.Modelo });
            cmd.Parameters.Add(new SqlParameter($"@E{i}", System.Data.SqlDbType.Int) { Value = r.TokensEntrada });
            cmd.Parameters.Add(new SqlParameter($"@C{i}", System.Data.SqlDbType.Int) { Value = r.TokensEntradaCacheados });
            cmd.Parameters.Add(new SqlParameter($"@S{i}", System.Data.SqlDbType.Int) { Value = r.TokensSalida });
            cmd.Parameters.Add(new SqlParameter($"@Q{i}", System.Data.SqlDbType.Int) { Value = r.Busquedas });
            cmd.Parameters.Add(new SqlParameter($"@Y{i}", System.Data.SqlDbType.BigInt) { Value = r.BytesAlmacenados });
            cmd.Parameters.Add(new SqlParameter($"@R{i}", System.Data.SqlDbType.NVarChar, 120) { Value = r.Referencia });
            cmd.Parameters.Add(new SqlParameter($"@K{i}", System.Data.SqlDbType.NVarChar, 40) { Value = (object?)r.IdCliente ?? DBNull.Value });
        }
    }

    private void GuardarPendientes(IReadOnlyList<IaUsoRegistro> lote)
    {
        try
        {
            Directory.CreateDirectory(DirectorioPendientes);
            var path = Path.Combine(DirectorioPendientes, $"ia-uso-pendiente-{DateTime.UtcNow:yyyyMM}.jsonl");
            File.AppendAllLines(path, lote.Select(r => JsonSerializer.Serialize(r)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudieron guardar localmente {Count} registros de consumo de IA.", lote.Count);
        }
    }
}

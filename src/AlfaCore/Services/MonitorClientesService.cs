using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <inheritdoc cref="IMonitorClientesService"/>
public sealed class MonitorClientesService(
    IConfiguration configuration,
    ICentralClientesService centralClientes,
    IAppEventService appEvents,
    ILogger<MonitorClientesService> logger) : IMonitorClientesService
{
    internal const string TipoReporte = "REPORTE";
    private const int MaxItemsPorReporte = 500;

    // El esquema se verifica una sola vez por proceso.
    private static volatile bool _esquemaListo;
    private static readonly SemaphoreSlim EsquemaLock = new(1, 1);

    private static readonly string[] FormatosFecha =
    [
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd"
    ];

    private string? ConnectionString => configuration.GetConnectionString("AlfaGestion");

    public bool Disponible => !string.IsNullOrWhiteSpace(ConnectionString);

    public async Task<MonitorReporteResultado> RegistrarReporteAsync(MonitorReporteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var licencia = request.LicenciaPrincipal.Trim();
        var dbServidor = Recortar(request.DbServidor, 128);
        var dbNombre = Recortar(request.DbNombre, 128);
        if (string.IsNullOrWhiteSpace(licencia))
            throw new InvalidOperationException("Falta licenciaPrincipal.");
        if (string.IsNullOrWhiteSpace(dbServidor) || string.IsNullOrWhiteSpace(dbNombre))
            throw new InvalidOperationException("Faltan dbServidor y/o dbNombre.");
        if (!Disponible)
            throw new InvalidOperationException("Esta instalación no tiene configurada la conexión AlfaGestion.");

        var cliente = await centralClientes.GetByLicenciaPrincipalAsync(licencia, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("licencia-inexistente");
        var clienteCodigo = Recortar(cliente.IdCliente, 15);

        // Desfase de reloj del equipo del cliente: las fechas del reporte se pasan a hora del servidor.
        // Al segundo: con milisegundos, el redondeo de datetime podía dejar las filas recién grabadas
        // "antes" de @Ahora y el DELETE de abajo las borraba.
        var ahora = DateTime.Now;
        ahora = ahora.AddTicks(-(ahora.Ticks % TimeSpan.TicksPerSecond));
        var desfase = TimeSpan.Zero;
        if (request.FechaHoraCliente is { } fechaCliente)
        {
            var diferencia = ahora - fechaCliente;
            if (diferencia.Duration() < TimeSpan.FromDays(7))
                desfase = diferencia;
        }

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await AsegurarEsquemaAsync(cn, ct).ConfigureAwait(false);

            var tiposValidos = (await cn.QueryAsync<string>(new CommandDefinition(
                    "SELECT LTRIM(RTRIM(Codigo)) FROM dbo.MON_CONTROL_TIPO;", cancellationToken: ct)).ConfigureAwait(false))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Normalizo y descarto lo que no corresponde: tipos desconocidos, REPORTE (lo calcula el
            // servidor) y destinos repetidos (gana el último).
            var items = request.Items
                .Take(MaxItemsPorReporte)
                .Select(i => new
                {
                    Tipo = Recortar(i.Tipo, 30).ToUpperInvariant(),
                    Destino = Recortar(i.Destino, 120),
                    UltimoOk = Corregir(i.UltimoOk, desfase),
                    UltimoIntento = Corregir(i.UltimoIntento, desfase),
                    Resultado = NullSiVacio(Recortar(i.Resultado, 15).ToUpperInvariant()),
                    Mensaje = NullSiVacio(Recortar(i.Mensaje, 500))
                })
                .Where(i => i.Tipo.Length > 0 && tiposValidos.Contains(i.Tipo) && !string.Equals(i.Tipo, TipoReporte, StringComparison.OrdinalIgnoreCase))
                .GroupBy(i => (i.Tipo, Destino: i.Destino.ToUpperInvariant()))
                .Select(g => g.Last())
                .ToList();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct).ConfigureAwait(false);

            var idOrigen = await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                SELECT IdOrigen FROM dbo.MON_ORIGEN WITH (UPDLOCK, HOLDLOCK)
                WHERE ClienteCodigo = @ClienteCodigo AND DbServidor = @DbServidor AND DbNombre = @DbNombre;
                """,
                new { ClienteCodigo = clienteCodigo, DbServidor = dbServidor, DbNombre = dbNombre }, tx, cancellationToken: ct)).ConfigureAwait(false);

            var parametrosOrigen = new
            {
                ClienteCodigo = clienteCodigo,
                LicenciaPrincipal = Recortar(licencia, 40),
                DbServidor = dbServidor,
                DbNombre = dbNombre,
                HostCliente = NullSiVacio(Recortar(request.HostCliente, 128)),
                VersionSistema = NullSiVacio(Recortar(request.VersionSistema, 50)),
                DesfaseSegundos = (int)Math.Round(desfase.TotalSeconds),
                Ahora = ahora,
                IdOrigen = idOrigen ?? 0
            };

            if (idOrigen is null)
            {
                idOrigen = await cn.QuerySingleAsync<int>(new CommandDefinition(
                    """
                    INSERT INTO dbo.MON_ORIGEN (ClienteCodigo, LicenciaPrincipal, DbServidor, DbNombre, HostCliente, VersionSistema, DesfaseSegundos, PrimerReporte, UltimoReporte)
                    OUTPUT INSERTED.IdOrigen
                    VALUES (@ClienteCodigo, @LicenciaPrincipal, @DbServidor, @DbNombre, @HostCliente, @VersionSistema, @DesfaseSegundos, @Ahora, @Ahora);
                    """, parametrosOrigen, tx, cancellationToken: ct)).ConfigureAwait(false);
            }
            else
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.MON_ORIGEN
                    SET LicenciaPrincipal = @LicenciaPrincipal,
                        HostCliente = ISNULL(@HostCliente, HostCliente),
                        VersionSistema = ISNULL(@VersionSistema, VersionSistema),
                        DesfaseSegundos = @DesfaseSegundos,
                        UltimoReporte = @Ahora
                    WHERE IdOrigen = @IdOrigen;
                    """, parametrosOrigen, tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            foreach (var item in items)
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.MON_ESTADO
                    SET UltimoOk = @UltimoOk, UltimoIntento = @UltimoIntento, Resultado = @Resultado,
                        Mensaje = @Mensaje, FechaReporte = @Ahora
                    WHERE IdOrigen = @IdOrigen AND TipoCodigo = @Tipo AND Destino = @Destino;
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.MON_ESTADO (IdOrigen, TipoCodigo, Destino, UltimoOk, UltimoIntento, Resultado, Mensaje, FechaReporte)
                        VALUES (@IdOrigen, @Tipo, @Destino, @UltimoOk, @UltimoIntento, @Resultado, @Mensaje, @Ahora);
                    """,
                    new { IdOrigen = idOrigen.Value, item.Tipo, item.Destino, item.UltimoOk, item.UltimoIntento, item.Resultado, item.Mensaje, Ahora = ahora },
                    tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            // Cada reporte trae la foto completa de los tipos que informa: lo que ya no vino de esos
            // tipos (un PDV dado de baja, una tarea borrada) se quita. Los tipos que no vinieron quedan.
            foreach (var tipo in items.Select(i => i.Tipo).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.MON_ESTADO WHERE IdOrigen = @IdOrigen AND TipoCodigo = @Tipo AND FechaReporte < @Ahora;",
                    new { IdOrigen = idOrigen.Value, Tipo = tipo, Ahora = ahora }, tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);

            return new MonitorReporteResultado
            {
                ClienteCodigo = clienteCodigo,
                IdOrigen = idOrigen.Value,
                ItemsGrabados = items.Count,
                ItemsIgnorados = request.Items.Count - items.Count
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            await appEvents.LogErrorAsync("MonitorClientes", "RegistrarReporte", ex, "No se pudo grabar el reporte del monitor.",
                new { Licencia = licencia, DbServidor = dbServidor, DbNombre = dbNombre }, AppEventSeverity.Error, ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<MonitorTableroDto> GetTableroAsync(CancellationToken ct = default)
    {
        if (!Disponible)
            throw new InvalidOperationException("Esta instalación no tiene configurada la conexión AlfaGestion.");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await AsegurarEsquemaAsync(cn, ct).ConfigureAwait(false);

        using var multi = await cn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT LTRIM(RTRIM(Codigo)) AS Codigo, Nombre, Descripcion, Medida, AvisoMinutos, CriticoMinutos, Orden, Activo
            FROM dbo.MON_CONTROL_TIPO;

            SELECT LTRIM(RTRIM(ClienteCodigo)) AS ClienteCodigo, LTRIM(RTRIM(TipoCodigo)) AS TipoCodigo, AvisoMinutos, CriticoMinutos, Desactivado
            FROM dbo.MON_CONTROL_CLIENTE;

            SELECT o.IdOrigen, LTRIM(RTRIM(o.ClienteCodigo)) AS ClienteCodigo,
                   ISNULL(LTRIM(RTRIM(c.RAZON_SOCIAL)), N'') AS RazonSocial,
                   ISNULL(LTRIM(RTRIM(c.Clasificacion)), N'') AS PrioridadCodigo,
                   ISNULL(LTRIM(RTRIM(cl.Descripcion)), N'') AS PrioridadDescripcion,
                   o.DbServidor, o.DbNombre, o.HostCliente, o.VersionSistema, o.UltimoReporte
            FROM dbo.MON_ORIGEN o
            OUTER APPLY (
                SELECT TOP (1) v.RAZON_SOCIAL, v.Clasificacion
                FROM dbo.VT_CLIENTES v
                WHERE LTRIM(RTRIM(v.CODIGO)) = LTRIM(RTRIM(o.ClienteCodigo))
            ) c
            LEFT JOIN dbo.TA_CLASIFICACIONES cl ON LTRIM(RTRIM(cl.Codigo)) = LTRIM(RTRIM(c.Clasificacion))
            WHERE o.Desactivado = 0;

            SELECT e.IdOrigen, LTRIM(RTRIM(e.TipoCodigo)) AS TipoCodigo, e.Destino, e.UltimoOk, e.UltimoIntento, e.Resultado, e.Mensaje
            FROM dbo.MON_ESTADO e
            INNER JOIN dbo.MON_ORIGEN o ON o.IdOrigen = e.IdOrigen
            WHERE o.Desactivado = 0;

            SELECT IdOrigen, LTRIM(RTRIM(TipoCodigo)) AS TipoCodigo, Destino, TomadoPor, TomadoFecha, SilenciadoHasta, SilenciadoPor, Nota
            FROM dbo.MON_SEGUIMIENTO;
            """, cancellationToken: ct)).ConfigureAwait(false);

        var tipos = (await multi.ReadAsync<MonitorTipoControlDto>().ConfigureAwait(false)).ToList();
        var limites = (await multi.ReadAsync<LimiteCliente>().ConfigureAwait(false)).ToList();
        var origenes = (await multi.ReadAsync<OrigenFila>().ConfigureAwait(false)).ToList();
        var estados = (await multi.ReadAsync<EstadoFila>().ConfigureAwait(false)).ToList();
        var seguimientos = (await multi.ReadAsync<SeguimientoFila>().ConfigureAwait(false)).ToList();

        var ahora = DateTime.Now;
        var filas = Evaluar(ahora, tipos, limites, origenes, estados);
        AplicarSeguimiento(filas, seguimientos, ahora);
        return new MonitorTableroDto
        {
            FechaHora = ahora,
            Tipos = tipos.OrderBy(t => t.Orden).ToList(),
            Filas = filas
        };
    }

    public async Task GuardarSeguimientoAsync(MonitorSeguimientoRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IdOrigen <= 0 || string.IsNullOrWhiteSpace(request.TipoCodigo))
            throw new InvalidOperationException("Falta la fila del monitor.");
        var usuario = Recortar(request.Usuario, 80);
        if (usuario.Length == 0)
            throw new InvalidOperationException("Falta el usuario.");

        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        await cn.ExecuteAsync(new CommandDefinition(
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.MON_SEGUIMIENTO WITH (UPDLOCK, HOLDLOCK)
                           WHERE IdOrigen = @IdOrigen AND TipoCodigo = @TipoCodigo AND Destino = @Destino)
                INSERT INTO dbo.MON_SEGUIMIENTO (IdOrigen, TipoCodigo, Destino, UsuarioModificacion)
                VALUES (@IdOrigen, @TipoCodigo, @Destino, @Usuario);

            UPDATE dbo.MON_SEGUIMIENTO
            SET TomadoPor = CASE WHEN @Tomar IS NULL THEN TomadoPor WHEN @Tomar = 1 THEN @Usuario ELSE NULL END,
                TomadoFecha = CASE WHEN @Tomar IS NULL THEN TomadoFecha WHEN @Tomar = 1 THEN GETDATE() ELSE NULL END,
                SilenciadoHasta = CASE WHEN @CambiarSilencio = 1 THEN @SilenciarHasta ELSE SilenciadoHasta END,
                SilenciadoPor = CASE WHEN @CambiarSilencio = 1 THEN CASE WHEN @SilenciarHasta IS NULL THEN NULL ELSE @Usuario END ELSE SilenciadoPor END,
                Nota = CASE WHEN @CambiarNota = 1 THEN @Nota ELSE Nota END,
                FechaModificacion = GETDATE(),
                UsuarioModificacion = @Usuario
            WHERE IdOrigen = @IdOrigen AND TipoCodigo = @TipoCodigo AND Destino = @Destino;

            DELETE FROM dbo.MON_SEGUIMIENTO
            WHERE IdOrigen = @IdOrigen AND TipoCodigo = @TipoCodigo AND Destino = @Destino
              AND TomadoPor IS NULL AND Nota IS NULL AND (SilenciadoHasta IS NULL OR SilenciadoHasta <= GETDATE());
            """,
            new
            {
                request.IdOrigen,
                TipoCodigo = Recortar(request.TipoCodigo, 30).ToUpperInvariant(),
                Destino = Recortar(request.Destino, 120),
                Usuario = usuario,
                request.Tomar,
                CambiarSilencio = request.CambiarSilencio,
                request.SilenciarHasta,
                CambiarNota = request.CambiarNota,
                Nota = NullSiVacio(Recortar(request.Nota, 500))
            }, cancellationToken: ct)).ConfigureAwait(false);

        await appEvents.LogAuditAsync("MonitorClientes", "Seguimiento", "MON_SEGUIMIENTO",
            MonitorClave.Armar(request.IdOrigen, request.TipoCodigo, request.Destino), "Seguimiento del monitor actualizado.",
            new { request.Tomar, request.CambiarSilencio, request.SilenciarHasta, request.CambiarNota, Usuario = usuario }, ct).ConfigureAwait(false);
    }

    public async Task<int> LimpiarSeguimientosAsync(MonitorTableroDto tablero, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tablero);
        var conProblema = tablero.Filas
            .Where(f => f.Estado != MonitorEstado.Ok)
            .Select(f => f.Clave)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        var seguimientos = (await cn.QueryAsync<SeguimientoFila>(new CommandDefinition(
            "SELECT IdOrigen, LTRIM(RTRIM(TipoCodigo)) AS TipoCodigo, Destino, TomadoPor, TomadoFecha, SilenciadoHasta, SilenciadoPor, Nota FROM dbo.MON_SEGUIMIENTO;",
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        var tocados = 0;
        foreach (var s in seguimientos)
        {
            var resuelta = !conProblema.Contains(MonitorClave.Armar(s.IdOrigen, s.TipoCodigo, s.Destino));
            var silencioVencido = s.SilenciadoHasta is null || s.SilenciadoHasta <= tablero.FechaHora;
            var parametros = new { s.IdOrigen, s.TipoCodigo, s.Destino };

            if (resuelta && silencioVencido)
            {
                tocados += await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.MON_SEGUIMIENTO WHERE IdOrigen = @IdOrigen AND TipoCodigo = @TipoCodigo AND Destino = @Destino;",
                    parametros, cancellationToken: ct)).ConfigureAwait(false);
            }
            else if (resuelta && (s.TomadoPor is not null || s.Nota is not null))
            {
                tocados += await cn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.MON_SEGUIMIENTO SET TomadoPor = NULL, TomadoFecha = NULL, Nota = NULL, FechaModificacion = GETDATE()
                    WHERE IdOrigen = @IdOrigen AND TipoCodigo = @TipoCodigo AND Destino = @Destino;
                    """, parametros, cancellationToken: ct)).ConfigureAwait(false);
            }
            else if (!resuelta && silencioVencido && s.SilenciadoHasta is not null && s.TomadoPor is null && s.Nota is null)
            {
                tocados += await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.MON_SEGUIMIENTO WHERE IdOrigen = @IdOrigen AND TipoCodigo = @TipoCodigo AND Destino = @Destino;",
                    parametros, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        return tocados;
    }

    public async Task<MonitorAvisoConfigDto> GetAvisoConfigAsync(CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        var valores = (await cn.QueryAsync<(string Clave, string? Valor)>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(Clave)), Valor FROM dbo.MON_CONFIG;", cancellationToken: ct)).ConfigureAwait(false))
            .ToDictionary(x => x.Clave, x => (x.Valor ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);

        string V(string clave) => valores.TryGetValue(clave, out var v) ? v : string.Empty;

        return new MonitorAvisoConfigDto
        {
            Activo = !string.Equals(V("AVISO_ACTIVO"), "NO", StringComparison.OrdinalIgnoreCase),
            Prioridades = V("AVISO_PRIORIDADES"),
            RepetirMinutos = int.TryParse(V("AVISO_REPETIR_MINUTOS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r >= 0 ? r : 120,
            UsuariosPush = V("AVISO_USUARIOS_PUSH"),
            IdHilo = long.TryParse(V("AVISO_ID_HILO"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) && h > 0 ? h : null,
            UltimoEnvio = ParsearFecha(V("AVISO_ULTIMO_ENVIO")),
            UltimaFirma = V("AVISO_ULTIMA_FIRMA"),
            Forzar = V("AVISO_FORZAR") == "1"
        };
    }

    public async Task GuardarAvisoConfigAsync(MonitorAvisoConfigDto config, string usuario, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var prioridades = string.Join(",", config.PrioridadesLista.Select(p => Recortar(p, 10)).Distinct(StringComparer.OrdinalIgnoreCase));
        var usuarios = string.Join(",", config.UsuariosPushLista.Select(u => Recortar(u, 80)).Distinct(StringComparer.OrdinalIgnoreCase));

        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        await GrabarConfigAsync(cn, new Dictionary<string, string>
        {
            ["AVISO_ACTIVO"] = config.Activo ? "SI" : "NO",
            ["AVISO_PRIORIDADES"] = prioridades,
            ["AVISO_REPETIR_MINUTOS"] = Math.Clamp(config.RepetirMinutos, 0, 10080).ToString(CultureInfo.InvariantCulture),
            ["AVISO_USUARIOS_PUSH"] = usuarios
        }, Recortar(usuario, 80), ct).ConfigureAwait(false);

        await appEvents.LogAuditAsync("MonitorClientes", "AvisoConfig", "MON_CONFIG", "AVISO",
            "Configuración de avisos del monitor actualizada.",
            new { config.Activo, Prioridades = prioridades, config.RepetirMinutos, UsuariosPush = usuarios, Usuario = usuario }, ct).ConfigureAwait(false);
    }

    public async Task GuardarAvisoEstadoAsync(MonitorAvisoConfigDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        await GrabarConfigAsync(cn, new Dictionary<string, string>
        {
            ["AVISO_ID_HILO"] = config.IdHilo?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["AVISO_ULTIMO_ENVIO"] = config.UltimoEnvio?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
            ["AVISO_ULTIMA_FIRMA"] = config.UltimaFirma.Length > 1000 ? config.UltimaFirma[..1000] : config.UltimaFirma,
            ["AVISO_FORZAR"] = config.Forzar ? "1" : "0"
        }, "MONITOR", ct).ConfigureAwait(false);
    }

    public async Task SolicitarAvisoAhoraAsync(string usuario, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        await GrabarConfigAsync(cn, new Dictionary<string, string> { ["AVISO_FORZAR"] = "1" }, Recortar(usuario, 80), ct).ConfigureAwait(false);
    }

    // ---- AlfaActualizacion_Monitor ---------------------------------------------------------

    public async Task<int> PublicarActualizacionAsync(string version, string archivoRemoto, string sha256, long tamanioBytes,
        string? notas, string usuario, CancellationToken ct = default)
    {
        var v = Recortar(version, 30);
        var archivo = Recortar(archivoRemoto, 300).Replace('\\', '/');
        var hash = Recortar(sha256, 64).ToLowerInvariant();
        if (!EsVersionValida(v))
            throw new InvalidOperationException("La versión tiene que tener el formato AAAAMMDD-NN (ej. 20261029-01).");
        if (archivo.Length == 0 || archivo.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Falta la ruta del archivo en AlfaArchivos.");
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidOperationException("El SHA-256 tiene que tener 64 caracteres hexadecimales.");
        if (tamanioBytes <= 0)
            throw new InvalidOperationException("Falta el tamaño del archivo.");

        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        var id = await cn.QuerySingleAsync<int>(new CommandDefinition(
            """
            UPDATE dbo.MON_ACTUALIZACION
            SET ArchivoRemoto = @Archivo, Sha256 = @Hash, TamanioBytes = @Tamanio, Notas = @Notas
            WHERE Version = @Version;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.MON_ACTUALIZACION (Version, ArchivoRemoto, Sha256, TamanioBytes, Notas, UsuarioAlta)
                VALUES (@Version, @Archivo, @Hash, @Tamanio, @Notas, @Usuario);
            SELECT IdVersion FROM dbo.MON_ACTUALIZACION WHERE Version = @Version;
            """,
            new { Version = v, Archivo = archivo, Hash = hash, Tamanio = tamanioBytes, Notas = NullSiVacio(Recortar(notas, 500)), Usuario = Recortar(usuario, 80) },
            cancellationToken: ct)).ConfigureAwait(false);

        await appEvents.LogAuditAsync("MonitorClientes", "PublicarActualizacion", "MON_ACTUALIZACION", id.ToString(CultureInfo.InvariantCulture),
            "Versión de AlfaActualizacion_Monitor publicada.", new { Version = v, Archivo = archivo, TamanioBytes = tamanioBytes }, ct).ConfigureAwait(false);
        return id;
    }

    public async Task<IReadOnlyList<MonitorActualizacionDto>> GetActualizacionesAsync(CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        return (await cn.QueryAsync<MonitorActualizacionDto>(new CommandDefinition(
            """
            SELECT a.IdVersion, a.Version, a.ArchivoRemoto, a.Sha256, a.TamanioBytes, a.Notas, a.Liberacion, a.ClientesPrueba,
                   a.FechaAlta, a.UsuarioAlta, a.FechaLiberacion, a.UsuarioLiberacion,
                   (SELECT COUNT(*) FROM dbo.MON_ESTADO e INNER JOIN dbo.MON_ORIGEN o ON o.IdOrigen = e.IdOrigen
                    WHERE o.Desactivado = 0 AND e.TipoCodigo = N'ACTUALIZACION' AND e.Destino = a.Version) AS Instalaciones
            FROM dbo.MON_ACTUALIZACION a
            ORDER BY a.Version DESC;
            """, cancellationToken: ct)).ConfigureAwait(false)).ToList();
    }

    public async Task CambiarLiberacionAsync(int idVersion, string liberacion, string? clientesPrueba, string usuario, CancellationToken ct = default)
    {
        var lib = (liberacion ?? string.Empty).Trim().ToUpperInvariant();
        if (!MonitorLiberacion.Valores.Contains(lib))
            throw new InvalidOperationException("Liberación inválida.");
        var prueba = string.Join(",", (clientesPrueba ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => Recortar(c, 15))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        if (lib == MonitorLiberacion.Prueba && prueba.Length == 0)
            throw new InvalidOperationException("Para liberar a prueba hay que indicar al menos un código de cliente.");

        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        var filas = await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.MON_ACTUALIZACION
            SET Liberacion = @Liberacion, ClientesPrueba = @Prueba, FechaLiberacion = GETDATE(), UsuarioLiberacion = @Usuario
            WHERE IdVersion = @IdVersion;
            """, new { IdVersion = idVersion, Liberacion = lib, Prueba = NullSiVacio(Recortar(prueba, 500)), Usuario = Recortar(usuario, 80) },
            cancellationToken: ct)).ConfigureAwait(false);
        if (filas == 0)
            throw new InvalidOperationException("La versión no existe.");

        await appEvents.LogAuditAsync("MonitorClientes", "LiberarActualizacion", "MON_ACTUALIZACION", idVersion.ToString(CultureInfo.InvariantCulture),
            "Liberación de AlfaActualizacion_Monitor cambiada.", new { Liberacion = lib, ClientesPrueba = prueba, Usuario = usuario }, ct).ConfigureAwait(false);
    }

    public async Task<MonitorActualizacionPendienteDto?> GetActualizacionPendienteAsync(string licenciaPrincipal, string? versionInstalada, CancellationToken ct = default)
    {
        var licencia = (licenciaPrincipal ?? string.Empty).Trim();
        if (licencia.Length == 0)
            throw new InvalidOperationException("Falta licenciaPrincipal.");
        var cliente = await centralClientes.GetByLicenciaPrincipalAsync(licencia, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("licencia-inexistente");
        var codigo = Recortar(cliente.IdCliente, 15);

        await using var cn = await AbrirAsync(ct).ConfigureAwait(false);
        var prioridad = await cn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) LTRIM(RTRIM(Clasificacion)) FROM dbo.VT_CLIENTES WHERE LTRIM(RTRIM(CODIGO)) = @Codigo;",
            new { Codigo = codigo }, cancellationToken: ct)).ConfigureAwait(false);
        var versiones = (await cn.QueryAsync<MonitorActualizacionDto>(new CommandDefinition(
            """
            SELECT IdVersion, Version, ArchivoRemoto, Sha256, TamanioBytes, Liberacion, ClientesPrueba
            FROM dbo.MON_ACTUALIZACION WHERE Liberacion <> N'NADIE';
            """, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        var elegida = ElegirActualizacion(versiones, codigo, prioridad, versionInstalada);
        return elegida is null
            ? null
            : new MonitorActualizacionPendienteDto
            {
                Version = elegida.Version,
                ArchivoRemoto = elegida.ArchivoRemoto,
                Sha256 = elegida.Sha256,
                TamanioBytes = elegida.TamanioBytes
            };
    }

    /// <summary>Formato de versión: AAAAMMDD-NN. Así el orden alfabético es el orden de publicación.</summary>
    internal static bool EsVersionValida(string? version)
        => version is { Length: 11 } v && v[8] == '-' && v[..8].All(char.IsAsciiDigit) && v[9..].All(char.IsAsciiDigit);

    /// <summary>La versión más nueva que le corresponde al cliente y que es posterior a la instalada.</summary>
    internal static MonitorActualizacionDto? ElegirActualizacion(IEnumerable<MonitorActualizacionDto> versiones,
        string clienteCodigo, string? prioridad, string? versionInstalada)
    {
        var instalada = EsVersionValida(versionInstalada?.Trim()) ? versionInstalada!.Trim() : string.Empty;
        var esPrioritario = prioridad?.Trim() is "1" or "2";
        return versiones
            .Where(v => EsVersionValida(v.Version))
            .Where(v => string.CompareOrdinal(v.Version, instalada) > 0)
            .Where(v =>
            {
                var lib = (v.Liberacion ?? string.Empty).Trim().ToUpperInvariant();
                if (lib == MonitorLiberacion.Nadie)
                    return false;
                if (v.ClientesPruebaLista.Contains(clienteCodigo.Trim(), StringComparer.OrdinalIgnoreCase))
                    return true;
                return lib == MonitorLiberacion.Todos || (lib == MonitorLiberacion.P1P2 && esPrioritario);
            })
            .OrderByDescending(v => v.Version, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static async Task GrabarConfigAsync(SqlConnection cn, IReadOnlyDictionary<string, string> valores, string usuario, CancellationToken ct)
    {
        foreach (var (clave, valor) in valores)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.MON_CONFIG SET Valor = @Valor, FechaModificacion = GETDATE(), UsuarioModificacion = @Usuario WHERE Clave = @Clave;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.MON_CONFIG (Clave, Valor, UsuarioModificacion) VALUES (@Clave, @Valor, @Usuario);
                """, new { Clave = clave, Valor = valor, Usuario = usuario }, cancellationToken: ct)).ConfigureAwait(false);
        }
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        if (!Disponible)
            throw new InvalidOperationException("Esta instalación no tiene configurada la conexión AlfaGestion.");
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await AsegurarEsquemaAsync(cn, ct).ConfigureAwait(false);
        return cn;
    }

    internal sealed class SeguimientoFila
    {
        public int IdOrigen { get; init; }
        public string TipoCodigo { get; init; } = string.Empty;
        public string Destino { get; init; } = string.Empty;
        public string? TomadoPor { get; init; }
        public DateTime? TomadoFecha { get; init; }
        public DateTime? SilenciadoHasta { get; init; }
        public string? SilenciadoPor { get; init; }
        public string? Nota { get; init; }
    }

    internal static void AplicarSeguimiento(IReadOnlyList<MonitorFilaDto> filas, IReadOnlyList<SeguimientoFila> seguimientos, DateTime ahora)
    {
        var porClave = seguimientos
            .GroupBy(s => MonitorClave.Armar(s.IdOrigen, s.TipoCodigo, s.Destino), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var f in filas)
        {
            if (!porClave.TryGetValue(f.Clave, out var s))
                continue;
            f.TomadoPor = NullSiVacio(s.TomadoPor?.Trim());
            f.TomadoFecha = f.TomadoPor is null ? null : s.TomadoFecha;
            f.Nota = NullSiVacio(s.Nota?.Trim());
            if (s.SilenciadoHasta is { } hasta && hasta > ahora)
            {
                f.Silenciado = true;
                f.SilenciadoHasta = hasta;
                f.SilenciadoPor = s.SilenciadoPor;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Evaluación (sin base de datos, para poder probarla)
    // ---------------------------------------------------------------------------------------------

    internal sealed class LimiteCliente
    {
        public string ClienteCodigo { get; init; } = string.Empty;
        public string TipoCodigo { get; init; } = string.Empty;
        public int? AvisoMinutos { get; init; }
        public int? CriticoMinutos { get; init; }
        public bool Desactivado { get; init; }
    }

    internal sealed class OrigenFila
    {
        public int IdOrigen { get; init; }
        public string ClienteCodigo { get; init; } = string.Empty;
        public string RazonSocial { get; init; } = string.Empty;
        public string PrioridadCodigo { get; init; } = string.Empty;
        public string PrioridadDescripcion { get; init; } = string.Empty;
        public string DbServidor { get; init; } = string.Empty;
        public string DbNombre { get; init; } = string.Empty;
        public string? HostCliente { get; init; }
        public string? VersionSistema { get; init; }
        public DateTime UltimoReporte { get; init; }
    }

    internal sealed class EstadoFila
    {
        public int IdOrigen { get; init; }
        public string TipoCodigo { get; init; } = string.Empty;
        public string Destino { get; init; } = string.Empty;
        public DateTime? UltimoOk { get; init; }
        public DateTime? UltimoIntento { get; init; }
        public string? Resultado { get; init; }
        public string? Mensaje { get; init; }
    }

    internal static IReadOnlyList<MonitorFilaDto> Evaluar(
        DateTime ahora,
        IReadOnlyList<MonitorTipoControlDto> tipos,
        IReadOnlyList<LimiteCliente> limites,
        IReadOnlyList<OrigenFila> origenes,
        IReadOnlyList<EstadoFila> estados)
    {
        var tiposPorCodigo = tipos.ToDictionary(t => t.Codigo, StringComparer.OrdinalIgnoreCase);
        var limitesPorClave = limites
            .GroupBy(l => (Cliente: l.ClienteCodigo.ToUpperInvariant(), Tipo: l.TipoCodigo.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.First());
        var estadosPorOrigen = estados.ToLookup(e => e.IdOrigen);
        var filas = new List<MonitorFilaDto>();

        foreach (var origen in origenes)
        {
            var reporteVencido = false;
            if (tiposPorCodigo.TryGetValue(TipoReporte, out var tipoReporte) && tipoReporte.Activo
                && !Desactivado(limitesPorClave, origen.ClienteCodigo, TipoReporte))
            {
                var (aviso, critico) = Limites(limitesPorClave, origen.ClienteCodigo, tipoReporte);
                var edad = ahora - origen.UltimoReporte;
                var estado = PorEdad(edad, aviso, critico);
                reporteVencido = estado != MonitorEstado.Ok;
                filas.Add(Fila(origen, tipoReporte, string.Empty, origen.UltimoReporte, origen.UltimoReporte, null, null, estado,
                    estado == MonitorEstado.Ok
                        ? $"Reportó hace {Duracion(edad)}"
                        : $"El sistema no reporta hace {Duracion(edad)}{TextoLimite(estado, aviso, critico)}"));
            }

            foreach (var e in estadosPorOrigen[origen.IdOrigen])
            {
                if (!tiposPorCodigo.TryGetValue(e.TipoCodigo, out var tipo) || !tipo.Activo
                    || Desactivado(limitesPorClave, origen.ClienteCodigo, tipo.Codigo))
                    continue;

                MonitorEstado estado;
                string motivo;
                if (reporteVencido)
                {
                    estado = MonitorEstado.SinDatos;
                    motivo = "Sin datos nuevos: el sistema no está reportando";
                }
                else if (string.Equals(tipo.Medida, "RESULTADO", StringComparison.OrdinalIgnoreCase))
                {
                    (estado, motivo) = PorResultado(e.Resultado, e.UltimoIntento, ahora);
                }
                else if (string.Equals(tipo.Medida, "VENCE", StringComparison.OrdinalIgnoreCase))
                {
                    var (aviso, critico) = Limites(limitesPorClave, origen.ClienteCodigo, tipo);
                    (estado, motivo) = PorVencimiento(e.UltimoOk, e.Resultado, ahora, aviso, critico);
                }
                else
                {
                    var (aviso, critico) = Limites(limitesPorClave, origen.ClienteCodigo, tipo);
                    if (e.UltimoOk is null)
                    {
                        estado = critico is null && aviso is null ? MonitorEstado.Ok : MonitorEstado.Critico;
                        motivo = "Nunca registró una ejecución correcta";
                    }
                    else
                    {
                        var edad = ahora - e.UltimoOk.Value;
                        estado = PorEdad(edad, aviso, critico);
                        motivo = estado == MonitorEstado.Ok
                            ? $"Último OK hace {Duracion(edad)}"
                            : $"Sin OK hace {Duracion(edad)}{TextoLimite(estado, aviso, critico)}";
                        if (estado == MonitorEstado.Ok && EsError(e.Resultado))
                        {
                            estado = MonitorEstado.Aviso;
                            motivo = $"El último intento falló (último OK hace {Duracion(edad)})";
                        }
                    }
                }

                filas.Add(Fila(origen, tipo, e.Destino, e.UltimoOk, e.UltimoIntento, e.Resultado, e.Mensaje, estado, motivo));
            }
        }

        return filas
            .OrderBy(f => f.Estado)
            .ThenBy(f => string.IsNullOrEmpty(f.PrioridadCodigo) ? 1 : 0)
            .ThenBy(f => OrdenPrioridad(f.PrioridadCodigo))
            .ThenBy(f => f.RazonSocial, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.ClienteCodigo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.DbNombre, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.TipoOrden)
            .ThenBy(f => f.Destino, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static MonitorFilaDto Fila(OrigenFila o, MonitorTipoControlDto tipo, string destino, DateTime? ultimoOk,
        DateTime? ultimoIntento, string? resultado, string? mensaje, MonitorEstado estado, string motivo)
        => new()
        {
            IdOrigen = o.IdOrigen,
            ClienteCodigo = o.ClienteCodigo,
            RazonSocial = string.IsNullOrWhiteSpace(o.RazonSocial) ? o.ClienteCodigo : o.RazonSocial,
            PrioridadCodigo = o.PrioridadCodigo,
            PrioridadDescripcion = o.PrioridadDescripcion,
            DbServidor = o.DbServidor,
            DbNombre = o.DbNombre,
            HostCliente = o.HostCliente,
            VersionSistema = o.VersionSistema,
            UltimoReporte = o.UltimoReporte,
            TipoCodigo = tipo.Codigo,
            TipoNombre = tipo.Nombre,
            TipoOrden = tipo.Orden,
            Medida = tipo.Medida,
            Destino = destino,
            UltimoOk = ultimoOk,
            UltimoIntento = ultimoIntento,
            Resultado = resultado,
            Mensaje = mensaje,
            Estado = estado,
            Motivo = motivo
        };

    private static (int? Aviso, int? Critico) Limites(
        IReadOnlyDictionary<(string Cliente, string Tipo), LimiteCliente> limites, string cliente, MonitorTipoControlDto tipo)
        => limites.TryGetValue((cliente.ToUpperInvariant(), tipo.Codigo.ToUpperInvariant()), out var propio)
            ? (propio.AvisoMinutos ?? tipo.AvisoMinutos, propio.CriticoMinutos ?? tipo.CriticoMinutos)
            : (tipo.AvisoMinutos, tipo.CriticoMinutos);

    private static bool Desactivado(IReadOnlyDictionary<(string Cliente, string Tipo), LimiteCliente> limites, string cliente, string tipo)
        => limites.TryGetValue((cliente.ToUpperInvariant(), tipo.ToUpperInvariant()), out var propio) && propio.Desactivado;

    internal static MonitorEstado PorEdad(TimeSpan edad, int? avisoMinutos, int? criticoMinutos)
    {
        if (criticoMinutos is > 0 && edad.TotalMinutes >= criticoMinutos.Value)
            return MonitorEstado.Critico;
        if (avisoMinutos is > 0 && edad.TotalMinutes >= avisoMinutos.Value)
            return MonitorEstado.Aviso;
        return MonitorEstado.Ok;
    }

    private static (MonitorEstado, string) PorResultado(string? resultado, DateTime? ultimoIntento, DateTime ahora)
    {
        var cuando = ultimoIntento is { } u ? $" (hace {Duracion(ahora - u)})" : string.Empty;
        if (EsError(resultado))
            return (MonitorEstado.Critico, $"La última ejecución terminó con error{cuando}");
        if (string.Equals(resultado, "ADVERTENCIA", StringComparison.OrdinalIgnoreCase))
            return (MonitorEstado.Aviso, $"La última ejecución terminó con advertencias{cuando}");
        return (MonitorEstado.Ok, ultimoIntento is null ? "Sin ejecuciones registradas" : $"Última ejecución correcta{cuando}");
    }

    /// <summary>
    /// Certificados y similares: <paramref name="vence"/> es la fecha de vencimiento y los límites son
    /// minutos antes de esa fecha. Si el sistema no pudo leerlo (sin archivo, ilegible) queda en aviso.
    /// </summary>
    internal static (MonitorEstado, string) PorVencimiento(DateTime? vence, string? resultado, DateTime ahora, int? avisoMinutos, int? criticoMinutos)
    {
        if (vence is null || EsError(resultado))
            return (MonitorEstado.Aviso, "No se pudo leer la fecha de vencimiento");

        var fecha = vence.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var falta = vence.Value - ahora;
        if (falta <= TimeSpan.Zero)
            return (MonitorEstado.Critico, $"Venció el {fecha} (hace {Duracion(-falta)})");
        if (criticoMinutos is > 0 && falta.TotalMinutes <= criticoMinutos.Value)
            return (MonitorEstado.Critico, $"Vence el {fecha} (en {Duracion(falta)})");
        if (avisoMinutos is > 0 && falta.TotalMinutes <= avisoMinutos.Value)
            return (MonitorEstado.Aviso, $"Vence el {fecha} (en {Duracion(falta)})");
        return (MonitorEstado.Ok, $"Vence el {fecha} (en {Duracion(falta)})");
    }

    private static bool EsError(string? resultado)
        => string.Equals(resultado, "ERROR", StringComparison.OrdinalIgnoreCase);

    private static string TextoLimite(MonitorEstado estado, int? aviso, int? critico)
        => estado switch
        {
            MonitorEstado.Critico when critico is > 0 => $"; límite {Duracion(TimeSpan.FromMinutes(critico.Value))}",
            MonitorEstado.Aviso when aviso is > 0 => $"; aviso desde {Duracion(TimeSpan.FromMinutes(aviso.Value))}",
            _ => string.Empty
        };

    private static int OrdenPrioridad(string codigo)
        => int.TryParse(codigo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;

    /// <summary>"45 min", "3 h 20 min", "2 d 4 h".</summary>
    internal static string Duracion(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;
        if (t.TotalMinutes < 1)
            return "menos de 1 min";
        if (t.TotalHours < 1)
            return $"{(int)t.TotalMinutes} min";
        if (t.TotalDays < 1)
            return t.Minutes == 0 ? $"{(int)t.TotalHours} h" : $"{(int)t.TotalHours} h {t.Minutes} min";
        return t.Hours == 0 ? $"{(int)t.TotalDays} d" : $"{(int)t.TotalDays} d {t.Hours} h";
    }

    // ---------------------------------------------------------------------------------------------
    // Formato del reporte (texto simple para armarlo fácil desde VB6)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Una línea por control: <c>TIPO|DESTINO|ULTIMO_OK|ULTIMO_INTENTO|RESULTADO|MENSAJE</c>, fechas
    /// <c>yyyy-MM-dd HH:mm:ss</c> (vacías si no hay). El mensaje es lo último y puede traer "|".
    /// </summary>
    public static IReadOnlyList<MonitorReporteItem> ParsearItems(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return [];

        var items = new List<MonitorReporteItem>();
        foreach (var linea in texto.Split('\n'))
        {
            var l = linea.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(l))
                continue;
            var partes = l.Split('|', 6);
            items.Add(new MonitorReporteItem
            {
                Tipo = Parte(partes, 0),
                Destino = Parte(partes, 1),
                UltimoOk = ParsearFecha(Parte(partes, 2)),
                UltimoIntento = ParsearFecha(Parte(partes, 3)),
                Resultado = Parte(partes, 4),
                Mensaje = Parte(partes, 5)
            });
        }

        return items;
    }

    public static DateTime? ParsearFecha(string? texto)
        => !string.IsNullOrWhiteSpace(texto)
           && DateTime.TryParseExact(texto.Trim(), FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : null;

    private static string Parte(string[] partes, int indice)
        => indice < partes.Length ? partes[indice].Trim() : string.Empty;

    // ---------------------------------------------------------------------------------------------

    private async Task AsegurarEsquemaAsync(SqlConnection cn, CancellationToken ct)
    {
        if (_esquemaListo)
            return;

        await EsquemaLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_esquemaListo)
                return;

            var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(MonitorClientesSchema.VerificacionSql, cancellationToken: ct)).ConfigureAwait(false);
            if (existe != 1)
            {
                await cn.ExecuteAsync(new CommandDefinition(MonitorClientesSchema.Script, commandTimeout: 120, cancellationToken: ct)).ConfigureAwait(false);
                logger.LogInformation("Se aplicó en la base central el esquema del Monitor de Clientes (MON_*).");
            }

            _esquemaListo = true;
        }
        finally
        {
            EsquemaLock.Release();
        }
    }

    private static DateTime? Corregir(DateTime? fecha, TimeSpan desfase)
        => fecha is { } f ? f + desfase : null;

    private static string Recortar(string? texto, int largo)
    {
        var t = (texto ?? string.Empty).Trim();
        return t.Length > largo ? t[..largo] : t;
    }

    private static string? NullSiVacio(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? null : texto;
}

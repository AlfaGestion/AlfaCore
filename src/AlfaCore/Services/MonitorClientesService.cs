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
            """, cancellationToken: ct)).ConfigureAwait(false);

        var tipos = (await multi.ReadAsync<MonitorTipoControlDto>().ConfigureAwait(false)).ToList();
        var limites = (await multi.ReadAsync<LimiteCliente>().ConfigureAwait(false)).ToList();
        var origenes = (await multi.ReadAsync<OrigenFila>().ConfigureAwait(false)).ToList();
        var estados = (await multi.ReadAsync<EstadoFila>().ConfigureAwait(false)).ToList();

        var ahora = DateTime.Now;
        return new MonitorTableroDto
        {
            FechaHora = ahora,
            Tipos = tipos.OrderBy(t => t.Orden).ToList(),
            Filas = Evaluar(ahora, tipos, limites, origenes, estados)
        };
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

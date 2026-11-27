using System.Globalization;
using System.Text;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Reglas del aviso al chat de soporte del Monitor de Clientes (fase 2, 2026-10-28), sin base de datos.
/// Entra en el aviso una fila en estado crítico, de un cliente con prioridad configurada (por defecto P1 y
/// P2), que nadie tomó y que no está silenciada. Se avisa una sola vez por grupo: cuando aparece una alerta
/// nueva, cada <see cref="MonitorAvisoConfigDto.RepetirMinutos"/> si siguen pendientes y una vez cuando
/// quedan todas resueltas, así el chat no se llena de mensajes.
/// </summary>
public static class MonitorClientesAvisos
{
    internal const int MaxLineas = 15;

    public enum Decision
    {
        Nada,
        Alertas,
        Normalizado
    }

    public static IReadOnlyList<MonitorFilaDto> Pendientes(IEnumerable<MonitorFilaDto> filas, MonitorAvisoConfigDto config)
    {
        var prioridades = config.PrioridadesLista;
        return filas
            .Where(f => f.Estado == MonitorEstado.Critico && !f.Tomado && !f.Silenciado)
            .Where(f => prioridades.Count == 0 || prioridades.Contains(f.PrioridadCodigo, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    public static string Firma(IEnumerable<MonitorFilaDto> pendientes)
        => string.Join(";", pendientes.Select(f => f.Clave).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlySet<string> ClavesDeFirma(string? firma)
        => (firma ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Qué hacer en este ciclo. <paramref name="forzar"/> = "Enviar aviso ahora" desde el tablero: manda el
    /// estado actual aunque no haya nada nuevo (si no hay alertas, avisa que está todo normal).
    /// </summary>
    public static Decision Decidir(IReadOnlyList<MonitorFilaDto> pendientes, MonitorAvisoConfigDto config, DateTime ahora, bool forzar)
    {
        var anteriores = ClavesDeFirma(config.UltimaFirma);
        if (pendientes.Count == 0)
            return forzar || anteriores.Count > 0 ? Decision.Normalizado : Decision.Nada;

        if (forzar || config.UltimoEnvio is null)
            return Decision.Alertas;
        if (pendientes.Any(p => !anteriores.Contains(p.Clave)))
            return Decision.Alertas;
        if (config.RepetirMinutos > 0 && ahora - config.UltimoEnvio.Value >= TimeSpan.FromMinutes(config.RepetirMinutos))
            return Decision.Alertas;
        return Decision.Nada;
    }

    public static string MensajeAlertas(IReadOnlyList<MonitorFilaDto> pendientes, IReadOnlySet<string> anteriores, MonitorAvisoConfigDto config)
    {
        var clientes = pendientes.Select(p => p.ClienteCodigo).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var sb = new StringBuilder();
        sb.Append("🔴 Monitor de Clientes: ")
            .Append(pendientes.Count == 1 ? "1 alerta crítica" : $"{pendientes.Count} alertas críticas")
            .Append(" sin tomar")
            .Append(clientes == 1 ? " (1 cliente" : $" ({clientes} clientes")
            .Append(config.PrioridadesLista.Count == 0 ? ")" : $", {string.Join("/", config.PrioridadesLista.Select(p => "P" + p))})")
            .Append('\n');

        foreach (var f in pendientes.Take(MaxLineas))
        {
            sb.Append("• ");
            if (!string.IsNullOrEmpty(f.PrioridadCodigo))
                sb.Append('P').Append(f.PrioridadCodigo).Append(' ');
            sb.Append(f.RazonSocial).Append(" — ").Append(f.TipoNombre);
            if (!string.IsNullOrWhiteSpace(f.Destino))
                sb.Append(' ').Append(f.Destino);
            sb.Append(": ").Append(f.Motivo);
            if (!anteriores.Contains(f.Clave))
                sb.Append(" (nueva)");
            sb.Append('\n');
        }

        if (pendientes.Count > MaxLineas)
            sb.Append("… y ").Append((pendientes.Count - MaxLineas).ToString(CultureInfo.InvariantCulture)).Append(" más.\n");

        sb.Append("Tomalas o silencialas en Administrar → Monitor de Clientes.");
        return sb.ToString();
    }

    public static string MensajeNormalizado(bool forzado)
        => forzado
            ? "🟢 Monitor de Clientes: no hay alertas críticas pendientes."
            : "🟢 Monitor de Clientes: se normalizaron (o se tomaron) las alertas críticas avisadas.";

    // ---- Backups diarios (2026-11-27) ------------------------------------------------------------
    // Hilo propio "Backups de clientes", visible en la bandeja Todos. Avisa una vez cuando el backup diario
    // de un cliente falla (el último intento dio error o pasó el plazo sin un backup correcto) y una vez cuando
    // se recupera. A diferencia del aviso general, entran todas las prioridades: sin backup se pierde todo.

    internal const string TipoBackup = "BACKUP";

    /// <summary>Backups diarios con problema que nadie tomó ni silenció.</summary>
    public static IReadOnlyList<MonitorFilaDto> BackupsFallidos(IEnumerable<MonitorFilaDto> filas)
        => filas
            .Where(f => string.Equals(f.TipoCodigo, TipoBackup, StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Estado is MonitorEstado.Critico or MonitorEstado.Aviso)
            .Where(f => !f.Tomado && !f.Silenciado)
            .OrderBy(f => f.Estado).ThenBy(f => f.PrioridadCodigo, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.RazonSocial, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Mensaje del ciclo, o null si no hay nada nuevo: backups que empezaron a fallar y los que se recuperaron
    /// (estaban en la firma anterior y ya no fallan). Una fila que se tomó o silenció no cuenta como recuperada.
    /// </summary>
    public static string? MensajeBackups(IReadOnlyList<MonitorFilaDto> fallidos, IReadOnlySet<string> anteriores, IEnumerable<MonitorFilaDto> todas)
    {
        var nuevos = fallidos.Where(f => !anteriores.Contains(f.Clave)).ToList();
        var actuales = fallidos.Select(f => f.Clave).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filasPorClave = todas.GroupBy(f => f.Clave, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var recuperados = anteriores
            .Where(c => !actuales.Contains(c))
            .Select(c => filasPorClave.TryGetValue(c, out var f) ? f : null)
            .Where(f => f is not null && f.Estado == MonitorEstado.Ok)
            .Select(f => f!)
            .ToList();
        if (nuevos.Count == 0 && recuperados.Count == 0)
            return null;

        var sb = new StringBuilder();
        if (nuevos.Count > 0)
        {
            sb.Append("💾 ").Append(nuevos.Count == 1 ? "Falló el backup diario de 1 cliente" : $"Falló el backup diario de {nuevos.Count} clientes").Append('\n');
            foreach (var f in nuevos.Take(MaxLineas))
                sb.Append("• ").Append(LineaBackup(f)).Append('\n');
            if (nuevos.Count > MaxLineas)
                sb.Append("… y ").Append((nuevos.Count - MaxLineas).ToString(CultureInfo.InvariantCulture)).Append(" más.\n");
        }
        if (recuperados.Count > 0)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("✅ ").Append(recuperados.Count == 1 ? "Volvió a funcionar el backup de 1 cliente" : $"Volvió a funcionar el backup de {recuperados.Count} clientes").Append('\n');
            foreach (var f in recuperados.Take(MaxLineas))
                sb.Append("• ").Append(Prioridad(f)).Append(f.RazonSocial).Append(" — ").Append(f.DbNombre).Append('\n');
        }
        var siguen = fallidos.Count - nuevos.Count;
        if (siguen > 0)
            sb.Append(siguen == 1 ? "Sigue 1 backup con problemas." : $"Siguen {siguen} backups con problemas.").Append('\n');
        sb.Append("Detalle en Administrar → Monitor de Clientes (Control: Backup diario).");
        return sb.ToString();
    }

    private static string LineaBackup(MonitorFilaDto f)
    {
        var equipo = string.IsNullOrWhiteSpace(f.HostCliente) ? string.Empty : $" ({f.HostCliente})";
        return $"{Prioridad(f)}{f.RazonSocial} — {f.DbNombre}{equipo}: {f.Motivo}";
    }

    private static string Prioridad(MonitorFilaDto f) => string.IsNullOrEmpty(f.PrioridadCodigo) ? string.Empty : $"P{f.PrioridadCodigo} ";

    /// <summary>Texto corto para el push (el título es "Monitor de Clientes").</summary>
    public static string TextoPush(IReadOnlyList<MonitorFilaDto> pendientes)
    {
        var primeros = pendientes
            .Select(p => p.RazonSocial)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(3)
            .ToList();
        var resto = pendientes.Select(p => p.RazonSocial).Distinct(StringComparer.CurrentCultureIgnoreCase).Count() - primeros.Count;
        var cantidad = pendientes.Count == 1 ? "1 alerta crítica" : $"{pendientes.Count} alertas críticas";
        return $"{cantidad}: {string.Join(", ", primeros)}{(resto > 0 ? $" y {resto} más" : string.Empty)}";
    }
}

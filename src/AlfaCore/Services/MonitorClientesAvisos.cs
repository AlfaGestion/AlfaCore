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

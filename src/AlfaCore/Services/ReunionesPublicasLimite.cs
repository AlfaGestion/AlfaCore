using System.Globalization;

namespace AlfaCore.Services;

/// <summary>
/// Límite de reservas públicas (link de reuniones/capacitaciones): un mismo cliente puede reservar
/// una sola reunión por semana (lunes a domingo). Lo que agendan los técnicos desde el Calendario
/// no pasa por acá. El cliente se reconoce por email, teléfono o razón social.
/// </summary>
public static class ReunionesPublicasLimite
{
    public const int ReservasPorSemana = 1;

    public sealed record ReservaCliente(DateTime FechaInicio, string Email, string Telefono, string RazonSocial);

    public static DateTime InicioSemana(DateTime fecha)
        => fecha.Date.AddDays(-(((int)fecha.DayOfWeek + 6) % 7));

    public static bool MismoCliente(ReservaCliente a, ReservaCliente b)
    {
        var emailA = (a.Email ?? string.Empty).Trim();
        if (emailA.Length > 0 && string.Equals(emailA, (b.Email ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        // Teléfono: se comparan los últimos 8 dígitos (con o sin 549, espacios o guiones).
        var telA = UltimosDigitos(a.Telefono);
        if (telA.Length == 8 && telA == UltimosDigitos(b.Telefono))
            return true;

        var rsA = SoloLetrasYNumeros(a.RazonSocial);
        return rsA.Length >= 4 && rsA == SoloLetrasYNumeros(b.RazonSocial);
    }

    /// <summary>Mensaje para el cliente si ya tiene la reserva de esa semana (null = puede reservar).</summary>
    public static string? Verificar(ReservaCliente nueva, IEnumerable<ReservaCliente> confirmadas)
    {
        var semana = InicioSemana(nueva.FechaInicio);
        var existentes = confirmadas
            .Where(r => InicioSemana(r.FechaInicio) == semana && MismoCliente(nueva, r))
            .OrderBy(r => r.FechaInicio)
            .ToList();
        if (existentes.Count < ReservasPorSemana)
            return null;

        var cultura = CultureInfo.GetCultureInfo("es-AR");
        var existente = existentes[0].FechaInicio;
        var proxima = semana.AddDays(7);
        return $"Ya tenés una reunión reservada esa semana ({existente.ToString("dddd dd/MM 'a las' HH:mm", cultura)}). "
             + $"Se puede reservar una por semana: elegí un horario a partir del lunes {proxima.ToString("dd/MM", cultura)}.";
    }

    private static string UltimosDigitos(string? telefono)
    {
        var digitos = new string((telefono ?? string.Empty).Where(char.IsDigit).ToArray());
        return digitos.Length >= 8 ? digitos[^8..] : string.Empty;
    }

    private static string SoloLetrasYNumeros(string? valor)
        => new string((valor ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}

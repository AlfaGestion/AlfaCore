using System.Globalization;

namespace AlfaCore.Services;

/// <summary>
/// Carga de medios de pago tipeando el código, como en el POS: filtra por código opcional, cuenta o descripción y resuelve
/// el medio con Enter o al salir del campo (Tab). La usan la cobranza directa y la orden de pago.
/// </summary>
public static class MedioPagoBuscador
{
    public static IReadOnlyList<T> Filtrar<T>(IEnumerable<T> medios, string? texto,
        Func<T, string?> cuenta, Func<T, string?> codigoOpcional, Func<T, string?> descripcion)
    {
        var t = (texto ?? "").Trim();
        return t.Length == 0 ? medios.ToList() : medios.Where(m =>
            (codigoOpcional(m) ?? "").Contains(t, StringComparison.OrdinalIgnoreCase)
            || (cuenta(m) ?? "").Contains(t, StringComparison.OrdinalIgnoreCase)
            || (descripcion(m) ?? "").Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Código exacto (código opcional o cuenta) o, si el texto deja un único medio, ese medio.</summary>
    public static (T? Medio, int Coincidencias) Resolver<T>(IEnumerable<T> medios, string? texto,
        Func<T, string?> cuenta, Func<T, string?> codigoOpcional, Func<T, string?> descripcion) where T : class
    {
        var t = (texto ?? "").Trim();
        var filtrados = Filtrar(medios, t, cuenta, codigoOpcional, descripcion);
        var exacto = t.Length == 0 ? null : filtrados.FirstOrDefault(m =>
            string.Equals((codigoOpcional(m) ?? "").Trim(), t, StringComparison.OrdinalIgnoreCase)
            || string.Equals((cuenta(m) ?? "").Trim(), t, StringComparison.OrdinalIgnoreCase));
        return (exacto ?? (filtrados.Count == 1 ? filtrados[0] : null), filtrados.Count);
    }

    /// <summary>Importe tipeado: acepta 1248 · 1248,50 · 1.248,50 · 1248.50 · $ 72.000,00.</summary>
    public static bool TryImporte(string? texto, out decimal importe)
    {
        texto = (texto ?? "").Trim().Replace("$", "").Replace(" ", "");
        return texto.Contains(',')
            ? decimal.TryParse(texto, NumberStyles.Number, CultureInfo.GetCultureInfo("es-AR"), out importe)
            : decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out importe);
    }

    /// <summary>Importe para precargar en el campo (formato argentino sin separador de miles), o vacío si no hay nada.</summary>
    public static string FormatoCarga(decimal importe) => importe > 0 ? importe.ToString("0.00", CultureInfo.GetCultureInfo("es-AR")) : "";
}

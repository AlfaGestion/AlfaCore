using System.Globalization;
using System.Text;

namespace AlfaCore.Services;

/// <summary>
/// Botón "Llamar" de Conversaciones. Con números en coexistence Meta no deja llamar por la API (las llamadas
/// quedan en la app WhatsApp Business), así que AlfaCore abre el chat del cliente en WhatsApp para que el
/// operador llame desde ahí y después anota la llamada en la conversación como nota interna.
/// </summary>
public static class ConversacionLlamadas
{
    /// <summary>TA_CONFIGURACION: "0" apaga el botón; vacío o cualquier otro valor lo deja prendido.</summary>
    public const string ClaveActivo = "CONV_WHATSAPP_LLAMAR_ACTIVO";
    public const string GrupoConfiguracion = "CONVERSACIONES";

    public const string ResultadoAtendio = "ATENDIO";
    public const string ResultadoNoAtendio = "NO_ATENDIO";
    public const string ResultadoOcupado = "OCUPADO";

    public static IReadOnlyList<(string Codigo, string Texto)> Resultados { get; } =
    [
        (ResultadoAtendio, "Atendió"),
        (ResultadoNoAtendio, "No atendió"),
        (ResultadoOcupado, "Ocupado o cortó")
    ];

    public const int NotaMaxima = 500;
    public const int MinutosMaximos = 600;

    public static bool EstaActivo(string? valorConfiguracion)
        => !string.Equals((valorConfiguracion ?? string.Empty).Trim(), "0", StringComparison.Ordinal);

    /// <summary>
    /// Enlace que abre el chat de WhatsApp con ese número (en la app del celular o en WhatsApp de escritorio).
    /// WhatsApp no tiene un enlace oficial que arranque la llamada directo: el operador toca el teléfono en el chat.
    /// Devuelve null si el teléfono no tiene dígitos suficientes para ser un número internacional.
    /// </summary>
    public static string? LinkWhatsApp(string? telefono)
    {
        var digitos = SoloDigitos(telefono);
        return digitos.Length is >= 8 and <= 15 ? $"https://wa.me/{digitos}" : null;
    }

    public static string TextoResultado(string? codigo)
        => Resultados.FirstOrDefault(r => string.Equals(r.Codigo, codigo, StringComparison.OrdinalIgnoreCase)).Texto
           ?? string.Empty;

    /// <summary>Texto de la nota interna: "📞 Llamada hecha · Atendió · 5 min · nota".</summary>
    public static string ArmarNota(bool hecha, string? resultado, int? minutos, string? nota)
    {
        var textoResultado = TextoResultado(resultado);
        if (string.IsNullOrEmpty(textoResultado))
            throw new InvalidOperationException("Elegí cómo fue la llamada.");
        if (minutos is < 0 or > MinutosMaximos)
            throw new InvalidOperationException($"La duración tiene que estar entre 0 y {MinutosMaximos} minutos.");

        var partes = new List<string> { hecha ? "📞 Llamada hecha" : "📞 Llamada recibida", textoResultado };
        if (minutos is > 0)
            partes.Add(minutos.Value.ToString(CultureInfo.InvariantCulture) + " min");

        var notaLimpia = string.Join(' ', (nota ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (notaLimpia.Length > NotaMaxima)
            notaLimpia = notaLimpia[..NotaMaxima];
        if (notaLimpia.Length > 0)
            partes.Add(notaLimpia);

        return string.Join(" · ", partes);
    }

    private static string SoloDigitos(string? valor)
    {
        var sb = new StringBuilder();
        foreach (var c in valor ?? string.Empty)
        {
            if (char.IsAsciiDigit(c))
                sb.Append(c);
        }

        return sb.ToString();
    }
}

using System.Text.RegularExpressions;

namespace AlfaCore.Services;

/// <summary>
/// Lee el contexto que manda Alfa Gestión (VB6) al abrir el chat de soporte (/webchat/soporte?contexto=...),
/// guardado en CONV_CONVERSACIONES.OrigenContexto. Formato: "ALFA GESTION | CLI:112010760 | ANIMAL PRO |
/// USR:Administrador | BASE:...". Viene de un parámetro de la URL: sirve para completar datos internos
/// (ej. el cliente de un ticket), nunca para identificar al visitante ante el asistente ni mostrarle datos privados.
/// </summary>
public static partial class WebChatContextoSoporte
{
    [GeneratedRegex(@"^\s*(?<clave>[A-Za-z]+)\s*:\s*(?<valor>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CampoRegex();

    /// <summary>Valor del campo (ej. "CLI", "USR") o null si el contexto no lo trae.</summary>
    public static string? Campo(string? contexto, string clave)
    {
        if (string.IsNullOrWhiteSpace(contexto) || string.IsNullOrWhiteSpace(clave))
            return null;

        foreach (var parte in contexto.Split('|'))
        {
            var m = CampoRegex().Match(parte);
            if (m.Success && string.Equals(m.Groups["clave"].Value, clave.Trim(), StringComparison.OrdinalIgnoreCase))
                return m.Groups["valor"].Value;
        }
        return null;
    }
}

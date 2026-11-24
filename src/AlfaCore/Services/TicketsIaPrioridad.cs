using AlfaCore.Models;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

/// <summary>
/// Prioridad sugerida por IA para un ticket: arma el contexto (pedido, cliente y últimos mensajes) y lee la
/// respuesta. La IA interpreta el impacto con todo el contexto; no hay reglas por palabras clave. El tipo de
/// cliente no se le pide a la IA porque AlfaCore ya lo suma solo a la prioridad efectiva.
/// </summary>
public static class TicketsIaPrioridad
{
    public const string SystemPrompt = """
        Sos coordinador de una mesa de ayuda de software de gestión. Leés un ticket y decidís con qué urgencia
        hay que atenderlo. Devolvés SOLO JSON válido con esta forma exacta:
        {"prioridad":0,"motivo":"...","resumen":"..."}
        - prioridad: un número de 0 a 3 según el impacto real en el trabajo del cliente:
          3 = el cliente no puede operar o hay riesgo de perder datos, dinero o un vencimiento inmediato;
          2 = una parte del trabajo está trabada o con errores, aunque haya una forma provisoria de seguir;
          1 = consulta, duda de uso, pedido de cambio o mejora sin apuro;
          0 = no requiere ninguna acción.
        - motivo: una frase corta, en español rioplatense, que explique por qué esa prioridad.
        - resumen: 1 o 2 frases con lo que necesita el cliente, en español rioplatense.
        Interpretá el pedido completo y la conversación; no decidas por palabras sueltas. Si falta información,
        elegí la prioridad más probable y decilo en el motivo. No tengas en cuenta si el cliente es importante:
        eso lo calcula el sistema aparte. No inventes datos.
        """;

    /// <summary>Texto que recibe la IA. Las descripciones vienen en HTML del editor; se pasan a texto.</summary>
    public static string ArmarContexto(string titulo, string? descripcionHtml, string? cliente,
        IReadOnlyList<(bool DelCliente, string Texto)> mensajes)
    {
        var sb = new StringBuilder();
        sb.Append("Título: ").AppendLine(Recortar(titulo, 300));
        var descripcion = InformeHtmlSanitizer.ATexto(descripcionHtml);
        if (descripcion.Length > 0)
            sb.AppendLine().AppendLine("Descripción:").AppendLine(Recortar(descripcion, 3000));
        if (!string.IsNullOrWhiteSpace(cliente))
            sb.AppendLine().Append("Cliente: ").AppendLine(cliente.Trim());

        var conTexto = mensajes.Where(m => !string.IsNullOrWhiteSpace(m.Texto)).ToList();
        if (conTexto.Count > 0)
        {
            sb.AppendLine().AppendLine("Conversación (de la más vieja a la más nueva):");
            foreach (var (delCliente, texto) in conTexto)
                sb.Append(delCliente ? "Cliente: " : "Nosotros: ").AppendLine(Recortar(texto.Trim(), 600));
        }
        return sb.ToString().Trim();
    }

    /// <summary>Lee la respuesta de la IA. Null si no vino un JSON con prioridad válida.</summary>
    public static TicketSugerenciaIaDto? Parsear(string? respuesta)
    {
        if (string.IsNullOrWhiteSpace(respuesta))
            return null;
        var inicio = respuesta.IndexOf('{');
        var fin = respuesta.LastIndexOf('}');
        if (inicio < 0 || fin <= inicio)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(respuesta[inicio..(fin + 1)]);
            var root = doc.RootElement;
            if (!root.TryGetProperty("prioridad", out var p))
                return null;
            int prioridad;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
                prioridad = n;
            else if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s))
                prioridad = s;
            else
                return null;

            return new TicketSugerenciaIaDto
            {
                Disponible = true,
                Prioridad = Math.Clamp(prioridad, 0, 3),
                Motivo = Texto(root, "motivo"),
                Resumen = Texto(root, "resumen")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Texto(JsonElement root, string nombre)
        => root.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty).Trim() : string.Empty;

    private static string Recortar(string? texto, int largo)
    {
        var limpio = (texto ?? string.Empty).Trim();
        return limpio.Length <= largo ? limpio : limpio[..largo] + "…";
    }
}

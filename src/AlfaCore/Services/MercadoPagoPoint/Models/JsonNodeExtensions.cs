using System.Text.Json.Nodes;

namespace AlfaCore.Services.MercadoPagoPoint.Models;

/// <summary>Equivalente mínimo de Newtonsoft.Json.Linq.JObject.SelectToken("a.b.c") para
/// System.Text.Json.Nodes, que no trae un helper de path con puntos.</summary>
internal static class JsonNodeExtensions
{
    public static JsonNode? SelectPath(this JsonNode? node, string path)
    {
        var current = node;
        foreach (var segment in path.Split('.'))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out var next))
                return null;

            current = next;
        }

        return current;
    }

    public static string? GetStringOrNull(this JsonNode? node, string propertyName)
    {
        if (node is not JsonObject obj || !obj.TryGetPropertyValue(propertyName, out var value) || value is null)
            return null;

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            return text;

        return value.ToJsonString().Trim('"');
    }

    public static string GetStringOrEmpty(this JsonNode? node, string propertyName)
        => GetStringOrNull(node, propertyName) ?? string.Empty;

    /// <summary>Convierte un JsonNode "hoja" (normalmente devuelto por SelectPath) a texto plano, sin
    /// las comillas que JsonNode.ToString() deja para valores string.</summary>
    public static string? AsRawString(this JsonNode? node)
    {
        if (node is null)
            return null;

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            return text;

        return node.ToJsonString().Trim('"');
    }
}

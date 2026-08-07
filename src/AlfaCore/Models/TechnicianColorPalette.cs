namespace AlfaCore.Models;

public static class TechnicianColorPalette
{
    private static readonly string[] GeneratedColors =
    [
        "#f97316",
        "#a855f7",
        "#f472b6",
        "#f59e0b",
        "#84cc16",
        "#fb7185"
    ];

    public static string Resolve(string? idTecnico, string? tecnicoNombre = null)
    {
        var fixedColor = ResolveFixed(idTecnico, tecnicoNombre);
        if (!string.IsNullOrWhiteSpace(fixedColor))
            return fixedColor;

        var value = FirstNonEmpty(idTecnico, tecnicoNombre);
        if (string.IsNullOrWhiteSpace(value))
            return GeneratedColors[0];

        var hash = value.Aggregate(0, static (current, ch) => unchecked((current * 31) + ch));
        return GeneratedColors[(int)((uint)hash % (uint)GeneratedColors.Length)];
    }

    public static string ResolveFixed(string? idTecnico, string? tecnicoNombre = null)
    {
        var value = Normalize($"{tecnicoNombre} {idTecnico}");
        return value switch
        {
            var x when x.Contains("FAVIO", StringComparison.Ordinal) => "#ef4444",
            var x when x.Contains("EVELYN", StringComparison.Ordinal) => "#8b5cf6",
            var x when x.Contains("FRANCO", StringComparison.Ordinal) => "#15803d",
            var x when x.Contains("ALBERTO", StringComparison.Ordinal) => "#f97316",
            var x when x.Contains("MARCOS", StringComparison.Ordinal) => "#06b6d4",
            var x when x.Contains("DANIEL", StringComparison.Ordinal) => "#a3e635",
            _ => string.Empty
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();
}

using System.Globalization;
using System.Text;

namespace AlfaCore.Services;

public static class AuditDataProtector
{
    public const int MaxChangeValueLength = 2000;
    public const int MaxMetadataValueLength = 1000;

    private static readonly string[] SensitiveTerms =
    [
        "password", "contrasena", "token", "secret", "connectionstring",
        "apikey", "cookie", "session", "credential", "credencial", "sql", "stacktrace"
    ];

    public static bool IsSensitiveName(string? name)
    {
        var normalized = NormalizeName(name);
        return SensitiveTerms.Any(normalized.Contains);
    }

    public static string? ProtectValue(string? fieldName, string? value, int maxLength)
    {
        if (IsSensitiveName(fieldName))
            return null;
        if (value is null)
            return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var buffer = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                buffer.Append(char.ToLowerInvariant(character));
        }
        return buffer.ToString();
    }
}

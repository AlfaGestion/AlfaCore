using System.Text;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public static class SearchTextHelper
{
    public const string AccentInsensitiveCollation = "Latin1_General_CI_AI";

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim(), @"\s+", " ");

    public static string LikeContains(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? string.Empty : $"%{normalized}%";
    }

    public static string? LikeContainsOrNull(string? value)
    {
        var like = LikeContains(value);
        return like.Length == 0 ? null : like;
    }

    public static string DigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    public static string? PhoneTail(string? value)
    {
        var digits = DigitsOnly(value);
        return digits.Length >= 10 ? digits[^10..] : null;
    }

    public static string SqlNormalizePhone(string columnName)
        => $"REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL({columnName}, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', ''), '.', '')";

    public static string SqlPhoneEquivalentPredicate(string columnName, string phoneParameterName, string tailParameterName)
    {
        var normalizedColumn = SqlNormalizePhone(columnName);
        return $"""
            (
                {normalizedColumn} = {phoneParameterName}
                OR (
                    {tailParameterName} IS NOT NULL
                    AND LEN({normalizedColumn}) >= 10
                    AND RIGHT({normalizedColumn}, 10) = {tailParameterName}
                )
            )
            """;
    }
}

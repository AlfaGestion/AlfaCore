using Microsoft.Data.SqlClient;

namespace AlfaCore.Configuration;

internal static class StartupConnectionResolver
{
    public static string? Resolve(string[] args, IConfiguration configuration)
    {
        var argumentValues = ParseArguments(args);
        var configuredConnectionString = configuration.GetConnectionString("AlfaGestion");
        var configuredBuilder = TryCreateBuilder(configuredConnectionString);

        var server = GetValue(argumentValues, "server") ?? configuredBuilder?.DataSource;
        var database = GetValue(argumentValues, "dbname") ?? configuredBuilder?.InitialCatalog;
        var user = GetValue(argumentValues, "usuario") ?? configuredBuilder?.UserID;
        var password = GetValue(argumentValues, "password") ?? configuredBuilder?.Password;

        if (!IsComplete(server, database, user, password))
        {
            return configuredConnectionString;
        }

        var builder = configuredBuilder ?? new SqlConnectionStringBuilder();
        builder.DataSource = server!;
        builder.InitialCatalog = database!;
        builder.UserID = user!;
        builder.Password = password!;
        builder.TrustServerCertificate = true;

        if (string.IsNullOrWhiteSpace(builder.ApplicationName))
        {
            builder.ApplicationName = "AlfaCore";
        }

        if (builder.IntegratedSecurity)
        {
            builder.IntegratedSecurity = false;
        }

        return builder.ConnectionString;
    }

    private static Dictionary<string, string> ParseArguments(IEnumerable<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var raw = string.Join(" ", args).Trim();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return values;
        }

        var normalized = raw.Replace("alfa@", string.Empty, StringComparison.OrdinalIgnoreCase);
        var segments = normalized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            var cleaned = segment.Trim();
            if (cleaned.StartsWith('@'))
            {
                cleaned = cleaned[1..];
            }

            var separatorIndex = cleaned.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = cleaned[..separatorIndex].Trim();
            var value = cleaned[(separatorIndex + 1)..].Trim();

            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static SqlConnectionStringBuilder? TryCreateBuilder(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            return new SqlConnectionStringBuilder(connectionString);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool IsComplete(string? server, string? database, string? user, string? password)
        => !string.IsNullOrWhiteSpace(server)
        && !string.IsNullOrWhiteSpace(database)
        && !string.IsNullOrWhiteSpace(user)
        && !string.IsNullOrWhiteSpace(password);

}

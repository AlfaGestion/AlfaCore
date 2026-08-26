using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class SqlIntegrationFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "ALFACORE_ES_SQL_TEST_CONNECTION";

    public SqlIntegrationFactAttribute()
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Skip = $"Requiere {EnvironmentVariable} apuntando a una ALFA_CENTRAL aislada de test.";
            return;
        }

        try
        {
            var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            if (string.IsNullOrWhiteSpace(database)
                || !(database.Contains("TEST", StringComparison.OrdinalIgnoreCase)
                     || database.Contains("DEV", StringComparison.OrdinalIgnoreCase)
                     || database.Contains("LOCAL", StringComparison.OrdinalIgnoreCase)))
            {
                Skip = "La base de integration test debe incluir TEST, DEV o LOCAL en Initial Catalog.";
            }
        }
        catch
        {
            Skip = $"{EnvironmentVariable} no contiene una connection string SQL válida.";
        }
    }
}

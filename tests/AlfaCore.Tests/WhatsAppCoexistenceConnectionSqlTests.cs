using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppCoexistenceConnectionSqlTests
{
    [SqlIntegrationFact]
    public async Task DefaultWebFallbackPreservesDisconnectedMetaConversationBindings()
    {
        var connectionString = Environment.GetEnvironmentVariable(SqlIntegrationFactAttribute.EnvironmentVariable)!;
        Assert.Equal("ALFA_CENTRAL_TEST", new SqlConnectionStringBuilder(connectionString).InitialCatalog.ToUpperInvariant());
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln"))) directory = directory.Parent;
        var source = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var method = source.IndexOf("private async Task ReassignStaleWhatsAppConversationsToDefaultWebAsync", StringComparison.Ordinal);
        var start = source.IndexOf("const string sql = \"\"\"", method, StringComparison.Ordinal) + "const string sql = \"\"\"".Length;
        var end = source.IndexOf("\"\"\";", start, StringComparison.Ordinal);
        var sql = source[start..end].Replace("dbo.CONV_CONVERSACIONES", "#Conversations").Replace("dbo.CONV_WHATSAPP_NUMEROS", "#Numbers");
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await cn.ExecuteAsync("""
            CREATE TABLE #Numbers (IdNumero int, PhoneNumberId nvarchar(80), WebInstanceName nvarchar(80), WebSessionStatus nvarchar(30));
            CREATE TABLE #Conversations (IdConversacion bigint, Canal nvarchar(20), CodigoEstado nvarchar(20), IdNumeroWhatsApp int NULL, FechaHora_Modificacion datetime NULL);
            INSERT #Numbers VALUES (25, N'100001', N'', N'DISCONNECTED'), (26, N'100002', N'', NULL),
                (1, N'WEB_1', N'web-1', N'CONNECTED'), (2, N'WEB_2', N'web-2', N'DISCONNECTED');
            INSERT #Conversations VALUES (10, N'WHATSAPP', N'ABIERTA', 25, NULL), (11, N'WHATSAPP', N'ABIERTA', NULL, NULL),
                (12, N'WHATSAPP', N'ABIERTA', 2, NULL), (13, N'WHATSAPP', N'ABIERTA', 26, NULL);
            """);
        await cn.ExecuteAsync(sql, new { IdNumero = 1, IdConversacion = (long?)null });
        Assert.Equal(new[] { 25, 1, 1, 26 }, (await cn.QueryAsync<int>("SELECT IdNumeroWhatsApp FROM #Conversations ORDER BY IdConversacion")).ToArray());
        await cn.ExecuteAsync(sql, new { IdNumero = 1, IdConversacion = (long?)10 });
        Assert.Equal(25, await cn.QuerySingleAsync<int>("SELECT IdNumeroWhatsApp FROM #Conversations WHERE IdConversacion=10"));
    }

    [SqlIntegrationFact]
    public async Task MigrationIsRepeatable_AndAccountEventsCannotUndoNewerState()
    {
        // Guardia adicional: este test aplica DDL únicamente en ALFA_CENTRAL_TEST explícita.
        var connectionString = Environment.GetEnvironmentVariable(SqlIntegrationFactAttribute.EnvironmentVariable)!;
        Assert.Equal("ALFA_CENTRAL_TEST", new SqlConnectionStringBuilder(connectionString).InitialCatalog.ToUpperInvariant());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsAppEmbeddedSignup:CentralConnectionString"] = connectionString
        }).Build();
        var store = new WhatsAppCoexistenceConnectionStore(configuration);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln"))) directory = directory.Parent;
        var sql = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "docs", "base-datos", "sql-referencia",
            "2026-11-11-001__alfa_central_whatsapp_coexistence_reconexion.sql"));
        await using var cn = new SqlConnection(connectionString);
        await cn.ExecuteAsync(sql);
        await cn.ExecuteAsync(sql);
        const int idBase = 1900000001;
        var waba = "99998" + DateTime.UtcNow.Ticks;
        var now = DateTime.UtcNow;
        try
        {
            await store.ApplyAsync(idBase, new(waba, "ACCOUNT_OFFBOARDED", "", now));
            Assert.Equal("RECOVERING", (await store.GetAsync(idBase, waba))!.State);
            await store.ApplyAsync(idBase, new(waba, "ACCOUNT_RECONNECTED", "", now.AddSeconds(20)));
            await store.ApplyAsync(idBase, new(waba, "PARTNER_REMOVED", "PRIMARY_INACTIVITY", now.AddSeconds(10)));
            Assert.Equal("CONNECTED", (await store.GetAsync(idBase, waba))!.State);
            await store.ApplyAsync(idBase, new(waba, "PARTNER_REMOVED", "PRIMARY_INACTIVITY", now.AddSeconds(20)));
            await store.ApplyAsync(idBase, new(waba, "ACCOUNT_OFFBOARDED", "", now.AddSeconds(20)));
            await store.ApplyAsync(idBase, new(waba, "ACCOUNT_RECONNECTED", "", now.AddSeconds(20)));
            Assert.Equal("DISCONNECTED", (await store.GetAsync(idBase, waba))!.State);
            await store.ApplyAsync(idBase, new(waba, "ACCOUNT_RECONNECTED", "", now.AddSeconds(30)));
            await store.ApplyAsync(idBase, new(waba, "ACCOUNT_RECONNECTED", "", now.AddSeconds(30)));
            Assert.Equal("CONNECTED", (await store.GetAsync(idBase, waba))!.State);
            Assert.Null(await store.GetAsync(1900000002, waba));
        }
        finally
        {
            await cn.ExecuteAsync("DELETE FROM dbo.WhatsAppCoexistenceConnection WHERE IdBase=@IdBase AND WabaId=@WabaId", new { IdBase = idBase, WabaId = waba });
        }
    }
}

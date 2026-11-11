using System.Text.Json;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed record WhatsAppCoexistenceAccountEvent(string WabaId, string Event, string Reason, DateTime? TimestampUtc)
{
    public string State => Event == "ACCOUNT_RECONNECTED" ? "CONNECTED"
        : Event == "ACCOUNT_OFFBOARDED" ? "RECOVERING" : "DISCONNECTED";
}

public sealed record WhatsAppCoexistenceConnectionStatus(int IdBase, string WabaId, string State, string Reason, DateTime EventTimestampUtc)
{
    public bool BlocksSending => State is "RECOVERING" or "DISCONNECTED";
    public string Message => State == "RECOVERING"
        ? "WhatsApp está recuperando la conexión. Abrí WhatsApp Business en el celular y esperá unos minutos. Tus conversaciones se conservan."
        : Reason == "PRIMARY_INACTIVITY"
            ? "Meta desconectó WhatsApp por inactividad del celular. Abrí WhatsApp Business y usá Reconectar con Meta. Tus conversaciones se conservan."
            : "La conexión con Meta necesita autorización. Usá Reconectar con Meta para recuperar este WhatsApp conservando sus conversaciones.";
}

public static class WhatsAppCoexistenceAccountEvents
{
    // Pistas no confiables usadas SOLO para consultar ownership y elegir el secreto HMAC.
    // Incluye TODOS los account_update, no solo los 4 eventos que cambian el estado: al suscribir el
    // campo, Meta manda por el mismo canal muchos otros (PARTNER_APP_INSTALLED en cada alta,
    // VERIFIED_ACCOUNT, cambios de tier de precios...). Si no se reconocen, el webhook contesta error,
    // Meta reintenta y una tasa alta de fallas puede frenar la entrega de TODOS los avisos de la app.
    public static IReadOnlyList<string> ExtractWabaIds(string rawPayload)
    {
        try
        {
            using var payload = JsonDocument.Parse(rawPayload);
            return ExtractAccountUpdateWabaIds(payload.RootElement);
        }
        catch (JsonException) { return []; }
    }

    /// <summary>WABAs (entry.id numérico) que traen al menos un change account_update bien formado, de cualquier evento.</summary>
    public static IReadOnlyList<string> ExtractAccountUpdateWabaIds(JsonElement root)
    {
        var result = new List<string>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var entry in entries.EnumerateArray())
        {
            var wabaId = Text(entry, "id");
            if (wabaId.Length == 0 || !wabaId.All(char.IsAsciiDigit)
                || !entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                continue;
            if (changes.EnumerateArray().Any(IsWellFormedAccountUpdate) && !result.Contains(wabaId, StringComparer.Ordinal))
                result.Add(wabaId);
        }
        return result;
    }

    /// <summary>Igual que <see cref="IsAccountOnly(JsonElement)"/> sobre el texto crudo (selector previo al HMAC).</summary>
    public static bool IsAccountOnly(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload)) return false;
        try
        {
            using var payload = JsonDocument.Parse(rawPayload);
            return IsAccountOnly(payload.RootElement);
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// true si el aviso trae solo changes account_update bien formados (de cualquier evento, conocido o no).
    /// Un aviso mixto o malformado nunca cuenta como "solo cuenta" y pasa por el guard de teléfonos.
    /// </summary>
    public static bool IsAccountOnly(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return false;
        var total = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var wabaId = Text(entry, "id");
            if (wabaId.Length == 0 || !wabaId.All(char.IsAsciiDigit)
                || !entry.TryGetProperty("changes", out var items) || items.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var change in items.EnumerateArray())
            {
                if (!IsWellFormedAccountUpdate(change)) return false;
                total++;
            }
        }
        return total > 0;
    }

    private static bool IsWellFormedAccountUpdate(JsonElement change)
        => Text(change, "field") == "account_update"
           && change.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object;

    // account_update identifica la WABA por entry.id, no por metadata.phone_number_id.
    public static IReadOnlyList<WhatsAppCoexistenceAccountEvent> Parse(JsonElement root)
    {
        var result = new List<WhatsAppCoexistenceAccountEvent>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var entry in entries.EnumerateArray())
        {
            var wabaId = Text(entry, "id");
            if (entry.ValueKind != JsonValueKind.Object || wabaId.Length == 0 || !wabaId.All(char.IsAsciiDigit)
                || !entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                continue;
            DateTime? timestamp = null;
            if (entry.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.Number && time.TryGetInt64(out var seconds))
            {
                try { timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime; }
                catch (ArgumentOutOfRangeException) { }
            }
            foreach (var change in changes.EnumerateArray())
            {
                if (Text(change, "field") != "account_update" || !change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;
                var eventName = Text(value, "event");
                if (eventName is not ("ACCOUNT_OFFBOARDED" or "ACCOUNT_RECONNECTED" or "PARTNER_REMOVED" or "PARTNER_APP_UNINSTALLED")) continue;
                var reason = "";
                if (value.TryGetProperty("disconnection_info", out var info)) reason = Text(info, "reason");
                if (value.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("disconnection_info", out info)) reason = Text(info, "reason");
                result.Add(new(wabaId, eventName, reason, timestamp));
            }
        }
        return result;
    }

    private static string Text(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? "" : "";
}

public interface IWhatsAppCoexistenceConnectionStore
{
    Task ApplyAsync(int idBase, WhatsAppCoexistenceAccountEvent accountEvent, CancellationToken ct = default);
    Task<WhatsAppCoexistenceConnectionStatus?> GetAsync(int idBase, string wabaId, CancellationToken ct = default);

    /// <summary>
    /// Phone Number IDs de la base conectados alguna vez con Coexistence (según el onboarding que dejó su
    /// credencial). Sirve para ofrecer "Reconectar con Meta" también en números conectados antes de que
    /// existiera la importación inicial del historial.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetCoexistencePhoneNumberIdsAsync(int idBase, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyCollection<string>>([]);
}

public sealed class WhatsAppCoexistenceConnectionStore(IConfiguration configuration, IHostEnvironment? environment = null) : IWhatsAppCoexistenceConnectionStore
{
    private string ConnectionString => WhatsAppEmbeddedSignupConnection.Resolve(configuration, environment);

    public async Task ApplyAsync(int idBase, WhatsAppCoexistenceAccountEvent accountEvent, CancellationToken ct = default)
    {
        if (idBase <= 0 || accountEvent.TimestampUtc is null)
            throw new InvalidOperationException("El evento de cuenta de WhatsApp no tiene base o fecha válida; no se cambió el estado de la conexión.");
        // Idempotente y ordenado por fecha de Meta. A igual fecha, la desconexión prevalece.
        const string sql = """
            MERGE dbo.WhatsAppCoexistenceConnection WITH (HOLDLOCK) AS target
            USING (SELECT @IdBase AS IdBase, @WabaId AS WabaId) AS source
                ON target.IdBase = source.IdBase AND target.WabaId = source.WabaId
            WHEN MATCHED AND (@TimestampUtc > target.EventTimestampUtc
                OR (@TimestampUtc = target.EventTimestampUtc
                    AND CASE @State WHEN 'DISCONNECTED' THEN 2 WHEN 'RECOVERING' THEN 1 ELSE 0 END
                      > CASE target.State WHEN 'DISCONNECTED' THEN 2 WHEN 'RECOVERING' THEN 1 ELSE 0 END)) THEN
                UPDATE SET State=@State, Reason=@Reason, EventTimestampUtc=@TimestampUtc, ModifiedAtUtc=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (IdBase, WabaId, State, Reason, EventTimestampUtc, ModifiedAtUtc)
                VALUES (@IdBase, @WabaId, @State, @Reason, @TimestampUtc, SYSUTCDATETIME());
            """;
        await using var cn = new SqlConnection(ConnectionString);
        var parameters = new DynamicParameters(new { IdBase = idBase, accountEvent.WabaId, accountEvent.State, accountEvent.Reason });
        parameters.Add("TimestampUtc", accountEvent.TimestampUtc.Value, DbType.DateTime2);
        await cn.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    public async Task<IReadOnlyCollection<string>> GetCoexistencePhoneNumberIdsAsync(int idBase, CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT LTRIM(RTRIM(v.PhoneNumberId))
            FROM dbo.WhatsAppSecureVault v
            INNER JOIN dbo.WhatsAppEmbeddedOnboarding o ON o.IdOnboarding = v.IdOnboarding
            WHERE v.IdBase = @IdBase
              AND v.SecretType = 'CREDENTIAL'
              AND o.ModoOnboarding = 'BUSINESS_APP_COEXISTENCE'
              AND LTRIM(RTRIM(ISNULL(v.PhoneNumberId, N''))) <> N'';
            """;
        await using var cn = new SqlConnection(ConnectionString);
        var rows = await cn.QueryAsync<string>(new CommandDefinition(sql, new { IdBase = idBase }, cancellationToken: ct));
        return rows.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<WhatsAppCoexistenceConnectionStatus?> GetAsync(int idBase, string wabaId, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        // Compatibilidad de despliegue: los eventos nuevos requieren el script; una lectura previa
        // a la actualización no debe interrumpir las integraciones ya operativas.
        if (!await cn.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT CAST(CASE WHEN OBJECT_ID('dbo.WhatsAppCoexistenceConnection', 'U') IS NOT NULL THEN 1 ELSE 0 END AS bit)", cancellationToken: ct))) return null;
        return await cn.QuerySingleOrDefaultAsync<WhatsAppCoexistenceConnectionStatus>(new CommandDefinition(
            "SELECT IdBase, WabaId, State, Reason, EventTimestampUtc FROM dbo.WhatsAppCoexistenceConnection WHERE IdBase=@IdBase AND WabaId=@WabaId",
            new { IdBase = idBase, WabaId = wabaId }, cancellationToken: ct));
    }
}

using System.Text.Json.Nodes;
using AlfaCore.Services.MercadoPagoPoint.Models;

namespace AlfaCore.Services.MercadoPagoPoint;

/// <summary>Puerto casi literal de Services/PosService.cs del proyecto original.</summary>
public sealed class PosService(MercadoPagoHttpClient client) : IPosService
{
    public async Task<IReadOnlyList<PosInfo>> ListPosInfosAsync(MercadoPagoPointOptions options, CancellationToken ct)
    {
        var payload = await client.GetAsync("/pos", options.AccessToken, ct);
        var positions = new List<PosInfo>();

        foreach (var item in ExtractItems(payload).OfType<JsonObject>())
        {
            positions.Add(new PosInfo
            {
                Id = item.GetStringOrNull("id") ?? item.GetStringOrNull("pos_id") ?? item.GetStringOrEmpty("external_id"),
                Name = item.GetStringOrNull("name") ?? item.GetStringOrEmpty("external_id"),
                Store = item.SelectPath("store.name").AsRawString() ?? item.GetStringOrNull("store_id") ?? item.GetStringOrEmpty("external_store_id"),
                TerminalId = item.GetStringOrNull("terminal_id") ?? item.SelectPath("terminal.id").AsRawString() ?? string.Empty
            });
        }

        return positions;
    }

    private static IEnumerable<JsonNode?> ExtractItems(JsonObject payload)
    {
        JsonNode?[] candidates =
        [
            payload["results"],
            payload.SelectPath("data.results"),
            payload["data"],
            payload["pos"]
        ];

        foreach (var candidate in candidates)
        {
            if (candidate is JsonArray array)
                return array;
        }

        return [];
    }
}

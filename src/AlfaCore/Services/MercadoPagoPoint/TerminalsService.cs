using System.Text.Json.Nodes;
using AlfaCore.Services.MercadoPagoPoint.Models;

namespace AlfaCore.Services.MercadoPagoPoint;

/// <summary>Puerto casi literal de Services/TerminalsService.cs del proyecto original.</summary>
public sealed class TerminalsService(MercadoPagoHttpClient client) : ITerminalsService
{
    public async Task<IReadOnlyList<TerminalInfo>> ListTerminalInfosAsync(MercadoPagoPointOptions options, CancellationToken ct)
    {
        var payload = await client.GetAsync("/terminals/v1/list", options.AccessToken, ct);
        var terminals = new List<TerminalInfo>();

        if (payload.SelectPath("data.terminals") is not JsonArray terminalsToken)
            return terminals;

        foreach (var token in terminalsToken)
        {
            if (token is not JsonObject item)
                continue;

            terminals.Add(new TerminalInfo
            {
                Id = item.GetStringOrEmpty("id"),
                OperatingMode = item.GetStringOrEmpty("operating_mode"),
                Store = item.SelectPath("store.name").AsRawString() ?? item.SelectPath("store.id").AsRawString() ?? string.Empty,
                Status = item.GetStringOrNull("status") ?? item.SelectPath("state").AsRawString() ?? string.Empty
            });
        }

        return terminals;
    }
}

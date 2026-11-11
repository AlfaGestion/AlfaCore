using System.Text.Json;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public class IaBackendAdjuntosTests
{
    private const long Max30Mb = 30L * 1024 * 1024;
    private static readonly string Png = "data:image/png;base64," + Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6 });

    private static JsonElement Input(int adjuntos)
    {
        var content = new List<object> { new { type = "input_text", text = "leer factura" } };
        for (var i = 0; i < adjuntos; i++)
            content.Add(new { type = "input_image", image_url = Png });
        var json = JsonSerializer.Serialize(new[] { new { role = "user", content } });
        return JsonDocument.Parse(json).RootElement;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void Acepta_hasta_el_maximo(int adjuntos)
        => Assert.Null(IaBackendProxyService.ValidateAttachments(Input(adjuntos), 10, Max30Mb));

    [Fact]
    public void Rechaza_mas_del_maximo_informando_el_maximo()
    {
        var outcome = IaBackendProxyService.ValidateAttachments(Input(11), 10, Max30Mb);
        Assert.NotNull(outcome);
        Assert.Equal(400, outcome!.StatusCode);
        var body = JsonSerializer.Serialize(outcome.Body);
        Assert.Contains("\"error\":\"too_many_attachments\"", body);
        Assert.Contains("\"max\":10", body);
    }

    [Fact]
    public void Rechaza_por_tamano_total()
    {
        var outcome = IaBackendProxyService.ValidateAttachments(Input(3), 10, 10);
        Assert.NotNull(outcome);
        Assert.Contains("attachments_too_large", JsonSerializer.Serialize(outcome!.Body));
    }
}

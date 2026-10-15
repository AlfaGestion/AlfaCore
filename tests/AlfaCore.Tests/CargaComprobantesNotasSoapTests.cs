using System.Net;
using System.Text;
using System.Xml.Linq;
using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CargaComprobantesNotasSoapTests
{
    private static ArcaCaeSolicitudDto Solicitud() => new("20123456789", 1007, 3, 1, 80, "20987654321",
        new(2026, 10, 15), 1, 1, 121, 0, 100, 0, 0, 21, 1, "PES", 1, [new(5, 100, 21)])
        { ComprobantesAsociados = [new(1, 1007, 186, "20123456789", new(2026, 10, 15))] };

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(2)]
    [InlineData(7)]
    public async Task EnvioDeNotaIncluyeFacturaAsociadaSinReintentar(int tipo)
    {
        var handler = new SoapHandler();
        using var services = new ServiceCollection().AddHttpClient("Arca")
            .ConfigurePrimaryHttpMessageHandler(() => handler).Services.BuildServiceProvider();
        var solicitud = Solicitud() with { CbteTipo = tipo };
        var result = await new Wsfev1Client(services.GetRequiredService<IHttpClientFactory>()).SolicitarCaeAsync(
            new("token", "sign", DateTime.UtcNow.AddMinutes(10)), solicitud, ArcaAmbiente.Homologacion, null, default);
        Assert.True(result.Aprobado);
        var xml = XDocument.Parse(handler.Request!);
        var asociado = Assert.Single(xml.Descendants().Where(x => x.Name.LocalName == "CbteAsoc"));
        Assert.Equal(new[] { "Tipo", "PtoVta", "Nro", "Cuit", "CbteFch" }, asociado.Elements().Select(x => x.Name.LocalName));
        Assert.Equal(new[] { "1", "1007", "186", "20123456789", "20261015" }, asociado.Elements().Select(x => x.Value));
        Assert.Equal(1, handler.Llamadas);
    }

    [Fact]
    public void RecuperacionRechazaOtraFacturaAsociadaAunqueLosImportesCoincidan()
    {
        var s = Solicitud();
        CargaComprobantesElectronicaEmisor.ValidarConciliacion(s, s);
        foreach (var distinta in new[] { s with { ComprobantesAsociados = [] },
            s with { ComprobantesAsociados = [s.ComprobantesAsociados[0] with { Numero = 185 }] },
            s with { ComprobantesAsociados = [s.ComprobantesAsociados[0] with { Cuit = "20987654321" }] },
            s with { ComprobantesAsociados = [s.ComprobantesAsociados[0] with { Fecha = new(2026, 10, 14) }] } })
            Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesElectronicaEmisor.ValidarConciliacion(s, distinta));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConsultaLeeAsociadosConCamposOpcionales(bool camposOpcionales)
    {
        var handler = new SoapHandler { Consulta = true, CamposOpcionales = camposOpcionales };
        using var services = new ServiceCollection().AddHttpClient("Arca")
            .ConfigurePrimaryHttpMessageHandler(() => handler).Services.BuildServiceProvider();
        var result = await new Wsfev1Client(services.GetRequiredService<IHttpClientFactory>()).ConsultarComprobanteAsync(
            new("token", "sign", DateTime.UtcNow.AddMinutes(10)), "20123456789", 1007, 3, 1, ArcaAmbiente.Homologacion, null, default);
        var esperado = Solicitud();
        if (!camposOpcionales) esperado = esperado with { ComprobantesAsociados = [new(1, 1007, 186)] };
        CargaComprobantesElectronicaEmisor.ValidarConciliacion(esperado, result.Solicitud);
        Assert.Equal(1, handler.Llamadas);
    }

    private sealed class SoapHandler : HttpMessageHandler
    {
        public string? Request { get; private set; }
        public int Llamadas { get; private set; }
        public bool Consulta { get; init; }
        public bool CamposOpcionales { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = await request.Content!.ReadAsStringAsync(ct); Llamadas++;
            var xml = Consulta ? $"""
                <Envelope><Body><FECompConsultarResult><ResultGet>
                <Resultado>A</Resultado><EmisionTipo>CAE</EmisionTipo><CodAutorizacion>12345678901234</CodAutorizacion><FchVto>20261025</FchVto>
                <PtoVta>1007</PtoVta><CbteTipo>3</CbteTipo><Concepto>1</Concepto><DocTipo>80</DocTipo><DocNro>20987654321</DocNro>
                <CbteFch>20261015</CbteFch><CbteDesde>1</CbteDesde><CbteHasta>1</CbteHasta>
                <ImpTotal>121</ImpTotal><ImpTotConc>0</ImpTotConc><ImpNeto>100</ImpNeto><ImpOpEx>0</ImpOpEx><ImpTrib>0</ImpTrib><ImpIVA>21</ImpIVA>
                <CondicionIVAReceptorId>1</CondicionIVAReceptorId><MonId>PES</MonId><MonCotiz>1</MonCotiz>
                <Iva><AlicIva><Id>5</Id><BaseImp>100</BaseImp><Importe>21</Importe></AlicIva></Iva>
                <CbtesAsoc><CbteAsoc><Tipo>1</Tipo><PtoVta>1007</PtoVta><Nro>186</Nro>
                {(CamposOpcionales ? "<Cuit>20123456789</Cuit><CbteFch>20261015</CbteFch>" : "")}
                </CbteAsoc></CbtesAsoc></ResultGet></FECompConsultarResult></Body></Envelope>
                """ : """
                <Envelope><Body><FECAESolicitarResult><FeDetResp><FECAEDetResponse>
                <Resultado>A</Resultado><CAE>12345678901234</CAE><CAEFchVto>20261025</CAEFchVto>
                </FECAEDetResponse></FeDetResp></FECAESolicitarResult></Body></Envelope>
                """;
            return new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") };
        }
    }
}

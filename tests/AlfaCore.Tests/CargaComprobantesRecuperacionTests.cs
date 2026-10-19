using System.Net;
using System.Reflection;
using System.Text;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesRecuperacionTests
{
    [Fact]
    public void ConciliacionRechazaOtroClienteImporteFechaOIva()
    {
        var s = Solicitud();
        CargaComprobantesElectronicaEmisor.ValidarConciliacion(s, s);
        foreach (var distinta in new[] { s with { DocNro = "12" }, s with { ImpTotal = 99 }, s with { CbteFch = s.CbteFch.AddDays(1) },
            s with { PtoVta = 1010 }, s with { Ivas = [new(5, 101, 20)] } })
            Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesElectronicaEmisor.ValidarConciliacion(s, distinta));
    }

    [Fact]
    public async Task ConsultaSoapRecuperaCaeYNoEnviaAutorizacion()
    {
        var handler = new ConsultaHandler();
        using var provider = new ServiceCollection().AddHttpClient("Arca").ConfigurePrimaryHttpMessageHandler(() => handler).Services.BuildServiceProvider();
        var result = await new Wsfev1Client(provider.GetRequiredService<IHttpClientFactory>()).ConsultarComprobanteAsync(
            new("test", "test", DateTime.UtcNow.AddMinutes(10)), "20123456789", 1007, 1, 185, ArcaAmbiente.Homologacion, null, default);
        Assert.Equal("86410998448850", result.Cae);
        CargaComprobantesElectronicaEmisor.ValidarConciliacion(Solicitud(), result.Solicitud);
        Assert.Equal(1, handler.Llamadas);
    }

    [CargaComprobantesArcaLecturaFact]
    public async Task ConsultaRealRecuperaFactura185SinEmitir()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        var ctx = await new CargaComprobantesRepository().GetContextoAsync(cn, "Administrador", default);
        var emisor = new CargaComprobantesElectronicaEmisor(runtime.Config, runtime.Wsaa, runtime.Wsfe, runtime.Events);
        var resultado = await emisor.RecuperarAsync(cn, ctx, Guid.Parse("f25166b6-e0ec-4d91-a2a1-e7f432aa00fd"), default);
        Assert.Equal("APROBADA", resultado.Estado); Assert.Equal(185, resultado.Numero); Assert.Equal("86410998448850", resultado.Cae);
    }

    [CargaComprobantesSqlFact]
    public async Task CaeAprobadoYFalloDeSesionConservaOperacionSinConsumirNiReemitir()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        var repo = new CargaComprobantesRepository(); var ctx = await repo.GetContextoAsync(cn, "Administrador", default);
        var origen = await repo.GetOrigenAsync(cn, ctx, 432549, default);
        var cantidades = origen.Lineas.Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = x.Pendiente }).ToList();
        var planner = new CargaComprobantesPlanificador(repo);
        var wsaa = DispatchProxy.Create<IWsaaClient, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)wsaa).Handler = (_, _) => Task.FromResult(new WsaaTicket("test", "test", DateTime.UtcNow.AddMinutes(10)));
        var wsfe = DispatchProxy.Create<IWsfev1Client, CargaComprobantesUiTests.EventOnlyProxy>();
        var envios = 0;
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)wsfe).Handler = (method, args) =>
        {
            if (method.Name == "ObtenerUltimoAutorizadoAsync") return Task.FromResult(99999996L);
            Assert.Equal("SolicitarCaeAsync", method.Name); envios++;
            var s = (ArcaCaeSolicitudDto)args![1]!;
            return Task.FromResult(new ArcaCaeResultadoDto(true, "A", s.CbteDesde, s.CbteHasta, "00000000000000", DateTime.Today, [], null));
        };
        var service = new CargaComprobantesEmisionService(planner, new(planner, new()), new(runtime.Config, wsaa, wsfe, runtime.Events), runtime.Config, runtime.Events);
        var plan = await service.ValidarAsync(cn, ctx, origen.Id, 10, cantidades, default);
        var op = Guid.NewGuid(); var validaciones = 0;
        var previo = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE");
        try
        {
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.EmitirAsync(cn, ctx, origen.Id, 10, cantidades,
                Guid.NewGuid(), "VISTA_DESACTUALIZADA", () => { }, default));
            Assert.Equal(0, envios);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.EmitirAsync(cn, ctx, origen.Id, 10, cantidades, op, plan.Huella,
                () => { if (++validaciones > 1) throw new CargaComprobantesReglaException("Prueba: cambió la sesión."); }, default));
            Assert.Equal("PENDIENTE_GRABACION", (await service.GetAsync(cn, ctx, op, default))!.Estado);
            var repetida = await service.EmitirAsync(cn, ctx, origen.Id, 10, cantidades, op, plan.Huella, () => { }, default);
            Assert.Equal("PENDIENTE_GRABACION", repetida.Estado); Assert.Equal(1, envios);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.EmitirAsync(cn, ctx, origen.Id, 10, cantidades,
                Guid.NewGuid(), plan.Huella, () => { }, default));
            Assert.Equal(previo, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTE"));
        }
        finally
        {
            // Solo elimina fixtures con el GUID que este test creó. Ningún comprobante comercial se grabó.
            await cn.ExecuteAsync("DELETE FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION WHERE Operacion=@Op; DELETE FROM dbo.ALFACORE_VENTAS_HOMO_PRUEBA WHERE Operacion=@Op;", new { Op = op });
        }
    }

    private static ArcaCaeSolicitudDto Solicitud() => new("20123456789", 1007, 1, 1, 80, "20123456789", new(2026, 10, 14), 185, 185,
        121, 0, 100, 0, 0, 21, 1, "PES", 1, [new(5, 100, 21)]);
    private sealed class ConsultaHandler : HttpMessageHandler
    {
        public int Llamadas { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Contains("FECompConsultar", await request.Content!.ReadAsStringAsync(ct));
            Assert.DoesNotContain("FECAESolicitar", await request.Content.ReadAsStringAsync(ct)); Llamadas++;
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                <Envelope><Body><FECompConsultarResponse><FECompConsultarResult><ResultGet>
                <Resultado>A</Resultado><EmisionTipo>CAE</EmisionTipo><CodAutorizacion>86410998448850</CodAutorizacion><FchVto>20261024</FchVto>
                <PtoVta>1007</PtoVta><CbteTipo>1</CbteTipo><Concepto>1</Concepto><DocTipo>80</DocTipo><DocNro>20123456789</DocNro>
                <CbteFch>20261014</CbteFch><CbteDesde>185</CbteDesde><CbteHasta>185</CbteHasta>
                <ImpTotal>121</ImpTotal><ImpTotConc>0</ImpTotConc><ImpNeto>100</ImpNeto><ImpOpEx>0</ImpOpEx><ImpTrib>0</ImpTrib><ImpIVA>21</ImpIVA>
                <CondicionIVAReceptorId>1</CondicionIVAReceptorId><MonId>PES</MonId><MonCotiz>1</MonCotiz>
                <Iva><AlicIva><Id>5</Id><BaseImp>100</BaseImp><Importe>21</Importe></AlicIva></Iva>
                </ResultGet></FECompConsultarResult></FECompConsultarResponse></Body></Envelope>
                """, Encoding.UTF8, "text/xml") };
        }
    }
}

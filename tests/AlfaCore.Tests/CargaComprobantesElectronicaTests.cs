using AlfaCore.Models;
using AlfaCore.Services;
using AlfaCore.Repositories;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesElectronicaTests
{
    private static ArcaEmisorConfig Emisor() => new("", "", ArcaAmbiente.Homologacion, 1007, "", "", "1",
        "https://wsaahomo.afip.gov.ar/ws/services/LoginCms?wsdl", "https://wswhomo.afip.gov.ar/wsfev1/service.asmx?WSDL");

    [Fact]
    public void HomologacionRechazaProduccionEndpointProductivoYPuntoDeOtraUnidad()
    {
        var e = Emisor();
        CargaComprobantesElectronicaVerificador.ValidarHomologacion(e, 1007);
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesElectronicaVerificador.ValidarHomologacion(e with { Ambiente = ArcaAmbiente.Produccion }, 1007));
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesElectronicaVerificador.ValidarHomologacion(e with { WsfeUrl = "https://servicios1.afip.gov.ar/wsfev1/service.asmx" }, 1007));
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesElectronicaVerificador.ValidarHomologacion(e with { PuntoVentaElectronico = 1010 }, 1007));
    }

    [Fact]
    public async Task VerificacionConsultaNumeracionSinPedirCae()
    {
        var config = DispatchProxy.Create<IArcaConfigService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)config).Handler = (_, _) => Task.FromResult<ArcaEmisorConfig?>(Emisor());
        var wsaa = DispatchProxy.Create<IWsaaClient, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)wsaa).Handler = (_, _) => Task.FromResult(new WsaaTicket("prueba", "prueba", DateTime.UtcNow.AddMinutes(5)));
        var wsfe = DispatchProxy.Create<IWsfev1Client, CargaComprobantesUiTests.EventOnlyProxy>();
        var tipos = new List<int>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)wsfe).Handler = (method, args) =>
        {
            Assert.Equal("ObtenerUltimoAutorizadoAsync", method.Name);
            tipos.Add((int)args![3]!); return Task.FromResult(25L);
        };
        await using var cn = new SqlConnection(); // Mocks: ninguna consulta SQL ni envío externo.
        var result = await new CargaComprobantesElectronicaVerificador(config, wsaa, wsfe).VerificarAsync(cn, "4", 1007, default);
        Assert.Equal(new[] { 1, 6, 11 }, tipos); Assert.Equal(1007, result.PuntoVenta); Assert.Equal(25L, result.UltimaFacturaA);
    }

    [CargaComprobantesArcaLecturaFact]
    public async Task HomologacionRealAutenticaYConsulta1007SinEmitir()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:AlfaGestion"] = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL") }).Build();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.Items["AlfaCore.ConnectionString"] = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL");
        var env = DispatchProxy.Create<IWebHostEnvironment, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)env).Handler = (method, _) => method.Name switch
        {
            "get_ContentRootPath" => Path.Combine(Path.GetTempPath(), "AlfaCorePruebasArca"), "get_EnvironmentName" => "Development", _ => null
        };
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var aux = new AuxErrRepository(configuration, http, loggerFactory.CreateLogger<AuxErrRepository>());
        var audit = new AppAuditRepository(configuration, http);
        var events = new AppEventService(env, http, aux, audit, new AppAuditActorAccessor { UserName = "PRUEBA_HOMOLOGACION" }, loggerFactory.CreateLogger<AppEventService>());
        var sessions = DispatchProxy.Create<ISessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)sessions).Handler = (method, _) => method.Name == "GetConnectionString" ? Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL") : null;
        using var httpProvider = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var factory = httpProvider.GetRequiredService<IHttpClientFactory>();
        var verifier = new CargaComprobantesElectronicaVerificador(new ArcaConfigService(events, sessions, configuration), new WsaaClient(factory, events), new Wsfev1Client(factory));
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME();"));
        var antes = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MV_CPTE_ELECTRONICOS;");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var estado = await verifier.VerificarAsync(cn, "4", 1007, timeout.Token);
        Assert.Equal("HOMOLOGACION", estado.Ambiente); Assert.Equal(1007, estado.PuntoVenta);
        Assert.Equal(antes, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MV_CPTE_ELECTRONICOS;"));
        Console.WriteLine($"Homologación 1007: últimos A={estado.UltimaFacturaA}, B={estado.UltimaFacturaB}, C={estado.UltimaFacturaC}. Sin emisión.");
    }

    [CargaComprobantesArcaEmisionFact]
    public async Task EmisionRealHomologacionUnaOperacionExplicitaSinConsumirPedido()
    {
        var operacion = Guid.Parse(Environment.GetEnvironmentVariable("ALFACORE_ARCA_OPERACION")!);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:AlfaGestion"] = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL") }).Build();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.Items["AlfaCore.ConnectionString"] = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL");
        var env = DispatchProxy.Create<IWebHostEnvironment, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)env).Handler = (method, _) => method.Name switch
        { "get_ContentRootPath" => Path.Combine(Path.GetTempPath(), "AlfaCorePruebasArca"), "get_EnvironmentName" => "Development", _ => null };
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var events = new AppEventService(env, http,
            new AuxErrRepository(configuration, http, loggerFactory.CreateLogger<AuxErrRepository>()),
            new AppAuditRepository(configuration, http), new AppAuditActorAccessor { UserName = "PRUEBA_HOMOLOGACION" }, loggerFactory.CreateLogger<AppEventService>());
        var sessions = DispatchProxy.Create<ISessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)sessions).Handler = (method, _) => method.Name == "GetConnectionString" ? Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL") : null;
        using var httpProvider = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var factory = httpProvider.GetRequiredService<IHttpClientFactory>();
        var emisor = new CargaComprobantesElectronicaEmisor(new ArcaConfigService(events, sessions, configuration), new WsaaClient(factory, events), new Wsfev1Client(factory), events);
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME();"));
        var repo = new CargaComprobantesRepository();
        var contexto = await repo.GetContextoAsync(cn, "Administrador", default);
        var pagina = await repo.BuscarAsync(cn, contexto, new(), default);
        var origen = await repo.GetOrigenAsync(cn, contexto, pagina.Items.First().Id, default);
        var cantidades = origen.Lineas.Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = x.Pendiente }).ToList();
        var movimientosAntes = await cn.ExecuteScalarAsync<string>("""
            SELECT CONCAT((SELECT COUNT(*) FROM V_MV_CPTE),'/',(SELECT COUNT(*) FROM MV_ASIENTOS),'/',
                (SELECT COUNT(*) FROM V_MV_STOCK),'/',(SELECT COUNT(*) FROM MV_APLICACION));
            """);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var resultado = await emisor.EmitirAsync(cn, contexto, origen, cantidades, operacion, timeout.Token);
        // Consultar la MISMA operación no autoriza otra factura.
        var repetido = await emisor.EmitirAsync(cn, contexto, origen, cantidades, operacion, timeout.Token);
        Assert.Equal(resultado.Numero, repetido.Numero); Assert.Equal(resultado.Cae, repetido.Cae);
        var recargado = await repo.GetOrigenAsync(cn, contexto, origen.Id, default);
        Assert.Equal(origen.Lineas.Select(x => x.Pendiente), recargado.Lineas.Select(x => x.Pendiente));
        Assert.Equal(movimientosAntes, await cn.ExecuteScalarAsync<string>("""
            SELECT CONCAT((SELECT COUNT(*) FROM V_MV_CPTE),'/',(SELECT COUNT(*) FROM MV_ASIENTOS),'/',
                (SELECT COUNT(*) FROM V_MV_STOCK),'/',(SELECT COUNT(*) FROM MV_APLICACION));
            """));
        Console.WriteLine($"Prueba ARCA: estado={resultado.Estado}, origen={origen.Tc} {origen.Comprobante}, tipo={resultado.TipoComprobante}, punto={resultado.PuntoVenta}, numero={resultado.Numero}, total={resultado.Importe}, CAE={resultado.Cae}, vence={resultado.Vencimiento:yyyy-MM-dd}, operación={operacion}.");
        Assert.Equal("APROBADA", resultado.Estado);
    }

    [Fact]
    public void SolicitudDePruebaCuadraBasesIvaYTotal()
    {
        var liquidacion = new CargaComprobantesLiquidacion { Total = 121.01m,
            Alicuotas = [new() { Alicuota = 21, Neto = 100.005m, Iva = 21.0011m }] };
        var solicitud = CargaComprobantesElectronicaEmisor.CrearSolicitud(Emisor(), liquidacion, 1, 1, 80, "0", 185, new DateTime(2026, 10, 14));
        Assert.Equal(solicitud.ImpTotal, solicitud.ImpNeto + solicitud.ImpIva);
        Assert.Equal(1007, solicitud.PtoVta); Assert.Equal(185, solicitud.CbteDesde);
    }
}

public sealed class CargaComprobantesArcaEmisionFactAttribute : FactAttribute
{
    public CargaComprobantesArcaEmisionFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_ARCA_EMITIR") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")) ||
            !Guid.TryParse(Environment.GetEnvironmentVariable("ALFACORE_ARCA_OPERACION"), out var operacion) || operacion == Guid.Empty)
            Skip = "EMISIÓN REAL EN HOMOLOGACIÓN: requiere habilitación explícita y GUID estable; nunca se ejecuta por defecto.";
    }
}

public sealed class CargaComprobantesArcaLecturaFactAttribute : FactAttribute
{
    public CargaComprobantesArcaLecturaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_ARCA_LECTURA") != "1" || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")))
            Skip = "Requiere SQL de pruebas y ALFACORE_TEST_ARCA_LECTURA=1; autentica WSAA y consulta números, sin solicitar CAE.";
    }
}

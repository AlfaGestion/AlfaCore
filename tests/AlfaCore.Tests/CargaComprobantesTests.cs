using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesTests
{
    private static CargaComprobantesOrigen Origen() => new()
    {
        Tc = "RM", Moneda = "1", TieneStock = true,
        Lineas = [new() { Id = 11, Articulo = "PAPEL", CantidadOriginal = 10, Pendiente = 4,
            TotalOriginal = 1210, RenglonesArticulo = 1 }]
    };
    private static CargaComprobantesContexto Contexto() => new() { DescargaStockRemitos = true };

    [Fact]
    public void ParcialPreservaImporteHistoricoYEvitaSegundaDescarga()
    {
        var origen = Origen();
        var result = CargaComprobantesSimulador.Simular(origen, Contexto(), [new() { LineaId = 11, Cantidad = 2 }]);
        Assert.Equal(242m, result.ImporteReferencia);
        Assert.Contains("segunda descarga", result.EfectoStock);
        Assert.Equal(4m, origen.Lineas[0].Pendiente);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void RechazaCantidadNegativaOSuperiorAlPendiente(decimal cantidad)
        => Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesSimulador.Simular(
            Origen(), Contexto(), [new() { LineaId = 11, Cantidad = cantidad }]));

    [Fact]
    public void RechazaRenglonAjenoYSeleccionDuplicada()
    {
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesSimulador.Simular(Origen(), Contexto(), [new() { LineaId = 99, Cantidad = 1 }]));
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesSimulador.Simular(Origen(), Contexto(),
            [new() { LineaId = 11, Cantidad = 1 }, new() { LineaId = 11, Cantidad = 1 }]));
    }

    [Fact]
    public void NoDistribuyePendientesAgregadosEntreRenglonesRepetidos()
    {
        var origen = Origen(); origen.Lineas[0].RenglonesArticulo = 2;
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesSimulador.Simular(origen, Contexto(), [new() { LineaId = 11, Cantidad = 1 }]));
    }

    [Fact]
    public void DevolucionPrevieneAfirmarQueNoSeMueveStock()
    {
        var origen = Origen(); origen.TieneDevolucion = true;
        var result = CargaComprobantesSimulador.Simular(origen, Contexto(), [new() { LineaId = 11, Cantidad = 1 }]);
        Assert.Contains("devolución", result.EfectoStock);
    }

    [Fact]
    public void ReservasYOtrosDetallesRequierenCircuitoCompleto()
    {
        var origen = Origen(); origen.TieneReservas = true;
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesSimulador.Simular(origen, Contexto(), [new() { LineaId = 11, Cantidad = 1 }]));
        origen.TieneReservas = false; origen.TieneOtrosDetalles = true;
        Assert.Throws<CargaComprobantesReglaException>(() => CargaComprobantesSimulador.Simular(origen, Contexto(), [new() { LineaId = 11, Cantidad = 1 }]));
    }

    [CargaComprobantesSqlFact]
    public async Task ConsultaRealPaginadaYSimulacionNoCreanMovimientos()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
        await cn.OpenAsync();
        var repository = new CargaComprobantesRepository();
        var contexto = await repository.GetContextoAsync(cn,
            Environment.GetEnvironmentVariable("ALFACORE_TEST_USUARIO") ?? "Administrador", default);
        var before = await cn.QuerySingleAsync<Counts>("SELECT (SELECT COUNT(*) FROM dbo.V_MV_CPTE) Comprobantes,(SELECT COUNT(*) FROM dbo.MV_ASIENTOS) Asientos;");
        var page = await repository.BuscarAsync(cn, contexto, new() { PageSize = 5 }, default);
        Assert.NotEmpty(page.Items); Assert.InRange(page.Items.Count, 1, 5);
        Assert.True(page.Total >= page.Items.Count);
        var origen = await repository.GetOrigenAsync(cn, contexto, page.Items[0].Id, default);
        Assert.NotEmpty(origen.Lineas);
        Assert.All(origen.Lineas, l => Assert.True(l.Pendiente <= l.CantidadOriginal));
        var cantidades = origen.Lineas.Select(l => new CargaComprobantesCantidad { LineaId = l.Id,
            Cantidad = l.Pendiente > 0 && l.RenglonesArticulo == 1 ? l.Pendiente : 0 }).ToList();
        Assert.Contains(cantidades, c => c.Cantidad > 0);
        var result = CargaComprobantesSimulador.Simular(origen, contexto, cantidades);
        Assert.True(result.LineasSeleccionadas > 0);
        var session = new SessionDto();
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM dbo.TA_USUARIOS WHERE NOMBRE=@Usuario;",
            new { Usuario = contexto.Usuario }));
        var authorized = true;
        var sessions = DispatchProxy.Create<ISessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)sessions).Handler = (method, _) => method.Name switch
        {
            "GetActiveSession" => session,
            "GetConnectionString" => Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"),
            _ => null
        };
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        {
            "get_IsAuthenticated" => true, "IsAuthorizedForSession" => authorized,
            "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null
        };
        var errors = 0;
        var events = DispatchProxy.Create<IAppEventService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)events).Handler = (_, _) => { errors++; return Task.FromResult("test-incident"); };
        var permissions = new PermissionService(new ConfigurationBuilder().Build(), sessions, users, events);
        var service = new CargaComprobantesService(sessions, users, permissions, events, repository,
            new CargaComprobantesElectronicaVerificador(DispatchProxy.Create<IArcaConfigService, CargaComprobantesUiTests.EventOnlyProxy>(),
                DispatchProxy.Create<IWsaaClient, CargaComprobantesUiTests.EventOnlyProxy>(), DispatchProxy.Create<IWsfev1Client, CargaComprobantesUiTests.EventOnlyProxy>()),
            new CargaComprobantesElectronicaEmisor(DispatchProxy.Create<IArcaConfigService, CargaComprobantesUiTests.EventOnlyProxy>(),
                DispatchProxy.Create<IWsaaClient, CargaComprobantesUiTests.EventOnlyProxy>(), DispatchProxy.Create<IWsfev1Client, CargaComprobantesUiTests.EventOnlyProxy>(), events),
            new CargaComprobantesEmisionService(new(repository), new(new(repository), new()),
                new(DispatchProxy.Create<IArcaConfigService, CargaComprobantesUiTests.EventOnlyProxy>(),
                    DispatchProxy.Create<IWsaaClient, CargaComprobantesUiTests.EventOnlyProxy>(), DispatchProxy.Create<IWsfev1Client, CargaComprobantesUiTests.EventOnlyProxy>(), events),
                DispatchProxy.Create<IArcaConfigService, CargaComprobantesUiTests.EventOnlyProxy>(), events),
            DispatchProxy.Create<IFacturaDocumentService, CargaComprobantesUiTests.EventOnlyProxy>(),
            DispatchProxy.Create<IDocumentPdfService, CargaComprobantesUiTests.EventOnlyProxy>());
        var serviceContext = await service.GetContextoAsync();
        Assert.Equal(session.Id, serviceContext.SesionId);
        var talonario = serviceContext.Talonarios.First(x => x.UnidadNegocio.Length == 0 || x.UnidadNegocio == origen.UnidadNegocio);
        var serviceResult = await service.SimularAsync(origen.Id, talonario.Talonario, cantidades);
        Assert.Equal(result.ImporteReferencia, serviceResult.ImporteReferencia);
        var pruebaAnterior = await service.GetUltimaPruebaElectronicaAsync(origen.Id);
        if (pruebaAnterior is not null)
        {
            Assert.Equal(origen.Id, pruebaAnterior.OrigenId);
            Assert.Equal(1007, pruebaAnterior.PuntoVenta);
        }
        var clienteBusqueda = await cn.QueryFirstAsync<(string Codigo, string Documento)>("SELECT TOP(1) LTRIM(RTRIM(CODIGO)),LTRIM(RTRIM(NUMERO_DOCUMENTO)) FROM dbo.VT_CLIENTES WHERE ISNULL(Dada_De_Baja,0)=0 AND ISNULL(BLOQUEO,0)=0 AND LEN(REPLACE(REPLACE(REPLACE(NUMERO_DOCUMENTO,'-',''),'.',''),' ',''))=11 ORDER BY RAZON_SOCIAL,CODIGO");
        var doc = new string(clienteBusqueda.Documento.Where(char.IsAsciiDigit).ToArray());
        var porCuit = await service.BuscarClientesDirectaAsync(doc);
        Assert.Contains(porCuit, x => x.Codigo == clienteBusqueda.Codigo);
        var porCuitFormateado = await service.BuscarClientesDirectaAsync($"{doc[..2]}-{doc[2..10]}-{doc[10..]}");
        Assert.Contains(porCuitFormateado, x => x.Codigo == clienteBusqueda.Codigo);
        Assert.Empty(await service.BuscarClientesDirectaAsync(" "));
        authorized = false;
        await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.GetContextoAsync());
        await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.GetUltimaPruebaElectronicaAsync(origen.Id));
        Assert.Equal(0, errors);
        var after = await cn.QuerySingleAsync<Counts>("SELECT (SELECT COUNT(*) FROM dbo.V_MV_CPTE) Comprobantes,(SELECT COUNT(*) FROM dbo.MV_ASIENTOS) Asientos;");
        Assert.Equal(before.Comprobantes, after.Comprobantes); Assert.Equal(before.Asientos, after.Asientos);
        var otherPage = await repository.BuscarAsync(cn, contexto, new() { PageSize = 5, PageNumber = 2 }, default);
        Assert.DoesNotContain(otherPage.Items, x => page.Items.Any(p => p.Id == x.Id));
    }
    private sealed class Counts { public int Comprobantes { get; set; } public int Asientos { get; set; } }
}

public sealed class CargaComprobantesSqlFactAttribute : FactAttribute
{
    public CargaComprobantesSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")))
            Skip = "Requiere ALFACORE_TEST_SQL apuntando a una base de pruebas relevada.";
    }
}

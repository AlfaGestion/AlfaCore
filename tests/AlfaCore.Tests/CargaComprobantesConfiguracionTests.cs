using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using AlfaCore.Components.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesConfiguracionTests
{
    [Theory]
    [InlineData("MaxDto", "-1")]
    [InlineData("MaxDto", "101")]
    [InlineData("CantidadDecimales", "5")]
    [InlineData("FECHAOPERDESDE", "31/02/2026")]
    [InlineData("PreciosFinales", "true")]
    [InlineData("ARCA_AMBIENTE", "PRODUCCION")]
    [InlineData("ARCA_CERTIFICADO", "secreto")]
    public void RechazaValoresInvalidosYClavesFueraDelEditor(string clave, string valor)
        => Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionCargaComprobantesService.ValidarCambios([new(clave, valor, new string('A', 64))]));

    [Fact]
    public void PreservaClavesDistintasDeProformaYValidaInventarioEmbebido()
    {
        Assert.Contains(ConfiguracionCargaComprobantesService.Definiciones, x => x.Clave == "USAPROFORMA" && x.Tipo == "LECTURA");
        Assert.Contains(ConfiguracionCargaComprobantesService.Definiciones, x => x.Clave == "UsaFacturaProforma" && x.Tipo == "SI_NO");
        var inventario = ConfiguracionCargaComprobantesService.GetInventarioLegacy();
        Assert.True(inventario.Count > 100);
        Assert.Contains(inventario, x => x.Clave == "MaxDto");
        ConfiguracionCargaComprobantesService.ValidarCambios([new("MaxDto", "15,5", new string('A',64))]);
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionCargaComprobantesService.ValidarCambios([
            new("MaxDto", "10", new string('A',64)), new("MaxDto", "20", new string('A',64))]));
    }

    [CargaComprobantesSqlFact]
    public async Task EditorRevalidaVersionAuditaYRevierteSinCambiarConfiguracion()
    {
        var session = new SessionDto();
        var authorized = true;
        var sessions = DispatchProxy.Create<ISessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)sessions).Handler = (m, _) => m.Name switch
        {
            "GetActiveSession" => session, "GetConnectionString" => Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"), _ => null
        };
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (m, _) => m.Name switch
        {
            "get_IsAuthenticated" => true, "IsAuthorizedForSession" => authorized, "GetCurrentUserName" => "Administrador", _ => null
        };
        using var runtime = new CargaComprobantesArcaRuntime();
        var service = new ConfiguracionCargaComprobantesService(sessions, users, runtime.Events);
        var snapshot = await service.GetAsync(); Assert.True(snapshot.PuedeGuardar);
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(service); services.AddSingleton(sessions); services.AddSingleton(users);
        services.AddSingleton<IAppUiOperationService, AppUiOperationService>();
        using (var provider = services.BuildServiceProvider())
        {
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync<ConfiguracionCargaComprobantesPanel>();
                var html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("UsaFacturaProforma", html);
                Assert.Contains("Padrón ARCA / alta por CUIT", html);
                Assert.Contains("MaxDto", html);
                Assert.Contains("Inventario de configuraciones", html);
                Assert.DoesNotContain("ARCA_CERTIFICADO", html);
            });
        }
        var max = snapshot.Valores.Single(x => x.Clave == "MaxDto");
        var otro = snapshot.Valores.Single(x => x.Clave == "RobaIvaProforma");
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        using (var tx = cn.BeginTransaction())
        {
            var method = typeof(ConfiguracionCargaComprobantesService).GetMethod("GuardarEnTransaccionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Task Guardar(IReadOnlyList<ConfiguracionCargaCambio> cambios) => (Task)method.Invoke(service, [cn, tx, session.Id, "Administrador", cambios, CancellationToken.None])!;
            await Guardar([new("MaxDto", "17", max.Version)]);
            Assert.Equal("17", await cn.QuerySingleAsync<string>("SELECT VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE='MaxDto'", transaction: tx));
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => Guardar([new("MaxDto", "18", max.Version)]));
            var conservado = await cn.QuerySingleOrDefaultAsync<string>("SELECT COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux),'') FROM dbo.TA_CONFIGURACION WHERE CLAVE='RobaIvaProforma'", transaction: tx);
            Assert.Equal(otro.Valor, conservado?.Trim() ?? "");
            // Simula revocación sin persistir cambios ni ejecutar otro circuito fiscal.
            await cn.ExecuteAsync("UPDATE dbo.TA_USUARIOS SET AdminCentral=0 WHERE NOMBRE='Administrador'", transaction: tx);
            await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => Guardar([]));
            tx.Rollback();
        }
        var final = await service.GetAsync();
        Assert.Equal(max.Version, final.Valores.Single(x => x.Clave == "MaxDto").Version);
        await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.GuardarAsync(Guid.NewGuid(), []));
        authorized = false;
        await Assert.ThrowsAsync<CargaComprobantesReglaException>(() => service.GetAsync());
    }
}

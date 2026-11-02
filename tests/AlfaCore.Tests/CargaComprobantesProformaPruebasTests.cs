using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>
/// Emisión real de una proforma de contingencia contra ALFANET2007_PRUEBAS: el cliente de
/// ALFACORE_FA_PROFORMA (o el primero en condiciones) se emite como FP aunque esté configurado con FC.
/// Requiere ALFACORE_TEST_SQL, ALFACORE_FA_PRUEBAS=1 y ALFACORE_FA_PROFORMA (cuenta o "auto").
/// </summary>
[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesProformaPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task EmiteLaFacturaAutomaticaComoProformaSinCae()
    {
        var pedido = Environment.GetEnvironmentVariable("ALFACORE_FA_PROFORMA");
        if (string.IsNullOrWhiteSpace(pedido)) { output.WriteLine("ALFACORE_FA_PROFORMA vacío: no se emite."); return; }
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));

        var repo = new CargaComprobantesRepository();
        var planner = new CargaComprobantesPlanificador(repo);
        var sender = new CargaComprobantesElectronicaEmisor(runtime.Config, runtime.Wsaa, runtime.Wsfe, runtime.Events);
        var emision = new CargaComprobantesEmisionService(planner, new(planner, new()), sender, runtime.Config, runtime.Events);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Administrador'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(runtime.Events).AddSingleton<DocumentPdfService>().BuildServiceProvider();
        var pdfService = provider.GetRequiredService<DocumentPdfService>();
        var loggers = provider.GetRequiredService<ILoggerFactory>();
        var docs = new FacturaDocumentService(runtime.Configuration,
            new DocumentTemplateService(runtime.Configuration, runtime.Events, users, loggers.CreateLogger<DocumentTemplateService>(), runtime.Sessions),
            new DocumentRenderer(), pdfService, new ArcaQrService(), runtime.Events, loggers.CreateLogger<FacturaDocumentService>(), runtime.Sessions);
        var service = new CargaComprobantesService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events),
            runtime.Events, repo, new(runtime.Config, runtime.Wsaa, runtime.Wsfe), sender, emision, docs, pdfService);

        var hoy = await cn.ExecuteScalarAsync<DateTime>("SELECT CONVERT(date,GETDATE())");
        var filas = await service.BuscarFacturacionAutomaticaAsync(new CargaComprobantesFiltro { TipoOrigen = CargaComprobantesFacturacionAutomatica.TipoOrigen, FechaFacturacion = hoy });
        var fila = pedido.Equals("auto", StringComparison.OrdinalIgnoreCase) ? filas.First(f => f.Tc == "FC") : filas.Single(f => f.Cuenta == pedido.Trim());
        output.WriteLine($"Cliente {fila.Cuenta} {fila.Cliente} · configurado {fila.Tc} · {fila.Importe:N2}");

        var r = await service.EmitirFacturacionAutomaticaAsync(fila.Cuenta, hoy, comoProforma: true);
        output.WriteLine($"{r.Estado} · {r.Tc} {r.Comprobante} · CAE «{r.Cae}» · total {r.Total:N2} · {r.Mensaje}");
        Assert.Equal("GRABADA", r.Estado);
        Assert.Equal("FP", r.Tc);
        Assert.True(string.IsNullOrEmpty(r.Cae));
        // Ya no aparece para facturar hoy: la proforma actualizó su facturación automática.
        Assert.DoesNotContain(await service.BuscarFacturacionAutomaticaAsync(new CargaComprobantesFiltro { TipoOrigen = CargaComprobantesFacturacionAutomatica.TipoOrigen, FechaFacturacion = hoy }),
            f => f.Cuenta == fila.Cuenta);
        var pdf = await service.GetPdfComprobanteAsync(r.Tc!, r.Comprobante!);
        Assert.True(pdf.Length > 1000);
        if (Environment.GetEnvironmentVariable("ALFACORE_MAIL_PDF") is { Length: > 0 } ruta) await File.WriteAllBytesAsync(ruta, pdf);
    }
}

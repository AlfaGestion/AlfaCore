using System.IO.Compression;
using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class EstudioPaqueteTests
{
    [Theory]
    [InlineData("000100001234A", "0001-00001234A")]
    [InlineData("001500010448B", "0015-00010448B")]
    [InlineData("X/1", "X_1")]
    public void NombreDelComprobante(string id, string esperado) => Assert.Equal(esperado, EstudioPaqueteService.NombreComprobante(id));

    [Fact]
    public void NombresSegurosYPeriodo()
    {
        Assert.Equal("CASA_PALM_S_A", EstudioPaqueteService.Seguro("CASA PALM S.A."));
        Assert.Equal("sin_nombre", EstudioPaqueteService.Seguro("  "));
        Assert.Equal(40, EstudioPaqueteService.Seguro(new string('x', 60)).Length);
        Assert.Equal((new DateTime(2026, 2, 1), new DateTime(2026, 2, 28)), EstudioPaqueteService.Periodo(2026, 2));
        Assert.ThrowsAny<InvalidOperationException>(() => EstudioPaqueteService.Periodo(2026, 13));
    }

    [Fact]
    public void LaDescargaVaPorTokenYVence()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "AlfaCorePaquete_" + Guid.NewGuid().ToString("N"));
        var env = DispatchProxy.Create<IWebHostEnvironment, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)env).Handler = (m, _) => m.Name == "get_ContentRootPath" ? raiz : null;
        try
        {
            var d = new EstudioPaqueteDescargas(env);
            var (ruta, token) = d.Nuevo("Paquete.zip", "u");
            Assert.Null(d.Obtener(token)); // todavía no existe el archivo
            File.WriteAllText(ruta, "x");
            Assert.Equal(("Paquete.zip"), d.Obtener(token)!.Value.Nombre);
            Assert.Null(d.Obtener("../" + token));
            Assert.Null(d.Obtener(new string('a', 48)));
            d.Descartar(token);
            Assert.Null(d.Obtener(token));
            Assert.False(File.Exists(ruta));
        }
        finally { try { Directory.Delete(raiz, true); } catch (IOException) { } }
    }
}

/// <summary>Contra ALFANET2007_PRUEBAS. Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class EstudioPaquetePruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task ArmaElPaqueteDelMes()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Alberto'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(runtime.Events).AddSingleton<DocumentPdfService>().BuildServiceProvider();
        var pdf = provider.GetRequiredService<DocumentPdfService>();
        var loggers = provider.GetRequiredService<ILoggerFactory>();
        var permisos = new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events);
        var docs = new FacturaDocumentService(runtime.Configuration,
            new DocumentTemplateService(runtime.Configuration, runtime.Events, users, loggers.CreateLogger<DocumentTemplateService>(), runtime.Sessions),
            new DocumentRenderer(), pdf, new ArcaQrService(), runtime.Events, loggers.CreateLogger<FacturaDocumentService>(), runtime.Sessions);
        var interfaces = DispatchProxy.Create<IInterfacesService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)interfaces).Handler = (method, _) =>
            method.Name == "GetAttachmentForServeAsync" ? Task.FromResult<InterfacesAdjuntoServeDto?>(null) : null;
        var raiz = Path.Combine(Path.GetTempPath(), "AlfaCorePaquetePruebas");
        var env = DispatchProxy.Create<IWebHostEnvironment, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)env).Handler = (m, _) => m.Name == "get_ContentRootPath" ? raiz : null;
        var descargas = new EstudioPaqueteDescargas(env);
        var svc = new EstudioPaqueteService(runtime.Sessions, users, permisos, runtime.Events,
            new LibroIvaService(runtime.Sessions, users, permisos, runtime.Events, pdf),
            new ContabilidadReportesService(runtime.Sessions, users, permisos, runtime.Events, pdf), docs, interfaces, descargas);

        // Último mes con facturas A.
        var ultima = await cn.QuerySingleAsync<DateTime>("SELECT MAX(FECHA) FROM V_MV_CPTE WHERE TC='FC' AND LETRA='A' AND ISNULL(ANULADA,0)=0 AND FECHA < '20261101'");
        var op = await svc.OpcionesAsync(ultima.Year, ultima.Month, "");
        output.WriteLine($"{ultima:MM/yyyy}: ventas A {op.VentasA} de {op.VentasTotal} · compras con archivo {op.ComprasConArchivo} · unidades {op.Unidades.Count}");
        var pasos = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await svc.GenerarAsync(new EstudioPaqueteSolicitud { Anio = ultima.Year, Mes = ultima.Month },
            new SyncProgress(a => { if (a.Hechos == 0) pasos.Add(a.Paso); }));
        output.WriteLine($"{r.Nombre}: {r.Archivos} archivos, {r.Bytes:N0} bytes en {sw.Elapsed.TotalSeconds:N0} s");
        foreach (var a in r.Avisos) output.WriteLine("  Aviso: " + a);
        var zip = descargas.Obtener(r.Token);
        Assert.NotNull(zip);
        using (var z = ZipFile.OpenRead(zip.Value.Ruta))
        {
            foreach (var g in z.Entries.GroupBy(e => e.FullName.Split('/')[0])) output.WriteLine($"  {g.Key}: {g.Count()} · {string.Join(", ", g.Take(3).Select(e => e.Name))}");
            Assert.Contains(z.Entries, e => e.FullName == "LEEME.txt");
            Assert.Contains(z.Entries, e => e.FullName.StartsWith("01_Libros_IVA/") && e.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(z.Entries, e => e.FullName.StartsWith("03_Asientos/"));
            Assert.Equal(op.VentasA, z.Entries.Count(e => e.FullName.StartsWith("05_Facturas_venta/")) + r.Avisos.Count(a => a.StartsWith("Facturas de venta")));
            Assert.Equal(r.Archivos, z.Entries.Count);
        }
        descargas.Descartar(r.Token);
    }

    private sealed class SyncProgress(Action<EstudioPaqueteAvance> a) : IProgress<EstudioPaqueteAvance> { public void Report(EstudioPaqueteAvance v) => a(v); }
}

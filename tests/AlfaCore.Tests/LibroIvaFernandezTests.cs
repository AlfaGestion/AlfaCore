using System.IO.Compression;
using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class LibroIvaFernandezTests(ITestOutputHelper output)
{
    [FernandezFact]
    public async Task Octubre2026GeneraGeneralYUnidadesYValidaArca()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("FERNANDEZF2", cn.Database);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var user = new AppUserSessionInfo { UserName = "PRUEBA_LIBRO_IVA", SystemCode = "ALFA" };
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(runtime.Events).AddSingleton<DocumentPdfService>().BuildServiceProvider();
        var pdf = provider.GetRequiredService<DocumentPdfService>();
        var loggers = provider.GetRequiredService<ILoggerFactory>();
        var permisos = DispatchProxy.Create<IPermissionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)permisos).Handler = (m, _) => m.Name == "HasAccessAsync" ? Task.FromResult(true) : null;
        var docs = new FacturaDocumentService(runtime.Configuration,
            new DocumentTemplateService(runtime.Configuration, runtime.Events, users, loggers.CreateLogger<DocumentTemplateService>(), runtime.Sessions),
            new DocumentRenderer(), pdf, new ArcaQrService(), runtime.Events, loggers.CreateLogger<FacturaDocumentService>(), runtime.Sessions);
        var interfaces = DispatchProxy.Create<IInterfacesService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)interfaces).Handler = (method, _) =>
            method.Name == "GetAttachmentForServeAsync" ? Task.FromResult<InterfacesAdjuntoServeDto?>(null) : null;
        var raiz = Path.Combine(Path.GetTempPath(), "AlfaCorePaqueteFernandez");
        var env = DispatchProxy.Create<IWebHostEnvironment, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)env).Handler = (m, _) => m.Name == "get_ContentRootPath" ? raiz : null;
        var descargas = new EstudioPaqueteDescargas(env);
        var svc = new EstudioPaqueteService(runtime.Sessions, users, permisos, runtime.Events,
            new LibroIvaService(runtime.Sessions, users, permisos, runtime.Events, pdf),
            new ContabilidadReportesService(runtime.Sessions, users, permisos, runtime.Events, pdf), docs, interfaces, descargas);


        var libros = new LibroIvaService(runtime.Sessions, users, permisos, runtime.Events, pdf);
        var observaciones = new Dictionary<string, int>();
        foreach (var compras in new[] { false, true })
        {
            var r = await libros.GetAsync(new() { Compras = compras, Desde = new(2026, 10, 1), Hasta = new(2026, 10, 31) });
            var esperado = await cn.ExecuteScalarAsync<decimal>("SELECT SUM(ROUND(LIVA_TOTAL,2)) FROM MV_ASIENTOS WHERE LIVA_TIPO=@Tipo AND FECHA>='20261001' AND FECHA<'20261101'", new { Tipo = compras ? "COMPRAS" : "VENTAS" });
            Assert.Equal(esperado, r.Total);
            Assert.Equal(r.Total, r.Unidades.Sum(u => u.Total));
            output.WriteLine($"{(compras ? "Compras" : "Ventas")}: {r.Filas.Count} comprobantes, total {r.Total:N2}, unidades {string.Join(",", r.Unidades.Select(u => u.Codigo))}");
            var errores = LibroIvaArcaValidator.Validar(r);
            observaciones[compras ? "compras" : "ventas"] = errores.Count;
            output.WriteLine($"Validación ARCA: {errores.Count} observaciones");
            foreach (var e in errores.Take(12)) output.WriteLine(e);
        }
        var revisionPrevia = await svc.RevisarAsync(2026, 10, "");
        Assert.Equal(2, revisionPrevia.Controles.Count);
        Assert.All(revisionPrevia.Controles, c => Assert.Contains(c.Estado, new[] { "PENDIENTE", "ACTUALIZAR" }));
        Assert.Equal(3, revisionPrevia.Observaciones.Count(o => o.Tipo == "Posible duplicado"));
        Assert.Equal(2, revisionPrevia.Observaciones.Count(o => o.Tipo == "Diferencia de totales"));
        var resultado = await svc.GenerarAsync(new() { Anio = 2026, Mes = 10, Asientos = false, Detalles = false, FacturasVenta = false, ComprobantesCompra = false });
        var archivo = descargas.Obtener(resultado.Token)!.Value;
        using var zip = ZipFile.OpenRead(archivo.Ruta);
        Assert.NotNull(resultado.Revision);
        Assert.Contains(zip.Entries, e => e.FullName == "00_Revision/Estado_controles_ARCA.txt");
        Assert.Contains(zip.Entries, e => e.FullName == "00_Revision/Duplicados_diferencias_y_observaciones.csv");
        var excel = zip.Entries.Where(e => e.FullName.StartsWith("01_Libros_IVA/") && e.Name.EndsWith(".xlsx")).ToList();
        Assert.Equal(6, excel.Count);
        Assert.Equal(6, zip.Entries.Count(e => e.FullName.StartsWith("01_Libros_IVA/") && e.Name.EndsWith(".pdf")));
        foreach (var (libro, cantidad) in observaciones)
        {
            if (cantidad == 0)
                Assert.Contains(zip.Entries, e => e.FullName.StartsWith("02_Libro_IVA_Digital/") && e.Name.Contains(libro, StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".zip"));
            else
            {
                var informe = Assert.Single(zip.Entries.Where(e => e.Name.StartsWith("Validacion_") && e.Name.Contains(libro)));
                using var lector = new StreamReader(informe.Open());
                Assert.Equal(cantidad, (await lector.ReadToEndAsync()).Split(Environment.NewLine).Length);
                Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("02_Libro_IVA_Digital/") && e.Name.Contains(libro, StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".zip"));
            }
        }
        foreach (var tipo in new[] { "Ventas", "Compras" })
        {
            decimal general = 0, unidades = 0;
            foreach (var e in excel.Where(e => e.Name.Contains(tipo)))
            {
                using var stream = new MemoryStream(); using (var source = e.Open()) source.CopyTo(stream); stream.Position = 0;
                using var wb = new XLWorkbook(stream); var ws = wb.Worksheet(1);
                var total = ws.RowsUsed().Single(row => row.Cell(4).GetString() == "Totales").Cell(16).Value.GetNumber();
                if (e.FullName.Contains("/General/")) { general = (decimal)total; Assert.Contains("General", ws.Cell(1, 1).GetString()); }
                else unidades += (decimal)total;
            }
            Assert.Equal(Math.Round(general, 2), Math.Round(unidades, 2));
        }
        foreach (var aviso in resultado.Avisos) output.WriteLine(aviso);
        foreach (var entry in zip.Entries) output.WriteLine(entry.FullName);
        output.WriteLine("Paquete de prueba: " + archivo.Ruta);
    }
}

public sealed class FernandezFactAttribute : FactAttribute
{
    public FernandezFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ALFACORE_IVA_FERNANDEZF2") != "1") Skip = "Requiere conexión explícita a FERNANDEZF2 y ALFACORE_IVA_FERNANDEZF2=1.";
    }
}

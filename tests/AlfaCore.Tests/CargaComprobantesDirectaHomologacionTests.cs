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

/// <summary>Prueba explícita con efectos persistentes, identificados por GUID para no reemitir al repetirla.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesDirectaHomologacionTests(ITestOutputHelper output)
{
    [CargaDirectaHomologacionFact]
    public async Task CircuitoRealFacturaCreditoDebitoYProformaConPdfYRecuperacion()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        var repo = new CargaComprobantesRepository(); var ctx = await repo.GetContextoAsync(cn, "Administrador", default);
        var planner = new CargaComprobantesPlanificador(repo); var sender = new CargaComprobantesElectronicaEmisor(runtime.Config, runtime.Wsaa, runtime.Wsfe, runtime.Events);
        var emission = new CargaComprobantesEmisionService(planner, new(planner, new()), sender, runtime.Config, runtime.Events);

        var session = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (m, _) => m.Name switch
        { "GetActiveSession" => session, "GetConnectionString" => Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"), _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Administrador'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (m, _) => m.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(runtime.Events).AddSingleton<DocumentPdfService>().BuildServiceProvider();
        var pdf = provider.GetRequiredService<DocumentPdfService>(); var logs = provider.GetRequiredService<ILoggerFactory>();
        var docs = new FacturaDocumentService(runtime.Configuration,
            new DocumentTemplateService(runtime.Configuration, runtime.Events, users, logs.CreateLogger<DocumentTemplateService>(), runtime.Sessions),
            new DocumentRenderer(), pdf, new ArcaQrService(), runtime.Events, logs.CreateLogger<FacturaDocumentService>(), runtime.Sessions);
        var service = new CargaComprobantesService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events),
            runtime.Events, repo, new(runtime.Config, runtime.Wsaa, runtime.Wsfe), sender, emission, docs, pdf);
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "src/AlfaCore/AlfaCore.csproj"))) root = Directory.GetParent(root)!.FullName;
        var destino = Path.Combine(root, "artifacts/ventas/homologacion"); Directory.CreateDirectory(destino);
        int? factura = null;
        var resultados = new List<CargaComprobantesGrabacionResultado>();
        foreach (var caso in new[] { (Tc: "FC", Tal: 10, Sufijo: "34"), (Tc: "NC", Tal: 7, Sufijo: "35"),
            (Tc: "ND", Tal: 6, Sufijo: "36"), (Tc: "FP", Tal: 0, Sufijo: "37") })
        {
            var op = Guid.Parse("e282dd4d-88b6-4f15-a12d-4a4a0e7aef" + caso.Sufijo);
            var draft = new CargaComprobantesDirecta { Borrador = op, Tc = caso.Tc, Cliente = "112011541", UnidadNegocio = "4", Deposito = "1",
                FacturaAsociadaId = caso.Tc is "NC" or "ND" ? factura : null,
                Observaciones = "Prueba de carga directa AlfaCore en homologación",
                Lineas = [new() { Articulo = "403", Cantidad = caso.Tc == "FC" ? 2 : 1 }] };
            var resultado = await emission.GetAsync(cn, ctx, op, default);
            if (resultado is null)
            {
                var plan = await emission.ValidarAsync(cn, ctx, 0, caso.Tal, [], default, draft);
                resultado = await emission.EmitirAsync(cn, ctx, 0, caso.Tal, [], op, plan.Huella, () => { }, default, draft);
            }
            else if (resultado.Estado != "GRABADA") resultado = await emission.RecuperarAsync(cn, ctx, op, () => { }, default);
            Assert.Equal("GRABADA", resultado.Estado); Assert.Equal(caso.Tc, resultado.Tc); Assert.NotNull(resultado.FacturaId);
            if (caso.Tc == "FC") factura = resultado.FacturaId;
            if (caso.Tc == "FP") Assert.Null(resultado.Cae); else Assert.NotEmpty(resultado.Cae!);
            var repetida = await emission.EmitirAsync(cn, ctx, 0, caso.Tal, [], op, "", () => { }, default, draft);
            Assert.Equal(resultado.FacturaId, repetida.FacturaId);
            Assert.Equal(resultado.FacturaId, (await emission.RecuperarAsync(cn, ctx, op, () => { }, default)).FacturaId);
            if (caso.Tc is "NC" or "ND")
                Assert.Equal("APROBADA", (await sender.RecuperarAsync(cn, ctx, op, default)).Estado);
            var bytes = await service.GetPdfFacturaAsync(op); Assert.True(bytes.Length > 1000);
            await File.WriteAllBytesAsync(Path.Combine(destino, $"{caso.Tc}-directa-{resultado.Comprobante}.pdf"), bytes);
            resultados.Add(resultado);
            output.WriteLine($"{caso.Tc} {resultado.Comprobante} · ID {resultado.FacturaId} · asiento {resultado.NumeroAsiento} · CAE {resultado.Cae ?? "no aplica"} · {op}");
        }
        await File.WriteAllTextAsync(Path.Combine(destino, "circuito-carga-directa.json"), System.Text.Json.JsonSerializer.Serialize(resultados,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
    }
}

public sealed class CargaDirectaHomologacionFactAttribute : FactAttribute
{
    public CargaDirectaHomologacionFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_DIRECTA_EMITIR") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")))
            Skip = "Requiere habilitar explícitamente ALFACORE_TEST_DIRECTA_EMITIR=1 en DISTRIWALTERP; conserva los comprobantes de prueba.";
    }
}

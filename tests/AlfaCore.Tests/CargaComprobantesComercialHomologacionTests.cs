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

[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesComercialHomologacionTests(ITestOutputHelper output)
{
    [CargaComprobantesComercialFact]
    public async Task FacturaParcialRealSolicitaUnCaeGrabaTodosLosEfectosYPdfSinDuplicar()
    {
        var op = Guid.Parse(Environment.GetEnvironmentVariable("ALFACORE_COMERCIAL_OPERACION")!);
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        Assert.Equal("DISTRIWALTERP", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        var repo = new CargaComprobantesRepository(); var ctx = await repo.GetContextoAsync(cn, "Administrador", default);
        var planner = new CargaComprobantesPlanificador(repo);
        var sender = new CargaComprobantesElectronicaEmisor(runtime.Config, runtime.Wsaa, runtime.Wsfe, runtime.Events);
        var emision = new CargaComprobantesEmisionService(planner, new(planner, new()), sender, runtime.Config, runtime.Events);
        var anterior = await emision.GetAsync(cn, ctx, op, default);
        var origen = await repo.GetOrigenAsync(cn, ctx, 432549, default);
        // Una unidad de un renglón deja el pedido con pendientes para pruebas manuales posteriores.
        var linea = origen.Lineas.First(x => x.Pendiente > 1 && x.RenglonesArticulo == 1);
        var cantidades = origen.Lineas.Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = x.Id == linea.Id ? 1 : 0 }).ToList();
        var saldo = origen.Lineas.Sum(x => x.Pendiente);
        var plan = await emision.ValidarAsync(cn, ctx, origen.Id, 10, cantidades, default);
        var result = await emision.EmitirAsync(cn, ctx, origen.Id, 10, cantidades, op, plan.Huella, () => { }, default);
        Assert.Equal("GRABADA", result.Estado); Assert.NotNull(result.FacturaId); Assert.NotEmpty(result.Cae!);
        var repetida = await emision.EmitirAsync(cn, ctx, origen.Id, 10, cantidades, op, plan.Huella, () => { }, default);
        Assert.Equal(result.FacturaId, repetida.FacturaId); Assert.Equal(result.Cae, repetida.Cae);
        var actual = await repo.GetOrigenAsync(cn, ctx, origen.Id, default);
        if (anterior is null) Assert.Equal(saldo - 1, actual.Lineas.Sum(x => x.Pendiente));
        var comprobacion = await cn.QuerySingleAsync<Control>("""
            SELECT (SELECT COUNT(*) FROM V_MV_CPTE WHERE ID=@Id) Cabeceras,
                (SELECT COUNT(*) FROM V_MV_CPTEINSUMOS WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante) Detalles,
                (SELECT COUNT(*) FROM V_MV_STOCK WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante) Stock,
                (SELECT COUNT(*) FROM MV_APLICACION WHERE TC='FC' AND IDCOMPROBANTE=@Comprobante) Aplicaciones,
                (SELECT COUNT(*) FROM MV_ASIENTOS WHERE TC='FC' AND SUCURSAL=LEFT(@Comprobante,4)
                    AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA=RIGHT(@Comprobante,1)) Asientos,
                (SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM MV_ASIENTOS
                    WHERE TC='FC' AND SUCURSAL=LEFT(@Comprobante,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA=RIGHT(@Comprobante,1)) Balance;
            """, new { Id = result.FacturaId, result.Comprobante });
        Assert.Equal(1, comprobacion.Cabeceras); Assert.Equal(1, comprobacion.Detalles); Assert.Equal(1, comprobacion.Stock);
        Assert.Equal(1, comprobacion.Aplicaciones); Assert.Equal(3, comprobacion.Asientos); Assert.Equal(0m, comprobacion.Balance);
        Assert.Equal(result.FacturaId, (await emision.RecuperarAsync(cn, ctx, op, () => { }, default)).FacturaId);

        // PDF mediante los servicios productivos de plantillas y reporte, con la guarda del módulo.
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"), _ => null };
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
        var pdf = await service.GetPdfFacturaAsync(op);
        Assert.True(pdf.Length > 1000);
        var dir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(dir, "src/AlfaCore/AlfaCore.csproj"))) dir = Directory.GetParent(dir)!.FullName;
        var destino = Path.Combine(dir, "artifacts/ventas/homologacion", $"factura-comercial-{result.Comprobante}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!); await File.WriteAllBytesAsync(destino, pdf);
        output.WriteLine($"GRABADA: FC {result.Comprobante}, ID {result.FacturaId}, CAE {result.Cae}, asiento {result.NumeroAsiento}, operación {op}. PDF: {destino}");
    }
    private sealed class Control
    {
        public int Cabeceras { get; set; } public int Detalles { get; set; } public int Stock { get; set; }
        public int Aplicaciones { get; set; } public int Asientos { get; set; } public decimal Balance { get; set; }
    }
}

public sealed class CargaComprobantesComercialFactAttribute : FactAttribute
{
    public CargaComprobantesComercialFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_COMERCIAL_EMITIR") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")) ||
            !Guid.TryParse(Environment.GetEnvironmentVariable("ALFACORE_COMERCIAL_OPERACION"), out var op) || op == Guid.Empty)
            Skip = "GRABACIÓN COMERCIAL Y CAE REAL EN HOMOLOGACIÓN: requiere habilitación expresa y GUID estable. No se ejecuta por defecto.";
    }
}

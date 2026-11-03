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
/// Nota de crédito electrónica con otros conceptos contra una factura (C de abono) emitida por AlfaCore, en
/// ALFANET2007_PRUEBAS (homologación): trae los conceptos de la factura, acredita una parte, pide CAE y graba.
/// Requiere ALFACORE_TEST_SQL, ALFACORE_FA_PRUEBAS=1 y ALFACORE_NC_EMITIR=1 (si no, solo valida sin grabar).
/// </summary>
[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesNotaCreditoConceptosPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task NotaDeCreditoConConceptosDeUnaFacturaC()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
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

        // Factura C de abono emitida por AlfaCore en homologación, con otros conceptos y sin notas previas.
        var f = await cn.QueryFirstAsync<(int Id, string Cuenta, string Unidad, string Deposito, string Comprobante, decimal Importe)>("""
            SELECT TOP (1) c.ID, LTRIM(RTRIM(c.CUENTA)), LTRIM(RTRIM(c.UNEGOCIO)), LTRIM(RTRIM(ISNULL(c.IdDeposito,''))), LTRIM(RTRIM(c.IDCOMPROBANTE)), c.IMPORTE
            FROM dbo.V_MV_CPTE c JOIN dbo.ALFACORE_VENTAS_CARGA_OPERACION op ON op.FacturaId=c.ID AND op.Estado='GRABADA'
            JOIN dbo.ALFACORE_VENTAS_HOMO_PRUEBA h ON h.Operacion=op.Operacion AND h.Estado='APROBADA'
            WHERE LTRIM(RTRIM(c.TC))='FC' AND c.LETRA='C' AND ISNULL(c.ANULADA,0)=0
              AND EXISTS (SELECT 1 FROM dbo.V_MV_CPTE_OBSERV o WHERE o.TC=c.TC AND o.IDCOMPROBANTE=c.IDCOMPROBANTE AND o.TIPO_OBS='OC')
              AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_VENTAS_CARGA_OPERACION n WHERE n.FacturaAsociadaId=c.ID)
            ORDER BY c.FECHA DESC, c.ID DESC;
            """);
        output.WriteLine($"Factura {f.Comprobante} · cliente {f.Cuenta} · U{f.Unidad} · {f.Importe:N2}");
        var asociables = await service.BuscarFacturasAsociadasAsync(f.Cuenta, f.Unidad, "");
        Assert.Contains(asociables, x => x.Codigo == f.Id.ToString());
        var conceptos = await service.GetConceptosFacturaAsync(f.Id);
        output.WriteLine("Conceptos: " + string.Join(" | ", conceptos.Select(x => $"{x.Concepto} {x.PrecioUnitarioNeto:N2}")));
        Assert.NotEmpty(conceptos);

        var deposito = f.Deposito.Length > 0 ? f.Deposito : await cn.ExecuteScalarAsync<string>(
            "SELECT TOP(1) LTRIM(RTRIM(IdDeposito)) FROM dbo.V_TA_Deposito WHERE ISNULL(LTRIM(RTRIM(UnidadNegocio)),'') IN ('',@U) ORDER BY CASE WHEN LTRIM(RTRIM(UnidadNegocio))=@U THEN 0 ELSE 1 END", new { U = f.Unidad });
        var talonario = await cn.ExecuteScalarAsync<int>("""
            SELECT TOP(1) Talonario FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='NC' AND LTRIM(RTRIM(SISTEMA))='Ventas'
              AND LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('',@U) AND LETRAS LIKE '%C%' AND C_SUC_DEFAULT=@Pv ORDER BY CASE WHEN LTRIM(RTRIM(UNEGOCIO))=@U THEN 0 ELSE 1 END, Talonario;
            """, new { U = f.Unidad, Pv = int.Parse(f.Comprobante[..4]) });
        // Acredita una parte del primer concepto.
        var linea = conceptos[0];
        linea.PrecioUnitarioNeto = Math.Min(linea.PrecioUnitarioNeto ?? 0, 1000m);
        var draft = new CargaComprobantesDirecta
        {
            Borrador = Guid.NewGuid(), Tc = "NC", Cliente = f.Cuenta, UnidadNegocio = f.Unidad, Deposito = deposito!, FacturaAsociadaId = f.Id,
            Observaciones = "Prueba AlfaCore: NC parcial de abono", Lineas = [linea]
        };
        var plan = await service.ValidarDirectaAsync(draft, talonario);
        output.WriteLine($"Plan NC {plan.Letra} · punto {plan.PuntoVenta} · total {plan.Liquidacion.Total:N2} · asociado {plan.AsociadoComprobante}");
        Assert.Equal("C", plan.Letra);
        Assert.Equal(f.Comprobante, plan.AsociadoComprobante);

        // Supera el saldo: rechazada sin pedir CAE.
        var excedida = new CargaComprobantesDirecta
        {
            Borrador = Guid.NewGuid(), Tc = "NC", Cliente = f.Cuenta, UnidadNegocio = f.Unidad, Deposito = deposito!, FacturaAsociadaId = f.Id,
            Lineas = [new CargaComprobantesDirectaLinea { Concepto = "Excedida", Cantidad = 1, PrecioUnitarioNeto = f.Importe + 100 }]
        };
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => service.ValidarDirectaAsync(excedida, talonario));
        output.WriteLine("Excedida: " + ex.Message);
        Assert.Contains("saldo", ex.Message, StringComparison.OrdinalIgnoreCase);

        if (Environment.GetEnvironmentVariable("ALFACORE_NC_EMITIR") != "1") return;
        var r = await service.GrabarDirectaAsync(draft, talonario, Guid.NewGuid(), plan.Huella);
        output.WriteLine($"Emitida: {r.Estado} · {r.Tc} {r.Comprobante} · CAE {r.Cae} · asiento {r.NumeroAsiento} · {r.Mensaje}");
        Assert.Equal("GRABADA", r.Estado);
        Assert.False(string.IsNullOrWhiteSpace(r.Cae));
        var conceptosNc = await cn.QueryAsync<(string, decimal)>("SELECT CONVERT(nvarchar(500),OBSERVACION), IMPORTE FROM dbo.V_MV_CPTE_OBSERV WHERE TC='NC' AND IDCOMPROBANTE=@Id AND TIPO_OBS='OC'", new { Id = r.Comprobante });
        output.WriteLine("Conceptos grabados: " + string.Join(" | ", conceptosNc.Select(x => $"{x.Item1} {x.Item2:N2}")));
        Assert.NotEmpty(conceptosNc);
    }
}

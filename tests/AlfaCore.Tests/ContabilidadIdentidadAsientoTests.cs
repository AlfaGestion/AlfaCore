using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Regresión de numeración legacy repetida; consultas de solo lectura.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class ContabilidadIdentidadAsientoTests(ITestOutputHelper output)
{
    [FernandezFact]
    public async Task ElControlNoMezclaComprobantesNiDivideSusLineasPorNumeracionLegacy()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("FERNANDEZF2", cn.Database);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (m, _) => m.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (m, _) => m.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => "PRUEBA_CONTABILIDAD", _ => null };
        var permisos = DispatchProxy.Create<IPermissionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)permisos).Handler = (m, _) => m.Name == "HasAccessAsync" ? Task.FromResult(true) : null;
        var svc = new ContabilidadReportesService(runtime.Sessions, users, permisos, runtime.Events, null!);
        ContabilidadFiltro Filtro(DateTime desde, DateTime hasta) => new() { Periodo = 16, Desde = desde, Hasta = hasta };
        var desde = new DateTime(2026, 1, 1); var hasta = new DateTime(2026, 1, 31);
        var r = await svc.GetSumasSaldosAsync(Filtro(desde, hasta));
        Assert.DoesNotContain(r.Desbalanceados, a => a.Tc == "CBCT" && a.Comprobante.Trim() == "0001-00001412 B");
        Assert.DoesNotContain(r.Desbalanceados, a => a.Tc == "FC" && a.Comprobante.Trim() == "0015-00018923 B");
        var esperado = await cn.QuerySingleAsync<(decimal Debe, decimal Haber)>("""
            SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE 0 END),SUM(CASE WHEN [DEBE-HABER]<>'D' THEN IMPORTE ELSE 0 END)
            FROM MV_ASIENTOS WHERE PERIODO='16' AND FECHA>=@desde AND FECHA<DATEADD(day,1,@hasta)
                AND LTRIM(RTRIM(ISNULL(TC,''))) NOT IN ('FP','NCFP','NDFP','CBFP','FPC','NCCP','PGFP');
            """, new { desde, hasta });
        Assert.Equal(esperado.Debe, r.TotalDebe); Assert.Equal(esperado.Haber, r.TotalHaber);
        output.WriteLine("CBCT y FC balanceados excluidos del aviso, aunque repitan o tengan distintos números de asiento.");

        var excel = await svc.ExportarSumasSaldosAsync(Filtro(desde, hasta), false);
        using (var wb = new XLWorkbook(new MemoryStream(excel.Contenido)))
        {
            var hoja = wb.Worksheet("Asientos desbalanceados");
            Assert.Equal("Subdiario", hoja.Cell(1, 9).GetString());
            Assert.DoesNotContain(hoja.RowsUsed(), row => row.Cell(4).GetString() == "FC" && row.Cell(5).GetString().Trim() == "0015-00018923 B");
            Assert.DoesNotContain(hoja.RowsUsed(), row => row.Cell(4).GetString() == "CBCT" && row.Cell(5).GetString().Trim() == "0001-00001412 B");
        }

        var fecha = new DateTime(2026, 1, 2);
        var diario = await svc.GetDiarioAsync(Filtro(fecha, fecha), false);
        var cbct = Assert.Single(diario.Asientos.Where(a => a.Tc == "CBCT" && a.Comprobante.Trim() == "0001-00001412 B"));
        Assert.Equal(2, cbct.Renglones.Count); Assert.Equal(45600m, cbct.Debe); Assert.Equal(cbct.Debe, cbct.Haber); Assert.Equal("CB", cbct.Subdiario);
        var fechaFc = new DateTime(2026, 1, 9);
        var diarioFc = await svc.GetDiarioAsync(Filtro(fechaFc, fechaFc), false);
        var fc = Assert.Single(diarioFc.Asientos.Where(a => a.Tc == "FC" && a.Comprobante.Trim() == "0015-00018923 B"));
        Assert.Equal(3, fc.Renglones.Count); Assert.Equal(1018, fc.Numero); Assert.Equal(10000m, fc.Debe); Assert.Equal(fc.Debe, fc.Haber);
        var fcb = Filtro(fecha, fecha); fcb.Subdiario = "CB";
        var cobros = await svc.GetDiarioAsync(fcb, true);
        Assert.All(cobros.Asientos, a => Assert.Equal("CB", a.Subdiario));
        Assert.Contains(cobros.Asientos, a => a.Comprobante.Trim() == "0001-00001412 B");
        var fdg = Filtro(fecha, fecha); fdg.ExcluirSubdiarios = true;
        var general = await svc.GetDiarioAsync(fdg, false);
        Assert.DoesNotContain(general.Asientos, a => a.Subdiario == "CB");
        Assert.Contains(general.Asientos, a => a.Tc == "FC" && a.Comprobante.Trim() == "0014-00002031 A");
    }
}

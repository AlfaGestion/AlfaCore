using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class FechasCierreTests
{
    [Theory]
    [InlineData("MANUAL", "2026-01-01", "2026-12-31")]
    [InlineData("DIA", "2026-01-01", "2026-11-03")]
    [InlineData("MENOS30", "2026-10-04", "2026-11-03")]
    [InlineData("MENOS60", "2026-09-04", "2026-11-03")]
    public void ModosDeFechaOperativaComoElLegacy(string modo, string desde, string hasta)
    {
        var dto = new FechasCierreDto { ModoOperativa = modo, OperativaDesde = new(2026, 1, 1), OperativaHasta = new(2026, 12, 31) };
        FechasCierreService.AplicarModo(dto, new DateTime(2026, 11, 3));
        Assert.Equal(DateTime.Parse(desde), dto.OperativaDesde);
        Assert.Equal(DateTime.Parse(hasta), dto.OperativaHasta);
    }
}

/// <summary>Contra ALFANET2007_PRUEBAS: grabar sin cambios no altera nada y retroceder un cierre pide confirmación
/// sin grabar. Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class FechasCierrePruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task ReabrirPideConfirmacionYGrabarSinCambiosNoAlteraNada()
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
        var svc = new FechasCierreService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events), runtime.Events);
        const string sqlFechas = "SELECT UPPER(LTRIM(RTRIM(CLAVE))) + '=' + LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('CIERREVENTAS','CIERRECOMPRAS','CIERRECAJA','CIERRESTOCK','FECHAOPERDESDE','FECHAOPERHASTA') ORDER BY 1";
        var antes = (await cn.QueryAsync<string>(sqlFechas)).ToList();
        var d = await svc.GetAsync();
        output.WriteLine($"Ventas {d.CierreVentas:dd/MM/yyyy} · Compras {d.CierreCompras:dd/MM/yyyy} · Caja {d.CierreCaja:dd/MM/yyyy} · Stock {d.CierreStock:dd/MM/yyyy} · operativa {d.OperativaDesde:dd/MM/yyyy}–{d.OperativaHasta:dd/MM/yyyy} ({d.ModoOperativa})");

        // Retroceder el cierre de ventas: devuelve qué se reabre y no graba.
        var reabre = await svc.GuardarAsync(new FechasCierreDto
        {
            CierreVentas = d.CierreVentas!.Value.AddDays(-1), CierreCompras = d.CierreCompras, CierreCaja = d.CierreCaja, CierreStock = d.CierreStock,
            OperativaDesde = d.OperativaDesde, OperativaHasta = d.OperativaHasta, ModoOperativa = "MANUAL"
        }, confirmaReapertura: false);
        output.WriteLine("Reabre: " + string.Join(" | ", reabre));
        Assert.Single(reabre);
        Assert.Equal(antes, (await cn.QueryAsync<string>(sqlFechas)).ToList());

        // Fecha operativa invertida: rechazada.
        await Assert.ThrowsAnyAsync<Exception>(() => svc.GuardarAsync(new FechasCierreDto
        {
            CierreVentas = d.CierreVentas, CierreCompras = d.CierreCompras, CierreCaja = d.CierreCaja, CierreStock = d.CierreStock,
            OperativaDesde = d.OperativaHasta!.Value.AddDays(1), OperativaHasta = d.OperativaHasta, ModoOperativa = "MANUAL"
        }, false));

        // Grabar lo mismo: no cambia ninguna fecha.
        Assert.Empty(await svc.GuardarAsync(d, confirmaReapertura: false));
        Assert.Equal(antes, (await cn.QueryAsync<string>(sqlFechas)).ToList());
    }
}

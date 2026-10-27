using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

[Collection("CargaComprobantes SQL")]
public sealed class ReporteVentasMenuTests
{
    [CargaComprobantesSqlFact]
    public async Task ReparacionIdempotenteHaceVisibleReporteDentroDeVentasSinTocarLegacy()
    {
        await using var cn=new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));await cn.OpenAsync();
        using var tx=cn.BeginTransaction();
        var antes=await cn.QuerySingleAsync<(string Titulo,string Nombre)>("SELECT Titulo,Nombre FROM dbo.TA_MENU WHERE Menu='ALFA' AND Clave='D60RPTWEB'",transaction:tx);
        await cn.ExecuteAsync("UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave=NULL WHERE Menu='ALFA' AND Clave='D60RPTWEB'",transaction:tx);
        Assert.DoesNotContain(await Secciones(cn,tx),s=>s.Items.Any(i=>i.Clave=="D60RPTWEB"));
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null && !File.Exists(Path.Combine(dir.FullName,"AlfaCore.sln")))dir=dir.Parent;
        var sql=await File.ReadAllTextAsync(Path.Combine(dir!.FullName,"src/AlfaCore/App_Data/updates/2026-10-16-001__ventas_reporte_ventas_padre_menu_web.sql"));
        await cn.ExecuteAsync(sql,transaction:tx);await cn.ExecuteAsync(sql,transaction:tx);
        var items=(await Secciones(cn,tx)).SelectMany(s=>s.Items).Where(i=>i.Clave=="D60RPTWEB").ToList();
        Assert.Single(items);Assert.Equal("/ventas/reportes",items[0].RutaWeb);
        var despues=await cn.QuerySingleAsync<(string Titulo,string Nombre)>("SELECT Titulo,Nombre FROM dbo.TA_MENU WHERE Menu='ALFA' AND Clave='D60RPTWEB'",transaction:tx);
        Assert.Equal(antes,despues);
        tx.Rollback();
    }

    private static async Task<IReadOnlyList<ShellWorkspaceSectionDto>> Secciones(SqlConnection cn,SqlTransaction tx)
    {
        var nodes=(await cn.QueryAsync<ShellMenuNodeDto>("SELECT Clave,ISNULL(PadreClave,'') Titulo,NombreWeb Nombre,RutaWeb,OrdenWeb FROM dbo.ALFACORE_MENU_WEB WHERE Menu='ALFA'",transaction:tx)).ToList();
        var parents=nodes.ToDictionary(n=>n.Clave,n=>n.Titulo,StringComparer.OrdinalIgnoreCase);
        var names=nodes.ToDictionary(n=>n.Clave,n=>n.Nombre,StringComparer.OrdinalIgnoreCase);
        return (IReadOnlyList<ShellWorkspaceSectionDto>)typeof(MenuService).GetMethod("BuildSections",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[nodes,parents,names,"D60"])!;
    }
}

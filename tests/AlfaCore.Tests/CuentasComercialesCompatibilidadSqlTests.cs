using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CuentasComercialesCompatibilidadSqlTests
{
    [ReportesCompatibilidadSqlFact]
    public Task ClientesConservanFiltrosOrdenYPaginas() => VerificarAsync(CuentaComercialTipo.Cliente, "VT_CLIENTES");

    [ReportesCompatibilidadSqlFact]
    public Task ProveedoresConservanFiltrosOrdenYPaginas() => VerificarAsync(CuentaComercialTipo.Proveedor, "VT_PROVEEDORES");

    private static async Task VerificarAsync(CuentaComercialTipo tipo, string vista)
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_REPORTES_SQL");
        T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler;
            return proxy;
        }
        var sessions = Proxy<ISessionService>((m, _) => m.Name == "GetConnectionString" ? cs : null);
        var events = Proxy<IAppEventService>((_, _) => Task.FromResult("PRUEBA_SQL"));
        var validator = Proxy<ICuentasComercialesValidator>((_, _) => throw new InvalidOperationException("Esta prueba solo consulta."));
        var service = new CuentasComercialesService(new ConfigurationBuilder().Build(), sessions, events, validator);
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        var filtros = new CuentaComercialFilters { PageSize = 5 };
        var primera = await service.SearchAsync(tipo, filtros);
        var esperados = await cn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{vista} WHERE ISNULL(Dada_De_Baja,0)=0;");
        Assert.Equal(esperados, primera.Total);
        Assert.NotEmpty(primera.Items);
        Assert.All(primera.Items, x => Assert.True(x.Activo));
        filtros.PageNumber = 2;
        var segunda = await service.SearchAsync(tipo, filtros);
        filtros.PageNumber = 1; filtros.PageSize = 10;
        var juntas = await service.SearchAsync(tipo, filtros);
        Assert.Equal(primera.Items.Concat(segunda.Items).Select(x => x.Codigo), juntas.Items.Select(x => x.Codigo));
        Assert.Equal(juntas.Items.Count, juntas.Items.Select(x => x.Codigo).Distinct().Count());
        filtros.Texto = primera.Items[0].Codigo.Trim();
        Assert.Contains((await service.SearchAsync(tipo, filtros)).Items, x => x.Codigo == primera.Items[0].Codigo);
        filtros.Texto = "__ALFACORE_CLIENTE_INEXISTENTE__";
        var vacia = await service.SearchAsync(tipo, filtros);
        Assert.Equal(0, vacia.Total); Assert.Empty(vacia.Items);
        filtros.Texto = ""; filtros.Activo = false;
        var bajas = await service.SearchAsync(tipo, filtros);
        Assert.Equal(await cn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{vista} WHERE ISNULL(Dada_De_Baja,0)<>0;"), bajas.Total);
        Assert.All(bajas.Items, x => Assert.False(x.Activo));
        filtros.Activo = true;
        filtros.ProvinciaCodigo = primera.Items[0].ProvinciaCodigo.Trim();
        filtros.PaisCodigo = primera.Items[0].PaisCodigo.Trim();
        filtros.Bloqueado = primera.Items[0].Bloqueado;
        var filtrada = await service.SearchAsync(tipo, filtros);
        Assert.NotEmpty(filtrada.Items);
        Assert.All(filtrada.Items, x =>
        {
            Assert.Equal(filtros.Bloqueado.Value, x.Bloqueado);
            if (filtros.ProvinciaCodigo.Length > 0) Assert.Equal(filtros.ProvinciaCodigo, x.ProvinciaCodigo.Trim(), ignoreCase: true);
            if (filtros.PaisCodigo.Length > 0) Assert.Equal(filtros.PaisCodigo, x.PaisCodigo.Trim(), ignoreCase: true);
        });
    }
}

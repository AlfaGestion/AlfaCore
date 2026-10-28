using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AlfaCore.Tests;

public sealed class PortalClientePedidosCompatibilidadSqlTests
{
    [ReportesCompatibilidadSqlFact]
    public async Task PedidosConservanClienteOrdenPaginasFechasYNumero()
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_REPORTES_SQL");
        T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
            ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler;
            return proxy;
        }
        var sessions = Proxy<ISessionService>((m, _) => m.Name == "GetConnectionString" ? cs : null);
        var users = Proxy<IAppUserSessionService>((_, _) => "PRUEBA_LECTURA_PORTAL");
        var events = Proxy<IAppEventService>((_, _) => Task.FromResult("PRUEBA_SQL"));
        var service = new PortalClienteService(new ConfigurationBuilder().Build(), sessions, users, events);
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        var cliente = await cn.QuerySingleAsync<string>("""
            SELECT TOP (1) LTRIM(RTRIM(CUENTA)) FROM dbo.V_MV_Cpte
            WHERE UPPER(LTRIM(RTRIM(TC)))='NP' AND LTRIM(RTRIM(SUCURSAL))='9999'
            GROUP BY CUENTA HAVING COUNT(*)>10 ORDER BY COUNT(*) DESC;
            """);
        var filtro = new PortalClientePedidosFiltroDto { CodigoCliente = cliente, PageSize = 5 };
        var primera = await service.GetPedidosClienteAsync(filtro);
        var idsEsperados = (await cn.QueryAsync<int>("""
            SELECT TOP (10) ID FROM dbo.V_MV_Cpte
            WHERE UPPER(LTRIM(RTRIM(TC)))='NP' AND LTRIM(RTRIM(SUCURSAL))='9999'
                AND UPPER(LTRIM(RTRIM(CUENTA)))=UPPER(@Cliente) ORDER BY FECHA DESC,ID DESC;
            """, new { Cliente = cliente })).ToList();
        filtro.PageNumber = 2;
        var segunda = await service.GetPedidosClienteAsync(filtro);
        Assert.Equal(idsEsperados, primera.Items.Concat(segunda.Items).Select(x => x.IdComprobante));
        Assert.Equal(primera.Total, segunda.Total);
        Assert.True(primera.Total > 10);
        var pedido = primera.Items[0];
        filtro.PageNumber = 1;
        filtro.FechaDesde = pedido.Fecha.Date; filtro.FechaHasta = pedido.Fecha.Date;
        var delDia = await service.GetPedidosClienteAsync(filtro);
        Assert.All(delDia.Items, x => Assert.Equal(pedido.Fecha.Date, x.Fecha.Date));
        var cantidad = await cn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.V_MV_Cpte
            WHERE UPPER(LTRIM(RTRIM(TC)))='NP' AND LTRIM(RTRIM(SUCURSAL))='9999'
                AND UPPER(LTRIM(RTRIM(CUENTA)))=UPPER(@Cliente) AND FECHA>=@Desde AND FECHA<@Hasta;
            """, new { Cliente = cliente, Desde = pedido.Fecha.Date, Hasta = pedido.Fecha.Date.AddDays(1) });
        Assert.Equal(cantidad, delDia.Total);
        filtro.Numero = pedido.IdComprobanteTexto;
        Assert.Contains((await service.GetPedidosClienteAsync(filtro)).Items, x => x.IdComprobante == pedido.IdComprobante);
        filtro.CodigoCliente = "__CLIENTE_INEXISTENTE__";
        var otro = await service.GetPedidosClienteAsync(filtro);
        Assert.Equal(0, otro.Total); Assert.Empty(otro.Items);
        filtro.CodigoCliente = cliente; filtro.Numero = "__PEDIDO_INEXISTENTE__";
        Assert.Empty((await service.GetPedidosClienteAsync(filtro)).Items);
    }
}

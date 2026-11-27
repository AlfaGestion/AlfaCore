using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

// Pagos a proveedores con listado (como Cobranzas) y carga de medios por código (como el POS).
public sealed class OrdenPagoListadoTests
{
    private sealed record Medio(string Cuenta, string CodigoOpcional, string Descripcion);
    private static readonly Medio[] Medios = [new("111010001", "EF", "Caja efectivo"), new("111020001", "6385", "Galicia Cuenta Corriente"),
        new("111020002", "", "Galicia Caja de Ahorro"), new("111010002", "CHT", "Caja Cheques 3ros")];

    private static (Medio? Medio, int Coincidencias) Resolver(string texto)
        => MedioPagoBuscador.Resolver(Medios, texto, m => m.Cuenta, m => m.CodigoOpcional, m => m.Descripcion);

    [Fact]
    public void ElMedioSeResuelvePorCodigoOpcionalCuentaODescripcionUnica()
    {
        Assert.Equal("111010001", Resolver("ef").Medio?.Cuenta);          // código opcional exacto, sin importar mayúsculas
        Assert.Equal("111020002", Resolver("111020002").Medio?.Cuenta);   // la cuenta, cuando no tiene código opcional
        Assert.Equal("111020002", Resolver("ahorro").Medio?.Cuenta);      // parte de la descripción que deja uno solo
        var (ambiguo, cantidad) = Resolver("galicia");
        Assert.Null(ambiguo); Assert.Equal(2, cantidad);                  // dos coincidencias: no elige ninguno
        var (inexistente, ninguna) = Resolver("ZZ");
        Assert.Null(inexistente); Assert.Equal(0, ninguna);
    }

    [Theory]
    [InlineData("1248", 1248)]
    [InlineData("1.248,50", 1248.5)]
    [InlineData("1248.50", 1248.5)]
    public void ElImporteTipeadoAceptaFormatoArgentino(string texto, decimal esperado)
    {
        Assert.True(MedioPagoBuscador.TryImporte(texto, out var importe));
        Assert.Equal(esperado, importe);
        Assert.Equal("1248,50", MedioPagoBuscador.FormatoCarga(1248.5m));
        Assert.Equal("", MedioPagoBuscador.FormatoCarga(0));
    }

    /// <summary>Solo lectura (COMPROBANTES_TEST_SQL): la consulta rápida de pendientes de proveedores da lo mismo que la vista
    /// CO_CPTES_IMPAGOS del legacy, para todos los proveedores y para uno solo.</summary>
    [ComprobantesSqlFact]
    public async Task LosPendientesDeProveedoresDanLoMismoQueLaVistaDelLegacy()
    {
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("COMPROBANTES_TEST_SQL")!);
        const string columnas = "CUENTA,FECHA,TC,SUCURSAL,NUMERO,LETRA,[DEBE-HABER],IMPORTE,MONEDA,VENCIMIENTO,CABNOMBRE,SALDO,Estado,UNEGOCIO";
        var proveedor = await cn.ExecuteScalarAsync<string?>("SELECT TOP(1) LTRIM(RTRIM(CUENTA)) FROM dbo.CO_CPTES_IMPAGOS GROUP BY CUENTA ORDER BY COUNT(*) DESC;");
        Assert.False(string.IsNullOrEmpty(proveedor), "La base de prueba no tiene comprobantes de proveedores pendientes.");
        foreach (var filtro in new[] { "", proveedor! })
        {
            var sql = $"""
                SET NOCOUNT ON;
                SELECT {columnas} INTO #viejo FROM dbo.CO_CPTES_IMPAGOS WHERE @proveedor='' OR LTRIM(RTRIM(CUENTA))=@proveedor;
                SELECT {columnas} INTO #nuevo FROM ({OrdenPagoService.ImpagosProveedoresSql}) n;
                SELECT (SELECT COUNT(*) FROM #viejo),
                       (SELECT COUNT(*) FROM (SELECT * FROM #viejo EXCEPT SELECT * FROM #nuevo) x),
                       (SELECT COUNT(*) FROM (SELECT * FROM #nuevo EXCEPT SELECT * FROM #viejo) x);
                """;
            var (total, soloViejo, soloNuevo) = await cn.QuerySingleAsync<(int, int, int)>(new CommandDefinition(sql, new { proveedor = filtro }, commandTimeout: 600));
            Assert.True(total > 0);
            Assert.Equal((0, 0), (soloViejo, soloNuevo));
        }
    }

    /// <summary>Solo lectura: listado, tarjeta «Pagado este mes» y saldo final por proveedor contra la base de prueba.</summary>
    [ComprobantesSqlFact]
    public async Task ElListadoLasTarjetasYElSaldoFinalSeCalculanContraLaBase()
    {
        var connection = Environment.GetEnvironmentVariable("COMPROBANTES_TEST_SQL")!;
        var service = new OrdenPagoService(new PlanCuentasAbmSqlTests.Sesion(connection),
            Proxy<IAppUserSessionService>((m, _) => m.Name switch
            {
                "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => "Administrador", _ => null
            }),
            Proxy<IPermissionService>((_, _) => Task.FromResult(true)), new PlanCuentasAbmSqlTests.Eventos());
        var pendientes = await service.PendientesAsync();
        Assert.NotEmpty(pendientes);
        Assert.All(pendientes, p => { Assert.True(p.Saldo > 0); Assert.False(string.IsNullOrWhiteSpace(p.Proveedor)); });
        Assert.Equal(pendientes.Count, pendientes.Select(p => p.Proveedor + "#" + p.Clave).Distinct().Count());
        var indicadores = await service.IndicadoresAsync();
        Assert.True(indicadores.PagadoMes >= indicadores.PagadoHoy && indicadores.OrdenesMes >= indicadores.OrdenesHoy);
        var saldos = await service.SaldosProveedoresAsync();
        Assert.Contains(saldos, s => s.Proveedor == pendientes[0].Proveedor);
        // La orden del proveedor (que también usa la consulta rápida) trae los mismos comprobantes pendientes que el listado.
        var primero = pendientes.GroupBy(p => p.Proveedor).First(g => g.All(p => !p.Proforma));
        var prep = await service.PrepararAsync(primero.Key, false);
        Assert.Equal(primero.Select(p => p.Clave).Order(), prep.Comprobantes.Select(d => $"{d.Tc}|{d.Comprobante}").Order());
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler; return proxy;
    }
}

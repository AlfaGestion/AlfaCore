using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

// Cobranza a cuenta (anticipo) desde el módulo Cobranzas: recibo CB con cliente elegido, con o sin facturas aplicadas;
// lo no aplicado queda como crédito del cliente, igual que un CB sin aplicar de Alfa Gestión.
public sealed class CobranzaACuentaTests
{
    private static readonly CobranzaACuenta Cliente = new("112010087", "1");

    [Fact]
    public void UnAnticipoSinFacturasSoloSeAdmiteEnCobranzaDirectaConClienteYUnidad()
    {
        var anticipo = new CobranzaSolicitud(Guid.NewGuid(), 0, new(2026, 11, 26), 0, [new("111010001", 500m)], null, null, [], null, Cliente);
        Assert.Equal(500m, CobranzasService.ValidarImportes(anticipo));
        Assert.Empty(CobranzasService.ResolverAplicaciones(anticipo, 500m));
        // Sin cliente elegido sigue exigiendo una factura, como antes.
        Assert.Throws<InvalidOperationException>(() => CobranzasService.ValidarImportes(anticipo with { ACuenta = null }));
        // Con movimiento bancario (conciliación) o con ajuste de centavos no se cobra a cuenta.
        Assert.Throws<InvalidOperationException>(() => CobranzasService.ValidarImportes(anticipo with { MovimientoId = 5 }));
        Assert.Throws<InvalidOperationException>(() => CobranzasService.ValidarImportes(anticipo with { Ajuste = new("512", .5m, "redondeo") }));
        Assert.Throws<InvalidOperationException>(() => CobranzasService.ValidarImportes(anticipo with { ACuenta = new("", "1") }));
        Assert.Throws<InvalidOperationException>(() => CobranzasService.ValidarImportes(anticipo with { ACuenta = new("112010087", "") }));
    }

    [Fact]
    public void ACuentaSePuedeAplicarMenosQueElTotalPeroNuncaMas()
    {
        var parcial = new CobranzaSolicitud(Guid.NewGuid(), 7, new(2026, 11, 26), 0, [new("111010001", 1000m)], null, null, [new(7, 800m)], null, Cliente);
        Assert.Equal(1000m, CobranzasService.ValidarImportes(parcial));
        Assert.Throws<InvalidOperationException>(() => CobranzasService.ValidarImportes(parcial with { Aplicaciones = [new(7, 1000.01m)] }));
        Assert.Throws<InvalidOperationException>(() => CobranzasService.ValidarImportes(parcial with { Aplicaciones = [new(8, 100m)] }));
        // Sin cliente elegido (conciliación y contrato anterior) lo aplicado debe ser exactamente el total.
        Assert.Throws<InvalidOperationException>(() => CobranzasService.ValidarImportes(parcial with { ACuenta = null }));
    }

    /// <summary>
    /// Contra una base de prueba (COMPROBANTES_TEST_SQL): graba un anticipo y un cobro parcial con resto a cuenta dentro de
    /// una transacción que se deshace al final. Verifica que el asiento balancee, que el anticipo no tenga aplicaciones y que
    /// la vista de créditos del legacy lo muestre como saldo a favor del cliente.
    /// </summary>
    [ComprobantesSqlFact]
    public async Task AnticipoYCobroParcialQuedanComoCreditoDelClienteSoloEnTransaccionDeEnsayo()
    {
        var connection = Environment.GetEnvironmentVariable("COMPROBANTES_TEST_SQL")!;
        var service = new CobranzasService(new PlanCuentasAbmSqlTests.Sesion(connection),
            Proxy<IAppUserSessionService>((m, _) => m.Name switch
            {
                "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => "Administrador", _ => null
            }),
            Proxy<IPermissionService>((_, _) => Task.FromResult(true)), new PlanCuentasAbmSqlTests.Eventos());
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        using var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            var doc = await cn.QueryFirstAsync<CobranzaDocumento>("""
                SELECT TOP(1) f.ID Id,LTRIM(RTRIM(f.CUENTA)) Cliente,LTRIM(RTRIM(f.UNEGOCIO)) Unidad,LTRIM(RTRIM(f.TC)) Tc,
                    f.IDCOMPROBANTE Comprobante,s.SALDO Saldo
                FROM dbo.VE_CPTES_SALDOS_VENTAS s JOIN dbo.V_MV_CPTE f
                    ON f.CUENTA=s.CUENTA AND f.TC=s.TC AND f.SUCURSAL=s.SUCURSAL AND f.NUMERO=s.NUMERO AND f.LETRA=s.LETRA
                JOIN dbo.VT_CLIENTES c ON c.CODIGO=f.CUENTA AND ISNULL(c.Dada_De_Baja,0)=0 AND ISNULL(c.TITULO,0)=0
                WHERE f.IDCOMPLEMENTO=0 AND s.SALDO>100 AND ISNULL(f.ANULADA,0)=0 AND LTRIM(RTRIM(f.TC))='FC'
                    AND LTRIM(RTRIM(f.MONEDA))='1' AND LTRIM(RTRIM(f.UNEGOCIO))<>''
                ORDER BY f.ID DESC;
                """, transaction: tx);
            var medio = await cn.QueryFirstAsync<string>("""
                SELECT TOP(1) LTRIM(RTRIM(CODIGO)) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO)) IN ('EF','DB')
                    AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0 AND LTRIM(RTRIM(MONEDA))='1' ORDER BY MEDIODEPAGO DESC,CODIGO;
                """, transaction: tx);
            var talonario = await cn.QueryFirstAsync<int>("""
                SELECT TOP(1) Talonario FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CB'
                    AND LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('','0',@Unidad)
                ORDER BY CASE WHEN LTRIM(RTRIM(UNEGOCIO))=@Unidad THEN 0 ELSE 1 END,Talonario;
                """, doc, tx);
            var cliente = new CobranzaACuenta(doc.Cliente, doc.Unidad);

            // 1) Anticipo puro: ninguna factura.
            var anticipo = new CobranzaSolicitud(Guid.NewGuid(), 0, DateTime.Today, talonario, [new(medio, 123.45m, "ENSAYO")],
                null, "Anticipo de ensayo", [], null, cliente);
            var r1 = await service.GrabarEnTransaccionAsync(cn, tx, anticipo, default);
            Assert.Equal(123.45m, r1.Importe);
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.MV_APLICACION WHERE TC='CB' AND IDCOMPROBANTE=@Comprobante;", r1, tx));
            var cabecera = await cn.QuerySingleAsync<(string Cuenta, string Nombre, string Unidad)>(
                "SELECT LTRIM(RTRIM(CUENTA)),NOMBRE,LTRIM(RTRIM(UNEGOCIO)) FROM dbo.V_MV_CPTE WHERE ID=@CobranzaId;", r1, tx);
            Assert.Equal(doc.Cliente, cabecera.Cuenta); Assert.False(string.IsNullOrWhiteSpace(cabecera.Nombre));
            Assert.Equal(doc.Unidad, cabecera.Unidad);
            Assert.Equal(0m, await Balance(cn, tx, r1));
            Assert.Equal(123.45m, await CreditoPendiente(cn, tx, doc.Cliente, r1));
            // Idempotente: la misma operación no graba dos veces.
            Assert.Equal(r1, await service.GrabarEnTransaccionAsync(cn, tx, anticipo, default));

            // 2) Cobro parcial: aplica 10 a la factura y 20 quedan a cuenta.
            var parcial = new CobranzaSolicitud(Guid.NewGuid(), doc.Id, DateTime.Today, talonario, [new(medio, 30m)],
                null, null, [new(doc.Id, 10m)], null, cliente);
            var r2 = await service.GrabarEnTransaccionAsync(cn, tx, parcial, default);
            Assert.Equal(10m, await cn.ExecuteScalarAsync<decimal>(
                "SELECT SUM(IMPORTE) FROM dbo.MV_APLICACION WHERE TC='CB' AND IDCOMPROBANTE=@Comprobante;", r2, tx));
            Assert.Equal(0m, await Balance(cn, tx, r2));
            Assert.Equal(20m, await CreditoPendiente(cn, tx, doc.Cliente, r2));

            // 3) Facturas de otro cliente no se aplican a un recibo a cuenta de este.
            var otro = await cn.QueryFirstOrDefaultAsync<int?>(
                "SELECT TOP(1) ID FROM dbo.V_MV_CPTE WHERE IDCOMPLEMENTO=0 AND TC='FC' AND LTRIM(RTRIM(CUENTA))<>@Cliente;", doc, tx);
            if (otro is int otroId)
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrabarEnTransaccionAsync(cn, tx,
                    parcial with { Operacion = Guid.NewGuid(), FacturaId = otroId, Aplicaciones = [new(otroId, 10m)] }, default));
        }
        finally { tx.Rollback(); }
    }

    private static Task<decimal> Balance(SqlConnection cn, SqlTransaction tx, CobranzaResultado r)
        => cn.ExecuteScalarAsync<decimal>("""
            SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS
            WHERE TC='CB' AND SUCURSAL=SUBSTRING(@Comprobante,1,4) AND NUMERO=SUBSTRING(@Comprobante,5,8) AND LETRA='X';
            """, r, tx);

    private static Task<decimal> CreditoPendiente(SqlConnection cn, SqlTransaction tx, string cliente, CobranzaResultado r)
        => cn.ExecuteScalarAsync<decimal>("""
            SELECT SUM(SALDO) FROM dbo.VE_CPTES_CREDITOS_PENDIENTES_SINREMITOS
            WHERE LTRIM(RTRIM(CUENTA))=@cliente AND TC='CB' AND SUCURSAL=SUBSTRING(@comprobante,1,4)
                AND NUMERO=SUBSTRING(@comprobante,5,8) AND LETRA='X';
            """, new { cliente, comprobante = r.Comprobante }, tx);

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler; return proxy;
    }
}

using System.Data;
using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class OrdenPagoSqlFactAttribute : FactAttribute
{
    public OrdenPagoSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ORDENPAGO_TEST_SQL")))
            Skip = "Requiere ORDENPAGO_TEST_SQL sobre ALFANET_DEMO o ALFANET2007_PRUEBAS.";
    }
}

[Collection("Conciliacion SQL")]
public sealed class OrdenPagoTests(ITestOutputHelper output)
{
    private static OrdenPagoClave K(string tc, string num) => new(tc, "0001", num.PadLeft(8, '0'), "X");

    private static OrdenPagoSolicitud Solicitud(decimal pagar, decimal medios, params (string Tc, string Num, decimal Importe)[] creditos) => new()
    {
        Operacion = Guid.NewGuid(), Proveedor = "211010002", Fecha = new DateTime(2026, 11, 6),
        Comprobantes = [new(K("FCC", "1"), pagar)],
        Creditos = [.. creditos.Select(c => new OrdenPagoImporte(K(c.Tc, c.Num), c.Importe))],
        Medios = [new() { Cuenta = "111010001", Importe = medios }]
    };

    [Fact]
    public void PagoExactoAplicaTodoConLaOrdenYNoGeneraAnticipo()
    {
        var r = OrdenPagoService.Repartir(Solicitud(1000, 1000));
        Assert.Equal(0, r.Anticipo);
        Assert.Empty(r.PorCredito);
        Assert.Equal(1000, Assert.Single(r.Propias).Importe);
    }

    [Fact]
    public void LoQueSobraDeLosMediosEsAnticipoYExigeConfirmacion()
    {
        var s = Solicitud(1000, 1500);
        Assert.Contains("anticipo", Assert.Throws<InvalidOperationException>(() => OrdenPagoService.Repartir(s)).Message, StringComparison.OrdinalIgnoreCase);
        s.ConfirmaAnticipo = true;
        var r = OrdenPagoService.Repartir(s);
        Assert.Equal(500, r.Anticipo);
        Assert.Equal(1000, r.Propias.Sum(p => p.Importe));
    }

    [Fact]
    public void LosCreditosSeAplicanALasFacturasEnOrdenYLaOrdenAplicaElResto()
    {
        var s = Solicitud(0, 300, ("NCC", "7", 700));
        s.Comprobantes = [new(K("FCC", "1"), 600), new(K("FCC", "2"), 400)];
        var r = OrdenPagoService.Repartir(s);
        Assert.Equal(0, r.Anticipo);
        Assert.Equal(new[] { (K("NCC", "7"), K("FCC", "1"), 600m), (K("NCC", "7"), K("FCC", "2"), 100m) }, r.PorCredito.ToArray());
        Assert.Equal(new[] { new OrdenPagoImporte(K("FCC", "2"), 300) }, r.Propias.ToArray());
        Assert.Equal(300, r.Propias.Sum(p => p.Importe));
    }

    [Theory]
    [InlineData(1000, 900)]   // los medios no alcanzan
    [InlineData(1000, 0)]     // sin medios
    public void RechazaMediosQueNoCubrenLoQueSePaga(decimal pagar, decimal medios)
        => Assert.Throws<InvalidOperationException>(() => OrdenPagoService.Repartir(Solicitud(pagar, medios)));

    [Fact]
    public void RechazaCreditosMayoresAlPagoDuplicadosEImportesInvalidos()
    {
        Assert.Throws<InvalidOperationException>(() => OrdenPagoService.Repartir(Solicitud(100, 10, ("NCC", "7", 150))));
        var duplicado = Solicitud(100, 100); duplicado.Comprobantes.Add(new(K("FCC", "1"), 1));
        Assert.Throws<InvalidOperationException>(() => OrdenPagoService.Repartir(duplicado));
        Assert.Throws<InvalidOperationException>(() => OrdenPagoService.Repartir(Solicitud(100.005m, 100.005m)));
        Assert.Throws<InvalidOperationException>(() => OrdenPagoService.Repartir(Solicitud(-5, 100)));
        var sinOperacion = Solicitud(100, 100); sinOperacion.Operacion = Guid.Empty;
        Assert.Throws<InvalidOperationException>(() => OrdenPagoService.Repartir(sinOperacion));
        var conHora = Solicitud(100, 100); conHora.Fecha = DateTime.Now;
        Assert.Throws<InvalidOperationException>(() => OrdenPagoService.Repartir(conHora));
    }

    [Theory]
    // (tc, esCredito, modoProforma, mezcla) → ¿corresponde al modo?
    [InlineData("FCC", false, false, false, true)]
    [InlineData("NDC", false, false, false, true)]
    [InlineData("FPC", false, false, false, false)]
    [InlineData("FPC", false, true, false, true)]
    [InlineData("FCC", false, true, false, false)]
    [InlineData("NCC", true, false, false, true)]
    [InlineData("PG", true, false, false, true)]
    [InlineData("NCCP", true, false, false, false)]
    [InlineData("PGFP", true, false, false, false)]
    [InlineData("NCCP", true, true, false, true)]
    [InlineData("PGFP", true, true, false, true)]
    [InlineData("NCC", true, true, false, false)]
    [InlineData("FPC", false, false, true, true)]    // MezclarProformas=SI levanta la restricción
    [InlineData("FCC", false, true, true, true)]
    public void ProformaNoSeMezclaConPagoNormalSalvoConfiguracion(string tc, bool credito, bool proforma, bool mezcla, bool esperado)
        => Assert.Equal(esperado, OrdenPagoService.CorrespondeAlModo(new OrdenPagoDocumento { Tc = tc }, credito, proforma, mezcla));

    // ── Ensayo contra la base (rollback siempre) ─────────────────────────────────────────────

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler; return proxy;
    }

    private static (OrdenPagoService Service, string Connection) Servicio(string usuario)
    {
        var connection = Environment.GetEnvironmentVariable("ORDENPAGO_TEST_SQL")!;
        Assert.Contains(new SqlConnectionStringBuilder(connection).InitialCatalog, new[] { "ALFANET_DEMO", "ALFANET2007_PRUEBAS", "PRUEBAS02122025" });
        using var runtime = new CargaComprobantesArcaRuntime(); var sesion = new SessionDto();
        var sessions = Proxy<ISessionService>((m, _) => m.Name switch { "GetConnectionString" => connection, "GetActiveSession" => sesion, _ => null });
        var users = Proxy<IAppUserSessionService>((m, _) => m.Name switch { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => usuario, _ => null });
        return (new OrdenPagoService(sessions, users, Proxy<IPermissionService>((_, _) => Task.FromResult(true)), runtime.Events), connection);
    }

    internal const string CrearJournal = """
        IF OBJECT_ID(N'dbo.ALFACORE_PAGO_OPERACION',N'U') IS NULL
            CREATE TABLE dbo.ALFACORE_PAGO_OPERACION (Operacion uniqueidentifier NOT NULL PRIMARY KEY, ComprobanteId int NOT NULL, Tc nvarchar(4) NOT NULL,
                Comprobante nvarchar(13) NOT NULL, NumeroAsiento int NOT NULL, Importe decimal(18,2) NOT NULL, Anticipo decimal(18,2) NOT NULL DEFAULT 0,
                SolicitudHash nvarchar(64) NOT NULL, Unidad nvarchar(4) NOT NULL, Usuario nvarchar(50) NOT NULL, FechaHora datetime NOT NULL DEFAULT GETDATE());
        """;

    [OrdenPagoSqlFact]
    public async Task PreparaYGrabaOrdenDePagoConCreditoAnticipoIdempotenciaYRollback()
    {
        var (service, connection) = Servicio("Administrador");
        // Preparación pública (solo lectura): un proveedor con pendientes y créditos.
        var prov = await new SqlConnection(connection).QueryFirstAsync<string>(
            "SELECT TOP 1 LTRIM(RTRIM(CUENTA)) FROM dbo.CO_CPTES_IMPAGOS WHERE TC='FCC' AND SALDO>0 AND LTRIM(RTRIM(MONEDA))='1' ORDER BY SALDO");
        var prep = await service.PrepararAsync(prov, false);
        Assert.NotEmpty(prep.Comprobantes); Assert.NotEmpty(prep.Medios); Assert.All(prep.Comprobantes, d => Assert.NotEqual("FPC", d.Tc));
        var proformas = await service.PrepararAsync(prov, true);
        Assert.All(proformas.Comprobantes, d => Assert.Equal("FPC", d.Tc));
        Assert.All(proformas.Creditos, d => Assert.Contains(d.Tc, new[] { "NCCP", "PGFP" }));
        Assert.True(prep.Creditos.Count == 0 || prep.Creditos.All(d => d.Tc is not ("NCCP" or "PGFP")));

        var factura = prep.Comprobantes.First(d => d.Tc == "FCC" && d.Seleccionable);
        var efectivo = prep.Medios.First(m => m.Tipo == "EF").Cuenta;

        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            await cn.ExecuteAsync(CrearJournal, transaction: tx);
            var antes = await cn.ExecuteScalarAsync<int>("SELECT X_ULTIMO_NRO FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='OPG'", transaction: tx);
            var s = new OrdenPagoSolicitud
            {
                Operacion = Guid.NewGuid(), Proveedor = prov, Fecha = DateTime.Today,
                Comprobantes = [new(factura.Clave, factura.Saldo)], Medios = [new() { Cuenta = efectivo, Importe = factura.Saldo }]
            };
            var r = await service.GrabarEnTransaccionAsync(cn, tx, s, CancellationToken.None);
            Assert.Equal("OPG", r.Tc); Assert.Equal(0, r.Anticipo); Assert.Equal(factura.Saldo, r.Importe);

            // Cabecera, asiento balanceado, aplicación, saldo y contador.
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MV_CPTE WHERE ID=@Id AND LTRIM(RTRIM(TC))='OPG' AND FINALIZADA=1 AND IMPORTE=@Importe", new { Id = r.Id, r.Importe }, tx));
            var lineas = (await cn.QueryAsync<(string Cuenta, string Dh, decimal Importe)>(
                "SELECT LTRIM(RTRIM(CUENTA)),[DEBE-HABER],IMPORTE FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))='OPG' AND SUCURSAL=@S AND NUMERO=@N AND LETRA='X' ORDER BY SECUENCIA",
                new { S = r.Comprobante[..4], N = r.Comprobante.Substring(4, 8) }, tx)).ToList();
            Assert.Equal(new[] { (prov, "D", factura.Saldo), (efectivo, "H", factura.Saldo) }, lineas.ToArray());
            Assert.Equal(factura.Saldo, await cn.ExecuteScalarAsync<decimal>(
                "SELECT SUM(IMPORTE) FROM dbo.MV_APLICACION WHERE LTRIM(RTRIM(TC))='OPG' AND IDCOMPROBANTE=@C AND TCO_ORIGEN=@Tc AND NUMERO_ORIGEN=@N AND IDCOMPROBANTE_PRINT=@C",
                new { C = r.Comprobante, Tc = factura.Tc, N = factura.Numero }, tx));
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.CO_CPTES_IMPAGOS WHERE LTRIM(RTRIM(CUENTA))=@P AND TC=@Tc AND NUMERO=@N AND SALDO>0", new { P = prov, Tc = factura.Tc, N = factura.Numero }, tx));
            Assert.Equal(Math.Max(antes, int.Parse(r.Comprobante.Substring(4, 8)) + 1), await cn.ExecuteScalarAsync<int>("SELECT X_ULTIMO_NRO FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='OPG'", transaction: tx));

            // Trazabilidad: el alta queda en el historial (V_MV_CpteAcciones) con el asiento.
            var alta = await cn.QuerySingleAsync<(string Tipo, string Usuario, int Asiento)>(
                "SELECT LTRIM(RTRIM(TIPO_ACCION)),LTRIM(RTRIM(USUARIO)),ISNULL(NUMEROASIENTO,0) FROM dbo.V_MV_CpteAcciones WHERE LTRIM(RTRIM(TC))='OPG' AND IDCOMPROBANTE=@C",
                new { C = r.Comprobante }, tx);
            Assert.Equal(("A", "Administrador", r.NumeroAsiento), alta);

            // Mismo identificador y mismos datos: devuelve la misma orden, sin duplicar nada.
            var otra = await service.GrabarEnTransaccionAsync(cn, tx, s, CancellationToken.None);
            Assert.Equal(r.Id, otra.Id);
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MV_CPTE WHERE LTRIM(RTRIM(TC))='OPG' AND IDCOMPROBANTE=@C", new { C = r.Comprobante }, tx));
            // Mismo identificador y otros datos: se rechaza.
            s.Detalle = "otro";
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrabarEnTransaccionAsync(cn, tx, s, CancellationToken.None));
            // El comprobante ya pagado ya no se puede volver a pagar.
            var repetido = new OrdenPagoSolicitud
            {
                Operacion = Guid.NewGuid(), Proveedor = prov, Fecha = DateTime.Today,
                Comprobantes = [new(factura.Clave, 1)], Medios = [new() { Cuenta = efectivo, Importe = 1 }]
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrabarEnTransaccionAsync(cn, tx, repetido, CancellationToken.None));
        }
        finally { tx.Rollback(); }
    }

    [OrdenPagoSqlFact]
    public async Task AnularRetiraAsientoYAplicacionesDevuelveElSaldoYDejaLaBaja()
    {
        var (service, connection) = Servicio("Administrador");
        var prov = await new SqlConnection(connection).QueryFirstAsync<string>(
            "SELECT TOP 1 LTRIM(RTRIM(CUENTA)) FROM dbo.CO_CPTES_IMPAGOS WHERE TC='FCC' AND SALDO>0 AND LTRIM(RTRIM(MONEDA))='1' ORDER BY SALDO");
        var prep = await service.PrepararAsync(prov, false);
        var factura = prep.Comprobantes.First(d => d.Tc == "FCC" && d.Seleccionable);
        var efectivo = prep.Medios.First(m => m.Tipo == "EF").Cuenta;

        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            await cn.ExecuteAsync(CrearJournal, transaction: tx);
            await cn.ExecuteAsync("""
                IF OBJECT_ID(N'dbo.ALFACORE_PAGO_ANULACION', N'U') IS NULL
                    CREATE TABLE dbo.ALFACORE_PAGO_ANULACION (Id int IDENTITY(1,1) PRIMARY KEY, ComprobanteId int NOT NULL, Tc nvarchar(4) NOT NULL, Comprobante nvarchar(13) NOT NULL,
                        Usuario nvarchar(50) NOT NULL, Motivo nvarchar(500) NOT NULL, AsientoJson nvarchar(max) NULL, AplicacionesJson nvarchar(max) NULL, FechaHora datetime NOT NULL DEFAULT (GETDATE()));
                """, transaction: tx);
            var r = await service.GrabarEnTransaccionAsync(cn, tx, new OrdenPagoSolicitud
            {
                Operacion = Guid.NewGuid(), Proveedor = prov, Fecha = DateTime.Today,
                Comprobantes = [new(factura.Clave, factura.Saldo)], Medios = [new() { Cuenta = efectivo, Importe = factura.Saldo }]
            }, CancellationToken.None);

            // Un trigger del legacy (FMT_NODEL) también anota «ELIMINACION FISICA» por cada línea de asiento que se borra; la baja propia lleva el motivo.
            const string Bajas = "SELECT COUNT(*) FROM dbo.V_MV_CpteAcciones WHERE LTRIM(RTRIM(TC))='OPG' AND IDCOMPROBANTE=@C AND LTRIM(RTRIM(TIPO_ACCION))='B' AND Proceso LIKE 'AlfaCore: anulación%'";
            var bajasAntes = await cn.ExecuteScalarAsync<int>(Bajas, new { C = r.Comprobante }, tx);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnularEnTransaccionAsync(cn, tx, "OPG", r.Comprobante, "x", CancellationToken.None)); // motivo corto
            await service.AnularEnTransaccionAsync(cn, tx, "OPG", r.Comprobante, "Prueba de anulación", CancellationToken.None);

            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MV_CPTE WHERE ID=@Id AND ANULADA=1", new { r.Id }, tx));
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))='OPG' AND SUCURSAL=@S AND NUMERO=@N AND LETRA='X'",
                new { S = r.Comprobante[..4], N = r.Comprobante.Substring(4, 8) }, tx));
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MV_APLICACION WHERE IDCOMPROBANTE_PRINT=@C", new { C = r.Comprobante }, tx));
            // La factura vuelve a tener su saldo.
            Assert.Equal(factura.Saldo, await cn.ExecuteScalarAsync<decimal>(
                "SELECT SUM(SALDO) FROM dbo.CO_CPTES_IMPAGOS WHERE LTRIM(RTRIM(CUENTA))=@P AND TC=@Tc AND NUMERO=@N", new { P = prov, Tc = factura.Tc, N = factura.Numero }, tx));
            Assert.Equal(bajasAntes + 1, await cn.ExecuteScalarAsync<int>(Bajas, new { C = r.Comprobante }, tx));
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ALFACORE_PAGO_ANULACION WHERE Comprobante=@C AND AsientoJson IS NOT NULL", new { C = r.Comprobante }, tx));
            // Anular dos veces no hace nada más; el comprobante ya anulado queda en el historial una sola vez.
            await service.AnularEnTransaccionAsync(cn, tx, "OPG", r.Comprobante, "Prueba de anulación", CancellationToken.None);
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ALFACORE_PAGO_ANULACION WHERE Comprobante=@C", new { C = r.Comprobante }, tx));
            Assert.Equal(bajasAntes + 1, await cn.ExecuteScalarAsync<int>(Bajas, new { C = r.Comprobante }, tx));
            // Solo se anulan órdenes y proformas; una de otro tipo o inexistente se rechaza.
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnularEnTransaccionAsync(cn, tx, "PG", r.Comprobante, "Prueba de anulación", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnularEnTransaccionAsync(cn, tx, "OPG", "000999999999X", "Prueba de anulación", CancellationToken.None));
        }
        finally { tx.Rollback(); }
    }

    [OrdenPagoSqlFact]
    public async Task AnticipoSobreFacturaAplicaCreditoExistenteYRechazaProformaEnPagoNormal()
    {
        var (service, connection) = Servicio("Administrador");
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        // Proveedor con una factura pendiente y un anticipo (PG) pendiente.
        var par = await cn.QueryFirstOrDefaultAsync<(string Prov, string Pg)>("""
            SELECT TOP 1 LTRIM(RTRIM(i.CUENTA)),LTRIM(RTRIM(c.NUMERO)) FROM dbo.CO_CPTES_IMPAGOS i
            JOIN dbo.CO_CPTES_CREDITOS_PENDIENTES c ON LTRIM(RTRIM(c.CUENTA))=LTRIM(RTRIM(i.CUENTA)) AND LTRIM(RTRIM(c.TC))='PG' AND c.SALDO>100
            WHERE i.TC='FCC' AND i.SALDO>0 AND LTRIM(RTRIM(i.MONEDA))='1'
            """);
        if (par.Prov is null) return;
        var prep = await service.PrepararAsync(par.Prov, false);
        var factura = prep.Comprobantes.First(d => d.Tc == "FCC" && d.Seleccionable);
        var pg = prep.Creditos.First(d => d.Tc == "PG" && d.Seleccionable);
        var efectivo = prep.Medios.First(m => m.Tipo == "EF").Cuenta;

        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            await cn.ExecuteAsync(CrearJournal, transaction: tx);
            // Una proforma no se puede pagar con una orden normal (ni al revés).
            var mezcla = new OrdenPagoSolicitud
            {
                Operacion = Guid.NewGuid(), Proveedor = par.Prov, Fecha = DateTime.Today, Proforma = true,
                Comprobantes = [new(factura.Clave, 1)], Medios = [new() { Cuenta = efectivo, Importe = 1 }]
            };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrabarEnTransaccionAsync(cn, tx, mezcla, CancellationToken.None));
            Assert.Contains("no corresponde", ex.Message);

            // Se aplica parte del anticipo y se paga el resto con efectivo, más un excedente en anticipo nuevo.
            var credito = Math.Min(pg.Saldo, factura.Saldo / 2);
            var efectivoImporte = factura.Saldo - credito + 5m;
            var s = new OrdenPagoSolicitud
            {
                Operacion = Guid.NewGuid(), Proveedor = par.Prov, Fecha = DateTime.Today, ConfirmaAnticipo = true,
                Comprobantes = [new(factura.Clave, factura.Saldo)], Creditos = [new(pg.Clave, credito)],
                Medios = [new() { Cuenta = efectivo, Importe = efectivoImporte }]
            };
            var r = await service.GrabarEnTransaccionAsync(cn, tx, s, CancellationToken.None);
            Assert.Equal(5m, r.Anticipo);
            // La orden aplica factura - crédito; el crédito se aplica a la factura; los 5 quedan como crédito pendiente de la orden.
            Assert.Equal(factura.Saldo - credito, await cn.ExecuteScalarAsync<decimal>(
                "SELECT SUM(IMPORTE) FROM dbo.MV_APLICACION WHERE LTRIM(RTRIM(TC))='OPG' AND IDCOMPROBANTE=@C", new { C = r.Comprobante }, tx));
            Assert.Equal(credito, await cn.ExecuteScalarAsync<decimal>(
                "SELECT SUM(IMPORTE) FROM dbo.MV_APLICACION WHERE LTRIM(RTRIM(TC))='PG' AND NUMERO=@N AND IDCOMPROBANTE_PRINT=@C AND TCO_ORIGEN=@Tc",
                new { N = pg.Numero, C = r.Comprobante, Tc = factura.Tc }, tx));
            Assert.Equal(5m, await cn.ExecuteScalarAsync<decimal>(
                "SELECT SUM(SALDO) FROM dbo.CO_CPTES_CREDITOS_PENDIENTES WHERE LTRIM(RTRIM(TC))='OPG' AND NUMERO=@N", new { N = r.Comprobante.Substring(4, 8) }, tx));
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.CO_CPTES_IMPAGOS WHERE LTRIM(RTRIM(CUENTA))=@P AND TC=@Tc AND NUMERO=@N AND SALDO>0", new { P = par.Prov, Tc = factura.Tc, N = factura.Numero }, tx));
            // El asiento balancea: D proveedor por el efectivo, H efectivo.
            Assert.Equal(0m, await cn.ExecuteScalarAsync<decimal>(
                "SELECT SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END) FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))='OPG' AND NUMERO=@N AND SUCURSAL=@S",
                new { N = r.Comprobante.Substring(4, 8), S = r.Comprobante[..4] }, tx));
        }
        finally { tx.Rollback(); }
    }

    [OrdenPagoSqlFact]
    public async Task ChequePropioValidaChequeraGrabaDatosBancariosYNoPermiteReemitirlo()
    {
        var (service, connection) = Servicio("Administrador");
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        var prov = await cn.QueryFirstOrDefaultAsync<string>(
            "SELECT TOP 1 LTRIM(RTRIM(CUENTA)) FROM dbo.CO_CPTES_IMPAGOS WHERE TC='FCC' AND SALDO>0 AND LTRIM(RTRIM(MONEDA))='1' ORDER BY SALDO");
        var prep = await service.PrepararAsync(prov!, false);
        var cheque = prep.Medios.FirstOrDefault(m => m.Tipo == "CH");
        var chequera = cheque is null ? null : prep.Chequeras.FirstOrDefault(c => c.Cuenta == cheque.Cuenta && c.Hasta - c.Desde > 10);
        if (cheque is null || chequera is null) return;   // la base no tiene chequera de cheque propio
        var factura = prep.Comprobantes.First(d => d.Tc == "FCC" && d.Seleccionable);
        var numero = (chequera.Desde + 7).ToString();
        OrdenPagoSolicitud Nueva(string serie, string nro) => new()
        {
            Operacion = Guid.NewGuid(), Proveedor = prov!, Fecha = DateTime.Today,
            Comprobantes = [new(factura.Clave, Math.Min(1m, factura.Saldo))],
            Medios = [new() { Cuenta = cheque.Cuenta, Importe = Math.Min(1m, factura.Saldo), Serie = serie, NumeroCheque = nro, FechaCheque = DateTime.Today.AddDays(30), Clearing = 24 }]
        };

        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            await cn.ExecuteAsync(CrearJournal, transaction: tx);
            // Fuera de la chequera o con fecha anterior: se rechaza.
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrabarEnTransaccionAsync(cn, tx, Nueva("Z", numero), CancellationToken.None));
            var vencido = Nueva(chequera.Serie, numero); vencido.Medios[0].FechaCheque = DateTime.Today.AddDays(-1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrabarEnTransaccionAsync(cn, tx, vencido, CancellationToken.None));

            var r = await service.GrabarEnTransaccionAsync(cn, tx, Nueva(chequera.Serie, numero), CancellationToken.None);
            var linea = await cn.QuerySingleAsync<(string Nro, string Banco, int Propio, int Clearing, string Origen, string Destino)>("""
                SELECT LTRIM(RTRIM(NroComprobanteBancario)),LTRIM(RTRIM(IdBanco)),ChPropio,Clearing,LTRIM(RTRIM(CuentaOrigen)),LTRIM(RTRIM(CuentaDestino))
                FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))='OPG' AND NUMERO=@N AND SUCURSAL=@S AND [DEBE-HABER]='H'
                """, new { N = r.Comprobante.Substring(4, 8), S = r.Comprobante[..4] }, tx);
            Assert.Equal(($"{chequera.Serie}-{numero}", chequera.IdBanco, 1, 24, cheque.Cuenta, prov), linea);
            // El mismo cheque ya emitido no se puede usar de nuevo.
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrabarEnTransaccionAsync(cn, tx, Nueva(chequera.Serie, numero), CancellationToken.None));
        }
        finally { tx.Rollback(); }
    }

    public sealed class CommitFactAttribute : FactAttribute
    {
        public CommitFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ORDENPAGO_TEST_SQL")) || Environment.GetEnvironmentVariable("ORDENPAGO_TEST_COMMIT") != "1")
                Skip = "Graba de verdad: requiere ORDENPAGO_TEST_SQL y ORDENPAGO_TEST_COMMIT=1 (solo en una base de pruebas).";
        }
    }

    /// <summary>Graba órdenes REALES (con commit) en una base de pruebas para revisarlas en Alfa Gestión y comparar con el legacy.</summary>
    [CommitFact]
    public async Task GrabaDeVerdadOpgCreditoProformaYChequePropioEnBaseDePruebas()
    {
        var (service, connection) = Servicio(Environment.GetEnvironmentVariable("ORDENPAGO_TEST_USER") ?? "Administrador");
        Assert.NotEqual("ALFANET2007", new SqlConnectionStringBuilder(connection).InitialCatalog);
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        async Task<OrdenPagoResultado> Grabar(OrdenPagoSolicitud s) { var r = await service.GrabarAsync(s); output.WriteLine($"{r.Tc} {r.Comprobante} importe {r.Importe:N2} asiento {r.NumeroAsiento} anticipo {r.Anticipo:N2}"); return r; }

        // A) Orden de pago exacta en efectivo.
        var prepA = await service.PrepararAsync("211010066", false);
        var fa = prepA.Comprobantes.First(d => d.Tc == "FCC" && d.Seleccionable);
        var ef = prepA.Medios.First(m => m.Tipo == "EF").Cuenta;
        var a = await Grabar(new() { Operacion = Guid.NewGuid(), Proveedor = "211010066", Fecha = DateTime.Today, Comprobantes = [new(fa.Clave, fa.Saldo)], Medios = [new() { Cuenta = ef, Importe = fa.Saldo }] });
        Assert.Equal(0, (await service.PrepararAsync("211010066", false)).Comprobantes.Count(d => d.Clave == fa.Clave));

        // B) Pago de una factura con una NC del proveedor + efectivo por el resto.
        var prepB = await service.PrepararAsync("211010103", false);
        var nc = prepB.Creditos.First(d => d.Tc == "NCC" && d.Seleccionable);
        var fb = prepB.Comprobantes.First(d => d.Tc == "FCC" && d.Seleccionable && d.Saldo > nc.Saldo);
        var b = await Grabar(new()
        {
            Operacion = Guid.NewGuid(), Proveedor = "211010103", Fecha = DateTime.Today, Comprobantes = [new(fb.Clave, fb.Saldo)], Creditos = [new(nc.Clave, nc.Saldo)],
            Medios = [new() { Cuenta = ef, Importe = fb.Saldo - nc.Saldo }]
        });
        var despuesB = await service.PrepararAsync("211010103", false);
        Assert.DoesNotContain(despuesB.Creditos, d => d.Clave == nc.Clave); Assert.DoesNotContain(despuesB.Comprobantes, d => d.Clave == fb.Clave);

        // C) Pago proforma (PGFP) de una FPC.
        var prepC = await service.PrepararAsync("211014004", true);
        Assert.True(prepC.PuedeProforma); var fc = prepC.Comprobantes.First(d => d.Tc == "FPC" && d.Seleccionable);
        var c = await Grabar(new() { Operacion = Guid.NewGuid(), Proveedor = "211014004", Fecha = DateTime.Today, Proforma = true, Comprobantes = [new(fc.Clave, fc.Saldo)], Medios = [new() { Cuenta = ef, Importe = fc.Saldo }] });
        Assert.Equal("PGFP", c.Tc);
        Assert.DoesNotContain((await service.PrepararAsync("211014004", true)).Comprobantes, d => d.Clave == fc.Clave);

        // D) Pago parcial con cheque propio de una chequera.
        var prepD = await service.PrepararAsync("211010160", false);
        var ch = prepD.Medios.First(m => m.Tipo == "CH"); var chequera = prepD.Chequeras.First(x => x.Cuenta == ch.Cuenta);
        var fd = prepD.Comprobantes.First(d => d.Tc == "FCC" && d.Seleccionable);
        var nro = (await cn.ExecuteScalarAsync<long?>("SELECT MAX(CONVERT(bigint,SUBSTRING(NroComprobanteBancario,3,20))) FROM dbo.MV_ASIENTOS WHERE ChPropio=1 AND NroComprobanteBancario LIKE @S+'-%' AND CUENTA=@C", new { S = chequera.Serie, C = ch.Cuenta }) ?? chequera.Desde - 1) + 1;
        var d = await Grabar(new()
        {
            Operacion = Guid.NewGuid(), Proveedor = "211010160", Fecha = DateTime.Today, Comprobantes = [new(fd.Clave, 1000)],
            Medios = [new() { Cuenta = ch.Cuenta, Importe = 1000, Serie = chequera.Serie, NumeroCheque = Math.Max(nro, chequera.Desde).ToString(), FechaCheque = DateTime.Today.AddDays(30), Clearing = 24 }]
        });
        output.WriteLine($"IDS: {a.Comprobante} {b.Comprobante} {c.Comprobante} {d.Comprobante}");
    }

    /// <summary>Lee de la base las órdenes ya grabadas (viewer real), arma el HTML del PDF de pagos y lo guarda para revisarlo.</summary>
    [CommitFact]
    public async Task ElPdfDeLasOrdenesGrabadasMuestraFacturasMediosYTitulo()
    {
        var connection = Environment.GetEnvironmentVariable("ORDENPAGO_TEST_SQL")!;
        var dir = Environment.GetEnvironmentVariable("ORDENPAGO_TEST_HTML_DIR");
        using var runtime = new CargaComprobantesArcaRuntime(); var sesion = new SessionDto();
        var sessions = Proxy<ISessionService>((m, _) => m.Name switch { "GetConnectionString" => connection, "GetActiveSession" => sesion, _ => null });
        var visor = new ComprobanteViewerService(new ConfigurationBuilder().Build(), sessions, runtime.Events);
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        var ultimas = (await cn.QueryAsync<(string Tc, string Cpte, string Cuenta, string Nombre, decimal Importe, DateTime Fecha)>("""
            SELECT TOP 4 LTRIM(RTRIM(c.TC)),c.IDCOMPROBANTE,LTRIM(RTRIM(c.CUENTA)),LTRIM(RTRIM(c.NOMBRE)),c.IMPORTE,c.FECHA
            FROM dbo.V_MV_CPTE c JOIN dbo.ALFACORE_PAGO_OPERACION o ON o.Comprobante=c.IDCOMPROBANTE AND LTRIM(RTRIM(o.Tc))=LTRIM(RTRIM(c.TC)) ORDER BY o.FechaHora DESC
            """)).ToList();
        Assert.NotEmpty(ultimas);
        using var playwright = dir is null ? null : await Playwright.CreateAsync();
        foreach (var o in ultimas)
        {
            var ficha = new CompraDocumentoMailService.Ficha { Tc = o.Tc, Comprobante = o.Cpte, Fecha = o.Fecha, Cuenta = o.Cuenta, Nombre = o.Nombre, Importe = o.Importe };
            var dto = await CompraDocumentoMailService.DetallePagoAsync(cn, visor, ficha, CancellationToken.None);
            Assert.NotEmpty(dto.AplicaA);                                   // comprobantes cancelados
            Assert.Contains(dto.Asientos, a => a.Haber > 0);                 // medios de pago
            var html = CompraDocumentoMailService.HtmlPago(new EmpresaDocumentData { Nombre = "ALFA NET", Cuit = "30-11111111-1", Domicilio = "Calle 1" }, ficha, [], dto);
            Assert.Contains(o.Tc == "PGFP" ? "PAGO PROFORMA" : "ORDEN DE PAGO", html);
            Assert.Contains("Comprobantes cancelados", html); Assert.Contains("Medios de pago", html);
            output.WriteLine($"{o.Tc} {o.Cpte}: {dto.AplicaA.Count} aplicaciones, {dto.Asientos.Count} líneas de asiento");
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
                var archivo = Path.Combine(dir, $"{o.Tc}-{o.Cpte}.png");
                await using var browser = await playwright!.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = CargaComprobantesBrowserFactAttribute.BrowserPath });
                var page = await browser.NewPageAsync(); await page.SetViewportSizeAsync(900, 900);
                await page.SetContentAsync(html); await page.ScreenshotAsync(new() { Path = archivo, FullPage = true });
            }
        }
    }
}

using System.Data;
using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Retención de Ganancias de la orden de pago: regla pura y comparación contra lo que grabó el legacy.</summary>
[Collection("Conciliacion SQL")]
public sealed class OrdenPagoRetencionTests(ITestOutputHelper output)
{
    private static OrdenPagoService.RetencionEntrada Entrada(decimal baseF, decimal debitos, decimal pagoReal, decimal anterior = 0, decimal retenido = 0, decimal descuento = 0, decimal noGrav = 0)
        => new("Ganancias", baseF, noGrav, debitos, pagoReal, descuento, anterior, retenido, 224000m, 240m, 2m, []);

    [Fact]
    public void BajoElMinimoNoImponibleDelPrimerPagoDelMesNoRetiene()
    {
        var r = OrdenPagoService.CalcularRetencion(Entrada(100000, 121000, 121000));
        Assert.True(r.Aplica); Assert.Equal(0, r.Importe);
    }

    [Fact]
    public void RetieneElPorcentajeSobreLoQueSuperaElMinimoNoImponible()
    {
        var r = OrdenPagoService.CalcularRetencion(Entrada(1000000, 1210000, 1210000));
        Assert.Equal(1000000 - 224000, r.BaseImponible);
        Assert.Equal(15520m, r.Importe);                       // (1.000.000 − 224.000) × 2 %
    }

    [Fact]
    public void ElSegundoPagoDelMesDescuentaLoYaRetenidoYAcumulaLaBase()
    {
        var primero = OrdenPagoService.CalcularRetencion(Entrada(1000000, 1210000, 1210000));
        var segundo = OrdenPagoService.CalcularRetencion(Entrada(500000, 605000, 605000, anterior: 1000000, retenido: primero.Importe));
        Assert.Equal(1500000 - 224000, segundo.BaseImponible);
        Assert.Equal(10000m, segundo.Importe);                 // 25.520 acumulado − 15.520 ya retenido
    }

    [Fact]
    public void LoRetenidoPorDebajoDelMinimoExcluidoSeAnula()
    {
        var r = OrdenPagoService.CalcularRetencion(Entrada(10000, 12100, 12100, anterior: 1000000, retenido: 15520m));
        Assert.Equal(0, r.Importe);                            // 20 de retención < mínimo excluido (240)
    }

    [Fact]
    public void ElAnticipoSinFacturaRetienePorElPagoReal()
    {
        var r = OrdenPagoService.CalcularRetencion(Entrada(0, 0, 1000000));
        Assert.Equal(15520m, r.Importe);
    }

    [Fact]
    public void ElPagoRealEnMediosTienePrioridadSobreLosCreditosAplicados()
    {
        // Factura de 1.210.000 neto 1.000.000, pagada con 900.000 en medios y una NC de 310.000 (neto 256.198,35).
        var r = OrdenPagoService.CalcularRetencion(Entrada(1000000, 1210000, 900000, descuento: 256198.35m));
        Assert.Equal(743801.65m + 0, 1000000 - 256198.35m);
        Assert.Equal(Math.Round((1000000 - 256198.35m - 224000) * 0.02m, 2), r.Importe);
    }

    [Fact]
    public void LaEscalaSumaImporteFijoYPorcentajeSobreElExcedente()
    {
        var entrada = Entrada(1000000, 1210000, 1210000) with { Alicuota = 0, Escala = [new(0, 5000000, 1000m, 10m, 500000m)] };
        var r = OrdenPagoService.CalcularRetencion(entrada);
        Assert.Equal(1000m + (776000m - 500000m) * 0.10m, r.Importe);
    }

    [Theory]
    [InlineData(1500, "Un Mil Quinientos Pesos Con 00/100")]
    [InlineData(1, "Un Peso Con 00/100")]
    [InlineData(100.5, "Cien Pesos Con 50/100")]
    [InlineData(21, "Veintiun Pesos Con 00/100")]
    [InlineData(1126900.93, "Un Millon Ciento Veintiseis Mil Novecientos Pesos Con 93/100")]
    [InlineData(2000000, "Dos Millones Pesos Con 00/100")]
    [InlineData(35, "Treinta Y Cinco Pesos Con 00/100")]
    public void ElImporteSeEscribeEnLetras(double importe, string esperado)
        => Assert.Equal(esperado, AlfaCore.Common.NumeroALetras.Pesos((decimal)importe));

    private static (OrdenPagoService Service, string Connection) Servicio()
    {
        var connection = Environment.GetEnvironmentVariable("ORDENPAGO_TEST_SQL")!;
        var runtime = new CargaComprobantesArcaRuntime(); var sesion = new SessionDto();
        var sessions = Proxy<ISessionService>((m, _) => m.Name switch { "GetConnectionString" => connection, "GetActiveSession" => sesion, _ => null });
        var users = Proxy<IAppUserSessionService>((m, _) => m.Name switch { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => "Administrador", _ => null });
        return (new OrdenPagoService(sessions, users, Proxy<IPermissionService>((_, _) => Task.FromResult(true)), runtime.Events), connection);
    }

    [OrdenPagoSqlFact]
    public async Task GrabaLaOrdenConRetencionAsientoCertificadoYRechazaUnaRetencionDistinta()
    {
        var (service, connection) = Servicio();
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        var par = await cn.QueryFirstOrDefaultAsync<(string Prov, string Numero)?>("""
            SELECT TOP 1 LTRIM(RTRIM(i.CUENTA)), i.NUMERO FROM dbo.CO_CPTES_IMPAGOS i
            JOIN dbo.Vt_Proveedores p ON LTRIM(RTRIM(p.CODIGO))=LTRIM(RTRIM(i.CUENTA)) AND LTRIM(RTRIM(ISNULL(p.RETGAN_IdRetencion,'')))<>''
            LEFT JOIN dbo.MA_CUENTASADIC a ON LTRIM(RTRIM(a.CODIGO))=LTRIM(RTRIM(p.CODIGO))
            WHERE i.TC='FCC' AND i.SALDO>=2000000 AND LTRIM(RTRIM(i.MONEDA))='1' AND (a.FhVtoExento IS NULL OR a.FhVtoExento<=GETDATE()) AND LTRIM(RTRIM(ISNULL(p.NUMERO_DOCUMENTO,'')))<>''
            ORDER BY i.SALDO
            """);
        if (par is not { } elegido) return;
        var prep = await service.PrepararAsync(elegido.Prov, false);
        Assert.True(prep.RetieneGanancias);
        var factura = prep.Comprobantes.First(d => d.Tc == "FCC" && d.Numero.Trim() == elegido.Numero.Trim() && d.Seleccionable);
        var efectivo = prep.Medios.First(m => m.Tipo == "EF").Cuenta;
        var s = new OrdenPagoSolicitud
        {
            Operacion = Guid.NewGuid(), Proveedor = elegido.Prov, Fecha = DateTime.Today,
            Comprobantes = [new(factura.Clave, factura.Saldo)], Medios = [new() { Cuenta = efectivo, Importe = factura.Saldo }]
        };
        var calculo = await service.CalcularRetencionAsync(s);
        Assert.True(calculo.Aplica); Assert.True(calculo.Importe > 0);
        output.WriteLine($"Retención: {calculo.Importe:N2} sobre base {calculo.BaseActual:N2} (anterior {calculo.BaseAnterior:N2})");

        await using var tx0 = (await Task.FromResult(cn.BeginTransaction(IsolationLevel.Serializable)));
        try
        {
            await cn.ExecuteAsync(OrdenPagoTests.CrearJournal, transaction: tx0);
            var certAntes = await cn.ExecuteScalarAsync<long>("SELECT ISNULL(MAX(CONVERT(bigint,RETGAN_NRO)),0) FROM dbo.V_MV_CPTE WHERE LTRIM(RTRIM(TC)) IN ('PG','OPG') AND RETGAN_Importe>0 AND TCORIGEN<>'0'", transaction: tx0);
            s.Medios[0].Importe = factura.Saldo - calculo.Importe;

            // Con otra retención que la calculada se rechaza.
            s.Retencion = calculo.Importe + 1;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrabarEnTransaccionAsync(cn, tx0, s, CancellationToken.None));
            s.Retencion = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrabarEnTransaccionAsync(cn, tx0, s, CancellationToken.None));

            s.Retencion = calculo.Importe;
            var r = await service.GrabarEnTransaccionAsync(cn, tx0, s, CancellationToken.None);
            Assert.Equal(factura.Saldo, r.Importe); Assert.Equal(0, r.Anticipo);
            var cab = await cn.QuerySingleAsync<(decimal Importe, decimal Ret, decimal Base, string Nro, string Regimen)>(
                "SELECT IMPORTE,RETGAN_Importe,RETGAN_BaseImponible,LTRIM(RTRIM(RETGAN_NRO)),LTRIM(RTRIM(RETGAN_IdRetencion)) FROM dbo.V_MV_CPTE WHERE ID=@Id", new { r.Id }, tx0);
            Assert.Equal(factura.Saldo, cab.Importe); Assert.Equal(calculo.Importe, cab.Ret); Assert.Equal(calculo.BaseActual, cab.Base); Assert.Equal((certAntes + 1).ToString(), cab.Nro); Assert.False(string.IsNullOrEmpty(cab.Regimen));
            var lineas = (await cn.QueryAsync<(string Cuenta, string Dh, decimal Importe, decimal RetLiva)>(
                "SELECT LTRIM(RTRIM(CUENTA)),[DEBE-HABER],IMPORTE,LIVA_Ret_Ganancias FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))='OPG' AND SUCURSAL=@S AND NUMERO=@N AND LETRA='X' ORDER BY SECUENCIA",
                new { S = r.Comprobante[..4], N = r.Comprobante.Substring(4, 8) }, tx0)).ToList();
            Assert.Equal(3, lineas.Count);
            Assert.Equal(("D", factura.Saldo, calculo.Importe), (lineas[0].Dh, lineas[0].Importe, lineas[0].RetLiva));
            Assert.Equal(factura.Saldo - calculo.Importe, lineas[1].Importe); Assert.Equal(efectivo, lineas[1].Cuenta);
            Assert.Equal(("H", calculo.Importe), (lineas[2].Dh, lineas[2].Importe));
            // La factura queda pagada por completo y el PDF rotula la retención con su certificado.
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.CO_CPTES_IMPAGOS WHERE LTRIM(RTRIM(CUENTA))=@P AND TC='FCC' AND NUMERO=@N AND SALDO>0", new { P = elegido.Prov, N = factura.Numero }, tx0));
            var doc = await OrdenPagoDocumentService.BuildDataAsync(cn, "OPG", r.Comprobante, CancellationToken.None, tx0);
            Assert.NotNull(doc);
            Assert.Contains(doc!.Datos.MediosPago, m => m.MedioPago.StartsWith("Retención de Ganancias · Certificado N° " + (certAntes + 1)) && m.Importe == calculo.Importe);
            Assert.Equal(factura.Saldo, doc.Datos.Totales.Total);
        }
        finally { tx0.Rollback(); }
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler; return proxy;
    }

    private sealed record Historica(int Id, string Tc, string Comprobante, string Cuenta, DateTime Fecha, decimal Importe, decimal Retencion, decimal Base);

    /// <summary>Recalcula órdenes ya grabadas por el legacy con la regla nueva y cuenta cuántas coinciden (no exige 100 %: la base proporcional difiere en pagos parciales).</summary>
    [OrdenPagoSqlFact]
    public async Task RecalculaOrdenesHistoricasYLasCompara()
    {
        var connection = Environment.GetEnvironmentVariable("ORDENPAGO_TEST_SQL")!;
        using var runtime = new CargaComprobantesArcaRuntime(); var sesion = new SessionDto();
        var sessions = Proxy<ISessionService>((m, _) => m.Name switch { "GetConnectionString" => connection, "GetActiveSession" => sesion, _ => null });
        var users = Proxy<IAppUserSessionService>((m, _) => m.Name switch { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => "Administrador", _ => null });
        var service = new OrdenPagoService(sessions, users, Proxy<IPermissionService>((_, _) => Task.FromResult(true)), runtime.Events);
        var n = int.TryParse(Environment.GetEnvironmentVariable("ORDENPAGO_VALIDAR_N"), out var cant) ? cant : 40;

        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        var historicas = (await cn.QueryAsync<Historica>($"""
            SELECT TOP({n}) ID Id, LTRIM(RTRIM(TC)) Tc, IDCOMPROBANTE Comprobante, LTRIM(RTRIM(CUENTA)) Cuenta, FECHA Fecha, CONVERT(decimal(18,2),IMPORTE) Importe,
                CONVERT(decimal(18,2),RETGAN_Importe) Retencion, CONVERT(decimal(18,2),ISNULL(RETGAN_BaseImponible,0)) Base
            FROM dbo.V_MV_CPTE WHERE LTRIM(RTRIM(TC))='OPG' AND FINALIZADA=1 AND ISNULL(ANULADA,0)=0 AND TCORIGEN='OPG' AND RETGAN_Importe>0 AND FECHA>='2026-01-01'
            ORDER BY NEWID()
            """)).ToList();
        Assert.NotEmpty(historicas);
        int iguales = 0, parciales = 0, sinRegimen = 0, comparablesSimples = 0, igualesSimples = 0; var diferencias = new List<string>();
        foreach (var h in historicas)
        {
            var aplicaciones = (await cn.QueryAsync<(string PagTc, string PagId, string Tc, string Suc, string Num, string Letra, decimal Importe)>("""
                SELECT LTRIM(RTRIM(TC)),IDCOMPROBANTE,LTRIM(RTRIM(TCO_ORIGEN)),SUCURSAL_ORIGEN,NUMERO_ORIGEN,LETRA_ORIGEN,IMPORTE FROM dbo.MV_APLICACION
                WHERE (LTRIM(RTRIM(TC_PRINT))='OPG' AND IDCOMPROBANTE_PRINT=@id) OR (LTRIM(RTRIM(TC))='OPG' AND IDCOMPROBANTE=@id)
                """, new { id = h.Comprobante })).ToList();
            var facturas = aplicaciones.GroupBy(a => (a.Tc, a.Suc, a.Num, a.Letra)).Select(g => new OrdenPagoImporte(new(g.Key.Tc, g.Key.Suc, g.Key.Num, g.Key.Letra), g.Sum(a => a.Importe))).ToList();
            var creditos = aplicaciones.Where(a => !(a.PagTc == "OPG" && a.PagId == h.Comprobante)).GroupBy(a => (a.PagTc, a.PagId)).ToList();
            var creditosImp = new List<OrdenPagoImporte>();
            foreach (var g in creditos)
            {
                var k = await cn.QueryFirstOrDefaultAsync<(string Suc, string Num, string Letra)?>(
                    "SELECT TOP 1 SUCURSAL,NUMERO,LETRA FROM dbo.MV_APLICACION WHERE LTRIM(RTRIM(TC))=@tc AND IDCOMPROBANTE=@id", new { tc = g.Key.PagTc, id = g.Key.PagId });
                if (k is { } kk) creditosImp.Add(new(new(g.Key.PagTc, kk.Suc, kk.Num, kk.Letra), g.Sum(a => a.Importe)));
            }
            var s = new OrdenPagoSolicitud
            {
                Operacion = Guid.NewGuid(), Proveedor = h.Cuenta, Fecha = h.Fecha.Date, Comprobantes = facturas, Creditos = creditosImp,
                Medios = [new() { Cuenta = "x", Importe = h.Importe - h.Retencion }]
            };
            var (r, _) = await service.CalcularRetencionAsync(cn, null, s, h.Id, CancellationToken.None);
            if (!r.Aplica) { sinRegimen++; diferencias.Add($"{h.Comprobante}: no aplica hoy ({r.Motivo})"); continue; }   // el régimen o la exención del proveedor cambiaron desde entonces
            var simple = creditosImp.Count == 0 && facturas.Count > 0;      // con créditos el legacy descuenta menos IVA que la base neta proporcional; sin facturas es un anticipo puro
            var coincide = Math.Abs(r.Importe - h.Retencion) <= 0.05m;
            if (simple) { comparablesSimples++; if (coincide) igualesSimples++; }
            if (coincide) iguales++;
            else diferencias.Add($"{h.Comprobante} {h.Fecha:dd/MM/yyyy}: legacy {h.Retencion:N2} (base {h.Base:N2}) / nuevo {r.Importe:N2} (base {r.BaseActual:N2}, anterior {r.BaseAnterior:N2}, retenido antes {r.RetenidoAntes:N2}) · {facturas.Count} fact, {creditosImp.Count} créd");
            if (creditosImp.Count > 0) parciales++;
        }
        output.WriteLine($"Coinciden (±0,05) {iguales} de {historicas.Count - sinRegimen} comparables; {sinRegimen} ya no retienen hoy. Sin créditos: {igualesSimples} de {comparablesSimples}. Con créditos aplicados: {parciales}.");
        diferencias.ForEach(output.WriteLine);
        Assert.True(igualesSimples >= comparablesSimples * 0.85, $"Sin créditos coinciden solo {igualesSimples} de {comparablesSimples}. Ver el detalle de diferencias.");
    }
}

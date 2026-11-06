using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Venta con listas de precios en una base Retail (clase fija, ofertas, internos) contra facturas legacy reales.</summary>
public sealed class CargaComprobantesRetailTests(ITestOutputHelper output)
{
    private static readonly CargaComprobantesConfigPrecios Descuento = new() { ClaseOferta = 5, OfertaPorDescuento = true };

    private static CargaComprobantesPrecioListaFila Fila() => new()
    {
        Articulo = "X", ConIva = true, Precio1 = 100, Precio4 = 1000, Precio5 = 900, Precio9 = 5, Precio10 = 10,
        CantidadOf2 = 1.5m, CantidadOf3 = 3, OfertaDesde = new DateTime(2026, 11, 1), OfertaHasta = new DateTime(2026, 11, 20)
    };

    [Fact]
    public void OfertaPorFechaTomaLaClaseDeOferta()
    {
        var r = CargaComprobantesPreciosLista.Resolver(Fila(), 4, 10, new DateTime(2026, 11, 20), Descuento);
        Assert.True(r.Oferta); Assert.Equal(900, r.Bruto); Assert.Equal(0, r.Descuento);
        var vencida = CargaComprobantesPreciosLista.Resolver(Fila(), 4, 1, new DateTime(2026, 11, 21), Descuento);
        Assert.False(vencida.Oferta); Assert.Equal(1000, vencida.Bruto);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1.5, 5)]
    [InlineData(2.9, 5)]
    [InlineData(3, 10)]
    public void OfertaPorCantidadComoDescuento(decimal cantidad, decimal descuento)
    {
        var fila = Fila(); fila.OfertaDesde = null;
        var r = CargaComprobantesPreciosLista.Resolver(fila, 4, cantidad, new DateTime(2026, 11, 6), Descuento);
        Assert.Equal(1000, r.Bruto); Assert.Equal(descuento, r.Descuento);
        Assert.Equal("5% desde 1,5 · 10% desde 3", r.Etiqueta);
    }

    [Fact]
    public void OfertaPorCantidadComoPrecioUnitarioOPack()
    {
        var fila = Fila(); fila.OfertaDesde = null; fila.Precio9 = 950; fila.Precio10 = 2700;
        Assert.Equal(950, CargaComprobantesPreciosLista.Resolver(fila, 4, 2, DateTime.Today, new() { OfertaPrecioUnitario = true }).Bruto);
        Assert.Equal(900, CargaComprobantesPreciosLista.Resolver(fila, 4, 3, DateTime.Today, new()).Bruto); // pack 3 x 2700
        Assert.Equal(1000, CargaComprobantesPreciosLista.Resolver(fila, 4, 2, DateTime.Today, new()).Bruto);
    }

    [Fact]
    public void InternosSeSeparanDelPrecioFinal()
    {
        // V_mv_vta (PreciosFinales): 3423,65 final con 89,59 de internos -> neto (3423,65-89,59)/1,21.
        var fila = new CargaComprobantesPrecioListaFila { ConIva = true, Precio4 = 3423.65m };
        var pv = CargaComprobantesPreciosLista.Calcular(fila, 0, 4, 1, DateTime.Today, new() { Retail = true }, 21, false, 89.59m);
        Assert.Equal(2755.4215m, pv.Neto); Assert.Equal(3423.65m, pv.Final);
    }

    [Fact]
    public void SolicitudArcaInformaInternosComoTributo4()
    {
        var liquidacion = new CargaComprobantesLiquidacion
        {
            NetoGravado = 20702.17m, Iva = 4347.45m, ImpuestosInternos = 179.18m, Total = 25228.80m,
            Alicuotas = [new() { Alicuota = 21, Neto = 20702.17m, Iva = 4347.45m }]
        };
        var s = CargaComprobantesElectronicaEmisor.CrearSolicitud(new ArcaEmisorConfig("20000000001", "Prueba", default, 1, "", "", "1", "", ""),
            liquidacion, 6, 5, 99, "0", 1, DateTime.Today);
        Assert.Equal(179.18m, s.ImpTrib); Assert.Equal(25228.80m, s.ImpTotal);
        var t = Assert.Single(s.Tributos);
        Assert.Equal(4, t.Id); Assert.Equal(20702.17m, t.BaseImponible); Assert.Equal(0.87m, t.Alicuota); Assert.Equal(179.18m, t.Importe);
    }

    /// <summary>
    /// El resolvedor compartido (POS, CRM, catálogos) y el POS respetan Retail: clase de venta fija,
    /// lista por defecto, oferta vigente y sin caer al costo del maestro. Solo lectura.
    /// </summary>
    [Fact]
    public async Task PosYResolvedorRespetanRetail()
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL_SQL");
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL") != "1" || string.IsNullOrWhiteSpace(cs)) return;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        var claseUno = await cn.QueryFirstAsync<string>("SELECT TOP (1) LTRIM(RTRIM(CODIGO)) FROM dbo.VT_CLIENTES WHERE Clase=1 AND LTRIM(RTRIM(IdLista))='0001' ORDER BY CODIGO;");
        var sinLista = await cn.QueryFirstAsync<string>("SELECT TOP (1) LTRIM(RTRIM(CODIGO)) FROM dbo.VT_CLIENTES WHERE ISNULL(LTRIM(RTRIM(IdLista)),'')='' ORDER BY CODIGO;");
        var resolver = new ArticuloPrecioResolverService();
        var p1 = await resolver.ResolveContextAsync(cn, claseUno, CancellationToken.None);
        Assert.True(p1.Retail); Assert.Equal(4, p1.ClasePrecio); Assert.Equal("0001", p1.IdLista);
        var p2 = await resolver.ResolveContextAsync(cn, sinLista, CancellationToken.None);
        Assert.Equal("0001", p2.IdLista); Assert.Equal(4, p2.ClasePrecio);

        var session = Proxy<ISessionService>((m, _) => m.Name == "GetConnectionString" ? cs : null);
        var pos = new PuntoVentaService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), session,
            Proxy<IAppUserSessionService>(), Proxy<IAppEventService>(), Proxy<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(),
            Proxy<IArcaConfigService>(), Proxy<IArcaFacturacionElectronicaService>(), resolver,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PuntoVentaService>.Instance);
        foreach (var cliente in new[] { claseUno, sinLista })
        {
            var normal = await pos.GetArticuloPorCodigoAsync("207", cliente);
            Assert.Equal(4688.45m, normal!.PrecioUnitario); // clase 4 de la lista 0001, no PROVEEDOR (4310)
            var oferta = await pos.GetArticuloPorCodigoAsync("1108", cliente);
            Assert.True(oferta!.EsPrecioOferta); Assert.Equal(11100m, oferta.PrecioUnitario); Assert.Equal(12019.95m, oferta.PrecioOriginal);
        }
        Assert.Null(await pos.GetArticuloPorCodigoAsync("15", claseUno)); // fuera de la lista: no se vende al costo
        var catalogo = await pos.SearchArticulosAsync(new PuntoVentaCatalogFiltersDto { Texto = "1108", TamanioPagina = 20 }, claseUno);
        Assert.Contains(catalogo.Articulos, x => x.IdArticulo == "1108" && x.EsPrecioOferta && x.PrecioUnitario == 11100m);
        output.WriteLine($"Clase 1: {claseUno} · sin lista: {sinLista} · catálogo {catalogo.ListaPrecioActual} clase {catalogo.ClasePrecioActual}");
    }

    private static T Proxy<T>(Func<System.Reflection.MethodInfo, object?[]?, object?>? handler = null) where T : class
    {
        var proxy = System.Reflection.DispatchProxy.Create<T, RetailProxy>();
        ((RetailProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class RetailProxy : System.Reflection.DispatchProxy
    {
        public Func<System.Reflection.MethodInfo, object?[]?, object?>? Handler { get; set; }
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
            => targetMethod is null ? null : Handler?.Invoke(targetMethod, args)
                ?? (targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : null);
    }

    /// <summary>
    /// Replica las facturas legacy 0003-00331492B (internos) y 0003-00331508B (oferta por cantidad) de PRUEBAS02122025
    /// con un cliente de cuenta corriente y graba todo en una transacción que se revierte.
    /// Requiere ALFACORE_TEST_RETAIL=1 y ALFACORE_TEST_RETAIL_SQL.
    /// </summary>
    [Fact]
    public async Task ReplicaFacturasLegacyRetail()
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL_SQL");
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL") != "1" || string.IsNullOrWhiteSpace(cs)) return;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        var repo = new CargaComprobantesRepository();
        var planner = new CargaComprobantesPlanificador(repo);
        var writer = new CargaComprobantesGrabador(planner, new());
        var ctx = await repo.GetContextoAsync(cn, "Administrador", CancellationToken.None);
        Assert.True(ctx.Retail);
        var talonario = ctx.TalonariosDirecta.First(x => x.Tc == "FC" && x.Talonario == 0).Talonario;
        const string cliente = "112010193"; // consumidor final, cuenta corriente, lista 0001
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            // Un cliente con clase 1 (PROVEEDOR = costo) igual factura con la clase de venta fija.
            await cn.ExecuteAsync("UPDATE dbo.VT_CLIENTES SET Clase=1 WHERE LTRIM(RTRIM(CODIGO))=@Cliente;", new { Cliente = cliente }, tx);
            CargaComprobantesDirecta Draft(params (string Art, decimal Cant)[] lineas) => new()
            {
                Borrador = Guid.NewGuid(), Tc = "FC", Cliente = cliente, UnidadNegocio = "1", Deposito = "1",
                Lineas = lineas.Select(x => new CargaComprobantesDirectaLinea { Articulo = x.Art, Cantidad = x.Cant }).ToList()
            };

            // 0003-00331492B: 207, 1235 x2, 1710, 7221 x2 (89,59 de internos por unidad).
            var plan1 = await planner.CrearAsync(cn, ctx, 0, talonario, [], DateTime.Today, CancellationToken.None, tx,
                Draft(("207", 1), ("1235", 2), ("1710", 1), ("7221", 2)));
            var l1 = plan1.Liquidacion;
            output.WriteLine($"Plan 1: {plan1.Letra} clase {plan1.ClasePrecio} lista {plan1.Origen.Lista} · neto {l1.NetoGravado} iva {l1.Iva} internos {l1.ImpuestosInternos} total {l1.Total}");
            Assert.Equal(4, plan1.ClasePrecio);
            Assert.Equal("B", plan1.Letra);
            Assert.Equal(25228.80m, l1.Total);
            Assert.Equal(179.18m, l1.ImpuestosInternos);
            var fiscal1 = CargaComprobantesDesgloseFiscal.Crear(l1);
            Assert.Equal(20702.17m, fiscal1.NetoGravado);
            Assert.Equal(4347.45m, fiscal1.Iva);

            // 0003-00331508B: 783 x0,59, 11695 x1,155, 16841 x1,5 (5% desde 1,5).
            var plan2 = await planner.CrearAsync(cn, ctx, 0, talonario, [], DateTime.Today, CancellationToken.None, tx,
                Draft(("783", 0.59m), ("11695", 1.155m), ("16841", 1.5m)));
            output.WriteLine($"Plan 2: neto {plan2.Liquidacion.NetoGravado} iva {plan2.Liquidacion.Iva} total {plan2.Liquidacion.Total}");
            Assert.Equal(5, plan2.Origen.Lineas.Single(x => x.Articulo == "16841").Descuento);
            Assert.Equal(0, plan2.Origen.Lineas.Single(x => x.Articulo == "11695").Descuento);
            Assert.Equal(65154.26m, plan2.Liquidacion.Total);
            var fiscal2 = CargaComprobantesDesgloseFiscal.Crear(plan2.Liquidacion);
            Assert.Equal(53846.50m, fiscal2.NetoGravado);
            Assert.Equal(11307.76m, fiscal2.Iva);

            // Grabación de la 1 con internos: cabecera, renglones, asiento y libro como el legacy.
            var g1 = await writer.GrabarAsync(cn, tx, ctx, plan1, Guid.NewGuid(), 99999991, "1", DateTime.Today, CancellationToken.None);
            var cab = await cn.QuerySingleAsync<(decimal Importe, decimal Neto, decimal Iva, decimal Internos, int Clase, string Lista)>(
                "SELECT IMPORTE,IMPORTE_S_IVA,ImporteIva,ImporteImpuestosInternos,CLASEPRECIO,LTRIM(RTRIM(IdLista)) FROM dbo.V_MV_CPTE WHERE ID=@Id;", new { Id = g1.FacturaId }, tx);
            Assert.Equal((25228.80m, 20702.17m, 4347.45m, 179.18m, 4, "0001"), cab);
            var renglon = await cn.QuerySingleAsync<(decimal Importe, decimal SinIva, decimal Impuestos, decimal Total, decimal TotalFinal, decimal PrecioOld, string Lista)>("""
                SELECT IMPORTE,IMPORTE_S_IVA,IMPUESTOS,TOTAL,TotalFinal,PrecioOld,LTRIM(RTRIM(IdLista)) FROM dbo.V_MV_CPTEINSUMOS
                WHERE TC='FC' AND IDCOMPROBANTE=@Cpte AND LTRIM(RTRIM(IDARTICULO))='7221';
                """, new { Cpte = g1.Comprobante }, tx);
            output.WriteLine($"7221: {renglon}");
            Assert.Equal((3423.65m, 2755.4215m, 89.59m, 6847.30m, 6847.30m, 2755.42m, "0001"), renglon);
            var asiento = (await cn.QueryAsync<(string Cuenta, string Dh, decimal Importe, decimal? Exento)>("""
                SELECT LTRIM(RTRIM(CUENTA)),[DEBE-HABER],IMPORTE,LIVA_EXENTO FROM dbo.MV_ASIENTOS
                WHERE TC='FC' AND SUCURSAL=@Suc AND NUMERO='99999991' AND LETRA='B' ORDER BY SECUENCIA;
                """, new { Suc = plan1.PuntoVenta.ToString("0000") }, tx)).ToList();
            foreach (var x in asiento) output.WriteLine($"  {x}");
            Assert.Equal(179.18m, asiento[0].Exento);
            Assert.Contains(asiento, x => x.Cuenta == "41102" && x.Dh == "H" && x.Importe == 179.18m);
            Assert.Contains(asiento, x => x.Cuenta == "41102" && x.Dh == "H" && x.Importe == 20702.17m);
            Assert.Contains(asiento, x => x.Dh == "H" && x.Importe == 4347.45m);
            Assert.Contains(asiento, x => x.Dh == "D" && x.Importe == 25228.80m);

            // Grabación de la 2: renglón con oferta por cantidad (TOTAL bruto, ImporteDto y TotalFinal como el legacy).
            var g2 = await writer.GrabarAsync(cn, tx, ctx, plan2, Guid.NewGuid(), 99999992, "1", DateTime.Today, CancellationToken.None);
            var oferta = await cn.QuerySingleAsync<(decimal Importe, decimal SinIva, decimal Total, decimal TotalFinal, double PorcDto, decimal ImporteDto)>("""
                SELECT IMPORTE,IMPORTE_S_IVA,TOTAL,TotalFinal,PorcDto,ImporteDto FROM dbo.V_MV_CPTEINSUMOS
                WHERE TC='FC' AND IDCOMPROBANTE=@Cpte AND LTRIM(RTRIM(IDARTICULO))='16841';
                """, new { Cpte = g2.Comprobante }, tx);
            output.WriteLine($"16841: {oferta}");
            Assert.Equal((16966.62m, 14022.00m, 25449.93m, 24177.43m, 5d, 1272.4965m), oferta);

            // Oferta por fecha vigente: 1108 a 11.100 (clase 5) como la factura legacy 0003-00331470B.
            var plan3 = await planner.CrearAsync(cn, ctx, 0, talonario, [], new DateTime(2026, 11, 6), CancellationToken.None, tx, Draft(("1108", 1)));
            Assert.Equal(11100m, plan3.Liquidacion.Total);

            // Contado: el mismo comprobante legacy a Consumidor Final (condición CONTADO) con su cobranza CBCT,
            // como 0003-00284694X: cliente al haber, Visa al debe y aplicación por el total.
            var contado = Draft(("207", 1), ("1235", 2), ("1710", 1), ("7221", 2));
            contado.Cliente = "112010001";
            var sinPagos = await Assert.ThrowsAsync<CargaComprobantesReglaException>(() =>
                planner.CrearAsync(cn, ctx, 0, talonario, [], DateTime.Today, CancellationToken.None, tx, contado));
            Assert.Contains("medios de pago", sinPagos.Message);
            contado.Pagos = [new() { CodigoMedioPago = "111030001" }];
            var planContado = await planner.CrearAsync(cn, ctx, 0, talonario, [], DateTime.Today, CancellationToken.None, tx, contado);
            Assert.Equal(25228.80m, Assert.Single(planContado.Cobranza!).Importe);
            var g3 = await writer.GrabarAsync(cn, tx, ctx, planContado, Guid.NewGuid(), 99999993, "1", DateTime.Today, CancellationToken.None);
            output.WriteLine($"Contado: {g3.Comprobante} cobranza {g3.Cobranza}");
            Assert.NotNull(g3.Cobranza);
            var cob = await cn.QuerySingleAsync<(decimal Importe, decimal Neto, decimal Iva, string Cuenta, string Lista, int Clase)>("""
                SELECT IMPORTE,IMPORTE_S_IVA,ImporteIva,LTRIM(RTRIM(CUENTA)),LTRIM(RTRIM(IdLista)),CLASEPRECIO FROM dbo.V_MV_CPTE
                WHERE TC='CBCT' AND IDCOMPROBANTE=@Cob;
                """, new { Cob = g3.Cobranza }, tx);
            Assert.Equal((25228.80m, 20702.17m, 4347.45m, "112010001", "0001", 4), cob);
            var asientoCob = (await cn.QueryAsync<(string Cuenta, string Dh, decimal Importe, string TipoReg, string Detalle)>("""
                SELECT LTRIM(RTRIM(CUENTA)),[DEBE-HABER],IMPORTE,LTRIM(RTRIM(TIPO_REG)),DETALLE FROM dbo.MV_ASIENTOS
                WHERE TC='CBCT' AND SUCURSAL=@Suc AND NUMERO=@Num AND LETRA='X' ORDER BY SECUENCIA;
                """, new { Suc = g3.Cobranza![..4], Num = g3.Cobranza.Substring(4, 8) }, tx)).ToList();
            foreach (var x in asientoCob) output.WriteLine($"  {x}");
            Assert.Equal([("112010001", "H", 25228.80m, "CB"), ("111030001", "D", 25228.80m, "CB")],
                asientoCob.Select(x => (x.Cuenta, x.Dh, x.Importe, x.TipoReg)).ToList());
            Assert.Equal(25228.80m, await cn.ExecuteScalarAsync<decimal>("""
                SELECT IMPORTE FROM dbo.MV_APLICACION WHERE TC='CBCT' AND IDCOMPROBANTE=@Cob AND TCO_ORIGEN='FC' AND IDCOMPROBANTE_ORIGEN=@Fc;
                """, new { Cob = g3.Cobranza, Fc = g3.Comprobante }, tx));
            // Dos medios que no cubren el total se rechazan antes de pedir el CAE.
            contado.Pagos = [new() { CodigoMedioPago = "111010001", Importe = 20000 }, new() { CodigoMedioPago = "111030001", Importe = 5000 }];
            var corto = await Assert.ThrowsAsync<CargaComprobantesReglaException>(() =>
                planner.CrearAsync(cn, ctx, 0, talonario, [], DateTime.Today, CancellationToken.None, tx, contado));
            Assert.Contains("tienen que coincidir", corto.Message);

            // Un artículo sin precio en la lista no se factura con el costo del maestro.
            var fuera = await cn.QueryFirstAsync<string>("""
                SELECT TOP (1) LTRIM(RTRIM(a.IDARTICULO)) FROM dbo.V_MA_ARTICULOS a
                WHERE ISNULL(a.Suspendido,0)=0 AND ISNULL(a.SuspendidoV,0)=0 AND ISNULL(LTRIM(RTRIM(a.IDUNIDAD)),'')<>''
                    AND NOT EXISTS (SELECT 1 FROM dbo.V_MA_PRECIOS p WHERE p.IdArticulo=a.IDARTICULO AND p.IdLista='0001' AND p.TipoLista='V');
                """, transaction: tx);
            var ex = await Assert.ThrowsAsync<CargaComprobantesReglaException>(() =>
                planner.CrearAsync(cn, ctx, 0, talonario, [], DateTime.Today, CancellationToken.None, tx, Draft((fuera, 1))));
            output.WriteLine(ex.Message);
            Assert.Contains("lista de precios 0001", ex.Message);
        }
        finally { await tx.RollbackAsync(); }
    }
}

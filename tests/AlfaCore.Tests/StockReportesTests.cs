using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class StockReportesCalculoTests
{
    [Fact]
    public void VencimientoComoElLegacy()
    {
        var fecha = new DateTime(2026, 11, 11);
        Assert.Equal(new DateTime(2026, 11, 21), StockMovimientosCalculo.VencimientoPropuesto(fecha, 10));
        Assert.Equal(new DateTime(2026, 11, 13), StockMovimientosCalculo.VencimientoPropuesto(fecha, 0));   // sin días: 2 por defecto
        Assert.Equal("20261121", StockMovimientosCalculo.LoteVencimiento(new DateTime(2026, 11, 21)));
        Assert.Equal(new DateTime(2025, 4, 18), StockMovimientosCalculo.Vencimiento("                 20250418"));
        Assert.Null(StockMovimientosCalculo.Vencimiento("L-123"));
        Assert.Null(StockMovimientosCalculo.Vencimiento("20251399"));
        Assert.Null(StockMovimientosCalculo.Vencimiento(""));
        Assert.True(StockMovimientosCalculo.LlevaVencimiento("IR", 5));
        Assert.True(StockMovimientosCalculo.LlevaVencimiento("AJP", 1));
        Assert.False(StockMovimientosCalculo.LlevaVencimiento("AJP", -1));
        Assert.False(StockMovimientosCalculo.LlevaVencimiento("TR", 5));
        Assert.False(StockMovimientosCalculo.LlevaVencimiento("VS", -5));
    }

    [Fact]
    public void FilasDeIngresoLlevanElLoteYLasSalidasNo()
    {
        var filas = StockMovimientosCalculo.Filas("AJP", [
            new("10", "A", "1", 1m, 5m, 0, 0, 0, "20261201"),
            new("20", "B", "1", 1m, -2m, 0, 0, 0, "20261201")], "1", null);
        Assert.Equal("20261201", filas[0].Lote);
        Assert.Null(filas[1].Lote);
        var tr = StockMovimientosCalculo.Filas("TR", [new("10", "A", "1", 1m, -5m, 0, 0, 0, "20261201")], "1", "2");
        Assert.All(tr, f => Assert.Null(f.Lote));
    }

    [Fact]
    public void ElStockSeAsignaALosIngresosMasNuevos()
    {
        // Ingresos: 10 el 01/10 (vence 20/10), 5 el 01/11 (sin lote: 01/11 + 30 días), 8 el 05/11 (vence 30/11).
        var ingresos = new[]
        {
            new StockReportesCalculo.Ingreso(new DateTime(2026, 10, 1), "20261020", 10),
            new StockReportesCalculo.Ingreso(new DateTime(2026, 11, 1), null, 5),
            new StockReportesCalculo.Ingreso(new DateTime(2026, 11, 5), "20261130", 8)
        };
        var partes = StockReportesCalculo.Repartir(16, ingresos, 30);
        Assert.Equal(3, partes.Count);
        Assert.Equal((new DateTime(2026, 10, 20), 3m), (partes[0].Vencimiento!.Value, partes[0].Cantidad));   // lo viejo ya salió en parte
        Assert.Equal((new DateTime(2026, 11, 30), 8m), (partes[1].Vencimiento!.Value, partes[1].Cantidad));
        Assert.Equal((new DateTime(2026, 12, 1), 5m), (partes[2].Vencimiento!.Value, partes[2].Cantidad));
        Assert.True(partes[2].Estimado);
        Assert.Equal(16m, partes.Sum(p => p.Cantidad));

        // Más stock que ingresos: el resto queda sin vencimiento. Sin stock: nada.
        var sobra = StockReportesCalculo.Repartir(30, ingresos, 30);
        Assert.Equal(7m, sobra.Single(p => p.Vencimiento is null).Cantidad);
        Assert.Empty(StockReportesCalculo.Repartir(0, ingresos, 30));
        Assert.Empty(StockReportesCalculo.Repartir(-4, ingresos, 30));
    }

    [Theory]
    [InlineData("2026-11-10", "VENCIDO")]
    [InlineData("2026-11-11", "PORVENCER")]
    [InlineData("2026-12-11", "PORVENCER")]
    [InlineData("2026-12-12", "VIGENTE")]
    [InlineData(null, "SINVTO")]
    public void EstadoDelVencimiento(string? vencimiento, string esperado)
        => Assert.Equal(esperado, StockReportesCalculo.Estado(vencimiento is null ? null : DateTime.Parse(vencimiento), new DateTime(2026, 11, 11), 30));
}

/// <summary>
/// Contra una base de pruebas (STOCK_MOV_TEST_SQL): aplica el update, compara saldos/período/ficha entre sí, exporta a Excel
/// y prueba un ingreso de un perecedero (vencimiento grabado y visible en el reporte). Repone artículo, clave y contadores.
/// </summary>
// Mismo artículo y depósito en la base de prueba: los tests SQL de stock no corren en paralelo.
[Collection("StockSql")]
public sealed class StockReportesSqlTests
{
    [StockMovimientosSqlFact]
    public async Task ReportesCoincidenEntreSiYElPerecederoSeVeConSuVencimiento()
    {
        var connection = Environment.GetEnvironmentVariable("STOCK_MOV_TEST_SQL")!;
        var usuario = Environment.GetEnvironmentVariable("STOCK_MOV_TEST_USER") is { Length: > 0 } u ? u : "Administrador";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AlfaCore.sln"))) dir = dir.Parent;
        await using (var cn = new SqlConnection(connection))
        {
            foreach (var nombre in new[] { "2026-11-11-003__stock_movimientos_menu_web.sql", "2026-11-11-004__stock_reportes_perecederos.sql" })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(dir!.FullName, "src/AlfaCore/App_Data/updates", nombre));
                foreach (var lote in sql.Split(["\r\nGO\r\n", "\nGO\n", "\nGO\r\n"], StringSplitOptions.RemoveEmptyEntries))
                { await cn.ExecuteAsync(lote); await cn.ExecuteAsync(lote); }
            }
            Assert.Equal("/stock/reportes", await cn.ExecuteScalarAsync<string>("SELECT RutaWeb FROM dbo.ALFACORE_MENU_WEB WHERE Menu='ALFA' AND Clave='D80-RPTSTOCK'"));
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE)))='USAPERECEDEROS'"));
        }

        var sesion = new PlanCuentasAbmSqlTests.Sesion(connection);
        var user = new PlanCuentasAbmSqlTests.Usuario(usuario);
        var reportes = new StockReportesService(sesion, user, new PlanCuentasAbmSqlTests.Permisos(), new PlanCuentasAbmSqlTests.Eventos());
        var movimientos = new StockMovimientosService(sesion, user, new PlanCuentasAbmSqlTests.Permisos(), new PlanCuentasAbmSqlTests.Eventos());

        var opciones = await reportes.GetOpcionesAsync();
        Assert.NotEmpty(opciones.Depositos);
        var saldos = await reportes.GetSaldosAsync(new StockReporteFiltro { TamanoPagina = 25 });
        Assert.True(saldos.Total >= saldos.Filas.Count);
        if (saldos.Filas.Count > 0)
        {
            var s = saldos.Filas[0];
            var ficha = await reportes.GetFichaAsync(new StockReporteFiltro { Articulo = s.Articulo, Deposito = s.Deposito, Desde = new DateTime(2000, 1, 1) });
            Assert.Equal(s.Saldo, ficha.SaldoFinal);
            Assert.Equal(ficha.SaldoInicial + ficha.Entradas - ficha.Salidas, ficha.SaldoFinal);
            var periodo = await reportes.GetPeriodoAsync(new StockReporteFiltro { Texto = s.Articulo, Deposito = s.Deposito, Desde = DateTime.Today.AddMonths(-1), SoloConStock = false, TamanoPagina = 200 });
            Assert.Equal(s.Saldo, periodo.Filas.First(x => x.Articulo == s.Articulo).SaldoFinal);
            var columnas = await reportes.GetSaldosAsync(new StockReporteFiltro { Texto = s.Articulo, DepositosEnColumnas = true });
            Assert.Equal(s.Saldo, columnas.Filas.First(x => x.Articulo == s.Articulo).PorDeposito[s.Deposito]);
        }
        foreach (var tipo in Enum.GetValues<StockReporteTipo>())
        {
            var filtro = new StockReporteFiltro { Articulo = saldos.Filas.FirstOrDefault()?.Articulo ?? "" };
            if (tipo == StockReporteTipo.Ficha && filtro.Articulo.Length == 0) continue;
            Assert.True((await reportes.ExportarAsync(tipo, filtro)).Length > 1000);
        }

        // Perecedero: un IR propone vencimiento = fecha + días, lo graba en NroLote y el reporte lo muestra.
        var ctx = await movimientos.GetContextoAsync();
        var unidad = ctx.UnidadNegocio.Length > 0 ? ctx.UnidadNegocio : ctx.UnidadesNegocio[0].Codigo;
        var deposito = ctx.Depositos.First(d => (d.Compartido || d.UnidadNegocio == unidad) && d.Empresa.Length == 0).Codigo;
        var articulo = (await movimientos.BuscarArticulosAsync("", "AJP", deposito)).First(a => a.Restriccion.Length == 0 && a.Stock >= 0);
        await using var db = new SqlConnection(connection);
        var original = await db.QuerySingleAsync<(bool? Perecedero, double? Largo, bool UsaLote)>(
            "SELECT Perecedero, Largo, USALOTE FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO))=@a", new { a = articulo.Codigo });
        var clave = await db.ExecuteScalarAsync<string>("SELECT VALOR FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE)))='USAPERECEDEROS'");
        var contador = await db.ExecuteScalarAsync<int?>("SELECT X_ULTIMO_NRO FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='IR' AND UPPER(LTRIM(RTRIM(SISTEMA)))='STOCK' AND ISNULL(Talonario,0)=0");
        string? comprobante = null;
        try
        {
            await db.ExecuteAsync("UPDATE dbo.V_MA_ARTICULOS SET Perecedero=1, Largo=10 WHERE LTRIM(RTRIM(IDARTICULO))=@a", new { a = articulo.Codigo });
            await db.ExecuteAsync("UPDATE dbo.TA_CONFIGURACION SET VALOR='SI' WHERE UPPER(LTRIM(RTRIM(CLAVE)))='USAPERECEDEROS'");
            var plan = await movimientos.CalcularAsync(new StockMovimientoBorrador { Tc = "IR", UnidadNegocio = unidad, Fecha = DateTime.Today, Deposito = deposito,
                Lineas = [new() { Articulo = articulo.Codigo, Cantidad = 3 }] });
            Assert.Equal(DateTime.Today.AddDays(10), plan.Lineas[0].Vencimiento);
            var ir = await movimientos.GrabarAsync(new StockMovimientoBorrador { Tc = "IR", UnidadNegocio = unidad, Fecha = DateTime.Today, Deposito = deposito,
                Lineas = [new() { Articulo = articulo.Codigo, Cantidad = 3, Vencimiento = DateTime.Today.AddDays(5) }] }, Guid.NewGuid());
            comprobante = ir.Comprobante;
            Assert.Equal(DateTime.Today.AddDays(5).ToString("yyyyMMdd"), await db.ExecuteScalarAsync<string>(
                "SELECT LTRIM(RTRIM(NroLote)) FROM dbo.V_MV_STOCK WHERE TC='IR' AND IDCOMPROBANTE=@c", new { c = comprobante }));
            var cargado = await movimientos.GetComprobanteAsync("IR", comprobante);
            Assert.Equal(DateTime.Today.AddDays(5), cargado.Borrador.Lineas[0].Vencimiento);

            var perecederos = await reportes.GetPerecederosAsync(new StockReporteFiltro { Texto = articulo.Codigo, Deposito = deposito, DiasPorVencer = 7, TamanoPagina = 200 });
            var fila = perecederos.Filas.First(x => x.Articulo == articulo.Codigo && x.Vencimiento == DateTime.Today.AddDays(5));
            Assert.Equal("PORVENCER", fila.Estado);
            Assert.False(fila.Estimado);
            Assert.Equal(3m, fila.Cantidad);
        }
        finally
        {
            if (comprobante is not null)
                await db.ExecuteAsync("""
                    DELETE FROM dbo.V_MV_STOCK WHERE TC='IR' AND IDCOMPROBANTE=@c;
                    DELETE FROM dbo.ALFACORE_STOCK_MOV_OPERACION WHERE TC='IR' AND IdComprobante=@c;
                    IF OBJECT_ID('dbo.V_MV_CPTEACCIONES') IS NOT NULL DELETE FROM dbo.V_MV_CPTEACCIONES WHERE TC='IR' AND IDCOMPROBANTE=@c AND SYSTEMUSER='AlfaCore Stock';
                    IF OBJECT_ID('dbo.V_MV_CTROL_CPTE') IS NOT NULL DELETE FROM dbo.V_MV_CTROL_CPTE WHERE LTRIM(RTRIM(TC))='IR' AND SUCURSAL=LEFT(@c,4) AND NUMERO=SUBSTRING(@c,5,8) AND LETRA=RIGHT(@c,1);
                    """, new { c = comprobante });
            await db.ExecuteAsync("UPDATE dbo.V_TA_CPTE SET X_ULTIMO_NRO=@n WHERE LTRIM(RTRIM(CODIGO))='IR' AND UPPER(LTRIM(RTRIM(SISTEMA)))='STOCK' AND ISNULL(Talonario,0)=0", new { n = contador });
            await db.ExecuteAsync("UPDATE dbo.V_MA_ARTICULOS SET Perecedero=@p, Largo=@l, USALOTE=@u WHERE LTRIM(RTRIM(IDARTICULO))=@a",
                new { p = original.Perecedero, l = original.Largo, u = original.UsaLote, a = articulo.Codigo });
            await db.ExecuteAsync("UPDATE dbo.TA_CONFIGURACION SET VALOR=@v WHERE UPPER(LTRIM(RTRIM(CLAVE)))='USAPERECEDEROS'", new { v = clave });
        }
    }
}

using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed class StockMovimientosCalculoTests
{
    [Theory]
    [InlineData("IR", "E", 1)]
    [InlineData("VS", "S", -1)]
    [InlineData("TR", "S", -1)]
    [InlineData("IR", "", 1)]   // sin ES: fijo del legacy
    [InlineData("VS", null, -1)]
    [InlineData("TR", "", -1)]
    [InlineData("AJP", "E", 1)] // la diferencia ya trae el signo
    [InlineData("SI", "S", 1)]
    public void SignoComoElLegacy(string tc, string? es, int esperado) => Assert.Equal(esperado, StockMovimientosCalculo.Signo(tc, es));

    [Fact]
    public void AjusteGrabaLaDiferenciaContraElSaldoALaFecha()
    {
        Assert.Equal(5m, StockMovimientosCalculo.Movimiento("AJP", "E", 15m, 10m));
        Assert.Equal(-3.5m, StockMovimientosCalculo.Movimiento("AJP", "E", 6.5m, 10m));
        Assert.Equal(0m, StockMovimientosCalculo.Movimiento("SI", "E", 10m, 10m));
        Assert.Equal(12m, StockMovimientosCalculo.Movimiento("SI", "E", 0m, -12m));   // saldo negativo: contado cero lo lleva a cero
        Assert.Equal(-4m, StockMovimientosCalculo.Movimiento("VS", "S", 4m, 100m));
        Assert.Equal(4m, StockMovimientosCalculo.Movimiento("IR", "E", 4m, 100m));
    }

    [Fact]
    public void CantidadesValidas()
    {
        Assert.Null(StockMovimientosCalculo.ValidarCantidad("AJP", 0m));   // contado cero es válido
        Assert.NotNull(StockMovimientosCalculo.ValidarCantidad("AJP", -1m));
        Assert.NotNull(StockMovimientosCalculo.ValidarCantidad("IR", 0m));
        Assert.NotNull(StockMovimientosCalculo.ValidarCantidad("VS", 1.23456m));
        Assert.NotNull(StockMovimientosCalculo.ValidarCantidad("TR", 10000000m));
        Assert.Null(StockMovimientosCalculo.ValidarCantidad("TR", 1.2345m));
    }

    [Fact]
    public void NumeracionComoObtenerNroComprobante()
    {
        Assert.Equal("000100000031X", StockMovimientosCalculo.Comprobante(1, 31, "X"));
        Assert.Equal("X", StockMovimientosCalculo.Letra(""));
        Assert.Equal("X", StockMovimientosCalculo.Letra(" x "));
        Assert.Equal("R", StockMovimientosCalculo.Letra("R"));
        Assert.Equal("X", StockMovimientosCalculo.Letra("Z"));   // sin columnas de numeración
        Assert.Equal(31, StockMovimientosCalculo.ProximoNumero(31, 12));   // el contador guarda el próximo
        Assert.Equal(41, StockMovimientosCalculo.ProximoNumero(31, 40));   // ya hay números usados más altos
        Assert.Equal(1, StockMovimientosCalculo.ProximoNumero(null, null));
        Assert.Equal(1, StockMovimientosCalculo.ProximoNumero(0, null));
    }

    [Fact]
    public void TransferenciaGrabaSalidaYEntradaEnElMismoComprobante()
    {
        var lineas = new[]
        {
            new StockMovimientosCalculo.LineaResuelta("10", "TORNILLO", "1", 5m, -3m, 0.25m, 0, 0),
            new StockMovimientosCalculo.LineaResuelta("20", "TUERCA", "1", 2m, -4m, 0, 0, 0)
        };
        var filas = StockMovimientosCalculo.Filas("TR", lineas, "1", "2");
        Assert.Equal([1, 2, 3, 4], filas.Select(f => f.Secuencia));
        Assert.Equal([-3m, -4m, 3m, 4m], filas.Select(f => f.Cantidad));
        Assert.Equal(["1", "1", "2", "2"], filas.Select(f => f.Deposito));
        Assert.Equal(["2", "2", "1", "1"], filas.Select(f => f.DepositoDestino));
        Assert.Equal(-0.25m, filas[0].Bultos);
        Assert.Equal(0.25m, filas[2].Bultos);
        Assert.Equal(0m, filas.Sum(f => f.Cantidad));
    }

    [Fact]
    public void AjusteUnaFilaPorArticuloConElSignoDeLaDiferencia()
    {
        var filas = StockMovimientosCalculo.Filas("AJP", [
            new("10", "A", "1", 1m, 5m, 0.5m, 0, 0),
            new("20", "B", "1", 1m, -2m, 0.2m, 0, 0),
            new("30", "C", "1", 1m, 0m, 0, 0, 0)], "1", null);
        Assert.Equal(3, filas.Count);
        Assert.All(filas, f => Assert.Null(f.DepositoDestino));
        Assert.Equal([0.5m, -0.2m, 0m], filas.Select(f => f.Bultos));
    }
}

/// <summary>
/// Ciclo completo contra una base de pruebas (STOCK_MOV_TEST_SQL): aplica el script de actualización, graba un AJP que deja
/// el saldo igual a lo contado, lo modifica, lo anula, y hace una TR con salida y entrada. Borra lo que grabó y repone el contador.
/// Usuario: STOCK_MOV_TEST_USER (por defecto Administrador), con unidad de negocio y depósitos de esa unidad.
/// </summary>
public sealed class StockMovimientosSqlTests
{
    [StockMovimientosSqlFact]
    public async Task AjusteModificacionAnulacionYTransferenciaComoElLegacy()
    {
        var connection = Environment.GetEnvironmentVariable("STOCK_MOV_TEST_SQL")!;
        var usuario = Environment.GetEnvironmentVariable("STOCK_MOV_TEST_USER") is { Length: > 0 } u ? u : "Administrador";
        await using (var cn = new SqlConnection(connection))
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AlfaCore.sln"))) dir = dir.Parent;
            var sql = await File.ReadAllTextAsync(Path.Combine(dir!.FullName, "src/AlfaCore/App_Data/updates/2026-11-11-003__stock_movimientos_menu_web.sql"));
            await cn.ExecuteAsync(sql); await cn.ExecuteAsync(sql);   // idempotente
            Assert.Equal("/stock/movimientos", await cn.ExecuteScalarAsync<string>("SELECT RutaWeb FROM dbo.ALFACORE_MENU_WEB WHERE Menu='ALFA' AND Clave='D80-MOVSTOCK'"));
            Assert.True(await cn.ExecuteScalarAsync<bool>("SELECT HabilitadoWeb FROM dbo.ALFACORE_MENU_WEB WHERE Menu='ALFA' AND Clave='D80'"));
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ALFACORE_MENU_WEB WHERE Menu='ALFA' AND Clave='D80-MOVSTOCK'"));
        }

        var service = new StockMovimientosService(new PlanCuentasAbmSqlTests.Sesion(connection), new PlanCuentasAbmSqlTests.Usuario(usuario),
            new PlanCuentasAbmSqlTests.Permisos(), new PlanCuentasAbmSqlTests.Eventos());
        var ctx = await service.GetContextoAsync();
        var unidad = ctx.UnidadNegocio.Length > 0 ? ctx.UnidadNegocio : ctx.UnidadesNegocio[0].Codigo;
        var depositos = ctx.Depositos.Where(d => (d.Compartido || d.UnidadNegocio == unidad) && d.Empresa.Length == 0).ToList();
        Assert.NotEmpty(depositos);
        var deposito = depositos[0].Codigo;
        var articulo = (await service.BuscarArticulosAsync("", "AJP", deposito)).First(a => a.Restriccion.Length == 0);
        var creados = new List<(string Tc, string Comprobante)>();
        var contadores = await Contadores(connection);
        try
        {
            // AJP: lo contado queda como saldo del depósito a la fecha.
            var fecha = DateTime.Today;
            var plan = await service.CalcularAsync(Borrador("AJP", unidad, fecha, deposito, articulo.Codigo, 0m));
            var saldo = plan.Lineas[0].Saldo;
            var contado = Math.Max(0, decimal.Truncate(saldo)) + 5;
            var ajuste = await service.GrabarAsync(Borrador("AJP", unidad, fecha, deposito, articulo.Codigo, contado, saldo), Guid.NewGuid());
            creados.Add(("AJP", ajuste.Comprobante));
            Assert.Equal(contado, await Saldo(connection, articulo.Codigo, deposito, fecha));
            Assert.Equal(contado - saldo, await Cantidad(connection, "AJP", ajuste.Comprobante));

            // La misma operación dos veces no graba dos comprobantes.
            var op = Guid.NewGuid();
            var doble1 = await service.GrabarAsync(Borrador("AJP", unidad, fecha, deposito, articulo.Codigo, contado, contado), op);
            creados.Add(("AJP", doble1.Comprobante));
            var doble2 = await service.GrabarAsync(Borrador("AJP", unidad, fecha, deposito, articulo.Codigo, contado, contado), op);
            Assert.Equal(doble1.Comprobante, doble2.Comprobante);

            // Saldo cambiado entre el cálculo y la grabación: pide recalcular.
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
                service.GrabarAsync(Borrador("AJP", unidad, fecha, deposito, articulo.Codigo, contado, contado - 1), Guid.NewGuid()));

            // Modificar: se recupera lo contado y la diferencia se recalcula sin contar el propio comprobante.
            var cargado = await service.GetComprobanteAsync("AJP", ajuste.Comprobante);
            Assert.Equal(contado, cargado.Borrador.Lineas[0].Cantidad);
            cargado.Borrador.Lineas[0].Cantidad = contado + 2;
            var mod = await service.GrabarAsync(cargado.Borrador, Guid.NewGuid());
            Assert.Equal("M", mod.Accion);
            Assert.Equal(ajuste.Comprobante, mod.Comprobante);
            Assert.Equal(contado + 2, await Saldo(connection, articulo.Codigo, deposito, fecha));

            // Anular: deja de contar.
            var anulado = await service.AnularAsync("AJP", ajuste.Comprobante, Guid.NewGuid());
            Assert.Equal("B", anulado.Accion);
            Assert.Equal(saldo, await Saldo(connection, articulo.Codigo, deposito, fecha)); // el segundo AJP tenía diferencia 0
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.GetComprobanteAsync("AJP", ajuste.Comprobante));

            // TR: salida del origen y entrada al destino, mismo comprobante.
            if (depositos.Count > 1 && ctx.Tipos.Any(t => t.Tc == "TR"))
            {
                var destino = depositos[1].Codigo;
                var antesDestino = await Saldo(connection, articulo.Codigo, destino, null);
                var tr = await service.GrabarAsync(new StockMovimientoBorrador { Tc = "TR", UnidadNegocio = unidad, Fecha = fecha, Deposito = deposito,
                    DepositoDestino = destino, Lineas = [new() { Articulo = articulo.Codigo, Cantidad = 1 }] }, Guid.NewGuid());
                creados.Add(("TR", tr.Comprobante));
                Assert.Equal(antesDestino + 1, await Saldo(connection, articulo.Codigo, destino, null));
                Assert.Equal(0m, await Cantidad(connection, "TR", tr.Comprobante));
                var trCargado = await service.GetComprobanteAsync("TR", tr.Comprobante);
                Assert.Equal(destino, trCargado.Borrador.DepositoDestino);
                Assert.Equal(1m, trCargado.Borrador.Lineas[0].Cantidad);
                await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.GrabarAsync(new StockMovimientoBorrador { Tc = "TR", UnidadNegocio = unidad,
                    Fecha = fecha, Deposito = deposito, DepositoDestino = deposito, Lineas = [new() { Articulo = articulo.Codigo, Cantidad = 1 }] }, Guid.NewGuid()));
            }
        }
        finally
        {
            await using var cn = new SqlConnection(connection);
            foreach (var (tc, cpte) in creados.Distinct())
                await cn.ExecuteAsync("""
                    DELETE FROM dbo.V_MV_STOCK WHERE TC=@tc AND IDCOMPROBANTE=@cpte;
                    DELETE FROM dbo.ALFACORE_STOCK_MOV_OPERACION WHERE TC=@tc AND IdComprobante=@cpte;
                    IF OBJECT_ID('dbo.V_MV_CPTEACCIONES') IS NOT NULL DELETE FROM dbo.V_MV_CPTEACCIONES WHERE TC=@tc AND IDCOMPROBANTE=@cpte AND SYSTEMUSER='AlfaCore Stock';
                    IF OBJECT_ID('dbo.V_MV_CTROL_CPTE') IS NOT NULL DELETE FROM dbo.V_MV_CTROL_CPTE WHERE LTRIM(RTRIM(TC))=@tc AND SUCURSAL=LEFT(@cpte,4) AND NUMERO=SUBSTRING(@cpte,5,8) AND LETRA=RIGHT(@cpte,1);
                    """, new { tc, cpte });
            foreach (var c in contadores)
                await cn.ExecuteAsync("UPDATE dbo.V_TA_CPTE SET X_ULTIMO_NRO=@Ultimo WHERE LTRIM(RTRIM(CODIGO))=@Codigo AND ISNULL(Talonario,0)=@Talonario AND UPPER(LTRIM(RTRIM(SISTEMA)))='STOCK';", c);
        }
    }

    private static StockMovimientoBorrador Borrador(string tc, string unidad, DateTime fecha, string deposito, string articulo, decimal cantidad, decimal? saldo = null)
        => new() { Tc = tc, UnidadNegocio = unidad, Fecha = fecha, Deposito = deposito, Lineas = [new() { Articulo = articulo, Cantidad = cantidad, SaldoEsperado = saldo }] };

    private static async Task<decimal> Saldo(string connection, string articulo, string deposito, DateTime? hasta)
    {
        await using var cn = new SqlConnection(connection);
        return await cn.ExecuteScalarAsync<decimal>("""
            SELECT CONVERT(decimal(18,4),ISNULL(SUM(CantidadUD),0)) FROM dbo.V_MV_STOCK
            WHERE LTRIM(RTRIM(IDArticulo))=@articulo AND LTRIM(RTRIM(IdDeposito))=@deposito AND ISNULL(Anulado,0)=0
              AND (@hasta IS NULL OR FECHA < DATEADD(day,1,@hasta));
            """, new { articulo, deposito, hasta });
    }

    private static async Task<decimal> Cantidad(string connection, string tc, string cpte)
    {
        await using var cn = new SqlConnection(connection);
        return await cn.ExecuteScalarAsync<decimal>("SELECT CONVERT(decimal(18,4),ISNULL(SUM(CantidadUD),0)) FROM dbo.V_MV_STOCK WHERE TC=@tc AND IDCOMPROBANTE=@cpte AND ISNULL(Anulado,0)=0;", new { tc, cpte });
    }

    private static async Task<List<Contador>> Contadores(string connection)
    {
        await using var cn = new SqlConnection(connection);
        return (await cn.QueryAsync<Contador>("""
            SELECT LTRIM(RTRIM(CODIGO)) Codigo, ISNULL(Talonario,0) Talonario, X_ULTIMO_NRO Ultimo FROM dbo.V_TA_CPTE
            WHERE UPPER(LTRIM(RTRIM(SISTEMA)))='STOCK' AND LTRIM(RTRIM(CODIGO)) IN ('AJP','TR');
            """)).ToList();
    }

    private sealed class Contador { public string Codigo { get; set; } = ""; public int Talonario { get; set; } public int? Ultimo { get; set; } }
}

public sealed class StockMovimientosSqlFactAttribute : FactAttribute
{
    public StockMovimientosSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STOCK_MOV_TEST_SQL")))
            Skip = "Requiere una base de pruebas con comprobantes de Stock (AJP, TR) en STOCK_MOV_TEST_SQL; graba y borra movimientos de prueba.";
    }
}

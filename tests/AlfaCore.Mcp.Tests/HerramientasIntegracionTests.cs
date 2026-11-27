using AlfaCore.Mcp.Infra;
using AlfaCore.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace AlfaCore.Mcp.Tests;

/// <summary>Solo corre si ALFACORE_MCP_TEST_CONNECTION apunta a una base con el esquema dir ya instalado (scripts 001-004).</summary>
public sealed class FactIntegracionAttribute : FactAttribute
{
    public const string Variable = "ALFACORE_MCP_TEST_CONNECTION";
    public FactIntegracionAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Integración: definir {Variable} (base con esquema dir instalado).";
    }
}

public class HerramientasIntegracionTests
{
    private sealed class ConfigFija(McpRuntimeSettings valores) : IMcpConfigProvider
    {
        public Task<McpRuntimeSettings> GetAsync(CancellationToken ct) => Task.FromResult(valores);
    }

    private sealed class AuditoriaMemoria : IAuditLog
    {
        public List<AuditEntry> Entradas { get; } = new();
        public Task WriteAsync(AuditEntry e, CancellationToken ct) { Entradas.Add(e); return Task.CompletedTask; }
    }

    private static (NegocioTools Negocio, SqlTools Sql, AuditoriaMemoria Audit) Crear(bool consultarSql = true, bool habilitado = true)
    {
        var cs = Environment.GetEnvironmentVariable(FactIntegracionAttribute.Variable)!;
        var resolver = new ConexionUnicaResolver(cs);
        var cfg = new McpRuntimeSettings(habilitado, 500, 100, 120, true, consultarSql, true, 5m);
        var audit = new AuditoriaMemoria();
        var runner = new ToolRunner(new ConfigFija(cfg), audit, new UsuarioFijoContext("test"), NullLogger<ToolRunner>.Instance);
        var ejecutor = new SqlExecutor(resolver);
        return (new NegocioTools(runner, ejecutor), new SqlTools(runner, ejecutor), audit);
    }

    private static decimal Num(object? v) => Convert.ToDecimal(v);

    [FactIntegracion]
    public async Task ventas_resumen_por_mes_cuadra_con_sus_totales()
    {
        var (n, _, audit) = Crear();
        var r = await n.VentasResumen("2026-10-01", "2026-10-31", "mes");
        Assert.Single(r.Filas);
        Assert.True(Num(r.Totales!["Ventas"]) > 0);
        Assert.Equal(Num(r.Totales["Ventas"]), Num(r.Filas[0]["Ventas"]));
        Assert.Equal(Num(r.Totales["Margen"]), Num(r.Filas[0]["Margen"]));
        Assert.Contains("NETOS de IVA", r.Criterio);
        var log = Assert.Single(audit.Entradas);
        Assert.Equal("ventas_resumen", log.Herramienta);
        Assert.Null(log.Error);
        Assert.Contains("2026-10-01", log.Parametros);
    }

    [FactIntegracion]
    public async Task ventas_resumen_agrupa_por_cada_dimension_y_las_filas_suman_el_total()
    {
        var (n, _, _) = Crear();
        foreach (var g in new[] { "dia", "vendedor", "rubro", "cliente" })
        {
            var r = await n.VentasResumen("2026-10-01", "2026-10-31", g);
            Assert.NotEmpty(r.Filas);
            if (!r.Truncado)
                Assert.Equal(Num(r.Totales!["Ventas"]), r.Filas.Sum(f => Num(f["Ventas"])));
        }
    }

    [FactIntegracion]
    public async Task ranking_articulos_ordena_segun_el_criterio()
    {
        var (n, _, _) = Crear();
        var gan = await n.RankingArticulos("2026-10-01", "2026-10-31", "ganancia", 5);
        Assert.Equal(5, gan.CantidadFilas);
        Assert.Equal(gan.Filas.Select(f => Num(f["Ganancia"])).OrderByDescending(x => x), gan.Filas.Select(f => Num(f["Ganancia"])));
        var cant = await n.RankingArticulos("2026-10-01", "2026-10-31", "cantidad", 5);
        Assert.Equal(cant.Filas.Select(f => Num(f["Cantidad"])).OrderByDescending(x => x), cant.Filas.Select(f => Num(f["Cantidad"])));
        var tope = await n.RankingArticulos("2026-10-01", "2026-10-31", "facturacion", 100000);
        Assert.Contains(tope.Advertencias, a => a.Contains("máximo permitido"));
    }

    [FactIntegracion]
    public async Task comparar_periodos_calcula_variaciones()
    {
        var (n, _, _) = Crear();
        var r = await n.CompararPeriodos("2026-09-01", "2026-09-30", "2026-10-01", "2026-10-31");
        var v = (Dictionary<string, object?>)r.Totales!["Ventas"]!;
        Assert.Equal(Num(v["B"]) - Num(v["A"]), Num(v["Variacion"]));
        Assert.NotEmpty(r.Filas);
        Assert.Contains(r.Filas[0].Keys, k => k == "VariacionPct");
    }

    [FactIntegracion]
    public async Task clientes_deuda_y_stock_devuelven_datos()
    {
        var (n, _, _) = Crear();
        var c = await n.ClientesTop("2026-10-01", "2026-10-31", 5);
        Assert.Equal(5, c.CantidadFilas);
        var d = await n.DeudaClientes(minimo: 1000, top: 5);
        Assert.True(Num(d.Totales!["DeudaTotal"]) > 0);
        Assert.All(d.Filas, f => Assert.True(Num(f["Deuda"]) >= 1000));
        var s = await n.StockCritico(5);
        Assert.True(Convert.ToInt32(s.Totales!["ArticulosCriticos"]) >= s.CantidadFilas);
        Assert.All(s.Filas, f => Assert.True(Num(f["Faltante"]) >= 0));
    }

    [FactIntegracion]
    public async Task consultar_sql_ejecuta_selects_sobre_dir_y_describe_el_esquema()
    {
        var (_, sql, _) = Crear();
        var e = await sql.DescribirEsquema();
        Assert.Contains(e.Filas, f => (string)f["Vista"]! == "dir.vw_Ventas");
        var r = await sql.ConsultarSql("SELECT TOP 3 Tc, Fecha, NetoSinIva FROM dir.vw_Ventas WHERE Fecha >= '2026-10-01' ORDER BY Fecha", 2);
        Assert.Equal(2, r.CantidadFilas);
        Assert.True(r.Truncado);
    }

    [FactIntegracion]
    public async Task consultar_sql_rechaza_tablas_base_y_las_auditorias_quedan_con_error()
    {
        var (_, sql, audit) = Crear();
        var ex = await Assert.ThrowsAsync<McpException>(() => sql.ConsultarSql("SELECT TOP 1 * FROM dbo.V_MV_Cpte"));
        Assert.Contains("esquema dir", ex.Message);
        Assert.NotNull(Assert.Single(audit.Entradas).Error);
    }

    [FactIntegracion]
    public async Task La_sesion_del_MCP_lee_sin_bloqueos_y_pierde_ante_un_deadlock()
    {
        var ejecutor = new SqlExecutor(new ConexionUnicaResolver(Environment.GetEnvironmentVariable(FactIntegracionAttribute.Variable)!));
        const string sesion = "SELECT transaction_isolation_level AS Aislamiento, deadlock_priority AS PrioridadDeadlock FROM sys.dm_exec_sessions WHERE session_id = @@SPID";

        var sinBloqueos = (await ejecutor.QueryAsync(OAuthApp.Base() with { LecturaSinBloqueos = true }, sesion, null, default)).Filas.Single();
        Assert.Equal(1, Convert.ToInt32(sinBloqueos["Aislamiento"]));          // 1 = READ UNCOMMITTED
        Assert.Equal(-5, Convert.ToInt32(sinBloqueos["PrioridadDeadlock"]));   // -5 = LOW: siempre la victima

        var normal = (await ejecutor.QueryAsync(OAuthApp.Base() with { LecturaSinBloqueos = false }, sesion, null, default)).Filas.Single();
        Assert.Equal(2, Convert.ToInt32(normal["Aislamiento"]));               // 2 = READ COMMITTED
        Assert.Equal(-5, Convert.ToInt32(normal["PrioridadDeadlock"]));        // la prioridad baja se mantiene aunque se apague la lectura sucia
    }

    [FactIntegracion]
    public async Task las_herramientas_respetan_los_interruptores_de_configuracion()
    {
        var (_, apagado, _) = Crear(habilitado: false);
        await Assert.ThrowsAsync<McpException>(() => apagado.DescribirEsquema());
        var (_, sinSql, _) = Crear(consultarSql: false);
        var ex = await Assert.ThrowsAsync<McpException>(() => sinSql.ConsultarSql("SELECT 1"));
        Assert.Contains("deshabilitada", ex.Message);
    }

    [FactIntegracion]
    public async Task parametros_invalidos_dan_mensajes_claros_y_no_llegan_a_la_base()
    {
        var (n, _, _) = Crear();
        var ex = await Assert.ThrowsAsync<McpException>(() => n.VentasResumen("31/10/2026", "2026-10-31"));
        Assert.Contains("AAAA-MM-DD", ex.Message);
        await Assert.ThrowsAsync<McpException>(() => n.VentasResumen("2026-10-01", "2026-10-31", "mes; DROP TABLE x"));
    }

    private static FichaTools CrearFichas()
    {
        var cs = Environment.GetEnvironmentVariable(FactIntegracionAttribute.Variable)!;
        var cfg = new McpRuntimeSettings(true, 500, 100, 120, true, true, true, 5m);
        var runner = new ToolRunner(new ConfigFija(cfg), new AuditoriaMemoria(), new UsuarioFijoContext("test"), NullLogger<ToolRunner>.Instance);
        return new FichaTools(runner, new SqlExecutor(new ConexionUnicaResolver(cs)));
    }

    [FactIntegracion]
    public async Task busqueda_y_ficha_de_cliente_cuadran_con_las_ventas_filtradas()
    {
        var (n, _, _) = Crear();
        var f = CrearFichas();
        // El mejor cliente de octubre: se lo busca por una palabra de su nombre (del maestro) y se abre su ficha.
        var top = await n.ClientesTop("2026-10-01", "2026-10-31", 1);
        var codigo = (string)top.Filas[0]["ClienteCodigo"]!;
        var nombre = (string)top.Filas[0]["Cliente"]!;
        var palabra = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Length).First();

        var busqueda = await f.BuscarClientes(palabra, 50);
        Assert.Contains(busqueda.Filas, x => (string)x["ClienteCodigo"]! == codigo);
        var exacto = await f.BuscarClientes(codigo);
        Assert.Equal(codigo, exacto.Filas[0]["ClienteCodigo"]);   // el codigo exacto sale primero

        var ficha = await f.FichaCliente(codigo);
        Assert.Equal(codigo, ficha.Totales!["ClienteCodigo"]);
        if (!ficha.Truncado)
            Assert.Equal(Num(ficha.Totales["Deuda"]), ficha.Filas.Sum(x => Num(x["Saldo"])));

        // ventas_resumen filtrado por ese cliente = su fila en clientes_top.
        var suyo = await n.VentasResumen("2026-10-01", "2026-10-31", "comprobante", cliente: codigo);
        Assert.Equal(Num(top.Filas[0]["Ventas"]), Num(suyo.Totales!["Ventas"]));
        Assert.Contains(codigo, suyo.Criterio);

        await Assert.ThrowsAsync<McpException>(() => f.FichaCliente("NO-EXISTE-XYZ"));
    }

    [FactIntegracion]
    public async Task busqueda_y_ficha_de_articulo_cuadran_con_el_ranking()
    {
        var (n, _, _) = Crear();
        var f = CrearFichas();
        var rk = await n.RankingArticulos("2026-10-01", "2026-10-31", "facturacion", 1);
        var codigo = (string)rk.Filas[0]["ArticuloCodigo"]!;

        var exacto = await f.BuscarArticulos(codigo);
        Assert.Equal(codigo, exacto.Filas[0]["ArticuloCodigo"]);

        var ficha = await f.FichaArticulo(codigo);
        Assert.Equal(codigo, ficha.Totales!["ArticuloCodigo"]);
        Assert.Equal(Num(ficha.Totales["StockTotal"]), ficha.Filas.Sum(x => Num(x["Stock"])));

        var quien = await n.ClientesTop("2026-10-01", "2026-10-31", 100, articulo: codigo);
        Assert.Equal(Num(rk.Filas[0]["Facturacion"]), Num(quien.Totales!["Ventas"]));

        await Assert.ThrowsAsync<McpException>(() => f.BuscarArticulos("x"));
    }
}

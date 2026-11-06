using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

[Collection("BasePruebasRetail")] // misma base de pruebas: sin correr en paralelo
public sealed class PoliticasPreciosTests(ITestOutputHelper output)
{
    private static readonly PoliticaPreciosVariables Vars = new(1000m, [1100m, 1250.5m, 0m, 2420m, 2000m, 0, 0, 0], 21m, 2.5m, 0.75m, 179.18m);

    [Theory]
    [InlineData("<COSTO>%+30", 1300)]
    [InlineData("<COSTO> %+ 30 * 1.21", 1573)]
    [InlineData("<COSTO>+10*2", 2020)]                    // izquierda a derecha, sin precedencia
    [InlineData("<CLASE1>%-10", 990)]
    [InlineData("<COSTO>/1.21", 826.4462)]
    [InlineData("<COSTO>\\3", 333)]
    [InlineData("<COSTO>+<CLASE2>", 2250.5)]
    [InlineData("<COSTO>*<IVA>/100", 210)]
    [InlineData("<CLASE3>+100", 0)]                        // base 0: la base devuelve 0
    [InlineData("<costo>%+50", 1500)]                     // variables sin importar mayúsculas
    [InlineData("<COSTO>+<KILOS>*2", 2005)]
    [InlineData("<COSTO>-<IMPUESTOS>", 820.82)]
    public void EvaluaComoLaBase(string formula, double esperado)
        => Assert.Equal((decimal)esperado, decimal.Round(PoliticasPreciosFormula.Evaluar(formula, Vars), 4));

    [Theory]
    [InlineData("<COSTO>%+30", null)]
    [InlineData("<COSTO>%+30,5", null)]                   // la coma se pasa a punto
    [InlineData("COSTO+10", "empezar con una variable")]
    [InlineData("<PRECIO1>+10", "no existe")]
    [InlineData("<M3>*10", "<M3> no puede ser la variable inicial")]
    [InlineData("<COSTO>/0", "dividir por cero")]
    [InlineData("<COSTO>+", "Falta el valor")]
    [InlineData("<COSTO>*-1", "sin signo")]
    [InlineData("<COSTO>%30", "Se esperaba un operador")]
    public void ValidaFormulas(string formula, string? error)
    {
        var r = PoliticasPreciosFormula.Validar(formula);
        if (error is null) Assert.Null(r); else Assert.Contains(error, r);
    }

    /// <summary>El motor da lo mismo que NW_OBTIENEVALOR de una base real. Requiere ALFACORE_TEST_RETAIL=1 y ALFACORE_TEST_RETAIL_SQL.</summary>
    [Fact]
    public async Task CoincideConNwObtieneValor()
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL_SQL");
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL") != "1" || string.IsNullOrWhiteSpace(cs)) return;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        string[] formulas = ["<COSTO>%+30", "<COSTO> %+ 30 * 1.21", "<COSTO>+10*2", "<CLASE1>%-10", "<COSTO>/1.21", "<COSTO>\\3",
            "<COSTO>+<CLASE2>", "<COSTO>*<IVA>/100", "<CLASE3>+100", "<COSTO>+<KILOS>*2", "<COSTO>-<IMPUESTOS>", "<CLASE4>/1.21%+5",
            "<CLASE2>*1.105%-3.5+0.33", "<COSTO>*<M3>", "<COSTO>+0", "<COSTO>*1.21/3*3"];
        foreach (var f in formulas)
        {
            var p = new DynamicParameters(new
            {
                Formula = f, Costo = Vars.Costo, P1 = Vars.Precios[0], P2 = Vars.Precios[1], P3 = Vars.Precios[2], P4 = Vars.Precios[3],
                P5 = Vars.Precios[4], P6 = 0m, P7 = 0m, P8 = 0m, Iva = 21.0, Kilos = 2.5, M3 = 0.75, Imp = Vars.Impuestos
            });
            p.Add("R", dbType: System.Data.DbType.Currency, direction: System.Data.ParameterDirection.Output);
            await cn.ExecuteAsync("EXEC dbo.NW_OBTIENEVALOR @Formula,@Costo,@P1,@P2,@P3,@P4,@P5,@P6,@P7,@P8,@Iva,@Kilos,@M3,@Imp,@R OUTPUT", p);
            var sql = p.Get<decimal?>("R") ?? 0;
            var cs2 = PoliticasPreciosFormula.Evaluar(f, Vars);
            output.WriteLine($"{f,-28} base {sql,14:0.0000}  AlfaCore {cs2,14:0.0000}");
            Assert.Equal(sql, cs2);
        }
    }

    /// <summary>
    /// Vista previa y aplicación sobre artículos reales: la base (FMT_V_MA_ARTICULOS) aplica la política y AlfaCore lee el resultado.
    /// Todo dentro de transacciones revertidas. Requiere ALFACORE_TEST_RETAIL=1 y ALFACORE_TEST_RETAIL_SQL.
    /// </summary>
    [Fact]
    public async Task PrevisualizaYAplicaConLosTriggersDeLaBase()
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL_SQL");
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_RETAIL") != "1" || string.IsNullOrWhiteSpace(cs)) return;
        var svc = Servicio(cs);
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        var art = await cn.QueryFirstAsync<(string Id, decimal Costo, decimal P2)>("""
            SELECT TOP (1) LTRIM(RTRIM(IDARTICULO)), CONVERT(decimal(19,4),COSTO), CONVERT(decimal(19,4),PRECIO2) FROM dbo.V_MA_ARTICULOS
            WHERE ISNULL(SUSPENDIDO,0)=0 AND ISNULL(COSTO,0) > 100 AND ISNULL(PRECIO1,0) > 0 ORDER BY IDARTICULO;
            """);

        // Porcentajes: clase 5 = costo + 40 % (sin IVA: MaestroArticuloConIva=NO); el costo es el que deja el trigger.
        var porcentaje = new PoliticaPrecioDto { Codigo = "T1", Descripcion = "PRUEBA", Porcentajes = [0, 0, 0, 0, 40, 0, 0, 0] };
        var vista = (await svc.PrevisualizarAsync(porcentaje, art.Id)).Single();
        Assert.Null(vista.Error);
        output.WriteLine($"{art.Id}: costo {vista.CostoActual} -> {vista.CostoNuevo}; clase 5 {vista.PreciosActuales[4]} -> {vista.PreciosNuevos[4]}");
        // Clase 5 = costo que deja el trigger + 40 %. En PRUEBAS02122025 (Retail + ClasePrecioListaProv) el trigger deja el costo
        // vacío: la vista previa lo muestra tal cual, para avisar antes de aplicar.
        Assert.Equal(decimal.Round(vista.CostoNuevo * 1.4m, 2), decimal.Round(vista.PreciosNuevos[4], 2));

        // Fórmulas: clase 2 = clase 2 actual %+ 10 (single pass, como el trigger).
        var formula = new PoliticaPrecioDto { Codigo = "T2", Descripcion = "PRUEBA F" };
        formula.Formulas[1] = "<CLASE2> %+ 10";
        var vistaF = (await svc.PrevisualizarAsync(formula, art.Id)).Single();
        Assert.Null(vistaF.Error);
        Assert.Equal(decimal.Round(art.P2 * 1.1m, 2), decimal.Round(vistaF.PreciosNuevos[1], 2));
        Assert.Equal(vistaF.PreciosActuales[0], vistaF.PreciosNuevos[0]); // <CLASE1> sin cambios

        // La base quedó como estaba.
        Assert.Equal(art.P2, await cn.ExecuteScalarAsync<decimal>("SELECT CONVERT(decimal(19,4),PRECIO2) FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO))=@Id", new { art.Id }));
        Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_TA_POLITICAPRECIOS WHERE CODIGO LIKE '%99%' AND DESCRIPCION LIKE 'PRUEBA%'"));

        // Aplicar: alta de la política, asignación al artículo y recálculo, en una transacción revertida.
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        await cn.ExecuteAsync("""
            INSERT INTO dbo.V_TA_POLITICAPRECIOS (CODIGO, DESCRIPCION, CLASE1, CLASE2, CLASE3, CLASE4, CLASE5, CLASE6, CLASE7, CLASE8)
            VALUES ('ZT9', 'PRUEBA APLICAR', 0, 0, 0, 0, 40, 0, 0, 0);
            UPDATE dbo.V_MA_ARTICULOS SET POLITICAPRECIOS = 'ZT9', DesdeTrigger = 1 WHERE LTRIM(RTRIM(IDARTICULO)) = @Id;
            """, new { art.Id }, tx);
        var r = await PoliticasPreciosService.AplicarCoreAsync(cn, tx, "ZT9", "PRUEBA", CancellationToken.None);
        Assert.Equal(1, r.Articulos);
        var p5 = await cn.ExecuteScalarAsync<decimal>("SELECT CONVERT(decimal(19,4),PRECIO5) FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO))=@Id", new { art.Id }, tx);
        Assert.Equal(decimal.Round(vista.PreciosNuevos[4], 2), decimal.Round(p5, 2));
        await tx.RollbackAsync();
    }

    private static PoliticasPreciosService Servicio(string cs)
    {
        var sesion = new SessionDto();
        var sessions = Proxy<ISessionService>((m, _) => m.Name switch { "GetConnectionString" => cs, "GetActiveSession" => sesion, _ => null });
        var users = Proxy<IAppUserSessionService>((m, _) => m.Name switch
        {
            "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => "PRUEBA-ALFACORE", _ => null
        });
        var permisos = Proxy<IPermissionService>((m, _) => m.Name == "HasAccessAsync" ? Task.FromResult(true) : null);
        var eventos = Proxy<IAppEventService>((m, _) => m.ReturnType == typeof(Task<string>) ? Task.FromResult("") : null);
        return new PoliticasPreciosService(sessions, users, permisos, eventos);
    }

    private static T Proxy<T>(Func<System.Reflection.MethodInfo, object?[]?, object?>? handler = null) where T : class
    {
        var proxy = System.Reflection.DispatchProxy.Create<T, CargaComprobantesRetailTests.RetailProxy>();
        ((CargaComprobantesRetailTests.RetailProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}

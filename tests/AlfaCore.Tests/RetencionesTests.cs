using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class RetencionesTests
{
    [Theory]
    [InlineData("CAPITAL", "", "901")]
    [InlineData("BUENOS AIRES.", "", "902")]
    [InlineData("RIO NEGRO", "", "916")]
    [InlineData("Río Negro", "", "916")]
    [InlineData("SANTIAGO DEL ESTERO", "", "922")]
    [InlineData("SANTA FE", "", "921")]
    [InlineData("SANTA CRUZ", "", "920")]
    [InlineData("TIERRA DEL FUEGO", "", "923")]
    [InlineData("CUALQUIERA", "915", "915")]
    [InlineData(".", "", "")]
    public void JurisdiccionSifereDesdeLaProvincia(string provincia, string codigo, string esperado)
        => Assert.Equal(esperado, RetencionesService.JurisdiccionDe(provincia, codigo));

    [Fact]
    public void FormatosDeCampos()
    {
        Assert.Equal("30-71800814-6", RetencionesService.CuitGuiones("30718008146"));
        Assert.Equal("0000000000000", RetencionesService.CuitGuiones("123"));
        Assert.Equal("00001234,56", RetencionesService.ImporteSifere(1234.56m));
        Assert.Equal("-0001234,56", RetencionesService.ImporteSifere(-1234.56m));
        Assert.Equal("00000123", RetencionesService.Num("123", 8));
        Assert.Equal("5678", RetencionesService.Num("12345678", 4));
    }

    private static RetencionFila Ret(string impuesto, string origen, string tc, decimal importe, string jur = "") => new()
    {
        Fecha = new(2026, 10, 5), FechaContable = new(2026, 10, 5), Tc = tc, Sucursal = "0001", Numero = "00007548", Letra = tc == "CB" ? "X" : "A",
        Impuesto = impuesto, Origen = origen, Clase = origen is "Compra" ? "Percepción" : "Retención", Sufrida = true,
        Cuit = "30718008146", CondicionIva = "1", Importe = importe, Jurisdiccion = jur, Contraparte = "112010001"
    };

    [Fact]
    public void SifereRetencionesPorJurisdiccionCon79Caracteres()
    {
        var cfg = new Dictionary<string, string> { ["CONDIVA"] = "1", [RetencionesService.ClaveJurisdiccion.ToUpperInvariant()] = "916" };
        var archivos = RetencionesService.SifereRetenciones([Ret("IIBB", "Cobranza", "CB", 7211.57m), Ret("IIBB", "Cobranza", "CB", 100m, "902"),
            Ret("IVA", "Cobranza", "CB", 50m)], cfg, "OCT2026");
        Assert.Equal(["RETENCION_OCT2026_JUR902.TXT", "RETENCION_OCT2026_JUR916.TXT"], archivos.Select(a => a.Nombre));
        var linea = archivos[1].Contenido.TrimEnd('\r', '\n');
        Assert.Equal(79, linea.Length);
        Assert.Equal("916" + "30-71800814-6" + "05/10/2026" + "0001" + "0000000000007548" + "R" + "A" + "0001" + "0000000000007548" + "00007211,57", linea);

        Assert.ThrowsAny<InvalidOperationException>(() => RetencionesService.SifereRetenciones([Ret("IIBB", "Cobranza", "CB", 1m)],
            new Dictionary<string, string>(), "OCT2026"));
    }

    [Fact]
    public void SiferePercepcionesUsaElDetallePorProvincia()
    {
        var fila = Ret("IIBB", "Compra", "FCC", 300m, "916");
        var detalle = new Dictionary<(string, string, string), List<(string, decimal)>>
        { [("FCC", "000100007548A", "112010001")] = [("901", 100m), ("902", 200m)] };
        var archivos = RetencionesService.SiferePercepciones([fila], detalle, new Dictionary<string, string>(), "OCT2026");
        Assert.Equal(["PERCEPCION_OCT2026_JUR901.TXT", "PERCEPCION_OCT2026_JUR902.TXT"], archivos.Select(a => a.Nombre));
        var linea = archivos[1].Contenido.TrimEnd('\r', '\n');
        Assert.Equal(51, linea.Length);
        Assert.EndsWith("0001" + "00007548" + "FA" + "00000200,00", linea);
    }

    [Fact]
    public void IvaSufridasF2002Con56Caracteres()
    {
        var txt = RetencionesService.IvaSufridas([Ret("IVA", "Compra", "FCC", 14168.04m), Ret("IVA", "Cobranza", "CB", 3254.77m)], retenciones: false, "767");
        var lineas = txt.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lineas);
        Assert.Equal(56, lineas[0].Length);
        Assert.Equal("767" + "30718008146" + "05/10/2026" + "00001" + "00000007548" + " 000000014168,04", lineas[0]);
        Assert.ThrowsAny<InvalidOperationException>(() => RetencionesService.IvaSufridas([], true, ""));
    }
}

/// <summary>Contra ALFANET2007_PRUEBAS (solo lectura). Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class RetencionesPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task LeeLasSufridasDelPeriodoYExporta()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Alberto'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        var svc = new RetencionesService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events), runtime.Events);

        // Último mes con retenciones de IIBB en cobranzas.
        var ultima = await cn.QuerySingleAsync<DateTime>("""
            SELECT MAX(a.FECHA) FROM dbo.MV_ASIENTOS a JOIN dbo.MA_CUENTAS c ON c.CODIGO=a.CUENTA
            WHERE LTRIM(RTRIM(c.MedioDePago))='RB' AND LTRIM(RTRIM(a.TC))='CB' AND LEFT(a.CUENTA,1)='1';
            """);
        var desde = new DateTime(ultima.Year, ultima.Month, 1);
        var filtro = new RetencionesFiltro { Desde = desde, Hasta = desde.AddMonths(1).AddDays(-1), Alcance = "TODAS" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await svc.GetAsync(filtro);
        output.WriteLine($"{r.Filas.Count} movimientos en {sw.ElapsedMilliseconds} ms");
        foreach (var t in r.Totales) output.WriteLine($"  {(t.Sufrida ? "Sufrida" : "Practicada")} {t.Impuesto} {t.Clase}: {t.Cantidad} · {t.Importe:N2}");
        foreach (var a in r.Avisos) output.WriteLine("  Aviso: " + a);
        foreach (var x in r.Filas.Take(10)) output.WriteLine($"  {x.Fecha:dd/MM} {x.Impuesto} {x.Origen} {x.Comprobante} {x.Cuit} {x.Nombre} {x.JurisdiccionNombre} {x.Importe:N2} base {x.Base:N2}");
        Assert.NotEmpty(r.Filas);
        Assert.All(r.Filas, x => Assert.True(x.Cuenta.StartsWith('1') == x.Sufrida));
        Assert.Contains(r.Filas, x => x.Origen == "Cobranza" && x.Impuesto == "IIBB");
        Assert.True((await svc.ExportarAsync(filtro, "EXCEL")).Contenido.Length > 3000);
    }
}

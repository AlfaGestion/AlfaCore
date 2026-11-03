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

    private static RetencionPago Pago(string tc = "OPG", string id = "02", decimal retencion = 1234.56m) => new()
    {
        Tc = tc, Sucursal = "0001", Numero = "00000123", Letra = "X", Fecha = new(2026, 10, 7), Cuenta = "211010001", Nombre = "PROVEEDOR ÑANDÚ S.A.",
        Domicilio = "Av. Siempre Viva 742", Localidad = "General Roca", Provincia = "RIO NEGRO", CodigoPostal = "8332", Cuit = "30-71800814-6",
        CondicionIva = "1", Importe = 100000m, ImporteSinIva = 82644.63m, IdRetencion = id, Base = 61722.31m, Retencion = retencion, AlicuotaIva = "21"
    };

    private static PercepcionVenta Venta(string tc = "FC", string letra = "A") => new()
    {
        Tc = tc, Sucursal = "0003", Numero = "00004567", Letra = letra, Fecha = new(2026, 10, 9), FechaContable = new(2026, 10, 9), Cuit = "20123456786",
        Nombre = "Cliente Peña", CondicionIva = "1", DocumentoTipo = "1", SituacionIb = "00", NetoGravado = 1000m, AlicuotaIva = 21m, Iva = 210m,
        PercepcionIibb = 30m, Total = 1240m, Provincia = "BUENOS AIRES"
    };

    [Fact]
    public void SicoreGananciasCon145CaracteresYRegimenObligatorio()
    {
        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["RETGANCR_02"] = "78" };
        var linea = RetencionesService.SicoreGanancias([Pago()], cfg).TrimEnd('\r', '\n');
        Assert.Equal(145, linea.Length);
        Assert.Equal("06" + "07/10/2026" + "000100000123X   " + "0000000082644,63" + "0217" + "078" + "1" + "00000061722,31" + "07/10/2026" + "01" + "0"
            + "00000001234,56" + "      " + "          " + "80" + "30718008146".PadRight(20) + new string(' ', 14), linea);
        var ex = Assert.ThrowsAny<InvalidOperationException>(() => RetencionesService.SicoreGanancias([Pago(id: "05")], cfg));
        Assert.Contains("05", ex.Message);
    }

    [Fact]
    public void SicoreSujetosUnoPorCuitCon83Caracteres()
    {
        var lineas = RetencionesService.SicoreSujetos([Pago(), Pago(tc: "PG")]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lineas);
        Assert.Equal(83, lineas[0].Length);
        Assert.Equal("30718008146" + "PROVEEDOR NANDU S.A." + "Av. Siempre Viva 742" + "General Roca".PadRight(20) + "22" + "8332    " + "80", lineas[0]);
    }

    [Fact]
    public void SicorePercepcionIvaCon145Caracteres()
    {
        var linea = RetencionesService.SicorePercepcionIva([Pago(tc: "NC", retencion: 300m)]).TrimEnd('\r', '\n');
        Assert.Equal(145, linea.Length);
        Assert.StartsWith("03" + "07/10/2026" + "0000100000000123" + "0000000100000,00" + "0767" + "602" + "2" + "00000099700,00" + "07/10/2026" + "13" + "0" + "00000000300,00" + "  0,00", linea);
    }

    [Fact]
    public void ArbaRetencionesYPercepciones()
    {
        var ret = RetencionesService.ArbaRetenciones([Pago()]).TrimEnd('\r', '\n');
        Assert.Equal(47, ret.Length);
        Assert.Equal("30-71800814-6" + "07/10/2026" + "0001" + "00000123" + "00001234,56" + "A", ret);

        var perc = RetencionesService.ArbaPercepciones([Venta(), Venta("NC")]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.All(perc, l => Assert.Equal(71, l.Length));
        Assert.Equal("20-12345678-6" + "09/10/2026" + "F" + "A" + "00003" + "00004567" + "00000001000.00" + "21,00" + "0000000030.00" + "A", perc[0]);
        Assert.Contains("-0000001000.00" + "21,00" + "-000000030.00", perc[1]);

        Assert.Equal("AR-30718008146-2026100-6201-LOTEOCTUBRE1", RetencionesService.NombreArba("30-71800814-6", new(2026, 10, 1), "SI", "6201"));
        Assert.Equal("AR-30718008146-2026102-6201-LOTEOCTUBRE2", RetencionesService.NombreArba("30718008146", new(2026, 10, 16), "NO", "6201"));
        Assert.ThrowsAny<InvalidOperationException>(() => RetencionesService.NombreArba("30718008146", new(2026, 10, 1), "SI", ""));
    }

    [Fact]
    public void ArcibaCon226CaracteresSinNotasDeCredito()
    {
        var lineas = RetencionesService.Arciba([Venta(), Venta("NC")]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var l = Assert.Single(lineas);
        Assert.Equal(226, l.Length);
        Assert.StartsWith("2" + "029" + "09/10/2026" + "01" + "A" + "0000000000004567" + "09/10/2026" + "0000000001210,00" + new string(' ', 16) + "3" + "20123456786"
            + "1" + "20123456786" + "1" + "Cliente Pena".PadRight(30) + "0000000000000,00" + "0000000000210,00" + "0000000001000,00" + "03,00"
            + "0000000000030,00" + "0000000000030,00", l);
    }

    [Fact]
    public void SircarCsvPorJurisdiccion()
    {
        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [RetencionesService.ClaveSircarRegimen] = "1" };
        var ret = RetencionesService.SircarRetenciones([Pago(), Pago(tc: "PG")], cfg, "OCT2026");
        var a = Assert.Single(ret);
        Assert.Equal("RETENCION_OCT2026_JUR916.TXT", a.Nombre);
        Assert.Equal("1,1,1,000100000123,30718008146,07/10/2026,61722.31,2.00,1234.56,001,916\r\n2,1,1,000100000123,30718008146,07/10/2026,61722.31,2.00,1234.56,001,916\r\n", a.Contenido);

        cfg[RetencionesService.ClaveSircarJurisdiccion] = "904";
        var perc = RetencionesService.SircarPercepciones([Venta(), Venta("NC")], cfg, "OCT2026");
        var p = Assert.Single(perc);
        Assert.Equal("PERCEPCION_OCT2026_JUR904.TXT", p.Nombre);
        Assert.Equal("1,1,A,000300004567,20123456786,09/10/2026,1000.00,3.00,30.00,001,904\r\n2,102,A,000300004567,20123456786,09/10/2026,-1000.00,3.00,-30.00,001,904\r\n", p.Contenido);

        Assert.ThrowsAny<InvalidOperationException>(() => RetencionesService.SircarRetenciones([Pago()], new Dictionary<string, string>(), "OCT2026"));
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

        var config = await svc.GetConfigAsync();
        Assert.NotEmpty(config.Sicore);
        Assert.All(config.Sicore, s => Assert.Equal(s.IdRetencion.Trim(), s.IdRetencion));

        // Practicadas: las consultas corren; PRUEBAS no tiene retenciones de agente, así que se espera un archivo o un aviso, nunca un error de SQL.
        foreach (var formato in RetencionesService.FormatosPracticadas)
        {
            try { output.WriteLine($"  {formato}: {(await svc.ExportarAsync(filtro, formato)).Nombre}"); }
            catch (AppUserFacingException ex) { Assert.Fail($"{formato}: {ex.InnerException?.Message ?? ex.Message}"); }
            catch (InvalidOperationException ex) { output.WriteLine($"  {formato}: {ex.Message}"); }
        }
    }
}

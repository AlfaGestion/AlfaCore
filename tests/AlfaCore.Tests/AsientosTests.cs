using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class AsientosTests
{
    private static readonly Dictionary<string, AsientoCuenta> Cuentas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["511"] = new("511", "Sueldos", "", false), ["211"] = new("211", "Sueldos a pagar", "", false),
        ["112CL1"] = new("112CL1", "Cliente Uno", "", false), ["111CH"] = new("111CH", "Cheques de terceros", "CH", false),
        ["214"] = new("214", "Documentos a pagar", "", true)
    };
    private static readonly HashSet<string> Terceros = new(["112CL1"], StringComparer.OrdinalIgnoreCase);

    private static AsientoDetalle A(params (string Cuenta, decimal Debe, decimal Haber)[] r) => new()
    {
        Fecha = new DateTime(2026, 10, 31), Detalle = "Sueldos octubre", Renglones = r.Select(x => new AsientoRenglon { Cuenta = x.Cuenta, Debe = x.Debe, Haber = x.Haber }).ToList()
    };

    [Fact]
    public void AsientoBalanceadoPasaYDescartaRenglonesVacios()
    {
        var a = A(("511", 1000, 0), ("211", 0, 1000), ("", 0, 0));
        AsientosService.ValidarRenglones(a, Cuentas, Terceros);
        Assert.Equal(2, a.Renglones.Count);
    }

    [Theory]
    [InlineData("no balancea")]
    [InlineData("debe y haber")]
    [InlineData("cuenta inexistente")]
    [InlineData("cheques")]
    [InlineData("cliente repetido")]
    [InlineData("un solo renglon")]
    [InlineData("negativo")]
    [InlineData("tres decimales")]
    [InlineData("vencimiento")]
    [InlineData("sin detalle")]
    public void ValidacionesDelLegacy(string caso)
    {
        var a = caso switch
        {
            "no balancea" => A(("511", 1000, 0), ("211", 0, 999)),
            "debe y haber" => A(("511", 1000, 1000), ("211", 0, 1000)),
            "cuenta inexistente" => A(("999", 1000, 0), ("211", 0, 1000)),
            "cheques" => A(("111CH", 1000, 0), ("211", 0, 1000)),
            "cliente repetido" => A(("112CL1", 500, 0), ("112CL1", 500, 0), ("211", 0, 1000)),
            "un solo renglon" => A(("511", 1000, 0)),
            "negativo" => A(("511", -10, 0), ("211", 0, -10)),
            "tres decimales" => A(("511", 10.005m, 0), ("211", 0, 10.005m)),
            "vencimiento" => A(("214", 0, 1000), ("511", 1000, 0)),
            _ => A(("511", 1000, 0), ("211", 0, 1000))
        };
        if (caso == "sin detalle") a.Detalle = " ";
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarRenglones(a, Cuentas, Terceros));
    }

    [Fact]
    public void CuentaQuePideVencimientoPasaConVencimiento()
    {
        var a = A(("214", 0, 1000), ("511", 1000, 0));
        a.Vencimiento = new DateTime(2026, 11, 30);
        AsientosService.ValidarRenglones(a, Cuentas, Terceros);
    }

    private static List<(string, string, string, bool, string, string, bool, bool, string, string, string)> R(string tc, string tipoReg = "", string liva = "",
        bool conciliado = false, string obs = "", bool cierre = false, string medio = "")
        => [(tc, tipoReg, liva, conciliado, obs, "", cierre, false, "1", tc + "000100000001X", medio), (tc, tipoReg, liva, conciliado, obs, "", cierre, false, "1", tc + "000100000001X", "")];

    [Fact]
    public void SoloSeModificanLosAsientosManuales()
    {
        string[] manuales = ["CC", "CJA", "VR"];
        Assert.Equal("", AsientosService.MotivoSoloLectura(R("CC"), manuales));
        Assert.Contains("compras", AsientosService.MotivoSoloLectura(R("FCC", liva: "Compras"), manuales));
        Assert.Contains("ventas", AsientosService.MotivoSoloLectura(R("FC", liva: "Ventas"), manuales));
        Assert.Contains("subdiario CB", AsientosService.MotivoSoloLectura(R("CB", tipoReg: "CB"), manuales));
        Assert.Contains("conciliados", AsientosService.MotivoSoloLectura(R("CC", conciliado: true), manuales));
        Assert.Contains("sueldos", AsientosService.MotivoSoloLectura(R("CC", obs: "ASIENTO_SISPER"), manuales));
        Assert.Contains("cierre", AsientosService.MotivoSoloLectura(R("CC", cierre: true), manuales));
        Assert.Contains("FP", AsientosService.MotivoSoloLectura(R("FP"), manuales));
        Assert.Contains("tarjetas", AsientosService.MotivoSoloLectura(R("LT"), manuales));
        Assert.Equal("", AsientosService.MotivoSoloLectura(R("CC", medio: "CH"), manuales)); // los cheques ya se cargan acá
        Assert.Equal("", AsientosService.MotivoSoloLectura(R("CC", medio: "TJ"), manuales)); // tarjetas: cuenta contable común
    }

    private static readonly IReadOnlyList<CompraCargaOpcion> CentrosCat = [new() { Codigo = "0000", Descripcion = "OTROS" }, new() { Codigo = "0217", Descripcion = "MOLINOS" }, new() { Codigo = "0665", Descripcion = "ENCOPRESS" }];

    [Fact]
    public void DistribucionRedondeaContraElRenglon()
    {
        var r = new AsientoRenglon { Debe = 100.01m, Centros = [new() { CCosto = "0000", Porcentaje = 33.33m }, new() { CCosto = "0217", Porcentaje = 33.33m }, new() { CCosto = "0665", Porcentaje = 33.34m }] };
        AsientosService.DistribuirImportes(r);
        Assert.Equal(100.01m, r.Centros.Sum(c => c.Importe));
        Assert.Equal([33.33m, 33.33m, 33.35m], r.Centros.Select(c => c.Importe));
    }

    [Fact]
    public void CentrosDeLaCuentaSeAplicanSolosSiNoPidenConfirmacion()
    {
        AsientoCentroDefault[] defaults = [new("42120", "0000", 100, false), new("42120", "0217", 0, false), new("42101", "0217", 0, true), new("42101", "0000", 100, true)];
        var r = new AsientoRenglon { Cuenta = "42120", Debe = 500 };
        AsientosService.ValidarCentros(r, 1, CentrosCat, defaults);
        var c = Assert.Single(r.Centros);
        Assert.Equal(("0000", 100m, 500m), (c.CCosto, c.Porcentaje, c.Importe));
        var conConfirmacion = new AsientoRenglon { Cuenta = "42101", Debe = 500 };
        AsientosService.ValidarCentros(conConfirmacion, 1, CentrosCat, defaults);
        Assert.Empty(conConfirmacion.Centros); // como el legacy: si la ventana se cancela, queda sin distribuir
        var manual = new AsientoRenglon { Cuenta = "42101", Haber = 1000, Centros = [new() { CCosto = "0217", Porcentaje = 60 }, new() { CCosto = "0665", Porcentaje = 40 }] };
        AsientosService.ValidarCentros(manual, 1, CentrosCat, defaults);
        Assert.Equal([600m, 400m], manual.Centros.Select(x => x.Importe));
    }

    [Theory]
    [InlineData("0217:60,0665:30")]
    [InlineData("0217:60,0217:40")]
    [InlineData("9999:100")]
    [InlineData("0217:0,0665:100")]
    public void CentrosMalCargados(string centros)
    {
        var r = new AsientoRenglon
        {
            Cuenta = "511", Debe = 10,
            Centros = centros.Split(',').Select(x => new AsientoCentro { CCosto = x.Split(':')[0], Porcentaje = decimal.Parse(x.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture) }).ToList()
        };
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarCentros(r, 1, CentrosCat, []));
    }

    [Fact]
    public void RenglonesDeModeloSinImporteNoSeGraban()
    {
        var a = A(("511", 1000, 0), ("211", 0, 1000));
        a.Renglones.Add(new AsientoRenglon { Cuenta = "214", DeModelo = true });
        AsientosService.ValidarRenglones(a, Cuentas, Terceros);
        Assert.Equal(2, a.Renglones.Count);
        var b = A(("511", 1000, 0), ("211", 0, 1000));
        b.Renglones.Add(new AsientoRenglon { Cuenta = "214" });
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarRenglones(b, Cuentas, Terceros));
    }

    [Fact]
    public void MonedaExtranjeraCalculaLosPesos()
    {
        var dolares = new AsientoCuenta("111USD", "Caja dólares", "EF", false, "2");
        var r = new AsientoRenglon { Cuenta = "111USD", Debe = 1, ImporteMoneda = 100, Cotizacion = 1548.5m };
        AsientosService.ValidarChequeYMoneda(r, dolares, 1, []);
        Assert.Equal((154850m, "2"), (r.Debe, r.Moneda));
        var h = new AsientoRenglon { Cuenta = "111USD", Haber = 1, ImporteMoneda = 10.5m, Cotizacion = 1000 };
        AsientosService.ValidarChequeYMoneda(h, dolares, 1, []);
        Assert.Equal(10500m, h.Haber);
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarChequeYMoneda(new AsientoRenglon { Debe = 5, Cotizacion = 0, ImporteMoneda = 5 }, dolares, 1, []));
        var pesos = new AsientoRenglon { Debe = 5, Moneda = "2", Cotizacion = 9, ImporteMoneda = 3 };
        AsientosService.ValidarChequeYMoneda(pesos, Cuentas["511"], 1, []);
        Assert.Equal(("1", 1m, 0m), (pesos.Moneda, pesos.Cotizacion, pesos.ImporteMoneda));
    }

    [Fact]
    public void DatosDelCheque()
    {
        var ch = new AsientoCuenta("111CH", "Cheques de terceros", "CH", false);
        AsientoRenglon R2(string serie, string nro, string banco, int clearing = 48, string cuit = "") => new()
        { Debe = 100, Cheque = new AsientoCheque { Serie = serie, Numero = nro, IdBanco = banco, Vencimiento = DateTime.Today.AddDays(30), Clearing = clearing, Cuit = cuit } };
        var ok = R2("a", "123456", "7", cuit: "30-71800814-6");
        AsientosService.ValidarChequeYMoneda(ok, ch, 1, ["7"]);
        Assert.Equal(("A", "A-123456", "30718008146"), (ok.Cheque!.Serie, ok.Cheque.NroComprobante, ok.Cheque.Cuit));
        Assert.Equal("-99", new AsientoCheque { Numero = "99" }.NroComprobante); // como el legacy cuando no hay serie
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarChequeYMoneda(new AsientoRenglon { Debe = 100 }, ch, 1, ["7"]));
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarChequeYMoneda(R2("AB", "1", "7"), ch, 1, ["7"]));
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarChequeYMoneda(R2("", "12a", "7"), ch, 1, ["7"]));
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarChequeYMoneda(R2("", "1", "9"), ch, 1, ["7"]));
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarChequeYMoneda(R2("", "1", "7", clearing: 30), ch, 1, ["7"]));
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarChequeYMoneda(R2("", "1", "7", cuit: "123"), ch, 1, ["7"]));
        var sinCheque = new AsientoRenglon { Debe = 1, Cheque = new AsientoCheque { Numero = "1" } };
        AsientosService.ValidarChequeYMoneda(sinCheque, Cuentas["511"], 1, []);
        Assert.Null(sinCheque.Cheque);
    }

    [Fact]
    public void AsientoEnOtraMonedaSePasaAPesosYBalancea()
    {
        // 3 renglones en dólares que en pesos, renglón por renglón, no balancean por redondeo.
        var a = A(("511", 10.01m, 0), ("511", 10.01m, 0), ("211", 0, 20.02m));
        a.MonedaAsiento = "2"; a.CotizacionAsiento = 1.333m;
        AsientosService.ValidarRenglones(a, Cuentas, Terceros);
        Assert.All(a.Renglones, r => Assert.Equal(("2", 1.333m), (r.Moneda, r.Cotizacion)));
        Assert.Equal([10.01m, 10.01m, 20.02m], a.Renglones.Select(r => r.ImporteMoneda));
        Assert.Equal(a.TotalDebe, a.TotalHaber);
        Assert.Equal(26.68m, a.TotalHaber); // 2 × 13,34 al debe: el haber (26,69) se ajusta un centavo
        // Leído de nuevo se muestra en dólares y balancea en dólares.
        AsientosService.DetectarMonedaAsiento(a);
        Assert.Equal(("2", 1.333m), (a.MonedaAsiento, a.CotizacionAsiento));
        Assert.Equal(a.TotalDebe, a.TotalHaber);
        Assert.Equal(20.02m, a.TotalHaber);

        var sinCot = A(("511", 10, 0), ("211", 0, 10)); sinCot.MonedaAsiento = "2"; sinCot.CotizacionAsiento = 0;
        Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarRenglones(sinCot, Cuentas, Terceros));
        var conCheque = A(("111CH", 10, 0), ("211", 0, 10)); conCheque.MonedaAsiento = "2"; conCheque.CotizacionAsiento = 1000;
        Assert.Contains("pesos", Assert.ThrowsAny<InvalidOperationException>(() => AsientosService.ValidarRenglones(conCheque, Cuentas, Terceros)).Message);
        // En pesos no cambia nada.
        var pesos = A(("511", 10, 0), ("211", 0, 10));
        AsientosService.ValidarRenglones(pesos, Cuentas, Terceros);
        AsientosService.DetectarMonedaAsiento(pesos);
        Assert.Equal(("1", 10m), (pesos.MonedaAsiento, pesos.TotalDebe));
    }

    [Fact]
    public void TarjetasSonCuentasComunes()
    {
        var cuentas = new Dictionary<string, AsientoCuenta>(Cuentas, StringComparer.OrdinalIgnoreCase) { ["113TJ"] = new("113TJ", "Tarjeta Visa", "TJ", false) };
        var a = A(("113TJ", 100, 0), ("511", 0, 100));
        AsientosService.ValidarRenglones(a, cuentas, Terceros);
        Assert.Equal(2, a.Renglones.Count);
    }

    [Theory]
    [InlineData("7", "Galicia", "7")]
    [InlineData("0014", "Provincia", "14")]
    [InlineData("brub", "Brubank", "BRUB")]
    public void BancoValido(string codigo, string nombre, string esperado)
    {
        var b = new BancoTabla { IdBanco = codigo, Descripcion = nombre };
        ChequerasService.ValidarBanco(b);
        Assert.Equal(esperado, b.IdBanco);
    }

    [Theory]
    [InlineData("", "Galicia")]
    [InlineData("12345", "Galicia")]
    [InlineData("1-2", "Galicia")]
    [InlineData("7", "")]
    public void BancoInvalido(string codigo, string nombre)
        => Assert.ThrowsAny<InvalidOperationException>(() => ChequerasService.ValidarBanco(new BancoTabla { IdBanco = codigo, Descripcion = nombre }));

    [Fact]
    public void ChequerasValidacionYSuperposicion()
    {
        Chequera C(int id, long d, long h, string serie = "M", int control = 1) => new() { Id = id, IdBanco = "25", Serie = serie, Talonario = control, Desde = d, Hasta = h, CuentaBanco = "111" };
        var existentes = new[] { C(1, 100, 150), C(2, 151, 200, control: 2), C(3, 100, 150, serie: "N") };
        Assert.Null(ChequerasService.Superpuesta(C(0, 201, 250, control: 3), existentes));
        Assert.Equal(1, ChequerasService.Superpuesta(C(0, 140, 160, control: 3), existentes)!.Id);
        Assert.Equal(1, ChequerasService.Superpuesta(C(0, 50, 300, control: 3), existentes)!.Id);
        Assert.Null(ChequerasService.Superpuesta(C(1, 100, 150), existentes.Take(1)));
        ChequerasService.Validar(C(0, 1, 50));
        Assert.ThrowsAny<InvalidOperationException>(() => ChequerasService.Validar(C(0, 50, 1)));
        Assert.ThrowsAny<InvalidOperationException>(() => ChequerasService.Validar(C(0, 0, 10)));
        Assert.ThrowsAny<InvalidOperationException>(() => ChequerasService.Validar(C(0, 1, 10, serie: "")));
        Assert.ThrowsAny<InvalidOperationException>(() => ChequerasService.Validar(new Chequera { IdBanco = "25", Serie = "M", Desde = 1, Hasta = 2 }));
    }

    [Fact]
    public void ClaveDelAsiento()
    {
        var k = new AsientoClave("20", 10, 25, "    ");
        Assert.Equal("10-0000025", k.Texto);
        Assert.Equal(k with { TipoReg = "" }, AsientoClave.Parse(k.Id));
        Assert.Null(AsientoClave.Parse("x|y"));
    }

    [Fact]
    public void ClaveDelAsientoIncluyeElComprobante()
    {
        // El N° de asiento 10-3407 se repite entre FC 0014-40144, FC 0015-38888 y FCC 2100-18895.
        var k = new AsientoClave("16", 10, 3407, "", "FC", "0014", "00040144", "B");
        Assert.True(k.ConComprobante);
        Assert.Equal(k, AsientoClave.Parse(k.Id));
        Assert.NotEqual(k, k with { Sucursal = "0015", NumeroCpte = "00038888" });
        Assert.False(AsientoClave.Parse("16|10|3407|")!.ConComprobante);
    }

    [Fact]
    public void CruceConMisComprobantes()
    {
        var arca = new ArcaMisComprobantesArchivo { Recibidos = true };
        arca.Comprobantes.Add(new ArcaMisComprobante { Clase = "FC", Letra = "A", PuntoVenta = 3, NumeroDesde = 269, NumeroHasta = 269, DocContraparte = "23957694039",
            Total = 257512.20m, TipoCambio = 1, Moneda = "$", Cae = "86400000000001", Fecha = new DateTime(2026, 10, 5) });
        var ok = AsientosService.ControlarComprobante(arca, "FCC", "A", "0003", "00000269", "23-95769403-9", 257512.20m, "86400000000001", new DateTime(2026, 10, 5), "x.xls", true);
        Assert.Equal("COINCIDE", ok.Estado);
        var dif = AsientosService.ControlarComprobante(arca, "FCC", "A", "0003", "00000269", "23957694039", 250000m, "", new DateTime(2026, 10, 5), "x.xls", true);
        Assert.Equal("DIFIERE", dif.Estado);
        Assert.Contains(dif.Observaciones, o => o.Contains("total"));
        Assert.Equal("NO_INFORMADO", AsientosService.ControlarComprobante(arca, "FCC", "A", "0003", "00000270", "23957694039", 1, "", DateTime.Today, "x.xls", true).Estado);
        Assert.Equal("NO_INFORMADO", AsientosService.ControlarComprobante(arca, "FCC", "A", "0003", "00000269", "20111111112", 1, "", DateTime.Today, "x.xls", true).Estado);
    }
}

/// <summary>Contra ALFANET2007_PRUEBAS: cheques de terceros (entrada y salida de cartera), cheque propio de chequera, moneda extranjera y chequeras.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class AsientosChequesPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task ChequesMonedaYChequeras()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (m, _) => m.Name switch { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Alberto'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (m, _) => m.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        var permisos = new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events);
        var svc = new AsientosService(runtime.Sessions, users, permisos, runtime.Events);
        var cat = await svc.CatalogoAsync();
        var terceros = (await cn.QueryAsync<string>("SELECT LTRIM(RTRIM(CODIGO)) FROM VT_CLIENTES UNION SELECT LTRIM(RTRIM(CODIGO)) FROM VT_PROVEEDORES")).ToHashSet();
        var libre = cat.Cuentas.First(c => c.MedioDePago.Length == 0 && !c.PideVencimiento && !c.EnMonedaExtranjera && !terceros.Contains(c.Codigo)
            && !cat.CentrosPorCuenta.Any(d => d.Cuenta == c.Codigo)).Codigo;
        var cartera = cat.Cuentas.First(c => c.EsCheque && !c.Bancaria && c.Codigo == "111010002").Codigo;
        var dolares = cat.Cuentas.FirstOrDefault(c => c.EnMonedaExtranjera && c.MedioDePago is "" or "EF");
        var banco = cat.Bancos.First(b => b.IdBanco == "7").IdBanco;
        var fecha = new DateTime(2026, 10, 31);
        var nro = (DateTime.Now.Ticks % 9_000_000 + 1_000_000).ToString();
        var cotiz = await svc.CotizacionesAsync(fecha);
        output.WriteLine($"Cuenta libre {libre} · cartera {cartera} · dólares {dolares?.Codigo} cotización {cotiz.GetValueOrDefault("2")} · cheque {nro}");
        var creados = new List<AsientoDetalle>();
        try
        {
            // 1. Entra un cheque de terceros.
            var entrada = await svc.GuardarAsync(new AsientoDetalle
            {
                Fecha = fecha, Tc = "CC", Detalle = "PRUEBA ALFACORE entra cheque",
                Renglones = [new() { Cuenta = cartera, Debe = 1000, Cheque = new() { Numero = nro, IdBanco = banco, Vencimiento = fecha.AddDays(30), Cuit = "30718008146" } },
                             new() { Cuenta = libre, Haber = 1000 }]
            });
            creados.Add(entrada);
            Assert.Equal($"-{nro}", entrada.Renglones[0].Cheque!.NroComprobante);
            Assert.Contains(await svc.ChequesEnCarteraAsync(cartera), x => x.NroComprobante == $"-{nro}" && x.Importe == 1000);
            // El mismo cheque no puede volver a entrar.
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.GuardarAsync(new AsientoDetalle
            {
                Fecha = fecha, Tc = "CC", Detalle = "PRUEBA ALFACORE duplicado",
                Renglones = [new() { Cuenta = cartera, Debe = 1000, Cheque = new() { Numero = nro, IdBanco = banco, Vencimiento = fecha } }, new() { Cuenta = libre, Haber = 1000 }]
            }));
            // 2. Sale de la cartera: con otro importe no, con el mismo sí.
            AsientoDetalle Salida(decimal importe) => new()
            {
                Fecha = fecha, Tc = "CC", Detalle = "PRUEBA ALFACORE sale cheque",
                Renglones = [new() { Cuenta = libre, Debe = importe }, new() { Cuenta = cartera, Haber = importe, Cheque = new() { Numero = nro, IdBanco = banco, Vencimiento = fecha.AddDays(30) } }]
            };
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.GuardarAsync(Salida(999)));
            var salida = await svc.GuardarAsync(Salida(1000));
            creados.Add(salida);
            Assert.DoesNotContain(await svc.ChequesEnCarteraAsync(cartera), x => x.NroComprobante == $"-{nro}");
            // Ya no está en cartera: no puede salir otra vez, y el asiento de entrada no se puede anular.
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.GuardarAsync(Salida(1000)));
            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.EliminarAsync(entrada.Clave!, entrada.Huella));
            output.WriteLine("Anular la entrada: " + ex.Message);
            // Modificar la salida (otro concepto) sigue andando: el cheque que entrega es el suyo.
            salida.Detalle = "PRUEBA ALFACORE sale cheque (modificado)";
            salida = await svc.GuardarAsync(salida);
            creados[1] = salida;

            // 3. Cheque propio de una chequera (banco 25 serie M) que no se haya emitido.
            var propia = cat.Cuentas.FirstOrDefault(c => c.Codigo == "211030303");
            if (propia is not null)
            {
                var usados = (await cn.QueryAsync<string>("SELECT NroComprobanteBancario FROM MV_ASIENTOS WHERE CUENTA='211030303' AND [DEBE-HABER]='H'")).ToHashSet();
                var libreNro = Enumerable.Range(49200076, 50).Select(x => x.ToString()).First(x => !usados.Contains("M-" + x));
                AsientoDetalle Propio(string n) => new()
                {
                    Fecha = fecha, Tc = "CC", Detalle = "PRUEBA ALFACORE cheque propio",
                    Renglones = [new() { Cuenta = libre, Debe = 500 }, new() { Cuenta = "211030303", Haber = 500, Cheque = new() { Serie = "M", Numero = n, IdBanco = "25", Vencimiento = fecha.AddDays(60) } }]
                };
                await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.GuardarAsync(Propio("1")));        // fuera de chequera
                var p = await svc.GuardarAsync(Propio(libreNro));
                creados.Add(p);
                await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.GuardarAsync(Propio(libreNro))); // ya emitido
                output.WriteLine($"Cheque propio M-{libreNro} emitido");
            }

            // 4. Moneda extranjera.
            if (dolares is not null && cotiz.TryGetValue(dolares.Moneda, out var c2) && c2 > 1)
            {
                var usd = await svc.GuardarAsync(new AsientoDetalle
                {
                    Fecha = fecha, Tc = "CC", Detalle = "PRUEBA ALFACORE dólares",
                    Renglones = [new() { Cuenta = dolares.Codigo, Debe = 1, ImporteMoneda = 100, Cotizacion = c2 }, new() { Cuenta = libre, Haber = Math.Round(100 * c2, 2) }]
                });
                creados.Add(usd);
                var r = usd.Renglones[0];
                Assert.Equal((Math.Round(100 * c2, 2), dolares.Moneda, c2, 100m), (r.Debe, r.Moneda, r.Cotizacion, r.ImporteMoneda));
                Assert.Equal(c2, await cn.ExecuteScalarAsync<decimal>("SELECT COTIZACION FROM MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N AND CUENTA=@C",
                    new { P = usd.Clave!.Periodo, M = usd.Clave.Mes, N = usd.Clave.Numero, C = dolares.Codigo }));
                output.WriteLine($"USD: 100 × {c2} = {r.Debe:N2}");
            }

            // 4b. Asiento entero en dólares (moneda del encabezado): se graba en pesos y se lee en dólares.
            if (cotiz.TryGetValue("2", out var c3) && c3 > 1)
            {
                var otra = cat.Cuentas.First(c => c.Codigo != libre && c.MedioDePago.Length == 0 && !c.PideVencimiento && !c.EnMonedaExtranjera && !terceros.Contains(c.Codigo)
                    && cat.CentrosPorCuenta.All(d => d.Cuenta != c.Codigo)).Codigo;
                var editor = new AsientoDetalle
                {
                    Fecha = fecha, Tc = "CC", Detalle = "PRUEBA ALFACORE asiento en dólares", MonedaAsiento = "2", CotizacionAsiento = c3,
                    Renglones = [new() { Cuenta = otra, Debe = 250.50m }, new() { Cuenta = libre, Haber = 250.50m }]
                };
                var cab = await svc.GuardarAsync(editor);
                creados.Add(cab);
                Assert.Equal(250.50m, editor.Renglones[0].Debe); // el editor no se toca
                Assert.Equal(("2", c3, 250.50m), (cab.MonedaAsiento, cab.CotizacionAsiento, cab.TotalDebe));
                var pesos = await cn.ExecuteScalarAsync<decimal>("SELECT SUM(IMPORTE) FROM MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N AND [DEBE-HABER]='D' AND LTRIM(RTRIM(ISNULL(TIPO_REG,'')))=''",
                    new { P = cab.Clave!.Periodo, M = cab.Clave.Mes, N = cab.Clave.Numero });
                Assert.Equal(Math.Round(250.50m * c3, 2, MidpointRounding.AwayFromZero), pesos);
                output.WriteLine($"Asiento en U$D: 250,50 × {c3} = {pesos:N2}");
            }

            // 5. Anular en orden: primero la salida, después la entrada.
            for (var i = creados.Count - 1; i >= 0; i--) { await svc.EliminarAsync(creados[i].Clave!, creados[i].Huella); }
            creados.Clear();

            // 6. Chequeras: superposición y alta/baja de una de prueba.
            var chequeras = new ChequerasService(runtime.Sessions, users, permisos, runtime.Events);
            var lista = await chequeras.ListarAsync();
            output.WriteLine($"Chequeras: {string.Join(" | ", lista.Select(x => $"{x.Banco} {x.Serie} {x.Desde}-{x.Hasta} emitidos {x.Emitidos}"))}");
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => chequeras.GuardarAsync(new Chequera { IdBanco = "25", Serie = "M", Talonario = 9, Desde = 49200100, Hasta = 49200200, CuentaBanco = "111020303" }));
            var nueva = await chequeras.GuardarAsync(new Chequera { IdBanco = "25", Serie = "Z", Talonario = 9, Desde = 1, Hasta = 50, CuentaBanco = "111020303" });
            Assert.Equal(0, nueva.Emitidos);
            await chequeras.EliminarAsync(nueva.Id);

            // 7. Bancos: alta, cambio de nombre, repetido, en uso y baja.
            var bancos = await chequeras.BancosAsync();
            Assert.True(bancos.First(b => b.IdBanco == "25").EnUso);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => chequeras.EliminarBancoAsync("25"));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => chequeras.GuardarBancoAsync(new BancoTabla { IdBanco = "025", Descripcion = "Otro" }, nuevo: true));
            var nb = await chequeras.GuardarBancoAsync(new BancoTabla { IdBanco = "ZZT", Descripcion = "PRUEBA ALFACORE banco" }, nuevo: true);
            Assert.False(nb.EnUso);
            nb = await chequeras.GuardarBancoAsync(new BancoTabla { IdBanco = "ZZT", Descripcion = "PRUEBA ALFACORE banco 2" }, nuevo: false);
            Assert.Equal("PRUEBA ALFACORE banco 2", nb.Descripcion);
            await chequeras.EliminarBancoAsync("ZZT");
            Assert.DoesNotContain(await chequeras.BancosAsync(), b => b.IdBanco == "ZZT");
            output.WriteLine($"Bancos: {bancos.Count}, en uso {bancos.Count(b => b.EnUso)}");
        }
        finally
        {
            foreach (var a in creados)
                await cn.ExecuteAsync("DELETE FROM MV_ASIENTOSCCOSTO WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N; DELETE FROM MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N AND LTRIM(RTRIM(ISNULL(TIPO_REG,'')))=''",
                    new { P = a.Clave!.Periodo, M = a.Clave.Mes, N = a.Clave.Numero });
            await cn.ExecuteAsync("DELETE FROM V_TA_CTRL_CHEQUERAS WHERE IDBANCO='  25' AND SERIE='Z'; DELETE FROM V_TA_Bancos WHERE LTRIM(RTRIM(IdBanco))='ZZT'");
        }
    }
}

/// <summary>Contra ALFANET2007_PRUEBAS: alta, modificación y anulación de un asiento manual. Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class AsientosPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task AltaModificacionYAnulacion()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (m, _) => m.Name switch { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Alberto'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (m, _) => m.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        var svc = new AsientosService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events), runtime.Events);

        var cat = await svc.CatalogoAsync();
        output.WriteLine($"{cat.Cuentas.Count} cuentas · talonarios {string.Join(", ", cat.Talonarios.Select(t => t.Codigo))} · cargar {cat.PuedeCargar} modificar {cat.PuedeModificar} anular {cat.PuedeAnular}");
        Assert.True(cat.PuedeCargar && cat.PuedeModificar && cat.PuedeAnular);
        // Dos cuentas sin medio de pago, que no sean de clientes ni proveedores ni pidan vencimiento.
        var terceros = (await cn.QueryAsync<string>("SELECT LTRIM(RTRIM(CODIGO)) FROM VT_CLIENTES UNION SELECT LTRIM(RTRIM(CODIGO)) FROM VT_PROVEEDORES")).ToHashSet();
        var libres = cat.Cuentas.Where(c => c.MedioDePago.Length == 0 && !c.PideVencimiento && !c.EnMonedaExtranjera && !terceros.Contains(c.Codigo)).Take(3).ToList();
        Assert.True(libres.Count == 3);
        var cc = cat.Talonarios.First(t => t.Codigo == "CC");
        var proximoAntes = await cn.ExecuteScalarAsync<int>("SELECT TOP 1 X_ULTIMO_NRO FROM V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CC' AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@U", new { U = cc.Unidad });
        var fecha = new DateTime(2026, 10, 31);

        // Alta: una cuenta con distribución automática (si existe en la base) y otra con centros cargados a mano.
        var conDistribucion = cat.CentrosPorCuenta.GroupBy(d => d.Cuenta).FirstOrDefault(g => g.Sum(d => d.Porcentaje) == 100 && !g.Any(d => d.PideConfirmacion)
            && cat.Cuentas.Any(c => c.Codigo == g.Key && c.MedioDePago.Length == 0 && !c.PideVencimiento) && !terceros.Contains(g.Key))?.Key;
        output.WriteLine($"Centros: {cat.Centros.Count} · cuenta con distribución automática: {conDistribucion ?? "(ninguna)"} · modelos: {string.Join(", ", cat.Modelos)}");
        var nuevo = new AsientoDetalle
        {
            Fecha = fecha, Tc = "CC", Detalle = "PRUEBA ALFACORE asiento manual",
            Renglones =
            [
                new() { Cuenta = conDistribucion ?? libres[0].Codigo, Debe = 1234.56m },
                new() { Cuenta = libres[1].Codigo, Haber = 1234.56m, Detalle = "renglón con concepto",
                        Centros = cat.Centros.Count >= 2 ? [new() { CCosto = cat.Centros[0].Codigo, Porcentaje = 70 }, new() { CCosto = cat.Centros[1].Codigo, Porcentaje = 30 }] : [] },
                new()
            ]
        };
        var a = await svc.GuardarAsync(nuevo);
        AsientoClave k = a.Clave!;
        try
        {
            output.WriteLine($"Alta: asiento {k.Texto} período {k.Periodo} · {a.Comprobante} · editable {a.Editable}");
            Assert.True(a.Editable);
            Assert.Equal(2, a.Renglones.Count);
            Assert.Equal("renglón con concepto", a.Renglones[1].Detalle);
            Assert.Equal("PRUEBA ALFACORE asiento manual", a.Detalle);
            if (cat.Centros.Count >= 2)
            {
                Assert.Equal([864.19m, 370.37m], a.Renglones[1].Centros.Select(c => c.Importe));
                if (conDistribucion is not null) Assert.Equal(1234.56m, Assert.Single(a.Renglones[0].Centros).Importe);
                Assert.Equal(conDistribucion is null ? 2 : 3, await cn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM MV_ASIENTOSCCOSTO WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N", new { P = k.Periodo, M = k.Mes, N = k.Numero }));
            }
            Assert.Equal(proximoAntes + 1 > int.Parse(a.Numero) ? proximoAntes + 1 : int.Parse(a.Numero) + 1,
                await cn.ExecuteScalarAsync<int>("SELECT TOP 1 X_ULTIMO_NRO FROM V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CC' AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@U", new { U = cc.Unidad }));
            var lista = await svc.BuscarAsync(new AsientosFiltro { Desde = fecha, Hasta = fecha, Texto = k.Texto, Origen = "MANUALES" });
            Assert.Contains(lista, x => x.Clave == k && x.Manual && x.Total == 1234.56m);
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTEACCIONES WHERE TC='CC' AND IDCOMPROBANTE=@Id AND TIPO_ACCION='A' AND NUMEROASIENTO=@N",
                new { Id = a.Sucursal + a.Numero + a.Letra, N = k.Numero }));

            // Modificación: tres renglones, mismo número.
            a.Renglones = [new() { Cuenta = libres[0].Codigo, Debe = 1000m }, new() { Cuenta = libres[2].Codigo, Debe = 500m }, new() { Cuenta = libres[1].Codigo, Haber = 1500m }];
            a.Detalle = "PRUEBA ALFACORE asiento modificado";
            var m = await svc.GuardarAsync(a);
            Assert.Equal(k, m.Clave);
            Assert.Equal(3, m.Renglones.Count);
            Assert.Equal(1500m, m.TotalDebe);
            Assert.NotNull(m.Modificado);
            // Con la huella vieja no se puede volver a grabar (otro lo cambió).
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.GuardarAsync(a));
            // Pasar a otro mes no se permite (es otro número de asiento).
            m.Fecha = new DateTime(2026, 9, 30);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.GuardarAsync(m));
            m.Fecha = fecha;

            // Un asiento de compras se ve pero no se modifica.
            var compra = await cn.QuerySingleOrDefaultAsync<(string P, int M, int N, string T)?>("""
                SELECT TOP 1 LTRIM(RTRIM(PERIODO)), CONVERT(int, MES_OPERATIVO), CONVERT(int, [NUMERO ASIENTO]), LTRIM(RTRIM(ISNULL(TIPO_REG,'')))
                FROM MV_ASIENTOS WHERE LIVA_TIPO='Compras' AND FECHA >= '20260801' ORDER BY FECHA DESC
                """);
            if (compra is { } c)
            {
                var d = await svc.GetAsync(new AsientoClave(c.P, c.M, c.N, c.T));
                output.WriteLine($"Compra {d.Comprobante}: {d.MotivoSoloLectura}");
                Assert.False(d.Editable);
                await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.EliminarAsync(d.Clave!, d.Huella));
                var arca = await svc.ControlArcaAsync(d.Clave!);
                output.WriteLine($"  ARCA: {arca.Estado} · {arca.Mensaje}");
            }

            // Modelo: guardar, leer y borrar.
            await svc.GuardarModeloAsync(new AsientoModelo { Nombre = "PRUEBA ALFACORE", Tc = "CC", ModificaCuentas = false, AgregaCuentas = false,
                Renglones = [new() { Cuenta = libres[0].Codigo, Debe = 10 }, new() { Cuenta = libres[1].Codigo, AlHaber = true }] });
            var modelo = await svc.ModeloAsync("PRUEBA ALFACORE");
            Assert.Equal(2, modelo.Renglones.Count);
            Assert.True(modelo.Renglones[1].AlHaber && modelo.Renglones.All(x => x.DeModelo && x.CuentaFija));
            Assert.False(modelo.AgregaCuentas);
            await svc.EliminarModeloAsync("PRUEBA ALFACORE");
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MA_MODELOS WHERE NOMBRE='PRUEBA ALFACORE'"));

            // Anulación.
            await svc.EliminarAsync(k, m.Huella);
            k = null!;
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N AND LTRIM(RTRIM(ISNULL(TIPO_REG,'')))=''",
                new { P = m.Clave!.Periodo, M = m.Clave.Mes, N = m.Clave.Numero }));
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTEACCIONES WHERE TC='CC' AND IDCOMPROBANTE=@Id AND TIPO_ACCION='B' AND Proceso=N'AlfaCore: asientos'",
                new { Id = m.Sucursal + m.Numero + m.Letra }));
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOSCCOSTO WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N",
                new { P = m.Clave.Periodo, M = m.Clave.Mes, N = m.Clave.Numero }));
            output.WriteLine("Anulado.");
        }
        finally
        {
            await cn.ExecuteAsync("DELETE FROM MA_MODELOS WHERE NOMBRE='PRUEBA ALFACORE'");
            if (k is not null)
                await cn.ExecuteAsync("DELETE FROM MV_ASIENTOSCCOSTO WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N; DELETE FROM MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N AND LTRIM(RTRIM(ISNULL(TIPO_REG,'')))=''",
                    new { P = k.Periodo, M = k.Mes, N = k.Numero });
        }
    }
}

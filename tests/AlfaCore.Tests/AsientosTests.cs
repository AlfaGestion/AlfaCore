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
        Assert.Contains("cheques", AsientosService.MotivoSoloLectura(R("CC", medio: "CH"), manuales));
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
        var libres = cat.Cuentas.Where(c => c.MedioDePago.Length == 0 && !c.PideVencimiento && !terceros.Contains(c.Codigo)).Take(3).ToList();
        Assert.True(libres.Count == 3);
        var cc = cat.Talonarios.First(t => t.Codigo == "CC");
        var proximoAntes = await cn.ExecuteScalarAsync<int>("SELECT TOP 1 X_ULTIMO_NRO FROM V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CC' AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@U", new { U = cc.Unidad });
        var fecha = new DateTime(2026, 10, 31);

        // Alta.
        var nuevo = new AsientoDetalle
        {
            Fecha = fecha, Tc = "CC", Detalle = "PRUEBA ALFACORE asiento manual",
            Renglones = [new() { Cuenta = libres[0].Codigo, Debe = 1234.56m }, new() { Cuenta = libres[1].Codigo, Haber = 1234.56m, Detalle = "renglón con concepto" }, new()]
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

            // Anulación.
            await svc.EliminarAsync(k, m.Huella);
            k = null!;
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N AND LTRIM(RTRIM(ISNULL(TIPO_REG,'')))=''",
                new { P = m.Clave!.Periodo, M = m.Clave.Mes, N = m.Clave.Numero }));
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_MV_CPTEACCIONES WHERE TC='CC' AND IDCOMPROBANTE=@Id AND TIPO_ACCION='B' AND Proceso=N'AlfaCore: asientos'",
                new { Id = m.Sucursal + m.Numero + m.Letra }));
            output.WriteLine("Anulado.");
        }
        finally
        {
            if (k is not null)
                await cn.ExecuteAsync("DELETE FROM MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@P AND MES_OPERATIVO=@M AND [NUMERO ASIENTO]=@N AND LTRIM(RTRIM(ISNULL(TIPO_REG,'')))=''",
                    new { P = k.Periodo, M = k.Mes, N = k.Numero });
        }
    }
}

using System.Reflection;
using System.Text;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Control contra «Mis Comprobantes» de ARCA: lectura del archivo y comparación.</summary>
public sealed class ControlArcaTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("1 - Factura A", 1, "FC", "A")]
    [InlineData("11 - Factura C", 11, "FC", "C")]
    [InlineData("8 - Nota de Crédito B", 8, "NC", "B")]
    [InlineData("12 - Nota de Débito C", 12, "ND", "C")]
    [InlineData("201 - Factura de Crédito Electrónica MiPyMEs (FCE) A", 201, "FC", "A")]
    [InlineData("15 - Recibo C", 15, "RC", "C")]
    [InlineData("1.0", 1, "FC", "A")]
    [InlineData("6", 6, "FC", "B")]
    [InlineData("8.0", 8, "NC", "B")]
    [InlineData("203", 203, "NC", "A")]
    [InlineData("82", 82, "FC", "B")]
    public void TipoDeComprobanteDeArca(string texto, int codigo, string clase, string letra)
    {
        var t = ControlArcaService.Tipo(texto);
        Assert.Equal((codigo, letra), (t.Codigo, t.Letra));
        Assert.Equal(clase, t.Clase);
    }

    [Theory]
    [InlineData("181642.92", 181642.92)] [InlineData("181.642,92", 181642.92)] [InlineData("181642,92", 181642.92)] [InlineData("", 0)]
    public void NumerosDelExcelYDelCsv(string texto, double esperado) => Assert.Equal((decimal)esperado, ControlArcaService.Num(texto));

    private const string Csv = """
        Mis Comprobantes Emitidos - CUIT 27228073259
        Fecha;Tipo;Punto de Venta;Número Desde;Número Hasta;Cód. Autorización;Tipo Doc. Receptor;Nro. Doc. Receptor;Denominación Receptor;Tipo Cambio;Moneda;Neto Gravado Total;Neto No Gravado;Op. Exentas;Otros Tributos;Total IVA;Imp. Total
        02/10/2026;11 - Factura C;10;100;100;111;CUIT;30111111111;CLIENTE UNO;1;$;0;0;0;0;0;1000
        02/10/2026;11 - Factura C;10;101;101;112;CUIT;30222222222;CLIENTE DOS;1;$;0;0;0;0;0;2000
        05/10/2026;11 - Factura C;10;102;102;113;CUIT;30333333333;CLIENTE TRES;1;$;0;0;0;0;0;3000
        30/10/2026;11 - Factura C;10;103;103;114;CUIT;30444444444;CLIENTE CUATRO;1;$;0;0;0;0;0;4000
        06/10/2026;6 - Factura B;2;50;52;115;DNI;0;CONSUMIDOR;1;$;0;0;0;0;0;300
        """;

    [Fact]
    public void ComparaYClasificaCadaComprobante()
    {
        var arca = ControlArcaService.Leer(Encoding.UTF8.GetBytes(Csv), "emitidos.csv");
        Assert.False(arca.Recibidos);
        Assert.Equal("27228073259", arca.Cuit);
        Assert.Equal(5, arca.Comprobantes.Count);
        var sistema = new List<ControlArcaService.ComprobanteSistema>
        {
            new("FC", "C", 10, 100, new(2026, 10, 2), 1000, "30111111111", "CLIENTE UNO", "FC 0010-00000100 C"),      // coincide
            new("FC", "C", 10, 101, new(2026, 10, 2), 2500, "30222222222", "CLIENTE DOS", "FC 0010-00000101 C"),      // difiere importe
            new("FC", "C", 10, 103, new(2026, 11, 1), 4000, "30444444444", "CLIENTE CUATRO", "FC 0010-00000103 C"),   // otro período
            new("FC", "B", 2, 50, new(2026, 10, 6), 100, "", "CF", "FC 0002-00000050 B"),                              // rango B 50–52
            new("FC", "B", 2, 51, new(2026, 10, 6), 100, "", "CF", "FC 0002-00000051 B"),
            new("FC", "B", 2, 52, new(2026, 10, 6), 100, "", "CF", "FC 0002-00000052 B"),
            new("FC", "C", 10, 104, new(2026, 10, 20), 500, "30555555555", "CLIENTE CINCO", "FC 0010-00000104 C")     // no está en ARCA
        };
        var r = ControlArcaService.Comparar(arca, sistema, new(2026, 10, 1), new(2026, 10, 31));
        foreach (var f in r.Filas) output.WriteLine($"{f.Estado} {f.Comprobante} {f.TotalArca} / {f.TotalSistema} {f.Observacion}");
        Assert.Equal(ControlArcaEstado.Coincide, r.Filas.Single(f => f.Numero == 100).Estado);
        Assert.Equal(ControlArcaEstado.DifiereImporte, r.Filas.Single(f => f.Numero == 101).Estado);
        Assert.Equal(ControlArcaEstado.FaltaEnSistema, r.Filas.Single(f => f.Numero == 102).Estado);
        Assert.Equal(ControlArcaEstado.OtroPeriodo, r.Filas.Single(f => f.Numero == 103 && f.FechaArca is not null).Estado);
        Assert.Equal(ControlArcaEstado.Coincide, r.Filas.Single(f => f.Numero == 50).Estado);
        Assert.Equal(ControlArcaEstado.FaltaEnArca, r.Filas.Single(f => f.Numero == 104).Estado);
        Assert.DoesNotContain(r.Filas, f => f.Numero == 103 && f.Estado == ControlArcaEstado.FaltaEnArca);
    }

    /// <summary>Con un archivo real de ARCA (ALFACORE_ARCA_XLSX=ruta): lo lee completo.</summary>
    [Fact]
    public void LeeUnArchivoRealDeArca()
    {
        if (Environment.GetEnvironmentVariable("ALFACORE_ARCA_XLSX") is not { Length: > 0 } ruta || !File.Exists(ruta)) return;
        var arca = ControlArcaService.Leer(File.ReadAllBytes(ruta), ruta);
        output.WriteLine($"{(arca.Recibidos ? "Recibidos" : "Emitidos")} · CUIT {arca.Cuit} · {arca.Comprobantes.Count} comprobantes · total {arca.Comprobantes.Sum(c => c.TotalPesos):N2}");
        foreach (var g in arca.Comprobantes.GroupBy(c => $"{c.Clase} {c.Letra}")) output.WriteLine($"  {g.Key}: {g.Count()}");
        Assert.NotEmpty(arca.Comprobantes);
        Assert.All(arca.Comprobantes, c => Assert.True(c.PuntoVenta > 0 && c.NumeroDesde > 0 && c.Clase.Length > 0));
    }
}

/// <summary>Contra ALFANET2007_PRUEBAS (solo lectura): un «emitidos» armado con las ventas de octubre, sin una factura
/// y con otra alterada. Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class ControlArcaPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task DetectaLaFacturaQueFaltaYLaQueDifiere()
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
        var svc = new ControlArcaService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events), runtime.Events);

        var cuit = await cn.ExecuteScalarAsync<string>("SELECT TOP 1 LTRIM(RTRIM(CUIT)) FROM dbo.V_TA_UnidadNegocio WHERE ISNULL(CUIT,'')<>''");
        var ventas = (await cn.QueryAsync<(string Tc, string Suc, string Nro, string Letra, DateTime Fecha, decimal Importe, string Doc, string Nombre)>("""
            SELECT LTRIM(RTRIM(c.TC)), c.SUCURSAL, c.NUMERO, LTRIM(RTRIM(c.LETRA)), c.FECHA, c.IMPORTE, ISNULL(c.DOCUMENTONUMERO,''), ISNULL(c.NOMBRE,'')
            FROM dbo.V_MV_CPTE c JOIN dbo.V_TA_UnidadNegocio u ON LTRIM(RTRIM(u.Codigo)) = LTRIM(RTRIM(c.UNEGOCIO)) AND LTRIM(RTRIM(u.CUIT)) = @Cuit
            WHERE LTRIM(RTRIM(c.TC)) IN ('FC','NC') AND ISNULL(c.ANULADA,0)=0 AND ISNULL(c.IDCOMPLEMENTO,0)=0 AND c.LETRA IN ('A','B','C')
              AND c.FECHA >= '20261001' AND c.FECHA < '20261101' ORDER BY c.FECHA, c.NUMERO;
            """, new { Cuit = cuit })).ToList();
        output.WriteLine($"Ventas de octubre del CUIT {cuit}: {ventas.Count}");
        Assert.True(ventas.Count >= 3);
        var sb = new StringBuilder($"Mis Comprobantes Emitidos - CUIT {cuit}\nFecha;Tipo;Punto de Venta;Número Desde;Número Hasta;Cód. Autorización;Tipo Doc. Receptor;Nro. Doc. Receptor;Denominación Receptor;Tipo Cambio;Moneda;Neto Gravado Total;Neto No Gravado;Op. Exentas;Otros Tributos;Total IVA;Imp. Total\n");
        string Tipo(string tc, string l) => (tc, l) switch { ("FC", "A") => "1 - Factura A", ("FC", "B") => "6 - Factura B", ("FC", "C") => "11 - Factura C",
            ("NC", "A") => "3 - Nota de Crédito A", ("NC", "B") => "8 - Nota de Crédito B", _ => "13 - Nota de Crédito C" };
        for (var i = 1; i < ventas.Count; i++) // la primera falta en ARCA
        {
            var v = ventas[i];
            var total = i == 1 ? v.Importe + 100 : v.Importe; // la segunda difiere
            sb.Append($"{v.Fecha:dd/MM/yyyy};{Tipo(v.Tc, v.Letra)};{int.Parse(v.Suc)};{long.Parse(v.Nro)};{long.Parse(v.Nro)};0;CUIT;{new string(v.Doc.Where(char.IsDigit).ToArray())};{v.Nombre};1;$;0;0;0;0;0;{total.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
        }
        // Una que ARCA informa y el sistema no tiene.
        sb.Append($"15/10/2026;11 - Factura C;9999;12345;12345;0;CUIT;30999999999;NO CARGADA;1;$;0;0;0;0;0;777\n");
        var r = await svc.ControlarAsync(Encoding.UTF8.GetBytes(sb.ToString()), "emitidos.csv");
        output.WriteLine($"{r.ComprobantesArca} ARCA / {r.ComprobantesSistema} sistema · coinciden {r.Cantidad(ControlArcaEstado.Coincide)} · difieren {r.Cantidad(ControlArcaEstado.DifiereImporte)} · faltan {r.Cantidad(ControlArcaEstado.FaltaEnSistema)} · no en ARCA {r.Cantidad(ControlArcaEstado.FaltaEnArca)} · unidades {string.Join(",", r.Unidades)} · aviso «{r.Aviso}»");
        Assert.Equal(ventas.Count - 2, r.Cantidad(ControlArcaEstado.Coincide));
        Assert.Equal(1, r.Cantidad(ControlArcaEstado.DifiereImporte));
        Assert.Equal(1, r.Cantidad(ControlArcaEstado.FaltaEnSistema));
        Assert.Contains(r.Filas, f => f.Estado == ControlArcaEstado.FaltaEnArca && f.Numero == long.Parse(ventas[0].Nro));
        Assert.True((await svc.ExportarAsync(r)).Contenido.Length > 1000);
    }
}

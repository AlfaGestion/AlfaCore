using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Libro de IVA Ventas: reparto por alícuota, códigos ARCA y formato del TXT del Libro IVA Digital.</summary>
public sealed class LibroIvaVentasTests
{
    [Fact]
    public void UnaAlicuotaTomaTodoElNetoGravado()
    {
        var a = Assert.Single(LibroIvaVentasService.RepartirAlicuotas(1000m, "A", [(21m, 210m), (0, 0), (0, 0), (0, 0)]));
        Assert.Equal(new LibroIvaAlicuota(21m, 1000m, 210m), a);
    }

    [Fact]
    public void VariasAlicuotasRepartenElNetoYSumanElTotal()
    {
        var r = LibroIvaVentasService.RepartirAlicuotas(1500m, "A", [(21m, 210m), (10.5m, 52.5m), (0, 0), (0, 0)]);
        Assert.Equal(2, r.Count);
        Assert.Equal(1000m, r[0].Neto);
        Assert.Equal(1500m, r.Sum(x => x.Neto));
    }

    [Fact]
    public void LetraCNoDiscriminaIvaYSinIvaVaAlCeroPorCiento()
    {
        Assert.Empty(LibroIvaVentasService.RepartirAlicuotas(1000m, "C", [(21m, 0), (0, 0), (0, 0), (0, 0)]));
        Assert.Equal(0m, Assert.Single(LibroIvaVentasService.RepartirAlicuotas(500m, "B", [(0, 0), (0, 0), (0, 0), (0, 0)])).Alicuota);
    }

    [Theory]
    [InlineData("FC", "A", 1)] [InlineData("NC", "A", 3)] [InlineData("FC", "B", 6)] [InlineData("NC", "C", 13)] [InlineData("FP", "X", 0)]
    public void CodigosDeComprobanteArca(string tc, string letra, int codigo) => Assert.Equal(codigo, LibroIvaVentasService.CodigoArca(tc, letra));

    [Fact]
    public void TxtDelLibroIvaDigitalTieneElLargoDeArca()
    {
        var filas = new[]
        {
            new LibroIvaVentasFila { Fecha = new(2026, 10, 2), Tc = "FC", Letra = "A", PuntoVenta = 2, Numero = 3850, CodigoArca = 1, RazonSocial = "Comarca Ándina S.A.",
                Cuit = "30-70939035-6", NetoGravado = 1000m, Total = 1210m, Alicuotas = [new(21m, 1000m, 210m)] },
            new LibroIvaVentasFila { Fecha = new(2026, 10, 22), Tc = "NC", Letra = "C", PuntoVenta = 10, Numero = 32, CodigoArca = 13, RazonSocial = "Consumidor",
                Cuit = "", NetoGravado = -500m, Total = -500m, Alicuotas = [] },
            new LibroIvaVentasFila { Fecha = new(2026, 10, 3), Tc = "FP", Letra = "X", CodigoArca = 0, Total = 99m }
        };
        var (cbte, alic) = LibroIvaVentasService.ArmarTxt(filas);
        var lineas = cbte.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lineas.Length);
        Assert.All(lineas, l => Assert.Equal(266, l.Length));
        // fecha + tipo + punto de venta + número desde/hasta + tipo doc (80 = CUIT) + número de documento
        Assert.StartsWith("20261002" + "001" + "00002" + "00000000000000003850" + "00000000000000003850" + "80" + "00000000030709390356", lineas[0]);
        Assert.Contains("COMARCA ANDINA S.A.", lineas[0]);
        Assert.Equal("000000000121000", lineas[0].Substring(108, 15));          // total, sin signo ni separador
        Assert.Equal("000000000050000", lineas[1].Substring(108, 15));          // la NC va en positivo
        var alicuotas = alic.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(62, Assert.Single(alicuotas).Length);                      // la C no informa alícuotas
        Assert.Equal("0005", alicuotas[0].Substring(43, 4));                    // 21%
    }
}

/// <summary>Contra ALFANET2007_PRUEBAS (solo lectura): el libro del mes anterior cuadra con los asientos y exporta.
/// Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class LibroIvaVentasPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task ElLibroCuadraConLosAsientosYExporta()
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
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(runtime.Events).AddSingleton<DocumentPdfService>().BuildServiceProvider();
        var svc = new LibroIvaVentasService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events),
            runtime.Events, provider.GetRequiredService<DocumentPdfService>());

        var mes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        var unidades = await svc.GetUnidadesAsync();
        output.WriteLine("Unidades: " + string.Join(" | ", unidades.Select(u => $"{u.Codigo} {u.Descripcion} {u.Cuit}")));
        foreach (var unidad in new[] { "" }.Concat(unidades.Where(u => u.Cuit.Length > 0).Select(u => u.Codigo)))
        {
            var filtro = new LibroIvaVentasFiltro { Desde = mes, Hasta = mes.AddMonths(1).AddDays(-1), UnidadNegocio = unidad };
            var r = await svc.GetAsync(filtro);
            var esperado = await cn.ExecuteScalarAsync<decimal>("""
                SELECT ISNULL(SUM(LIVA_TOTAL),0) FROM dbo.MV_ASIENTOS WHERE LIVA_TIPO=N'VENTAS' AND FECHA>=@D AND FECHA<DATEADD(day,1,@H)
                  AND (@U='' OR LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@U)
                """, new { D = filtro.Desde, H = filtro.Hasta, U = unidad });
            output.WriteLine($"U«{unidad}» {r.Emisor} {r.CuitEmisor} {r.CondicionIvaEmisor}: {r.Filas.Count} cptes · neto {r.NetoGravado:N2} · IVA {r.Iva:N2} · total {r.Total:N2} (asientos {esperado:N2}) · " +
                string.Join(" ", r.TotalesPorAlicuota.Select(a => $"[{a.Alicuota}% {a.Neto:N2}/{a.Iva:N2}]")) + $" · cierre {r.CierreVentas:dd/MM/yyyy} · sin código {r.SinCodigoArca}");
            Assert.Equal(esperado, r.Total);
            if (unidad.Length == 0 || r.Filas.Count == 0) continue;
            var txt = await svc.ExportarTxtArcaAsync(filtro);
            var xls = await svc.ExportarExcelAsync(filtro);
            output.WriteLine($"  TXT {txt.Nombre} {txt.Contenido.Length} bytes · Excel {xls.Contenido.Length} bytes");
            Assert.True(txt.Contenido.Length > 100 && xls.Contenido.Length > 1000);
        }
        var pdf = await svc.ExportarPdfAsync(new LibroIvaVentasFiltro { Desde = mes, Hasta = mes.AddMonths(1).AddDays(-1), UnidadNegocio = unidades.First(u => u.Cuit.Length > 0).Codigo });
        output.WriteLine($"PDF {pdf.Contenido.Length} bytes");
        if (Environment.GetEnvironmentVariable("ALFACORE_MAIL_PDF") is { Length: > 0 } ruta) await File.WriteAllBytesAsync(ruta, pdf.Contenido);
        Assert.True(pdf.Contenido.Length > 1000);
    }
}

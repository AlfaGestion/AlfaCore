using System.Reflection;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class CierreCajaTests
{
    [Fact]
    public void ExportacionIncluyeTodasLasFilasYNumerosSinInterpretarTextoComoFormula()
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var section = new CierreCajaSeccion("prueba", "Prueba", "", "", [new("codigo", "Código", false, false), new("importe", "Importe", true, true)]);
        var rows = Enumerable.Range(1, 120).Select(i => (IReadOnlyDictionary<string, object?>)
            new Dictionary<string, object?> { ["codigo"] = "=1+1", ["importe"] = 12.5m }).ToArray();
        CierreCajaExportService.AddSheet(workbook, section, new(120, rows, new Dictionary<string, object?> { ["importe"] = 1500m }));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        using var restored = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = restored.Worksheet("prueba");
        Assert.Equal("=1+1", sheet.Cell(123, 1).GetString());
        Assert.False(sheet.Cell(123, 1).HasFormula);
        Assert.Equal(12.5, sheet.Cell(123, 2).GetDouble());
        Assert.Equal(1500, sheet.Cell(124, 2).GetDouble());
    }

    [Theory]
    [MemberData(nameof(Secciones))]
    public void ExportacionNoPaginaYConservaFiltroUnidad(string clave)
    {
        var sql = CierreCajaService.BuildSql(CierreCajaSecciones.Todas.Single(s => s.Clave == clave), true);
        Assert.DoesNotContain("OFFSET @Offset", sql);
        Assert.Contains("@unidad", sql);
        Assert.Contains("SELECT COUNT(*)", sql);
    }

    [Fact]
    public void OpcionesIndependientesIncluyenTodosLosInformesDelOriginal()
    {
        Assert.Equal(9, CierreCajaSecciones.Activas(new()).Count);
        var filtros = new CierreCajaFiltros { Diario = true, Mensual = true, Productos = true,
            Rubros = true, Cancelados = true, Ventas = true, Cobranzas = true };
        Assert.Equal(16, CierreCajaSecciones.Activas(filtros).Count);
        var copia = filtros with { };
        filtros.Diario = false;
        Assert.Contains(CierreCajaSecciones.Activas(copia), s => s.Clave == "diario");
    }

    public static IEnumerable<object[]> Secciones() => CierreCajaSecciones.Todas.Select(s => new object[] { s.Clave });

    [Theory]
    [MemberData(nameof(Secciones))]
    public void ConsultasTienenParametrosCompletosSinColisionesNiDependenciasWeb(string clave)
    {
        var seccion = CierreCajaSecciones.Todas.Single(s => s.Clave == clave);
        var sql = CierreCajaService.BuildSql(seccion);
        var args = CierreCajaService.BuildParameters(new() { Caja = " 2 ", UnidadNegocio = " 3 ", Fecha = new(2026, 9, 14), SaldoInicial = true }, seccion, 3, 50);
        Assert.Equal(100, args.Get<int>("Offset"));
        Assert.Equal("2", args.Get<string>("idcaja"));
        Assert.Equal("3", args.Get<string>("unidad"));
        Assert.Contains("@unidad", sql);
        Assert.DoesNotContain("sp_web_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXEC(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NW_RECONS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", sql);
        var declared = Regex.Matches(sql, @"DECLARE\s+(@\w+)", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in args.ParameterNames) Assert.DoesNotContain("@" + name, declared);
        declared.UnionWith(args.ParameterNames.Select(n => "@" + n));
        var used = Regex.Matches(sql, @"(?<!@)@\w+").Select(m => m.Value)
            .Where(n => !n.Equals("@FETCH_STATUS", StringComparison.OrdinalIgnoreCase));
        foreach (var name in used) Assert.Contains(name, declared);
    }

    [Fact]
    public async Task PermisoDenegadoImpideConsultarLaBaseYRegistraIncidente()
    {
        var events = DispatchProxy.Create<IAppEventService, EventProxy>();
        var service = new CierreCajaService(null!, null!, new DeniedPermission(), events);
        var ex = await Assert.ThrowsAsync<AppUserFacingException>(() => service.GetCajasAsync(DateTime.Today));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("permiso", ex.InnerException!.Message);
        Assert.Equal(1, ((EventProxy)(object)events).Calls);
    }

    [Fact]
    public async Task PermisoDenegadoTambienImpideExportar()
    {
        var events = DispatchProxy.Create<IAppEventService, EventProxy>();
        var service = new CierreCajaService(null!, null!, new DeniedPermission(), events);
        await Assert.ThrowsAsync<AppUserFacingException>(() => service.ExportarSeccionAsync(new(), "consolidado"));
        Assert.Equal(1, ((EventProxy)(object)events).Calls);
    }

    [Fact]
    public async Task EmailInvalidoSeRechazaAntesDeConfigurarOEnviarYRegistraError()
    {
        var events = DispatchProxy.Create<IAppEventService, EventProxy>();
        var service = new CierreCajaExportService(null!, null!, events);
        var error = await Assert.ThrowsAsync<AppUserFacingException>(() => service.EnviarEmailAsync(new("prueba.xlsx", [1]), "", default));
        Assert.IsNotType<NullReferenceException>(error.InnerException);
        Assert.Equal(1, ((EventProxy)(object)events).Calls);
    }

    private sealed class DeniedPermission : IPermissionService
    {
        public Task<IReadOnlySet<string>?> GetAllowedTaskKeysAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>?>(new HashSet<string> { "OTRO-MODULO" });
        public Task<bool> HasAccessAsync(string clave, CancellationToken ct = default) => Task.FromResult(false);
    }
    public class EventProxy : DispatchProxy
    {
        public int Calls;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IAppEventService.LogErrorAsync), targetMethod!.Name);
            Calls++;
            return Task.FromResult("TEST-CIERRE");
        }
    }
}

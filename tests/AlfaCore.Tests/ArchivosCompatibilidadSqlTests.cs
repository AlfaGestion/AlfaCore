using System.Globalization;
using System.Reflection;
using AlfaCore.Common;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ArchivosCompatibilidadSqlTests
{
    private static string? Conexion => Environment.GetEnvironmentVariable("ALFACORE_REPORTES_SQL");
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?>? handler = null) where T : class
    {
        var proxy = DispatchProxy.Create<T, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    private static ISessionService Sesiones() => Proxy<ISessionService>((m, _) => m.Name == "GetConnectionString" ? Conexion : null);
    private static IAppEventService Eventos() => Proxy<IAppEventService>((_, _) => Task.FromResult("PRUEBA_SQL"));

    [ReportesCompatibilidadSqlFact]
    public async Task ArticulosPaginasFiltrosOrdenNumericoYProximoCodigo()
    {
        var svc = new ArticulosService(new ConfigurationBuilder().Build(), Sesiones(), Eventos(), Proxy<IArticuloImagenFtpService>(), Proxy<ICentralBasesService>());
        var f = new ArticuloFilters { PageSize = 5 };
        var primera = await svc.SearchAsync(f);
        Assert.NotEmpty(primera.Items);
        Assert.All(primera.Items, x => Assert.True(x.Activo));
        f.PageNumber = 2;
        var segunda = await svc.SearchAsync(f);
        f.PageNumber = 1; f.PageSize = 10;
        var juntas = await svc.SearchAsync(f);
        Assert.Equal(primera.Items.Concat(segunda.Items).Select(x => x.Codigo), juntas.Items.Select(x => x.Codigo));
        await using var cn = new SqlConnection(Conexion); await cn.OpenAsync();
        Assert.Equal(await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MA_ARTICULOS WHERE ISNULL(SUSPENDIDO,0)=0"), primera.Total);
        f.SortBy = ArticuloViewColumnKeys.Codigo;
        var ordenada = await svc.SearchAsync(f);
        Assert.NotEmpty(ordenada.Items);
        f.SortDescending = true;
        Assert.NotEmpty((await svc.SearchAsync(f)).Items);
        var codigos = await cn.QueryAsync<string>("SELECT LTRIM(RTRIM(IDARTICULO)) FROM dbo.V_MA_ARTICULOS");
        var numericos = codigos.Select(x => long.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? (long?)n : null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        Assert.Equal(((numericos.Count == 0 ? 0 : numericos.Max()) + 1).ToString(CultureInfo.InvariantCulture), await svc.GetNextCodigoAsync());
        f.Texto = primera.Items[0].Codigo;
        var buscada = await svc.SearchAsync(f);
        Assert.NotEmpty(buscada.Items);
        Assert.Equal(await cn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.V_MA_ARTICULOS WHERE ISNULL(SUSPENDIDO,0)=0
                AND (ISNULL(IDARTICULO,'') LIKE @Texto OR ISNULL(CODIGOBARRA,'') LIKE @Texto
                    OR ISNULL(DESCRIPCION,'') COLLATE Latin1_General_CI_AI LIKE @Texto);
            """, new { Texto = "%" + f.Texto + "%" }), buscada.Total);
        f.Texto = "__ARTICULO_INEXISTENTE__";
        Assert.Empty((await svc.SearchAsync(f)).Items);
        f.Texto = ""; f.Activo = false;
        var bajas = await svc.SearchAsync(f);
        Assert.All(bajas.Items, x => Assert.False(x.Activo));
        Assert.Equal(await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_MA_ARTICULOS WHERE ISNULL(SUSPENDIDO,0)<>0"), bajas.Total);
    }

    [ReportesCompatibilidadSqlFact]
    public async Task TecnicosPaginasFiltrosYProximoCodigo()
    {
        var svc = new TecnicosService(new ConfigurationBuilder().Build(), Sesiones(), Eventos(), Proxy<ITecnicosValidator>());
        var f = new TecnicosFilters { PageSize = 1, Activo = null };
        var primera = await svc.SearchAsync(f);
        Assert.NotEmpty(primera.Items);
        f.PageNumber = 2;
        var segunda = await svc.SearchAsync(f);
        f.PageNumber = 1; f.PageSize = 2;
        Assert.Equal(primera.Items.Concat(segunda.Items).Select(x => x.IdTecnico), (await svc.SearchAsync(f)).Items.Select(x => x.IdTecnico));
        await using var cn = new SqlConnection(Conexion); await cn.OpenAsync();
        Assert.Equal(await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.V_TA_Tecnicos"), primera.Total);
        var codigos = await cn.QueryAsync<string>("SELECT LTRIM(RTRIM(IdTecnico)) FROM dbo.V_TA_Tecnicos");
        var numeros = codigos.Select(x => int.TryParse(x, out var n) ? (int?)n : null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        Assert.Equal(((numeros.Count == 0 ? 0 : numeros.Max()) + 1).ToString(), await svc.GetNextIdTecnicoAsync());
        f.Texto = primera.Items[0].Nombre;
        Assert.NotEmpty((await svc.SearchAsync(f)).Items);
        f.Texto = "__TECNICO_INEXISTENTE__";
        Assert.Empty((await svc.SearchAsync(f)).Items);
        f.Texto = ""; f.Activo = false;
        Assert.All((await svc.SearchAsync(f)).Items, x => Assert.True(x.Baja));
        f.Activo = true;
        Assert.All((await svc.SearchAsync(f)).Items, x => Assert.False(x.Baja));
    }

    [ReportesCompatibilidadSqlFact]
    public async Task ContactosPaginasBusquedaYOrdenAlternativo()
    {
        var svc = new ContactosService(new ConfigurationBuilder().Build(), Sesiones(), Eventos(), Proxy<IContactosValidator>(), NullLogger<ContactosService>.Instance);
        var f = new ContactosFilters { PageSize = 5, Activo = null };
        var primera = await svc.SearchAsync(f);
        Assert.NotEmpty(primera.Items);
        f.PageNumber = 2;
        var segunda = await svc.SearchAsync(f);
        f.PageNumber = 1; f.PageSize = 10;
        Assert.Equal(primera.Items.Concat(segunda.Items).Select(x => x.Id), (await svc.SearchAsync(f)).Items.Select(x => x.Id));
        await using var cn = new SqlConnection(Conexion); await cn.OpenAsync();
        Assert.Equal(await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MA_CONTACTOS"), primera.Total);
        f.OrdenarPor = ContactosViewColumnKeys.Localidad; f.OrdenDescendente = true;
        Assert.NotEmpty((await svc.SearchAsync(f)).Items);
        f.Texto = await cn.QuerySingleAsync<string>("""
            SELECT TOP (1) COALESCE(NULLIF(LTRIM(RTRIM(Nombre_y_Apellido)),''),NULLIF(LTRIM(RTRIM(email)),''))
            FROM dbo.MA_CONTACTOS WHERE NULLIF(LTRIM(RTRIM(Nombre_y_Apellido)),'') IS NOT NULL
                OR NULLIF(LTRIM(RTRIM(email)),'') IS NOT NULL ORDER BY id;
            """);
        Assert.NotEmpty((await svc.SearchAsync(f)).Items);
        f.Texto = "__CONTACTO_INEXISTENTE__";
        var vacia = await svc.SearchAsync(f);
        Assert.Equal(0, vacia.Total); Assert.Empty(vacia.Items);
        f.Texto = ""; f.Activo = true;
        Assert.All((await svc.SearchAsync(f)).Items, x => Assert.True(x.Activo));
        f.Activo = false;
        Assert.All((await svc.SearchAsync(f)).Items, x => Assert.False(x.Activo));
    }

    [ReportesCompatibilidadSqlFact]
    public async Task ConversionCompatibleDescartaAlfanumericosYDesbordesSinPerderCerosSignosOLimites()
    {
        await using var cn = new SqlConnection(Conexion); await cn.OpenAsync();
        foreach (var valor in new[] { "0", "000000000000000000000001", " 42 ", "+5", "-7", "2147483647", "2147483648", "-2147483648", "-2147483649", "9223372036854775807", "-9223372036854775808", "9223372036854775808", "-9223372036854775809", "9999999999999999999999999", "ABC", "1.5", "1e3", "+", "--1", "", "   " })
        {
            var row = await cn.QuerySingleAsync<(int? Entero, long? Largo)>(
                $"SELECT {SqlServerCompatibilidad.EnteroONull("@Valor")} Entero,{SqlServerCompatibilidad.EnteroONull("@Valor", bigint: true)} Largo;", new { Valor = valor });
            Assert.Equal(int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entero) ? (int?)entero : null, row.Entero);
            Assert.Equal(long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var largo) ? (long?)largo : null, row.Largo);
        }
    }
}

using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Claves de TA_CONFIGURACION por unidad: las mismas que lee y graba Tablas.frm del VB6.</summary>
public sealed class UnidadesNegocioClavesTests
{
    [Fact]
    public void ClavesPorUnidadIgualQueElVb6()
    {
        var claves = UnidadesNegocioService.Claves("  3 ");
        Assert.Equal("CONDICIONIVA_UN_3", claves["CondicionIva"]);
        Assert.Equal("DOMICILIO_UN_3", claves["Domicilio"]);
        Assert.Equal("INGRESOS_BRUTOS_UN_3", claves["IngresosBrutos"]);
        Assert.Equal("INICIO_ACTIVIDADES_UN_3", claves["InicioActividades"]);
        Assert.Equal("FORMADEPAGO_UN_3", claves["FormaDePago"]);
        Assert.Equal("AGENTE_RETPERC_UN_3", claves["AgenteRetPerc"]);
        Assert.Equal("AGENTE_RETGAN_UN_3", claves["AgenteRetGan"]);
    }
}

/// <summary>
/// Ensayo explícito contra ALFANET2007_PRUEBAS: graba la unidad 1 sin cambios (todo debe quedar igual)
/// y crea/borra una unidad de prueba. Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.
/// </summary>
[Collection("CargaComprobantes SQL")]
public sealed class UnidadesNegocioPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task GrabarSinCambiosNoAlteraNadaYAltaBajaFuncionan()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetConnectionString" => cs, _ => null };
        var svc = new UnidadesNegocioService(runtime.Configuration, runtime.Sessions, runtime.Events);

        foreach (var f in await svc.ListarAsync()) output.WriteLine($"{f.Codigo} · {f.Descripcion} · {f.Cuit} · {f.CondicionIva} · PV {f.PuntoVenta} · EFC {f.UsaFacturacionElectronica}");
        var catalogos = await svc.GetCatalogosAsync();
        output.WriteLine($"{catalogos.Depositos.Count} depósitos · {catalogos.MotivosStock.Count} motivos · {catalogos.CondicionesIva.Count} cond. IVA · grupos: {string.Join(",", catalogos.Grupos)}");

        const string fotoSql = """
            SELECT Codigo, Descripcion, DepositosAsociados, SUCURSALCUENTA, GRUPO, IdDepositoDif, MotivoParaDiferencias, ModificaDiferencias,
                   CUIT, RAZON_SOCIAL, RUTA_CRT, RUTA_KEY, PDV, USAEFC, RUTA_REPORTE,
                   (SELECT STUFF((SELECT '|' + CLAVE + '=' + ISNULL(VALOR,'') + '~' + ISNULL(CONVERT(nvarchar(max), ValorAux),'') FROM TA_CONFIGURACION
                                  WHERE CLAVE LIKE '%[_]UN[_]1' ORDER BY CLAVE FOR XML PATH('')), 1, 1, '')) Cfg
            FROM V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo)) = '1' FOR XML RAW;
            """;
        var antes = await cn.ExecuteScalarAsync<string>(fotoSql);
        var u1 = await svc.GetAsync("1");
        output.WriteLine($"U1: {u1.RazonSocial} · CUIT {u1.Cuit} · IVA {u1.CondicionIva} · {u1.Domicilio} · IIBB {u1.IngresosBrutos} · inicio {u1.InicioActividades} · dep {u1.Deposito} · grupo «{u1.Grupo}»");
        await svc.GuardarAsync(u1, esNueva: false);
        Assert.Equal(antes, await cn.ExecuteScalarAsync<string>(fotoSql));

        // Alta y baja de una unidad de prueba.
        await cn.ExecuteAsync("DELETE FROM V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo))='97'; DELETE FROM TA_CONFIGURACION WHERE CLAVE LIKE '%[_]UN[_]97';");
        await svc.GuardarAsync(new UnidadNegocioEdicion { Codigo = "97", Descripcion = "PRUEBA ALFACORE", CondicionIva = "5", Domicilio = "Calle 1", FormaDePago = "Alias x" }, esNueva: true);
        Assert.Equal("  97", await cn.ExecuteScalarAsync<string>("SELECT Codigo FROM V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo))='97'"));
        var u97 = await svc.GetAsync("97");
        Assert.Equal(("5", "Calle 1", "Alias x"), (u97.CondicionIva, u97.Domicilio, u97.FormaDePago));
        await Assert.ThrowsAsync<AppUserFacingException>(() => svc.GuardarAsync(u97, esNueva: true));
        await svc.EliminarAsync("97");
        Assert.Equal(0, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo))='97'") +
                        await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TA_CONFIGURACION WHERE CLAVE LIKE '%[_]UN[_]97'"));
        // La unidad 1 tiene comprobantes: no se puede borrar.
        await Assert.ThrowsAsync<AppUserFacingException>(() => svc.EliminarAsync("1"));
    }
}

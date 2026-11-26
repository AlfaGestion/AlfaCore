using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using static AlfaCore.Tests.PlanCuentasAbmSqlTests;

namespace AlfaCore.Tests;

/// <summary>
/// Prueba la tabla de comprobantes contra una base real (COMPROBANTES_TEST_SQL): da de alta el comprobante de prueba ZPRB con dos
/// talonarios, revisa validaciones, lo modifica y lo borra. El trigger FMT_V_TA_Cpte crea su copia en TA_COMPROBANTES: también se borra.
/// </summary>
public class ComprobantesTablaSqlTests
{
    private const string Codigo = "ZPRB";

    [ComprobantesSqlFact]
    public async Task AltaTalonariosModificacionYBajaComoElLegacy()
    {
        var connection = Environment.GetEnvironmentVariable("COMPROBANTES_TEST_SQL")!;
        var service = new ComprobantesTablaService(new Sesion(connection), new Usuario(), new Permisos(), new Eventos());
        await Limpiar(connection);
        try
        {
            var catalogo = await service.CatalogoAsync();
            Assert.NotEmpty(catalogo.Unidades);
            var lista = await service.ListarAsync();
            Assert.Contains(lista, c => c.Codigo == "FC");

            // Código existente con movimientos: propone el próximo talonario y no deja tocar sistema ni atributos.
            var fc = await service.PrepararAltaAsync("fc");
            Assert.True(fc.EsNuevo && fc.CodigoExistente && !fc.SistemaEditable && !fc.EntradaSalidaEditable && !fc.DebeHaberEditable);
            Assert.Equal(lista.Where(c => c.Codigo == "FC").Max(c => c.Talonario) + 1, fc.Datos.Talonario);
            Assert.Equal(("Ventas", "S", "D"), (fc.Datos.Sistema, fc.Datos.EntradaSalida, fc.Datos.DebeHaber));

            var alta = await service.PrepararAltaAsync(Codigo);
            Assert.True(alta.SistemaEditable && !alta.CodigoExistente);
            Assert.Equal(0, alta.Datos.Talonario);
            var t = alta.Datos;
            t.Descripcion = "PRUEBA TABLA CPTES WEB"; t.Sistema = "Contable"; t.EntradaSalida = "E"; t.PuntoVenta = 9998;
            t.Unidad = catalogo.Unidades[0].Codigo; t.Letras = "BA";
            t.Letra("A").Desde = 1; t.Letra("A").Hasta = 100; t.Letra("A").Proximo = 1;
            t.Letra("B").Desde = 1; t.Letra("B").Hasta = 100; t.Letra("B").Proximo = 5; t.Letra("B").CpteAfip = "6";
            var g = await service.GuardarAsync(t, nuevo: true);
            Assert.Equal(("Contable", "", 9998, "BA"), (g.Sistema, g.EntradaSalida, g.PuntoVenta, g.Letras));

            await using (var cn = new SqlConnection(connection))
            {
                var raw = await cn.QuerySingleAsync("SELECT UNEGOCIO, A_SUC_DEFAULT, X_SUC_DEFAULT, M_SUC_DEFAULT, FechaHora_Grabacion FROM dbo.V_TA_CPTE WHERE CODIGO = @Codigo;", new { Codigo });
                Assert.Equal(catalogo.Unidades[0].Codigo, ((string)raw.UNEGOCIO).Trim());
                Assert.Equal((9998, 9998, 9998), ((int)raw.A_SUC_DEFAULT, (int)raw.X_SUC_DEFAULT, (int)raw.M_SUC_DEFAULT));
                Assert.NotNull(raw.FechaHora_Grabacion);
                Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TA_COMPROBANTES WHERE CODIGO = @Codigo;", new { Codigo }));
            }

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.GuardarAsync(t, nuevo: true)); // ya existe

            // Segundo talonario del mismo punto de venta: el rango no puede superponerse.
            var segundo = await service.PrepararAltaAsync(Codigo);
            Assert.Equal(1, segundo.Datos.Talonario);
            Assert.Equal("Contable", segundo.Datos.Sistema);
            var s = segundo.Datos;
            s.PuntoVenta = 9998; s.Letras = "A";
            s.Letra("A").Desde = 50; s.Letra("A").Hasta = 200; s.Letra("A").Proximo = 50;
            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.GuardarAsync(s, nuevo: true));
            Assert.Contains("superpone", ex.Message);
            s.Letra("A").Desde = 101; s.Letra("A").Proximo = 101;
            await service.GuardarAsync(s, nuevo: true);

            // Con otro talonario no puede ser externo.
            var edit = await service.ObtenerAsync(Codigo, 0);
            Assert.Equal(1, edit.OtrosTalonarios);
            edit.Datos.Externo = true;
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.GuardarAsync(edit.Datos, nuevo: false));

            edit = await service.ObtenerAsync(Codigo, 0);
            edit.Datos.Descripcion = "PRUEBA TABLA CPTES WEB MOD";
            edit.Datos.Letra("A").Proximo = 101; // fuera del rango 1-100
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.GuardarAsync(edit.Datos, nuevo: false));
            edit.Datos.Letra("A").Proximo = 7;
            var mod = await service.GuardarAsync(edit.Datos, nuevo: false);
            Assert.Equal(("PRUEBA TABLA CPTES WEB MOD", 7, "BA"), (mod.Descripcion, mod.Letra("A").Proximo, mod.Letras));

            await service.EliminarAsync(Codigo, 1);
            await service.EliminarAsync(Codigo, 0);
            Assert.DoesNotContain(await service.ListarAsync(), c => c.Codigo == Codigo);
        }
        finally
        {
            await Limpiar(connection);
        }
    }

    private static async Task Limpiar(string connection)
    {
        await using var cn = new SqlConnection(connection);
        await cn.ExecuteAsync("DELETE FROM dbo.V_TA_CPTE WHERE CODIGO = @Codigo; DELETE FROM dbo.TA_COMPROBANTES WHERE CODIGO = @Codigo;", new { Codigo });
    }
}

public sealed class ComprobantesSqlFactAttribute : FactAttribute
{
    public ComprobantesSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPROBANTES_TEST_SQL")))
            Skip = "Requiere una base Alfa Gestión de prueba en COMPROBANTES_TEST_SQL; da de alta y borra el comprobante ZPRB.";
    }
}

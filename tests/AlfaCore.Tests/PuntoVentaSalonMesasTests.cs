using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static AlfaCore.Tests.PlanCuentasAbmSqlTests;

namespace AlfaCore.Tests;

public class PuntoVentaSalonMesasTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(8, true)]
    [InlineData(20, false)]
    [InlineData(60, true)]
    public void Las_mesas_quedan_dentro_del_plano_sin_pisarse_ni_pisar_la_barra(int cantidad, bool conBarra)
    {
        var mesas = PuntoVentaConfigService.DistribuirMesas(cantidad, conBarra);
        Assert.Equal(cantidad, mesas.Count);
        var barra = PuntoVentaConfigService.PosicionBarra();
        foreach (var m in mesas)
        {
            Assert.InRange(m.X, 0, 1000 - m.W);
            Assert.InRange(m.Y, 0, 600 - m.H);
            Assert.True(m.W >= 20);
            if (conBarra)
                Assert.True(m.Y >= barra.Y + barra.H, "La mesa queda debajo de la barra.");
        }
        for (var i = 0; i < mesas.Count; i++)
            for (var j = i + 1; j < mesas.Count; j++)
                Assert.False(mesas[i].X < mesas[j].X + mesas[j].W && mesas[j].X < mesas[i].X + mesas[i].W
                             && mesas[i].Y < mesas[j].Y + mesas[j].H && mesas[j].Y < mesas[i].Y + mesas[i].H, $"Las mesas {i} y {j} se pisan.");
    }

    [Fact]
    public void Cinco_mesas_van_en_dos_filas_de_tres_y_dos()
    {
        var filas = PuntoVentaConfigService.DistribuirMesas(5, conBarra: true).GroupBy(m => m.Y).Select(g => g.Count()).ToList();
        Assert.Equal([3, 2], filas);
    }

    [Theory]
    [InlineData("M12", 12)]
    [InlineData("M1", 1)]
    [InlineData("BAR", int.MaxValue)]
    public void Numero_de_mesa_sale_del_codigo(string codigo, int numero)
        => Assert.Equal(numero, PuntoVentaConfigService.NumeroMesa(codigo));

    /// <summary>
    /// Contra una base real (COMPROBANTES_TEST_SQL): un punto de venta de salón nuevo recibe sector, barra y 5 mesas;
    /// «Cantidad de mesas» agrega, borra, da de baja la que tiene historia y la reactiva; una mesa con historia no se borra.
    /// </summary>
    [ComprobantesSqlFact]
    public async Task ArmaElSalonYAjustaLaCantidadDeMesas()
    {
        var connection = Environment.GetEnvironmentVariable("COMPROBANTES_TEST_SQL")!;
        var config = new ConfigurationBuilder().Build();
        var sesion = new Sesion(connection);
        var service = new PuntoVentaConfigService(config, sesion, new Eventos(), new PuntoVentaConfigValidator(config, sesion),
            NullLogger<PuntoVentaConfigService>.Instance);
        await using var cn = new SqlConnection(connection);
        var idPv = 0;
        try
        {
            idPv = await service.SavePuntoVentaAsync(new PuntoVentaEntidadSaveRequest
            {
                Codigo = "ZT3", Nombre = "PRUEBA SALON", Modo = PuntoVentaModoKeys.Salon, Activo = true, UsuarioAccion = "TEST"
            });
            var sector = Assert.Single(await service.GetSectoresAsync(idPv));
            Assert.Equal("SAL", sector.Codigo.Trim());

            var mesas = await service.GetMesasAsync(sector.Id);
            Assert.Contains(mesas, m => m.Codigo.Trim() == "BAR" && m.Width > m.Height);
            Assert.Equal(5, mesas.Count(m => m.Codigo.Trim() != "BAR"));
            Assert.All(mesas, m => Assert.NotNull(m.PosX));

            var r = await service.AjustarCantidadMesasAsync(sector.Id, 8, 4, redistribuir: true, "TEST");
            Assert.Equal(3, r.Agregadas);
            mesas = await service.GetMesasAsync(sector.Id);
            Assert.Equal(8, mesas.Count(m => m.Activo && m.Codigo.Trim() != "BAR"));

            // M8 tuvo un llamado: al bajar a 6 se da de baja (no se borra); M7 se borra.
            var m8 = mesas.Single(m => m.Codigo.Trim() == "M8").Id;
            await cn.ExecuteAsync("INSERT INTO dbo.POS_MESA_LLAMADO (IDPUNTOVENTA, IDMESA, TIPO, ATENDIDO) VALUES (@Pv, @Mesa, N'MOZO', 1);",
                new { Pv = idPv, Mesa = m8 });
            r = await service.AjustarCantidadMesasAsync(sector.Id, 6, null, redistribuir: true, "TEST");
            Assert.Equal((1, 1), (r.Borradas, r.Desactivadas));
            mesas = await service.GetMesasAsync(sector.Id);
            Assert.False(mesas.Single(m => m.Id == m8).Activo);
            Assert.DoesNotContain(mesas, m => m.Codigo.Trim() == "M7");

            r = await service.AjustarCantidadMesasAsync(sector.Id, 7, null, redistribuir: false, "TEST");
            Assert.Equal((1, 0), (r.Reactivadas, r.Agregadas));

            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.DeleteMesaAsync(m8));
            Assert.Contains("desmarcá «Activa»", ex.Message);
            await service.DeleteMesaAsync(mesas.Single(m => m.Codigo.Trim() == "M1").Id);
            Assert.DoesNotContain(await service.GetMesasAsync(sector.Id), m => m.Codigo.Trim() == "M1");
        }
        finally
        {
            await cn.ExecuteAsync("""
                DELETE FROM dbo.POS_MESA_LLAMADO WHERE IDPUNTOVENTA = @Pv;
                DELETE FROM dbo.POS_MESA WHERE IDSECTOR IN (SELECT ID FROM dbo.POS_SECTOR WHERE IDPUNTOVENTA = @Pv);
                DELETE FROM dbo.POS_SECTOR WHERE IDPUNTOVENTA = @Pv;
                DELETE FROM dbo.POS_PUNTOVENTA_RUBRO WHERE IDPUNTOVENTA = @Pv;
                DELETE FROM dbo.POS_PUNTOVENTA WHERE ID = @Pv AND CODIGO = N'ZT3';
                """, new { Pv = idPv });
        }
    }
}

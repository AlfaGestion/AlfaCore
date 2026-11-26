using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static AlfaCore.Tests.PlanCuentasAbmSqlTests;

namespace AlfaCore.Tests;

public class PuntoVentaBorrarTests
{
    [Fact]
    public void Sin_movimientos_se_puede_borrar()
        => Assert.Null(PuntoVentaConfigService.MotivoNoBorrar([("pedido(s)", 0), ("turno(s) de caja", 0)]));

    [Fact]
    public void Con_movimientos_dice_cuales_y_sugiere_desactivar()
    {
        var motivo = PuntoVentaConfigService.MotivoNoBorrar([("pedido(s)", 3), ("comanda(s)", 0), ("turno(s) de caja", 1)]);
        Assert.Equal("El punto de venta tiene movimientos (3 pedido(s), 1 turno(s) de caja): no se puede borrar. Si no se usa más, desmarcá «Activo».", motivo);
    }

    /// <summary>
    /// Contra una base real (COMPROBANTES_TEST_SQL): crea un punto de venta de prueba con sector, mesa y rubro y lo borra;
    /// crea otro con un turno de caja y comprueba que no se deja borrar. Limpia todo al final.
    /// </summary>
    [ComprobantesSqlFact]
    public async Task BorraSinMovimientosYBloqueaConTurno()
    {
        var connection = Environment.GetEnvironmentVariable("COMPROBANTES_TEST_SQL")!;
        var service = new PuntoVentaConfigService(new ConfigurationBuilder().Build(), new Sesion(connection), new Eventos(), null!,
            NullLogger<PuntoVentaConfigService>.Instance);
        await using var cn = new SqlConnection(connection);
        int libre = 0, conTurno = 0;
        try
        {
            libre = await cn.ExecuteScalarAsync<int>("""
                INSERT INTO dbo.POS_PUNTOVENTA (CODIGO, NOMBRE, MODO, ACTIVO) VALUES (N'ZT1', N'PRUEBA BORRAR', N'SALON', 1);
                DECLARE @pv int = CAST(SCOPE_IDENTITY() AS int);
                INSERT INTO dbo.POS_SECTOR (CODIGO, IDPUNTOVENTA, NOMBRE) VALUES (N'ZT', @pv, N'SECTOR PRUEBA');
                INSERT INTO dbo.POS_MESA (CODIGO, IDSECTOR, NOMBRE) VALUES (N'ZT', CAST(SCOPE_IDENTITY() AS int), N'MESA PRUEBA');
                INSERT INTO dbo.POS_PUNTOVENTA_RUBRO (IDPUNTOVENTA, IDRUBRO) VALUES (@pv, N'ZT');
                SELECT @pv;
                """);
            conTurno = await cn.ExecuteScalarAsync<int>("""
                INSERT INTO dbo.POS_PUNTOVENTA (CODIGO, NOMBRE, MODO, ACTIVO) VALUES (N'ZT2', N'PRUEBA CON TURNO', N'MOSTRADOR', 1);
                DECLARE @pv int = CAST(SCOPE_IDENTITY() AS int);
                INSERT INTO dbo.POS_TURNO (IDPUNTOVENTA, FECHAOPERATIVA, FECHAHORA_APERTURA) VALUES (@pv, CAST(GETDATE() AS date), GETDATE());
                SELECT @pv;
                """);

            await service.DeletePuntoVentaAsync(libre);
            Assert.Equal(0, await cn.ExecuteScalarAsync<int>("""
                SELECT (SELECT COUNT(*) FROM dbo.POS_PUNTOVENTA WHERE ID = @Id) + (SELECT COUNT(*) FROM dbo.POS_SECTOR WHERE IDPUNTOVENTA = @Id)
                     + (SELECT COUNT(*) FROM dbo.POS_PUNTOVENTA_RUBRO WHERE IDPUNTOVENTA = @Id) + (SELECT COUNT(*) FROM dbo.POS_MESA WHERE CODIGO = N'ZT' AND NOMBRE = N'MESA PRUEBA');
                """, new { Id = libre }));

            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.DeletePuntoVentaAsync(conTurno));
            Assert.Contains("1 turno(s) de caja", ex.Message);
            Assert.Equal(1, await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.POS_PUNTOVENTA WHERE ID = @Id;", new { Id = conTurno }));
        }
        finally
        {
            await cn.ExecuteAsync("""
                DELETE FROM dbo.POS_TURNO WHERE IDPUNTOVENTA IN (@Libre, @ConTurno);
                DELETE FROM dbo.POS_MESA WHERE CODIGO = N'ZT' AND NOMBRE = N'MESA PRUEBA';
                DELETE FROM dbo.POS_SECTOR WHERE IDPUNTOVENTA IN (@Libre, @ConTurno);
                DELETE FROM dbo.POS_PUNTOVENTA_RUBRO WHERE IDPUNTOVENTA IN (@Libre, @ConTurno);
                DELETE FROM dbo.POS_PUNTOVENTA WHERE ID IN (@Libre, @ConTurno) AND NOMBRE LIKE N'PRUEBA %';
                """, new { Libre = libre, ConTurno = conTurno });
        }
    }
}

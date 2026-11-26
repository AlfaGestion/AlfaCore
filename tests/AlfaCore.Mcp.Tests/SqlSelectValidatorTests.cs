using AlfaCore.Mcp.Security;
using Xunit;

namespace AlfaCore.Mcp.Tests;

public class SqlSelectValidatorTests
{
    [Theory]
    [InlineData("SELECT TOP 10 RubroNombre, SUM(NetoSinIva) FROM dir.vw_Ventas GROUP BY RubroNombre")]
    [InlineData("select * from [dir].[vw_Clientes]")]
    [InlineData("SELECT a.Tc FROM dir.vw_Ventas a INNER JOIN dir.vw_Clientes c ON c.ClienteCodigo = a.ClienteCodigo")]
    [InlineData("SELECT * FROM (SELECT Tc FROM dir.vw_Ventas) x")]
    [InlineData("SELECT Tc FROM dir.vw_Ventas WHERE ArticuloCodigo IN (SELECT ArticuloCodigo FROM dir.vw_Articulos)")]
    [InlineData("WITH m AS (SELECT AnioMes, SUM(NetoSinIva) v FROM dir.vw_Ventas GROUP BY AnioMes) SELECT * FROM m")]
    [InlineData("SELECT 1 AS uno")]
    [InlineData("SELECT Tc FROM dir.vw_Ventas UNION ALL SELECT Tc FROM dir.vw_Ventas;")]
    [InlineData("SELECT Tc FROM dir.vw_Ventas WITH (NOLOCK) WHERE Fecha >= '2026-10-01'")]
    [InlineData("-- comentario\nSELECT GETDATE() AS ahora, DATEDIFF(day, '20260101', GETDATE()) AS d")]
    public void Acepta_selects_sobre_el_esquema_dir(string sql)
    {
        var r = SqlSelectValidator.Validar(sql);
        Assert.True(r.Valido, r.Error);
    }

    [Theory]
    [InlineData("", "vacía")]
    [InlineData("   ", "vacía")]
    [InlineData("INSERT INTO dir.vw_Ventas (Tc) VALUES ('x')", "SELECT")]
    [InlineData("UPDATE dir.vw_Ventas SET Tc = 'x'", "SELECT")]
    [InlineData("DELETE FROM dir.vw_Ventas", "SELECT")]
    [InlineData("DROP TABLE dir.vw_Ventas", "SELECT")]
    [InlineData("EXEC sp_who", "SELECT")]
    [InlineData("SELECT 1; DROP TABLE dbo.V_MV_Cpte", "única")]
    [InlineData("SELECT 1; SELECT 2", "única")]
    [InlineData("SELECT * FROM dbo.V_MV_Cpte", "esquema dir")]
    [InlineData("SELECT * FROM V_MV_Cpte", "esquema dir")]
    [InlineData("SELECT * FROM sys.objects", "esquema dir")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.TABLES", "esquema dir")]
    [InlineData("SELECT * FROM dir.vw_Ventas v JOIN dbo.MA_CUENTAS c ON 1 = 1", "esquema dir")]
    [InlineData("SELECT * FROM dir.vw_Ventas WHERE ArticuloCodigo IN (SELECT IDARTICULO FROM dbo.V_MA_ARTICULOS)", "esquema dir")]
    [InlineData("WITH V_MV_Cpte AS (SELECT * FROM dbo.V_MV_Cpte) SELECT * FROM V_MV_Cpte", "esquema dir")]
    [InlineData("SELECT * FROM OtroServidor.master.dbo.sysdatabases", "servidor")]
    [InlineData("SELECT * FROM master.dbo.sysdatabases", "servidor ni de base")]
    [InlineData("SELECT * INTO #t FROM dir.vw_Ventas", "INTO")]
    [InlineData("SELECT * INTO dbo.copia FROM dir.vw_Ventas", "INTO")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;', 'SELECT 1')", "no permitido")]
    [InlineData("SELECT * FROM OPENQUERY(srv, 'SELECT 1')", "no permitido")]
    [InlineData("SELECT * FROM dir.fn_algo()", "no permitido")]
    [InlineData("SELECT * FROM dbo.fn_split('a,b', ',')", "no permitido")]
    [InlineData("SELECT dbo.fn_secreta(1)", "funciones")]
    [InlineData("SELECT @@VERSION", "globales")]
    [InlineData("SELECT @x", "variables")]
    [InlineData("SELECT @x = 1", "variables")]
    [InlineData("WAITFOR DELAY '00:00:30'", "SELECT")]
    [InlineData("SELEC nada", "SELECT")]
    public void Rechaza_lo_que_no_es_un_select_sobre_dir(string sql, string fragmentoDelMensaje)
    {
        var r = SqlSelectValidator.Validar(sql);
        Assert.False(r.Valido, $"Debió rechazar: {sql}");
        Assert.Contains(fragmentoDelMensaje, r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rechaza_consultas_demasiado_largas()
    {
        var sql = "SELECT 1 /*" + new string('x', SqlSelectValidator.LargoMaximo) + "*/";
        Assert.False(SqlSelectValidator.Validar(sql).Valido);
    }

    [Fact]
    public void Rechaza_sintaxis_posterior_a_SQL_2008_que_la_base_no_ejecutaria()
    {
        // OFFSET/FETCH existe desde SQL 2012; las bases a compat 80/100 lo rechazan, y el parser 2008 lo corta antes.
        var r = SqlSelectValidator.Validar("SELECT Tc FROM dir.vw_Ventas ORDER BY Fecha OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");
        Assert.False(r.Valido);
    }
}

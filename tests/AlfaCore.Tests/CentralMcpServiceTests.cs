using AlfaCore.Compartido;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Consultas con IA desde /admin: clave del login de solo lectura, nombre del login y SQL que lo crea.</summary>
public class CentralMcpServiceTests
{
    [Fact]
    public void La_clave_generada_es_larga_variada_distinta_y_segura_para_SQL()
    {
        var claves = Enumerable.Range(0, 50).Select(_ => ClaveBaseMcp.NuevaClave()).ToList();
        Assert.Equal(50, claves.Distinct().Count());
        foreach (var c in claves)
        {
            Assert.Equal(32, c.Length);
            Assert.Contains(c, char.IsAsciiLetterUpper);
            Assert.Contains(c, char.IsAsciiLetterLower);
            Assert.Contains(c, char.IsAsciiDigit);
            Assert.Contains(c, ch => !char.IsAsciiLetterOrDigit(ch));
            Assert.True(ClaveBaseMcp.EsSeguroParaSql(c));
            Assert.DoesNotContain('\'', c);
            Assert.DoesNotContain(']', c);
        }
    }

    [Fact]
    public void La_clave_cifrada_no_contiene_la_clave_y_se_recupera_en_esta_maquina()
    {
        if (!OperatingSystem.IsWindows()) return;
        var clave = ClaveBaseMcp.NuevaClave();
        var cifrada = ClaveBaseMcp.Cifrar(clave);
        Assert.StartsWith(ClaveBaseMcp.Prefijo, cifrada);
        Assert.DoesNotContain(clave, cifrada);
        Assert.NotEqual(cifrada, ClaveBaseMcp.Cifrar(clave));   // DPAPI agrega aleatoriedad
        Assert.Equal(clave, ClaveBaseMcp.Descifrar(cifrada));
        Assert.True(ClaveBaseMcp.EstaCifradaConDpapi(cifrada));
        Assert.False(ClaveBaseMcp.EstaCifradaConDpapi("otro-formato"));
    }

    [Fact]
    public void Cada_base_tiene_su_propio_login()
    {
        Assert.Equal("mcp_ro_4283", ClaveBaseMcp.NombreLogin(4283));
        Assert.NotEqual(ClaveBaseMcp.NombreLogin(1), ClaveBaseMcp.NombreLogin(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClaveBaseMcp.NombreLogin(0));
        Assert.True(ClaveBaseMcp.EsSeguroParaSql(ClaveBaseMcp.NombreLogin(4283)));
        Assert.False(ClaveBaseMcp.EsSeguroParaSql("x]; DROP LOGIN sa; --"));
    }

    [Fact]
    public void El_SQL_que_crea_el_login_usa_parametros_y_QUOTENAME_y_solo_da_lectura_de_dir()
    {
        var sql = CentralMcpService.SqlCrearLogin;
        Assert.Contains("QUOTENAME(@Login)", sql);
        Assert.Contains("QUOTENAME(@Clave, '''')", sql);
        Assert.Contains("GRANT SELECT ON SCHEMA::[dir]", sql);
        Assert.Contains("GRANT INSERT ON OBJECT::[dbo].[IA_LOG_CONSULTAS]", sql);
        Assert.DoesNotContain("db_datareader", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sp_addrolemember", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER ROLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHECK_POLICY = ON", sql);
    }

    [Theory]
    [InlineData(" Ana@Cliente.COM ", "ana@cliente.com")]
    [InlineData("112010841", null)]              // usuario central que no es un mail: no puede recibir el codigo
    [InlineData("sin-arroba.com", null)]
    [InlineData("a@b", null)]
    public void Solo_los_usuarios_que_son_mail_pueden_tener_consultas_IA(string usuario, string? esperado) =>
        Assert.Equal(esperado, CentralMcpService.NormalizarEmail(usuario));

    [Fact]
    public void La_invitacion_lleva_la_url_el_mail_y_las_bases()
    {
        var t = CentralMcpService.TextoInvitacion("ana@cliente.com", ["Panaderia", "Sucursal Sur"], "https://mcp.ejemplo/mcp");
        Assert.Contains("https://mcp.ejemplo/mcp", t);
        Assert.Contains("ana@cliente.com", t);
        Assert.Contains("\"Panaderia\", \"Sucursal Sur\"", t);
        Assert.Contains("varias bases", t);
        Assert.DoesNotContain("varias bases", CentralMcpService.TextoInvitacion("a@b.com", ["Una"], "u"));
    }
}

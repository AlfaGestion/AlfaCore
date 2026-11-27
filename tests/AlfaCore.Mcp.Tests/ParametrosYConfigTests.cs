using AlfaCore.Mcp.Infra;
using AlfaCore.Mcp.Tools;
using Xunit;

namespace AlfaCore.Mcp.Tests;

public class ParametrosYConfigTests
{
    private static readonly McpRuntimeSettings Cfg = McpRuntimeSettings.Desde(new Dictionary<string, string>
    {
        ["IA_MCP_HABILITADO"] = "SI", ["IA_MCP_TOP_MAXIMO"] = "20"
    });

    [Theory]
    [InlineData("2026-10-31")]
    [InlineData("20261031")]
    [InlineData(" 2026-10-31 ")]
    public void Fecha_acepta_formatos_iso(string texto) =>
        Assert.Equal(new DateTime(2026, 10, 31), Parametros.Fecha(texto, "desde"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("31/10/2026")]   // ambiguo (d/m vs m/d): se rechaza a proposito
    [InlineData("2026-13-01")]
    [InlineData("mañana")]
    public void Fecha_rechaza_lo_ambiguo_o_invalido(string? texto) =>
        Assert.Throws<ToolUserException>(() => Parametros.Fecha(texto, "desde"));

    [Fact]
    public void Rango_es_inclusivo_y_devuelve_hasta_exclusivo()
    {
        var (d, h) = Parametros.Rango("2026-10-01", "2026-10-31");
        Assert.Equal(new DateTime(2026, 10, 1), d);
        Assert.Equal(new DateTime(2026, 11, 1), h);
    }

    [Fact]
    public void Rango_invertido_falla_con_mensaje_claro()
    {
        var ex = Assert.Throws<ToolUserException>(() => Parametros.Rango("2026-10-31", "2026-10-01"));
        Assert.Contains("anterior", ex.Message);
    }

    [Theory]
    [InlineData("MES", "mes")]
    [InlineData("  rubro ", "rubro")]
    [InlineData(null, "mes")]
    public void Opcion_normaliza_y_aplica_defecto(string? entrada, string esperado) =>
        Assert.Equal(esperado, Parametros.Opcion(entrada, "agrupar_por", ["dia", "mes", "vendedor", "rubro", "cliente"], "mes"));

    [Fact]
    public void Opcion_rechaza_valores_fuera_de_la_lista_cerrada()
    {
        // Esto es lo que impide que texto del modelo llegue al SQL: solo pasan valores de la lista.
        Assert.Throws<ToolUserException>(() =>
            Parametros.Opcion("mes; DROP TABLE x", "agrupar_por", ["dia", "mes"], "mes"));
    }

    [Fact]
    public void Top_se_limita_al_maximo_configurado_y_avisa()
    {
        var r = new ToolResponse();
        Assert.Equal(20, Parametros.Top(500, Cfg, r));
        Assert.Single(r.Advertencias);
        Assert.Equal(10, Parametros.Top(null, Cfg, new ToolResponse()));
        Assert.Throws<ToolUserException>(() => Parametros.Top(0, Cfg, new ToolResponse()));
    }

    [Fact]
    public void Sin_claves_en_la_base_lo_riesgoso_queda_apagado()
    {
        var c = McpRuntimeSettings.Desde(new Dictionary<string, string>());
        Assert.False(c.Habilitado);
        Assert.False(c.ConsultarSqlHabilitado);
        Assert.True(c.IncluirProformas);
        Assert.Equal(500, c.MaxFilas);
        Assert.Equal(60, c.TimeoutSeg);
    }

    [Fact]
    public void La_lectura_sin_bloqueos_viene_prendida_por_defecto_y_se_apaga_con_NO()
    {
        Assert.True(McpRuntimeSettings.Desde(new Dictionary<string, string>()).LecturaSinBloqueos);
        Assert.True(McpRuntimeSettings.Desde(new Dictionary<string, string> { ["IA_MCP_LECTURA_SIN_BLOQUEOS"] = "SI" }).LecturaSinBloqueos);
        Assert.False(McpRuntimeSettings.Desde(new Dictionary<string, string> { ["IA_MCP_LECTURA_SIN_BLOQUEOS"] = "NO" }).LecturaSinBloqueos);
    }

    [Fact]
    public void Config_lee_valores_y_tolera_basura_y_decimales_con_coma()
    {
        var c = McpRuntimeSettings.Desde(new Dictionary<string, string>
        {
            ["IA_MCP_HABILITADO"] = "SI", ["IA_MCP_MAX_FILAS"] = "abc", ["IA_MCP_TIMEOUT_SEG"] = "999999",
            ["IA_MCP_ADVERTIR_SIN_COSTO_PCT"] = "7,5", ["IA_MCP_INCLUIR_PROFORMAS"] = "NO"
        });
        Assert.True(c.Habilitado);
        Assert.Equal(500, c.MaxFilas);          // basura => defecto
        Assert.Equal(600, c.TimeoutSeg);        // acotado al maximo razonable
        Assert.Equal(7.5m, c.AdvertirSinCostoPct);
        Assert.False(c.IncluirProformas);
    }

    [Fact]
    public void Palabras_separa_y_quita_repetidas()
    {
        Assert.Equal(["tornillo", "8mm"], Parametros.Palabras("  tornillo   8mm TORNILLO ", "texto"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("a")]                      // demasiado corto: devolveria toda la tabla
    [InlineData("a b c d e f")]            // mas de 5 palabras
    public void Palabras_rechaza_busquedas_vacias_o_demasiado_amplias(string? texto) =>
        Assert.Throws<ToolUserException>(() => Parametros.Palabras(texto, "texto"));

    [Theory]
    [InlineData("perez", "%perez%")]
    [InlineData("50%", "%50[%]%")]
    [InlineData("a_b", "%a[_]b%")]
    [InlineData("[x]", "%[[]x]%")]
    public void Contiene_escapa_los_comodines_de_like(string palabra, string esperado) =>
        Assert.Equal(esperado, Parametros.Contiene(palabra));

    [Fact]
    public void Codigo_recorta_y_valida()
    {
        Assert.Equal("00123", Parametros.Codigo(" 00123 ", "cliente"));
        Assert.Null(Parametros.CodigoOpcional("  ", "cliente"));
        Assert.Throws<ToolUserException>(() => Parametros.Codigo(null, "cliente"));
        Assert.Throws<ToolUserException>(() => Parametros.Codigo(new string('x', 51), "cliente"));
    }
}

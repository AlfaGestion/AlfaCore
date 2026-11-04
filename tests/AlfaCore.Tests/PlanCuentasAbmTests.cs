using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public class PlanCuentasAbmTests
{
    // Estructura de ALFANET: 1-1-1-2-4-0 → largos 1, 2, 3, 5, 9 (9).
    private static readonly PlanCuentasEstructura Alfa = new() { Digitos = [1, 1, 1, 2, 4, 0] };

    [Theory]
    [InlineData("4", 0)]
    [InlineData("42", 1)]
    [InlineData("425", 2)]
    [InlineData("42502", 3)]
    [InlineData("112010681", 4)]
    [InlineData("4250", -1)]
    [InlineData("1120106810", -1)]
    public void Nivel_por_largo_del_codigo(string codigo, int nivel) => Assert.Equal(nivel, PlanCuentasReglas.Nivel(codigo, Alfa));

    [Fact]
    public void Padre_y_formato_como_el_legacy()
    {
        Assert.Equal("425", PlanCuentasReglas.Padre("42502", Alfa));
        Assert.Equal("42502", PlanCuentasReglas.Padre("425020001", Alfa));
        Assert.Equal(string.Empty, PlanCuentasReglas.Padre("4", Alfa));
        Assert.Equal("4-2-5-02", PlanCuentasReglas.Formatear("42502", Alfa));
        Assert.Equal("1-1-2-01-0681", PlanCuentasReglas.Formatear("112010681", Alfa));
        Assert.Equal("42502", PlanCuentasReglas.Normalizar(" 4-2-5-02 "));
    }

    [Fact]
    public void Formato_valida_largo_numerico_y_niveles()
    {
        Assert.Null(PlanCuentasReglas.ErrorFormato("42502", Alfa));
        Assert.NotNull(PlanCuentasReglas.ErrorFormato("", Alfa));
        Assert.Contains("dígitos", PlanCuentasReglas.ErrorFormato("4250", Alfa));
        Assert.Contains("alfanuméricas", PlanCuentasReglas.ErrorFormato("42A02", Alfa));
        var alfanumerica = new PlanCuentasEstructura { Digitos = [1, 1, 1, 2, 4, 0], CuentasAlfanumericas = true };
        Assert.Null(PlanCuentasReglas.ErrorFormato("42A02", alfanumerica));
        Assert.Contains("15", PlanCuentasReglas.ErrorFormato("1234567890123456", alfanumerica));
    }

    [Fact]
    public void Imputable_requiere_nivel_superior_titulo_y_no_en_capitulo_ni_subcapitulo()
    {
        Assert.Contains("no puede ser imputable", PlanCuentasReglas.ErrorImputable("4", Alfa, false, false));
        Assert.Contains("no puede ser imputable", PlanCuentasReglas.ErrorImputable("42", Alfa, true, true));
        Assert.Null(PlanCuentasReglas.ErrorImputable("425", Alfa, true, true));
        Assert.Contains("No existe un nivel superior (425)", PlanCuentasReglas.ErrorImputable("42502", Alfa, false, false));
        Assert.Contains("debe ser una cuenta de título", PlanCuentasReglas.ErrorImputable("42502", Alfa, true, false));
        Assert.Null(PlanCuentasReglas.ErrorImputable("42502", Alfa, true, true));
    }

    [Fact]
    public void Titulo_no_en_el_ultimo_nivel_y_con_nivel_superior_titulo()
    {
        Assert.Null(PlanCuentasReglas.ErrorTitulo("4", Alfa, false, false));
        Assert.Null(PlanCuentasReglas.ErrorTitulo("42502", Alfa, true, true));
        Assert.Contains("no permite que sea cuenta título", PlanCuentasReglas.ErrorTitulo("425020001", Alfa, true, true));
        Assert.Contains("No existe", PlanCuentasReglas.ErrorTitulo("425", Alfa, false, false));
    }

    [Fact]
    public void Digito_verificador_igual_que_CalculoDV()
    {
        // 4*9 + 2*7 + 5*5 + 0*3 + 2*1 = 77
        Assert.Equal(77, PlanCuentasReglas.DigitoVerificador("42502"));
        // 1*9+1*7+2*5+0*3+1*1+0*9+6*7+8*5+1*3 = 112 → 11
        Assert.Equal(11, PlanCuentasReglas.DigitoVerificador("112010681"));
    }

    [Fact]
    public void Siguiente_codigo_libre_debajo_de_la_madre()
    {
        var existentes = new HashSet<string> { "42501", "42502" };
        Assert.Equal("42503", PlanCuentasReglas.SiguienteCodigo("425", Alfa, existentes));
        Assert.Equal("425020001", PlanCuentasReglas.SiguienteCodigo("42502", Alfa, existentes));
        Assert.Equal("41", PlanCuentasReglas.SiguienteCodigo("4", Alfa, new HashSet<string>()));
        Assert.Null(PlanCuentasReglas.SiguienteCodigo("425020001", Alfa, existentes));
        var lleno = Enumerable.Range(1, 9).Select(i => "4" + i).ToHashSet();
        Assert.Null(PlanCuentasReglas.SiguienteCodigo("4", Alfa, lleno));
    }

    [Fact]
    public void Medio_de_pago_y_tipo_vista_como_el_legacy()
    {
        Assert.Equal("SC", PlanCuentasReglas.MedioPagoDesdeBase("TJ", "11105", "11105", ""));
        Assert.Equal("TJ", PlanCuentasReglas.MedioPagoDesdeBase("TJ", "11105", "", ""));
        Assert.Equal("SU", PlanCuentasReglas.MedioPagoDesdeBase("RO", "21305", "", "SU"));
        Assert.Equal("", PlanCuentasReglas.MedioPagoDesdeBase(null, "21305", "", ""));
        Assert.Equal("RO", PlanCuentasReglas.MediosPago.Single(m => m.Clave == "SU").MedioDePago);
        var cfg = new Dictionary<string, string> { ["TITULOCLIENTES"] = "11201", ["TITULOPROVEEDORES"] = "21101", ["TITULOBANCOS"] = "11102" };
        Assert.Equal("CL", PlanCuentasReglas.TipoVistaPorTitulos("112010001", cfg));
        Assert.Equal("BC", PlanCuentasReglas.TipoVistaPorTitulos("111020003", cfg));
        Assert.Equal("", PlanCuentasReglas.TipoVistaPorTitulos("42502", cfg));
    }

    [Fact]
    public void Estructura_desde_la_configuracion()
    {
        var e = PlanCuentasReglas.EstructuraDesde(new Dictionary<string, string>
        {
            ["DIGITOS_CAPITULO"] = "1", ["DIGITOS_SUBCAPITULO"] = "1", ["DIGITOS_RUBRO"] = "1", ["DIGITOS_SUBRUBRO"] = "2",
            ["DIGITOS_CUENTA"] = "4", ["DIGITOS_SUBCUENTA"] = "", ["NIVEL_1"] = "CAPITULO", ["CUENTASALFANUMERICAS"] = "NO", ["PLANCUENTAS_CLYPR"] = "SI"
        });
        Assert.Equal(new[] { 1, 2, 3, 5, 9, 9 }, e.Largos);
        Assert.Equal("CAPITULO", PlanCuentasReglas.NombreNivel(0, e));
        Assert.Equal("Nivel 2", PlanCuentasReglas.NombreNivel(1, e));
        Assert.False(e.CuentasAlfanumericas);
        Assert.True(e.MostrarClientesProveedores);
    }

    private static readonly string V = new('0', 64);

    [Fact]
    public void Configuracion_contable_valida_tipos_y_solo_lectura()
    {
        ConfiguracionContableService.ValidarCambios([new("USACCOSTO", "SI", V), new("DIGITOS_CUENTA", "4", V), new("FECHAOPERDESDE", "01/05/2026", V)]);
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionContableService.ValidarCambios([new("USACCOSTO", "TAL VEZ", V)]));
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionContableService.ValidarCambios([new("DIGITOS_CUENTA", "12", V)]));
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionContableService.ValidarCambios([new("FECHAOPERDESDE", "2026-05-01", V)]));
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionContableService.ValidarCambios([new("PERIODO", "8", V)]));
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionContableService.ValidarCambios([new("CLAVE_INEXISTENTE", "SI", V)]));
    }

    [Fact]
    public void Configuracion_contable_valida_el_conjunto()
    {
        Dictionary<string, string> Base() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["DIGITOS_CAPITULO"] = "1", ["DIGITOS_SUBCAPITULO"] = "1", ["DIGITOS_RUBRO"] = "1", ["DIGITOS_SUBRUBRO"] = "2", ["DIGITOS_CUENTA"] = "4",
            ["DIGITOS_SUBCUENTA"] = "0", ["CC_digitos_1"] = "1", ["CC_digitos_2"] = "1", ["CC_digitos_3"] = "2", ["CC_digitos_4"] = "0",
            ["FECHAOPERDESDE"] = "01/05/2025", ["FECHAOPERHASTA"] = "30/04/2027"
        };
        ConfiguracionContableService.ValidarConjunto(Base());
        var sinCapitulo = Base(); sinCapitulo["DIGITOS_CAPITULO"] = "0";
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionContableService.ValidarConjunto(sinCapitulo));
        var largo = Base(); largo["DIGITOS_SUBCUENTA"] = "7";
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionContableService.ValidarConjunto(largo));
        var centros = Base(); centros["CC_digitos_4"] = "1";
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionContableService.ValidarConjunto(centros));
        var fechas = Base(); fechas["FECHAOPERDESDE"] = "01/05/2028";
        Assert.Throws<CargaComprobantesReglaException>(() => ConfiguracionContableService.ValidarConjunto(fechas));
    }
}

using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Reglas del plan de cuentas copiadas del legacy (MA_CUENTAS.vbp, Cn020.frm, y Cndll.cls: CuentaValida,
/// ValidaImputable, ValidaTitulo, CalculoDV, IdentificaCuenta). Sin acceso a datos para poder testearlas.
/// </summary>
public static class PlanCuentasReglas
{
    public const int LargoMaximoCodigo = 15;
    public const int LargoMaximoDescripcion = 50;

    public static IReadOnlyList<string> ClavesDigitos { get; } =
        ["DIGITOS_CAPITULO", "DIGITOS_SUBCAPITULO", "DIGITOS_RUBRO", "DIGITOS_SUBRUBRO", "DIGITOS_CUENTA", "DIGITOS_SUBCUENTA"];

    public static IReadOnlyList<string> ClavesNiveles { get; } = ["NIVEL_1", "NIVEL_2", "NIVEL_3", "NIVEL_4", "NIVEL_5", "NIVEL_6"];

    public static IReadOnlyList<string> ClavesEstructura { get; } =
        [.. ClavesDigitos, .. ClavesNiveles, "CUENTASALFANUMERICAS", "USADIGITOVERIFICADOR", "PLANCUENTAS_CLYPR"];

    /// <summary>Estructura según TA_CONFIGURACION; un valor vacío o no numérico vale 0, como Val() del legacy.</summary>
    public static PlanCuentasEstructura EstructuraDesde(IReadOnlyDictionary<string, string> cfg) => new()
    {
        Digitos = ClavesDigitos.Select(k => int.TryParse(ConfiguracionSql.Valor(cfg, k), out var n) && n > 0 ? n : 0).ToList(),
        Niveles = ClavesNiveles.Select(k => ConfiguracionSql.Valor(cfg, k)).ToList(),
        CuentasAlfanumericas = ConfiguracionSql.EsSi(cfg, "CUENTASALFANUMERICAS"),
        UsaDigitoVerificador = ConfiguracionSql.EsSi(cfg, "USADIGITOVERIFICADOR"),
        MostrarClientesProveedores = ConfiguracionSql.EsSi(cfg, "PLANCUENTAS_CLYPR")
    };

    /// <summary>Medios de pago del combo «Tipo de Cuenta», en el orden del legacy.</summary>
    public static IReadOnlyList<PlanCuentaMedioPago> MediosPago { get; } =
    [
        new("", "Ninguno", null),
        new("EF", "Efectivo", "EF"),
        new("CH", "Cheque", "CH"),
        new("TJ", "Tarjetas de crédito", "TJ"),
        new("DB", "Débitos/Créditos bancarios", "DB"),
        new("DC", "Documentos varios", "DC"),
        new("RI", "Retenciones/Percepciones IVA", "RI"),
        new("RB", "Retención/Percepción Ingresos Brutos", "RB"),
        new("RG", "Retención/Percepción Ganancias", "RG"),
        new("RO", "Retención/Percepción varias", "RO"),
        // SUSS y Solicitud de crédito graban RO y TJ en MA_CUENTAS y se distinguen por TA_CONFIGURACION.
        new("SU", "Retención/Percepción SUSS", "RO"),
        new("SC", "Solicitud de crédito", "TJ")
    ];

    /// <summary>Medios que piden el código de conciliación automática y «No pide datos de la tarjeta».</summary>
    public static bool UsaDatosTarjeta(string medio) => medio is "TJ" or "DB" or "SC";

    public static string ClaveRetencionOtra(string codigo) => "CTA_RET_OTRA_" + codigo;

    /// <summary>Saca guiones y espacios: el legacy muestra 4-2-5-02 y graba 42502.</summary>
    public static string Normalizar(string? codigo) => (codigo ?? string.Empty).Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

    /// <summary>Nivel (0 = capítulo … 5 = sub-cuenta) según el largo del código; -1 si el largo no corresponde a ningún nivel.</summary>
    public static int Nivel(string codigo, PlanCuentasEstructura e)
    {
        var largos = e.Largos;
        for (var i = 0; i < largos.Count; i++)
            if (largos[i] == codigo.Length && largos[i] > 0) return i;
        return -1;
    }

    public static string NombreNivel(int nivel, PlanCuentasEstructura e)
        => nivel >= 0 && nivel < e.Niveles.Count && e.Niveles[nivel].Trim().Length > 0 ? e.Niveles[nivel].Trim() : nivel >= 0 ? $"Nivel {nivel + 1}" : string.Empty;

    /// <summary>Código del nivel superior (vacío para un capítulo).</summary>
    public static string Padre(string codigo, PlanCuentasEstructura e)
    {
        var nivel = Nivel(codigo, e);
        return nivel <= 0 ? string.Empty : codigo[..e.Largos[nivel - 1]];
    }

    /// <summary>Formato de pantalla del legacy (Format_Cuenta): un guión entre niveles, ej. 4-2-5-02.</summary>
    public static string Formatear(string codigo, PlanCuentasEstructura e)
    {
        var partes = new List<string>();
        var desde = 0;
        foreach (var largo in e.Largos)
        {
            if (largo <= desde || desde >= codigo.Length) continue;
            partes.Add(codigo[desde..Math.Min(largo, codigo.Length)]);
            desde = largo;
        }
        if (desde < codigo.Length) partes.Add(codigo[desde..]);
        return string.Join("-", partes);
    }

    /// <summary>CampoValida del código: largo, numérico (salvo CuentasAlfanumericas = SI) y cantidad de dígitos de algún nivel.</summary>
    public static string? ErrorFormato(string codigo, PlanCuentasEstructura e)
    {
        if (codigo.Length == 0) return "Ingresá el código de la cuenta.";
        if (codigo.Length > LargoMaximoCodigo) return $"La longitud de la cuenta no puede superar los {LargoMaximoCodigo} caracteres.";
        if (!e.CuentasAlfanumericas && !codigo.All(char.IsAsciiDigit))
            return "No está permitido ingresar cuentas alfanuméricas en esta configuración.";
        if (e.CuentasAlfanumericas && !codigo.All(char.IsAsciiLetterOrDigit))
            return "El código de la cuenta solo puede tener letras y números.";
        if (Nivel(codigo, e) < 0)
            return $"La cuenta ingresada no es correcta: verificá la cantidad de dígitos ({string.Join(", ", e.Largos.Distinct().Where(x => x > 0))}).";
        return null;
    }

    /// <summary>ValidaImputable: capítulo y sub-capítulo no pueden ser imputables; el nivel superior debe existir y ser título.</summary>
    public static string? ErrorImputable(string codigo, PlanCuentasEstructura e, bool padreExiste, bool padreEsTitulo)
    {
        var nivel = Nivel(codigo, e);
        if (nivel is 0 or 1) return $"Una cuenta {NombreNivel(nivel, e)} no puede ser imputable.";
        var padre = Padre(codigo, e);
        if (!padreExiste) return $"No existe un nivel superior ({padre}) para esta cuenta.";
        if (!padreEsTitulo) return $"El nivel superior ({padre}) debe ser una cuenta de título.";
        return null;
    }

    /// <summary>ValidaTitulo: el último nivel no puede ser título; si hay nivel superior debe existir y ser título.</summary>
    public static string? ErrorTitulo(string codigo, PlanCuentasEstructura e, bool padreExiste, bool padreEsTitulo)
    {
        if (codigo.Length == e.LargoMaximo) return "La cantidad de dígitos ingresados no permite que sea cuenta título.";
        var padre = Padre(codigo, e);
        if (padre.Length == 0) return null;
        if (!padreExiste) return $"No existe un nivel superior ({padre}) para esta cuenta.";
        if (!padreEsTitulo) return $"El nivel superior ({padre}) debe ser una cuenta de título.";
        return null;
    }

    /// <summary>CalculoDV del legacy: coeficientes 9, 7, 5, 3, 1 repetidos y se reduce a dos dígitos.</summary>
    public static int DigitoVerificador(string codigo)
    {
        var dv = 0;
        var coef = 9;
        foreach (var c in codigo)
        {
            if (coef == -1) coef = 9;
            dv += (char.IsAsciiDigit(c) ? c - '0' : 0) * coef;
            coef -= 2;
        }
        while (dv > 99) dv /= 10;
        return dv;
    }

    /// <summary>
    /// IdentificaCuenta: al elegir la cuenta madre propone el primer código libre del nivel siguiente
    /// (1, 01, 0001… según los dígitos del nivel). Null si la madre no tiene nivel siguiente o está completa.
    /// </summary>
    public static string? SiguienteCodigo(string madre, PlanCuentasEstructura e, ISet<string> existentes)
    {
        var nivel = Nivel(madre, e);
        if (nivel < 0) return null;
        var siguiente = -1;
        for (var j = nivel + 1; j < e.Digitos.Count; j++)
            if (e.Digitos[j] > 0) { siguiente = j; break; }
        if (siguiente < 0) return null;
        var ancho = e.Digitos[siguiente];
        var maximo = (int)Math.Min(Math.Pow(10, ancho) - 1, 99999);
        for (var n = 1; n <= maximo; n++)
        {
            var candidato = madre + n.ToString().PadLeft(ancho, '0');
            if (!existentes.Contains(candidato)) return candidato;
        }
        return null;
    }

    /// <summary>
    /// Tipo de vista que el legacy asigna por los títulos configurados (ObtieneVistaRangoVto, cuando no hay rango de
    /// datos adicionales): TituloClientes → CL, TituloProveedores → PR, TituloBancos / TituloBancosDiferido → BC,
    /// TITULOPERSONAL → PE, TituloClientesBonos → CB, TituloClientesMutuales → CM.
    /// </summary>
    public static string TipoVistaPorTitulos(string codigo, IReadOnlyDictionary<string, string> cfg)
    {
        foreach (var (clave, vista) in new[]
                 {
                     ("TITULOCLIENTES", "CL"), ("TITULOPROVEEDORES", "PR"), ("TITULOBANCOS", "BC"), ("TITULOBANCOSDIFERIDO", "BC"),
                     ("TITULOPERSONAL", "PE"), ("TITULOCLIENTESBONOS", "CB"), ("TITULOCLIENTESMUTUALES", "CM")
                 })
        {
            if (cfg.TryGetValue(clave, out var titulo) && titulo.Trim().Length > 0 && codigo.StartsWith(titulo.Trim(), StringComparison.OrdinalIgnoreCase))
                return vista;
        }
        return string.Empty;
    }

    /// <summary>Clave del combo para lo grabado: TJ que es la cuenta de solicitud de crédito → SC; RO marcada SUSS → SU.</summary>
    public static string MedioPagoDesdeBase(string? medioDePago, string codigo, string ctaSolicitudCredito, string retencionOtra)
    {
        var mp = (medioDePago ?? string.Empty).Trim().ToUpperInvariant();
        if (mp == "TJ" && ctaSolicitudCredito.Trim().Length > 0 && ctaSolicitudCredito.Trim() == codigo) return "SC";
        if (mp == "RO" && retencionOtra.Trim().Equals("SU", StringComparison.OrdinalIgnoreCase)) return "SU";
        return MediosPago.Any(m => m.Clave == mp) ? mp : string.Empty;
    }
}

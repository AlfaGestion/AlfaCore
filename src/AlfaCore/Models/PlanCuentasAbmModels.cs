namespace AlfaCore.Models;

/// <summary>
/// Estructura de codificación del plan de cuentas (Configuración → Gestión contable → Plan General de Cuentas),
/// igual que el legacy: dígitos por nivel DIGITOS_CAPITULO..DIGITOS_SUBCUENTA y nombres NIVEL_1..NIVEL_6.
/// </summary>
public sealed class PlanCuentasEstructura
{
    /// <summary>Dígitos de cada uno de los 6 niveles (capítulo, sub-capítulo, rubro, sub-rubro, cuenta, sub-cuenta).</summary>
    public IReadOnlyList<int> Digitos { get; init; } = [1, 1, 1, 2, 4, 0];
    public IReadOnlyList<string> Niveles { get; init; } = ["CAPITULO", "SUB-CAPITULO", "RUBRO", "SUB-RUBRO", "CUENTA", "SUB-CUENTA"];
    public bool CuentasAlfanumericas { get; init; }
    public bool UsaDigitoVerificador { get; init; }
    /// <summary>PlanCuentas_CLyPR: la lista de cuentas madre incluye los títulos de clientes y proveedores.</summary>
    public bool MostrarClientesProveedores { get; init; }

    /// <summary>Largo acumulado del código en cada nivel (TD_Capitulo..TD_SubCuenta del legacy).</summary>
    public IReadOnlyList<int> Largos
    {
        get
        {
            var r = new int[Digitos.Count];
            for (var i = 0; i < Digitos.Count; i++) r[i] = (i == 0 ? 0 : r[i - 1]) + Math.Max(0, Digitos[i]);
            return r;
        }
    }

    public int LargoMaximo => Largos.Count == 0 ? 0 : Largos[^1];
}

/// <summary>Medio de pago de la cuenta (combo «Tipo de Cuenta» del legacy).</summary>
public sealed record PlanCuentaMedioPago(string Clave, string Nombre, string? MedioDePago);

public sealed record PlanCuentaOpcion(string Codigo, string Descripcion);

public sealed class PlanCuentasAbmCatalogo
{
    public PlanCuentasEstructura Estructura { get; init; } = new();
    public IReadOnlyList<PlanCuentaOpcion> Titulos { get; init; } = [];
    public IReadOnlyList<PlanCuentaOpcion> Monedas { get; init; } = [];
    public string MonedaDefault { get; init; } = string.Empty;
    public bool MultiMoneda { get; init; }
    public IReadOnlyList<string> Indices { get; init; } = [];
    public bool UsaBienesDeUso { get; init; }
    public bool UsaCentrosCosto { get; init; }
    /// <summary>Hay centros de costo cargados (TA_CCOSTO): sin ellos no se puede marcar «Usa centros de costos».</summary>
    public bool HayCentrosCosto { get; init; }
    public bool PuedeModificar { get; init; }
    public IReadOnlyList<string> CamposPersonalizados { get; init; } = [];
}

public sealed class PlanCuentaCampo
{
    public string Nombre { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
}

/// <summary>Una cuenta del plan para dar de alta o modificar (pantalla «Actualización Plan General de Cuentas»).</summary>
public sealed class PlanCuentaEdicion
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Titulo { get; set; }
    public bool Ajuste { get; set; }
    /// <summary>Índice de ajuste 1 a 4 (nombres Nombre_Indice1..4).</summary>
    public int Indice { get; set; } = 1;
    public bool Bloqueo { get; set; }
    public bool Bienes { get; set; }
    public bool CajaYBanco { get; set; }
    /// <summary>Clave de <see cref="PlanCuentaMedioPago"/> ("" = Ninguno).</summary>
    public string MedioPago { get; set; } = string.Empty;
    public bool NoPideDatosTarjeta { get; set; }
    public string CodTarjetaConcAuto { get; set; } = string.Empty;
    public string Moneda { get; set; } = string.Empty;
    public string CodigoOpcional { get; set; } = string.Empty;
    public string CuentaPrincipal { get; set; } = string.Empty;
    public bool PideVencimiento { get; set; }
    public bool UsaCCostos { get; set; }
    public List<PlanCuentaCampo> Personalizados { get; set; } = [];

    // Solo lectura (los informa el servicio).
    public bool Existe { get; set; }
    public bool DadaDeBaja { get; set; }
    public string TipoVista { get; set; } = string.Empty;
    public string CuentaPrincipalDescripcion { get; set; } = string.Empty;
    public string CuentaMadre { get; set; } = string.Empty;
    public string CuentaMadreDescripcion { get; set; } = string.Empty;
    public string Nivel { get; set; } = string.Empty;
    public int DigitoVerificador { get; set; }
    public bool TieneNivelInferior { get; set; }
    public bool TieneMovimientos { get; set; }
    /// <summary>Al dar de alta un código que existe dado de baja: confirmación de reutilizarlo.</summary>
    public bool ReutilizarBaja { get; set; }
}

public sealed class PlanCuentaBajaInfo
{
    public string Codigo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    /// <summary>Motivo por el que no se puede borrar (vacío = se puede).</summary>
    public string Bloqueo { get; init; } = string.Empty;
    /// <summary>Tiene movimientos contables: en lugar de borrarla se marca como dada de baja.</summary>
    public bool SeMarcaDeBaja { get; init; }
    public int RenglonesCashFlow { get; init; }
}

public enum PlanCuentaBajaResultado
{
    Borrada,
    MarcadaDeBaja
}

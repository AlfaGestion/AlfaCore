namespace AlfaCore.Models;

/// <summary>
/// Unidad de negocio tal como la edita Tablas.frm del VB6: columnas de V_TA_UnidadNegocio más los datos
/// fiscales que el VB6 guarda en TA_CONFIGURACION con sufijo _UN_{codigo}.
/// </summary>
public sealed class UnidadNegocioEdicion
{
    /// <summary>Código sin espacios (en la base se guarda alineado a la derecha, ej. "   1").</summary>
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Deposito { get; set; } = string.Empty;
    public string DepositoDiferencias { get; set; } = string.Empty;
    public string MotivoDiferencias { get; set; } = string.Empty;
    public bool ModificaDiferencias { get; set; }
    public string CuentaAsociada { get; set; } = string.Empty;
    public string Grupo { get; set; } = string.Empty;

    // Facturación electrónica / datos fiscales.
    public bool UsaFacturacionElectronica { get; set; }
    public string Cuit { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string PuntoVenta { get; set; } = string.Empty;
    public string RutaCrt { get; set; } = string.Empty;
    public string RutaKey { get; set; } = string.Empty;
    public string RutaReportes { get; set; } = string.Empty;

    // TA_CONFIGURACION *_UN_{codigo}.
    public string Domicilio { get; set; } = string.Empty;
    public string CondicionIva { get; set; } = string.Empty;
    public string InicioActividades { get; set; } = string.Empty;
    public string IngresosBrutos { get; set; } = string.Empty;
    public string FormaDePago { get; set; } = string.Empty;
    public bool AgenteRetencionPercepcionIibb { get; set; }
    public bool AgenteRetencionGanancias { get; set; }

    public UnidadNegocioEdicion Clonar() => (UnidadNegocioEdicion)MemberwiseClone();
}

public sealed record UnidadNegocioFila(string Codigo, string Descripcion, bool UsaFacturacionElectronica, string Cuit, string PuntoVenta, string CondicionIva);

public sealed record UnidadNegocioOpcion(string Codigo, string Descripcion);

/// <summary>Listas para los combos del editor (depósitos, motivos de stock, condiciones de IVA, grupos).</summary>
public sealed class UnidadNegocioCatalogos
{
    public IReadOnlyList<UnidadNegocioOpcion> Depositos { get; init; } = [];
    public IReadOnlyList<UnidadNegocioOpcion> MotivosStock { get; init; } = [];
    public IReadOnlyList<UnidadNegocioOpcion> CondicionesIva { get; init; } = [];
    public IReadOnlyList<string> Grupos { get; init; } = [];
}

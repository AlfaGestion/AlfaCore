namespace AlfaCore.Models;

/// <summary>Un comprobante del archivo «Mis Comprobantes» de ARCA (emitidos o recibidos).</summary>
public sealed class ArcaMisComprobante
{
    public DateTime Fecha { get; set; }
    public int CodigoTipo { get; set; }
    public string TipoTexto { get; set; } = string.Empty;
    /// <summary>FC, NC, ND o RC.</summary>
    public string Clase { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public long NumeroDesde { get; set; }
    public long NumeroHasta { get; set; }
    public string Cae { get; set; } = string.Empty;
    public string DocContraparte { get; set; } = string.Empty;
    public string NombreContraparte { get; set; } = string.Empty;
    public string Moneda { get; set; } = "$";
    public decimal TipoCambio { get; set; } = 1;
    public decimal NetoGravado { get; set; }
    public decimal NoGravado { get; set; }
    public decimal Exento { get; set; }
    public decimal OtrosTributos { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    /// <summary>Neto e IVA por alícuota (columnas «Neto Grav. IVA x%» / «IVA x%»); vacío en el formato anterior.</summary>
    public List<ArcaAlicuota> Alicuotas { get; set; } = [];
    /// <summary>Total en pesos (por el tipo de cambio si es moneda extranjera).</summary>
    public decimal TotalPesos => Moneda is "$" or "PES" or "" ? Total : Math.Round(Total * TipoCambio, 2);
}

public sealed record ArcaAlicuota(decimal Tasa, decimal Neto, decimal Iva);

public sealed class ArcaMisComprobantesArchivo
{
    /// <summary>true = Recibidos (compras); false = Emitidos (ventas).</summary>
    public bool Recibidos { get; set; }
    public string Cuit { get; set; } = string.Empty;
    public List<ArcaMisComprobante> Comprobantes { get; set; } = [];
}

public enum ControlArcaEstado { Coincide, DifiereImporte, DifiereFecha, OtroPeriodo, FaltaEnSistema, FaltaEnArca, OtraUnidad, PosibleDuplicado }

public sealed class ControlArcaFila
{
    public ControlArcaEstado Estado { get; set; }
    public string Clase { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public int PuntoVenta { get; set; }
    public long Numero { get; set; }
    public long NumeroHasta { get; set; }
    public string Cuit { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public DateTime? FechaArca { get; set; }
    public DateTime? FechaSistema { get; set; }
    public decimal? TotalArca { get; set; }
    public decimal? TotalSistema { get; set; }
    public decimal Diferencia => (TotalArca ?? 0) - (TotalSistema ?? 0);
    public string Cae { get; set; } = string.Empty;
    /// <summary>Comprobante en el sistema (TC + número) cuando existe.</summary>
    public string ComprobanteSistema { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;
    /// <summary>Datos completos de ARCA (para importar los que faltan).</summary>
    public ArcaMisComprobante? Arca { get; set; }
    public string Comprobante => NumeroHasta > Numero ? $"{PuntoVenta:00000}-{Numero:00000000} a {NumeroHasta:00000000}" : $"{PuntoVenta:00000}-{Numero:00000000}";
}

public sealed class ControlArcaResultado
{
    public bool Recibidos { get; set; }
    public string CuitArchivo { get; set; } = string.Empty;
    public string Emisor { get; set; } = string.Empty;
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public IReadOnlyList<string> Unidades { get; set; } = [];
    /// <summary>Aviso cuando el CUIT del archivo no es el de ninguna unidad de negocio.</summary>
    public string? Aviso { get; set; }
    public IReadOnlyList<ControlArcaFila> Filas { get; set; } = [];
    public int ComprobantesArca { get; set; }
    public int ComprobantesSistema { get; set; }
    public decimal TotalArca { get; set; }
    public decimal TotalSistema { get; set; }
    public int Cantidad(ControlArcaEstado e) => Filas.Count(f => f.Estado == e);
}

// ── Importación de faltantes (recibidos → compras) ─────────────────────────────────────────────

public sealed class ControlArcaImportacionContexto
{
    public string CuentaCompras { get; set; } = string.Empty;
    public string CuentaIva { get; set; } = string.Empty;
    public string CuentaOtrosTributos { get; set; } = string.Empty;
    /// <summary>true = importa emitidos a ventas (clientes); false = recibidos a compras (proveedores).</summary>
    public bool Ventas { get; set; }
    /// <summary>Cuenta para los que no existen: proveedores varios, o en ventas la de consumidor final.</summary>
    public string CuentaProveedoresVarios { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public IReadOnlyList<CompraCargaOpcion> Cuentas { get; set; } = [];
    public IReadOnlyList<CompraCargaOpcion> Unidades { get; set; } = [];
    public IReadOnlyList<CompraCargaOpcion> CondicionesCompra { get; set; } = [];
    public string CondicionCompra { get; set; } = string.Empty;
    public bool PuedeCrearProveedores { get; set; }
    public DateTime? CierreCompras { get; set; }
    public IReadOnlyList<ControlArcaImportacionFila> Filas { get; set; } = [];
}

public enum ControlArcaProveedorModo { Alta, Varios, Omitir }

public sealed class ControlArcaImportacionFila
{
    public string Clave { get; set; } = string.Empty;
    public string Comprobante { get; set; } = string.Empty;
    public string Cuit { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public decimal Total { get; set; }
    /// <summary>Proveedor encontrado por CUIT (vacío = no existe).</summary>
    public string Proveedor { get; set; } = string.Empty;
    public string ProveedorNombre { get; set; } = string.Empty;
    /// <summary>Motivo por el que no se puede importar (vacío = se puede).</summary>
    public string Bloqueo { get; set; } = string.Empty;
    /// <summary>Resultado después de importar.</summary>
    public string Resultado { get; set; } = string.Empty;
    public bool Importado { get; set; }
}

public sealed class ControlArcaImportacionSolicitud
{
    public List<ArcaMisComprobante> Comprobantes { get; set; } = [];
    public string CuentaCompras { get; set; } = string.Empty;
    public string CuentaIva { get; set; } = string.Empty;
    public string CuentaOtrosTributos { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public bool Ventas { get; set; }
    public ControlArcaProveedorModo ProveedoresNuevos { get; set; } = ControlArcaProveedorModo.Alta;
    public string CondicionCompra { get; set; } = string.Empty;
    /// <summary>CUIT del archivo de ARCA: la unidad de negocio elegida tiene que ser una de ese CUIT.</summary>
    public string CuitArchivo { get; set; } = string.Empty;
    /// <summary>Nombre del archivo de ARCA que se está importando (queda en MV_Transmisiones.Ubicacion_Archivo).</summary>
    public string Archivo { get; set; } = string.Empty;
}

/// <summary>Una corrida registrada en MV_Transmisiones (importación o control de ARCA).</summary>
public sealed record ControlArcaTransmision(int Numero, DateTime Fecha, string Hora, string Usuario, string Observaciones, string Archivo,
    int Enviados, int Recibidos, string Log);

/// <summary>Un comprobante de una corrida (AUX_MV_CTROL_CPTE).</summary>
public sealed record ControlArcaTransmisionDetalle(string Comprobante, string Cuenta, string Nombre, DateTime? Fecha, decimal Importe, string Mensaje);

public sealed record ControlArcaAsientoLinea(string Cuenta, string DebeHaber, decimal Importe);

// ── Descarga desde ARCA (script propio de AlfaCore, configurado en Administrar → Servidor) ─────────────────────────────────────

public sealed class ArcaDescargaConfig
{
    public string CuitLogin { get; set; } = string.Empty;
    /// <summary>Solo para grabar: nunca se devuelve la clave guardada.</summary>
    public string? ClaveNueva { get; set; }
    public bool TieneClave { get; set; }
    public string CuitRepresentado { get; set; } = string.Empty;
    /// <summary>Carpeta efectiva de esta base en el servidor (solo lectura: la define Administrar → Servidor).</summary>
    public string Carpeta { get; set; } = string.Empty;
    /// <summary>Descarga y control automático diario (AlfaCore).</summary>
    public bool AutoRecibidos { get; set; }
    public bool AutoEmitidos { get; set; }
    public string HoraAuto { get; set; } = "07:00";
    // Estado en este servidor (solo lectura).
    public bool ScriptExiste { get; set; }
    public bool PythonExiste { get; set; }
    public string UltimaAutoRecibidos { get; set; } = string.Empty;
    public string UltimaAutoEmitidos { get; set; } = string.Empty;
    /// <summary>El servidor tiene el script y python, y la base tiene sus datos de acceso a ARCA.</summary>
    public bool Configurado => CuitLogin.Length > 0 && TieneClave && CuitRepresentado.Length > 0 && ScriptExiste && PythonExiste;
}

public sealed record ArcaArchivoDescargado(string Nombre, bool Recibidos, string Periodo, DateTime Fecha, long Bytes);

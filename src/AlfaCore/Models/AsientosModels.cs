namespace AlfaCore.Models;

public sealed class AsientosFiltro
{
    public DateTime Desde { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime Hasta { get; set; } = DateTime.Today;
    /// <summary>Número («10-25» o «25»), comprobante, detalle, cuenta o usuario.</summary>
    public string Texto { get; set; } = string.Empty;
    /// <summary>MANUALES (editables), COMPROBANTES (de compras, ventas, cobranzas, pagos…) o TODOS.</summary>
    public string Origen { get; set; } = "MANUALES";
    public string Cuenta { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
}

/// <summary>Clave de un asiento: PERIODO + MES_OPERATIVO + [NUMERO ASIENTO] + TIPO_REG.</summary>
public sealed record AsientoClave(string Periodo, int Mes, int Numero, string TipoReg)
{
    /// <summary>Como lo muestra el legacy: MM-0000000.</summary>
    public string Texto => $"{Mes:00}-{Numero:0000000}";
    public string Id => $"{Periodo.Trim()}|{Mes}|{Numero}|{TipoReg.Trim()}";

    public static AsientoClave? Parse(string? id)
    {
        var p = (id ?? "").Split('|');
        return p.Length == 4 && int.TryParse(p[1], out var m) && int.TryParse(p[2], out var n) && p[0].Trim().Length > 0 ? new(p[0].Trim(), m, n, p[3]) : null;
    }
}

public sealed class AsientoResumen
{
    public string Periodo { get; set; } = string.Empty;
    public int Mes { get; set; }
    public int Numero { get; set; }
    public string TipoReg { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Tc { get; set; } = string.Empty;
    public string Comprobante { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Renglones { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public string LivaTipo { get; set; } = string.Empty;
    public bool Manual { get; set; }
    public AsientoClave Clave => new(Periodo, Mes, Numero, TipoReg);
}

public sealed class AsientoRenglon
{
    public string Cuenta { get; set; } = string.Empty;
    public string CuentaNombre { get; set; } = string.Empty;
    /// <summary>Concepto del renglón (opcional); vacío = el detalle del asiento.</summary>
    public string Detalle { get; set; } = string.Empty;
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
}

public sealed class AsientoDetalle
{
    public AsientoClave? Clave { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Tc { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Letra { get; set; } = "X";
    public string Detalle { get; set; } = string.Empty;
    public DateTime? Vencimiento { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTime? Grabado { get; set; }
    public DateTime? Modificado { get; set; }
    public List<AsientoRenglon> Renglones { get; set; } = [];
    public bool Editable { get; set; } = true;
    /// <summary>Por qué no se puede modificar desde acá (comprobante de compras, ventas, cobranzas, conciliado…).</summary>
    public string MotivoSoloLectura { get; set; } = string.Empty;
    /// <summary>Datos del comprobante cuando el asiento viene de una factura (libro de IVA).</summary>
    public string LivaTipo { get; set; } = string.Empty;
    public string CuentaComprobante { get; set; } = string.Empty;
    public string NombreComprobante { get; set; } = string.Empty;
    public string CuitComprobante { get; set; } = string.Empty;
    public decimal TotalComprobante { get; set; }
    public string Cae { get; set; } = string.Empty;
    public string Comprobante => $"{Tc} {Sucursal}-{Numero} {Letra}".Trim();
    /// <summary>Huella de lo leído: al grabar o anular se verifica que nadie lo haya cambiado mientras tanto.</summary>
    public string Huella { get; set; } = string.Empty;
    public decimal TotalDebe => Renglones.Sum(r => r.Debe);
    public decimal TotalHaber => Renglones.Sum(r => r.Haber);
}

public sealed record AsientoCuenta(string Codigo, string Descripcion, string MedioDePago, bool PideVencimiento);

public sealed record AsientoTalonario(string Codigo, string Descripcion, string Letras, int Sucursal, int Proximo, string Unidad);

public sealed class AsientosCatalogo
{
    public IReadOnlyList<AsientoCuenta> Cuentas { get; set; } = [];
    public IReadOnlyList<AsientoTalonario> Talonarios { get; set; } = [];
    public IReadOnlyList<CompraCargaOpcion> Unidades { get; set; } = [];
    public string UnidadUsuario { get; set; } = string.Empty;
    public bool PuedeCargar { get; set; }
    public bool PuedeModificar { get; set; }
    public bool PuedeAnular { get; set; }
}

/// <summary>Cruce del asiento de una factura con Mis Comprobantes de ARCA (último archivo bajado del mes).</summary>
public sealed class AsientoControlArca
{
    /// <summary>COINCIDE, DIFIERE, NO_INFORMADO, SIN_ARCHIVO o NO_APLICA.</summary>
    public string Estado { get; set; } = "NO_APLICA";
    public string Mensaje { get; set; } = string.Empty;
    public string Archivo { get; set; } = string.Empty;
    public decimal? TotalArca { get; set; }
    public string CaeArca { get; set; } = string.Empty;
    public DateTime? FechaArca { get; set; }
    public IReadOnlyList<string> Observaciones { get; set; } = [];
}

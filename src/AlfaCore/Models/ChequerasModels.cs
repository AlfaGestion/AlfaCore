namespace AlfaCore.Models;

/// <summary>Chequera de cheques propios (V_TA_CTRL_CHEQUERAS del legacy).</summary>
public sealed class Chequera
{
    public int Id { get; set; }
    public string IdBanco { get; set; } = string.Empty;
    public string Banco { get; set; } = string.Empty;
    public string Serie { get; set; } = string.Empty;
    /// <summary>«Nro de control» del legacy: distingue chequeras del mismo banco y serie.</summary>
    public int Talonario { get; set; }
    public long Desde { get; set; }
    public long Hasta { get; set; }
    /// <summary>Cuenta del banco para cheques comunes (MEDIODEPAGO DB).</summary>
    public string CuentaBanco { get; set; } = string.Empty;
    /// <summary>Cuenta de cheques de pago diferido (MEDIODEPAGO CH).</summary>
    public string CuentaDiferido { get; set; } = string.Empty;
    /// <summary>Cheques de la chequera ya emitidos (salidas registradas en las cuentas de la chequera dentro del rango).</summary>
    public int Emitidos { get; set; }
    public long? UltimoEmitido { get; set; }
}

public sealed record ChequeraBanco(string IdBanco, string Descripcion);

/// <summary>Banco de la tabla V_TA_Bancos (código de hasta 4 caracteres, como los usa el legacy en cheques y chequeras).</summary>
public sealed class BancoTabla
{
    public string IdBanco { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    /// <summary>Cheques registrados con este banco (MV_ASIENTOS).</summary>
    public int Cheques { get; set; }
    public int Chequeras { get; set; }
    public int Legajos { get; set; }
    public bool EnUso => Cheques + Chequeras + Legajos > 0;
}

public sealed class ChequerasCatalogo
{
    public IReadOnlyList<ChequeraBanco> Bancos { get; set; } = [];
    public IReadOnlyList<AsientoCuenta> Cuentas { get; set; } = [];
    public bool PuedeModificar { get; set; }
}

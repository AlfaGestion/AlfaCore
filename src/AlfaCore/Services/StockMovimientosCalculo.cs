namespace AlfaCore.Services;

/// <summary>
/// Reglas puras de Movimientos de stock (V_MV_STOCK del legacy, Mvstock.frm + ClsCmnCptes.ActualizaStock).
/// IR/VS mueven la cantidad con el signo del comprobante (ES), TR graba la salida del origen y la entrada al
/// destino en el mismo comprobante, y AJP/SI dejan el saldo del depósito igual a la cantidad contada a la
/// fecha del movimiento (graban la diferencia). Sin acceso a datos: se prueba con tests unitarios.
/// </summary>
public static class StockMovimientosCalculo
{
    /// <summary>Comprobantes de la pantalla, en el orden del combo.</summary>
    public static readonly IReadOnlyList<string> Tipos = ["IR", "VS", "TR", "AJP", "SI"];

    public static bool EsAjuste(string tc) => tc is "AJP" or "SI";
    public static bool EsTransferencia(string tc) => tc == "TR";

    /// <summary>Signo del movimiento según ES de V_TA_CPTE; si falta, el fijo del legacy (ObtieneSignoComprobante).</summary>
    public static int Signo(string tc, string? es)
    {
        if (EsAjuste(tc)) return 1; // la diferencia ya trae el signo
        return (es ?? "").Trim().ToUpperInvariant() switch
        {
            "E" => 1,
            "S" => -1,
            _ => tc is "VS" or "TR" ? -1 : 1
        };
    }

    /// <summary>AJP/SI: lo que hay que grabar para que el saldo a la fecha quede igual a lo contado.</summary>
    public static decimal Diferencia(decimal contado, decimal saldo) => decimal.Round(contado - saldo, 4, MidpointRounding.ToEven);

    /// <summary>Cantidad a mover en el depósito (origen en TR) con su signo.</summary>
    public static decimal Movimiento(string tc, string? es, decimal cantidad, decimal saldo)
        => EsAjuste(tc) ? Diferencia(cantidad, saldo) : Signo(tc, es) * cantidad;

    /// <summary>Valida la cantidad cargada. En ajustes es la contada y puede ser cero; en el resto debe ser positiva.</summary>
    public static string? ValidarCantidad(string tc, decimal cantidad)
    {
        if (cantidad > 9999999m) return "La cantidad supera el máximo permitido.";
        if (decimal.Round(cantidad, 4) != cantidad) return "La cantidad admite hasta cuatro decimales.";
        if (EsAjuste(tc)) return cantidad < 0 ? "La cantidad contada no puede ser negativa." : null;
        return cantidad <= 0 ? "La cantidad debe ser mayor a cero." : null;
    }

    /// <summary>Número de comprobante como lo graba el legacy: sucursal 4 + número 8 + letra.</summary>
    public static string Comprobante(int sucursal, int numero, string letra)
        => $"{sucursal:0000}{numero:00000000}{letra}";

    /// <summary>Letra del talonario (primera de LETRAS); X si no tiene. Solo letras con columnas de numeración.</summary>
    public static string Letra(string? letras)
    {
        var l = (letras ?? "").Trim().ToUpperInvariant();
        var letra = l.Length == 0 ? "X" : l[..1];
        return letra is "A" or "B" or "C" or "E" or "M" or "R" or "X" ? letra : "X";
    }

    /// <summary>Próximo número: el mayor entre el contador del talonario y el último usado + 1.</summary>
    public static int ProximoNumero(int? ultimoTalonario, int? ultimoUsado)
        => Math.Max(Math.Max(ultimoTalonario ?? 1, 1), (ultimoUsado ?? 0) + 1);

    /// <summary>Línea ya resuelta (cantidades en la unidad del artículo) lista para armar las filas de V_MV_STOCK.</summary>
    public sealed record LineaResuelta(string Articulo, string Descripcion, string Unidad, decimal Costo,
        decimal Movimiento, decimal Bultos, decimal Kilos, decimal MetrosCubicos);

    public sealed record Fila(int Secuencia, string Articulo, string Descripcion, string Unidad, decimal Costo,
        decimal Cantidad, decimal Bultos, decimal Kilos, decimal MetrosCubicos, string Deposito, string? DepositoDestino);

    /// <summary>
    /// Filas de V_MV_STOCK. Bultos/kilos/m³ llevan el signo del movimiento. En TR, primero todas las salidas del
    /// origen y después las entradas al destino, con la secuencia continuada (ClsCmnCptes: CantVeces=2).
    /// </summary>
    public static IReadOnlyList<Fila> Filas(string tc, IReadOnlyList<LineaResuelta> lineas, string deposito, string? destino)
    {
        var filas = new List<Fila>();
        var secuencia = 0;
        foreach (var l in lineas)
        {
            var signo = Math.Sign(l.Movimiento);
            filas.Add(new(++secuencia, l.Articulo, l.Descripcion, l.Unidad, l.Costo, l.Movimiento,
                signo * Math.Abs(l.Bultos), signo * Math.Abs(l.Kilos), signo * Math.Abs(l.MetrosCubicos),
                deposito, EsTransferencia(tc) ? destino : null));
        }
        if (!EsTransferencia(tc)) return filas;
        foreach (var l in lineas)
            filas.Add(new(++secuencia, l.Articulo, l.Descripcion, l.Unidad, l.Costo, -l.Movimiento,
                Math.Abs(l.Bultos), Math.Abs(l.Kilos), Math.Abs(l.MetrosCubicos), destino!, deposito));
        return filas;
    }
}

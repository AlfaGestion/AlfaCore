namespace AlfaCore.Common;

/// <summary>
/// Formato estándar de los códigos-PK de texto de ancho fijo del sistema (TA_/V_TA_/C_TA_,
/// artículos, vendedores y demás maestros): si el valor es <b>numérico</b> se alinea a la
/// <b>derecha</b> (blancos a la izquierda); si es <b>alfanumérico</b> se alinea a la
/// <b>izquierda</b>. Es la misma lógica que la función global del sistema de escritorio, y
/// permite ordenar por código —sea numérico o no— aunque el campo sea nvarchar.
/// Al grabar cualquier PK de texto hay que pasarlo por acá.
/// <para><b>Excepción</b> (ver docs/DATABASE_TABLES_SUMMARY.md): el plan de cuentas
/// <c>MA_CUENTAS</c> (CODIGO/CUENTA) se alinea SIEMPRE a la izquierda por jerarquía
/// contable — ese caso NO debe usar este helper.</para>
/// </summary>
public static class CodigoPk
{
    /// <summary>
    /// Devuelve el código formateado al ancho del campo. Numérico → derecha; alfanumérico →
    /// izquierda. Si <paramref name="ancho"/> es menor o igual a 0, devuelve el valor sin
    /// espacios de relleno (solo recortado). Si el valor supera el ancho, se trunca.
    /// </summary>
    public static string Format(string? codigo, int ancho)
    {
        var valor = (codigo ?? string.Empty).Trim();
        if (ancho <= 0)
            return valor;
        if (valor.Length > ancho)
            valor = valor[..ancho];

        var esNumerico = valor.Length > 0 && valor.All(char.IsDigit);
        return esNumerico ? valor.PadLeft(ancho) : valor.PadRight(ancho);
    }
}

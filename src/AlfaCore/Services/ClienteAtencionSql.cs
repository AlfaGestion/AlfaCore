namespace AlfaCore.Services;

/// <summary>
/// Fragmentos SQL compartidos para saber "qué tipo de cliente es" (Conversaciones, informes de Conversaciones y
/// Tickets). Una sola definición del abono al mantenimiento para que las pantallas no muestren cosas distintas.
/// </summary>
public static class ClienteAtencionSql
{
    /// <summary>
    /// OUTER APPLY que expone <c>{alias}.Estado</c>: 'Abonado', 'Suspendido' o NULL (sin abono).
    /// Abonado al mantenimiento = la fila más reciente (por FechaUltMov) de la cuenta en MA_CUENTAS_AUTOCPTES, sin
    /// filtrar por concepto, activa y con movimiento en los últimos 3 meses.
    /// </summary>
    /// <param name="codigoClienteSql">Expresión SQL con el código del cliente (ej. <c>t.ClienteCodigo</c>).</param>
    public static string AbonoApply(string codigoClienteSql, string alias)
        => $"""
            OUTER APPLY (
                SELECT TOP (1)
                    CASE
                        WHEN ISNULL(auto.Activo, 0) = 0 THEN N'Suspendido'
                        WHEN auto.FechaUltMov IS NULL OR auto.FechaUltMov < DATEADD(MONTH, -3, GETDATE()) THEN N'Suspendido'
                        ELSE N'Abonado'
                    END AS Estado
                FROM dbo.MA_CUENTAS_AUTOCPTES auto
                WHERE LTRIM(RTRIM(ISNULL({codigoClienteSql}, N''))) <> N''
                  AND UPPER(LTRIM(RTRIM(auto.Cuenta))) = UPPER(LTRIM(RTRIM({codigoClienteSql})))
                ORDER BY auto.FechaUltMov DESC
            ) {alias}
            """;
}

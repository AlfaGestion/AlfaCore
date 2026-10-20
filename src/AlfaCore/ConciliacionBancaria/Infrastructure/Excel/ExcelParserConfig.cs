namespace AlfaCore.ConciliacionBancaria.Infrastructure.Excel;

/// <summary>
/// Configuración de columnas para un extracto bancario en Excel.
/// Cada banco declara sus propios nombres de columna; la base aplica la lógica.
/// </summary>
public sealed record ExcelParserConfig
{
    /// <summary>
    /// Cantidad máxima de filas iniciales que se escanean buscando la cabecera.
    /// Permite saltear filas de metadata del banco (logos, razón social, etc.).
    /// </summary>
    public int MaxHeaderScanRows { get; init; } = 15;

    /// <summary>
    /// Nombre del worksheet a usar. Null = primer worksheet disponible.
    /// </summary>
    public string? WorksheetName { get; init; }

    // --- Columnas obligatorias ---

    public required string DateColumn { get; init; }
    public required string DescriptionColumn { get; init; }

    // --- Importe: usar SignedAmountColumn (importe único con signo)
    //     O bien DebitColumn + CreditColumn (dos columnas separadas). ---

    /// <summary>Columna con importe con signo. Positivo = crédito, negativo = débito.</summary>
    public string? SignedAmountColumn { get; init; }

    /// <summary>Débito (egreso). Los valores en Excel son positivos; se toma como negativo.</summary>
    public string? DebitColumn { get; init; }

    /// <summary>Crédito (ingreso). Los valores en Excel son positivos; se toma como positivo.</summary>
    public string? CreditColumn { get; init; }

    // --- Columnas opcionales ---

    public string? BalanceColumn { get; init; }
    public string? ReferenceColumn { get; init; }
    public string? CounterpartyNameColumn { get; init; }
    public string? CounterpartyTaxIdColumn { get; init; }
    public string? ValueDateColumn { get; init; }
}

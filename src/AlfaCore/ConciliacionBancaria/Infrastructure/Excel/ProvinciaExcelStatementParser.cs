using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Infrastructure.Excel;

/// <summary>
/// Parser para extractos del Banco Provincia de Buenos Aires exportados en Excel.
///
/// Formato observado:
///   Fila 1:  "Banco Provincia de Buenos Aires - Detalle de movimientos"
///   Fila 2:  cabecera: Número Secuencia | Fecha | Importe | Saldo |
///                      Descripción | Descripción Extendida | Nombre Comercio
///   Filas 3+: datos
///
/// Importe con signo:  positivo = crédito (ingreso), negativo = débito (egreso).
/// Se prefiere "Descripción Extendida" porque contiene más detalle
/// (ej: "CR.TRAN. 30672598164 REN FOR SRL" vs "CREDITO TRANSFERENCIA I").
///
/// Los aliases con "|" permiten que el parser funcione aunque el banco
/// cambie levemente los nombres de columna entre versiones del extracto.
/// </summary>
public sealed class ProvinciaExcelStatementParser : BaseExcelStatementParser
{
    public override BankSource Source => BankSource.Provincia;

    protected override ExcelParserConfig Config { get; } = new()
    {
        // Importe con signo (no columnas separadas Débito/Crédito)
        SignedAmountColumn     = "Importe",

        // Fecha
        DateColumn             = "Fecha",

        // Descripción: preferir la extendida; si no existe, usar la corta
        DescriptionColumn      = "Descripción Extendida|Descripcion Extendida|Descripción|Descripcion",

        // Saldo
        BalanceColumn          = "Saldo",

        // Referencia interna del banco
        ReferenceColumn        = "Número Secuencia|Numero Secuencia|Nro. Secuencia",

        // Nombre del comercio / contraparte (cuando aplica)
        CounterpartyNameColumn = "Nombre Comercio",

        // La fila 1 es texto libre; la cabecera real está en la fila 2
        MaxHeaderScanRows      = 40
    };
}

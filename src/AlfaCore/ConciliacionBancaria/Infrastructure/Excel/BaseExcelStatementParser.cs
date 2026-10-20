using System.Globalization;
using System.Text;
using System.Text.Json;
using AlfaCore.ConciliacionBancaria.Application.Abstractions;
using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using ClosedXML.Excel;

namespace AlfaCore.ConciliacionBancaria.Infrastructure.Excel;

/// <summary>
/// Base para todos los parsers de extractos Excel.
///
/// NOMBRES DE COLUMNA CON ALIASES
/// ──────────────────────────────
/// Cada campo en ExcelParserConfig acepta múltiples nombres posibles
/// separados por "|".  El parser prueba cada alternativa en orden y usa
/// la primera que encuentre en el archivo.
///
/// Ejemplo:  DateColumn = "Fecha|Fecha Mov.|FECHA|Date"
///
/// Esto permite que el mismo parser funcione con variantes del mismo
/// extracto (distintas versiones del portal, distintos períodos, etc.)
/// sin necesidad de crear parsers adicionales.
///
/// DETECCIÓN AUTOMÁTICA DE CABECERA
/// ─────────────────────────────────
/// Se escanean las primeras MaxHeaderScanRows filas buscando una que
/// contenga al menos uno de los aliases de cada columna obligatoria.
/// Las filas de metadata del banco (logos, razón social, etc.) se
/// ignoran automáticamente.
/// </summary>
public abstract class BaseExcelStatementParser : IExcelStatementParser
{
    private static readonly CultureInfo ArgentinaCulture = CultureInfo.GetCultureInfo("es-AR");

    public abstract BankSource Source { get; }

    protected abstract ExcelParserConfig Config { get; }

    public virtual Task<IReadOnlyCollection<NormalizedMovementDto>> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("No se encontró el archivo a importar.", filePath);

        cancellationToken.ThrowIfCancellationRequested();
        var movements = ParseInternal(filePath, cancellationToken);
        return Task.FromResult<IReadOnlyCollection<NormalizedMovementDto>>(movements);
    }

    // =========================================================================
    // Lectura
    // =========================================================================

    private List<NormalizedMovementDto> ParseInternal(string filePath, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(filePath);

        var worksheet = Config.WorksheetName is not null
            ? workbook.Worksheet(Config.WorksheetName)
            : workbook.Worksheets.First();

        var (headerRowNumber, columnMap) = FindHeader(worksheet);

        if (headerRowNumber == 0)
        {
            var expected = string.Join(", ",
                RequiredColumnSpecs().Select(s => s.Replace("|", " ó ")));
            throw new InvalidOperationException(
                $"No se encontró la fila de cabecera en el archivo. " +
                $"Se esperaban columnas: {expected}.");
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRowNumber;
        var result  = new List<NormalizedMovementDto>();

        for (int r = headerRowNumber + 1; r <= lastRow; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = worksheet.Row(r);
            if (row.IsEmpty()) continue;

            var dto = BuildMovement(row, columnMap);
            if (dto is not null) result.Add(dto);
        }

        return result;
    }

    // =========================================================================
    // Detección de cabecera
    // =========================================================================

    private (int headerRow, Dictionary<string, int> columnMap) FindHeader(IXLWorksheet worksheet)
    {
        var required    = RequiredColumnSpecs();
        var lastScanRow = Math.Min(Config.MaxHeaderScanRows, worksheet.LastRowUsed()?.RowNumber() ?? 1);

        for (int r = 1; r <= lastScanRow; r++)
        {
            var row = worksheet.Row(r);
            var map = BuildColumnMap(row);

            // La fila es cabecera si para cada columna obligatoria
            // se encuentra al menos un alias en el mapa.
            if (required.All(spec => AnyAliasPresent(map, spec)))
                return (r, map);
        }

        return (0, new Dictionary<string, int>());
    }

    /// <summary>
    /// Devuelve true si al menos uno de los aliases del spec (separados por |)
    /// está presente en el mapa de columnas.
    /// </summary>
    private static bool AnyAliasPresent(Dictionary<string, int> map, string columnSpec) =>
        Aliases(columnSpec).Any(map.ContainsKey);

    private Dictionary<string, int> BuildColumnMap(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in headerRow.CellsUsed())
        {
            var header = NormalizeHeader(cell.GetString());
            if (!string.IsNullOrEmpty(header) && !map.ContainsKey(header))
                map[header] = cell.Address.ColumnNumber;
        }

        return map;
    }

    /// <summary>
    /// Columnas que deben estar presentes para confirmar que una fila es la cabecera.
    /// Incluye fecha, descripción e importe (en cualquiera de sus formas).
    /// </summary>
    private List<string> RequiredColumnSpecs()
    {
        var list = new List<string> { Config.DateColumn, Config.DescriptionColumn };

        if (Config.SignedAmountColumn is not null)
            list.Add(Config.SignedAmountColumn);
        else
        {
            if (Config.DebitColumn  is not null) list.Add(Config.DebitColumn);
            if (Config.CreditColumn is not null) list.Add(Config.CreditColumn);
        }

        return list;
    }

    // =========================================================================
    // Construcción de movimiento
    // =========================================================================

    private NormalizedMovementDto? BuildMovement(IXLRow row, Dictionary<string, int> columnMap)
    {
        var dateStr = GetCell(row, columnMap, Config.DateColumn);
        var dateCell=Aliases(Config.DateColumn).Where(columnMap.ContainsKey).Select(a=>row.Cell(columnMap[a]))
            .FirstOrDefault(c=>c.DataType==XLDataType.DateTime);
        DateTime date;
        var validDate=dateCell is not null ? (date=dateCell.GetDateTime().Date)!=default : ParseMovementDate(dateStr,out date);
        if (!validDate)
        {
            if (ResolveAmount(row, columnMap) != 0m)
                throw new InvalidOperationException($"La fila {row.RowNumber()} tiene importe y una fecha que no se pudo interpretar: {dateStr}.");
            return null;
        }

        var description = ResolveDescription(row, columnMap).Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            if (ResolveAmount(row,columnMap) != 0m)
                throw new InvalidOperationException($"La fila {row.RowNumber()} tiene importe y no tiene descripción.");
            return null;
        }

        var amount = ResolveAmount(row, columnMap);
        if (amount == 0m) return null;

        var balance           = GetCellDecimal(row, columnMap, Config.BalanceColumn);
        var valueDate         = TryParseDate(GetCell(row, columnMap, Config.ValueDateColumn), out var vd) ? vd : (DateTime?)null;
        var reference         = GetCell(row, columnMap, Config.ReferenceColumn);
        var counterpartyName  = GetCell(row, columnMap, Config.CounterpartyNameColumn);
        var counterpartyTaxId = GetCell(row, columnMap, Config.CounterpartyTaxIdColumn);

        return new NormalizedMovementDto
        {
            Source            = Source,
            MovementDate      = date,
            ValueDate         = valueDate,
            Description       = description,
            Amount            = amount,
            Balance           = balance,
            ExternalReference = reference,
            CounterpartyName  = counterpartyName,
            CounterpartyTaxId = counterpartyTaxId,
            RawDataJson       = BuildRawJson(row, columnMap)
        };
    }

    protected virtual bool ParseMovementDate(string value, out DateTime date) => TryParseDate(value, out date);
    protected virtual string ResolveDescription(IXLRow row, Dictionary<string,int> columns)
        => GetCell(row, columns, Config.DescriptionColumn);

    private decimal ResolveAmount(IXLRow row, Dictionary<string, int> columnMap)
    {
        if (Config.SignedAmountColumn is not null)
            return GetCellDecimal(row, columnMap, Config.SignedAmountColumn) ?? 0m;

        // Débito/Crédito separados: crédito positivo, débito negativo
        var credit = GetCellDecimal(row, columnMap, Config.CreditColumn) ?? 0m;
        var debit  = GetCellDecimal(row, columnMap, Config.DebitColumn)  ?? 0m;
        return credit - debit;
    }

    /// <summary>
    /// Lee una celda como decimal. Si la celda contiene un valor numérico nativo de Excel
    /// lo obtiene directamente (evita problemas de separador decimal cultural).
    /// Si es texto, cae al parser de string.
    /// </summary>
    private static decimal? GetCellDecimal(IXLRow row, Dictionary<string, int> map, string? columnSpec)
    {
        if (columnSpec is null) return null;

        foreach (var alias in Aliases(columnSpec))
        {
            if (!map.TryGetValue(alias, out var col)) continue;

            var cell = row.Cell(col);

            // Celda numérica nativa → leer directo sin pasar por string
            if (cell.DataType == XLDataType.Number)
                return (decimal)cell.GetDouble();

            // Celda de texto → parser con cultura
            var text = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(text))
                return TryParseDecimal(text) ?? throw new InvalidOperationException($"La fila {row.RowNumber()} contiene un importe o saldo inválido: {text}.");
        }

        return null;
    }

    // =========================================================================
    // Lectura de celda con soporte de aliases
    // =========================================================================

    /// <summary>
    /// Lee el valor de la celda para la columna especificada.
    /// Si columnSpec contiene aliases separados por "|", los prueba en orden
    /// y devuelve el valor del primero que exista en el mapa.
    /// </summary>
    protected static string GetCell(IXLRow row, Dictionary<string, int> map, string? columnSpec)
    {
        if (columnSpec is null) return string.Empty;

        string? firstExisting = null;

        foreach (var alias in Aliases(columnSpec))
        {
            if (map.TryGetValue(alias, out var col))
            {
                var value = row.Cell(col).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                firstExisting ??= value;
            }
        }

        return firstExisting ?? string.Empty;
    }

    /// <summary>
    /// Descompone un spec de columna en sus aliases normalizados.
    /// "Fecha|Fecha Mov." → ["fecha", "fecha mov."]
    /// </summary>
    private static IEnumerable<string> Aliases(string columnSpec) =>
        columnSpec.Split('|').Select(a => NormalizeHeader(a.Trim()));

    // =========================================================================
    // Helpers de parseo
    // =========================================================================

    private static bool TryParseDate(string value, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        // Fecha almacenada como número OLE Automation por Excel
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var oaDate)
            && oaDate is > 1 and < 3_000_000)
        {
            result = DateTime.FromOADate(oaDate);
            return true;
        }

        string[] formats =
        [
            "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy",
            "yyyy-MM-dd", "dd-MM-yyyy",
            "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm",
            "d/M/yyyy HH:mm"
        ];

        return DateTime.TryParseExact(value, formats, ArgentinaCulture, DateTimeStyles.None, out result)
            || DateTime.TryParse(value, ArgentinaCulture, DateTimeStyles.None, out result);
    }

    private static decimal? TryParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var cleaned = value.Replace("$", "").Replace("\u00a0", "").Replace(" ", "").Trim();
        if (string.IsNullOrEmpty(cleaned)) return null;

        return ParseAmount(cleaned);
    }

    public static decimal? ParseAmount(string value)
    {
        var culture = value.LastIndexOf(',') > value.LastIndexOf('.') ? ArgentinaCulture : CultureInfo.InvariantCulture;
        return decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowParentheses, culture, out var amount) ? amount : null;
    }

    private static string NormalizeHeader(string header)
    {
        var normalized = header.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Trim();
    }

    private static string BuildRawJson(IXLRow row, Dictionary<string, int> columnMap)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (header, col) in columnMap)
        {
            var v = row.Cell(col).GetString().Trim();
            if (!string.IsNullOrEmpty(v))
                dict[header] = v;
        }
        return JsonSerializer.Serialize(dict);
    }
}

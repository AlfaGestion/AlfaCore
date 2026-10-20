using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Infrastructure.Excel;

public sealed class SantanderExcelStatementParser : BaseExcelStatementParser
{
    private static readonly CultureInfo ArgentinaCulture = CultureInfo.GetCultureInfo("es-AR");

    public override BankSource Source => BankSource.Santander;

    protected override ExcelParserConfig Config { get; } = new()
    {
        DateColumn        = "Fecha Mov.|Fecha|FECHA",
        DescriptionColumn = "Descripción|Descripcion|Concepto|DESCRIPCION|Detalle",
        SignedAmountColumn = "Caja de Ahorro|Cuenta Corriente|Importe|Monto",
        DebitColumn       = "Débito|Debito|DEBITO",
        CreditColumn      = "Crédito|Credito|CREDITO",
        BalanceColumn     = "Saldo|SALDO",
        ReferenceColumn   = "Referencia|Nro. Referencia|Ref.",
        MaxHeaderScanRows = 30
    };

    public override Task<IReadOnlyCollection<NormalizedMovementDto>> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("No se encontró el archivo a importar.", filePath);

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();

        var headerRow = FindHeaderRow(worksheet);
        if (headerRow is null)
        {
            throw new InvalidOperationException(
                "No se encontró la tabla de movimientos de Santander. " +
                "Se esperaba una fila con columnas equivalentes a Fecha, Descripción, Referencia y alguna de importe.");
        }

        var columnMap = BuildColumnMap(headerRow);
        var dateCol = RequireColumn(columnMap, "fecha");
        var descriptionCol = RequireFirst(columnMap, "descripcion", "descripción", "concepto", "detalle");
        var referenceCol = FindFirst(columnMap, "referencia", "ref.", "nro. referencia");
        var savingsCol = FindFirst(columnMap, "caja de ahorro", "caja ahorro");
        var currentCol = FindFirst(columnMap, "cuenta corriente");
        var balanceCol = FindFirst(columnMap, "saldo");

        if (savingsCol is null && currentCol is null)
        {
            throw new InvalidOperationException(
                "No se encontraron columnas de importe válidas. Se esperaba 'Caja de Ahorro' o 'Cuenta Corriente'.");
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        var result = new List<NormalizedMovementDto>();

        for (int rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty()) continue;

            var dateCell=row.Cell(dateCol);
            var dateText = dateCell.GetString().Trim();
            DateTime movementDate;
            var validDate=dateCell.DataType==XLDataType.DateTime ? (movementDate=dateCell.GetDateTime().Date)!=default : TryParseDate(dateText,out movementDate);
            if (!validDate) continue;

            var description = row.Cell(descriptionCol).GetString().Trim();
            if (string.IsNullOrWhiteSpace(description)) continue;

            var amount = ResolveAmount(
                savingsCol.HasValue ? row.Cell(savingsCol.Value).GetString().Trim() : string.Empty,
                currentCol.HasValue ? row.Cell(currentCol.Value).GetString().Trim() : string.Empty);

            if (amount == 0m) continue;

            result.Add(new NormalizedMovementDto
            {
                Source = Source,
                MovementDate = movementDate,
                Description = description,
                Amount = amount,
                Balance = balanceCol.HasValue ? TryParseDecimal(row.Cell(balanceCol.Value).GetString().Trim()) : null,
                ExternalReference = referenceCol.HasValue ? row.Cell(referenceCol.Value).GetString().Trim() : string.Empty,
                CounterpartyName = ExtractCounterpartyName(description),
                CounterpartyTaxId = ExtractCounterpartyTaxId(description),
                RawDataJson = BuildRawJson(row, columnMap)
            });
        }

        return Task.FromResult<IReadOnlyCollection<NormalizedMovementDto>>(result);
    }

    private static IXLRow? FindHeaderRow(IXLWorksheet worksheet)
    {
        var lastScanRow = Math.Min(40, worksheet.LastRowUsed()?.RowNumber() ?? 1);

        for (int rowNumber = 1; rowNumber <= lastScanRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var headers = row.CellsUsed()
                .Select(cell => Normalize(cell.GetString()))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (headers.Count == 0) continue;

            var hasDate = headers.Any(h => h == "fecha" || h == "fecha mov");
            var hasDescription = headers.Any(h => h == "descripcion" || h == "descripción" || h == "concepto" || h == "detalle");
            var hasReference = headers.Any(h => h.Contains("referencia", StringComparison.Ordinal) || h == "ref.");
            var hasAmount = headers.Any(h => h == "caja de ahorro" || h == "cuenta corriente" || h == "importe" || h == "monto");

            if (hasDate && hasDescription && hasReference && hasAmount)
                return row;
        }

        return null;
    }

    private static Dictionary<string, int> BuildColumnMap(IXLRow row)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in row.CellsUsed())
        {
            var normalized = Normalize(cell.GetString());
            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
                map[normalized] = cell.Address.ColumnNumber;
        }

        return map;
    }

    private static int RequireColumn(Dictionary<string, int> map, string key)
    {
        if (map.TryGetValue(key, out var column))
            return column;

        throw new InvalidOperationException($"No se encontró la columna requerida '{key}'.");
    }

    private static int RequireFirst(Dictionary<string, int> map, params string[] keys)
    {
        var column = FindFirst(map, keys);
        if (!column.HasValue)
            throw new InvalidOperationException($"No se encontró ninguna de las columnas esperadas: {string.Join(", ", keys)}.");

        return column.Value;
    }

    private static int? FindFirst(Dictionary<string, int> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (map.TryGetValue(Normalize(key), out var column))
                return column;
        }

        return null;
    }

    private static decimal ResolveAmount(string savingsValue, string currentValue)
    {
        var savings = TryParseDecimal(savingsValue);
        var current = TryParseDecimal(currentValue);

        if (savings.HasValue && savings.Value != 0m) return savings.Value;
        if (current.HasValue && current.Value != 0m) return current.Value;
        return 0m;
    }

    private static bool TryParseDate(string value, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (DateTime.TryParseExact(value, "dd/MM/yyyy", ArgentinaCulture, DateTimeStyles.None, out result))
            return true;

        return DateTime.TryParse(value, ArgentinaCulture, DateTimeStyles.None, out result);
    }

    private static decimal? TryParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var cleaned = value.Replace("$", "").Replace("\u00a0", "").Replace(" ", "").Trim();
        return ParseAmount(cleaned);
    }

    private static string ExtractCounterpartyName(string description)
        => AlfaCore.ConciliacionBancaria.Application.Services.ContraparteBancaria.Extraer(description).Nombre;

    private static string ExtractCounterpartyTaxId(string description)
        => AlfaCore.ConciliacionBancaria.Application.Services.ContraparteBancaria.Extraer(description).Cuit ?? "";

    private static string Normalize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString().Trim();
    }

    private static string BuildRawJson(IXLRow row, Dictionary<string, int> map)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (header, col) in map)
        {
            var value = row.Cell(col).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(value))
                dict[header] = value;
        }

        return JsonSerializer.Serialize(dict);
    }
}

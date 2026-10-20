using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Infrastructure.Excel;

/// <summary>
/// Parser para extractos de Mercado Pago exportados en Excel (.xlsx).
///
/// Formato típico (reporte de actividad de MP):
///   Fila 1: cabecera: Fecha | Descripción | Tipo de operación |
///                     Número de referencia | Monto | ...
///   Filas 2+: datos
///
/// El importe viene en columna "Monto" con signo:
///   positivo = acreditación, negativo = deducción/retiro.
///
/// Los aliases con "|" cubren el caso en que MP exporte con nombres
/// en inglés o con variantes de tildes.
/// </summary>
public sealed class MercadoPagoExcelStatementParser(DateTime? fechaExtraccion = null) : BaseExcelStatementParser
{
    public override BankSource Source => BankSource.MercadoPago;
    public static bool RequiereFechaReferencia(string value)
    {
        var texto=value.Trim().ToLowerInvariant();
        return texto is "hoy" or "ayer" or "anteayer" || System.Text.RegularExpressions.Regex.IsMatch(texto,@"^\d{1,2} de [a-záéíóú]+$");
    }

    protected override string ResolveDescription(ClosedXML.Excel.IXLRow row, Dictionary<string,int> columns)
        => string.Join(" · ",new[] { base.ResolveDescription(row,columns),GetCell(row,columns,"Tipo|Tipo de operación") }.Where(s=>!string.IsNullOrWhiteSpace(s)));

    protected override bool ParseMovementDate(string value, out DateTime date)
    {
        var normalized=value.Trim().ToLowerInvariant();
        if (normalized is "hoy" or "ayer" or "anteayer")
        {
            if (!fechaExtraccion.HasValue)
                throw new InvalidOperationException("El extracto contiene fechas relativas. Indicá la fecha de referencia de la descarga.");
            date=fechaExtraccion.Value.Date.AddDays(normalized=="hoy"?0:normalized=="ayer"?-1:-2);
            return true;
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(normalized,@"^\d{1,2} de [a-záéíóú]+$"))
        {
            if (!fechaExtraccion.HasValue)
                throw new InvalidOperationException("El extracto contiene fechas sin año. Indicá la fecha de referencia de la descarga.");
            if (DateTime.TryParse(normalized+" de "+fechaExtraccion.Value.Year,System.Globalization.CultureInfo.GetCultureInfo("es-AR"),System.Globalization.DateTimeStyles.None,out date))
            {
                if(date.Date>fechaExtraccion.Value.Date) date=date.AddYears(-1);
                return true;
            }
        }
        return base.ParseMovementDate(value,out date);
    }

    protected override ExcelParserConfig Config { get; } = new()
    {
        DateColumn             = "Fecha|FECHA|Date",
        DescriptionColumn      = "Descripción|Descripcion|Detalle|DESCRIPCION",
        SignedAmountColumn     = "Monto|Importe|Amount",
        ReferenceColumn        = "Número de referencia|Numero de referencia|Referencia|ID operación",
        CounterpartyNameColumn = "Nombre del emisor|Emisor|Pagador",
        MaxHeaderScanRows      = 5
    };
}

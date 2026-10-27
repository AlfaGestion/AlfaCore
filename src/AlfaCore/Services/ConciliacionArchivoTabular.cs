using System.Globalization;
using System.Text;
using AlfaCore.Models;
using ClosedXML.Excel;
using ExcelDataReader;

namespace AlfaCore.Services;

// XLS y CSV se normalizan al mismo libro que consumen los parsers existentes.
internal static class ConciliacionArchivoTabular
{
    internal static XLWorkbook Abrir(ConciliacionArchivo archivo,CancellationToken ct)
    {
        using var stream=new MemoryStream(archivo.Contenido,false);
        if(Path.GetExtension(archivo.Nombre).Equals(".xlsx",StringComparison.OrdinalIgnoreCase))
            return new XLWorkbook(stream);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var config=new ExcelReaderConfiguration { FallbackEncoding=Encoding.GetEncoding(1252),AutodetectSeparators=[';',',','\t','|'] };
        using var reader=Path.GetExtension(archivo.Nombre).Equals(".csv",StringComparison.OrdinalIgnoreCase)
            ? ExcelReaderFactory.CreateCsvReader(stream,config) : ExcelReaderFactory.CreateReader(stream,config);
        var wb=new XLWorkbook();
        try
        {
            // Convertir no aporta evidencia de la fecha de descarga del archivo original.
            wb.Properties.Created=DateTime.UnixEpoch;
            var ws=wb.AddWorksheet("Extracto");
            var fila=0;
            while(reader.Read())
            {
                ct.ThrowIfCancellationRequested();fila++;
                if(fila>1_048_576 || reader.FieldCount>16_384)
                    throw new InvalidOperationException("El extracto supera el tamaño de hoja admitido. Exportá un período más corto.");
                for(var col=0;col<reader.FieldCount;col++)
                {
                    var valor=reader.GetValue(col);
                    if(valor is null || valor is string {Length:0}) continue;
                    var cell=ws.Cell(fila,col+1);
                    if(valor is DateTime fecha) cell.Value=fecha;
                    else if(valor is double numero) cell.Value=numero;
                    else if(valor is int entero) cell.Value=entero;
                    else if(valor is bool logico) cell.Value=logico;
                    else cell.Value=Convert.ToString(valor,CultureInfo.InvariantCulture) ?? "";
                }
            }
            return wb;
        }
        catch { wb.Dispose();throw; }
    }
}

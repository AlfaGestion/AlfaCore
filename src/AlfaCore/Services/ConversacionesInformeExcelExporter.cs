using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class ConversacionesInformeExcelExporter
{
    public byte[] Exportar(ConversacionInformeMensualDto informe)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add($"Informe {informe.Mes:00}-{informe.Anio}");

        ws.Cell(1, 1).Value = $"Informe mensual de conversaciones — {informe.PeriodoLabel}";
        ws.Range(1, 1, 1, 9).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(2, 1).Value = $"Generado {informe.FechaGeneracion:dd/MM/yyyy HH:mm} · {informe.Filas.Count} clientes/contactos";
        ws.Range(2, 1, 2, 9).Merge();
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;

        var headers = new[] { "Cliente / Contacto", "Clasificación", "Categoría", "Abono", "Conv.", "Días", "Mensajes", "Horas", "Hs. fact." };
        const int headerRow = 4;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f2942");
            cell.Style.Font.FontColor = XLColor.White;
        }

        var row = headerRow + 1;
        foreach (var f in informe.Filas)
        {
            ws.Cell(row, 1).Value = f.NombreMostrar;
            ws.Cell(row, 2).Value = f.ClasificacionDesc;
            ws.Cell(row, 3).Value = f.CategoriaDesc;
            ws.Cell(row, 4).Value = EstadoAbonoLabel(f.EstadoAbono);
            ws.Cell(row, 5).Value = f.CantConversaciones;
            ws.Cell(row, 6).Value = f.CantDias;
            ws.Cell(row, 7).Value = f.CantMensajes;
            SetHoras(ws.Cell(row, 8), f.MinutosTotales);
            SetHoras(ws.Cell(row, 9), f.MinutosFacturables);
            row++;
        }

        // Fila de totales
        ws.Cell(row, 1).Value = $"TOTALES ({informe.Filas.Count})";
        ws.Cell(row, 5).Value = informe.Filas.Sum(x => x.CantConversaciones);
        ws.Cell(row, 7).Value = informe.Filas.Sum(x => x.CantMensajes);
        SetHoras(ws.Cell(row, 8), informe.Filas.Sum(x => x.MinutosTotales));
        SetHoras(ws.Cell(row, 9), informe.Filas.Sum(x => x.MinutosFacturables));
        ws.Range(row, 1, row, 9).Style.Font.Bold = true;
        ws.Range(row, 1, row, 9).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivo(ConversacionInformeMensualDto informe)
        => $"Informe_Conversaciones_{informe.Anio}-{informe.Mes:00}.xlsx";

    private static void SetHoras(IXLCell cell, int minutos)
    {
        cell.Value = Math.Round(minutos / 60.0, 2);
        cell.Style.NumberFormat.Format = "0.00";
    }

    private static string EstadoAbonoLabel(string estado) => estado switch
    {
        "Abonado" => "Abonado",
        "Suspendido" => "Suspendido",
        _ => "Sin abono"
    };
}

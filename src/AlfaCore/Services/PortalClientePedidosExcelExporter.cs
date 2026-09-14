using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class PortalClientePedidosExcelExporter
{
    public byte[] Exportar(IReadOnlyList<PortalClientePedidoResumenDto> pedidos, string nombreEmpresa, string nombreCliente, DateTime? desde, DateTime? hasta)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Pedidos");
        ws.Cell(1, 1).Value = $"Pedidos — {nombreEmpresa}";
        ws.Cell(2, 1).Value = $"Cliente: {nombreCliente} · Registros: {pedidos.Count}";
        var headers = new[] { "Fecha", "Comprobante", "Descripción", "Vencimiento", "Importe", "Estado" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(4, i + 1).Value = headers[i];
        var row = 5;
        foreach (var pedido in pedidos)
        {
            ws.Cell(row, 1).Value = pedido.Fecha; ws.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(row, 2).Value = $"{pedido.Tc} {pedido.IdComprobanteTexto}";
            ws.Cell(row, 3).Value = pedido.EsPedidoWeb ? "Pedido web" : "Pedido registrado";
            ws.Cell(row, 4).Value = "—";
            ws.Cell(row, 5).Value = pedido.Total; ws.Cell(row, 5).Style.NumberFormat.Format = "$ #,##0.00";
            ws.Cell(row, 6).Value = pedido.Anulada ? "Anulado" : "Registrado";
            row++;
        }
        ws.Row(4).Style.Font.Bold = true;
        ws.Columns().AdjustToContents(8, 60);
        ws.Column(3).Width = Math.Max(ws.Column(3).Width, 25);
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    public static string NombreArchivo() => $"pedidos_{DateTime.Today:yyyyMMdd}.xlsx";
}

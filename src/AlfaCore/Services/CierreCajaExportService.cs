using AlfaCore.Models;
using ClosedXML.Excel;
using System.Net;
using System.Net.Mail;

namespace AlfaCore.Services;

public sealed record CierreCajaArchivo(string Nombre, byte[] Contenido)
{
    public const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed class CierreCajaExportService(ICierreCajaService cierre, IConfiguracionGeneralService configuracion,
    IAppEventService events)
{
    public Task<CierreCajaArchivo> PrepararAsync(CierreCajaFiltros filtros, string unidad, CancellationToken ct)
        => LoggedAsync("ExportarExcel", async () =>
        {
            using var workbook = new XLWorkbook();
            var resumen = workbook.Worksheets.Add("Filtros");
            resumen.Cell(1, 1).Value = "Cierre de caja";
            resumen.Cell(2, 1).Value = "Fecha operativa";
            resumen.Cell(2, 2).Value = filtros.Fecha.Date;
            resumen.Cell(2, 2).Style.DateFormat.Format = "dd/MM/yyyy";
            resumen.Cell(3, 1).Value = "Unidad de negocio";
            resumen.Cell(3, 2).Value = unidad;
            resumen.Cell(4, 1).Value = "Caja";
            resumen.Cell(4, 2).Value = filtros.Caja.Length == 0 ? "Todas las cajas" : filtros.Caja;
            resumen.Cell(5, 1).Value = "Incluir saldo inicial";
            resumen.Cell(5, 2).Value = filtros.SaldoInicial ? "Sí" : "No";
            resumen.Cell(6, 1).Value = "Generado";
            resumen.Cell(6, 2).Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            resumen.Cell(7, 1).Value = "Secciones incluidas";
            resumen.Cell(7, 2).Value = string.Join(", ", CierreCajaSecciones.Activas(filtros).Select(s => s.Nombre));
            resumen.Cell(8, 1).Value = "Productos y rubros incluyen todas las cajas de la unidad y fecha seleccionadas.";
            resumen.Columns().AdjustToContents(12, 65);
            foreach (var section in CierreCajaSecciones.Activas(filtros))
            {
                ct.ThrowIfCancellationRequested();
                var data = await cierre.ExportarSeccionAsync(filtros, section.Clave, ct);
                AddSheet(workbook, section, data);
            }
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return new CierreCajaArchivo($"Cierre_caja_{filtros.Fecha:yyyy-MM-dd}.xlsx", stream.ToArray());
        }, ct);

    internal static void AddSheet(XLWorkbook workbook, CierreCajaSeccion section, CierreCajaPagina data)
    {
        var sheet = workbook.Worksheets.Add(section.Clave);
        sheet.Cell(1, 1).Value = section.Nombre;
        for (var c = 0; c < section.Columnas.Count; c++)
        {
            var column = section.Columnas[c];
            sheet.Cell(3, c + 1).Value = column.Nombre;
            var row = 4;
            foreach (var record in data.Filas)
            {
                record.TryGetValue(column.Campo, out var value);
                var cell = sheet.Cell(row++, c + 1);
                if (value is DateTime date) { cell.Value = date; cell.Style.DateFormat.Format = "dd/MM/yyyy"; }
                else if (column.Numero && value is not null && value is not DBNull)
                { cell.Value = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); cell.Style.NumberFormat.Format = "#,##0.00"; }
                else cell.Value = value is DBNull ? "" : value?.ToString()?.Trim() ?? "";
            }
            if (column.Totaliza && data.Totales.TryGetValue(column.Campo, out var total))
            {
                sheet.Cell(row, c + 1).Value = Convert.ToDouble(total);
                sheet.Cell(row, c + 1).Style.NumberFormat.Format = "#,##0.00";
            }
        }
        sheet.Row(3).Style.Font.Bold = true;
        sheet.Row(3).Style.Fill.BackgroundColor = XLColor.FromHtml("#153b60");
        sheet.Row(3).Style.Font.FontColor = XLColor.White;
        sheet.Row(data.Filas.Count + 4).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(3);
        sheet.Columns().AdjustToContents(10, 60);
    }

    public Task<bool> EnviarEmailAsync(CierreCajaArchivo archivo, string destinatario, CancellationToken ct)
        => LoggedAsync("EnviarEmail", async () =>
        {
            var to = new MailAddress(destinatario.Trim());
            var config = await configuracion.GetEmailAsync(ct);
            if (string.IsNullOrWhiteSpace(config.Server) || string.IsNullOrWhiteSpace(config.Cuenta))
                throw new InvalidOperationException("Completá el servidor y la cuenta de email en Configuración general.");
            using var message = new MailMessage { From = new MailAddress(config.Cuenta), Subject = "Cierre de caja",
                Body = "Se adjunta el cierre de caja en Excel, con los filtros y las secciones seleccionadas." };
            message.To.Add(to);
            message.Attachments.Add(new Attachment(new MemoryStream(archivo.Contenido), archivo.Nombre, CierreCajaArchivo.Mime));
            using var smtp = new SmtpClient(config.Server, int.TryParse(config.Port, out var port) ? port : 587)
            { EnableSsl = config.Ssl, UseDefaultCredentials = false, Credentials = new NetworkCredential(config.Cuenta, config.Password) };
            await smtp.SendMailAsync(message, ct);
            return true;
        }, ct);

    private async Task<T> LoggedAsync<T>(string action, Func<Task<T>> operation, CancellationToken ct)
    {
        try { return await operation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var incident = await events.LogErrorAsync("Cierre de caja", action, ex, "No se pudo completar la operación del informe.", ct: ct);
            throw new AppUserFacingException("No se pudo completar la operación del informe. " + ex.Message, incident, ex);
        }
    }
}

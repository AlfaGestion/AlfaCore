using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class ReporteComprasExcelExporter
{
    private static readonly System.Globalization.CultureInfo Cultura =
        System.Globalization.CultureInfo.GetCultureInfo("es-AR");

    // ── Resumen ───────────────────────────────────────────────────────────────

    public byte[] ExportarResumen(ResumenComprasDto resumen, FiltrosReporteCompras filtros, bool ventas = false)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ventas ? "Resumen de Ventas" : "Resumen de Compras");

        // Fila 1: título
        SetTitulo(ws, ventas ? "Resumen de Ventas" : "Resumen de Compras", 7);

        // Fila 2: período y fecha de exportación
        SetSubtitulo(ws, DescribirFiltros(filtros,ventas), 7);

        // Fila 3: KPIs
        ws.Cell(3, 1).Value = ventas ? "Total vendido" : "Total comprado";
        ws.Cell(3, 2).Value = resumen.TotalComprado;
        ws.Cell(3, 3).Value = "Neto";
        ws.Cell(3, 4).Value = resumen.NetoTotal;
        ws.Cell(3, 5).Value = "IVA";
        ws.Cell(3, 6).Value = resumen.IvaTotal;
        ws.Cell(3, 7).Value = "Comprobantes";
        ws.Cell(3, 8).Value = resumen.CantidadComprobantes;
        ws.Cell(3, 9).Value = ventas ? "Clientes" : "Proveedores";
        ws.Cell(3, 10).Value = resumen.CantidadProveedores;
        for (int c = 1; c <= 10; c++)
        {
            ws.Cell(3, c).Style.Font.Bold = true;
            ws.Cell(3, c).Style.Font.FontColor = XLColor.FromHtml("#1e3a5f");
        }

        // Fila 5: encabezados de tabla
        var headers = new[] { "Cuenta", ventas ? "Cliente" : "Proveedor", "Comprobantes", "Neto", "IVA", "Total", "% Part." };
        SetHeaders(ws, 5, headers);

        // Datos
        for (int i = 0; i < resumen.Filas.Count; i++)
        {
            var fila = resumen.Filas[i];
            int row = i + 6;
            ws.Cell(row, 1).Value = fila.Cuenta;
            ws.Cell(row, 2).Value = fila.RazonSocial;
            SetDecimalCell(ws.Cell(row, 3), fila.CantidadComprobantes);
            SetDecimalCell(ws.Cell(row, 4), fila.Neto);
            SetDecimalCell(ws.Cell(row, 5), fila.Iva);
            SetDecimalCell(ws.Cell(row, 6), fila.Total);
            ws.Cell(row, 7).Value = fila.Participacion.ToString("P1", Cultura);
            if (i % 2 == 1) SetRowBackground(ws, row, 7);
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.SheetView.FreezeRows(5);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Detalle ───────────────────────────────────────────────────────────────

    public byte[] ExportarDetalle(IReadOnlyList<DetalleComprasFilaDto> items, FiltrosReporteCompras filtros, int total, bool ventas = false)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ventas ? "Detalle de Ventas" : "Detalle de Compras");

        SetTitulo(ws, ventas ? "Detalle de Ventas" : "Detalle de Compras", 10);
        SetSubtitulo(ws, $"{DescribirFiltros(filtros,ventas)} — {total} comprobantes", 10);

        var headers = new[] { "Fecha", "TC", "Comprobante", "Cuenta", ventas ? "Cliente" : "Proveedor", "Motivo", "Neto", "IVA", "Total", "Usuario" };
        SetHeaders(ws, 3, headers);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int row = i + 4;
            SetDateCell(ws.Cell(row, 1), item.Fecha);
            ws.Cell(row, 2).Value = item.Tc;
            ws.Cell(row, 3).Value = item.IdComprobante;
            ws.Cell(row, 4).Value = item.Cuenta;
            ws.Cell(row, 5).Value = item.RazonSocial;
            ws.Cell(row, 6).Value = item.DescMotivo;
            SetDecimalCell(ws.Cell(row, 7), item.Neto);
            SetDecimalCell(ws.Cell(row, 8), item.Iva);
            SetDecimalCell(ws.Cell(row, 9), item.Total);
            ws.Cell(row, 10).Value = item.Usuario;
            if (i % 2 == 1) SetRowBackground(ws, row, 10);
        }

        ws.Columns().AdjustToContents(8, 60);
        ws.SheetView.FreezeRows(3);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportarCuentaCorriente(IReadOnlyList<CuentaCorrienteFilaDto> items, FiltrosReporteCompras filtros, decimal saldo, bool ventas = false)
    {
        return ExportarCuentaCorrienteVentas(items,filtros,saldo,ventas);
    }

    private static byte[] ExportarCuentaCorrienteVentas(IReadOnlyList<CuentaCorrienteFilaDto> items, FiltrosReporteCompras filtros, decimal movimiento,bool ventas)
    {
        using var wb=new XLWorkbook(); var ws=wb.Worksheets.Add("Cuenta corriente");
        var cuentas=items.GroupBy(x=>x.Cuenta).ToList();
        SetTitulo(ws,ventas ? "Cuenta corriente de clientes" : "Cuenta corriente de proveedores",10);
        SetSubtitulo(ws,$"{DescribirFiltros(filtros,ventas)} · Inicial: {cuentas.Sum(x=>x.First().SaldoInicial):N2} · Movimiento neto del período: {movimiento:N2} · Cierre: {cuentas.Sum(x=>x.First().SaldoFinalCuenta):N2}",10);
        SetHeaders(ws,3,["Cuenta",ventas?"Cliente":"Proveedor","Fecha","TC","Comprobante","Debe","Haber","Saldo parcial","Tipo de línea","Saldo inicial"]);
        var row=4;
        foreach(var cuenta in cuentas)
        {
            var first=cuenta.First();
            ws.Cell(row,1).Value=first.Cuenta;ws.Cell(row,2).Value=first.RazonSocial;
            SetDecimalCell(ws.Cell(row,8),first.SaldoInicial);SetDecimalCell(ws.Cell(row,10),first.SaldoInicial);
            ws.Cell(row,9).Value="Saldo inicial";row++;
            foreach(var x in cuenta.Where(x=>!x.EsSaldoInicial))
            {
                ws.Cell(row,1).Value=x.Cuenta;ws.Cell(row,2).Value=x.RazonSocial;SetDateCell(ws.Cell(row,3),x.Fecha);
                ws.Cell(row,4).Value=x.Tc;ws.Cell(row,5).Value=x.IdComprobante;
                SetDecimalCell(ws.Cell(row,6),x.DebeHaber=="D"?x.Importe:0);
                SetDecimalCell(ws.Cell(row,7),x.DebeHaber=="H"?x.Importe:0);
                SetDecimalCell(ws.Cell(row,8),x.SaldoParcial);ws.Cell(row,9).Value="Movimiento";row++;
            }
            ws.Cell(row,1).Value=first.Cuenta;ws.Cell(row,2).Value=first.RazonSocial;
            ws.Cell(row,9).Value="Saldo al cierre";SetDecimalCell(ws.Cell(row,8),first.SaldoFinalCuenta);
            ws.Range(row,1,row,10).Style.Font.Bold=true;row++;
        }
        ws.Columns().AdjustToContents(8,60);ws.SheetView.FreezeRows(3);
        using var ms=new MemoryStream();wb.SaveAs(ms);return ms.ToArray();
    }

    // ── Nombres de archivo ────────────────────────────────────────────────────

    public byte[] ExportarSaldosVentas(IReadOnlyList<SaldoVentasFilaDto> items,FiltrosReporteCompras filtros,bool controlCredito,bool ventas=true)
    {
        using var wb=new XLWorkbook();var ws=wb.Worksheets.Add(controlCredito?"Control de crédito":"Saldos");
        var columns=controlCredito?7:3;
        SetTitulo(ws,controlCredito?"Saldos Control de Crédito":ventas?"Saldos por cliente":"Saldos por proveedor",columns);
        SetSubtitulo(ws,$"Corte: {(filtros.FechaHasta??DateTime.Today):dd/MM/yyyy} · {DescribirFiltros(filtros,ventas)} · Incluye saldo cero: {(filtros.IncluirSaldosCero?"Sí":"No")}",columns);
        SetHeaders(ws,3,controlCredito ? ["Cuenta","Cliente","Saldo de la selección","Deuda global","Límite actual","Disponible global","Exceso"]
            : ["Cuenta",ventas?"Cliente":"Proveedor","Saldo de la selección"]);
        var row=4;
        foreach(var x in items)
        {
            ws.Cell(row,1).Value=x.Cuenta;ws.Cell(row,2).Value=x.RazonSocial;SetDecimalCell(ws.Cell(row,3),x.Saldo);
            if(controlCredito) { SetDecimalCell(ws.Cell(row,4),x.SaldoGlobal);SetDecimalCell(ws.Cell(row,5),x.LimiteCredito);
                SetDecimalCell(ws.Cell(row,6),x.CreditoDisponible);SetDecimalCell(ws.Cell(row,7),x.Exceso); }
            row++;
        }
        ws.Cell(row,2).Value="Total general";SetDecimalCell(ws.Cell(row,3),items.Sum(x=>x.Saldo));
        if(controlCredito) { SetDecimalCell(ws.Cell(row,4),items.Sum(x=>x.SaldoGlobal));SetDecimalCell(ws.Cell(row,5),items.Sum(x=>x.LimiteCredito));
            SetDecimalCell(ws.Cell(row,6),items.Sum(x=>x.CreditoDisponible));SetDecimalCell(ws.Cell(row,7),items.Sum(x=>x.Exceso)); }
        ws.Range(row,1,row,columns).Style.Font.Bold=true;
        if(controlCredito) ws.Cell(row+2,1).Value="Límites actuales; disponible contra deuda global al corte. Límite cero devuelve cero. No incluye exposición adicional por cheques ni remitos.";
        ws.Columns().AdjustToContents(8,60);ws.SheetView.FreezeRows(3);
        using var ms=new MemoryStream();wb.SaveAs(ms);return ms.ToArray();
    }

    public byte[] ExportarComprobantesCuentaCorriente(IReadOnlyList<ComprobanteCuentaCorrienteDto> items,FiltrosReporteCompras filtros,bool analitico,bool ventas=true)
    {
        using var wb=new XLWorkbook();var ws=wb.Worksheets.Add("Comprobantes");
        SetTitulo(ws,(analitico?"Cuenta corriente analítica":"Cuenta corriente resumida")+(ventas?" de clientes":" de proveedores"),9);
        SetSubtitulo(ws,$"{DescribirFiltros(filtros,ventas)} · Fechas de emisión; aplicaciones actuales",9);
        SetHeaders(ws,3,["Cuenta",ventas?"Cliente":"Proveedor","Emisión","TC","Comprobante","Vencimiento","Original","Aplicado","Pendiente"]);
        var row=4;
        foreach(var d in items)
        {
            ws.Cell(row,1).Value=d.Cuenta;ws.Cell(row,2).Value=d.RazonSocial;SetDateCell(ws.Cell(row,3),d.Fecha);
            ws.Cell(row,4).Value=d.Tc;ws.Cell(row,5).Value=d.IdComprobante;
            if(d.Vencimiento is { } v)SetDateCell(ws.Cell(row,6),v);
            SetDecimalCell(ws.Cell(row,7),d.Importe);SetDecimalCell(ws.Cell(row,8),d.Aplicado);SetDecimalCell(ws.Cell(row,9),d.Pendiente);row++;
        }
        ws.Cell(row,2).Value="Total general";SetDecimalCell(ws.Cell(row,7),items.Sum(x=>x.Importe));
        SetDecimalCell(ws.Cell(row,8),items.Sum(x=>x.Aplicado));SetDecimalCell(ws.Cell(row,9),items.Sum(x=>x.Pendiente));
        ws.Range(row,1,row,9).Style.Font.Bold=true;
        if(analitico)
        {
            var ap=wb.Worksheets.Add("Aplicaciones");
            SetTitulo(ap,"Detalle de aplicaciones por comprobante",10);
            SetSubtitulo(ap,"Cada fila es un importe aplicado; no sumar nuevamente los originales de la hoja Comprobantes.",10);
            SetHeaders(ap,3,["Cuenta","TC origen","Comprobante origen","Id aplicación","Fecha aplicación","TC aplicado","Comprobante aplicado","Importe aplicado","Saldo restante","Control"]);
            var n=4;
            foreach(var d in items)
            {
                foreach(var a in d.Aplicaciones)
                {
                    ap.Cell(n,1).Value=d.Cuenta;ap.Cell(n,2).Value=d.Tc;ap.Cell(n,3).Value=d.IdComprobante;
                    ap.Cell(n,4).Value=a.IdAplicacion;if(a.Fecha is { } fecha)SetDateCell(ap.Cell(n,5),fecha);
                    ap.Cell(n,6).Value=a.Tc;ap.Cell(n,7).Value=a.IdComprobante;
                    SetDecimalCell(ap.Cell(n,8),a.Importe);SetDecimalCell(ap.Cell(n,9),a.SaldoRestante);
                    ap.Cell(n,10).Value=a.ReferenciaAmbigua?"Referencia a revisar":"";n++;
                }
                if(d.DiferenciaAplicaciones!=0)
                {
                    ap.Cell(n,1).Value=d.Cuenta;ap.Cell(n,2).Value=d.Tc;ap.Cell(n,3).Value=d.IdComprobante;
                    ap.Cell(n,10).Value=$"Diferencia entre saldo y aplicaciones: {d.DiferenciaAplicaciones:N4}";n++;
                }
            }
            ap.Columns().AdjustToContents(8,60);ap.SheetView.FreezeRows(3);
        }
        ws.Columns().AdjustToContents(8,60);ws.SheetView.FreezeRows(3);
        using var ms=new MemoryStream();wb.SaveAs(ms);return ms.ToArray();
    }

    public static string NombreArchivoResumen() =>
        $"reporte_compras_resumen_{DateTime.Today:yyyyMMdd}.xlsx";

    public static string NombreArchivoDetalle() =>
        $"reporte_compras_detalle_{DateTime.Today:yyyyMMdd}.xlsx";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SetTitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(1, 1);
        cell.Value = texto;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 12;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
        cell.Style.Font.FontColor = XLColor.White;
        if (colspan > 1) ws.Range(1, 1, 1, colspan).Merge();
    }

    private static void SetSubtitulo(IXLWorksheet ws, string texto, int colspan)
    {
        var cell = ws.Cell(2, 1);
        cell.Value = $"Exportado el {DateTime.Now:dd/MM/yyyy HH:mm} — {texto}";
        cell.Style.Font.Italic = true;
        cell.Style.Font.FontSize = 9;
        cell.Style.Font.FontColor = XLColor.FromHtml("#64748b");
        if (colspan > 1) ws.Range(2, 1, 2, colspan).Merge();
    }

    private static void SetHeaders(IXLWorksheet ws, int row, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0369a1");
        }
    }

    private static void SetDecimalCell(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = "#,##0.00";
    }

    private static void SetDateCell(IXLCell cell, DateTime value)
    {
        cell.Value = value;
        cell.Style.DateFormat.Format = "dd/MM/yyyy";
    }

    private static void SetRowBackground(IXLWorksheet ws, int row, int cols)
    {
        for (int c = 1; c <= cols; c++)
            ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
    }

    public static string DescribirFiltros(FiltrosReporteCompras f,bool ventas)
    {
        var desde = f.FechaDesde?.ToString("dd/MM/yyyy") ?? "—";
        var hasta = f.FechaHasta?.ToString("dd/MM/yyyy") ?? "—";
        var extra = string.IsNullOrWhiteSpace(f.Proveedor) ? string.Empty : $" · Cuenta: {f.Proveedor}";
        var tc    = string.IsNullOrWhiteSpace(f.TipoComprobante) ? string.Empty : $" · TC: {f.TipoComprobante}";
        var cartera = f.SinVendedor ? " · Sin vendedor" : string.IsNullOrWhiteSpace(f.Vendedor) ? "" : $" · Vendedor del cliente: {f.Vendedor}";
        cartera += f.SinZona ? " · Sin zona" : string.IsNullOrWhiteSpace(f.Zona) ? "" : $" · Zona del cliente: {f.Zona}";
        if(f.UnidadesNegocio.Length>0) cartera += " · Unidades: " + string.Join(", ",f.UnidadesNegocio.Select(x=>string.IsNullOrEmpty(x)?"Sin unidad":x));
        if (!string.IsNullOrWhiteSpace(f.CuentaExacta)) cartera += $" · Código exacto: {f.CuentaExacta}";
        if (ventas && !string.IsNullOrWhiteSpace(f.Provincia)) cartera += $" · Provincia del cliente: {f.Provincia}";
        if (ventas) cartera += f.IncluirProforma ? " · Incluye proformas (FP, NCFP, CBFP)" : " · Sin proformas (excluye FP, NCFP, CBFP)";
        return $"Período: {desde} al {hasta}{extra}{tc}{cartera}";
    }
}

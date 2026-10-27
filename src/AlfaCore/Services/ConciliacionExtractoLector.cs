using System.Globalization;
using System.Text;
using AlfaCore.ConciliacionBancaria.Application.Abstractions;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using AlfaCore.ConciliacionBancaria.Infrastructure.Excel;
using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed class ConciliacionExtractoLector(IWebHostEnvironment? environment=null)
{
    public const int MaxBytes = 20 * 1024 * 1024;

    public static string SugerirCuenta(BankSource banco, IReadOnlyList<ConciliacionCuenta> cuentas)
    {
        var nombre = banco switch { BankSource.Provincia => "banco provincia", BankSource.Santander => "banco santander", BankSource.Nacion => "banco nacion", BankSource.Hipotecario => "banco hipotecario", _ => "mercado pago" };
        var principales=cuentas.Where(c=>Normalizar(c.Descripcion).Contains(nombre,StringComparison.Ordinal)).ToList();
        if (principales.Count==1) return principales[0].Codigo;
        if (principales.Count>1) return "";
        var corto=banco==BankSource.MercadoPago ? "mercadopago" : banco.ToString().ToLowerInvariant();
        var candidatas=cuentas.Where(c=>Normalizar(c.Descripcion).Replace(" ","").Contains(corto,StringComparison.Ordinal)).ToList();
        return candidatas.Count==1 ? candidatas[0].Codigo : "";
    }

    public async Task<ConciliacionLectura> LeerAsync(ConciliacionArchivo archivo, BankSource? banco = null, CancellationToken ct = default,string python="python")
    {
        var extension=Path.GetExtension(archivo.Nombre).ToLowerInvariant();
        if (extension is not (".xlsx" or ".xls" or ".csv" or ".pdf")
            || archivo.Contenido.Length is 0 or > MaxBytes)
            throw new InvalidOperationException("Seleccioná un extracto .xlsx, .xls, .csv o .pdf de hasta 20 MB.");
        if(archivo.FechaReferencia is { Year: <1900 })
            throw new InvalidOperationException("Indicá una fecha de referencia válida para la descarga.");
        if(extension==".pdf")
            return await new ConciliacionExtractoPdfLector(environment??throw new InvalidOperationException("No se pudo localizar el lector PDF en esta instalación."))
                .LeerAsync(archivo,banco,python,ct);
        // Se reutilizan los parsers del proyecto original con un archivo temporal privado.
        var temp = Path.Combine(Path.GetTempPath(), $"alfacore-extracto-{Guid.NewGuid():N}.xlsx");
        try
        {
            using var wb=ConciliacionArchivoTabular.Abrir(archivo,ct);
            banco ??= Detectar(wb);
            if (banco is null)
                return new(null, [], "No se pudo reconocer un formato único. Elegí el formato bancario para interpretar el archivo; la cuenta contable se selecciona por separado.");
            var fechaReferencia=archivo.FechaReferencia?.Date ?? FechaArchivo(archivo.Nombre);
            if(banco==BankSource.MercadoPago && fechaReferencia is null)
            {
                var ws=wb.Worksheets.First();
                var columnaFecha=ws.RowsUsed().Take(5).SelectMany(r=>r.CellsUsed())
                    .FirstOrDefault(c=>Normalizar(c.GetString()) is "fecha" or "date");
                if(columnaFecha is not null && ws.Column(columnaFecha.Address.ColumnNumber).CellsUsed()
                    .Where(c=>c.Address.RowNumber>columnaFecha.Address.RowNumber)
                    .Any(c=>MercadoPagoExcelStatementParser.RequiereFechaReferencia(c.GetString())))
                    return new(banco,[],"El archivo contiene fechas relativas o sin año. Confirmá la fecha de referencia y pulsá Procesar archivo.")
                    {
                        RequiereFechaReferencia=true,
                        FechaReferenciaSugerida=wb.Properties.Created.Year>=2000 ? wb.Properties.Created.Date : null
                    };
            }
            IExcelStatementParser parser = banco switch
            {
                BankSource.Provincia => new ProvinciaExcelStatementParser(),
                BankSource.Santander => new SantanderExcelStatementParser(),
                BankSource.MercadoPago => new MercadoPagoExcelStatementParser(fechaReferencia),
                _ => throw new InvalidOperationException("Para Nación e Hipotecario seleccioná el resumen PDF; ese banco no tiene lector Excel o CSV disponible.")
            };
            if(extension==".xlsx") await File.WriteAllBytesAsync(temp,archivo.Contenido,ct);
            else { ct.ThrowIfCancellationRequested();wb.SaveAs(temp); }
            var rows = await parser.ParseAsync(temp, ct);
            if (rows.Count == 0) throw new InvalidOperationException("El archivo no contiene movimientos válidos. Revisá el banco y el formato del extracto.");
            return new(banco, rows, $"{banco}: {rows.Count} movimientos listos para importar."+
                (banco==BankSource.MercadoPago && fechaReferencia.HasValue ? $" Fecha de referencia: {fechaReferencia:dd/MM/yyyy}." : ""));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static DateTime? FechaArchivo(string nombre)
    {
        var match=System.Text.RegularExpressions.Regex.Match(nombre,@"\d{4}-\d{2}-\d{2}");
        return match.Success && DateTime.TryParseExact(match.Value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date) ? date : null;
    }

    public static BankSource? Detectar(XLWorkbook wb)
    {
        var ws = wb.Worksheets.First();
        var detected = new HashSet<BankSource>();
        var titulos = new HashSet<BankSource>();
        var tablaProvincia=false;
        var detalleMovimientos=false;
        var cuentaEnCabecera=false;
        foreach (var row in ws.RowsUsed().Take(40))
        {
            var cells = row.CellsUsed().Select(c => Normalizar(c.GetString())).ToHashSet();
            if(row.RowNumber()<=5)
            {
                detalleMovimientos |= cells.Contains("detalle de movimientos");
                cuentaEnCabecera |= cells.Any(c=>c.StartsWith("cuenta:",StringComparison.Ordinal));
            }
            if(cells.Contains("fecha") && cells.Contains("descripcion") && cells.Contains("importe") && cells.Contains("saldo"))
                tablaProvincia=true;
            if (cells.Contains("numero secuencia") || cells.Contains("nro. secuencia")) detected.Add(BankSource.Provincia);
            if (cells.Contains("caja de ahorro") || cells.Contains("cuenta corriente")) detected.Add(BankSource.Santander);
            if (cells.Contains("monto") && (cells.Contains("tipo") && (cells.Contains("medio") || cells.Contains("medio de pago"))
                || cells.Contains("tipo de operacion") || cells.Contains("numero de referencia"))) detected.Add(BankSource.MercadoPago);
            foreach (var cell in cells.Where(_ => row.RowNumber() <= 5))
            {
                if (cell.Contains("banco provincia")) titulos.Add(BankSource.Provincia);
                if (cell.Contains("santander")) titulos.Add(BankSource.Santander);
                if (cell.Contains("mercado pago")) titulos.Add(BankSource.MercadoPago);
            }
        }
        // Una transferencia puede mencionar otro banco: prevalece el formato de la tabla.
        if (detected.Count == 0) detected = titulos;
        if(detected.Count==0 && tablaProvincia && detalleMovimientos && cuentaEnCabecera) detected.Add(BankSource.Provincia);
        return detected.Count == 1 ? detected.Single() : null;
    }

    private static string Normalizar(string s) => string.Concat(s.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).Trim().ToLowerInvariant();
}

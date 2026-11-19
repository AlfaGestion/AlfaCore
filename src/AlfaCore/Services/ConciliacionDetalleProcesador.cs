using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace AlfaCore.Services;

public sealed record DetalleOperacion(string Codigo,DateTime? FechaOperacion,DateTime? FechaAcreditacion,string MedioPago,
    string Estado,decimal Bruto,decimal Comision,decimal IvaComision,decimal Neto);

public sealed record DetalleLectura(string Procesador,IReadOnlyList<DetalleOperacion> Operaciones)
{
    public DateTime? Desde=>Operaciones.Count==0?null:Operaciones.Min(o=>(o.FechaAcreditacion??o.FechaOperacion)!.Value.Date);
    public DateTime? Hasta=>Operaciones.Count==0?null:Operaciones.Max(o=>(o.FechaAcreditacion??o.FechaOperacion)!.Value.Date);
}

public sealed record DetalleEmparejamiento(long DetalleId,string Procesador,int Operaciones,int Emparejadas,int NetoDistinto,
    int OperacionesSinCredito,int CreditosSinOperacion,decimal Bruto,decimal Comision,decimal IvaComision,decimal Neto,
    IReadOnlyList<DetalleMedio> PorMedio,IReadOnlyList<string> Avisos);

public sealed record DetalleMedio(string MedioPago,int Operaciones,decimal Bruto,decimal Comision,decimal IvaComision,decimal Neto);

/// <summary>
/// Lectores de reportes de operaciones de procesadores de cobro. Cada procesador se reconoce por sus columnas;
/// para sumar uno nuevo se agrega un perfil a <see cref="Perfiles"/> (nombres de columna con alternativas).
/// </summary>
public static class ConciliacionDetalleProcesador
{
    private sealed record Perfil(string Procesador,string Codigo,string FechaOperacion,string FechaAcreditacion,string MedioPago,
        string Estado,string Bruto,string Comision,string Iva,string Neto);

    private static readonly Perfil[] Perfiles=
    [
        new("NAVE","codigo de operacion","fecha de operacion","fecha de acreditacion","medio de pago","estado",
            "monto bruto","comision","iva comision","monto neto"),
    ];

    public static DetalleLectura Leer(byte[] contenido)
    {
        XLWorkbook libro;
        try { libro=new XLWorkbook(new MemoryStream(contenido)); }
        catch (Exception) { throw new InvalidOperationException("No se pudo abrir el Excel del detalle de operaciones."); }
        using(libro)
        {
            var hoja=libro.Worksheets.FirstOrDefault()??throw new InvalidOperationException("El Excel no tiene hojas.");
            var ultima=Math.Min(hoja.LastRowUsed()?.RowNumber()??0,20000);
            for(var fila=1;fila<=Math.Min(ultima,80);fila++)
            {
                var celdas=hoja.Row(fila).CellsUsed().ToDictionary(c=>Normalizar(c.GetString()),c=>c.Address.ColumnNumber,StringComparer.Ordinal);
                foreach(var p in Perfiles)
                {
                    if(!celdas.ContainsKey(p.Codigo)||!celdas.ContainsKey(p.Bruto)||!celdas.ContainsKey(p.Neto)) continue;
                    return LeerFilas(hoja,fila,ultima,p,celdas);
                }
            }
        }
        throw new InvalidOperationException("No reconozco el formato del detalle. Hoy se admite el reporte \"Detalle de operaciones\" de NAVE.");
    }

    private static DetalleLectura LeerFilas(IXLWorksheet hoja,int cabecera,int ultima,Perfil p,Dictionary<string,int> col)
    {
        int Col(string nombre)=>col.TryGetValue(nombre,out var c)?c:0;
        var ops=new List<DetalleOperacion>();
        for(var f=cabecera+1;f<=ultima;f++)
        {
            var codigo=hoja.Cell(f,Col(p.Codigo)).GetString().Trim();
            if(codigo.Length==0) continue; // fila de totales o vacía
            // Un código puede repetirse: el pago y su devolución comparten código.
            var bruto=Importe(hoja.Cell(f,Col(p.Bruto)));
            var neto=Importe(hoja.Cell(f,Col(p.Neto)));
            var comision=Col(p.Comision)>0?Importe(hoja.Cell(f,Col(p.Comision))):0m;
            var iva=Col(p.Iva)>0?Importe(hoja.Cell(f,Col(p.Iva))):0m;
            if(bruto-comision-iva!=neto)
                throw new InvalidOperationException($"La operación {codigo} (fila {f}) no cierra: bruto - comisión - IVA no es igual al neto.");
            ops.Add(new(codigo,Fecha(hoja.Cell(f,Col(p.FechaOperacion))),Fecha(hoja.Cell(f,Col(p.FechaAcreditacion))),
                Col(p.MedioPago)>0?hoja.Cell(f,Col(p.MedioPago)).GetString().Trim():"",
                Col(p.Estado)>0?hoja.Cell(f,Col(p.Estado)).GetString().Trim():"",bruto,comision,iva,neto));
        }
        if(ops.Count==0) throw new InvalidOperationException("El detalle no contiene operaciones.");
        if(ops.Count>20000) throw new InvalidOperationException("El detalle supera las 20.000 operaciones.");
        if(ops.Any(o=>o.FechaOperacion is null&&o.FechaAcreditacion is null))
            throw new InvalidOperationException("Hay operaciones sin fecha en el detalle.");
        return new(p.Procesador,ops);
    }

    public static string Normalizar(string? texto)
    {
        var d=(texto??"").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb=new StringBuilder(d.Length);
        foreach(var ch in d) if(CharUnicodeInfo.GetUnicodeCategory(ch)!=UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return string.Join(' ',sb.ToString().Split(' ',StringSplitOptions.RemoveEmptyEntries));
    }

    private static decimal Importe(IXLCell c)
    {
        if(c.DataType==XLDataType.Number) return Math.Round((decimal)c.GetDouble(),2);
        var s=c.GetString().Trim();
        if(s.Length==0) return 0m;
        if(decimal.TryParse(s,NumberStyles.Number|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var v)) return Math.Round(v,2);
        throw new InvalidOperationException($"Importe no válido en {c.Address}: \"{s}\".");
    }

    private static DateTime? Fecha(IXLCell c)
    {
        if(c.DataType==XLDataType.DateTime) return c.GetDateTime();
        var s=c.GetString().Trim();
        if(s.Length==0) return null;
        string[] formatos=["dd/MM/yyyy HH:mm","dd/MM/yyyy HH:mm:ss","dd/MM/yyyy","yyyy-MM-dd","yyyy-MM-dd HH:mm:ss"];
        return DateTime.TryParseExact(s,formatos,CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d
            :throw new InvalidOperationException($"Fecha no válida en {c.Address}: \"{s}\".");
    }
}

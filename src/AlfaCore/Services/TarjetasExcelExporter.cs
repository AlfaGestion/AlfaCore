using System.Globalization;
using AlfaCore.Models;
using ClosedXML.Excel;

namespace AlfaCore.Services;

internal static class TarjetasExcelExporter
{
    internal const string Advertencia="Este informe concilia presentaciones con liquidaciones de tarjetas; no confirma la acreditación bancaria.";
    private const string Moneda="#,##0.00;[Red]-#,##0.00";
    internal static byte[] Crear(TarjetasDatosExportacion datos,CancellationToken ct=default)
    {
        using var libro=new XLWorkbook();
        var c=datos.Comparacion;var s=datos.Solicitud;var i=c.Importacion;
        var resumen=libro.Worksheets.Add("Resumen");var fila=1;
        void Texto(string titulo,string valor){resumen.Cell(fila,1).Value=titulo;Escribir(resumen.Cell(fila++,2),valor);}
        void Numero(string titulo,decimal valor){resumen.Cell(fila,1).Value=titulo;resumen.Cell(fila,2).Value=valor;resumen.Cell(fila++,2).Style.NumberFormat.Format=Moneda;}
        void Fecha(string titulo,DateTime valor,string formato="dd/MM/yyyy"){resumen.Cell(fila,1).Value=titulo;resumen.Cell(fila,2).Value=valor;resumen.Cell(fila++,2).Style.NumberFormat.Format=formato;}
        Texto("Informe","Conciliación de tarjetas asistida con IA");Texto("Alcance del control",Advertencia);
        Texto("Base",datos.Base);Texto("Usuario de exportación",datos.Usuario);Fecha("Exportado (UTC)",datos.GeneradoUtc,"dd/MM/yyyy HH:mm:ss");
        Texto("Archivo original",i.Nombre);Texto("Importación",i.Id.ToString(CultureInfo.InvariantCulture));Texto("Banco",i.Banco);
        Texto("Tarjeta",i.Tarjeta);Texto("Comercio",i.Comercio);Texto("Cuenta contable",i.Cuenta);Texto("Moneda","Pesos");
        Fecha("Operaciones del PDF desde",i.Desde);Fecha("Operaciones del PDF hasta",i.Hasta);
        Numero("Bruto total del archivo",i.Bruto);Numero("Neto total del archivo (informativo)",i.Neto);
        Texto("Modalidad",s.Alcance switch{TarjetasExportacionAlcance.Pendientes=>"Solo pendientes",TarjetasExportacionAlcance.Conciliados=>"Solo conciliados",_=>"Comparación actual"});
        Fecha("Período de Alfa desde",s.Filtro.Desde);Fecha("Período de Alfa hasta",s.Filtro.Hasta);
        Texto("Sucursal general de Alfa",s.Filtro.Sucursal.Length==0?"Todas":s.Filtro.Sucursal);
        Texto("Estado exportado",s.Filtro.Estado);Texto("Búsqueda aplicada",s.Filtro.Buscar);
        Texto("Fecha local de liquidación",s.FechaLiquidacion.Length==0?"Todas":s.FechaLiquidacion);
        Texto("Fecha local de Alfa",s.FechaAlfa.Length==0?"Todas":s.FechaAlfa);
        Texto("Sucursal local de Alfa",s.SucursalAlfa.Length==0?"Todas":s.SucursalAlfa=="__sin_sucursal__"?"Sin sucursal":s.SucursalAlfa);
        Texto("Solo seleccionados",s.SoloSeleccionados?"Sí":"No");
        Texto("Registros exportados",$"Liquidación: {c.Detalles.Count}; Alfa: {c.Movimientos.Count}. Incluye todas las páginas del alcance elegido.");
        Numero("Bruto exportado",c.Detalles.Sum(d=>d.Importe));Numero("Alfa exportado",c.Movimientos.Sum(m=>m.Importe));
        Numero("Diferencia del alcance exportado",c.Detalles.Sum(d=>d.Importe)-c.Movimientos.Sum(m=>m.Importe));
        Numero("Liquidación conciliada exportada",c.Detalles.Where(d=>d.Vinculo.HasValue).Sum(d=>d.Importe));
        Numero("Liquidación pendiente exportada",c.Detalles.Where(d=>!d.Vinculo.HasValue).Sum(d=>d.Importe));
        Numero("Alfa conciliado en AlfaCore exportado",c.Movimientos.Where(m=>m.Vinculo.HasValue).Sum(m=>m.Importe));
        Numero("Alfa liquidado en sistema anterior exportado",c.Movimientos.Where(m=>!m.Vinculo.HasValue&&m.LegacyLiquidado).Sum(m=>m.Importe));
        Numero("Alfa pendiente exportado",c.Movimientos.Where(m=>!m.Vinculo.HasValue&&!m.LegacyLiquidado).Sum(m=>m.Importe));
        Texto("Movimientos modificados",c.Movimientos.Count(m=>m.Alterado).ToString(CultureInfo.InvariantCulture));
        Texto("Extracción del PDF",i.DetalleCompleto?"Detalle validado contra el bruto":"Revisar extracción; no apta para confirmar");
        Texto("Avisos",i.Avisos);Texto("Vínculos parciales","Las hojas de detalle incluyen solo filas autorizadas dentro de los filtros. En Conciliaciones, los datos confirmados y de auditoría se muestran únicamente para vínculos completos de esta importación.");
        resumen.Column(1).Width=46;resumen.Column(2).Width=100;resumen.Column(2).Style.Alignment.WrapText=true;
        resumen.Range(1,1,1,2).Style.Font.Bold=true;resumen.SheetView.FreezeRows(2);resumen.RowsUsed().AdjustToContents();

        var liquidacion=Hoja(libro,"Liquidacion",["Id movimiento","Fecha operación","Fecha pago","Liquidación","Lote","Cupón","Terminal","Nivel","Cantidad","Bruto","Estado","Conciliación"]);
        fila=2;
        foreach(var d in c.Detalles)
        {
            ct.ThrowIfCancellationRequested();Escribir(liquidacion.Cell(fila,1),d.Id.ToString(CultureInfo.InvariantCulture));
            Dia(liquidacion.Cell(fila,2),d.Fecha);if(d.FechaPago is {} pago)Dia(liquidacion.Cell(fila,3),pago);
            Escribir(liquidacion.Cell(fila,4),d.Liquidacion);Escribir(liquidacion.Cell(fila,5),d.Lote);Escribir(liquidacion.Cell(fila,6),d.Cupon);
            Escribir(liquidacion.Cell(fila,7),d.Terminal);Escribir(liquidacion.Cell(fila,8),d.Nivel);liquidacion.Cell(fila,9).Value=d.Cantidad;
            Importe(liquidacion.Cell(fila,10),d.Importe);Escribir(liquidacion.Cell(fila,11),d.Vinculo.HasValue?"Conciliado en AlfaCore":"Pendiente");
            if(d.Vinculo is {} id)Escribir(liquidacion.Cell(fila,12),id.ToString(CultureInfo.InvariantCulture));fila++;
        }
        Terminar(liquidacion,fila,12,10,c.Detalles.Sum(d=>d.Importe));
        var alfa=Hoja(libro,"Alfa",["Cuenta","Clave movimiento","Fecha","Comprobante","Sucursal","Detalle","Presentación","Cupón","Importe","Estado","Conciliación","Modificado después de conciliar"]);
        fila=2;
        foreach(var m in c.Movimientos)
        {
            ct.ThrowIfCancellationRequested();Escribir(alfa.Cell(fila,1),m.Cuenta);Escribir(alfa.Cell(fila,2),m.Clave);Dia(alfa.Cell(fila,3),m.Fecha);
            Escribir(alfa.Cell(fila,4),m.Comprobante);Escribir(alfa.Cell(fila,5),m.Sucursal);Escribir(alfa.Cell(fila,6),m.Detalle);
            Escribir(alfa.Cell(fila,7),m.Presentacion.ToString(CultureInfo.InvariantCulture));Escribir(alfa.Cell(fila,8),m.Cupon);Importe(alfa.Cell(fila,9),m.Importe);
            Escribir(alfa.Cell(fila,10),m.Vinculo.HasValue?"Conciliado en AlfaCore":m.LegacyLiquidado?"Liquidado en sistema anterior":"Pendiente");
            if(m.Vinculo is {} id)Escribir(alfa.Cell(fila,11),id.ToString(CultureInfo.InvariantCulture));Escribir(alfa.Cell(fila,12),m.Alterado?"Sí":"No");fila++;
        }
        Terminar(alfa,fila,12,9,c.Movimientos.Sum(m=>m.Importe));

        var conciliaciones=Hoja(libro,"Conciliaciones",["Conciliación","Alcance del vínculo","Liquidación exportada (filas)","Alfa exportado (filas)","Bruto exportado","Alfa exportado","Diferencia exportada","Bruto confirmado","Alfa confirmado","Diferencia confirmada","Usuario confirmación","Confirmado (UTC)","Motivo","Detalle"]);
        var metadatos=datos.Vinculos.ToDictionary(v=>v.Id);
        var porDetalle=c.Detalles.Where(d=>d.Vinculo.HasValue).GroupBy(d=>d.Vinculo!.Value).ToDictionary(g=>g.Key,g=>g.ToList());
        var porAlfa=c.Movimientos.Where(m=>m.Vinculo.HasValue).GroupBy(m=>m.Vinculo!.Value).ToDictionary(g=>g.Key,g=>g.ToList());
        fila=2;
        foreach(var id in porDetalle.Keys.Union(porAlfa.Keys).OrderBy(id=>id))
        {
            ct.ThrowIfCancellationRequested();var detalles=porDetalle.GetValueOrDefault(id)??[];var movimientos=porAlfa.GetValueOrDefault(id)??[];
            metadatos.TryGetValue(id,out var v);
            var completo=v is not null&&v.ImportacionId==i.Id&&detalles.Count>0&&movimientos.Count>0
                &&detalles.Count==v.CantidadDetalles&&movimientos.Count==v.CantidadMovimientos&&!movimientos.Any(m=>m.Alterado);
            Escribir(conciliaciones.Cell(fila,1),id.ToString(CultureInfo.InvariantCulture));
            Escribir(conciliaciones.Cell(fila,2),completo?"Completo":"Parcial o modificado: revisar alcance");
            conciliaciones.Cell(fila,3).Value=detalles.Count;conciliaciones.Cell(fila,4).Value=movimientos.Count;
            Importe(conciliaciones.Cell(fila,5),detalles.Sum(d=>d.Importe));Importe(conciliaciones.Cell(fila,6),movimientos.Sum(m=>m.Importe));
            Importe(conciliaciones.Cell(fila,7),detalles.Sum(d=>d.Importe)-movimientos.Sum(m=>m.Importe));
            if(completo)
            {
                Importe(conciliaciones.Cell(fila,8),v!.ImporteResumen);Importe(conciliaciones.Cell(fila,9),v.ImporteAlfa);
                Importe(conciliaciones.Cell(fila,10),v.ImporteResumen-v.ImporteAlfa);Escribir(conciliaciones.Cell(fila,11),v.Usuario);
                Dia(conciliaciones.Cell(fila,12),v.FechaUtc,"dd/MM/yyyy HH:mm:ss");Escribir(conciliaciones.Cell(fila,13),v.Motivo);
            }
            Escribir(conciliaciones.Cell(fila,14),"Filtrá este número de conciliación en las hojas Liquidacion y Alfa para ver sus movimientos exportados.");fila++;
        }
        Terminar(conciliaciones,fila,14);
        ct.ThrowIfCancellationRequested();using var salida=new MemoryStream();libro.SaveAs(salida);ct.ThrowIfCancellationRequested();return salida.ToArray();
    }

    private static IXLWorksheet Hoja(XLWorkbook libro,string nombre,string[] columnas)
    {
        var hoja=libro.Worksheets.Add(nombre);
        for(var col=0;col<columnas.Length;col++)hoja.Cell(1,col+1).Value=columnas[col];
        var cabecera=hoja.Range(1,1,1,columnas.Length);cabecera.Style.Font.Bold=true;cabecera.Style.Fill.BackgroundColor=XLColor.FromHtml("#DDEBF7");
        cabecera.Style.Alignment.WrapText=true;hoja.Row(1).Height=30;
        hoja.SheetView.FreezeRows(1);return hoja;
    }
    private static void Terminar(IXLWorksheet hoja,int siguiente,int columnas,int columnaImporte=0,decimal total=0)
    {
        hoja.Range(1,1,Math.Max(1,siguiente-1),columnas).SetAutoFilter();hoja.Columns(1,columnas).AdjustToContents();
        foreach(var columna in hoja.Columns(1,columnas))columna.Width=Math.Clamp(columna.Width,12,55);
        if(columnaImporte>0){hoja.Cell(siguiente+1,1).Value="Total exportado";Importe(hoja.Cell(siguiente+1,columnaImporte),total);hoja.Row(siguiente+1).Style.Font.Bold=true;}
    }
    private static void Escribir(IXLCell celda,string texto)
    {
        if(texto.Length>32767)throw new InvalidOperationException("Un detalle supera el límite de texto de Excel. Acotá la consulta antes de exportar.");
        celda.Style.NumberFormat.Format="@";celda.Value=texto;
    }
    private static void Dia(IXLCell celda,DateTime valor,string formato="dd/MM/yyyy"){celda.Value=valor;celda.Style.NumberFormat.Format=formato;}
    private static void Importe(IXLCell celda,decimal valor){celda.Value=valor;celda.Style.NumberFormat.Format=Moneda;}
}

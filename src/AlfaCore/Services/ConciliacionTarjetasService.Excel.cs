using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    /// <summary>Un .xlsx es un ZIP: empieza con "PK". Un PDF empieza con "%PDF".</summary>
    internal static bool EsExcel(byte[] contenido)
        => contenido.Length > 4 && contenido[0] == 0x50 && contenido[1] == 0x4B && contenido[2] == 0x03 && contenido[3] == 0x04;

    /// <summary>
    /// Lee el documento a conciliar: el Excel «Detalle de operaciones» del procesador (NAVE) o el PDF de liquidación/extracto.
    /// El Excel trae el bruto de cada operación, que es lo que se compara con las ventas registradas en Alfa.
    /// </summary>
    private Task<TarjetasLectura> LeerDocumentoAsync(ConciliacionArchivo archivo, string python, CancellationToken ct)
        => EsExcel(archivo.Contenido) ? Task.FromResult(LecturaDesdeExcel(archivo.Contenido)) : lector.LeerAsync(archivo, python, ct);

    internal static TarjetasLectura LecturaDesdeExcel(byte[] contenido)
    {
        var det = ConciliacionDetalleProcesador.Leer(contenido);
        var ops = det.Operaciones.Where(o => o.Bruto != 0).ToList();
        if (ops.Count == 0) throw new InvalidOperationException("El Excel no tiene operaciones para conciliar.");
        var lectura = new TarjetasLectura
        {
            Banco = "Galicia", Comercio = det.Procesador, Tarjeta = det.Procesador,
            Bruto = ops.Sum(o => o.Bruto), Neto = ops.Sum(o => o.Neto), DetalleCompleto = true
        };
        lectura.TotalDetalle = lectura.Bruto;
        foreach (var o in ops)
        {
            var fecha = (o.FechaOperacion ?? o.FechaAcreditacion)!.Value.Date;
            lectura.Detalles.Add(new TarjetasDetalle
            {
                Fecha = fecha, FechaPago = o.FechaAcreditacion?.Date ?? fecha, Liquidacion = o.Codigo, Lote = "", Cupon = "",
                Terminal = det.Procesador, Nivel = "Cupón", Cantidad = 1, Importe = o.Bruto
            });
        }
        var com = ops.Sum(o => o.Comision); var iva = ops.Sum(o => o.IvaComision);
        lectura.Avisos.Add($"Excel {det.Procesador}: los importes son el BRUTO de cada operación ({ops.Count} operaciones). Comisión {com:N2} + IVA {iva:N2} = neto acreditado {lectura.Neto:N2}. Cada fila es un cobro individual sin número de cupón; se concilia por fecha e importe.");
        return lectura;
    }
}

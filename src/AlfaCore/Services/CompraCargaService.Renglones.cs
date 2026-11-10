using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    /// <summary>
    /// Convierte los renglones leídos por la IA en artículos de la compra (como FrmPersonalizarCompra del legacy). Los que se emparejan pasan
    /// al borrador; los demás quedan en la lista que devuelve para que el operador elija un artículo, los dé de alta o los pase a concepto.
    /// Sin proveedor identificado o sin renglones se conserva el resumen fiscal de servicios / gastos.
    /// </summary>
    internal async Task<IReadOnlyList<CompraRenglonResuelto>> AplicarRenglonesLeidosAsync(SqlConnection c,InterfacesCompraIaResultadoDto lectura,CompraCargaBorrador d,List<string> avisos,CancellationToken t)
    {
        if(lectura.Items.Count==0||d.Tipo is not("FCC" or "NCC" or "NDC"))return [];
        if(d.Proveedor.Length==0)
        {avisos.Add("No se identificó el proveedor: elegilo y volvé a abrir el documento para emparejar los renglones con artículos.");return [];}
        var contexto=await Contexto(c,t);
        var renglones=await ResolverRenglonesAsync(c,lectura.Items,d.Proveedor,contexto.Unidad,contexto.Depositos.FirstOrDefault()?.Codigo??"",contexto.TasasFiscales.FirstOrDefault(),t);
        d.Lineas.Clear();
        // Estos avisos hablaban del resumen fiscal que reemplazan los artículos.
        avisos.RemoveAll(a=>a.StartsWith("Se precarga el resumen fiscal",StringComparison.Ordinal)||a.StartsWith("Hay varias alícuotas",StringComparison.Ordinal)||a.StartsWith("La lectura no informa bases fiscales",StringComparison.Ordinal));
        foreach(var r in renglones)
        {
            if(r.Articulo is {} a)
            {
                r.Cantidad=decimal.Round(r.Cantidad,4);
                if(r.Cantidad is <=0 or >9999999||r.Costo is <=0 or >999999999)
                {r.Motivo=$"El renglón no informa cantidad o costo utilizables para el artículo {a.Codigo}.";r.Candidatos.Insert(0,new(a,r.Puntaje,r.Metodo));r.Articulo=null;continue;}
                d.Lineas.Add(LineaDeRenglon(r,a,d.Letra));
            }
        }
        var pendientes=renglones.Count(r=>!r.Resuelto);
        avisos.Add(pendientes==0
            ?$"Se emparejaron los {renglones.Count} renglones con artículos del maestro. Revisá cantidades, costos y totales contra el original."
            :$"Se emparejaron {renglones.Count-pendientes} de {renglones.Count} renglones. Resolvé los {pendientes} restantes (elegir artículo, alta o concepto) antes de registrar.");
        if(renglones.Any(r=>r.Aviso.Length>0))avisos.Add("Hay renglones cuyo importe no cierra con cantidad × costo: están marcados para revisar.");
        return renglones;
    }

    /// <summary>Línea de compra con artículo a partir de un renglón resuelto (cantidad y costo ya llevados a la unidad del artículo).</summary>
    internal static CompraCargaLinea LineaDeRenglon(CompraRenglonResuelto r,CompraArticulo a,string letra)
    {
        var cantidad=decimal.Round(r.Cantidad,4);var costo=decimal.Round(r.Costo,4);
        var nota=new List<string>();
        if(r.CodigoProveedor.Length>0)nota.Add("Cód. proveedor "+r.CodigoProveedor);
        if(r.Metodo.Length>0)nota.Add($"{r.Metodo} ({r.Puntaje:0})");
        if(r.UnidadesPorBulto>1)nota.Add($"{r.UnidadesPorBulto:0.####} u/bulto"+(r.OrigenBulto.Length>0?$" ({r.OrigenBulto})":""));
        if(r.Aviso.Length>0)nota.Add("⚠ "+r.Aviso);
        return new CompraCargaLinea{Articulo=a.Codigo,ArticuloHuella=a.Huella,UnidadArticulo=a.Unidad,Cantidad=cantidad,CostoUnitario=costo,
            Neto=CompraCargaPlanificador.Moneda(cantidad*costo),Descripcion=a.Descripcion,Cuenta=a.Cuenta,Tratamiento=a.Exento?"Exento":"Gravado",
            Tasa=a.Exento||letra=="C"?0:a.Tasa,CodigoProveedor=r.CodigoProveedor,UnidadesPorBulto=r.UnidadesPorBulto,Nota=string.Join(" · ",nota)};
    }
}

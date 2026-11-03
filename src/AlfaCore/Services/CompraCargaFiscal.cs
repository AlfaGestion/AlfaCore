using AlfaCore.Models;

namespace AlfaCore.Services;

// Mantiene el orden de PIVA/PIVADIF/PIVADIF1/PIVADIF2 en los campos legacy del libro.
internal sealed record CompraCargaFiscal(decimal Gravado,decimal NoGravado,decimal[] Tasas,decimal[] Importes)
{
    internal static CompraCargaFiscal Crear(CompraCargaBorrador d,CompraCargaVista v,IReadOnlyList<decimal> tasas)
    {
        var alicuotas=new decimal[4];var importes=new decimal[4];
        if(tasas.Where(t=>t>0).GroupBy(t=>t).Any(g=>g.Count()>1))throw new InvalidOperationException("Hay alícuotas repetidas en las posiciones fiscales de la configuración.");
        foreach(var grupo in v.Bases.Where(b=>b.Iva!=0).GroupBy(b=>b.Tasa))
        {
            var posicion=tasas.ToList().IndexOf(grupo.Key);
            if(posicion<0||posicion>=4)throw new InvalidOperationException("La alícuota no tiene posición fiscal configurada.");
            alicuotas[posicion]=grupo.Key;importes[posicion]=grupo.Sum(b=>b.Iva);
        }
        return new(v.Bases.Where(b=>b.Tratamiento=="Gravado").Sum(b=>b.Neto),v.Bases.Where(b=>b.Tratamiento!="Gravado").Sum(b=>b.Neto),alicuotas,importes);
    }
}

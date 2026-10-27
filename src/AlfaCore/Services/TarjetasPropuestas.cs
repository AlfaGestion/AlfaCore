using AlfaCore.Models;

namespace AlfaCore.Services;

public static class TarjetasPropuestas
{
    // Se evalúa todo el resultado consultado, no solo las filas de la página visible.
    public static IReadOnlyList<(long Detalle,string Clave)> Unicas(TarjetasComparacion datos)
    {
        var alfa=datos.Movimientos.Where(x=>x.Vinculo is null&&!x.LegacyLiquidado&&!x.Alterado).GroupBy(x=>(x.Fecha.Date,x.Importe)).ToDictionary(g=>g.Key,g=>g.ToList());
        var detalles=datos.Detalles.Where(x=>x.Vinculo is null).GroupBy(x=>(x.Fecha.Date,x.Importe));
        var result=new List<(long,string)>();
        foreach(var grupo in detalles)
        {
            if(result.Count==2000)break;
            if(grupo.Count()==1&&alfa.TryGetValue(grupo.Key,out var candidatos)&&candidatos.Count==1)
                result.Add((grupo.Single().Id,candidatos[0].Clave));
        }
        return result;
    }
}

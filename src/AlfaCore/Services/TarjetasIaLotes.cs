using System.Text.Json;
using AlfaCore.Models;

namespace AlfaCore.Services;

internal static class TarjetasIaLotes
{
    internal static IReadOnlyList<TarjetasComparacion> Preparar(TarjetasComparacion datos,int dias,int maximo)
    {
        var pendientes=datos with
        {
            Detalles=datos.Detalles.Where(d=>d.Vinculo is null).ToList(),
            Movimientos=datos.Movimientos.Where(m=>m.Vinculo is null&&!m.LegacyLiquidado&&!m.Alterado).ToList()
        };
        if(pendientes.Detalles.Count<=maximo&&pendientes.Movimientos.Count<=maximo)return [pendientes];
        var lotes=new List<TarjetasComparacion>();var actuales=new List<TarjetasDetalle>();
        List<TarjetasMovimiento> Candidatos(IReadOnlyList<TarjetasDetalle> detalles)
        {
            var desde=detalles.Min(d=>d.Fecha).Date.AddDays(-dias);var hasta=detalles.Max(d=>d.Fecha).Date.AddDays(dias);
            return pendientes.Movimientos.Where(m=>m.Fecha.Date>=desde&&m.Fecha.Date<=hasta).ToList();
        }
        foreach(var fecha in pendientes.Detalles.GroupBy(d=>d.Fecha.Date).OrderBy(g=>g.Key))
        {
            var dia=fecha.ToList();var candidatosDia=Candidatos(dia);
            if(candidatosDia.Count==0)
            {
                lotes.Add(pendientes with{Detalles=dia,Movimientos=[]});
                continue;
            }
            // No partir un día ni recortar candidatos arbitrariamente: podría perder una agrupación válida.
            if(dia.Count>maximo||candidatosDia.Count>maximo)
                throw new InvalidOperationException($"El día {fecha.Key:dd/MM/yyyy} reúne {dia.Count} registros de liquidación y {candidatosDia.Count} candidatos de Alfa con el margen de fechas. Supera {maximo} por lado en un lote: filtrá por sucursal o conciliá parte de ese día antes de reintentar. No se envió una muestra parcial.");
            var combinados=actuales.Concat(dia).ToList();
            if(actuales.Count>0&&(combinados.Count>maximo||Candidatos(combinados).Count>maximo))
            {
                lotes.Add(pendientes with{Detalles=actuales,Movimientos=Candidatos(actuales)});
                actuales=dia;
            }
            else actuales=combinados;
        }
        if(actuales.Count>0)lotes.Add(pendientes with{Detalles=actuales,Movimientos=Candidatos(actuales)});
        return lotes;
    }

    internal static TarjetasIaResultado Unir(IReadOnlyList<TarjetasComparacion> lotes,IReadOnlyList<TarjetasIaResultado> resultados,
        TarjetasComparacion original,int dias)
    {
        var propuestas=resultados.SelectMany(r=>r.Propuestas).GroupBy(p=>JsonSerializer.Serialize(new
        {
            Detalles=p.Detalles.Select(d=>d.Id).Order().ToArray(),Movimientos=p.Movimientos.Select(m=>m.Clave).Order(StringComparer.Ordinal).ToArray()
        })).Select(g=>g.First()).ToList();
        var repetidosDetalle=propuestas.SelectMany(p=>p.Detalles).GroupBy(d=>d.Id).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        var repetidosAlfa=propuestas.SelectMany(p=>p.Movimientos).GroupBy(m=>m.Clave).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        var validas=propuestas.Where(p=>!p.Detalles.Any(d=>repetidosDetalle.Contains(d.Id))&&!p.Movimientos.Any(m=>repetidosAlfa.Contains(m.Clave))).ToList();
        var enviados=lotes.Where(l=>l.Detalles.Count>0&&l.Movimientos.Count>0).ToList();
        var detalles=enviados.SelectMany(l=>l.Detalles).Select(d=>d.Id).Distinct().Count();
        var movimientos=enviados.SelectMany(l=>l.Movimientos).Select(m=>m.Clave).Distinct().Count();
        return new(string.Join(", ",resultados.Select(r=>r.Modelo).Distinct()),detalles,movimientos,
            resultados.Sum(r=>r.Descartadas)+propuestas.Count-validas.Count,dias,validas)
        {
            LotesAnalizados=enviados.Count,
            DetallesSinCandidatosPorFecha=original.Detalles.Count(d=>d.Vinculo is null)-detalles,
            MovimientosSinCandidatosPorFecha=original.Movimientos.Count(m=>m.Vinculo is null&&!m.LegacyLiquidado&&!m.Alterado)-movimientos
        };
    }
}

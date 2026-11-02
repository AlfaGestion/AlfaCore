using System.Diagnostics;
using AlfaCore.Models;

namespace AlfaCore.Services;

internal sealed record TarjetasObjetivoAutomatico(int Id,IReadOnlyList<TarjetasDetalle> Detalles)
{
    public int Cantidad=>Detalles.Sum(d=>d.Cantidad);
    public decimal Total=>Detalles.Sum(d=>d.Importe);
    public DateTime Fecha=>Detalles[0].Fecha.Date;
    public string Descripcion=>$"Lote {Detalles[0].Lote} · {Fecha:dd/MM/yyyy}";
}
internal sealed record TarjetasAlcanceAutomatico(int Id,IReadOnlyList<TarjetasMovimiento> Movimientos)
{
    public DateTime Desde=>Movimientos.Min(m=>m.Fecha).Date;
    public DateTime Hasta=>Movimientos.Max(m=>m.Fecha).Date;
    public string Sucursal=>Movimientos[0].Sucursal.Trim();
}
internal sealed record TarjetasResultadoAutomatico(IReadOnlyList<TarjetasIaPropuesta> Propuestas,
    IReadOnlyList<TarjetasDiagnostico> Diagnosticos,int Intentos,int AlcancesOmitidos);

internal static class TarjetasBusquedaAutomatica
{
    internal static IReadOnlyList<TarjetasObjetivoAutomatico> Objetivos(TarjetasComparacion datos,TarjetasCriteriosAnalisis c)
        =>datos.Detalles.Where(d=>d.Vinculo is null&&d.Nivel=="Lote"&&d.Importe>0&&d.Cantidad>0&&!string.IsNullOrWhiteSpace(d.Lote))
            .GroupBy(d=>(d.Fecha.Date,d.Lote,d.Terminal)).Where(g=>g.Count()<=c.MaxLotesCombinados&&g.Sum(d=>(long)d.Cantidad)<=c.MaxMovimientosGrupo)
            .OrderBy(g=>g.Key.Date).ThenBy(g=>g.Key.Lote).Select((g,i)=>new TarjetasObjetivoAutomatico(i,g.ToList())).ToList();

    internal static IReadOnlyList<TarjetasAlcanceAutomatico> Alcances(TarjetasComparacion datos,TarjetasCriteriosAnalisis c,CancellationToken ct=default)
    {
        var resultado=new List<TarjetasAlcanceAutomatico>();
        foreach(var sucursal in datos.Movimientos.Where(m=>m.Vinculo is null&&!m.LegacyLiquidado&&!m.Alterado&&m.Importe>0)
            .GroupBy(m=>m.Sucursal.Trim()).OrderBy(g=>g.Key))
        {
            ct.ThrowIfCancellationRequested();
            var dias=sucursal.GroupBy(m=>m.Fecha.Date).OrderBy(g=>g.Key).Select(g=>g.ToList()).ToList();
            for(var i=0;i<dias.Count;i++)
            {
                var miembros=new List<TarjetasMovimiento>();
                for(var j=i;j<dias.Count&&(dias[j][0].Fecha.Date-dias[i][0].Fecha.Date).Days<c.MaxDiasGrupo;j++)
                {
                    ct.ThrowIfCancellationRequested();miembros.AddRange(dias[j]);if(miembros.Count>c.MaxMovimientosGrupo)break;
                    resultado.Add(new(resultado.Count,miembros.ToArray()));
                }
            }
        }
        return resultado;
    }
    internal static bool Compatible(TarjetasObjetivoAutomatico o,TarjetasAlcanceAutomatico a,TarjetasCriteriosAnalisis c)
        =>a.Movimientos.Count>=o.Cantidad&&Math.Abs((a.Desde-o.Fecha).TotalDays)<=c.Dias&&Math.Abs((a.Hasta-o.Fecha).TotalDays)<=c.Dias
            &&(a.Desde==a.Hasta||a.Movimientos.Count==o.Cantidad);

    // Un presupuesto se aplica a todos los alcances, ordenados por costo, no al primer lote que aparezca.
    internal static TarjetasResultadoAutomatico Buscar(TarjetasComparacion datos,TarjetasCriteriosAnalisis c,CancellationToken ct=default,
        Action<string,int>? progreso=null,IReadOnlySet<(int Objetivo,int Alcance)>? plan=null)
    {
        c.Validar();ct.ThrowIfCancellationRequested();
        var objetivos=Objetivos(datos,c);var alcances=Alcances(datos,c,ct);
        var candidatos=objetivos.ToDictionary(o=>o.Id,_=>new List<TarjetasIaPropuesta>());
        var omitidos=objetivos.ToDictionary(o=>o.Id,_=>0);var intentos=0;var reloj=Stopwatch.StartNew();long ultimo=0;
        var trabajo=new List<(TarjetasObjetivoAutomatico o,TarjetasAlcanceAutomatico a,long costo)>();
        var porFecha=alcances.GroupBy(a=>a.Desde).ToDictionary(g=>g.Key,g=>g.ToList());
        foreach(var o in objetivos)
        {
            ct.ThrowIfCancellationRequested();
            if(trabajo.Count>=25000){omitidos[o.Id]++;continue;}
            for(var fecha=o.Fecha.AddDays(-c.Dias);fecha<=o.Fecha.AddDays(c.Dias);fecha=fecha.AddDays(1))
            {
                if(!porFecha.TryGetValue(fecha,out var opciones))continue;
                foreach(var a in opciones)
                {
                    ct.ThrowIfCancellationRequested();
                    if(!Compatible(o,a,c)||(plan is not null&&!plan.Contains((o.Id,a.Id)))||(!c.BuscarSubconjuntos&&a.Movimientos.Count!=o.Cantidad))continue;
                    if(trabajo.Count>=25000){omitidos[o.Id]++;break;}
                    trabajo.Add((o,a,Combinaciones(a.Movimientos.Count,Math.Min(o.Cantidad,a.Movimientos.Count-o.Cantidad),c.MaxIntentosAutomaticos)));
                }
            }
        }
        trabajo=trabajo.OrderBy(x=>x.costo).ThenBy(x=>Math.Abs((x.a.Hasta-x.o.Fecha).TotalDays)).ThenBy(x=>x.o.Id).ThenBy(x=>x.a.Id).ToList();
        progreso?.Invoke("Comparando lotes completos, días y sucursales; ordenando las búsquedas por costo.",0);
        foreach(var (o,a,costo) in trabajo)
        {
            ct.ThrowIfCancellationRequested();if(candidatos[o.Id].Count>=2)continue;
            if(costo>c.MaxIntentosAutomaticos-intentos){omitidos[o.Id]++;continue;}
            var registros=a.Movimientos;var complemento=o.Cantidad>registros.Count/2;
            var k=complemento?registros.Count-o.Cantidad:o.Cantidad;
            var objetivo=complemento?registros.Sum(m=>m.Importe)-o.Total:o.Total;
            var indices=Enumerable.Range(0,k).ToArray();var continuar=true;
            while(continuar)
            {
                ct.ThrowIfCancellationRequested();intentos++;
                if(reloj.ElapsedMilliseconds-ultimo>=400){ultimo=reloj.ElapsedMilliseconds;progreso?.Invoke($"{o.Descripcion}: probando {a.Desde:dd/MM} al {a.Hasta:dd/MM}, sucursal {a.Sucursal}; búsqueda por selección o exclusión.",intentos);}
                var suma=indices.Sum(i=>registros[i].Importe);
                if(Math.Abs(suma-objetivo)<=datos.Tolerancia)
                {
                    var posiciones=indices.ToHashSet();var elegidos=registros.Where((_,i)=>complemento?!posiciones.Contains(i):posiciones.Contains(i)).ToList();
                    var diferencia=o.Total-elegidos.Sum(m=>m.Importe);
                    var motivo=$"{o.Descripcion}: {o.Detalles.Count} filas PDF, {o.Cantidad} operaciones; Alfa {a.Desde:dd/MM/yyyy} al {a.Hasta:dd/MM/yyyy}, sucursal {a.Sucursal}, {elegidos.Count} movimientos. Bruto {o.Total:N2}; Alfa {elegidos.Sum(m=>m.Importe):N2}; diferencia {diferencia:N2}. Alcance elegido automáticamente; coincidencia de cantidad e importe, revisar identidad en el original.";
                    var propuesta=new TarjetasIaPropuesta("Media",motivo,o.Detalles,elegidos){Origen=plan is null?"Búsqueda automática":"Plan IA verificado"};
                    if(!candidatos[o.Id].Any(p=>Mismos(p,propuesta)))candidatos[o.Id].Add(propuesta);
                    if(candidatos[o.Id].Count>=2)break;
                }
                if(k==0)break;
                var posicion=k-1;while(posicion>=0&&indices[posicion]==registros.Count-k+posicion)posicion--;
                if(posicion<0)continuar=false;
                else{indices[posicion]++;for(var j=posicion+1;j<k;j++)indices[j]=indices[j-1]+1;}
            }
        }
        var unicas=candidatos.Values.Where(p=>p.Count==1).Select(p=>p[0]).ToList();
        var repetidos=unicas.SelectMany(p=>p.Movimientos).GroupBy(m=>m.Clave).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
        var validas=unicas.Where(p=>!p.Movimientos.Any(m=>repetidos.Contains(m.Clave))).ToList();
        var diagnosticos=new List<TarjetasDiagnostico>();
        foreach(var o in objetivos)
        {
            var ids=o.Detalles.Select(d=>d.Id).ToList();var opciones=candidatos[o.Id];
            if(opciones.Count>=2||opciones.Any(p=>p.Movimientos.Any(m=>repetidos.Contains(m.Clave))))
            {
                var mensaje="Hay alternativas que cumplen cantidad e importe o compiten por movimientos. No se eligió una automáticamente; revisá los comprobantes.";
                if(opciones.Count>=2)
                {
                    var comunes=opciones[0].Movimientos.Select(m=>m.Clave).Intersect(opciones[1].Movimientos.Select(m=>m.Clave),StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
                    var diferentes=opciones.Take(2).SelectMany(p=>p.Movimientos).Where(m=>!comunes.Contains(m.Clave)).DistinctBy(m=>m.Clave).Take(4);
                    mensaje+=" Alternativas: "+string.Join(" / ",diferentes.Select(m=>$"{(m.Comprobante.Length>0?m.Comprobante:m.Clave)} · {m.Fecha:dd/MM/yyyy} · {m.Importe:N2}"))+".";
                }
                diagnosticos.Add(new("Ambiguo",o.Descripcion,ids,mensaje,[]));
            }
            else if(opciones.Count==0)
                diagnosticos.Add(new(omitidos[o.Id]>0?"Limite":"SinCandidato",o.Descripcion,ids,omitidos[o.Id]>0?"Hay alcances que exceden el presupuesto de búsqueda; no se concluye que falten movimientos.":"No hay coincidencia verificable con cantidad e importe en los alcances consultados.",[]));
            else if(omitidos[o.Id]>0)
                diagnosticos.Add(new("AlcanceParcial",o.Descripcion,ids,"La propuesta es verificable en su fecha/sucursal. No se agotaron otros alcances más costosos; no se afirma unicidad en todo el período.",[]));
        }
        var incluidos=objetivos.SelectMany(o=>o.Detalles).Select(d=>d.Id).ToHashSet();
        foreach(var fuera in datos.Detalles.Where(d=>d.Vinculo is null&&d.Nivel=="Lote"&&!incluidos.Contains(d.Id)).GroupBy(d=>(d.Fecha.Date,d.Lote)))
            diagnosticos.Add(new("Limite",$"Lote {fuera.Key.Lote} · {fuera.Key.Date:dd/MM/yyyy}",fuera.Select(d=>d.Id).ToList(),"El lote excede los límites de conceptos/movimientos o no tiene cantidad positiva utilizable. No se concluye que falten movimientos.",[]));
        var diasExcedidos=datos.Movimientos.Where(m=>m.Vinculo is null&&!m.LegacyLiquidado&&!m.Alterado&&m.Importe>0).GroupBy(m=>(m.Fecha.Date,Sucursal:m.Sucursal.Trim())).Where(g=>g.Count()>c.MaxMovimientosGrupo).ToList();
        foreach(var dia in diasExcedidos)diagnosticos.Add(new("Limite",$"Alfa {dia.Key.Date:dd/MM/yyyy} · sucursal {dia.Key.Sucursal}",[],
            $"Hay {dia.Count()} pendientes en ese día, por encima del máximo {c.MaxMovimientosGrupo}. No se exploró ese alcance; revisá los límites autorizados.",[]));
        return new(validas,diagnosticos,intentos,omitidos.Values.Sum()+diasExcedidos.Count);
    }
    private static long Combinaciones(int n,int k,int limite)
    {
        decimal total=1;for(var i=1;i<=k;i++){total=total*(n-k+i)/i;if(total>limite)return (long)limite+1;}return (long)total;
    }
    internal static bool CumpleLimites(TarjetasIaPropuesta p,TarjetasCriteriosAnalisis c)
        =>p.Detalles.Count>0&&p.Movimientos.Count>0&&p.Movimientos.Count<=c.MaxMovimientosGrupo
            &&p.Movimientos.Select(m=>m.Sucursal.Trim()).Distinct(StringComparer.Ordinal).Count()==1
            &&(p.Movimientos.Max(m=>m.Fecha.Date)-p.Movimientos.Min(m=>m.Fecha.Date)).Days<c.MaxDiasGrupo
            &&(!p.Detalles.Any(d=>d.Nivel=="Lote")||(p.Detalles.All(d=>d.Nivel=="Lote")&&p.Detalles.Count<=c.MaxLotesCombinados
                &&p.Detalles.Select(d=>(d.Fecha.Date,d.Lote,d.Terminal)).Distinct().Count()==1
                &&p.Detalles.Sum(d=>(long)d.Cantidad)==p.Movimientos.Count));
    internal static bool HayAlternativaIndistinguible(TarjetasIaPropuesta p,TarjetasComparacion datos,TarjetasCriteriosAnalisis c)
    {
        if(!p.Detalles.All(d=>d.Nivel=="Cupón"))return false;
        static bool TieneId(string v)=>v.Trim().TrimStart('0') is not ("" or "1");
        var elegidos=p.Movimientos.Select(m=>m.Clave).ToHashSet(StringComparer.Ordinal);
        foreach(var m in p.Movimientos)
        {
            if(TieneId(m.Cupon)&&p.Detalles.Any(d=>TieneId(d.Cupon)&&d.Cupon.Trim().TrimStart('0')==m.Cupon.Trim().TrimStart('0')))continue;
            if(datos.Movimientos.Any(a=>!elegidos.Contains(a.Clave)&&a.Vinculo is null&&!a.LegacyLiquidado&&!a.Alterado
                &&p.Detalles.Any(d=>Math.Abs(m.Importe-d.Importe)<=datos.Tolerancia&&Math.Abs((m.Fecha.Date-d.Fecha.Date).Days)<=c.Dias
                    &&Math.Abs((a.Fecha.Date-d.Fecha.Date).Days)<=c.Dias&&Math.Abs(a.Importe-d.Importe)<=datos.Tolerancia)))return true;
        }
        return false;
    }
    private static bool Mismos(TarjetasIaPropuesta a,TarjetasIaPropuesta b)=>a.Movimientos.Select(m=>m.Clave).Order(StringComparer.Ordinal).SequenceEqual(b.Movimientos.Select(m=>m.Clave).Order(StringComparer.Ordinal));

    internal static IReadOnlyList<TarjetasDiagnostico> DetectarYaConciliados(TarjetasComparacion todos,TarjetasCriteriosAnalisis c,IReadOnlySet<long> propuestos)
    {
        var resultado=new List<TarjetasDiagnostico>();
        foreach(var o in Objetivos(todos with{Detalles=todos.Detalles.Where(d=>d.Vinculo is null&&!propuestos.Contains(d.Id)).ToList()},c))
        foreach(var grupo in todos.Movimientos.Where(m=>!m.Alterado&&!m.LegacyLiquidado&&m.Importe>0&&Math.Abs((m.Fecha.Date-o.Fecha).Days)<=c.Dias).GroupBy(m=>(m.Fecha.Date,m.Sucursal)))
        {
            var libres=grupo.Where(m=>m.Vinculo is null).ToList();if(libres.Count!=o.Cantidad-1)continue;
            var faltante=o.Total-libres.Sum(m=>m.Importe);
            var usados=grupo.Where(m=>m.Vinculo.HasValue&&Math.Abs(m.Importe-faltante)<=todos.Tolerancia).ToList();
            if(usados.Count==0)continue;
            var vinculos=usados.Select(m=>m.Vinculo!.Value).Distinct().ToList();
            resultado.Add(new("YaConciliado",o.Descripcion,o.Detalles.Select(d=>d.Id).ToList(),
                $"En {grupo.Key.Date:dd/MM/yyyy}, sucursal {grupo.Key.Sucursal}, hay {libres.Count} pendientes; faltaría {faltante:N2}. Coincide con un movimiento ya usado en vínculo(s) {string.Join(", ",vinculos.Select(id=>"#"+id))}. Revisar el vínculo original; no se deshizo nada.",vinculos));
        }
        return resultado;
    }
}

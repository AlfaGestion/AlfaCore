using AlfaCore.Models;
using System.Diagnostics;

namespace AlfaCore.Services;

internal sealed record TarjetasBusquedaAgrupada(IReadOnlyList<TarjetasIaPropuesta> Propuestas,int Intentos,
    bool Limitada,int Ambiguas,int DiasExcedidos)
{
    public IReadOnlyList<TarjetasDiagnostico> Diagnosticos {get;init;}=[];
}

internal static class TarjetasAgrupaciones
{
    private sealed record Objetivo(IReadOnlyList<TarjetasDetalle> Detalles,int Cantidad,decimal Total,DateTime Fecha);
    internal static TarjetasBusquedaAgrupada Buscar(TarjetasComparacion datos,TarjetasCriteriosAnalisis criterios,CancellationToken ct=default,Action<string,int>? progreso=null)
    {
        criterios.Validar();ct.ThrowIfCancellationRequested();
        var intentos=0;var limitada=false;var ambiguas=0;
        var fase="Preparando combinaciones de conceptos del PDF";var ultimoAviso=Stopwatch.GetTimestamp();
        void Informar(string actividad){fase=actividad;ultimoAviso=Stopwatch.GetTimestamp();progreso?.Invoke(fase,intentos);}
        bool Paso()
        {
            ct.ThrowIfCancellationRequested();if(intentos>=criterios.MaxIntentos){limitada=true;return false;}intentos++;
            if(progreso is not null&&intentos%512==0&&Stopwatch.GetElapsedTime(ultimoAviso).TotalMilliseconds>=400)
            {ultimoAviso=Stopwatch.GetTimestamp();progreso(fase,intentos);}
            return true;
        }
        Informar(fase);
        var dias=datos.Movimientos.Where(m=>m.Vinculo is null&&!m.LegacyLiquidado&&!m.Alterado&&m.Importe>0)
            .GroupBy(m=>(m.Fecha.Date,Sucursal:m.Sucursal.Trim())).OrderBy(g=>g.Key.Date).ThenBy(g=>g.Key.Sucursal)
            .Select(g=>g.OrderBy(m=>m.Importe).ThenBy(m=>m.Clave,StringComparer.Ordinal).ToList()).ToList();
        var diasExcedidos=dias.Count(g=>g.Count>criterios.MaxMovimientosGrupo);
        var objetivos=new List<Objetivo>();
        // Unir solo conceptos del mismo lote, fecha de presentación y terminal; nunca mezclar sucursales de Alfa.
        foreach(var bloque in datos.Detalles.Where(d=>d.Vinculo is null&&d.Nivel=="Lote"&&d.Importe>0&&d.Cantidad>0&&!string.IsNullOrWhiteSpace(d.Lote))
            .GroupBy(d=>(d.Fecha.Date,Lote:d.Lote.Trim(),Terminal:d.Terminal.Trim())).OrderBy(g=>g.Key.Date).ThenBy(g=>g.Key.Lote))
        {
            var filas=bloque.OrderBy(d=>d.Id).ToList();var elegidas=new List<TarjetasDetalle>();
            void Combinar(int desde,int cantidad,decimal total)
            {
                for(var j=desde;j<filas.Count&&!limitada;j++)
                {
                    if(!Paso())return;
                    var nuevaCantidad=(long)cantidad+filas[j].Cantidad;
                    if(nuevaCantidad>criterios.MaxMovimientosGrupo)continue;
                    elegidas.Add(filas[j]);
                    objetivos.Add(new(elegidas.ToArray(),(int)nuevaCantidad,total+filas[j].Importe,filas[j].Fecha.Date));
                    if(objetivos.Count>=10000){limitada=true;elegidas.RemoveAt(elegidas.Count-1);return;}
                    if(elegidas.Count<criterios.MaxLotesCombinados)Combinar(j+1,(int)nuevaCantidad,total+filas[j].Importe);
                    elegidas.RemoveAt(elegidas.Count-1);
                }
            }
            Combinar(0,0,0);if(limitada)return new([],intentos,true,0,diasExcedidos);
        }
        bool Compatible(Objetivo o,List<TarjetasMovimiento> grupo)=>grupo.Count<=criterios.MaxMovimientosGrupo
            &&Math.Abs((grupo[0].Fecha.Date-o.Fecha).TotalDays)<=criterios.Dias;
        TarjetasIaPropuesta Propuesta(Objetivo o,IReadOnlyList<TarjetasMovimiento> ms,bool diaCompleto)
        {
            var diferencia=o.Total-ms.Sum(m=>m.Importe);
            var evidencia=$"{(diaCompleto?"Total diario":"Combinación dentro del día")}: {o.Detalles.Count} concepto(s) del lote {o.Detalles[0].Lote}, presentación {o.Fecha:dd/MM/yyyy}; {ms.Count} movimientos de Alfa del {ms[0].Fecha:dd/MM/yyyy}, sucursal {ms[0].Sucursal.Trim()}. Cantidad PDF {o.Cantidad}; bruto {o.Total:N2}; Alfa {ms.Sum(m=>m.Importe):N2}; diferencia {diferencia:N2}. Revisá el original: coincidir no prueba identidad de cada cupón.";
            if(evidencia.Length>500)evidencia=evidencia[..500];
            return new(diaCompleto&&diferencia==0?"Alta":"Media",evidencia,o.Detalles,ms){Origen="Agrupación local"};
        }
        Informar("Comparando totales diarios por fecha, sucursal, cantidad e importe");
        var diarias=new List<TarjetasIaPropuesta>();
        foreach(var o in objetivos)
        foreach(var grupo in dias)
        {
            if(!Paso())return new([],intentos,true,0,diasExcedidos);
            if(Compatible(o,grupo)&&grupo.Count==o.Cantidad&&Math.Abs(grupo.Sum(m=>m.Importe)-o.Total)<=datos.Tolerancia)
                diarias.Add(Propuesta(o,grupo,true));
            if(diarias.Count>=20000)return new([],intentos,true,0,diasExcedidos);
        }
        var ambiguosPdf=diarias.SelectMany(p=>p.Detalles).GroupBy(d=>d.Id).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        var ambiguosAlfa=diarias.SelectMany(p=>p.Movimientos).GroupBy(m=>m.Clave).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
        var aceptadas=SinConflictos(diarias);ambiguas=diarias.Count-aceptadas.Count;
        if(!criterios.BuscarSubconjuntos)return new(aceptadas,intentos,false,ambiguas,diasExcedidos);
        var usadosPdf=aceptadas.SelectMany(p=>p.Detalles).Select(d=>d.Id).Concat(ambiguosPdf).ToHashSet();
        var usadosAlfa=aceptadas.SelectMany(p=>p.Movimientos).Select(m=>m.Clave).Concat(ambiguosAlfa).ToHashSet(StringComparer.Ordinal);
        Informar("Probando subconjuntos de movimientos dentro de cada día y sucursal");
        var combinadas=new List<TarjetasIaPropuesta>();
        foreach(var o in objetivos.Where(o=>!o.Detalles.Any(d=>usadosPdf.Contains(d.Id))))
        {
            var soluciones=new List<TarjetasIaPropuesta>();
            foreach(var original in dias)
            {
                if(!Compatible(o,original))continue;
                var grupo=original.Where(m=>!usadosAlfa.Contains(m.Clave)).ToList();
                if(grupo.Count<o.Cantidad)continue;
                var prefijos=new decimal[grupo.Count+1];for(var j=0;j<grupo.Count;j++)prefijos[j+1]=prefijos[j]+grupo[j].Importe;
                var elegidos=new List<TarjetasMovimiento>();
                void BuscarSuma(int desde,int faltan,decimal total)
                {
                    if(soluciones.Count>=2||!Paso())return;
                    if(faltan==0)
                    {
                        if(Math.Abs(total-o.Total)<=datos.Tolerancia)soluciones.Add(Propuesta(o,elegidos.ToArray(),false));
                        return;
                    }
                    if(grupo.Count-desde<faltan||total+prefijos[desde+faltan]-prefijos[desde]>o.Total+datos.Tolerancia
                        ||total+prefijos[grupo.Count]-prefijos[grupo.Count-faltan]<o.Total-datos.Tolerancia)return;
                    for(var j=desde;j<=grupo.Count-faltan&&!limitada&&soluciones.Count<2;j++)
                    {
                        elegidos.Add(grupo[j]);BuscarSuma(j+1,faltan-1,total+grupo[j].Importe);elegidos.RemoveAt(elegidos.Count-1);
                    }
                }
                BuscarSuma(0,o.Cantidad,0);
                // No presentar una búsqueda truncada como única: conservar únicamente los totales diarios ya verificados.
                if(limitada)return new(aceptadas,intentos,true,ambiguas,diasExcedidos);
                if(soluciones.Count>=2)break;
            }
            combinadas.AddRange(soluciones);
            if(combinadas.Count>=20000)return new(aceptadas,intentos,true,ambiguas,diasExcedidos);
        }
        var unicas=SinConflictos(combinadas);ambiguas+=combinadas.Count-unicas.Count;
        return new(aceptadas.Concat(unicas).ToList(),intentos,false,ambiguas,diasExcedidos);
    }

    private static List<TarjetasIaPropuesta> SinConflictos(IReadOnlyList<TarjetasIaPropuesta> propuestas)
    {
        var ds=propuestas.SelectMany(p=>p.Detalles).GroupBy(d=>d.Id).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        var ms=propuestas.SelectMany(p=>p.Movimientos).GroupBy(m=>m.Clave).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
        return propuestas.Where(p=>!p.Detalles.Any(d=>ds.Contains(d.Id))&&!p.Movimientos.Any(m=>ms.Contains(m.Clave))).ToList();
    }
}

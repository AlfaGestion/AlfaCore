using System.Text.Json;
using AlfaCore.Models;

namespace AlfaCore.Services;

internal sealed record TarjetasPlanContexto(ConciliacionIaContexto Contexto,IReadOnlyList<TarjetasObjetivoAutomatico> Objetivos,IReadOnlyList<TarjetasAlcanceAutomatico> Alcances);
internal static class TarjetasPlanIa
{
    internal static TarjetasPlanContexto Preparar(TarjetasComparacion datos,TarjetasFiltro filtro,TarjetasCriteriosAnalisis criterios)
    {
        var objetivos=TarjetasBusquedaAutomatica.Objetivos(datos,criterios);var alcances=TarjetasBusquedaAutomatica.Alcances(datos,criterios);
        // Nunca recortar el contexto para aparentar un análisis completo.
        if(objetivos.Count>criterios.MaxFilasIa||alcances.Count>criterios.MaxFilasIa)
            throw new InvalidOperationException("El plan automático supera el máximo de alcances por petición. Acotá el período o la sucursal.");
        var contexto=new ConciliacionIaContexto(new(datos.Importacion.Cuenta,filtro.Desde,filtro.Hasta),new(criterios.Dias,datos.Tolerancia,true,false),
            objetivos.Select(o=>new ConciliacionFilaBanco{Id=o.Id,Fecha=o.Fecha,Importe=o.Total,Detalle=$"Objetivo de búsqueda; cantidad {o.Cantidad}; filas PDF {o.Detalles.Count}",Referencia=o.Descripcion,Categoria="Tarjetas"}).ToList(),
            alcances.Select(a=>new ConciliacionFilaAlfa{Secuencia=a.Id,Fecha=a.Desde,Importe=a.Movimientos.Sum(m=>m.Importe),
                Detalle=$"Alcance: cantidad {a.Movimientos.Count}; {a.Desde:yyyy-MM-dd} al {a.Hasta:yyyy-MM-dd}; sucursal {a.Sucursal}",Referencia=$"Sucursal {a.Sucursal}",Comprobante="Resumen para planificar, no para confirmar"}).ToList(),[]){EsTarjetas=true,EsPlanTarjetas=true};
        return new(contexto,objetivos,alcances);
    }
    internal static IReadOnlySet<(int Objetivo,int Alcance)> Validar(TarjetasPlanContexto plan,string json,TarjetasCriteriosAnalisis criterios)
    {
        if(string.IsNullOrWhiteSpace(json)||json.Length>1000000)throw new InvalidOperationException("La IA no devolvió un plan de búsqueda válido.");
        ConciliacionIaSalida salida;
        try{salida=JsonSerializer.Deserialize<ConciliacionIaSalida>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new JsonException();}
        catch(JsonException ex){throw new InvalidOperationException("La IA no devolvió un plan de búsqueda válido.",ex);}
        if(salida.Grupos is null||salida.Orientaciones is null||salida.Grupos.Count>200)throw new InvalidOperationException("El plan de IA excede los límites permitidos.");
        var resultado=new HashSet<(int,int)>();
        foreach(var grupo in salida.Grupos)
        {
            if(grupo is null||grupo.Banco is null||grupo.Alfa is null||grupo.Banco.Length!=1||grupo.Alfa.Length is <1 or >20
                ||grupo.Alfa.Distinct().Count()!=grupo.Alfa.Length||grupo.Banco[0]<0||grupo.Banco[0]>=plan.Objetivos.Count
                ||grupo.Alfa.Any(i=>i<0||i>=plan.Alcances.Count))continue;
            var objetivo=plan.Objetivos[grupo.Banco[0]];
            // Los índices son una propuesta de alcance, no una excepción a cantidades, fechas o sucursales.
            foreach(var id in grupo.Alfa)
            {
                var a=plan.Alcances[id];
                if(TarjetasBusquedaAutomatica.Compatible(objetivo,a,criterios))resultado.Add((objetivo.Id,a.Id));
            }
        }
        return resultado;
    }
}

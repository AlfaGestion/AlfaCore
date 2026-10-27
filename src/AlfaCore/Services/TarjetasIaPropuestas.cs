using AlfaCore.Models;

namespace AlfaCore.Services;

internal static class TarjetasIaPropuestas
{
    internal static ConciliacionIaContexto Contexto(TarjetasComparacion datos,TarjetasFiltro filtro,int dias,int maximo)
    {
        var detalles=datos.Detalles.Where(x=>x.Vinculo is null).ToList();
        var movimientos=datos.Movimientos.Where(x=>x.Vinculo is null&&!x.LegacyLiquidado&&!x.Alterado).ToList();
        if(detalles.Count>maximo||movimientos.Count>maximo)
            throw new InvalidOperationException($"El análisis admite hasta {maximo} pendientes por lado. Acotá el período o conciliá parte de la liquidación; no se envía una muestra parcial.");
        return new(new(datos.Importacion.Cuenta,filtro.Desde,filtro.Hasta),new(dias,datos.Tolerancia,true,false),
            detalles.Select(d=>new ConciliacionFilaBanco{Id=d.Id,Fecha=d.Fecha,Importe=d.Importe,
                Detalle=$"{d.Nivel}; cantidad {d.Cantidad}; pago {d.FechaPago:yyyy-MM-dd}",Referencia=$"Cupón {d.Cupon}; lote {d.Lote}; liquidación {d.Liquidacion}",Categoria="Tarjetas"}).ToList(),
            movimientos.Select((m,index)=>new ConciliacionFilaAlfa{Secuencia=index,Fecha=m.Fecha,Importe=m.Importe,Moneda="1",
                Detalle=m.Detalle,Referencia=$"Cupón {m.Cupon}; presentación {m.Presentacion}; sucursal {m.Sucursal}",Comprobante=m.Comprobante,Huella=m.Huella}).ToList(),[]){EsTarjetas=true};
    }

    internal static TarjetasIaResultado Validar(TarjetasComparacion datos,ConciliacionIaContexto contexto,ConciliacionIaRespuesta respuesta)
    {
        // Se reutilizan los controles de índices, duplicados, fechas, signos y tolerancia del circuito bancario.
        var validacion=ConciliacionBancariaService.ValidarRespuestaIa(contexto,respuesta.Json);
        var movimientos=datos.Movimientos.Where(x=>x.Vinculo is null&&!x.LegacyLiquidado&&!x.Alterado).ToList();
        var propuestas=new List<TarjetasIaPropuesta>();var descartadas=validacion.Descartadas;
        foreach(var p in validacion.Propuestas)
        {
            var ds=p.Banco.Select(b=>datos.Detalles.Single(d=>d.Id==b.Id)).ToList();
            var ms=p.Alfa.Select(a=>movimientos[a.Secuencia]).ToList();
            // Si ambos lados identifican un cupón, una suma correcta no permite ignorar que sean distintos.
            if(ds.Any(d=>d.Nivel=="Cupón"&&Identificable(d.Cupon)&&ms.All(m=>Identificable(m.Cupon))&&!ms.Any(m=>IgualCupon(d.Cupon,m.Cupon)))
                ||(ds.All(d=>d.Nivel=="Cupón"&&Identificable(d.Cupon))&&ms.Any(m=>Identificable(m.Cupon)&&!ds.Any(d=>IgualCupon(d.Cupon,m.Cupon)))))
            {descartadas++;continue;}
            propuestas.Add(new(p.Confianza,p.Motivo,ds,ms));
        }
        return new(respuesta.Modelo,contexto.Banco.Count,contexto.Alfa.Count,descartadas,contexto.Criterios.Dias,propuestas);
    }
    private static bool Identificable(string cupon)=>cupon.Trim().TrimStart('0') is not ("" or "1");
    private static bool IgualCupon(string a,string b)=>a.Trim().TrimStart('0')==b.Trim().TrimStart('0');
}

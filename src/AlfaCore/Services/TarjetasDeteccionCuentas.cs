using AlfaCore.Models;

namespace AlfaCore.Services;

internal static class TarjetasDeteccionCuentas
{
    private static string Cupon(string valor)
    {
        var clave=valor.Trim().ToUpperInvariant().TrimStart('0');
        return clave is "" or "1"?"":clave;
    }

    internal static IReadOnlyList<TarjetasCuentaCandidata> Evaluar(TarjetasLectura lectura,
        IReadOnlyList<ConciliacionCuenta> cuentas,IReadOnlyList<TarjetasMovimiento> movimientos,int dias,decimal tolerancia,
        CancellationToken ct=default)
    {
        var resultado=new List<TarjetasCuentaCandidata>();
        var porCuenta=movimientos.GroupBy(m=>m.Cuenta.Trim()).ToDictionary(g=>g.Key,g=>g.ToList());
        foreach(var cuenta in cuentas)
        {
            ct.ThrowIfCancellationRequested();
            if(!porCuenta.TryGetValue(cuenta.Codigo,out var alfa))continue;
            var usados=new HashSet<TarjetasMovimiento>();var detalles=new HashSet<TarjetasDetalle>();
            var cupones=0;var exactas=0;var lotes=0;
            // Un cupón genérico (1) o repetido no identifica una venta. Cada fila se usa una sola vez.
            var cuponAlfa=alfa.Where(m=>Cupon(m.Cupon).Length>0).GroupBy(m=>Cupon(m.Cupon)).ToDictionary(g=>g.Key,g=>g.ToList());
            foreach(var grupo in lectura.Detalles.Where(d=>d.Importe>0&&d.Cantidad==1&&Cupon(d.Cupon).Length>0).GroupBy(d=>Cupon(d.Cupon)))
            {
                if(grupo.Count()!=1||!cuponAlfa.TryGetValue(grupo.Key,out var candidatos))continue;
                var d=grupo.Single();
                var compatibles=candidatos.Where(m=>m.Importe==d.Importe&&Math.Abs((m.Fecha.Date-d.Fecha.Date).Days)<=dias).ToList();
                if(compatibles.Count!=1)continue;
                usados.Add(compatibles[0]);detalles.Add(d);cupones++;
            }
            var porFechaImporte=alfa.Where(m=>!usados.Contains(m)).GroupBy(m=>(m.Fecha.Date,m.Importe)).ToDictionary(g=>g.Key,g=>g.ToList());
            foreach(var grupo in lectura.Detalles.Where(d=>d.Importe>0&&d.Cantidad==1&&!detalles.Contains(d)).GroupBy(d=>(d.Fecha.Date,d.Importe)))
            {
                if(grupo.Count()!=1||!porFechaImporte.TryGetValue(grupo.Key,out var candidatos)||candidatos.Count!=1)continue;
                var d=grupo.Single();var m=candidatos[0];
                // No ignorar cupones informativos que se contradicen aunque coincidan fecha e importe.
                if(Cupon(d.Cupon).Length>0&&Cupon(m.Cupon).Length>0&&Cupon(d.Cupon)!=Cupon(m.Cupon))continue;
                usados.Add(m);detalles.Add(d);exactas++;
            }
            var diasAlfa=alfa.GroupBy(m=>m.Fecha.Date).ToDictionary(g=>g.Key,g=>g.ToList());
            foreach(var grupo in lectura.Detalles.GroupBy(d=>d.Fecha.Date))
            {
                // Totales diarios de lotes: evidencia orientativa, nunca identidad de cada cupón.
                if(grupo.Any(d=>d.Importe<=0)||!grupo.Any(d=>d.Cantidad>1)||!diasAlfa.TryGetValue(grupo.Key,out var dia))continue;
                if(grupo.Sum(d=>(long)d.Cantidad)==dia.Count&&Math.Abs(grupo.Sum(d=>d.Importe)-dia.Sum(m=>m.Importe))<=tolerancia)lotes++;
            }
            if(cupones+exactas+lotes==0)continue;
            var sucursales=usados.Select(m=>m.Sucursal.Trim()).Distinct().ToList();
            resultado.Add(new(cuenta.Codigo,cuenta.Descripcion,cupones,exactas,lotes,detalles.Sum(d=>d.Importe),
                sucursales.Count==1?sucursales[0]:"",alfa.Count));
        }
        return resultado.OrderByDescending(c=>c.PorCupon).ThenByDescending(c=>c.Coincidencias)
            .ThenByDescending(c=>c.ImporteCoincidente).ThenByDescending(c=>c.LotesCompatibles).ThenBy(c=>c.Cuenta).ToList();
    }

    internal static void Sugerir(TarjetasLectura lectura,IReadOnlyList<ConciliacionCuenta> cuentas,
        IReadOnlyList<TarjetasCuentaCandidata> candidatas,string recordada,string sucursalRecordada,
        DateTime desde,DateTime hasta,int dias,bool completo)
    {
        lectura.CuentaSugerida="";lectura.SucursalSugerida="";
        var mensaje="";
        var regla=cuentas.SingleOrDefault(c=>c.Codigo==recordada);
        if(!completo)
            mensaje="El período supera 10.000 movimientos entre las cuentas TJ. No se usó una muestra parcial para sugerir: elegí la cuenta manualmente.";
        else if(regla is not null&&candidatas.Any(c=>c.Cuenta!=regla.Codigo)&&!candidatas.Any(c=>c.Cuenta==regla.Codigo))
            mensaje="La cuenta recordada no presenta coincidencias y hay evidencia en otra cuenta. Revisá las candidatas antes de importar.";
        else if(regla is not null)
        {
            lectura.CuentaSugerida=regla.Codigo;lectura.SucursalSugerida=sucursalRecordada;
            mensaje=candidatas.Any(c=>c.Cuenta==regla.Codigo)?"Cuenta recordada para este banco, comercio y tarjeta, con coincidencias en Alfa. Revisá la evidencia.":"Cuenta recordada para este banco, comercio y tarjeta. Sin coincidencias verificables en el período; revisá antes de importar.";
        }
        else if(candidatas.Count==1&&candidatas[0].Coincidencias>0)
        {
            lectura.CuentaSugerida=candidatas[0].Cuenta;lectura.SucursalSugerida=candidatas[0].Sucursal;
            mensaje="Cuenta sugerida por coincidencias únicas de cupón o fecha e importe. No se encontró evidencia en otras cuentas TJ; revisá antes de importar.";
        }
        else if(candidatas.Count>0)
            mensaje="Hay varias cuentas posibles o solo totales de lotes compatibles. Elegí la cuenta revisando la evidencia; no se seleccionó una automáticamente.";
        else
        {
            var nombres=cuentas.Where(c=>lectura.Tarjeta.Length>0&&c.Descripcion.Contains(lectura.Tarjeta,StringComparison.OrdinalIgnoreCase)).ToList();
            if(nombres.Count==1){lectura.CuentaSugerida=nombres[0].Codigo;mensaje="Cuenta sugerida únicamente por el nombre de la tarjeta, sin coincidencias verificables. Revisá antes de importar.";}
            else mensaje="No hay una cuenta identificable con la información del archivo y los movimientos autorizados. Elegí la cuenta manualmente.";
        }
        if(recordada.Length>0&&regla is null)lectura.Avisos.Add("La asociación guardada apunta a una cuenta que ya no es TJ activa en pesos. No se utilizó.");
        lectura.Deteccion=new(desde,hasta,dias,cuentas.Count,mensaje,candidatas);
    }
}

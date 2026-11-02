using System.Data;
using System.Globalization;
using AlfaCore.Models;
using Dapper;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    public Task<ConciliacionArchivo> ExportarExcelAsync(TarjetasExportar solicitud,CancellationToken ct=default)
        =>Ejecutar("ExportarExcel",async(c,t)=>
        {
            var filtro=FiltroExportacion(solicitud);
            TarjetasComparacion comparacion;var vinculos=new List<TarjetasVinculoExportacion>();
            // Solo lecturas. La transacción termina antes de construir el libro.
            using(var tx=c.BeginTransaction(IsolationLevel.RepeatableRead))
            {
                comparacion=FiltrarExportacion(await Comparar(c,filtro,t,tx),solicitud);
                var ids=comparacion.Detalles.Select(d=>d.Vinculo).Concat(comparacion.Movimientos.Select(m=>m.Vinculo))
                    .Where(id=>id.HasValue).Select(id=>id!.Value).Distinct().ToArray();
                foreach(var lote in ids.Chunk(500))
                    vinculos.AddRange(await c.QueryAsync<TarjetasVinculoExportacion>(Cmd("""
                        SELECT h.Id,h.ImportacionId,h.Usuario,h.FechaUtc,h.Motivo,h.ImporteResumen,h.ImporteAlfa,
                          (SELECT COUNT(*) FROM dbo.TJ_VinculoDetalle d WHERE d.VinculoId=h.Id AND d.Activo=1) CantidadDetalles,
                          (SELECT COUNT(*) FROM dbo.TJ_VinculoAlfa a WHERE a.VinculoId=h.Id AND a.Activo=1) CantidadMovimientos
                        FROM dbo.TJ_Vinculo h WHERE h.Activo=1 AND h.Id IN @ids;
                        """,new{ids=lote},t,tx)));
                tx.Commit();
            }
            var datos=new TarjetasDatosExportacion(comparacion,solicitud with{Filtro=filtro},vinculos,
                sessions.GetActiveSession()?.Nombre??"",users.GetCurrentUserName(),DateTime.UtcNow);
            var contenido=await Task.Run(()=>TarjetasExcelExporter.Crear(datos,t),t);
            return new ConciliacionArchivo($"conciliacion_tarjetas_{filtro.Importacion}_{datos.GeneradoUtc:yyyyMMdd_HHmmss}.xlsx",contenido);
        },ct);

    internal static TarjetasFiltro FiltroExportacion(TarjetasExportar solicitud)
    {
        if(!Enum.IsDefined(solicitud.Alcance)||solicitud.SucursalAlfa.Length>20
            ||(solicitud.DetallesSeleccionados?.Count??0)>2000||(solicitud.MovimientosSeleccionados?.Count??0)>2000)
            throw new InvalidOperationException("Revisá el alcance y la selección de la exportación.");
        foreach(var fecha in new[]{solicitud.FechaLiquidacion,solicitud.FechaAlfa})
            if(fecha.Length>0&&!DateTime.TryParseExact(fecha,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
                throw new InvalidOperationException("Revisá las fechas de los filtros de cada lista antes de exportar.");
        return solicitud.Filtro with{Estado=solicitud.Alcance switch
        {
            TarjetasExportacionAlcance.Pendientes=>"Pendientes",TarjetasExportacionAlcance.Conciliados=>"Conciliados",_=>solicitud.Filtro.Estado
        }};
    }

    internal static TarjetasComparacion FiltrarExportacion(TarjetasComparacion comparacion,TarjetasExportar solicitud)
    {
        FiltroExportacion(solicitud);
        var detalles=(solicitud.DetallesSeleccionados??[]).ToHashSet();
        var movimientos=(solicitud.MovimientosSeleccionados??[]).ToHashSet(StringComparer.Ordinal);
        static bool Fecha(DateTime valor,string filtro)=>filtro.Length==0||valor.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)==filtro;
        return comparacion with
        {
            Detalles=comparacion.Detalles.Where(d=>Fecha(d.Fecha,solicitud.FechaLiquidacion)&&(!solicitud.SoloSeleccionados||detalles.Contains(d.Id))).OrderBy(d=>d.Fecha).ThenBy(d=>d.Id).ToList(),
            Movimientos=comparacion.Movimientos.Where(m=>Fecha(m.Fecha,solicitud.FechaAlfa)
                &&(solicitud.SucursalAlfa.Length==0||m.Sucursal.Trim()==(solicitud.SucursalAlfa=="__sin_sucursal__"?"":solicitud.SucursalAlfa))
                &&(!solicitud.SoloSeleccionados||movimientos.Contains(m.Clave))).OrderBy(m=>m.Fecha).ThenBy(m=>m.Clave,StringComparer.Ordinal).ToList()
        };
    }
}

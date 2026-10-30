using System.Data;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    internal sealed record GrupoConfirmacion(IReadOnlyList<TarjetasDetalle> Detalles,IReadOnlyList<TarjetasMovimiento> Movimientos)
    {
        public decimal Total=>Detalles.Sum(d=>d.Importe);
        public decimal Alfa=>Movimientos.Sum(m=>m.Importe);
    }

    internal static GrupoConfirmacion ResolverConfirmacion(TarjetasComparacion actual,TarjetasConfirmar solicitud)
    {
        if(solicitud.Detalles.Count is 0 or >2000||solicitud.Movimientos.Count is 0 or >2000||solicitud.Motivo.Length>500)
            throw new InvalidOperationException("Seleccioná hasta 2.000 operaciones de cada lado y un motivo de hasta 500 caracteres.");
        if(!actual.Importacion.DetalleCompleto)throw new InvalidOperationException("El detalle del PDF no coincide con el bruto. Requiere revisión antes de confirmar.");
        var ds=solicitud.Detalles.Distinct().Select(id=>actual.Detalles.SingleOrDefault(x=>x.Id==id)??throw new InvalidOperationException("La liquidación cambió. Compará nuevamente.")).ToList();
        var ms=solicitud.Movimientos.Select(s=>actual.Movimientos.SingleOrDefault(x=>x.Clave==s.Clave&&x.Huella==s.Huella)??throw new InvalidOperationException("El movimiento cambió o dejó de estar autorizado. Compará nuevamente.")).ToList();
        if(ds.Count!=solicitud.Detalles.Count||ms.Select(x=>x.Clave).Distinct().Count()!=ms.Count||ds.Any(x=>x.Vinculo!=null)||ms.Any(x=>x.Vinculo!=null||x.LegacyLiquidado||x.Alterado))
            throw new InvalidOperationException("Hay operaciones repetidas, modificadas o conciliadas. Actualizá la comparación.");
        var grupo=new GrupoConfirmacion(ds,ms);
        ValidarDiferencia(grupo.Total,grupo.Alfa,actual.Tolerancia,solicitud.Motivo);
        return grupo;
    }

    private async Task<long> InsertarVinculo(SqlConnection c,SqlTransaction tx,TarjetasComparacion actual,GrupoConfirmacion grupo,string motivo,CancellationToken ct)
    {
        var id=await c.ExecuteScalarAsync<long>(Cmd("INSERT dbo.TJ_Vinculo(ImportacionId,ImporteResumen,ImporteAlfa,Motivo,Usuario) OUTPUT INSERTED.Id VALUES(@import,@total,@alfa,@motivo,@usuario)",new{import=actual.Importacion.Id,total=grupo.Total,alfa=grupo.Alfa,motivo=motivo.Trim(),usuario=users.GetCurrentUserName()},ct,tx));
        foreach(var d in grupo.Detalles)await c.ExecuteAsync(Cmd("INSERT dbo.TJ_VinculoDetalle(VinculoId,DetalleId,Activo) VALUES(@id,@detalle,1)",new{id,detalle=d.Id},ct,tx));
        foreach(var m in grupo.Movimientos)await c.ExecuteAsync(Cmd("INSERT dbo.TJ_VinculoAlfa(VinculoId,Cuenta,Clave,Huella,Importe,Activo) VALUES(@id,@cuenta,@Clave,@Huella,@Importe,1)",new{id,cuenta=actual.Importacion.Cuenta,m.Clave,m.Huella,m.Importe},ct,tx));
        return id;
    }

    public Task<IReadOnlyList<long>> ConfirmarVariasAsync(TarjetasConfirmarVarias solicitud,CancellationToken ct=default)
        =>Ejecutar<IReadOnlyList<long>>("ConfirmarVariasIA",async(c,t)=>
        {
            using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            var ids=await ConfirmarVariasEnTransaccionAsync(c,tx,solicitud,t);
            tx.Commit();return ids;
        },ct);

    internal static IReadOnlyList<(GrupoConfirmacion Grupo,string Motivo)> PrepararConfirmaciones(TarjetasComparacion actual,TarjetasConfirmarVarias solicitud)
    {
        if(solicitud.Propuestas.Count is 0 or >10000||solicitud.Motivo.Length>200
            ||solicitud.Propuestas.Sum(p=>(long)p.Detalles.Count)>10000||solicitud.Propuestas.Sum(p=>(long)p.Movimientos.Count)>10000)
            throw new InvalidOperationException("Revisá las propuestas seleccionadas: deben estar dentro de los 10.000 movimientos consultados por lado, con un motivo de hasta 200 caracteres.");
        var ds=new HashSet<long>();var ms=new HashSet<string>();var resultado=new List<(GrupoConfirmacion,string)>();
        foreach(var p in solicitud.Propuestas)
        {
            if(p.Detalles.Count is 0 or >20||p.Movimientos.Count is 0 or >20||string.IsNullOrWhiteSpace(p.Evidencia)||p.Evidencia.Length>500
                ||p.Detalles.Any(d=>!ds.Add(d))||p.Movimientos.Any(m=>!ms.Add(m.Clave)))
                throw new InvalidOperationException("Las propuestas tienen registros repetidos o exceden los 20 movimientos por lado de un grupo de IA. Volvé a analizar.");
            var motivo=(solicitud.Motivo.Trim().Length>0?solicitud.Motivo.Trim()+" · ":"")+"IA · "+p.Evidencia;
            if(motivo.Length>500)motivo=motivo[..500];
            var grupo=ResolverConfirmacion(actual,new(solicitud.Filtro,p.Detalles,p.Movimientos,motivo));
            if(grupo.Total!=grupo.Alfa&&(!solicitud.AceptarDiferencias||string.IsNullOrWhiteSpace(solicitud.Motivo)))
                throw new InvalidOperationException("Aceptá expresamente las diferencias y escribí un motivo para confirmar las propuestas que difieren en importe.");
            resultado.Add((grupo,motivo));
        }
        return resultado;
    }

    internal async Task<IReadOnlyList<long>> ConfirmarVariasEnTransaccionAsync(SqlConnection c,SqlTransaction tx,TarjetasConfirmarVarias solicitud,CancellationToken ct)
    {
        var session=sessions.GetActiveSession()!.Id;
        var actual=await Comparar(c,solicitud.Filtro with{Estado="Todos",Buscar=""},ct,tx);
        // Validar todos los grupos antes del primer INSERT. Un fallo nunca confirma solo parte del lote.
        var grupos=PrepararConfirmaciones(actual,solicitud);var ids=new List<long>();
        foreach(var (grupo,motivo) in grupos)
        {
            AntesDeGrabar(session,ct);
            ids.Add(await InsertarVinculo(c,tx,actual,grupo,motivo,ct));
        }
        AntesDeGrabar(session,ct);return ids;
    }
}

using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    public Task<TarjetasIaResultado> AnalizarIaAsync(TarjetasFiltro filtro,CancellationToken ct=default,Func<int,int,Task>? progreso=null)=>Ejecutar("AsistirIA",async(c,t)=>
    {
        var session=sessions.GetActiveSession()!.Id;
        var datos=await Comparar(c,filtro with{Estado="Pendientes"},t);
        if(!datos.Importacion.DetalleCompleto)throw new InvalidOperationException("Revisá el detalle del PDF: no coincide con el bruto. No se puede proponer una conciliación.");
        var dias=EnteroIa(await Config(c,"TARJETAS-IA-DIAS",t),3,0,31);
        var maximo=EnteroIa(await Config(c,"TARJETAS-IA-MAX-FILAS",t),2000,50,2000);
        var lotes=TarjetasIaLotes.Preparar(datos,dias,maximo);
        var analizables=lotes.Where(l=>l.Detalles.Count>0&&l.Movimientos.Count>0).ToList();
        if(analizables.Count==0)throw new InvalidOperationException("No hay pendientes compatibles por fecha en ambos lados para analizar. Revisá el período, sucursal y margen de días.");
        if(iaModelo is null)throw new InvalidOperationException("La conexión de IA no está disponible en este servidor.");
        var resultados=new List<TarjetasIaResultado>();
        for(var i=0;i<analizables.Count;i++)
        {
            AntesDeGrabar(session,t);
            if(progreso is not null)await progreso(i+1,analizables.Count);
            var contexto=TarjetasIaPropuestas.Contexto(analizables[i],filtro,dias,maximo);
            var respuesta=await iaModelo.AnalizarAsync(contexto,"",t);
            AntesDeGrabar(session,t);
            resultados.Add(TarjetasIaPropuestas.Validar(analizables[i],contexto,respuesta));
        }
        return TarjetasIaLotes.Unir(lotes,resultados,datos,dias);
    },ct);

    private static int EnteroIa(string valor,int inicial,int minimo,int maximo)=>string.IsNullOrWhiteSpace(valor)?inicial:
        int.TryParse(valor,out var n)&&n>=minimo&&n<=maximo?n:throw new InvalidOperationException("Revisá los parámetros de IA de tarjetas: días entre 0 y 31; máximo de filas entre 50 y 2000.");
}

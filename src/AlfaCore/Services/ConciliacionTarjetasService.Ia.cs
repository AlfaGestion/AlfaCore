using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    public Task<TarjetasIaResultado> AnalizarIaAsync(TarjetasFiltro filtro,CancellationToken ct=default)=>Ejecutar("AsistirIA",async(c,t)=>
    {
        var session=sessions.GetActiveSession()!.Id;
        var datos=await Comparar(c,filtro with{Estado="Pendientes"},t);
        if(!datos.Importacion.DetalleCompleto)throw new InvalidOperationException("Revisá el detalle del PDF: no coincide con el bruto. No se puede proponer una conciliación.");
        var dias=EnteroIa(await Config(c,"TARJETAS-IA-DIAS",t),3,0,31);
        var maximo=EnteroIa(await Config(c,"TARJETAS-IA-MAX-FILAS",t),2000,50,2000);
        var contexto=TarjetasIaPropuestas.Contexto(datos,filtro,dias,maximo);
        if(contexto.Banco.Count==0||contexto.Alfa.Count==0)throw new InvalidOperationException("No hay operaciones pendientes en ambos lados para analizar con estos filtros.");
        if(iaModelo is null)throw new InvalidOperationException("La conexión de IA no está disponible en este servidor.");
        var respuesta=await iaModelo.AnalizarAsync(contexto,"",t);
        AntesDeGrabar(session,t);
        return TarjetasIaPropuestas.Validar(datos,contexto,respuesta);
    },ct);

    private static int EnteroIa(string valor,int inicial,int minimo,int maximo)=>string.IsNullOrWhiteSpace(valor)?inicial:
        int.TryParse(valor,out var n)&&n>=minimo&&n<=maximo?n:throw new InvalidOperationException("Revisá los parámetros de IA de tarjetas: días entre 0 y 31; máximo de filas entre 50 y 2000.");
}

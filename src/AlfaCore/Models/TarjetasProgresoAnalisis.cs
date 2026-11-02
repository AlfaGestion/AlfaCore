namespace AlfaCore.Models;

public sealed record TarjetasProgresoAnalisis(int Paso,string Actividad,int Intentos=0,
    int LoteActual=0,int TotalLotes=0,int LotesCompletados=0,bool Finalizado=false)
{
    public int EtapasCompletadas=>Finalizado?4:Math.Clamp(Paso-1,0,3);
    public int LotesPendientes=>Math.Max(0,TotalLotes-LotesCompletados);
    public string Titulo=>$"Paso {Paso} de 4 · "+(Paso switch{1=>"Consultar movimientos",2=>"Buscar agrupaciones",3=>"Analizar casos restantes",_=>"Preparar resultados"});
}

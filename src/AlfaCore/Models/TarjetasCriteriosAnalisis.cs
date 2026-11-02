namespace AlfaCore.Models;

public sealed record TarjetasCriteriosAnalisis
{
    public bool Automatico {get;init;}=true;
    public int MaxDiasGrupo {get;init;}=3;
    public int MaxIntentosAutomaticos {get;init;}=1000000;
    public int Dias {get;init;}=3;
    public int MaxFilasIa {get;init;}=2000;
    public int MaxMovimientosGrupo {get;init;}=200;
    public int MaxLotesCombinados {get;init;}=4;
    public int MaxIntentos {get;init;}=100000;
    public bool BuscarSubconjuntos {get;init;}=true;
    public bool ConsultarIa {get;init;}=true;
    public void Validar()
    {
        if(MaxDiasGrupo is <1 or >7||MaxIntentosAutomaticos is <100 or >1000000||Dias is <0 or >31||MaxFilasIa is <50 or >2000||MaxMovimientosGrupo is <1 or >500
            ||MaxLotesCombinados is <1 or >6||MaxIntentos is <100 or >1000000)
            throw new InvalidOperationException("Revisá los criterios: días agrupados 1 a 7; intentos automáticos 100 a 1.000.000; días 0 a 31; filas IA 50 a 2000; movimientos por día/grupo 1 a 500; lotes combinados 1 a 6; intentos 100 a 1.000.000.");
    }
}

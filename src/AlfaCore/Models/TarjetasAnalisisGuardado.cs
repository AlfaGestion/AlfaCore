namespace AlfaCore.Models;

// Guarda propuestas, nunca selecciones aceptadas ni autorizaciones para conciliar.
public sealed record TarjetasAnalisisGuardado(int Version,string Cuenta,TarjetasFiltro Filtro,
    TarjetasCriteriosAnalisis Criterios,TarjetasIaResultado Resultado);
public sealed record TarjetasAnalisisReferencia(long Id,DateTime FechaUtc);
public sealed record TarjetasAnalisisRecuperado(TarjetasAnalisisReferencia Referencia,TarjetasFiltro Filtro,
    TarjetasCriteriosAnalisis Criterios,TarjetasComparacion Comparacion,TarjetasIaResultado Resultado,int Omitidas);

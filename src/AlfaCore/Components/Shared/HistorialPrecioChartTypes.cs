namespace AlfaCore.Components.Shared;

public enum HistorialPrecioChartModo { Lineas, Barras }

/// <summary>Clase: hpc-s1, hpc-s2 o hpc-s3, siempre en ese orden para que cada dato conserve su color.</summary>
public sealed record HistorialPrecioSerie(string Nombre, string Clase, IReadOnlyList<double?> Valores, string? Resumen = null);

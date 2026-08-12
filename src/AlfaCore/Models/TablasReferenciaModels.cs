namespace AlfaCore.Models;

/// <summary>Una tabla de referencia registrada para el editor genérico de Archivos &gt; Tablas.</summary>
public sealed class TablaReferenciaDto
{
    public string Clave { get; set; } = string.Empty;
    public string NombreVisible { get; set; } = string.Empty;
    public bool TieneColor { get; set; }
}

/// <summary>Una fila de la tabla física detrás de una <see cref="TablaReferenciaDto"/>.</summary>
public sealed class TablaReferenciaFilaDto
{
    /// <summary>Estas tablas se identifican por su Código (no tienen Id entero).</summary>
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    /// <summary>Color en formato "#RRGGBB", o null si la tabla no tiene columna de color.</summary>
    public string? ColorHex { get; set; }
}

public sealed class GuardarFilaTablaReferenciaRequest
{
    /// <summary>Null/vacío = alta nueva; con valor = edición de la fila con ese código.</summary>
    public string? CodigoOriginal { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
}

namespace AlfaCore.Models;

/// <summary>
/// Política de precios (V_TA_POLITICAPRECIOS). Con algún porcentaje distinto de 0 calcula cada clase como costo + %;
/// con todos en 0 usa las fórmulas (costo y clases 1..8, en ese orden). La aplican los triggers de la base.
/// </summary>
public sealed class PoliticaPrecioDto
{
    /// <summary>Vacío = alta.</summary>
    public string CodigoOriginal { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    /// <summary>Porcentaje sobre el costo de las clases 1..8 (índice 0 = clase 1).</summary>
    public decimal[] Porcentajes { get; set; } = new decimal[8];
    /// <summary>Fórmula del costo (FCOSTO).</summary>
    public string FormulaCosto { get; set; } = "<COSTO>";
    /// <summary>Fórmulas de las clases 1..8 (índice 0 = FCLASE1).</summary>
    public string[] Formulas { get; set; } = Enumerable.Range(1, 8).Select(c => $"<CLASE{c}>").ToArray();
    public int Articulos { get; set; }
    public int FilasListas { get; set; }

    public bool UsaFormulas => Porcentajes.All(x => x == 0);
}

public sealed class PoliticaPrecioContextoDto
{
    public List<PoliticaPrecioDto> Politicas { get; set; } = [];
    public List<string> NombresClases { get; set; } = [];
    public bool PuedeEditar { get; set; }
    /// <summary>MaestroArticuloConIva=SI y MaestroArticuloCostoConIVA≠SI: los porcentajes suman el IVA (como el trigger).</summary>
    public bool PorcentajeSumaIva { get; set; }
    public decimal TasaIvaDefault { get; set; } = 21;
}

/// <summary>Cómo queda un artículo con la política (lo calcula AlfaCore con la misma regla que el trigger).</summary>
public sealed class PoliticaPrecioPreviewDto
{
    public string IdArticulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal CostoActual { get; set; }
    public decimal CostoNuevo { get; set; }
    public decimal[] PreciosActuales { get; set; } = new decimal[8];
    public decimal[] PreciosNuevos { get; set; } = new decimal[8];
    public string? Error { get; set; }
}

public sealed class PoliticaPrecioAplicarResultado
{
    public int Articulos { get; set; }
    public int FilasListas { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}

namespace AlfaCore.Models;

// Fórmula de producción del artículo (solapa "Insumos" de Ma_art.frm en Alfa Gestión).
// Se guarda en las mismas tablas que el legacy, así VB6 y AlfaCore ven la misma fórmula:
//   V_MA_INSUMOS        (IdArticulo, IdArticuloInsumo, IdUnidadInsumo, Cantidad, ClasePr)
//   V_MA_INSUMOSTAREAS  (IdArticulo, IdTarea, DESCRIPCION, Minutos, Segundos)
//   V_MA_ARTICULOS      ActualizaCosto, InsumosPorPorcentaje, CostoInsumos, Procedencia = 'PRODUCCION'.
// Se llama "Fórmula" y no "Insumos" porque en AlfaCore V_MV_CPTEINSUMOS son las líneas de los comprobantes.

/// <summary>Fórmula completa de un artículo, tal como se edita en la solapa.</summary>
public sealed class ArticuloFormulaDto
{
    /// <summary>InsumosPorPorcentaje: la cantidad de cada insumo es un % (deben sumar 100). Se usa para
    /// partir el stock (ej. media res); como en el legacy, en este modo los insumos no suman costo.</summary>
    public bool PorPorcentaje { get; set; }

    /// <summary>ActualizaCosto ("Calcular costo automáticamente"): el COSTO del artículo es el costo de la
    /// fórmula y se recalcula cuando cambia el costo de un insumo.</summary>
    public bool CalcularCostoAutomatico { get; set; }

    /// <summary>Procedencia = 'PRODUCCION': los insumos se descuentan del stock solo al producir, no al vender.</summary>
    public bool DescuentaSoloPorProduccion { get; set; }

    public List<ArticuloFormulaInsumoDto> Insumos { get; set; } = [];
    public List<ArticuloFormulaTareaDto> Tareas { get; set; } = [];

    public decimal CostoInsumos => ArticuloFormulaCalculo.CostoTotal(this);

    /// <summary>El costo automático solo vale con fórmula cargada: sin insumos ni tareas no se pisa el costo con 0
    /// (hay bases con ActualizaCosto = 1 en artículos sin fórmula).</summary>
    public bool CostoAutomaticoVigente => CalcularCostoAutomatico && (Insumos.Count > 0 || Tareas.Count > 0);
}

public sealed class ArticuloFormulaInsumoDto
{
    /// <summary>Código del insumo. Si empieza con '*' es un insumo de texto libre (sin artículo, costo 0).</summary>
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    /// <summary>Unidad en la que se expresa la cantidad. Si no es la del insumo se convierte con S_TA_EQUIV.</summary>
    public string Unidad { get; set; } = string.Empty;
    /// <summary>Cantidad (o % si la fórmula es por porcentaje).</summary>
    public decimal Cantidad { get; set; }
    /// <summary>Valor del insumo que se toma: 0 = costo, 1..8 = PRECIOn del insumo.</summary>
    public int ClasePr { get; set; }
    /// <summary>Valor del insumo por unidad del renglón (ya convertido por equivalencia). Lo calcula el servidor.</summary>
    public decimal CostoUnitario { get; set; }
    /// <summary>Problema que impide calcular o grabar el renglón (no existe, sin equivalencia...); vacío si está bien.</summary>
    public string Error { get; set; } = string.Empty;
    /// <summary>Aviso que no impide grabar (ej. insumo en otra moneda: no se convierte).</summary>
    public string Aviso { get; set; } = string.Empty;

    public bool EsTextoLibre => Codigo.TrimStart().StartsWith('*');
    public decimal Costo => ArticuloFormulaCalculo.CostoRenglon(CostoUnitario, Cantidad);
}

public sealed class ArticuloFormulaTareaDto
{
    public string IdTarea { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Minutos { get; set; }
    public int Segundos { get; set; }
    /// <summary>V_TA_TAREAS.ValorCosto: costo por hora de la tarea.</summary>
    public decimal ValorHora { get; set; }

    public decimal Costo => ArticuloFormulaCalculo.CostoTarea(Minutos, Segundos, ValorHora);
}

/// <summary>Resultado de grabar un artículo: su código y lo que cambió en cascada.</summary>
public sealed class ArticuloGuardadoResultado
{
    public string Codigo { get; set; } = string.Empty;
    public ArticuloCascadaResultado Cascada { get; set; } = new();
    /// <summary>Los precios nuevos del artículo quedaron en el borrador de precios (PreciosArticulosPorBorrador=SI).</summary>
    public bool PreciosAlBorrador { get; set; }
}

/// <summary>Resultado (o vista previa) de la cascada de costos por fórmula.</summary>
public sealed class ArticuloCascadaResultado
{
    /// <summary>PreciosArticulosPorBorrador=SI: los costos se grabaron, los precios de venta quedaron en el borrador.</summary>
    public bool AlBorrador { get; set; }
    public List<ArticuloCascadaCambio> Cambios { get; set; } = [];
    /// <summary>Lo que no se pudo recalcular o se calculó con algún reparo (ciclos, insumos inexistentes...).</summary>
    public List<string> Observaciones { get; set; } = [];
}

public sealed class ArticuloCascadaCambio
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal CostoAnterior { get; set; }
    public decimal CostoNuevo { get; set; }
    /// <summary>Precio de venta del maestro (clase ClasePrecioVenta) antes y después (en borrador: el propuesto).</summary>
    public decimal PrecioVentaAnterior { get; set; }
    public decimal PrecioVentaNuevo { get; set; }
    /// <summary>Listas de precios cuyo precio se recalculó (o se mandó al borrador).</summary>
    public int Listas { get; set; }
}

/// <summary>Tarea disponible (V_TA_TAREAS) para la grilla de tareas de la fórmula.</summary>
public sealed class ArticuloTareaOptionDto
{
    public string IdTarea { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal ValorHora { get; set; }
}

/// <summary>Sentido de la equivalencia de unidades encontrada en S_TA_EQUIV (IdArticuloTieneEquivalencias de Rt_Stock.bas).</summary>
public enum EquivalenciaSentido
{
    /// <summary>Misma unidad: no hay conversión.</summary>
    Ninguna,
    /// <summary>Fila (unidad del insumo → unidad del renglón): 1 unidad del insumo = coef unidades del renglón; el valor se divide.</summary>
    Dividir,
    /// <summary>Fila (unidad del renglón → unidad del insumo): 1 unidad del renglón = coef unidades del insumo; el valor se multiplica.</summary>
    Multiplicar
}

/// <summary>Cálculos de la fórmula, sin base de datos, para poder testearlos. Mismo criterio que Ma_art.frm.</summary>
public static class ArticuloFormulaCalculo
{
    public const decimal TotalPorcentaje = 100m;

    /// <summary>Valor del insumo en la unidad del renglón (ImporteSegunTipoyCoeficiente de Rt_Stock.bas).</summary>
    public static decimal ValorEnUnidad(decimal valor, EquivalenciaSentido sentido, decimal coeficiente)
    {
        if (valor == 0 || coeficiente == 0 || sentido == EquivalenciaSentido.Ninguna)
            return valor;
        return sentido == EquivalenciaSentido.Dividir ? valor / coeficiente : valor * coeficiente;
    }

    public static decimal CostoRenglon(decimal costoUnitario, decimal cantidad)
        => Math.Round(costoUnitario * cantidad, 4, MidpointRounding.AwayFromZero);

    /// <summary>CalculaHora de RT_ARTICULOS.bas: (minutos + segundos/60) / 60 horas × valor hora.</summary>
    public static decimal CostoTarea(decimal minutos, int segundos, decimal valorHora)
        => Math.Round((minutos + segundos / 60m) / 60m * valorHora, 4, MidpointRounding.AwayFromZero);

    /// <summary>CalculaCostoConInsumos de Ma_art.frm: insumos (salvo por porcentaje) + tareas, a 2 decimales.</summary>
    public static decimal CostoTotal(ArticuloFormulaDto formula)
    {
        var insumos = formula.PorPorcentaje ? 0m : formula.Insumos.Sum(i => i.Costo);
        var tareas = formula.Tareas.Sum(t => t.Costo);
        return Math.Round(insumos + tareas, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Errores de la fórmula que se pueden ver sin base (cantidades, porcentajes, repetidos).</summary>
    public static List<string> Validar(ArticuloFormulaDto formula)
    {
        var errores = new List<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in formula.Insumos)
        {
            var codigo = i.Codigo.Trim();
            if (codigo.Length == 0)
            {
                errores.Add("Hay un insumo sin código.");
                continue;
            }
            if (i.Cantidad <= 0)
                errores.Add($"{codigo}: la cantidad tiene que ser mayor a 0.");
            if (i.ClasePr is < 0 or > 8)
                errores.Add($"{codigo}: la clase de precio va de 0 (costo) a 8.");
            if (!vistos.Add(codigo))
                errores.Add($"{codigo}: está repetido en la fórmula.");
        }
        if (formula.PorPorcentaje && formula.Insumos.Count > 0)
        {
            var suma = formula.Insumos.Sum(i => i.Cantidad);
            if (suma != TotalPorcentaje)
                errores.Add($"Los porcentajes de los insumos tienen que sumar 100 (suman {suma:0.##}).");
        }
        foreach (var t in formula.Tareas)
        {
            if (t.IdTarea.Trim().Length == 0)
                errores.Add("Hay una tarea sin código.");
            else if (t.Segundos is < 0 or > 60)
                errores.Add($"Tarea {t.IdTarea.Trim()}: los segundos van de 0 a 60.");
            else if (t.Minutos < 0)
                errores.Add($"Tarea {t.IdTarea.Trim()}: los minutos no pueden ser negativos.");
        }
        return errores;
    }

    /// <summary>
    /// Cascada de costos (reemplaza ReCalculaCostoConInsumos de RT_ARTICULOS.bas): a partir de los artículos cuyo
    /// valor cambió, devuelve los productos a recalcular en orden (cada uno después de todos sus insumos afectados),
    /// una sola vez cada uno. Solo se recalculan los productos con "Calcular costo automáticamente"; la cadena
    /// sigue por ellos y se corta en los que no lo tienen. Los que forman un ciclo quedan aparte, sin recalcular.
    /// </summary>
    public static (List<string> Orden, List<string> EnCiclo) OrdenCascada(
        IEnumerable<string> origenes,
        Func<string, IEnumerable<string>> productosQueLoUsan,
        Func<string, bool> calculaCostoAutomatico)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var afectados = new HashSet<string>(comparer);
        var cola = new Queue<string>();
        foreach (var o in origenes.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(comparer))
            cola.Enqueue(o);
        while (cola.Count > 0)
        {
            var actual = cola.Dequeue();
            foreach (var padre in productosQueLoUsan(actual))
                if (calculaCostoAutomatico(padre) && afectados.Add(padre))
                    cola.Enqueue(padre);
        }

        // Orden topológico (Kahn) dentro de los afectados: un producto espera a sus insumos afectados.
        var pendientes = afectados.ToDictionary(a => a, _ => 0, comparer);
        var hijosAfectados = afectados.ToDictionary(a => a, _ => new List<string>(), comparer);
        foreach (var insumo in afectados)
            foreach (var padre in productosQueLoUsan(insumo).Distinct(comparer))
                if (afectados.Contains(padre))
                {
                    pendientes[padre]++;
                    hijosAfectados[insumo].Add(padre);
                }
        var listos = new Queue<string>(pendientes.Where(p => p.Value == 0).Select(p => p.Key).OrderBy(k => k, comparer));
        var orden = new List<string>();
        while (listos.Count > 0)
        {
            var actual = listos.Dequeue();
            orden.Add(actual);
            foreach (var padre in hijosAfectados[actual])
                if (--pendientes[padre] == 0)
                    listos.Enqueue(padre);
        }
        var enCiclo = afectados.Where(a => !orden.Contains(a, comparer)).OrderBy(a => a, comparer).ToList();
        return (orden, enCiclo);
    }

    /// <summary>
    /// Ciclos: el producto no puede estar entre los insumos (directos o indirectos) de sus propios insumos.
    /// <paramref name="insumosDe"/> devuelve los insumos directos de cada artículo según la base.
    /// Devuelve el camino del ciclo (producto → ... → producto) o null si no hay.
    /// </summary>
    public static IReadOnlyList<string>? BuscarCiclo(
        string producto,
        IEnumerable<string> insumosDirectos,
        Func<string, IEnumerable<string>> insumosDe)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var visitados = new HashSet<string>(comparer);
        var padre = new Dictionary<string, string>(comparer);
        var cola = new Queue<string>();
        foreach (var d in insumosDirectos)
        {
            if (comparer.Equals(d, producto))
                return [producto, producto];
            if (visitados.Add(d))
            {
                padre[d] = producto;
                cola.Enqueue(d);
            }
        }
        while (cola.Count > 0)
        {
            var actual = cola.Dequeue();
            foreach (var hijo in insumosDe(actual))
            {
                if (comparer.Equals(hijo, producto))
                {
                    var camino = new List<string> { producto, actual };
                    var p = actual;
                    while (padre.TryGetValue(p, out var anterior) && !comparer.Equals(anterior, producto))
                    {
                        camino.Insert(1, anterior);
                        p = anterior;
                    }
                    camino.Add(producto);
                    return camino;
                }
                if (visitados.Add(hijo))
                {
                    padre[hijo] = actual;
                    cola.Enqueue(hijo);
                }
            }
        }
        return null;
    }
}

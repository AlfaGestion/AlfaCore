using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Solapa "Fórmula" del editor de artículos: port de la solapa Insumos de Ma_art.frm (Alfa Gestión).
// Mismas tablas que el legacy (V_MA_INSUMOS / V_MA_INSUMOSTAREAS / V_TA_TAREAS) para que VB6 y AlfaCore
// compartan la fórmula. Diferencias a propósito con el legacy:
//  - se rechazan los ciclos (A insumo de B y B insumo de A): el legacy entraba en un bucle infinito;
//  - el valor de cada insumo respeta ClasePr también fuera de la pantalla (ResolverInsumosAsync es la
//    única rutina de valuación: la usa el editor y la va a usar la cascada de costos).
public sealed partial class ArticulosService
{
    private const string ProcedenciaProduccion = "PRODUCCION";
    private const int MaxRenglonesFormula = 500;

    public Task<ArticuloFormulaDto> GetFormulaAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetFormula", async token =>
        {
            var normalizado = (codigo ?? string.Empty).Trim();
            if (normalizado.Length == 0)
                return new ArticuloFormulaDto();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await LeerFormulaAsync(cn, null, normalizado, token);
        }, "No se pudo cargar la fórmula del artículo.", ct);

    /// <summary>Fórmula guardada de un artículo con cada insumo y tarea ya valuados (editor y cascada).</summary>
    internal static async Task<ArticuloFormulaDto> LeerFormulaAsync(SqlConnection cn, SqlTransaction? tx, string normalizado, CancellationToken token)
    {
        var formula = new ArticuloFormulaDto();
        var cols = await LeerEsquemaFormulaAsync(cn, tx, token);
        if (!cols.TablaInsumos)
            return formula;

        var cab = await cn.QuerySingleOrDefaultAsync<FormulaCabeceraFila>(new CommandDefinition($"""
            SELECT TOP (1)
                {(cols.ActualizaCosto ? "CONVERT(bit, ISNULL(ActualizaCosto, 0))" : "CAST(0 AS bit)")} AS CalcularCostoAutomatico,
                {(cols.InsumosPorPorcentaje ? "CONVERT(bit, ISNULL(InsumosPorPorcentaje, 0))" : "CAST(0 AS bit)")} AS PorPorcentaje,
                {(cols.Procedencia ? "CONVERT(bit, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(Procedencia, '')))) = 'PRODUCCION' THEN 1 ELSE 0 END)" : "CAST(0 AS bit)")} AS DescuentaSoloPorProduccion,
                ISNULL(LTRIM(RTRIM(Moneda)), '') AS Moneda
            FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO)) = @Codigo;
            """, new { Codigo = normalizado }, tx, cancellationToken: token));
        if (cab is null)
            return formula;
        formula.CalcularCostoAutomatico = cab.CalcularCostoAutomatico;
        formula.PorPorcentaje = cab.PorPorcentaje;
        formula.DescuentaSoloPorProduccion = cab.DescuentaSoloPorProduccion;

        var renglones = await cn.QueryAsync<(string Insumo, string Unidad, double? Cantidad, int? ClasePr)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(IdArticuloInsumo)), LTRIM(RTRIM(ISNULL(IdUnidadInsumo, ''))), Cantidad, ClasePr
            FROM dbo.V_MA_INSUMOS
            WHERE LTRIM(RTRIM(IdArticulo)) = @Codigo
            ORDER BY ID;
            """, new { Codigo = normalizado }, tx, cancellationToken: token));
        foreach (var r in renglones)
        {
            var textoLibre = r.Insumo.StartsWith('*');
            formula.Insumos.Add(new ArticuloFormulaInsumoDto
            {
                Codigo = textoLibre ? "*" : r.Insumo,
                Descripcion = textoLibre ? r.Insumo[1..].Trim() : string.Empty,
                Unidad = r.Unidad,
                Cantidad = (decimal)(r.Cantidad ?? 0),
                ClasePr = r.ClasePr ?? 0
            });
        }
        await ResolverInsumosAsync(cn, tx, cab.Moneda, formula.Insumos, token);

        if (cols.TablaTareas)
        {
            var tareas = await cn.QueryAsync<ArticuloFormulaTareaDto>(new CommandDefinition($"""
                SELECT LTRIM(RTRIM(it.IdTarea)) AS IdTarea,
                    ISNULL(NULLIF(LTRIM(RTRIM(it.DESCRIPCION)), ''), ISNULL(t.Descripcion, '')) AS Descripcion,
                    CONVERT(decimal(18, 4), ISNULL(it.Minutos, 0)) AS Minutos,
                    ISNULL(it.Segundos, 0) AS Segundos,
                    {(cols.TablaTareasCatalogo ? "CONVERT(decimal(18, 4), ISNULL(t.ValorCosto, 0))" : "CAST(0 AS decimal(18, 4))")} AS ValorHora
                FROM dbo.V_MA_INSUMOSTAREAS it
                {(cols.TablaTareasCatalogo ? "LEFT JOIN dbo.V_TA_TAREAS t ON LTRIM(RTRIM(t.IdTarea)) = LTRIM(RTRIM(it.IdTarea))" : "CROSS APPLY (SELECT CAST('' AS nvarchar(1)) AS Descripcion) t")}
                WHERE LTRIM(RTRIM(it.IdArticulo)) = @Codigo
                ORDER BY it.ID;
                """, new { Codigo = normalizado }, tx, cancellationToken: token));
            formula.Tareas.AddRange(tareas);
        }
        return formula;
    }

    public Task<ArticuloFormulaInsumoDto> ResolverInsumoAsync(string? codigoProducto, ArticuloFormulaInsumoDto renglon, CancellationToken ct = default)
        => ExecuteLoggedAsync("ResolverInsumo", async token =>
        {
            ArgumentNullException.ThrowIfNull(renglon);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var producto = (codigoProducto ?? string.Empty).Trim();
            var moneda = producto.Length == 0 ? string.Empty : await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT TOP (1) ISNULL(LTRIM(RTRIM(Moneda)), '') FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO)) = @Codigo;",
                new { Codigo = producto }, cancellationToken: token)) ?? string.Empty;
            var copia = new ArticuloFormulaInsumoDto
            {
                Codigo = renglon.Codigo.Trim(),
                Descripcion = renglon.Descripcion,
                Unidad = renglon.Unidad.Trim(),
                Cantidad = renglon.Cantidad,
                ClasePr = renglon.ClasePr
            };
            await ResolverInsumosAsync(cn, null, moneda, [copia], token);
            if (producto.Length > 0 && string.Equals(copia.Codigo, producto, StringComparison.OrdinalIgnoreCase))
                copia.Error = "Un artículo no puede ser insumo de sí mismo.";
            return copia;
        }, "No se pudo calcular el insumo.", ct);

    public Task<IReadOnlyList<StockMovimientoArticulo>> BuscarInsumosAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<StockMovimientoArticulo>>("BuscarInsumos", async token =>
        {
            var t = (texto ?? string.Empty).Trim();
            if (t.Length > 100)
                throw new InvalidOperationException("El texto de búsqueda es demasiado largo.");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = await cn.QueryAsync<StockMovimientoArticulo>(new CommandDefinition("""
                SELECT TOP (60)
                    LTRIM(RTRIM(IDARTICULO)) AS Codigo,
                    ISNULL(DESCRIPCION, '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(IDUNIDAD)), '') AS Unidad,
                    CONVERT(decimal(18, 4), ISNULL(COSTO, 0)) AS Costo,
                    CONVERT(decimal(18, 4), ISNULL(COSTO, 0)) AS Stock
                FROM dbo.V_MA_ARTICULOS
                WHERE ISNULL(SUSPENDIDO, 0) = 0
                  AND (@Texto = ''
                       OR LTRIM(RTRIM(IDARTICULO)) = @Texto
                       OR LTRIM(RTRIM(ISNULL(CODIGOBARRA, ''))) = @Texto
                       OR DESCRIPCION LIKE '%' + @Texto + '%')
                ORDER BY CASE WHEN LTRIM(RTRIM(IDARTICULO)) = @Texto OR LTRIM(RTRIM(ISNULL(CODIGOBARRA, ''))) = @Texto THEN 0 ELSE 1 END,
                         DESCRIPCION;
                """, new { Texto = t }, cancellationToken: token));
            return (IReadOnlyList<StockMovimientoArticulo>)rows.ToList();
        }, "No se pudieron buscar los insumos.", ct);

    /// <summary>
    /// Única rutina de valuación de insumos (editor y cascada). Completa en cada renglón la descripción, la unidad
    /// (la del insumo si viene vacía), el valor por unidad del renglón y el error/aviso. Valor = COSTO (ClasePr 0)
    /// o PRECIOn (ClasePr n) del insumo, convertido con S_TA_EQUIV si la unidad del renglón no es la del insumo
    /// (IdArticuloTieneEquivalencias + ImporteSegunTipoyCoeficiente de Rt_Stock.bas).
    /// </summary>
    internal static async Task ResolverInsumosAsync(
        SqlConnection cn, SqlTransaction? tx, string monedaProducto, IReadOnlyList<ArticuloFormulaInsumoDto> renglones, CancellationToken ct)
    {
        var codigos = renglones.Where(r => !r.EsTextoLibre && r.Codigo.Trim().Length > 0)
            .Select(r => r.Codigo.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var articulos = new Dictionary<string, InsumoArticuloFila>(StringComparer.OrdinalIgnoreCase);
        var equivalencias = new List<EquivalenciaFila>();
        foreach (var lote in codigos.Chunk(500))
        {
            foreach (var a in await cn.QueryAsync<InsumoArticuloFila>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IDARTICULO)) AS Codigo, ISNULL(DESCRIPCION, '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(IDUNIDAD)), '') AS Unidad, ISNULL(LTRIM(RTRIM(Moneda)), '') AS Moneda,
                    CONVERT(bit, ISNULL(SUSPENDIDO, 0)) AS Suspendido,
                    ISNULL(COSTO, 0) AS P0, ISNULL(PRECIO1, 0) AS P1, ISNULL(PRECIO2, 0) AS P2, ISNULL(PRECIO3, 0) AS P3,
                    ISNULL(PRECIO4, 0) AS P4, ISNULL(PRECIO5, 0) AS P5, ISNULL(PRECIO6, 0) AS P6, ISNULL(PRECIO7, 0) AS P7,
                    ISNULL(PRECIO8, 0) AS P8
                FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO)) IN @Codigos;
                """, new { Codigos = lote }, tx, cancellationToken: ct)))
                articulos[a.Codigo] = a;
            equivalencias.AddRange(await cn.QueryAsync<EquivalenciaFila>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IDARTICULO)) AS Codigo, LTRIM(RTRIM(IDUNIDAD)) AS Unidad,
                    LTRIM(RTRIM(IDUNIDAD_EQUIV)) AS UnidadEquiv, CONVERT(decimal(18, 6), COEFICIENTE) AS Coeficiente
                FROM dbo.S_TA_EQUIV WHERE LTRIM(RTRIM(IDARTICULO)) IN @Codigos AND COEFICIENTE > 0;
                """, new { Codigos = lote }, tx, cancellationToken: ct)));
        }

        foreach (var r in renglones)
        {
            r.Error = string.Empty;
            r.Aviso = string.Empty;
            r.CostoUnitario = 0;
            r.Codigo = r.Codigo.Trim();
            r.Unidad = r.Unidad.Trim();
            if (r.EsTextoLibre)
            {
                // Insumo de texto libre (IdArticuloInsumo = '*' + descripción): no tiene artículo ni costo.
                if (r.Descripcion.Trim().Length == 0)
                    r.Error = "Escribí la descripción del insumo de texto libre.";
                if (r.Unidad.Length == 0)
                    r.Error = "Indicá la unidad del insumo de texto libre.";
                continue;
            }
            if (r.Codigo.Length == 0)
                continue;
            if (!articulos.TryGetValue(r.Codigo, out var art))
            {
                r.Error = "El artículo no existe.";
                continue;
            }
            r.Codigo = art.Codigo;
            r.Descripcion = art.Descripcion;
            if (r.Unidad.Length == 0)
                r.Unidad = art.Unidad;
            if (art.Suspendido)
                r.Aviso = "El insumo está dado de baja.";
            if (monedaProducto.Length > 0 && art.Moneda.Length > 0 && !string.Equals(monedaProducto, art.Moneda, StringComparison.OrdinalIgnoreCase))
                r.Aviso = $"El insumo está en otra moneda ({art.Moneda}): su valor no se convierte.";

            var valor = art.Valor(r.ClasePr is >= 0 and <= 8 ? r.ClasePr : 0);
            if (string.Equals(r.Unidad, art.Unidad, StringComparison.OrdinalIgnoreCase) || art.Unidad.Length == 0)
            {
                r.CostoUnitario = valor;
                continue;
            }
            var eqArt = equivalencias.Where(e => string.Equals(e.Codigo, art.Codigo, StringComparison.OrdinalIgnoreCase)).ToList();
            var izquierda = eqArt.FirstOrDefault(e => Iguales(e.Unidad, art.Unidad) && Iguales(e.UnidadEquiv, r.Unidad));
            var derecha = eqArt.FirstOrDefault(e => Iguales(e.Unidad, r.Unidad) && Iguales(e.UnidadEquiv, art.Unidad));
            if (izquierda is not null)
                r.CostoUnitario = ArticuloFormulaCalculo.ValorEnUnidad(valor, EquivalenciaSentido.Dividir, izquierda.Coeficiente);
            else if (derecha is not null)
                r.CostoUnitario = ArticuloFormulaCalculo.ValorEnUnidad(valor, EquivalenciaSentido.Multiplicar, derecha.Coeficiente);
            else
                r.Error = $"No tiene equivalencia informada entre {art.Unidad} y {r.Unidad}.";
        }

        static bool Iguales(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Graba la fórmula dentro de la transacción del artículo (delete + insert, como Ma_art.frm) y los indicadores
    /// del maestro. Devuelve el costo de la fórmula. Valida renglones, unidades, tareas y ciclos.
    /// </summary>
    internal static async Task<decimal> GuardarFormulaAsync(
        SqlConnection cn, SqlTransaction tx, string codigoProducto, ArticuloFormulaDto formula, CancellationToken ct)
    {
        var cols = await LeerEsquemaFormulaAsync(cn, tx, ct);
        if (!cols.TablaInsumos)
        {
            if (formula.Insumos.Count > 0 || formula.Tareas.Count > 0)
                throw new InvalidOperationException("Esta base no tiene la tabla de fórmulas (V_MA_INSUMOS).");
            return 0;
        }
        if (formula.Insumos.Count + formula.Tareas.Count > MaxRenglonesFormula)
            throw new InvalidOperationException($"La fórmula admite hasta {MaxRenglonesFormula} renglones.");
        if (formula.Tareas.Count > 0 && !cols.TablaTareas)
            throw new InvalidOperationException("Esta base no tiene la tabla de tareas de la fórmula (V_MA_INSUMOSTAREAS).");

        var producto = await cn.QuerySingleOrDefaultAsync<(string IdArticulo, string Moneda)>(new CommandDefinition("""
            SELECT TOP (1) IDARTICULO, ISNULL(LTRIM(RTRIM(Moneda)), '')
            FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO)) = @Codigo;
            """, new { Codigo = codigoProducto.Trim() }, tx, cancellationToken: ct));
        if (producto.IdArticulo is null)
            throw new InvalidOperationException("El artículo ya no existe en la base activa.");

        var errores = ArticuloFormulaCalculo.Validar(formula);
        await ResolverInsumosAsync(cn, tx, producto.Moneda, formula.Insumos, ct);
        errores.AddRange(formula.Insumos.Where(i => i.Error.Length > 0).Select(i => $"{(i.EsTextoLibre ? i.Descripcion : i.Codigo)}: {i.Error}"));
        if (formula.Insumos.Any(i => !i.EsTextoLibre && string.Equals(i.Codigo, codigoProducto.Trim(), StringComparison.OrdinalIgnoreCase)))
            errores.Add("Un artículo no puede ser insumo de sí mismo.");

        // Unidades: IdUnidadInsumo tiene FK a V_TA_UNIDAD.
        var unidades = formula.Insumos.Select(i => i.Unidad.Trim()).Where(u => u.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unidades.Count > 0)
        {
            var existentes = (await cn.QueryAsync<string>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(IdUnidad)) FROM dbo.v_ta_unidad WHERE LTRIM(RTRIM(IdUnidad)) IN @Unidades;",
                new { Unidades = unidades }, tx, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            errores.AddRange(unidades.Where(u => !existentes.Contains(u)).Select(u => $"La unidad {u} no existe."));
        }

        // Tareas: deben existir en V_TA_TAREAS; se toma la descripción y el valor hora del catálogo.
        var catalogoTareas = new Dictionary<string, ArticuloTareaOptionDto>(StringComparer.OrdinalIgnoreCase);
        if (formula.Tareas.Count > 0)
        {
            if (!cols.TablaTareasCatalogo)
                throw new InvalidOperationException("Esta base no tiene el catálogo de tareas (V_TA_TAREAS).");
            foreach (var t in await LeerTareasAsync(cn, tx, ct))
                catalogoTareas[t.IdTarea] = t;
            foreach (var t in formula.Tareas)
            {
                if (!catalogoTareas.TryGetValue(t.IdTarea.Trim(), out var cat))
                {
                    if (t.IdTarea.Trim().Length > 0)
                        errores.Add($"La tarea {t.IdTarea.Trim()} no existe.");
                    continue;
                }
                t.IdTarea = cat.IdTarea;
                t.ValorHora = cat.ValorHora;
                if (t.Descripcion.Trim().Length == 0)
                    t.Descripcion = cat.Descripcion;
            }
        }

        // Ciclos: con la fórmula nueva, ningún insumo puede llevar (directa o indirectamente) al producto.
        var directos = formula.Insumos.Where(i => !i.EsTextoLibre && i.Codigo.Length > 0).Select(i => i.Codigo).ToList();
        if (directos.Count > 0 && errores.Count == 0)
        {
            var aristas = (await cn.QueryAsync<(string Padre, string Hijo)>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IdArticulo)), LTRIM(RTRIM(IdArticuloInsumo)) FROM dbo.V_MA_INSUMOS
                WHERE LTRIM(RTRIM(IdArticulo)) <> @Producto AND LEFT(LTRIM(IdArticuloInsumo), 1) <> '*';
                """, new { Producto = codigoProducto.Trim() }, tx, cancellationToken: ct)))
                .ToLookup(a => a.Padre, a => a.Hijo, StringComparer.OrdinalIgnoreCase);
            var ciclo = ArticuloFormulaCalculo.BuscarCiclo(codigoProducto.Trim(), directos, a => aristas[a]);
            if (ciclo is not null)
                errores.Add($"La fórmula arma un ciclo: {string.Join(" → ", ciclo)}. Un artículo no puede ser insumo de sus propios insumos.");
        }

        if (errores.Count > 0)
            throw new InvalidOperationException("Revisá la fórmula: " + string.Join(" ", errores.Distinct().Take(8)));

        await cn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.V_MA_INSUMOS WHERE IdArticulo = @Id;", new { Id = producto.IdArticulo }, tx, cancellationToken: ct));
        foreach (var i in formula.Insumos)
        {
            var insumo = i.EsTextoLibre ? Recortar("*" + i.Descripcion.Trim(), 25) : i.Codigo;
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.V_MA_INSUMOS (IdArticulo, IdUnidadInsumo, IdArticuloInsumo, Cantidad, ClasePr, CostoInsumo)
                VALUES (@Id, @Unidad, @Insumo, @Cantidad, @ClasePr, @Costo);
                """, new
            {
                Id = producto.IdArticulo,
                Unidad = CodigoPk.Format(i.Unidad.Trim(), 4),
                Insumo = insumo,
                Cantidad = (double)i.Cantidad,
                i.ClasePr,
                Costo = (double)i.Costo
            }, tx, cancellationToken: ct));
        }
        if (cols.TablaTareas)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.V_MA_INSUMOSTAREAS WHERE IdArticulo = @Id;", new { Id = producto.IdArticulo }, tx, cancellationToken: ct));
            foreach (var t in formula.Tareas)
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.V_MA_INSUMOSTAREAS (IdArticulo, IdTarea, DESCRIPCION, Minutos, Segundos)
                    VALUES (@Id, @IdTarea, @Descripcion, @Minutos, @Segundos);
                    """, new
                {
                    Id = producto.IdArticulo,
                    t.IdTarea,
                    Descripcion = Recortar(t.Descripcion.Trim(), 100),
                    Minutos = (double)t.Minutos,
                    t.Segundos
                }, tx, cancellationToken: ct));
        }

        var costo = formula.CostoInsumos;
        var sets = new List<string>();
        if (cols.ActualizaCosto) sets.Add("ActualizaCosto = @Auto");
        if (cols.InsumosPorPorcentaje) sets.Add("InsumosPorPorcentaje = @PorPorcentaje");
        if (cols.CostoInsumos) sets.Add("CostoInsumos = @Costo");
        if (cols.Procedencia)
            sets.Add("""
                Procedencia = CASE WHEN @Produccion = 1 THEN 'PRODUCCION'
                                   WHEN UPPER(LTRIM(RTRIM(ISNULL(Procedencia, '')))) = 'PRODUCCION' THEN NULL
                                   ELSE Procedencia END
                """);
        if (sets.Count > 0)
            // DesdeTrigger = 1: son datos de la fórmula, no tienen que volver a disparar la política de precios
            // (el costo ya lo grabó el UPDATE principal del artículo con DesdeTrigger = 0).
            await cn.ExecuteAsync(new CommandDefinition(
                $"UPDATE dbo.V_MA_ARTICULOS SET {string.Join(", ", sets)}, DesdeTrigger = 1 WHERE IDARTICULO = @Id;",
                new
                {
                    Id = producto.IdArticulo,
                    Auto = formula.CalcularCostoAutomatico,
                    formula.PorPorcentaje,
                    Costo = costo,
                    Produccion = formula.DescuentaSoloPorProduccion
                }, tx, cancellationToken: ct));
        return costo;

        static string Recortar(string s, int max) => s.Length > max ? s[..max] : s;
    }

    /// <summary>
    /// Costo de la fórmula antes de grabar el artículo, para que con "Calcular costo automáticamente" el UPDATE
    /// principal ya grabe ese costo (y el trigger aplique la política sobre él). No valida: eso lo hace GuardarFormulaAsync.
    /// </summary>
    private static async Task<decimal> CalcularCostoFormulaAsync(SqlConnection cn, string codigoProducto, ArticuloFormulaDto formula, CancellationToken ct)
    {
        var moneda = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) ISNULL(LTRIM(RTRIM(Moneda)), '') FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO)) = @Codigo;",
            new { Codigo = codigoProducto.Trim() }, cancellationToken: ct)) ?? string.Empty;
        await ResolverInsumosAsync(cn, null, moneda, formula.Insumos, ct);
        if (formula.Tareas.Count > 0 && (await LeerEsquemaFormulaAsync(cn, null, ct)).TablaTareasCatalogo)
        {
            var catalogo = (await LeerTareasAsync(cn, null, ct)).ToDictionary(t => t.IdTarea, StringComparer.OrdinalIgnoreCase);
            foreach (var t in formula.Tareas)
                if (catalogo.TryGetValue(t.IdTarea.Trim(), out var cat))
                    t.ValorHora = cat.ValorHora;
        }
        return formula.CostoInsumos;
    }

    private static async Task<List<ArticuloTareaOptionDto>> LeerTareasAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => (await cn.QueryAsync<ArticuloTareaOptionDto>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(IdTarea)) AS IdTarea, ISNULL(Descripcion, '') AS Descripcion,
                CONVERT(decimal(18, 4), ISNULL(ValorCosto, 0)) AS ValorHora
            FROM dbo.V_TA_TAREAS ORDER BY Descripcion;
            """, transaction: tx, cancellationToken: ct))).ToList();

    /// <summary>Qué tablas y columnas de la fórmula tiene la base (no todas las bases tienen todo).</summary>
    private static async Task<EsquemaFormula> LeerEsquemaFormulaAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => await cn.QuerySingleAsync<EsquemaFormula>(new CommandDefinition("""
            SELECT
                CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.V_MA_INSUMOS', N'U') IS NULL THEN 0 ELSE 1 END) AS TablaInsumos,
                CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.V_MA_INSUMOSTAREAS', N'U') IS NULL THEN 0 ELSE 1 END) AS TablaTareas,
                CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.V_TA_TAREAS', N'U') IS NULL THEN 0 ELSE 1 END) AS TablaTareasCatalogo,
                CONVERT(bit, CASE WHEN COL_LENGTH(N'dbo.V_MA_ARTICULOS', N'ActualizaCosto') IS NULL THEN 0 ELSE 1 END) AS ActualizaCosto,
                CONVERT(bit, CASE WHEN COL_LENGTH(N'dbo.V_MA_ARTICULOS', N'InsumosPorPorcentaje') IS NULL THEN 0 ELSE 1 END) AS InsumosPorPorcentaje,
                CONVERT(bit, CASE WHEN COL_LENGTH(N'dbo.V_MA_ARTICULOS', N'CostoInsumos') IS NULL THEN 0 ELSE 1 END) AS CostoInsumos,
                CONVERT(bit, CASE WHEN COL_LENGTH(N'dbo.V_MA_ARTICULOS', N'Procedencia') IS NULL THEN 0 ELSE 1 END) AS Procedencia;
            """, transaction: tx, cancellationToken: ct));

    private sealed class EsquemaFormula
    {
        public bool TablaInsumos { get; set; }
        public bool TablaTareas { get; set; }
        public bool TablaTareasCatalogo { get; set; }
        public bool ActualizaCosto { get; set; }
        public bool InsumosPorPorcentaje { get; set; }
        public bool CostoInsumos { get; set; }
        public bool Procedencia { get; set; }
    }

    private sealed class FormulaCabeceraFila
    {
        public bool CalcularCostoAutomatico { get; set; }
        public bool PorPorcentaje { get; set; }
        public bool DescuentaSoloPorProduccion { get; set; }
        public string Moneda { get; set; } = string.Empty;
    }

    private sealed class InsumoArticuloFila
    {
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string Moneda { get; set; } = string.Empty;
        public bool Suspendido { get; set; }
        public decimal P0 { get; set; }
        public decimal P1 { get; set; }
        public decimal P2 { get; set; }
        public decimal P3 { get; set; }
        public decimal P4 { get; set; }
        public decimal P5 { get; set; }
        public decimal P6 { get; set; }
        public decimal P7 { get; set; }
        public decimal P8 { get; set; }

        public decimal Valor(int clase) => clase switch
        {
            1 => P1, 2 => P2, 3 => P3, 4 => P4, 5 => P5, 6 => P6, 7 => P7, 8 => P8, _ => P0
        };
    }

    private sealed class EquivalenciaFila
    {
        public string Codigo { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string UnidadEquiv { get; set; } = string.Empty;
        public decimal Coeficiente { get; set; }
    }
}

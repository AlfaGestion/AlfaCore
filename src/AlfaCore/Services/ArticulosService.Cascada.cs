using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Cascada de costos por fórmula: port corregido de ReCalculaCostoConInsumos (RT_ARTICULOS.bas) + FrmConfirmar.frm.
// Cuando cambia el valor de un artículo, se recalculan los productos que lo usan como insumo y tienen
// "Calcular costo automáticamente" (ActualizaCosto = 1), y los que usan a esos, hasta el final de la cadena.
// Diferencias a propósito con el legacy:
//  - toda la cadena (el legacy llega a 2 niveles salvo ActualizaNivelesInsumos=SI), en orden: cada producto se
//    recalcula una sola vez y después de sus insumos; los ciclos se informan en vez de colgar el sistema;
//  - el costo se calcula con la misma rutina que la solapa Fórmula (respeta ClasePr, unidades, tareas, porcentaje);
//  - todo dentro de la transacción de quien origina el cambio: si algo falla no queda la cadena a medias;
//  - margen: maestro sin política con UTILIDAD > 0 recalcula su precio de venta (en el legacy estaba comentado) y cada
//    lista con UTILIDAD > 0 recalcula su precio de la clase ClasePrecioVenta (el legacy fijaba PRECIO4). Con utilidad 0
//    no se toca el precio (el legacy lo dejaba igual al costo).
//  - con PreciosArticulosPorBorrador=SI el costo se graba pero ningún precio de venta real cambia: se calcula lo que haría
//    la base (trigger FMT_V_MA_ARTICULOS con política/fórmulas y listas) dentro de un savepoint que se deshace, y el
//    resultado va a V_MA_PRECIOS_BORRADOR en el mismo formato que usa Alfa Gestión (fila B = maestro, V = lista).
public sealed partial class ArticulosService
{
    private const string ClaveBorrador = "PreciosArticulosPorBorrador";
    private static readonly string[] ColumnasPrecioMaestro =
        ["COSTO", "PRECIO1", "PRECIO2", "PRECIO3", "PRECIO4", "PRECIO5", "PRECIO6", "PRECIO7", "PRECIO8"];
    private static readonly string[] ColumnasPrecioLista =
        ["COSTO", "PRECIO1", "PRECIO2", "PRECIO3", "PRECIO4", "PRECIO5", "PRECIO6", "PRECIO7", "PRECIO8", "PRECIO9", "PRECIO10"];
    // Fila B (maestro) del borrador: lo que aplica FrmBorradorPrecios.AplicarListaB.
    private static readonly string[] ColumnasBorradorMaestro =
        ["COSTO", "PRECIO1", "PRECIO2", "PRECIO3", "PRECIO4", "PRECIO5", "PRECIO6", "PRECIO7", "PRECIO8", "UTILIDAD",
         "POLITICAPRECIOS", "DTO1", "DTO2", "DTO3", "DTO4", "DTO5", "REC1", "REC2", "REC3", "TASAIVA", "IMPUESTOS"];

    /// <summary>
    /// Propaga el cambio de valor de <paramref name="origenes"/> a los productos que los usan como insumo.
    /// Corre dentro de la transacción del llamador (no hace commit ni rollback).
    /// </summary>
    internal static async Task<ArticuloCascadaResultado> PropagarCostosAsync(
        SqlConnection cn, SqlTransaction tx, IReadOnlyCollection<string> origenes, string usuario, CancellationToken ct)
    {
        var resultado = new ArticuloCascadaResultado();
        var esquema = await LeerEsquemaFormulaAsync(cn, tx, ct);
        if (!esquema.TablaInsumos || !esquema.ActualizaCosto || origenes.Count == 0)
            return resultado;

        var cfg = await ReadConfigMapAsync(cn,
            ["ClasePrecioVenta", "MAESTROARTICULOCONIVA", "PIVA", ClaveBorrador, "CLASEPRECIOLISTAPROV", "RETAIL"], ct, tx);
        var claseVenta = int.TryParse(Get(cfg, "ClasePrecioVenta"), out var cv) && cv is >= 1 and <= 8 ? cv : 1;
        var maestroConIva = ParseBool(Get(cfg, "MAESTROARTICULOCONIVA"));
        var ivaGeneral = ParseDecimal(Get(cfg, "PIVA"));
        // Precio de lista del proveedor: el legacy lo iguala al costo nuevo (en Retail, por defecto la clase 1).
        var claseProveedorTexto = Get(cfg, "CLASEPRECIOLISTAPROV").Trim();
        if (claseProveedorTexto.Length == 0 && ParseBool(Get(cfg, "RETAIL")))
            claseProveedorTexto = "1";
        var claseProveedor = int.TryParse(claseProveedorTexto, out var cp) && cp is >= 1 and <= 8 ? cp : 0;
        resultado.AlBorrador = ParseBool(Get(cfg, ClaveBorrador))
            && await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.V_MA_PRECIOS_BORRADOR', N'U') IS NULL THEN 0 ELSE 1 END;", transaction: tx, cancellationToken: ct)) == 1;

        var aristas = (await cn.QueryAsync<(string Producto, string Insumo)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(IdArticulo)), LTRIM(RTRIM(IdArticuloInsumo)) FROM dbo.V_MA_INSUMOS
            WHERE LEFT(LTRIM(IdArticuloInsumo), 1) <> '*';
            """, transaction: tx, cancellationToken: ct)))
            .ToLookup(a => a.Insumo, a => a.Producto, StringComparer.OrdinalIgnoreCase);
        var automaticos = (await cn.QueryAsync<string>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(IDARTICULO)) FROM dbo.V_MA_ARTICULOS WHERE ISNULL(ActualizaCosto, 0) = 1;",
            transaction: tx, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var (orden, enCiclo) = ArticuloFormulaCalculo.OrdenCascada(origenes, a => aristas[a], automaticos.Contains);
        if (enCiclo.Count > 0)
            resultado.Observaciones.Add($"No se recalcularon porque hay un ciclo en sus fórmulas o dependen de uno: {string.Join(", ", enCiclo)}. Revisá esas fórmulas.");
        if (orden.Count == 0)
            return resultado;

        var columnasBorrador = resultado.AlBorrador ? await ColumnasEscribiblesAsync(cn, tx, "V_MA_PRECIOS_BORRADOR", ct) : [];
        foreach (var codigo in orden)
        {
            var art = await cn.QuerySingleOrDefaultAsync<CascadaArticuloFila>(new CommandDefinition("""
                SELECT TOP (1) IDARTICULO AS IdArticulo, ISNULL(DESCRIPCION, '') AS Descripcion, ISNULL(COSTO, 0) AS Costo,
                    ISNULL(UTILIDAD, 0) AS Utilidad, ISNULL(LTRIM(RTRIM(POLITICAPRECIOS)), '') AS Politica,
                    CONVERT(decimal(9, 4), ISNULL(TasaIVA, 0)) AS TasaIva, CONVERT(bit, ISNULL(EXENTO, 0)) AS Exento,
                    ISNULL(LTRIM(RTRIM(Moneda)), '') AS Moneda
                FROM dbo.V_MA_ARTICULOS WITH (UPDLOCK) WHERE LTRIM(RTRIM(IDARTICULO)) = @Codigo;
                """, new { Codigo = codigo }, tx, cancellationToken: ct));
            if (art is null)
                continue;

            var formula = await LeerFormulaAsync(cn, tx, codigo, ct);
            foreach (var malo in formula.Insumos.Where(i => i.Error.Length > 0))
                resultado.Observaciones.Add($"{codigo}: el insumo {(malo.EsTextoLibre ? malo.Descripcion : malo.Codigo)} no suma costo ({malo.Error}).");
            var costoNuevo = formula.CostoInsumos;
            if (Math.Round(costoNuevo, 2) == Math.Round(art.Costo, 2))
                continue;

            var iva = art.Exento ? 0 : art.TasaIva > 0 ? art.TasaIva : ivaGeneral;
            // Sin política, con utilidad: precio de venta del maestro desde el costo (CalUtilidad de Ma_art.frm).
            decimal? precioVentaMaestro = art.Politica.Length == 0 && art.Utilidad > 0
                ? ArticuloLoteCalculo.PrecioDesdeUtilidad(costoNuevo, (decimal)art.Utilidad, maestroConIva ? iva : 0, 0)
                : null;

            var antes = await LeerValoresAsync(cn, tx, art.IdArticulo, ct);
            var cambio = new ArticuloCascadaCambio
            {
                Codigo = codigo,
                Descripcion = art.Descripcion,
                CostoAnterior = art.Costo,
                CostoNuevo = costoNuevo,
                PrecioVentaAnterior = antes.Maestro.GetValueOrDefault($"PRECIO{claseVenta}")
            };

            if (!resultado.AlBorrador)
            {
                await GrabarPreciosCascadaAsync(cn, tx, art, costoNuevo, precioVentaMaestro, claseVenta, claseProveedor, iva, usuario, ct);
                var despues = await LeerValoresAsync(cn, tx, art.IdArticulo, ct);
                cambio.PrecioVentaNuevo = despues.Maestro.GetValueOrDefault($"PRECIO{claseVenta}");
                cambio.Listas = despues.Listas.Count(l => !MismosValores(l.Value, antes.Listas.GetValueOrDefault(l.Key), ColumnasPrecioLista.Skip(1)));
            }
            else
            {
                // Lo que haría la base (trigger + márgenes), calculado en un savepoint que se deshace.
                const string punto = "cascada_borrador";
                tx.Save(punto);
                await GrabarPreciosCascadaAsync(cn, tx, art, costoNuevo, precioVentaMaestro, claseVenta, claseProveedor, iva, usuario, ct);
                var simulado = await LeerValoresAsync(cn, tx, art.IdArticulo, ct);
                tx.Rollback(punto);

                // Real: solo el costo (DesdeTrigger = 1: el trigger no recalcula precios ni los pasa a las listas).
                await cn.ExecuteAsync(new CommandDefinition($"""
                    UPDATE dbo.V_MA_ARTICULOS
                    SET COSTO = @Costo{(await TieneColumnaAsync(cn, tx, "V_MA_ARTICULOS", "CostoInsumos", ct) ? ", CostoInsumos = @Costo" : "")},
                        Usuario = @Usuario, DesdeTrigger = 1
                    WHERE IDARTICULO = @Id;
                    UPDATE dbo.V_MA_PRECIOS SET COSTO = @Costo, Usuario = @Usuario, DesdeTrigger = 1
                    WHERE IDARTICULO = @Id AND TIPOLISTA = 'V';
                    """, new { Id = art.IdArticulo, Costo = costoNuevo, Usuario = usuario }, tx, cancellationToken: ct));

                cambio.PrecioVentaNuevo = simulado.Maestro.GetValueOrDefault($"PRECIO{claseVenta}");
                (_, cambio.Listas) = await EscribirBorradorAsync(cn, tx, new ConfigBorrador(maestroConIva, columnasBorrador),
                    art.IdArticulo, antes, simulado, usuario, ct);
            }
            resultado.Cambios.Add(cambio);
        }
        return resultado;
    }

    /// <summary>Borrador activo: configuración que necesita la escritura al borrador. Null si PreciosArticulosPorBorrador no es SI.</summary>
    internal sealed record ConfigBorrador(bool MaestroConIva, HashSet<string> Columnas);

    internal static async Task<ConfigBorrador?> ConfigBorradorAsync(SqlConnection cn, SqlTransaction tx, CancellationToken ct)
    {
        var cfg = await ReadConfigMapAsync(cn, [ClaveBorrador, "MAESTROARTICULOCONIVA"], ct, tx);
        if (!ParseBool(Get(cfg, ClaveBorrador)))
            return null;
        var columnas = await ColumnasEscribiblesAsync(cn, tx, "V_MA_PRECIOS_BORRADOR", ct);
        return columnas.Count == 0 ? null : new ConfigBorrador(ParseBool(Get(cfg, "MAESTROARTICULOCONIVA")), columnas);
    }

    /// <summary>
    /// Escribe al borrador lo que cambió entre dos fotos del artículo (fila B si cambió algún precio del maestro, fila V por cada
    /// lista con precios distintos). Devuelve si escribió la fila del maestro y cuántas listas.
    /// </summary>
    private static async Task<(bool Maestro, int Listas)> EscribirBorradorAsync(
        SqlConnection cn, SqlTransaction tx, ConfigBorrador cfg, string idArticulo, CascadaValores antes, CascadaValores nuevo, string usuario, CancellationToken ct)
    {
        var maestro = false;
        if (!MismosValores(nuevo.Maestro, antes.Maestro, ColumnasPrecioMaestro.Skip(1)))
        {
            maestro = true;
            await GuardarBorradorAsync(cn, tx, cfg.Columnas, idArticulo, "B", string.Empty, nuevo.FilaMaestro,
                antes.Maestro, ColumnasPrecioMaestro.Skip(1), cfg.MaestroConIva, usuario, ct);
        }
        var listas = 0;
        foreach (var (idLista, fila) in nuevo.FilasListas)
        {
            // Una lista que el artículo no tenía entra con sus precios (no hay precio real que retener).
            if (antes.Listas.GetValueOrDefault(idLista) is not { } anterior
                || MismosValores(nuevo.Listas[idLista], anterior, ColumnasPrecioLista.Skip(1)))
                continue;
            listas++;
            await GuardarBorradorAsync(cn, tx, cfg.Columnas, idArticulo, "V", idLista, fila,
                anterior, ColumnasPrecioLista.Skip(1), cfg.MaestroConIva, usuario, ct);
        }
        return (maestro, listas);
    }

    /// <summary>
    /// "Todos los cambios van al borrador" (PreciosArticulosPorBorrador=SI) para operaciones sobre artículos (lote, políticas):
    /// corre <paramref name="operacion"/> en un savepoint, toma la foto de cómo quedarían los precios, la deshace y graba de verdad
    /// solo el costo (DesdeTrigger = 1); los precios de venta nuevos van a V_MA_PRECIOS_BORRADOR. Sin borrador activo corre la
    /// operación tal cual. Devuelve si fue al borrador y cuántos artículos quedaron con cambios pendientes.
    /// </summary>
    internal static async Task<(bool AlBorrador, int Articulos)> RetenerPreciosArticulosAsync(
        SqlConnection cn, SqlTransaction tx, IReadOnlyCollection<string> codigos, string usuario, Func<Task> operacion, CancellationToken ct)
    {
        var cfg = await ConfigBorradorAsync(cn, tx, ct);
        if (cfg is null)
        {
            await operacion();
            return (false, 0);
        }
        var ids = new List<string>();
        foreach (var lote in codigos.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
            ids.AddRange(await cn.QueryAsync<string>(new CommandDefinition(
                "SELECT IDARTICULO FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO)) IN @Codigos;", new { Codigos = lote }, tx, cancellationToken: ct)));
        var antes = new Dictionary<string, CascadaValores>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
            antes[id] = await LeerValoresAsync(cn, tx, id, ct);

        const string punto = "retener_precios";
        tx.Save(punto);
        await operacion();
        var nuevos = new Dictionary<string, CascadaValores>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
            nuevos[id] = await LeerValoresAsync(cn, tx, id, ct);
        tx.Rollback(punto);

        var conCambios = 0;
        foreach (var id in ids)
        {
            var a = antes[id];
            var n = nuevos[id];
            var costo = n.Maestro.GetValueOrDefault("COSTO");
            if (Math.Round(costo, 2) != Math.Round(a.Maestro.GetValueOrDefault("COSTO"), 2))
                await GrabarSoloCostoAsync(cn, tx, id, costo, usuario, ct);
            var (m, l) = await EscribirBorradorAsync(cn, tx, cfg, id, a, n, usuario, ct);
            if (m || l > 0) conCambios++;
        }
        return (true, conCambios);
    }

    /// <summary>Igual que RetenerPreciosArticulosAsync pero para operaciones sobre una lista de precios (aumento, redondeo, edición).</summary>
    internal static async Task<(bool AlBorrador, int Filas)> RetenerPreciosListaAsync(
        SqlConnection cn, SqlTransaction tx, string idLista, string usuario, Func<Task> operacion, CancellationToken ct)
    {
        var cfg = await ConfigBorradorAsync(cn, tx, ct);
        if (cfg is null)
        {
            await operacion();
            return (false, 0);
        }
        async Task<Dictionary<int, Dictionary<string, object?>>> LeerAsync()
            => (await cn.QueryAsync(new CommandDefinition(
                    "SELECT * FROM dbo.V_MA_PRECIOS WHERE IdLista = @IdLista AND TIPOLISTA = 'V';", new { IdLista = idLista }, tx, commandTimeout: 300, cancellationToken: ct)))
                .Cast<IDictionary<string, object?>>()
                .Select(f => new Dictionary<string, object?>(f, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(f => Convert.ToInt32(f["ID"]));

        var antes = await LeerAsync();
        const string punto = "retener_lista";
        tx.Save(punto);
        await operacion();
        var nuevos = await LeerAsync();
        tx.Rollback(punto);

        var filas = 0;
        foreach (var (id, fila) in nuevos)
        {
            if (!antes.TryGetValue(id, out var anterior))
                continue;
            var valoresAntes = Numeros(anterior, ColumnasPrecioLista);
            if (MismosValores(Numeros(fila, ColumnasPrecioLista), valoresAntes, ColumnasPrecioLista.Skip(1)))
                continue;
            filas++;
            await GuardarBorradorAsync(cn, tx, cfg.Columnas, Convert.ToString(fila["IdArticulo"]) ?? string.Empty, "V",
                Convert.ToString(fila["IdLista"]) ?? idLista, fila, valoresAntes, ColumnasPrecioLista.Skip(1), cfg.MaestroConIva, usuario, ct);
        }
        return (true, filas);
    }

    /// <summary>Costo real sin tocar precios: DesdeTrigger = 1 para que el trigger no recalcule la política ni pase precios a las listas.</summary>
    private static async Task GrabarSoloCostoAsync(SqlConnection cn, SqlTransaction tx, string idArticulo, decimal costo, string usuario, CancellationToken ct)
        => await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.V_MA_ARTICULOS SET COSTO = @Costo, Usuario = @Usuario, DesdeTrigger = 1 WHERE IDARTICULO = @Id;
            UPDATE dbo.V_MA_PRECIOS SET COSTO = @Costo, Usuario = @Usuario, DesdeTrigger = 1 WHERE IDARTICULO = @Id AND TIPOLISTA = 'V';
            """, new { Id = idArticulo, Costo = costo, Usuario = usuario }, tx, cancellationToken: ct));

    /// <summary>Costo nuevo + margen: maestro con DesdeTrigger = 0 (el trigger aplica política y pasa a listas) y listas con utilidad.</summary>
    private static async Task GrabarPreciosCascadaAsync(
        SqlConnection cn, SqlTransaction tx, CascadaArticuloFila art, decimal costo, decimal? precioVenta,
        int claseVenta, int claseProveedor, decimal iva, string usuario, CancellationToken ct)
    {
        var sets = new List<string> { "COSTO = @Costo", "Usuario = @Usuario", "DesdeTrigger = 0" };
        if (await TieneColumnaAsync(cn, tx, "V_MA_ARTICULOS", "CostoInsumos", ct)) sets.Add("CostoInsumos = @Costo");
        if (precioVenta is not null) sets.Add($"PRECIO{claseVenta} = @PrecioVenta");
        if (claseProveedor > 0 && claseProveedor != claseVenta) sets.Add($"PRECIO{claseProveedor} = @Costo");
        await cn.ExecuteAsync(new CommandDefinition(
            $"UPDATE dbo.V_MA_ARTICULOS SET {string.Join(", ", sets)} WHERE IDARTICULO = @Id;",
            new { Id = art.IdArticulo, Costo = costo, PrecioVenta = precioVenta ?? 0, Usuario = usuario }, tx, cancellationToken: ct));

        // Todas las listas de venta toman el costo nuevo (como el legacy); las que tienen margen propio
        // (RespetarMKUP / CalPorcUtilidad) recalculan además su precio de la clase de venta configurada.
        var listas = await cn.QueryAsync<(string IdLista, double Utilidad, bool ConIva)>(new CommandDefinition("""
            SELECT IdLista, ISNULL(UTILIDAD, 0), CONVERT(bit, ISNULL(ConIVA, 0))
            FROM dbo.V_MA_PRECIOS WHERE IDARTICULO = @Id AND TIPOLISTA = 'V';
            """, new { Id = art.IdArticulo }, tx, cancellationToken: ct));
        foreach (var l in listas)
        {
            var conMargen = l.Utilidad > 0;
            var precioLista = conMargen ? ArticuloLoteCalculo.PrecioDesdeUtilidad(costo, (decimal)l.Utilidad, l.ConIva ? iva : 0, 0) : 0;
            await cn.ExecuteAsync(new CommandDefinition($"""
                UPDATE dbo.V_MA_PRECIOS SET COSTO = @Costo{(conMargen ? $", PRECIO{claseVenta} = @Precio" : "")}, DesdeTrigger = 0, Usuario = @Usuario
                WHERE IDARTICULO = @Id AND IdLista = @IdLista AND TIPOLISTA = 'V';
                """, new { Id = art.IdArticulo, l.IdLista, Costo = costo, Precio = precioLista, Usuario = usuario }, tx, cancellationToken: ct));
        }
    }

    /// <summary>Valores de precio del maestro y de cada lista de venta (y las filas completas, para el borrador).</summary>
    private static async Task<CascadaValores> LeerValoresAsync(SqlConnection cn, SqlTransaction tx, string idArticulo, CancellationToken ct)
    {
        var valores = new CascadaValores();
        var maestro = (IDictionary<string, object?>?)await cn.QuerySingleOrDefaultAsync(new CommandDefinition(
            "SELECT TOP (1) * FROM dbo.V_MA_ARTICULOS WHERE IDARTICULO = @Id;", new { Id = idArticulo }, tx, cancellationToken: ct));
        if (maestro is not null)
        {
            valores.FilaMaestro = new Dictionary<string, object?>(maestro, StringComparer.OrdinalIgnoreCase);
            valores.Maestro = Numeros(valores.FilaMaestro, ColumnasPrecioMaestro);
        }
        foreach (IDictionary<string, object?> fila in await cn.QueryAsync(new CommandDefinition(
            "SELECT * FROM dbo.V_MA_PRECIOS WHERE IDARTICULO = @Id AND TIPOLISTA = 'V';", new { Id = idArticulo }, tx, cancellationToken: ct)))
        {
            var f = new Dictionary<string, object?>(fila, StringComparer.OrdinalIgnoreCase);
            var idLista = Convert.ToString(f.GetValueOrDefault("IdLista")) ?? string.Empty;
            valores.FilasListas[idLista] = f;
            valores.Listas[idLista] = Numeros(f, ColumnasPrecioLista);
        }
        return valores;
    }

    private static Dictionary<string, decimal> Numeros(IDictionary<string, object?> fila, IEnumerable<string> columnas)
        => columnas.ToDictionary(c => c, c => fila.TryGetValue(c, out var v) && v is not null and not DBNull ? Convert.ToDecimal(v) : 0m,
            StringComparer.OrdinalIgnoreCase);

    private static bool MismosValores(IReadOnlyDictionary<string, decimal> a, IReadOnlyDictionary<string, decimal>? b, IEnumerable<string> columnas)
        => b is not null && columnas.All(c => Math.Round(a.GetValueOrDefault(c), 2) == Math.Round(b.GetValueOrDefault(c), 2));

    /// <summary>
    /// Upsert en V_MA_PRECIOS_BORRADOR por (artículo, tipo, lista), como Rs2a.AddNew/Update del legacy: si la fila ya
    /// existe se cambian solo los valores que cambiaron (respeta lo que ya estaba pendiente); si no, se crea con la
    /// foto completa calculada (nunca con precios en NULL, que al aplicar quedarían en 0).
    /// </summary>
    private static async Task GuardarBorradorAsync(
        SqlConnection cn, SqlTransaction tx, HashSet<string> columnasBorrador, string idArticulo, string tipo, string idLista,
        IReadOnlyDictionary<string, object?> fotoNueva, IReadOnlyDictionary<string, decimal> anteriores, IEnumerable<string> columnasPrecio,
        bool maestroConIva, string usuario, CancellationToken ct)
    {
        var existe = await cn.ExecuteScalarAsync<int?>(new CommandDefinition("""
            SELECT TOP (1) ID FROM dbo.V_MA_PRECIOS_BORRADOR
            WHERE IdArticulo = @Id AND TipoLista = @Tipo AND LTRIM(RTRIM(IdLista)) = LTRIM(RTRIM(@IdLista));
            """, new { Id = idArticulo, Tipo = tipo, IdLista = idLista }, tx, cancellationToken: ct));
        var p = new DynamicParameters();
        p.Add("Id", idArticulo);
        p.Add("Usuario", usuario);
        if (existe is int id)
        {
            var cambiadas = columnasPrecio
                .Where(c => columnasBorrador.Contains(c) && fotoNueva.TryGetValue(c, out var v)
                    && Math.Round(v is null or DBNull ? 0m : Convert.ToDecimal(v), 2) != Math.Round(anteriores.GetValueOrDefault(c), 2))
                .ToList();
            foreach (var c in cambiadas)
                p.Add("v_" + c, fotoNueva[c] is null or DBNull ? 0m : Convert.ToDecimal(fotoNueva[c]));
            p.Add("Fila", id);
            var sets = cambiadas.Select(c => $"[{c}] = @v_{c}").Append("Usuario = @Usuario").Append("DesdeTrigger = 0");
            await cn.ExecuteAsync(new CommandDefinition(
                $"UPDATE dbo.V_MA_PRECIOS_BORRADOR SET {string.Join(", ", sets)} WHERE ID = @Fila;", p, tx, cancellationToken: ct));
            return;
        }

        var valores = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (tipo == "B")
        {
            foreach (var c in ColumnasBorradorMaestro.Where(columnasBorrador.Contains))
                valores[c] = fotoNueva.GetValueOrDefault(c);
            valores["IdLista"] = string.Empty;
            valores["Nombre"] = "Lista Base";
            valores["ConIVA"] = maestroConIva;
            var descripcion = Convert.ToString(fotoNueva.GetValueOrDefault("DESCRIPCION")) ?? string.Empty;
            valores["DESCRIPCIONARTICULO"] = descripcion.Length > 100 ? descripcion[..100] : descripcion;
            valores["IdMoneda"] = fotoNueva.GetValueOrDefault("Moneda");
        }
        else
        {
            foreach (var (c, v) in fotoNueva)
                if (columnasBorrador.Contains(c))
                    valores[c] = v;
            if (valores.GetValueOrDefault("Nombre") is null or DBNull || Convert.ToString(valores["Nombre"])!.Trim().Length == 0)
                valores["Nombre"] = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                    "SELECT TOP (1) NOMBRE FROM dbo.V_MA_PRECIOSCAB WHERE LTRIM(RTRIM(IDLISTA)) = LTRIM(RTRIM(@IdLista)) AND TIPOLISTA = 'V';",
                    new { IdLista = idLista }, tx, cancellationToken: ct)) ?? idLista;
        }
        // El costo en AlfaCore siempre se graba real (como el legacy): el borrador no lo lleva. Si lo llevara, aplicar una fila
        // vieja volvería el costo atrás. Vacío = al aplicar se conserva el costo vigente.
        valores["COSTO"] = null;
        valores["IdArticulo"] = idArticulo;
        valores["TipoLista"] = tipo;
        valores["Usuario"] = usuario;
        valores["DesdeTrigger"] = 0;
        foreach (var c in new[] { "ConIVA", "Precio1", "Precio2", "Precio3", "Precio4", "Precio5" })
            if (valores.GetValueOrDefault(c) is null or DBNull)
                valores[c] = c == "ConIVA" ? false : 0m;

        var columnas = valores.Keys.Where(columnasBorrador.Contains).ToList();
        for (var i = 0; i < columnas.Count; i++)
            p.Add("c" + i, valores[columnas[i]] is DBNull ? null : valores[columnas[i]]);
        await cn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO dbo.V_MA_PRECIOS_BORRADOR ({string.Join(", ", columnas.Select(c => $"[{c}]"))}) VALUES ({string.Join(", ", columnas.Select((_, i) => "@c" + i))});",
            p, tx, cancellationToken: ct));
    }

    /// <summary>Columnas que se pueden insertar en la tabla (sin identidad ni calculadas).</summary>
    private static async Task<HashSet<string>> ColumnasEscribiblesAsync(SqlConnection cn, SqlTransaction tx, string tabla, CancellationToken ct)
        => (await cn.QueryAsync<string>(new CommandDefinition("""
            SELECT name FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.' + @Tabla) AND is_identity = 0 AND is_computed = 0;
            """, new { Tabla = tabla }, tx, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static async Task<bool> TieneColumnaAsync(SqlConnection cn, SqlTransaction tx, string tabla, string columna, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN COL_LENGTH(N'dbo.' + @Tabla, @Columna) IS NULL THEN 0 ELSE 1 END;",
            new { Tabla = tabla, Columna = columna }, tx, cancellationToken: ct)) == 1;

    private sealed class CascadaArticuloFila
    {
        public string IdArticulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal Costo { get; set; }
        public double Utilidad { get; set; }
        public string Politica { get; set; } = string.Empty;
        public decimal TasaIva { get; set; }
        public bool Exento { get; set; }
        public string Moneda { get; set; } = string.Empty;
    }

    private sealed class CascadaValores
    {
        public Dictionary<string, object?> FilaMaestro { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> Maestro { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, object?>> FilasListas { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, decimal>> Listas { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

using AlfaCore.Common;
using AlfaCore.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IHistorialPreciosService
{
    Task<HistorialPrecioContextoDto> GetContextoAsync(CancellationToken ct = default);
    Task<HistorialPrecioResumenDto> GetResumenAsync(HistorialPrecioFiltro filtro, CancellationToken ct = default);
    Task<HistorialPrecioPagina<HistorialPrecioCambioDto>> GetCambiosAsync(HistorialPrecioFiltro filtro, CancellationToken ct = default);
    Task<HistorialPrecioPagina<HistorialPrecioArticuloDto>> GetArticulosAsync(HistorialPrecioFiltro filtro, CancellationToken ct = default);
    Task<HistorialPrecioPagina<HistorialPrecioAlertaDto>> GetAlertasAsync(HistorialPrecioFiltro filtro, CancellationToken ct = default);
    Task<HistorialPrecioFichaDto> GetFichaAsync(string idArticulo, HistorialPrecioFiltro filtro, CancellationToken ct = default);
    Task<byte[]> ExportarAsync(string solapa, HistorialPrecioFiltro filtro, CancellationToken ct = default);
}

/// <summary>
/// Historial de costos y precios sobre V_MV_PreciosHis, que graban los triggers FMT_V_MA_ARTICULOS (maestro, IdLista NULL) y
/// FMT_V_MA_PRECIOS (listas). Cada fila es una foto de los nueve valores: un cambio del campo elegido se detecta comparando
/// cada foto con la anterior del mismo artículo. Solo lectura. Sin LAG ni PERCENTILE, para bases con compatibilidad 100.
/// </summary>
public sealed class HistorialPreciosService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events) : IHistorialPreciosService
{
    /// <summary>Clave legacy «Reporte de Cambios de Precios (Historico)» (Shell:Rp_PRECIOSHISTORIAL).</summary>
    public const string MenuKey = "D010507";
    internal const int MaximoMeses = 36;
    internal const int MaximoExcel = 100_000;
    private const int Timeout = 180;
    private const int MaximoPuntosFicha = 1000;

    private sealed class ReglaException(string message) : InvalidOperationException(message);

    public Task<HistorialPrecioContextoDto> GetContextoAsync(CancellationToken ct = default)
        => EjecutarAsync("Contexto", cn => LeerContextoAsync(cn, ct), ct);
    public Task<HistorialPrecioResumenDto> GetResumenAsync(HistorialPrecioFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Resumen", cn => LeerResumenAsync(cn, filtro, ct), ct);
    public Task<HistorialPrecioPagina<HistorialPrecioCambioDto>> GetCambiosAsync(HistorialPrecioFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Cambios", cn => LeerCambiosAsync(cn, filtro, ct), ct);
    public Task<HistorialPrecioPagina<HistorialPrecioArticuloDto>> GetArticulosAsync(HistorialPrecioFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Articulos", cn => LeerArticulosAsync(cn, filtro, ct), ct);
    public Task<HistorialPrecioPagina<HistorialPrecioAlertaDto>> GetAlertasAsync(HistorialPrecioFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Alertas", cn => LeerAlertasAsync(cn, filtro, ct), ct);
    public Task<HistorialPrecioFichaDto> GetFichaAsync(string idArticulo, HistorialPrecioFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Ficha", cn => LeerFichaAsync(cn, idArticulo, filtro, ct), ct);

    public Task<byte[]> ExportarAsync(string solapa, HistorialPrecioFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Exportar", cn => ArmarExcelAsync(cn, solapa, filtro, ct), ct);

    internal static async Task<byte[]> ArmarExcelAsync(SqlConnection cn, string solapa, HistorialPrecioFiltro filtro, CancellationToken ct)
    {
        var f = filtro.Copiar(); f.Pagina = 1; f.TamanoPagina = MaximoExcel;
        var cfg = await CargaComprobantesConfigPrecios.LeerAsync(cn, ct);
        var campo = NombreCampo(cfg, Columna(f.Campo));
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(solapa switch { "articulos" => "Por artículo", "alertas" => "Alertas", "resumen" => "Resumen", _ => "Cambios" });
        switch (solapa)
        {
            case "articulos":
                Hoja(ws, ["Código", "Descripción", "Rubro", "Proveedor", "Valor inicial", "Valor final", "Variación %", "Cambios", "Último cambio"],
                    (await LeerArticulosAsync(cn, f, ct)).Filas.Select(x => new object?[] { x.Codigo, x.Descripcion, x.Rubro, x.Proveedor, x.ValorInicial, x.ValorFinal, x.Pct, x.Cambios, x.UltimoCambio }));
                break;
            case "alertas":
                Hoja(ws, ["Alerta", "Código", "Descripción", "Fecha", "Valor A", "Valor B", "Variación A %", "Variación B %", "Usuario"],
                    (await LeerAlertasAsync(cn, f, ct)).Filas.Select(x => new object?[] { NombreAlerta(x.Tipo), x.Codigo, x.Descripcion, x.Fecha, x.ValorA, x.ValorB, x.PctA, x.PctB, x.Usuario }));
                break;
            case "resumen":
                var r = await LeerResumenAsync(cn, f, ct);
                Hoja(ws, ["Período", "Cambios", "Artículos", "Aumentos", "Bajas", "Mediana del cambio %", "Variación %", "Acumulada %"],
                    r.Periodos.Select(x => new object?[] { x.Periodo, x.Cambios, x.Articulos, x.Aumentos, x.Bajas, x.Mediana, x.Variacion, x.Acumulada }));
                break;
            default:
                Hoja(ws, ["Fecha", "Código", "Descripción", campo + " anterior", campo + " nuevo", "Diferencia", "Variación %", "Usuario", "Observaciones"],
                    (await LeerCambiosAsync(cn, f, ct)).Filas.Select(x => new object?[] { x.FechaHora, x.Codigo, x.Descripcion, x.Anterior, x.Nuevo, x.Nuevo - x.Anterior, x.Pct, x.Usuario, x.Observaciones }));
                break;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Hoja(IXLWorksheet ws, IReadOnlyList<string> titulos, IEnumerable<object?[]> filas)
    {
        for (var c = 0; c < titulos.Count; c++) ws.Cell(1, c + 1).Value = titulos[c];
        ws.Row(1).Style.Font.Bold = true;
        var f = 2;
        foreach (var fila in filas)
        {
            for (var c = 0; c < fila.Length; c++)
            {
                var celda = ws.Cell(f, c + 1);
                switch (fila[c])
                {
                    case DateTime d: celda.Value = d; celda.Style.DateFormat.Format = "dd/mm/yyyy hh:mm"; break;
                    case decimal m: celda.Value = m; celda.Style.NumberFormat.Format = "#,##0.00"; break;
                    case double p: celda.Value = Math.Round(p, 2); celda.Style.NumberFormat.Format = "0.00"; break;
                    case int i: celda.Value = i; break;
                    case string s: celda.Value = s; break;
                }
            }
            f++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, titulos.Count).AdjustToContents(1, Math.Min(f, 200));
    }

    internal static string NombreAlerta(string tipo) => tipo switch
    {
        HistorialPrecioAlertas.VariacionFuerte => "Variación fuerte",
        HistorialPrecioAlertas.QuedoEnCero => "Quedó en cero",
        HistorialPrecioAlertas.BajoCosto => "Precio bajo el costo",
        HistorialPrecioAlertas.Margen => "El precio no acompañó al costo",
        _ => tipo
    };

    /// <summary>Whitelist: el campo se interpola en el SQL, nunca llega texto del usuario.</summary>
    internal static string Columna(string? campo)
    {
        var c = (campo ?? string.Empty).Trim().ToUpperInvariant();
        return c is "COSTO" or "PRECIO1" or "PRECIO2" or "PRECIO3" or "PRECIO4" or "PRECIO5" or "PRECIO6" or "PRECIO7" or "PRECIO8"
            ? c : throw new ReglaException("Elegí el costo o una clase de precio.");
    }

    private static string NombreCampo(CargaComprobantesConfigPrecios cfg, string columna)
        => columna == "COSTO" ? "Costo" : cfg.NombreClase(columna[^1] - '0');

    internal static void Validar(HistorialPrecioFiltro f)
    {
        Columna(f.Campo);
        if (f.Hasta.Date < f.Desde.Date) throw new ReglaException("La fecha hasta no puede ser anterior a la fecha desde.");
        if (f.Desde.Date < f.Hasta.Date.AddMonths(-MaximoMeses)) throw new ReglaException($"El período puede abarcar hasta {MaximoMeses} meses.");
    }

    /// <summary>Por día hasta 45 días; por mes en períodos más largos.</summary>
    internal static bool AgrupaPorDia(HistorialPrecioFiltro f) => (f.Hasta.Date - f.Desde.Date).TotalDays <= 45;

    /// <summary>Completa los períodos sin cambios y encadena la variación: cada artículo del alcance pesa lo mismo y el que no cambió aporta 0.</summary>
    internal static List<HistorialPrecioPeriodoDto> Encadenar(IEnumerable<HistorialPrecioPeriodoDto> filas, DateTime desde, DateTime hasta, bool porDia, int universo)
    {
        var porPeriodo = filas.ToDictionary(x => x.Periodo.Date);
        var salida = new List<HistorialPrecioPeriodoDto>();
        var inicio = porDia ? desde.Date : new DateTime(desde.Year, desde.Month, 1);
        var acumuladoLn = 0d;
        for (var p = inicio; p <= hasta.Date; p = porDia ? p.AddDays(1) : p.AddMonths(1))
        {
            var fila = porPeriodo.GetValueOrDefault(p) ?? new HistorialPrecioPeriodoDto { Periodo = p };
            var ln = universo > 0 ? (fila.SumaLn ?? 0) / universo : 0;
            acumuladoLn += ln;
            fila.Variacion = (Math.Exp(ln) - 1) * 100;
            fila.Acumulada = (Math.Exp(acumuladoLn) - 1) * 100;
            salida.Add(fila);
        }
        return salida;
    }

    private static DynamicParameters Parametros(HistorialPrecioFiltro f)
    {
        var p = new DynamicParameters();
        p.Add("Desde", f.Desde.Date);
        p.Add("HastaExcl", f.Hasta.Date.AddDays(1));
        p.Add("Lista", (f.Origen ?? string.Empty).Trim());
        p.Add("TextoLike", SearchTextHelper.LikeContains(f.Texto));
        p.Add("Rubro", (f.RubroCodigo ?? string.Empty).Trim());
        p.Add("Marca", (f.MarcaCodigo ?? string.Empty).Trim());
        p.Add("Proveedor", (f.ProveedorCodigo ?? string.Empty).Trim());
        p.Add("UsuarioLike", SearchTextHelper.LikeContains(f.Usuario));
        p.Add("Sentido", (f.Sentido ?? string.Empty).Trim().ToUpperInvariant());
        p.Add("VarMin", (double)Math.Abs(f.VariacionMinima));
        p.Add("Umbral", (double)(f.UmbralAlerta > 0 ? f.UmbralAlerta : 30));
        p.Add("SoloActivos", f.SoloActivos);
        p.Add("TipoAlerta", (f.TipoAlerta ?? string.Empty).Trim());
        var tamano = Math.Clamp(f.TamanoPagina, 1, MaximoExcel);
        p.Add("Skip", (Math.Max(f.Pagina, 1) - 1) * tamano);
        p.Add("PageSize", tamano);
        return p;
    }

    private static string CondicionLista(HistorialPrecioFiltro f)
        => string.IsNullOrWhiteSpace(f.Origen) ? "h.IdLista IS NULL" : "LTRIM(RTRIM(h.IdLista)) = @Lista";

    /// <summary>
    /// Arma #u (artículos del alcance), #h (fotos del período más la última anterior) y #c (cambios reales del campo elegido).
    /// Las listas pueden tener un precio por unidad, por eso la secuencia se arma por artículo y unidad.
    /// </summary>
    internal static string SqlBase(HistorialPrecioFiltro f, string columna, string columnaControl, string? comparado = null)
    {
        var esLista = !string.IsNullOrWhiteSpace(f.Origen);
        var lista = CondicionLista(f);
        string Actual(string col) => esLista
            ? $"(SELECT MAX(p.{col}) FROM dbo.V_MA_Precios p WHERE p.IdArticulo = a.IDARTICULO AND LTRIM(RTRIM(p.IdLista)) = @Lista)"
            : $"a.{col}";
        var existeLista = esLista ? "AND EXISTS (SELECT 1 FROM dbo.V_MA_Precios p WHERE p.IdArticulo = a.IDARTICULO AND LTRIM(RTRIM(p.IdLista)) = @Lista)" : "";
        var unidad = esLista ? "LTRIM(RTRIM(ISNULL(h.IdUnidad, '')))" : "N''";
        var valor2 = comparado is null ? "CAST(NULL AS money)" : $"h.{comparado}";
        return $"""
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            CREATE TABLE #u (IdArticulo nvarchar(25) COLLATE DATABASE_DEFAULT NOT NULL, Codigo nvarchar(25) COLLATE DATABASE_DEFAULT NOT NULL,
                Descripcion nvarchar(250) COLLATE DATABASE_DEFAULT NOT NULL, Rubro nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
                Proveedor nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL, ValorActual money NULL, ValorComparado money NULL, CostoActual money NULL, PrecioControl money NULL);
            INSERT INTO #u (IdArticulo, Codigo, Descripcion, Rubro, Proveedor, ValorActual, ValorComparado, CostoActual, PrecioControl)
            SELECT a.IDARTICULO, LTRIM(RTRIM(a.IDARTICULO)), ISNULL(a.DESCRIPCION, ''), LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))),
                   LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR, ''))), {Actual(columna)}, {(comparado is null ? "NULL" : Actual(comparado))}, a.COSTO, {Actual(columnaControl)}
            FROM dbo.V_MA_ARTICULOS a
            WHERE (@TextoLike = '' OR ISNULL(a.DESCRIPCION, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike OR ISNULL(a.IDARTICULO, '') LIKE @TextoLike OR ISNULL(a.CODIGOBARRA, '') LIKE @TextoLike)
              AND (@Rubro = '' OR LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = @Rubro)
              AND (@Marca = '' OR LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = @Marca)
              AND (@Proveedor = '' OR LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR, ''))) = @Proveedor)
              AND (@SoloActivos = 0 OR ISNULL(a.SUSPENDIDO, 0) = 0)
              {existeLista};
            CREATE CLUSTERED INDEX IX_u ON #u (IdArticulo);

            CREATE TABLE #h (Id int NOT NULL, IdArticulo nvarchar(25) COLLATE DATABASE_DEFAULT NOT NULL, Unidad nvarchar(10) COLLATE DATABASE_DEFAULT NOT NULL,
                FechaHora datetime NOT NULL, Valor money NULL, Valor2 money NULL, CostoFoto money NULL, ControlFoto money NULL, Moneda nvarchar(4) COLLATE DATABASE_DEFAULT NULL, Usuario nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                Observaciones nvarchar(250) COLLATE DATABASE_DEFAULT NULL, Orden int NOT NULL);
            INSERT INTO #h (Id, IdArticulo, Unidad, FechaHora, Valor, Valor2, Moneda, Usuario, Observaciones, Orden)
            SELECT h.Id, h.IdArticulo, {unidad}, h.FechaHora, h.{columna}, {valor2}, LTRIM(RTRIM(h.Moneda)), LTRIM(RTRIM(h.Usuario)), LTRIM(RTRIM(h.Observaciones)),
                   ROW_NUMBER() OVER (PARTITION BY h.IdArticulo, {unidad} ORDER BY h.FechaHora, h.Id)
            FROM dbo.V_MV_PreciosHis h
            INNER JOIN #u u ON u.IdArticulo = h.IdArticulo
            WHERE h.FechaHora >= @Desde AND h.FechaHora < @HastaExcl AND {lista};

            -- Ultima foto anterior al periodo de cada articulo, en dos pasos: mas rapido que un TOP 1 por articulo.
            -- Se ordena por fecha y despues por Id, igual que la secuencia del periodo (hay Id viejos con fechas posteriores).
            SELECT h.IdArticulo, {unidad} AS Unidad, MAX(h.FechaHora) AS FechaHora INTO #f
            FROM dbo.V_MV_PreciosHis h
            WHERE h.FechaHora < @Desde AND {lista} AND h.IdArticulo IN (SELECT IdArticulo FROM #h)
            GROUP BY h.IdArticulo{(esLista ? ", " + unidad : "")};
            SELECT f.IdArticulo, f.Unidad, MAX(h.Id) AS Id INTO #b
            FROM #f f
            INNER JOIN dbo.V_MV_PreciosHis h ON h.IdArticulo = f.IdArticulo AND h.FechaHora = f.FechaHora AND {lista} AND {unidad} = f.Unidad
            GROUP BY f.IdArticulo, f.Unidad;
            INSERT INTO #h (Id, IdArticulo, Unidad, FechaHora, Valor, Valor2, CostoFoto, ControlFoto, Moneda, Orden)
            SELECT h.Id, b.IdArticulo, b.Unidad, h.FechaHora, h.{columna}, {valor2}, h.COSTO, h.{columnaControl}, LTRIM(RTRIM(h.Moneda)), 0
            FROM #b b INNER JOIN dbo.V_MV_PreciosHis h ON h.Id = b.Id;
            CREATE CLUSTERED INDEX IX_h ON #h (IdArticulo, Unidad, Orden);

            {SqlCambios("#c", "Valor")}
            {(comparado is null ? "" : SqlCambios("#c2", "Valor2"))}
            """;
    }

    private static string SqlCambios(string destino, string valor) => $"""
        SELECT c.Id, c.IdArticulo, u.Codigo, u.Descripcion, u.Rubro, u.Proveedor, c.FechaHora,
               ISNULL(p.{valor}, 0) AS Anterior, ISNULL(c.{valor}, 0) AS Nuevo, ISNULL(c.Usuario, N'') AS Usuario, ISNULL(c.Observaciones, N'') AS Observaciones,
               CASE WHEN ISNULL(p.{valor}, 0) > 0 AND ISNULL(c.Moneda, '') = ISNULL(p.Moneda, '')
                    THEN (CAST(ISNULL(c.{valor}, 0) AS float) / CAST(p.{valor} AS float) - 1) * 100 END AS Pct,
               CASE WHEN ISNULL(p.{valor}, 0) > 0 AND ISNULL(c.{valor}, 0) > 0 AND ISNULL(c.Moneda, '') = ISNULL(p.Moneda, '')
                    THEN CASE WHEN CAST(c.{valor} AS float) / CAST(p.{valor} AS float) BETWEEN 0.2 AND 5
                              THEN LOG(CAST(c.{valor} AS float) / CAST(p.{valor} AS float)) END END AS Ln
        INTO {destino}
        FROM #h c
        INNER JOIN #h p ON p.IdArticulo = c.IdArticulo AND p.Unidad = c.Unidad AND p.Orden = c.Orden - 1
        INNER JOIN #u u ON u.IdArticulo = c.IdArticulo
        WHERE c.Orden > 0 AND ABS(ISNULL(c.{valor}, 0) - ISNULL(p.{valor}, 0)) >= 0.005; -- Menos de medio centavo es redondeo, no un cambio de precio.

        DELETE FROM {destino}
        WHERE (@UsuarioLike <> '' AND Usuario NOT LIKE @UsuarioLike)
           OR (@Sentido = 'SUBE' AND Nuevo <= Anterior)
           OR (@Sentido = 'BAJA' AND Nuevo >= Anterior)
           OR (@VarMin > 0 AND (Pct IS NULL OR ABS(Pct) < @VarMin));

        """;

    /// <summary>Mediana sin PERCENTILE_CONT (compatibilidad 100).</summary>
    private static string Medianas(string clave, string tabla = "#c") => $"""
        r AS (SELECT {clave} AS K, Pct, ROW_NUMBER() OVER (PARTITION BY {clave} ORDER BY Pct) AS N, COUNT(*) OVER (PARTITION BY {clave}) AS T FROM {tabla} WHERE Ln IS NOT NULL),
        m AS (SELECT K, AVG(Pct) AS Mediana FROM r WHERE N IN ((T + 1) / 2, (T + 2) / 2) GROUP BY K)
        """;

    private const string Contadores = """
        COUNT(*) AS Cambios, COUNT(DISTINCT IdArticulo) AS Articulos,
        SUM(CASE WHEN Nuevo > Anterior THEN 1 ELSE 0 END) AS Aumentos, SUM(CASE WHEN Nuevo < Anterior THEN 1 ELSE 0 END) AS Bajas
        """;

    private static string SqlPeriodos(bool porDia, string tabla = "#c")
    {
        var clave = porDia ? "DATEADD(day, DATEDIFF(day, 0, FechaHora), 0)" : "DATEADD(month, DATEDIFF(month, 0, FechaHora), 0)";
        return $"""
            ;WITH {Medianas(clave, tabla)},
            g AS (SELECT {clave} AS K, {Contadores}, SUM(Ln) AS SumaLn FROM {tabla} GROUP BY {clave})
            SELECT g.K AS Periodo, g.Cambios, g.Articulos, g.Aumentos, g.Bajas, m.Mediana, g.SumaLn
            FROM g LEFT JOIN m ON m.K = g.K ORDER BY g.K;

            """;
    }

    private static string SqlGrupo(string clave, string nombre, bool conVariacion) => $"""
        ;WITH {Medianas(clave)},
        g AS (SELECT {clave} AS K, {Contadores}, SUM(Ln) AS SumaLn FROM #c GROUP BY {clave}),
        n AS ({(conVariacion ? $"SELECT {clave} AS K, COUNT(*) AS N FROM #u WHERE ValorActual > 0 GROUP BY {clave}" : "SELECT g.K, 0 AS N FROM g WHERE 1 = 0")})
        SELECT TOP (10) g.K AS Codigo, {nombre} AS Nombre, g.Cambios, g.Articulos, g.Aumentos, g.Bajas, m.Mediana,
               {(conVariacion ? "(EXP(ISNULL(g.SumaLn, 0) / NULLIF(n.N, 0)) - 1) * 100" : "CAST(NULL AS float)")} AS Variacion
        FROM g LEFT JOIN m ON m.K = g.K LEFT JOIN n ON n.K = g.K
        ORDER BY {(conVariacion ? "CASE WHEN n.N >= 5 THEN 0 ELSE 1 END, (EXP(ISNULL(g.SumaLn, 0) / NULLIF(n.N, 0)) - 1) * 100 DESC," : "")} g.Cambios DESC;

        """;

    internal static async Task<HistorialPrecioContextoDto> LeerContextoAsync(SqlConnection cn, CancellationToken ct)
    {
        var cfg = await CargaComprobantesConfigPrecios.LeerAsync(cn, ct);
        async Task<IReadOnlyList<HistorialPrecioOpcion>> Opciones(string sql)
            => (await cn.QueryAsync<(string Codigo, string Nombre)>(new CommandDefinition(sql, cancellationToken: ct))).Select(x => new HistorialPrecioOpcion(x.Codigo, x.Nombre)).ToList();
        return new HistorialPrecioContextoDto
        {
            ClaseVenta = cfg.ClaseVenta,
            Campos = [new("COSTO", "Costo"), .. Enumerable.Range(1, 8).Select(c => new HistorialPrecioOpcion($"PRECIO{c}", cfg.NombreClase(c)))],
            Listas = await Opciones("SELECT LTRIM(RTRIM(IdLista)), LTRIM(RTRIM(IdLista)) + ' · ' + ISNULL(Nombre, '') FROM dbo.V_MA_PRECIOSCAB WHERE TipoLista = 'V' ORDER BY Nombre;"),
            Rubros = await Opciones("SELECT LTRIM(RTRIM(IdRubro)), ISNULL(Descripcion, '') FROM dbo.v_ta_rubros ORDER BY Descripcion;"),
            Marcas = await Opciones("SELECT LTRIM(RTRIM(IdTipo)), ISNULL(Descripcion, '') FROM dbo.v_ta_tipoArticulo ORDER BY Descripcion;")
        };
    }

    /// <summary>
    /// Precio contra el que se controla el costo: el elegido o, si se mira el costo, la clase de venta configurada.
    /// Si esa clase casi no se usa en la base, la que más artículos tiene por encima del costo.
    /// </summary>
    internal static async Task<string> ColumnaControlAsync(SqlConnection cn, string columna, CancellationToken ct)
    {
        if (columna != "COSTO") return columna;
        var cfg = await CargaComprobantesConfigPrecios.LeerAsync(cn, ct);
        var conteos = string.Join(", ", Enumerable.Range(1, 8).Select(c => $"SUM(CASE WHEN a.PRECIO{c} > a.COSTO AND a.COSTO > 0 THEN 1 ELSE 0 END) AS P{c}"));
        var fila = (IDictionary<string, object?>)await cn.QuerySingleAsync(new CommandDefinition(
            $"SELECT COUNT(*) AS Total, {conteos} FROM dbo.V_MA_ARTICULOS a WITH (NOLOCK) WHERE ISNULL(a.SUSPENDIDO, 0) = 0;", commandTimeout: Timeout, cancellationToken: ct));
        var usados = Enumerable.Range(1, 8).ToDictionary(c => c, c => Convert.ToInt32(fila[$"P{c}"] ?? 0));
        return $"PRECIO{ClaseDeReferencia(cfg.ClaseVenta, usados, Convert.ToInt32(fila["Total"] ?? 0))}";
    }

    internal static int ClaseDeReferencia(int claseVenta, IReadOnlyDictionary<int, int> articulosSobreCosto, int total)
        => claseVenta is >= 1 and <= 8 && articulosSobreCosto.GetValueOrDefault(claseVenta) * 2 >= total ? claseVenta
            : articulosSobreCosto.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key).FirstOrDefault(1);

    internal static async Task<HistorialPrecioResumenDto> LeerResumenAsync(SqlConnection cn, HistorialPrecioFiltro f, CancellationToken ct)
    {
        Validar(f);
        var columna = Columna(f.Campo);
        var control = await ColumnaControlAsync(cn, columna, ct);
        var porDia = AgrupaPorDia(f);
        var comparado = columna == "COSTO" ? control : "COSTO";
        var sql = SqlBase(f, columna, control, comparado) + $"""
            ;WITH {Medianas("1")}
            SELECT t.Cambios, t.Articulos, t.Aumentos, t.Bajas, t.Atipicos, t.MayorSuba, t.MayorBaja, m.Mediana,
                   (SELECT COUNT(*) FROM #u WHERE ValorActual > 0) AS Universo
            FROM (SELECT 1 AS K, {Contadores}, SUM(CASE WHEN Pct IS NOT NULL AND Ln IS NULL THEN 1 ELSE 0 END) AS Atipicos,
                         MAX(CASE WHEN Ln IS NOT NULL THEN Pct END) AS MayorSuba, MIN(CASE WHEN Ln IS NOT NULL THEN Pct END) AS MayorBaja FROM #c) t
            LEFT JOIN m ON m.K = t.K;

            """ + SqlPeriodos(porDia)
            + SqlGrupo("Rubro", "ISNULL((SELECT TOP (1) x.Descripcion FROM dbo.v_ta_rubros x WHERE LTRIM(RTRIM(x.IdRubro)) = g.K), CASE WHEN g.K = '' THEN 'Sin rubro' ELSE g.K END)", true)
            + SqlGrupo("Proveedor", "ISNULL((SELECT TOP (1) x.RAZON_SOCIAL FROM dbo.vt_proveedores x WHERE LTRIM(RTRIM(x.CODIGO)) = g.K), CASE WHEN g.K = '' THEN 'Sin proveedor' ELSE g.K END)", true)
            + SqlGrupo("Usuario", "CASE WHEN g.K = '' THEN 'Sin usuario' ELSE g.K END", false)
            + "SELECT COUNT(*) FROM #u WHERE ValorComparado > 0;\n" + SqlPeriodos(porDia, "#c2");

        await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, Parametros(f), commandTimeout: Timeout, cancellationToken: ct));
        var r = await multi.ReadSingleAsync<HistorialPrecioResumenDto>();
        var periodos = (await multi.ReadAsync<HistorialPrecioPeriodoDto>()).ToList();
        r.Rubros = (await multi.ReadAsync<HistorialPrecioGrupoDto>()).ToList();
        r.Proveedores = (await multi.ReadAsync<HistorialPrecioGrupoDto>()).ToList();
        r.Usuarios = (await multi.ReadAsync<HistorialPrecioGrupoDto>()).ToList();
        var universoComparado = await multi.ReadSingleAsync<int>();
        var comparados = await multi.ReadAsync<HistorialPrecioPeriodoDto>();
        r.PorDia = porDia;
        r.Periodos = Encadenar(periodos, f.Desde, f.Hasta, porDia, r.Universo);
        r.PeriodosComparados = Encadenar(comparados, f.Desde, f.Hasta, porDia, universoComparado);
        r.VariacionAcumulada = r.Periodos.Count > 0 ? r.Periodos[^1].Acumulada : 0;
        r.CampoComparado = comparado;
        r.VariacionComparada = r.PeriodosComparados.Count > 0 ? r.PeriodosComparados[^1].Acumulada : 0;
        return r;
    }

    internal static async Task<HistorialPrecioPagina<HistorialPrecioCambioDto>> LeerCambiosAsync(SqlConnection cn, HistorialPrecioFiltro f, CancellationToken ct)
    {
        Validar(f);
        var columna = Columna(f.Campo);
        var orden = f.Orden switch
        {
            HistorialPrecioOrden.FechaAsc => "FechaHora, Id",
            HistorialPrecioOrden.VariacionDesc => "CASE WHEN Pct IS NULL THEN 1 ELSE 0 END, Pct DESC, FechaHora DESC, Id DESC",
            HistorialPrecioOrden.VariacionAsc => "CASE WHEN Pct IS NULL THEN 1 ELSE 0 END, Pct, FechaHora DESC, Id DESC",
            HistorialPrecioOrden.Articulo => "Descripcion, Codigo, FechaHora DESC, Id DESC",
            _ => "FechaHora DESC, Id DESC"
        };
        var sql = SqlBase(f, columna, await ColumnaControlAsync(cn, columna, ct)) + $"""
            ;WITH Pagina AS (SELECT Id, IdArticulo, Codigo, Descripcion, FechaHora, Anterior, Nuevo, Pct, Usuario, Observaciones, ROW_NUMBER() OVER (ORDER BY {orden}) AS Fila FROM #c)
            SELECT * FROM Pagina {SqlServerCompatibilidad.Paginar(cn.ServerVersion, "Fila")};
            SELECT COUNT(*) FROM #c;
            """;
        await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, Parametros(f), commandTimeout: Timeout, cancellationToken: ct));
        var filas = (await multi.ReadAsync<HistorialPrecioCambioDto>()).ToList();
        return new() { Filas = filas, Total = await multi.ReadSingleAsync<int>(), Pagina = Math.Max(f.Pagina, 1), TamanoPagina = f.TamanoPagina };
    }

    internal static async Task<HistorialPrecioPagina<HistorialPrecioArticuloDto>> LeerArticulosAsync(SqlConnection cn, HistorialPrecioFiltro f, CancellationToken ct)
    {
        Validar(f);
        var columna = Columna(f.Campo);
        var orden = f.Orden switch
        {
            HistorialPrecioOrden.VariacionAsc => "CASE WHEN Pct IS NULL THEN 1 ELSE 0 END, Pct, Descripcion",
            HistorialPrecioOrden.Articulo => "Descripcion, Codigo",
            HistorialPrecioOrden.FechaAsc => "UltimoCambio DESC, Descripcion",
            _ => "CASE WHEN Pct IS NULL THEN 1 ELSE 0 END, Pct DESC, Descripcion"
        };
        var sql = SqlBase(f, columna, await ColumnaControlAsync(cn, columna, ct)) + $"""
            ;WITH o AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY IdArticulo ORDER BY FechaHora, Id) AS N1,
                               ROW_NUMBER() OVER (PARTITION BY IdArticulo ORDER BY FechaHora DESC, Id DESC) AS N2,
                               COUNT(*) OVER (PARTITION BY IdArticulo) AS Cant FROM #c)
            SELECT p.IdArticulo, p.Codigo, p.Descripcion, p.Rubro, p.Proveedor, p.Anterior AS ValorInicial, u.Nuevo AS ValorFinal, p.Cant AS Cambios, u.FechaHora AS UltimoCambio,
                   CASE WHEN p.Anterior > 0 THEN (CAST(u.Nuevo AS float) / CAST(p.Anterior AS float) - 1) * 100 END AS Pct
            INTO #r FROM o p INNER JOIN o u ON u.IdArticulo = p.IdArticulo AND u.N2 = 1 WHERE p.N1 = 1;
            ;WITH Pagina AS (SELECT r.*, ROW_NUMBER() OVER (ORDER BY {orden}) AS Fila FROM #r r)
            SELECT p.IdArticulo, p.Codigo, p.Descripcion, p.ValorInicial, p.ValorFinal, p.Cambios, p.UltimoCambio, p.Pct,
                   ISNULL((SELECT TOP (1) x.Descripcion FROM dbo.v_ta_rubros x WHERE LTRIM(RTRIM(x.IdRubro)) = p.Rubro), p.Rubro) AS Rubro,
                   ISNULL((SELECT TOP (1) x.RAZON_SOCIAL FROM dbo.vt_proveedores x WHERE LTRIM(RTRIM(x.CODIGO)) = p.Proveedor), p.Proveedor) AS Proveedor
            FROM Pagina p {SqlServerCompatibilidad.Paginar(cn.ServerVersion, "p.Fila")};
            SELECT COUNT(*) FROM #r;
            """;
        await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, Parametros(f), commandTimeout: Timeout, cancellationToken: ct));
        var filas = (await multi.ReadAsync<HistorialPrecioArticuloDto>()).ToList();
        return new() { Filas = filas, Total = await multi.ReadSingleAsync<int>(), Pagina = Math.Max(f.Pagina, 1), TamanoPagina = f.TamanoPagina };
    }

    internal static async Task<HistorialPrecioPagina<HistorialPrecioAlertaDto>> LeerAlertasAsync(SqlConnection cn, HistorialPrecioFiltro f, CancellationToken ct)
    {
        Validar(f);
        var columna = Columna(f.Campo);
        var control = await ColumnaControlAsync(cn, columna, ct);
        var sql = SqlBase(f, columna, control) + $"""
            CREATE TABLE #a (Tipo nvarchar(12) COLLATE DATABASE_DEFAULT NOT NULL, IdArticulo nvarchar(25) COLLATE DATABASE_DEFAULT NOT NULL, Codigo nvarchar(25) COLLATE DATABASE_DEFAULT NOT NULL,
                Descripcion nvarchar(250) COLLATE DATABASE_DEFAULT NOT NULL, Fecha datetime NULL, ValorA money NULL, ValorB money NULL, PctA float NULL, PctB float NULL, Usuario nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL);
            INSERT INTO #a SELECT '{HistorialPrecioAlertas.VariacionFuerte}', IdArticulo, Codigo, Descripcion, FechaHora, Anterior, Nuevo, Pct, NULL, Usuario FROM #c WHERE Nuevo > 0 AND ABS(Pct) >= @Umbral;
            INSERT INTO #a SELECT '{HistorialPrecioAlertas.QuedoEnCero}', IdArticulo, Codigo, Descripcion, FechaHora, Anterior, Nuevo, Pct, NULL, Usuario FROM #c WHERE Nuevo = 0 AND Anterior > 0;
            INSERT INTO #a SELECT '{HistorialPrecioAlertas.BajoCosto}', IdArticulo, Codigo, Descripcion, NULL, CostoActual, PrecioControl,
                   (CAST(PrecioControl AS float) / CAST(CostoActual AS float) - 1) * 100, NULL, N''
            FROM #u WHERE CostoActual > 0 AND PrecioControl > 0 AND PrecioControl < CostoActual;
            INSERT INTO #a SELECT '{HistorialPrecioAlertas.Margen}', u.IdArticulo, u.Codigo, u.Descripcion, NULL, u.CostoActual, u.PrecioControl, (v.RC - 1) * 100, (v.RP - 1) * 100, N''
            FROM #u u
            INNER JOIN (SELECT IdArticulo, MAX(CostoFoto) AS Costo, MAX(ControlFoto) AS Precio FROM #h WHERE Orden = 0 GROUP BY IdArticulo) b ON b.IdArticulo = u.IdArticulo
            CROSS APPLY (SELECT CASE WHEN b.Costo > 0 THEN CAST(u.CostoActual AS float) / CAST(b.Costo AS float) END AS RC,
                                CASE WHEN b.Precio > 0 THEN CAST(u.PrecioControl AS float) / CAST(b.Precio AS float) END AS RP) v
            WHERE v.RC BETWEEN 1.02 AND 5 AND v.RP BETWEEN 0.2 AND 5 AND v.RP < v.RC - 0.02;

            SELECT Tipo, COUNT(*) AS Cantidad FROM #a GROUP BY Tipo;
            ;WITH Pagina AS (SELECT *, ROW_NUMBER() OVER (ORDER BY CASE WHEN Fecha IS NULL THEN 1 ELSE 0 END, Fecha DESC, ABS(ISNULL(PctA, 0)) DESC, Descripcion) AS Fila
                             FROM #a WHERE @TipoAlerta = '' OR Tipo = @TipoAlerta)
            SELECT * FROM Pagina {SqlServerCompatibilidad.Paginar(cn.ServerVersion, "Fila")};
            SELECT COUNT(*) FROM #a WHERE @TipoAlerta = '' OR Tipo = @TipoAlerta;
            """;
        await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, Parametros(f), commandTimeout: Timeout, cancellationToken: ct));
        var conteos = (await multi.ReadAsync<(string Tipo, int Cantidad)>()).Select(x => new HistorialPrecioAlertaConteo(x.Tipo, x.Cantidad)).ToList();
        var filas = (await multi.ReadAsync<HistorialPrecioAlertaDto>()).ToList();
        return new() { Filas = filas, Total = await multi.ReadSingleAsync<int>(), Pagina = Math.Max(f.Pagina, 1), TamanoPagina = f.TamanoPagina, Conteos = conteos, CampoControl = control };
    }

    internal static async Task<HistorialPrecioFichaDto> LeerFichaAsync(SqlConnection cn, string idArticulo, HistorialPrecioFiltro f, CancellationToken ct)
    {
        Validar(f);
        var esLista = !string.IsNullOrWhiteSpace(f.Origen);
        var lista = CondicionLista(f);
        const string valores = "ISNULL({0}COSTO,0) Costo, ISNULL({0}PRECIO1,0) Precio1, ISNULL({0}PRECIO2,0) Precio2, ISNULL({0}PRECIO3,0) Precio3, ISNULL({0}PRECIO4,0) Precio4, ISNULL({0}PRECIO5,0) Precio5, ISNULL({0}PRECIO6,0) Precio6, ISNULL({0}PRECIO7,0) Precio7, ISNULL({0}PRECIO8,0) Precio8";
        var actual = esLista
            ? $"SELECT TOP (1) {string.Format(valores, "p.")} FROM dbo.V_MA_Precios p WHERE p.IdArticulo = @Articulo AND LTRIM(RTRIM(p.IdLista)) = @Lista;"
            : $"SELECT TOP (1) {string.Format(valores, "a.")} FROM dbo.V_MA_ARTICULOS a WHERE a.IDARTICULO = @Articulo;";
        var sql = $"""
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT TOP (1) LTRIM(RTRIM(a.IDARTICULO)) AS Codigo, ISNULL(a.DESCRIPCION, '') AS Descripcion,
                   ISNULL((SELECT TOP (1) x.RAZON_SOCIAL FROM dbo.vt_proveedores x WHERE LTRIM(RTRIM(x.CODIGO)) = LTRIM(RTRIM(a.CUENTAPROVEEDOR))), '') AS Proveedor
            FROM dbo.V_MA_ARTICULOS a WHERE a.IDARTICULO = @Articulo;
            {actual}
            DECLARE @Base datetime;
            SELECT @Base = MAX(h.FechaHora) FROM dbo.V_MV_PreciosHis h WHERE h.IdArticulo = @Articulo AND h.FechaHora < @Desde AND {lista};
            SELECT TOP ({MaximoPuntosFicha * 3}) h.FechaHora, {string.Format(valores, "h.")}, LTRIM(RTRIM(ISNULL(h.IdUnidad, ''))) AS IdUnidad,
                   LTRIM(RTRIM(ISNULL(h.Moneda, ''))) AS Moneda, LTRIM(RTRIM(ISNULL(h.Usuario, ''))) AS Usuario, LTRIM(RTRIM(ISNULL(h.Observaciones, ''))) AS Observaciones
            FROM dbo.V_MV_PreciosHis h
            WHERE h.IdArticulo = @Articulo AND h.FechaHora >= ISNULL(@Base, @Desde) AND h.FechaHora < @HastaExcl AND {lista}
            ORDER BY h.FechaHora DESC, h.Id DESC;
            """;
        var p = Parametros(f);
        p.Add("Articulo", idArticulo);
        await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, p, commandTimeout: Timeout, cancellationToken: ct));
        var cab = await multi.ReadFirstOrDefaultAsync<(string Codigo, string Descripcion, string Proveedor)?>()
            ?? throw new ReglaException("El artículo ya no existe.");
        var vigente = await multi.ReadFirstOrDefaultAsync<HistorialPrecioPuntoDto>();
        var filas = (await multi.ReadAsync<HistorialPrecioPuntoDto>()).Reverse().ToList();
        var puntos = SoloCambios(filas);
        return new HistorialPrecioFichaDto
        {
            IdArticulo = idArticulo, Codigo = cab.Codigo, Descripcion = cab.Descripcion, Proveedor = cab.Proveedor, Actual = vigente,
            Puntos = puntos.TakeLast(MaximoPuntosFicha).ToList(), Truncado = puntos.Count > MaximoPuntosFicha
        };
    }

    /// <summary>Deja una sola unidad (la de la última foto) y descarta las fotos iguales a la anterior.</summary>
    internal static List<HistorialPrecioPuntoDto> SoloCambios(IReadOnlyList<HistorialPrecioPuntoDto> filas)
    {
        var salida = new List<HistorialPrecioPuntoDto>();
        if (filas.Count == 0) return salida;
        var unidad = filas[^1].IdUnidad;
        foreach (var x in filas.Where(x => x.IdUnidad == unidad))
        {
            if (salida.Count > 0 && Iguales(salida[^1], x)) continue;
            salida.Add(x);
        }
        return salida;
    }

    private static bool Iguales(HistorialPrecioPuntoDto a, HistorialPrecioPuntoDto b)
        => a.Costo == b.Costo && a.Precio1 == b.Precio1 && a.Precio2 == b.Precio2 && a.Precio3 == b.Precio3 && a.Precio4 == b.Precio4
           && a.Precio5 == b.Precio5 && a.Precio6 == b.Precio6 && a.Precio7 == b.Precio7 && a.Precio8 == b.Precio8;

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var sesion)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            if (!await permissions.HasAccessAsync(MenuKey, ct))
                throw new ReglaException("Tu usuario no tiene permiso para el historial de precios.");
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            var r = await operacion(cn);
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != sesion)
                throw new ReglaException("Cambió la sesión. Volvé a abrir el historial de precios en la base activa.");
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            const string mensaje = "No se pudo consultar el historial de precios. Probá con un período más corto.";
            var codigo = await events.LogErrorAsync("HistorialPrecios", accion, ex, mensaje, ct: CancellationToken.None);
            throw new AppUserFacingException(mensaje, codigo, ex);
        }
    }
}

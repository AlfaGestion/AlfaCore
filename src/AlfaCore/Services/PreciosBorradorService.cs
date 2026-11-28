using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IPreciosBorradorService
{
    Task<PrecioBorradorPagina> ListarAsync(PrecioBorradorFiltro filtro, CancellationToken ct = default);
    /// <summary>Pasa las filas del borrador a los precios reales y las borra del borrador.</summary>
    Task<PrecioBorradorResultado> AplicarAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);
    /// <summary>Borra filas del borrador sin aplicarlas.</summary>
    Task<PrecioBorradorResultado> DescartarAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);
}

/// <summary>
/// Borrador de precios (D010508 del legacy, FrmBorradorPrecios.frm). Lee y aplica V_MA_PRECIOS_BORRADOR, la misma tabla que usa
/// Alfa Gestión, así un cambio generado en cualquiera de los dos se aplica desde cualquiera. Diferencias con el legacy:
///  - todo en una transacción y con auditoría; el usuario que queda en los precios es el que aplica, no el que generó;
///  - un precio o costo en 0/vacío en el borrador no se aplica (se conserva el actual): el legacy dejaba en 0 los precios que la
///    fila no traía;
///  - se aplican/descartan las filas elegidas (el legacy borraba todas las del artículo, de todos los usuarios);
///  - al aplicar precios del maestro se recalculan los productos que los usan como insumo (fórmulas con ClasePr).
/// </summary>
public sealed class PreciosBorradorService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events) : IPreciosBorradorService
{
    /// <summary>Clave legacy «Borrador de Cambios de Precios» (Shell:EM_PLANILLAEDITDATOS @PARAMETROADICIONAL=BORRADOR).</summary>
    public const string MenuKey = "D010508";
    private const int MaxFilas = 5000;

    // Maestro: valores con "0 = no cambia" y el resto con "vacío = no cambia".
    private static readonly string[] MaestroPrecios = ["COSTO", "PRECIO1", "PRECIO2", "PRECIO3", "PRECIO4", "PRECIO5", "PRECIO6", "PRECIO7", "PRECIO8"];
    private static readonly string[] MaestroOtros = ["UTILIDAD", "POLITICAPRECIOS", "DTO1", "DTO2", "DTO3", "DTO4", "DTO5", "REC1", "REC2", "REC3", "TASAIVA", "IMPUESTOS"];
    private static readonly string[] ListaPrecios = ["COSTO", "PRECIO1", "PRECIO2", "PRECIO3", "PRECIO4", "PRECIO5", "PRECIO6", "PRECIO7", "PRECIO8", "PRECIO9", "PRECIO10"];
    private static readonly string[] ListaOtros = ["CantidadOf2", "CantidadOf3", "UTILIDAD", "FhOfertaDesde", "FhOfertaHasta"];

    private sealed class ReglaException(string message) : InvalidOperationException(message);

    public Task<PrecioBorradorPagina> ListarAsync(PrecioBorradorFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Listar", cn => ListarCoreAsync(cn, filtro, ct), "No se pudo leer el borrador de precios.", ct);

    internal static async Task<PrecioBorradorPagina> ListarCoreAsync(SqlConnection cn, PrecioBorradorFiltro filtro, CancellationToken ct, SqlTransaction? tx = null)
        {
            ArgumentNullException.ThrowIfNull(filtro);
            var pagina = new PrecioBorradorPagina
            {
                Pagina = Math.Max(1, filtro.Pagina),
                TamanoPagina = filtro.TamanoPagina is >= 10 and <= 500 ? filtro.TamanoPagina : 100
            };
            pagina.Instalado = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.V_MA_PRECIOS_BORRADOR', N'U') IS NULL THEN 0 ELSE 1 END;", transaction: tx, cancellationToken: ct)) == 1;
            if (!pagina.Instalado)
                return pagina;

            var cfg = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
                SELECT UPPER(LTRIM(RTRIM(CLAVE))), ISNULL(LTRIM(RTRIM(VALOR)), '') FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('CLASEPRECIOVENTA', 'PRECIOSARTICULOSPORBORRADOR', 'NOM_PCLASE1', 'NOM_PCLASE2', 'NOM_PCLASE3',
                    'NOM_PCLASE4', 'NOM_PCLASE5', 'NOM_PCLASE6', 'NOM_PCLASE7', 'NOM_PCLASE8');
                """, transaction: tx, cancellationToken: ct))).ToDictionary(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase);
            pagina.ClasePrecioVenta = int.TryParse(cfg.GetValueOrDefault("CLASEPRECIOVENTA"), out var cv) && cv is >= 1 and <= 8 ? cv : 1;
            var nombre = cfg.GetValueOrDefault($"NOM_PCLASE{pagina.ClasePrecioVenta}")?.Trim();
            pagina.NombreClaseVenta = string.IsNullOrWhiteSpace(nombre) ? $"Precio {pagina.ClasePrecioVenta}" : nombre;
            pagina.BorradorActivo = cfg.GetValueOrDefault("PRECIOSARTICULOSPORBORRADOR")?.Trim().ToUpperInvariant() is "SI" or "S" or "1";

            var texto = (filtro.Texto ?? string.Empty).Trim();
            if (texto.Length > 50) throw new ReglaException("El texto de búsqueda es demasiado largo.");
            var tipo = (filtro.Tipo ?? string.Empty).Trim().ToUpperInvariant();
            var usuario = (filtro.Usuario ?? string.Empty).Trim();

            var filas = (await cn.QueryAsync<FilaPlana>(new CommandDefinition($"""
                SELECT TOP ({MaxFilas})
                    b.ID AS Id, LTRIM(RTRIM(b.IdArticulo)) AS IdArticulo,
                    ISNULL(a.DESCRIPCION, ISNULL(b.DESCRIPCIONARTICULO, '')) AS Descripcion,
                    UPPER(LTRIM(RTRIM(b.TipoLista))) AS Tipo, LTRIM(RTRIM(ISNULL(b.IdLista, ''))) AS IdLista,
                    LTRIM(RTRIM(ISNULL(b.Nombre, ''))) AS Lista, LTRIM(RTRIM(ISNULL(b.Usuario, ''))) AS Usuario,
                    b.COSTO AS N0, b.Precio1 AS N1, b.Precio2 AS N2, b.Precio3 AS N3, b.Precio4 AS N4, b.Precio5 AS N5,
                    b.PRECIO6 AS N6, b.PRECIO7 AS N7, b.PRECIO8 AS N8, b.PRECIO9 AS N9, b.PRECIO10 AS N10,
                    CONVERT(bit, CASE WHEN COALESCE(m.IDARTICULO, l.IDARTICULO, g.IDARTICULO) IS NULL THEN 1 ELSE 0 END) AS SinDestino,
                    COALESCE(m.COSTO, l.COSTO, g.COSTO) AS A0,
                    COALESCE(m.PRECIO1, l.PRECIO1, g.PRECIO1) AS A1, COALESCE(m.PRECIO2, l.PRECIO2, g.PRECIO2) AS A2,
                    COALESCE(m.PRECIO3, l.PRECIO3, g.PRECIO3) AS A3, COALESCE(m.PRECIO4, l.PRECIO4, g.PRECIO4) AS A4,
                    COALESCE(m.PRECIO5, l.PRECIO5, g.PRECIO5) AS A5, COALESCE(m.PRECIO6, l.PRECIO6, g.PRECIO6) AS A6,
                    COALESCE(m.PRECIO7, l.PRECIO7, g.PRECIO7) AS A7, COALESCE(m.PRECIO8, l.PRECIO8, g.PRECIO8) AS A8,
                    COALESCE(l.PRECIO9, g.PRECIO9) AS A9, COALESCE(l.PRECIO10, g.PRECIO10) AS A10
                FROM dbo.V_MA_PRECIOS_BORRADOR b
                LEFT JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = b.IdArticulo
                LEFT JOIN dbo.V_MA_ARTICULOS m ON UPPER(b.TipoLista) = 'B' AND m.IDARTICULO = b.IdArticulo
                LEFT JOIN dbo.V_MA_PRECIOS l ON UPPER(b.TipoLista) = 'V' AND l.IDARTICULO = b.IdArticulo
                    AND LTRIM(RTRIM(l.IdLista)) = LTRIM(RTRIM(b.IdLista)) AND l.TIPOLISTA = 'V'
                OUTER APPLY (SELECT TOP (1) p.* FROM dbo.V_MA_PRECIOS p
                             INNER JOIN dbo.V_MA_PRECIOSCAB c ON LTRIM(RTRIM(c.IDLISTA)) = LTRIM(RTRIM(p.IdLista)) AND c.TIPOLISTA = 'V'
                             WHERE UPPER(b.TipoLista) = 'G' AND p.IDARTICULO = b.IdArticulo AND p.TIPOLISTA = 'V'
                               AND LTRIM(RTRIM(c.GRUPO)) = LTRIM(RTRIM(b.Nombre))
                             ORDER BY p.IdLista) g
                WHERE UPPER(b.TipoLista) <> 'M'
                  AND (@Tipo = '' OR (@Tipo = 'B' AND UPPER(b.TipoLista) = 'B') OR (@Tipo = 'V' AND UPPER(b.TipoLista) IN ('V', 'G')))
                  AND (@Usuario = '' OR LTRIM(RTRIM(ISNULL(b.Usuario, ''))) = @Usuario)
                  AND (@Texto = '' OR LTRIM(RTRIM(b.IdArticulo)) = @Texto OR LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, ''))) = @Texto
                       OR a.DESCRIPCION LIKE '%' + @Texto + '%')
                ORDER BY ISNULL(a.DESCRIPCION, b.DESCRIPCIONARTICULO), b.IdArticulo, b.TipoLista, b.IdLista;
                """, new { Tipo = tipo, Usuario = usuario, Texto = texto }, tx, commandTimeout: 120, cancellationToken: ct))).ToList();

            pagina.Total = filas.Count;
            pagina.IdsFiltrados = filas.Select(f => f.Id).ToList();
            pagina.Usuarios = (await cn.QueryAsync<string>(new CommandDefinition("""
                SELECT DISTINCT LTRIM(RTRIM(Usuario)) FROM dbo.V_MA_PRECIOS_BORRADOR
                WHERE UPPER(TipoLista) <> 'M' AND LTRIM(RTRIM(ISNULL(Usuario, ''))) <> '' ORDER BY 1;
                """, transaction: tx, cancellationToken: ct))).ToList();
            pagina.Filas = filas
                .Skip((pagina.Pagina - 1) * pagina.TamanoPagina).Take(pagina.TamanoPagina)
                .Select(f => f.ADto(pagina.ClasePrecioVenta)).ToList();
            return pagina;
        }

    public Task<PrecioBorradorResultado> AplicarAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
        => EjecutarAsync("Aplicar", async cn =>
        {
            var lista = ValidarIds(ids);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var resultado = await AplicarCoreAsync(cn, tx, lista, Usuario(), ct);
            await tx.CommitAsync(ct);
            await events.LogAuditAsync("PreciosBorrador", "Aplicar", "V_MA_PRECIOS_BORRADOR", string.Empty,
                $"Borrador de precios: se aplicaron {resultado.Aplicadas} fila(s); {resultado.ProductosRecalculados} producto(s) recalculados por fórmula.",
                new { Ids = lista.Take(200), resultado.Aplicadas, resultado.ProductosRecalculados }, CancellationToken.None);
            return resultado;
        }, "No se pudo aplicar el borrador de precios.", ct);

    /// <summary>Aplica las filas dentro de la transacción del llamador (no hace commit).</summary>
    internal static async Task<PrecioBorradorResultado> AplicarCoreAsync(SqlConnection cn, SqlTransaction tx, List<int> lista, string usuario, CancellationToken ct)
        {
            var resultado = new PrecioBorradorResultado();
            var colsBorrador = await ColumnasAsync(cn, tx, "V_MA_PRECIOS_BORRADOR", ct);
            var colsMaestro = await ColumnasAsync(cn, tx, "V_MA_ARTICULOS", ct);
            var colsLista = await ColumnasAsync(cn, tx, "V_MA_PRECIOS", ct);
            var setMaestro = Sets("a", MaestroPrecios, MaestroOtros, colsBorrador, colsMaestro);
            var setLista = Sets("p", ListaPrecios, ListaOtros, colsBorrador, colsLista);

            var filas = (await cn.QueryAsync<(int Id, string IdArticulo, string Tipo, string IdLista, string Nombre)>(new CommandDefinition("""
                SELECT ID, IdArticulo, UPPER(LTRIM(RTRIM(TipoLista))), LTRIM(RTRIM(ISNULL(IdLista, ''))), LTRIM(RTRIM(ISNULL(Nombre, '')))
                FROM dbo.V_MA_PRECIOS_BORRADOR WITH (UPDLOCK) WHERE ID IN @Ids;
                """, new { Ids = lista }, tx, cancellationToken: ct))).ToList();
            if (filas.Count < lista.Count)
                resultado.Observaciones.Add($"{lista.Count - filas.Count} fila(s) ya no estaban en el borrador (las aplicó o descartó otro usuario).");

            var maestrosAplicados = new List<string>();
            // Primero el maestro y después las listas, como el legacy.
            foreach (var f in filas.OrderBy(f => f.Tipo == "B" ? 0 : 1))
            {
                int afectadas;
                switch (f.Tipo)
                {
                    case "B":
                        // DesdeTrigger = 1: se aplica tal cual quedó en el borrador (el trigger no vuelve a calcular la política).
                        afectadas = await cn.ExecuteAsync(new CommandDefinition($"""
                            UPDATE a SET {setMaestro}, a.Usuario = @Usuario, a.DesdeTrigger = 1
                            FROM dbo.V_MA_ARTICULOS a INNER JOIN dbo.V_MA_PRECIOS_BORRADOR b ON b.ID = @Id
                            WHERE a.IDARTICULO = b.IdArticulo;
                            """, new { f.Id, Usuario = usuario }, tx, cancellationToken: ct));
                        if (afectadas > 0) maestrosAplicados.Add(f.IdArticulo.Trim());
                        break;
                    case "V":
                        // DesdeTrigger = 0 como el legacy: FMT_V_MA_PRECIOS aplica la política y el redondeo de la lista.
                        afectadas = await cn.ExecuteAsync(new CommandDefinition($"""
                            UPDATE p SET {setLista}, p.Usuario = @Usuario, p.DesdeTrigger = 0
                            FROM dbo.V_MA_PRECIOS p INNER JOIN dbo.V_MA_PRECIOS_BORRADOR b ON b.ID = @Id
                            WHERE p.IDARTICULO = b.IdArticulo AND LTRIM(RTRIM(p.IdLista)) = LTRIM(RTRIM(b.IdLista)) AND p.TIPOLISTA = 'V';
                            """, new { f.Id, Usuario = usuario }, tx, cancellationToken: ct));
                        break;
                    case "G":
                        afectadas = await cn.ExecuteAsync(new CommandDefinition($"""
                            UPDATE p SET {setLista}, p.Usuario = @Usuario, p.DesdeTrigger = 0
                            FROM dbo.V_MA_PRECIOS p INNER JOIN dbo.V_MA_PRECIOS_BORRADOR b ON b.ID = @Id
                            INNER JOIN dbo.V_MA_PRECIOSCAB c ON LTRIM(RTRIM(c.IDLISTA)) = LTRIM(RTRIM(p.IdLista)) AND c.TIPOLISTA = 'V'
                            WHERE p.IDARTICULO = b.IdArticulo AND p.TIPOLISTA = 'V' AND LTRIM(RTRIM(c.GRUPO)) = LTRIM(RTRIM(b.Nombre));
                            """, new { f.Id, Usuario = usuario }, tx, cancellationToken: ct));
                        break;
                    default:
                        continue;
                }
                if (afectadas == 0)
                {
                    resultado.Observaciones.Add($"{f.IdArticulo.Trim()} ({(f.Tipo == "B" ? "maestro" : f.Nombre)}): el artículo ya no está ahí; la fila queda en el borrador.");
                    continue;
                }
                await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.V_MA_PRECIOS_BORRADOR WHERE ID = @Id;", new { f.Id }, tx, cancellationToken: ct));
                resultado.Aplicadas++;
            }

            // Productos que usan esos precios del maestro como insumo (fórmulas con ClasePr).
            if (maestrosAplicados.Count > 0)
            {
                var cascada = await ArticulosService.PropagarCostosAsync(cn, tx, maestrosAplicados, usuario, ct);
                resultado.ProductosRecalculados = cascada.Cambios.Count;
                resultado.Observaciones.AddRange(cascada.Observaciones);
            }
            return resultado;
        }

    public Task<PrecioBorradorResultado> DescartarAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
        => EjecutarAsync("Descartar", async cn =>
        {
            var lista = ValidarIds(ids);
            var borradas = 0;
            foreach (var lote in lista.Chunk(1000))
                borradas += await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.V_MA_PRECIOS_BORRADOR WHERE ID IN @Ids AND UPPER(TipoLista) <> 'M';", new { Ids = lote }, cancellationToken: ct));
            await events.LogAuditAsync("PreciosBorrador", "Descartar", "V_MA_PRECIOS_BORRADOR", string.Empty,
                $"Borrador de precios: se descartaron {borradas} fila(s).", new { Ids = lista.Take(200), borradas }, CancellationToken.None);
            return new PrecioBorradorResultado { Descartadas = borradas };
        }, "No se pudo descartar el borrador de precios.", ct);

    /// <summary>SET dinámico: solo columnas que existen en el borrador y en el destino. Precios/costo: 0 o vacío = no cambia.</summary>
    private static string Sets(string alias, string[] precios, string[] otros, HashSet<string> borrador, HashSet<string> destino)
        => string.Join(", ",
            precios.Where(c => borrador.Contains(c) && destino.Contains(c))
                .Select(c => $"{alias}.[{c}] = COALESCE(NULLIF(b.[{c}], 0), {alias}.[{c}])")
            .Concat(otros.Where(c => borrador.Contains(c) && destino.Contains(c))
                .Select(c => $"{alias}.[{c}] = COALESCE(b.[{c}], {alias}.[{c}])")));

    private static async Task<HashSet<string>> ColumnasAsync(SqlConnection cn, SqlTransaction? tx, string tabla, CancellationToken ct)
        => (await cn.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + @Tabla) AND is_computed = 0;",
            new { Tabla = tabla }, tx, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<int> ValidarIds(IReadOnlyCollection<int>? ids)
    {
        var lista = (ids ?? []).Where(i => i > 0).Distinct().ToList();
        if (lista.Count == 0) throw new ReglaException("Elegí al menos una fila del borrador.");
        if (lista.Count > MaxFilas) throw new ReglaException($"Se pueden procesar hasta {MaxFilas} filas por vez.");
        return lista;
    }

    private string Usuario()
    {
        var u = users.GetCurrentUserName().Trim();
        return u.Length == 0 ? "AlfaCore" : u.Length > 50 ? u[..50] : u;
    }

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, string mensaje, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var sesion)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            if (!await permissions.HasAccessAsync(MenuKey, ct))
                throw new ReglaException("Tu usuario no tiene permiso para el borrador de precios.");
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            var r = await operacion(cn);
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != sesion)
                throw new ReglaException("Cambió la sesión. Volvé a abrir el borrador de precios en la base activa.");
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (InvalidOperationException) { throw; } // incluye AppUserFacingException y las reglas de la cascada
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("PreciosBorrador", accion, ex, mensaje, ct: CancellationToken.None);
            throw new AppUserFacingException(mensaje, codigo, ex);
        }
    }

    private sealed class FilaPlana
    {
        public int Id { get; set; }
        public string IdArticulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string IdLista { get; set; } = string.Empty;
        public string Lista { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public bool SinDestino { get; set; }
        public decimal? N0 { get; set; } public decimal? N1 { get; set; } public decimal? N2 { get; set; } public decimal? N3 { get; set; }
        public decimal? N4 { get; set; } public decimal? N5 { get; set; } public decimal? N6 { get; set; } public decimal? N7 { get; set; }
        public decimal? N8 { get; set; } public decimal? N9 { get; set; } public decimal? N10 { get; set; }
        public decimal? A0 { get; set; } public decimal? A1 { get; set; } public decimal? A2 { get; set; } public decimal? A3 { get; set; }
        public decimal? A4 { get; set; } public decimal? A5 { get; set; } public decimal? A6 { get; set; } public decimal? A7 { get; set; }
        public decimal? A8 { get; set; } public decimal? A9 { get; set; } public decimal? A10 { get; set; }

        public PrecioBorradorFilaDto ADto(int claseVenta)
        {
            decimal?[] nuevos = [N0, N1, N2, N3, N4, N5, N6, N7, N8, N9, N10];
            decimal?[] actuales = [A0, A1, A2, A3, A4, A5, A6, A7, A8, A9, A10];
            decimal Actual(int i) => actuales[i] ?? 0m;
            decimal Nuevo(int i) => PrecioBorradorCalculo.ValorAplicado(nuevos[i], Actual(i));
            var dto = new PrecioBorradorFilaDto
            {
                Id = Id, IdArticulo = IdArticulo, Descripcion = Descripcion, Tipo = Tipo, IdLista = IdLista,
                Lista = Tipo == "B" ? "Maestro" : Tipo == "G" ? $"Grupo {Lista}" : $"{IdLista} · {Lista}",
                Usuario = Usuario, SinDestino = SinDestino,
                CostoActual = Actual(0), CostoNuevo = Nuevo(0),
                PrecioActual = Actual(claseVenta), PrecioNuevo = Nuevo(claseVenta)
            };
            var ultima = Tipo == "B" ? 8 : 10;
            for (var i = 1; i <= ultima; i++)
                if (i != claseVenta && Math.Round(Nuevo(i), 2) != Math.Round(Actual(i), 2))
                    dto.OtrosCambios.Add($"P{i} {Actual(i):#,##0.##} → {Nuevo(i):#,##0.##}");
            return dto;
        }
    }
}

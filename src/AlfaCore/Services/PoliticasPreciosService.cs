using System.Globalization;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IPoliticasPreciosService
{
    Task<PoliticaPrecioContextoDto> GetContextoAsync(CancellationToken ct = default);
    Task<PoliticaPrecioDto> GuardarAsync(PoliticaPrecioDto politica, CancellationToken ct = default);
    Task BorrarAsync(string codigo, CancellationToken ct = default);
    /// <summary>Cómo quedarían los artículos con la política (la que se está editando, sin grabar): la aplica la base en una
    /// transacción que se revierte. Sin artículo: hasta 15 de los que usan la política (o los primeros con costo).</summary>
    Task<IReadOnlyList<PoliticaPrecioPreviewDto>> PrevisualizarAsync(PoliticaPrecioDto politica, string? idArticulo, CancellationToken ct = default);
    /// <summary>Recalcula los artículos y filas de listas que tienen la política (botón "Aplicar política" del legacy).</summary>
    Task<PoliticaPrecioAplicarResultado> AplicarAsync(string codigo, CancellationToken ct = default);
}

/// <summary>
/// Políticas de precios (V_TA_POLITICAPRECIOS) como Tablas.frm + frmPoliticaFormulas.frm del legacy. El cálculo lo hacen los
/// triggers FMT_V_MA_ARTICULOS y FMT_V_MA_PRECIOS de cada base (con DesdeTrigger=0): AlfaCore no lo reimplementa.
/// </summary>
public sealed class PoliticasPreciosService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events) : IPoliticasPreciosService
{
    private const int TimeoutMasivo = 600;
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    public Task<PoliticaPrecioContextoDto> GetContextoAsync(CancellationToken ct = default)
        => EjecutarAsync("Contexto", escribe: false, async cn =>
        {
            var cfg = await CargaComprobantesConfigPrecios.LeerAsync(cn, ct);
            var claves = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
                SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(CONVERT(nvarchar(50), VALOR))) FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('MAESTROARTICULOCONIVA','MAESTROARTICULOCOSTOCONIVA','PIVA');
                """, cancellationToken: ct))).GroupBy(x => x.Clave).ToDictionary(x => x.Key, x => x.First().Valor ?? "");
            static bool Si(string? v) => v?.Trim().ToUpperInvariant() is "SI" or "S" or "1";
            return new PoliticaPrecioContextoDto
            {
                Politicas = await LeerAsync(cn, null, null, ct),
                NombresClases = Enumerable.Range(1, 8).Select(cfg.NombreClase).ToList(),
                PuedeEditar = await permissions.HasAccessAsync(ListasPreciosService.MenuKey, ct),
                PorcentajeSumaIva = Si(claves.GetValueOrDefault("MAESTROARTICULOCONIVA")) && !Si(claves.GetValueOrDefault("MAESTROARTICULOCOSTOCONIVA")),
                TasaIvaDefault = decimal.TryParse(claves.GetValueOrDefault("PIVA"), NumberStyles.Number, CultureInfo.InvariantCulture, out var t) && t > 0 ? t : 21
            };
        }, ct);

    public Task<PoliticaPrecioDto> GuardarAsync(PoliticaPrecioDto politica, CancellationToken ct = default)
        => EjecutarAsync("Guardar", escribe: true, async cn =>
        {
            var p = Normalizar(politica);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            await GrabarAsync(cn, tx, p, ct);
            await tx.CommitAsync(ct);
            await events.LogAuditAsync("PoliticasPrecios", p.CodigoOriginal.Length == 0 ? "Alta" : "Modificacion", "V_TA_POLITICAPRECIOS", p.Codigo.Trim(),
                $"Política de precios {p.Codigo.Trim()} {p.Descripcion}: {(p.UsaFormulas ? "fórmulas" : "porcentajes")}", p, CancellationToken.None);
            return (await LeerAsync(cn, null, p.Codigo, ct)).First();
        }, ct);

    public Task BorrarAsync(string codigo, CancellationToken ct = default)
        => EjecutarAsync("Borrar", escribe: true, async cn =>
        {
            var p = (await LeerAsync(cn, null, codigo, ct)).FirstOrDefault() ?? throw new ReglaException($"La política {codigo.Trim()} no existe.");
            // El legacy deja borrarla aunque esté en uso y los artículos quedan apuntando a una política inexistente.
            if (p.Articulos > 0 || p.FilasListas > 0)
                throw new ReglaException($"La política {p.Codigo.Trim()} la usan {p.Articulos} artículo(s) y {p.FilasListas} fila(s) de listas de precios. Sacásela antes de borrarla.");
            await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.V_TA_POLITICAPRECIOS WHERE CODIGO = @Codigo;", new { Codigo = p.Codigo }, cancellationToken: ct));
            await events.LogAuditAsync("PoliticasPrecios", "Baja", "V_TA_POLITICAPRECIOS", p.Codigo.Trim(), $"Baja de la política de precios {p.Codigo.Trim()} {p.Descripcion}", null, CancellationToken.None);
            return true;
        }, ct);

    public Task<IReadOnlyList<PoliticaPrecioPreviewDto>> PrevisualizarAsync(PoliticaPrecioDto politica, string? idArticulo, CancellationToken ct = default)
        => EjecutarAsync<IReadOnlyList<PoliticaPrecioPreviewDto>>("Previsualizar", escribe: false, async cn =>
        {
            var p = Normalizar(politica, validarCodigo: false);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            try
            {
                // La política tal como está en pantalla, con un código propio de la transacción (nunca se confirma).
                var original = p.CodigoOriginal;
                p.CodigoOriginal = string.Empty;
                p.Codigo = await CodigoTemporalAsync(cn, tx, ct);
                await GrabarAsync(cn, tx, p, ct);
                var articulos = (idArticulo ?? "").Trim().Length > 0
                    ? (await cn.QueryAsync<string>(new CommandDefinition(
                        "SELECT IDARTICULO FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO)) = @Id;", new { Id = idArticulo!.Trim() }, tx, cancellationToken: ct))).ToList()
                    : (await cn.QueryAsync<string>(new CommandDefinition("""
                        SELECT TOP (15) IDARTICULO FROM dbo.V_MA_ARTICULOS
                        WHERE ISNULL(SUSPENDIDO,0) = 0 AND ((@Original <> '' AND POLITICAPRECIOS = @Original) OR (@Original = '' AND ISNULL(COSTO,0) > 0))
                        ORDER BY DESCRIPCION;
                        """, new { Original = original }, tx, cancellationToken: ct))).ToList();
                if (articulos.Count == 0 && original.Length > 0)
                    articulos = (await cn.QueryAsync<string>(new CommandDefinition(
                        "SELECT TOP (15) IDARTICULO FROM dbo.V_MA_ARTICULOS WHERE ISNULL(SUSPENDIDO,0) = 0 AND ISNULL(COSTO,0) > 0 ORDER BY DESCRIPCION;",
                        transaction: tx, cancellationToken: ct))).ToList();
                if (articulos.Count == 0) throw new ReglaException(idArticulo is { Length: > 0 } ? $"El artículo {idArticulo.Trim()} no existe." : "No hay artículos con costo para mostrar el ejemplo.");
                var antes = await LeerPreciosAsync(cn, tx, articulos, ct);
                var resultado = new List<PoliticaPrecioPreviewDto>();
                foreach (var id in articulos)
                {
                    var fila = antes[id];
                    try
                    {
                        // Un artículo por UPDATE, como el legacy; todo se revierte al final.
                        await cn.ExecuteAsync(new CommandDefinition(
                            "UPDATE dbo.V_MA_ARTICULOS SET POLITICAPRECIOS = @Codigo, DesdeTrigger = 0 WHERE IDARTICULO = @Id;",
                            new { p.Codigo, Id = id }, tx, cancellationToken: ct));
                        var despues = (await LeerPreciosAsync(cn, tx, [id], ct))[id];
                        fila.CostoNuevo = despues.CostoActual;
                        fila.PreciosNuevos = despues.PreciosActuales;
                    }
                    catch (SqlException ex)
                    {
                        // Un error dentro de un trigger anula la transacción: se informa y no se sigue con los demás.
                        fila.Error = CargaComprobantesErroresSql.Explicar(ex);
                        fila.CostoNuevo = fila.CostoActual; fila.PreciosNuevos = fila.PreciosActuales;
                        resultado.Add(fila);
                        break;
                    }
                    resultado.Add(fila);
                }
                return resultado;
            }
            finally
            {
                try { if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None); }
                catch (InvalidOperationException) { } // la base ya la revirtió
                catch (SqlException) { }
            }
        }, ct);

    public Task<PoliticaPrecioAplicarResultado> AplicarAsync(string codigo, CancellationToken ct = default)
        => EjecutarAsync("Aplicar", escribe: true, async cn =>
        {
            var p = (await LeerAsync(cn, null, codigo, ct)).FirstOrDefault() ?? throw new ReglaException($"La política {codigo.Trim()} no existe.");
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var r = await AplicarCoreAsync(cn, tx, p.Codigo, Usuario(), ct);
            await tx.CommitAsync(ct);
            await events.LogAuditAsync("PoliticasPrecios", "Aplicar", "V_TA_POLITICAPRECIOS", p.Codigo.Trim(),
                $"Política {p.Codigo.Trim()} aplicada: {r.Articulos} artículo(s), {r.FilasListas} fila(s) de listas", null, CancellationToken.None);
            return r;
        }, ct);

    /// <summary>
    /// Como "Aplicar Politica de Precios" del legacy: marca DesdeTrigger=0 en los artículos y filas de listas con la política
    /// para que FMT_V_MA_ARTICULOS / FMT_V_MA_PRECIOS los recalculen. Solo toca las filas de listas que ya tienen la política
    /// (el legacy además se la asignaba a todas las listas del artículo, pisando precios de venta propios de cada lista).
    /// </summary>
    internal static async Task<PoliticaPrecioAplicarResultado> AplicarCoreAsync(SqlConnection cn, SqlTransaction tx, string codigo, string usuario, CancellationToken ct)
    {
        var ids = (await cn.QueryAsync<string>(new CommandDefinition(
            "SELECT IDARTICULO FROM dbo.V_MA_ARTICULOS WHERE POLITICAPRECIOS = @Codigo;", new { Codigo = codigo }, tx, cancellationToken: ct))).ToList();
        // De a un artículo, como el legacy: los triggers recorren su propio cursor y TR_BlockMassUpdate_Precios no ve un UPDATE masivo.
        foreach (var id in ids)
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.V_MA_ARTICULOS SET POLITICAPRECIOS = @Codigo, DesdeTrigger = 0 WHERE IDARTICULO = @Id;",
                new { Codigo = codigo, Id = id }, tx, commandTimeout: 60, cancellationToken: ct));
        var listas = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SET NOCOUNT ON;
            UPDATE dbo.V_MA_PRECIOS SET DesdeTrigger = 0, USUARIO = @Usuario WHERE POLITICAPRECIOS = @Codigo;
            SELECT @@ROWCOUNT;
            """, new { Codigo = codigo, Usuario = usuario }, tx, commandTimeout: TimeoutMasivo, cancellationToken: ct));
        return new PoliticaPrecioAplicarResultado
        {
            Articulos = ids.Count, FilasListas = listas,
            Mensaje = ids.Count + listas == 0 ? "Ningún artículo ni lista usa esta política todavía."
                : $"Se recalcularon {ids.Count} artículo(s) y {listas} fila(s) de listas de precios."
        };
    }

    // ---------------- Lectura y grabación ----------------

    internal static async Task<List<PoliticaPrecioDto>> LeerAsync(SqlConnection cn, SqlTransaction? tx, string? codigo, CancellationToken ct)
    {
        var filas = await cn.QueryAsync(new CommandDefinition("""
            SELECT p.CODIGO, ISNULL(p.DESCRIPCION,'') DESCRIPCION,
                CONVERT(decimal(19,4),ISNULL(p.CLASE1,0)) C1, CONVERT(decimal(19,4),ISNULL(p.CLASE2,0)) C2, CONVERT(decimal(19,4),ISNULL(p.CLASE3,0)) C3,
                CONVERT(decimal(19,4),ISNULL(p.CLASE4,0)) C4, CONVERT(decimal(19,4),ISNULL(p.CLASE5,0)) C5, CONVERT(decimal(19,4),ISNULL(p.CLASE6,0)) C6,
                CONVERT(decimal(19,4),ISNULL(p.CLASE7,0)) C7, CONVERT(decimal(19,4),ISNULL(p.CLASE8,0)) C8,
                p.FCOSTO, p.FCLASE1, p.FCLASE2, p.FCLASE3, p.FCLASE4, p.FCLASE5, p.FCLASE6, p.FCLASE7, p.FCLASE8,
                (SELECT COUNT(*) FROM dbo.V_MA_ARTICULOS a WHERE a.POLITICAPRECIOS = p.CODIGO) Articulos,
                (SELECT COUNT(*) FROM dbo.V_MA_PRECIOS l WHERE l.POLITICAPRECIOS = p.CODIGO) FilasListas
            FROM dbo.V_TA_POLITICAPRECIOS p
            WHERE @Codigo IS NULL OR LTRIM(RTRIM(p.CODIGO)) = LTRIM(RTRIM(@Codigo))
            ORDER BY p.CODIGO;
            """, new { Codigo = codigo }, tx, commandTimeout: 60, cancellationToken: ct));
        return filas.Select(f =>
        {
            var d = (IDictionary<string, object?>)f;
            string? S(string k) => d[k] as string;
            return new PoliticaPrecioDto
            {
                CodigoOriginal = (string)d["CODIGO"]!, Codigo = (string)d["CODIGO"]!, Descripcion = ((string?)d["DESCRIPCION"] ?? "").Trim(),
                Porcentajes = Enumerable.Range(1, 8).Select(c => (decimal)d[$"C{c}"]!).ToArray(),
                FormulaCosto = string.IsNullOrWhiteSpace(S("FCOSTO")) ? PoliticasPreciosFormula.Identidad(0) : S("FCOSTO")!.Trim(),
                Formulas = Enumerable.Range(1, 8).Select(c => string.IsNullOrWhiteSpace(S($"FCLASE{c}")) ? PoliticasPreciosFormula.Identidad(c) : S($"FCLASE{c}")!.Trim()).ToArray(),
                Articulos = (int)d["Articulos"]!, FilasListas = (int)d["FilasListas"]!
            };
        }).ToList();
    }

    private static PoliticaPrecioDto Normalizar(PoliticaPrecioDto p, bool validarCodigo = true)
    {
        var descripcion = (p.Descripcion ?? "").Trim();
        if (descripcion.Length == 0) throw new ReglaException("Ingresá la descripción de la política.");
        if (descripcion.Length > 50) throw new ReglaException("La descripción admite hasta 50 caracteres.");
        var crudo = (p.Codigo ?? "").Trim();
        if (validarCodigo && crudo.Length == 0) throw new ReglaException("Ingresá el código de la política.");
        if (crudo.Length > 4) throw new ReglaException("El código de la política admite hasta 4 caracteres.");
        var porcentajes = (p.Porcentajes ?? []).Concat(Enumerable.Repeat(0m, 8)).Take(8).Select(x => decimal.Round(x, 4)).ToArray();
        if (porcentajes.Any(x => x <= -100)) throw new ReglaException("Los porcentajes tienen que ser mayores a -100.");
        var formulas = (p.Formulas ?? []).Concat(Enumerable.Repeat("", 8)).Take(8).ToArray();
        var r = new PoliticaPrecioDto
        {
            CodigoOriginal = (p.CodigoOriginal ?? "").Trim().Length == 0 ? "" : p.CodigoOriginal,
            Codigo = crudo.Length == 0 ? "" : CodigoPk.Format(crudo, 4), Descripcion = descripcion, Porcentajes = porcentajes,
            // Vacía = deja el valor como está (la base devolvería 0 y pondría el precio en 0).
            FormulaCosto = Formula(p.FormulaCosto, 0),
            Formulas = Enumerable.Range(1, 8).Select(c => Formula(formulas[c - 1], c)).ToArray()
        };
        // Con porcentajes la base ignora las fórmulas, pero se validan igual para no guardar algo que falle al pasar a fórmulas.
        var nombres = new[] { "del costo" }.Concat(Enumerable.Range(1, 8).Select(c => $"de la clase {c}")).ToArray();
        var todas = new[] { r.FormulaCosto }.Concat(r.Formulas).ToArray();
        for (var i = 0; i < todas.Length; i++)
            if (PoliticasPreciosFormula.Validar(todas[i]) is { } error)
                throw new ReglaException($"Fórmula {nombres[i]}: {error}");
        return r;
    }

    private static string Formula(string? f, int clase)
    {
        var n = PoliticasPreciosFormula.Normalizar(f);
        return n.Length == 0 ? PoliticasPreciosFormula.Identidad(clase) : n;
    }

    private static async Task GrabarAsync(SqlConnection cn, SqlTransaction tx, PoliticaPrecioDto p, CancellationToken ct)
    {
        var parametros = new DynamicParameters();
        parametros.Add("Codigo", p.Codigo);
        parametros.Add("Original", p.CodigoOriginal);
        parametros.Add("Descripcion", p.Descripcion);
        for (var c = 1; c <= 8; c++)
        {
            parametros.Add($"C{c}", p.Porcentajes[c - 1]);
            parametros.Add($"F{c}", p.Formulas[c - 1]);
        }
        parametros.Add("F0", p.FormulaCosto);
        const string valores = "CLASE1=@C1, CLASE2=@C2, CLASE3=@C3, CLASE4=@C4, CLASE5=@C5, CLASE6=@C6, CLASE7=@C7, CLASE8=@C8, " +
            "FCOSTO=@F0, FCLASE1=@F1, FCLASE2=@F2, FCLASE3=@F3, FCLASE4=@F4, FCLASE5=@F5, FCLASE6=@F6, FCLASE7=@F7, FCLASE8=@F8";
        if (p.CodigoOriginal.Length == 0)
        {
            var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.V_TA_POLITICAPRECIOS WITH (UPDLOCK, HOLDLOCK) WHERE LTRIM(RTRIM(CODIGO)) = LTRIM(RTRIM(@Codigo));",
                parametros, tx, cancellationToken: ct));
            if (existe > 0) throw new ReglaException($"Ya existe la política {p.Codigo.Trim()}.");
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.V_TA_POLITICAPRECIOS (CODIGO, DESCRIPCION, CLASE1, CLASE2, CLASE3, CLASE4, CLASE5, CLASE6, CLASE7, CLASE8,
                    FCOSTO, FCLASE1, FCLASE2, FCLASE3, FCLASE4, FCLASE5, FCLASE6, FCLASE7, FCLASE8)
                VALUES (@Codigo, @Descripcion, @C1, @C2, @C3, @C4, @C5, @C6, @C7, @C8, @F0, @F1, @F2, @F3, @F4, @F5, @F6, @F7, @F8);
                """, parametros, tx, cancellationToken: ct));
        }
        else
        {
            var n = await cn.ExecuteAsync(new CommandDefinition(
                $"UPDATE dbo.V_TA_POLITICAPRECIOS SET DESCRIPCION=@Descripcion, {valores} WHERE LTRIM(RTRIM(CODIGO)) = LTRIM(RTRIM(@Original));",
                parametros, tx, cancellationToken: ct));
            if (n == 0) throw new ReglaException($"La política {p.CodigoOriginal.Trim()} ya no existe.");
        }
    }

    private static async Task<string> CodigoTemporalAsync(SqlConnection cn, SqlTransaction tx, CancellationToken ct)
    {
        for (var i = 9999; i > 9000; i--)
        {
            var c = CodigoPk.Format(i.ToString(CultureInfo.InvariantCulture), 4);
            if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.V_TA_POLITICAPRECIOS WHERE CODIGO = @C;",
                new { C = c }, tx, cancellationToken: ct)) == 0)
                return c;
        }
        throw new ReglaException("No hay un código libre para la vista previa.");
    }

    private static async Task<Dictionary<string, PoliticaPrecioPreviewDto>> LeerPreciosAsync(SqlConnection cn, SqlTransaction tx, IReadOnlyList<string> ids, CancellationToken ct)
        => (await cn.QueryAsync(new CommandDefinition("""
            SELECT IDARTICULO, ISNULL(DESCRIPCION,'') DESCRIPCION, CONVERT(decimal(19,4),ISNULL(COSTO,0)) COSTO,
                CONVERT(decimal(19,4),ISNULL(PRECIO1,0)) P1, CONVERT(decimal(19,4),ISNULL(PRECIO2,0)) P2, CONVERT(decimal(19,4),ISNULL(PRECIO3,0)) P3,
                CONVERT(decimal(19,4),ISNULL(PRECIO4,0)) P4, CONVERT(decimal(19,4),ISNULL(PRECIO5,0)) P5, CONVERT(decimal(19,4),ISNULL(PRECIO6,0)) P6,
                CONVERT(decimal(19,4),ISNULL(PRECIO7,0)) P7, CONVERT(decimal(19,4),ISNULL(PRECIO8,0)) P8
            FROM dbo.V_MA_ARTICULOS WHERE IDARTICULO IN @Ids;
            """, new { Ids = ids.ToArray() }, tx, cancellationToken: ct)))
            .Select(f => (IDictionary<string, object?>)f)
            .ToDictionary(d => (string)d["IDARTICULO"]!, d => new PoliticaPrecioPreviewDto
            {
                IdArticulo = ((string)d["IDARTICULO"]!).Trim(), Descripcion = ((string)d["DESCRIPCION"]!).Trim(), CostoActual = (decimal)d["COSTO"]!,
                PreciosActuales = Enumerable.Range(1, 8).Select(c => (decimal)d[$"P{c}"]!).ToArray()
            });

    private string Usuario()
    {
        var u = users.GetCurrentUserName().Trim();
        if (u.Length == 0) u = "AlfaCore";
        return u.Length > 50 ? u[..50] : u;
    }

    private async Task<T> EjecutarAsync<T>(string accion, bool escribe, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var sesion)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            // Mismo permiso que las listas de precios: cambian precios de venta.
            if (escribe && !await permissions.HasAccessAsync(ListasPreciosService.MenuKey, ct))
                throw new ReglaException("Tu usuario no tiene permiso para modificar políticas de precios (Listas de Precios de ventas).");
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            var r = await operacion(cn);
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != sesion)
                throw new ReglaException("Cambió la sesión. Volvé a abrir las políticas de precios en la base activa.");
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (SqlException ex)
        {
            var mensaje = CargaComprobantesErroresSql.Explicar(ex);
            var codigo = await events.LogErrorAsync("PoliticasPrecios", accion, ex, mensaje, ct: CancellationToken.None);
            throw new AppUserFacingException(mensaje, codigo, ex);
        }
        catch (Exception ex)
        {
            const string mensaje = "No se pudo completar la operación con las políticas de precios.";
            var codigo = await events.LogErrorAsync("PoliticasPrecios", accion, ex, mensaje, ct: CancellationToken.None);
            throw new AppUserFacingException(mensaje, codigo, ex);
        }
    }
}

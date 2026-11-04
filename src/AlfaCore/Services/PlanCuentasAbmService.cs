using System.Data;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IPlanCuentasAbmService
{
    Task<PlanCuentasAbmCatalogo> CatalogoAsync(CancellationToken ct = default);
    /// <summary>Lee la cuenta, o arma una nueva con ese código (Existe = false) si no está en el plan.</summary>
    Task<PlanCuentaEdicion> PrepararAsync(string codigo, CancellationToken ct = default);
    /// <summary>Primer código libre debajo de la cuenta madre (IdentificaCuenta del legacy).</summary>
    Task<string> SiguienteCodigoAsync(string madre, CancellationToken ct = default);
    Task<PlanCuentaEdicion> GuardarAsync(PlanCuentaEdicion cuenta, bool alta, CancellationToken ct = default);
    Task<PlanCuentaBajaInfo> ConsultarBajaAsync(string codigo, CancellationToken ct = default);
    Task<PlanCuentaBajaResultado> EliminarAsync(string codigo, bool confirmaCashFlow, CancellationToken ct = default);
}

/// <summary>
/// Alta, baja y modificación del plan de cuentas (MA_CUENTAS), como «Actualización Plan General de Cuentas» del legacy
/// (opción D9510, MA_CUENTAS.vbp / Cn020.frm), con sus mismas validaciones. Se usa desde la consulta del plan de cuentas.
/// No replica a puntos de venta ni a empresas del grupo (ActualizaGEmpresas del legacy) y no abre los datos adicionales.
/// </summary>
public sealed class PlanCuentasAbmService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions, IAppEventService events)
    : IPlanCuentasAbmService
{
    public const string MenuAbm = "D9510";
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    private static readonly string[] ClavesCatalogo =
    [
        .. PlanCuentasReglas.ClavesEstructura, "USAMULTILESMONEDAS", "MONEDAVENTAS", "NOMBRE_INDICE1", "NOMBRE_INDICE2", "NOMBRE_INDICE3", "NOMBRE_INDICE4",
        "USA BIENES DE USO", "USACCOSTO", "CTASOLICITUDCREDITO", "TITULOCLIENTES", "TITULOPROVEEDORES", "TITULOBANCOS", "TITULOBANCOSDIFERIDO",
        "TITULOPERSONAL", "TITULOCLIENTESBONOS", "TITULOCLIENTESMUTUALES"
    ];

    // ── Catálogo ────────────────────────────────────────────────────────────────────────────────

    public Task<PlanCuentasAbmCatalogo> CatalogoAsync(CancellationToken ct = default) => EjecutarAsync("Catalogo", false, async cn =>
    {
        var cfg = await ConfiguracionSql.LeerAsync(cn, null, ClavesCatalogo, ct);
        var e = PlanCuentasReglas.EstructuraDesde(cfg);
        var multi = ConfiguracionSql.EsSi(cfg, "USAMULTILESMONEDAS");
        var monedas = await MonedasAsync(cn, null, ct);
        return new PlanCuentasAbmCatalogo
        {
            Estructura = e,
            Titulos = (await cn.QueryAsync<PlanCuentaOpcion>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(CODIGO)) Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion FROM dbo.MA_CUENTAS
                WHERE ISNULL(TITULO,0) = 1 AND ISNULL(Dada_De_Baja,0) = 0
                  AND (@ConTerceros = 1 OR ISNULL(TipoVista,'') NOT IN ('CL','PR'))
                ORDER BY CODIGO;
                """, new { ConTerceros = e.MostrarClientesProveedores }, cancellationToken: ct))).ToList(),
            Monedas = monedas,
            MonedaDefault = MonedaDefault(monedas, ConfiguracionSql.Valor(cfg, "MONEDAVENTAS")),
            MultiMoneda = multi,
            Indices = Enumerable.Range(1, 4).Select(i => ConfiguracionSql.Valor(cfg, $"NOMBRE_INDICE{i}") is { Length: > 0 } n ? n : $"Índice {i}").ToList(),
            UsaBienesDeUso = ConfiguracionSql.EsSi(cfg, "USA BIENES DE USO"),
            UsaCentrosCosto = ConfiguracionSql.EsSi(cfg, "USACCOSTO"),
            HayCentrosCosto = await HayCentrosCostoAsync(cn, null, ct),
            PuedeModificar = await permissions.HasAccessAsync(MenuAbm, ct),
            CamposPersonalizados = (await ColumnasAsync(cn, null, ct)).Where(c => c.StartsWith("P_", StringComparison.OrdinalIgnoreCase)).OrderBy(c => c).ToList()
        };
    }, ct);

    // ── Lectura ─────────────────────────────────────────────────────────────────────────────────

    public Task<PlanCuentaEdicion> PrepararAsync(string codigo, CancellationToken ct = default) => EjecutarAsync("Preparar", true, async cn =>
    {
        var cfg = await ConfiguracionSql.LeerAsync(cn, null, ClavesCatalogo, ct);
        var e = PlanCuentasReglas.EstructuraDesde(cfg);
        var c = PlanCuentasReglas.Normalizar(codigo);
        if (PlanCuentasReglas.ErrorFormato(c, e) is { } error) throw new ReglaException(error);
        return await LeerAsync(cn, null, c, e, cfg, ct) ?? await NuevaAsync(cn, c, e, cfg, ct);
    }, ct);

    public Task<string> SiguienteCodigoAsync(string madre, CancellationToken ct = default) => EjecutarAsync("SiguienteCodigo", true, async cn =>
    {
        var e = PlanCuentasReglas.EstructuraDesde(await ConfiguracionSql.LeerAsync(cn, null, PlanCuentasReglas.ClavesEstructura, ct));
        var m = PlanCuentasReglas.Normalizar(madre);
        var existentes = (await cn.QueryAsync<string>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.MA_CUENTAS WHERE CODIGO LIKE @Prefijo;", new { Prefijo = m.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%" },
            cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var siguiente = PlanCuentasReglas.SiguienteCodigo(m, e, existentes);
        if (siguiente is null) throw new ReglaException($"No hay códigos libres debajo de {m}: elegí otra cuenta madre o cargá el código a mano.");
        return siguiente;
    }, ct);

    private static async Task<PlanCuentaEdicion?> LeerAsync(SqlConnection cn, SqlTransaction? tx, string codigo, PlanCuentasEstructura e,
        IReadOnlyDictionary<string, string> cfg, CancellationToken ct)
    {
        var fila = (IDictionary<string, object?>?)await cn.QuerySingleOrDefaultAsync(new CommandDefinition(
            "SELECT * FROM dbo.MA_CUENTAS WHERE CODIGO = @Codigo;", new { Codigo = codigo }, tx, cancellationToken: ct));
        if (fila is null) return null;
        var f = new Dictionary<string, object?>(fila, StringComparer.OrdinalIgnoreCase);
        string S(string col) => f.TryGetValue(col, out var v) && v is not null and not DBNull ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!.Trim() : string.Empty;
        bool B(string col) => f.TryGetValue(col, out var v) && v is not null and not DBNull && Convert.ToBoolean(v);
        int I(string col) => f.TryGetValue(col, out var v) && v is not null and not DBNull ? Convert.ToInt32(v) : 0;

        var retencion = await ConfiguracionSql.LeerAsync(cn, tx, [PlanCuentasReglas.ClaveRetencionOtra(codigo)], ct);
        var cuenta = new PlanCuentaEdicion
        {
            Codigo = codigo,
            Descripcion = S("DESCRIPCION"),
            Titulo = B("TITULO"),
            Ajuste = B("AJUSTE"),
            Indice = Math.Clamp(I("INDICE"), 1, 4),
            Bloqueo = B("BLOQUEO"),
            Bienes = B("Bienes"),
            CajaYBanco = B("CajaYBanco"),
            MedioPago = PlanCuentasReglas.MedioPagoDesdeBase(S("MedioDePago"), codigo, ConfiguracionSql.Valor(cfg, "CTASOLICITUDCREDITO"),
                ConfiguracionSql.Valor(retencion, PlanCuentasReglas.ClaveRetencionOtra(codigo))),
            NoPideDatosTarjeta = B("NoPideDatosTarjeta"),
            CodTarjetaConcAuto = S("CodTarjeta_ConcAuto"),
            Moneda = f.TryGetValue("Moneda", out var mon) && mon is string ms ? ms : string.Empty,
            CodigoOpcional = S("CodigoOpcional"),
            CuentaPrincipal = S("CuentaPrincipal"),
            PideVencimiento = B("PideVencimiento"),
            UsaCCostos = B("usaCCostos"),
            Personalizados = f.Keys.Where(k => k.StartsWith("P_", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k)
                .Select(k => new PlanCuentaCampo { Nombre = k, Valor = S(k) }).ToList(),
            Existe = true,
            DadaDeBaja = B("Dada_De_Baja"),
            TipoVista = S("TipoVista")
        };
        await CompletarAsync(cn, tx, cuenta, e, ct);
        cuenta.TieneMovimientos = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS WHERE CUENTA = @Codigo) THEN 1 ELSE 0 END;", new { Codigo = codigo }, tx, cancellationToken: ct)) == 1;
        if (cuenta.CuentaPrincipal.Length > 0)
            cuenta.CuentaPrincipalDescripcion = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(DESCRIPCION)) FROM dbo.MA_CUENTAS WHERE CODIGO = @Codigo;", new { Codigo = cuenta.CuentaPrincipal }, tx, cancellationToken: ct)) ?? string.Empty;
        return cuenta;
    }

    private static async Task<PlanCuentaEdicion> NuevaAsync(SqlConnection cn, string codigo, PlanCuentasEstructura e, IReadOnlyDictionary<string, string> cfg, CancellationToken ct)
    {
        var cols = await ColumnasAsync(cn, null, ct);
        var cuenta = new PlanCuentaEdicion
        {
            Codigo = codigo,
            Moneda = MonedaDefault(await MonedasAsync(cn, null, ct), ConfiguracionSql.Valor(cfg, "MONEDAVENTAS")),
            Personalizados = cols.Where(c => c.StartsWith("P_", StringComparison.OrdinalIgnoreCase)).OrderBy(c => c).Select(c => new PlanCuentaCampo { Nombre = c }).ToList()
        };
        await CompletarAsync(cn, null, cuenta, e, ct);
        return cuenta;
    }

    /// <summary>Nivel, cuenta madre, dígito verificador y si tiene niveles inferiores.</summary>
    private static async Task CompletarAsync(SqlConnection cn, SqlTransaction? tx, PlanCuentaEdicion c, PlanCuentasEstructura e, CancellationToken ct)
    {
        c.Nivel = PlanCuentasReglas.NombreNivel(PlanCuentasReglas.Nivel(c.Codigo, e), e);
        c.DigitoVerificador = c.Titulo ? 0 : PlanCuentasReglas.DigitoVerificador(c.Codigo);
        c.CuentaMadre = PlanCuentasReglas.Padre(c.Codigo, e);
        if (c.CuentaMadre.Length > 0)
            c.CuentaMadreDescripcion = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(DESCRIPCION)) FROM dbo.MA_CUENTAS WHERE CODIGO = @Codigo;", new { Codigo = c.CuentaMadre }, tx, cancellationToken: ct)) ?? string.Empty;
        c.TieneNivelInferior = await ExisteNivelInferiorAsync(cn, tx, c.Codigo, ct);
    }

    // ── Alta y modificación ─────────────────────────────────────────────────────────────────────

    public Task<PlanCuentaEdicion> GuardarAsync(PlanCuentaEdicion c, bool alta, CancellationToken ct = default) => EjecutarAsync("Guardar", true, async cn =>
    {
        var usuario = users.GetCurrentUserName().Trim();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var cfg = await ConfiguracionSql.LeerAsync(cn, tx, ClavesCatalogo, ct);
        var e = PlanCuentasReglas.EstructuraDesde(cfg);
        var codigo = PlanCuentasReglas.Normalizar(c.Codigo);
        if (PlanCuentasReglas.ErrorFormato(codigo, e) is { } formato) throw new ReglaException(formato);
        var descripcion = (c.Descripcion ?? string.Empty).Trim();
        if (descripcion.Length == 0) throw new ReglaException("Debe ingresar el nombre de la cuenta.");
        if (descripcion.Length > PlanCuentasReglas.LargoMaximoDescripcion) throw new ReglaException($"El nombre de la cuenta puede tener hasta {PlanCuentasReglas.LargoMaximoDescripcion} caracteres.");
        var opcional = (c.CodigoOpcional ?? string.Empty).Replace("-", "").Trim();
        var principal = PlanCuentasReglas.Normalizar(c.CuentaPrincipal);
        var tarjeta = (c.CodTarjetaConcAuto ?? string.Empty).Trim();
        if (opcional.Length > 15 || principal.Length > 15 || tarjeta.Length > 15) throw new ReglaException("El código opcional, la cuenta relacionada y el código de conciliación tienen hasta 15 caracteres.");
        var medio = PlanCuentasReglas.MediosPago.FirstOrDefault(m => m.Clave == (c.MedioPago ?? string.Empty).Trim().ToUpperInvariant())
            ?? throw new ReglaException("Elegí un tipo de cuenta (medio de pago) válido.");

        var actual = await PrimeraAsync<(string Descripcion, bool Baja, bool LibroCompras, bool LibroVentas, string? Moneda)>(cn, new CommandDefinition("""
            SELECT LTRIM(RTRIM(ISNULL(DESCRIPCION,''))), CONVERT(bit,ISNULL(Dada_De_Baja,0)), CONVERT(bit,ISNULL(Libro_Iva_Compras,0)), CONVERT(bit,ISNULL(Libro_Iva_Ventas,0)), Moneda
            FROM dbo.MA_CUENTAS WITH (UPDLOCK, HOLDLOCK) WHERE CODIGO = @Codigo;
            """, new { Codigo = codigo }, tx, cancellationToken: ct));
        if (alta && actual is { Baja: false } ya) throw new ReglaException($"Ya existe la cuenta {codigo} ({ya.Descripcion}).");
        if (alta && actual is { Baja: true } && !c.ReutilizarBaja) throw new ReglaException($"La cuenta {codigo} está dada de baja: confirmá si querés reutilizarla.");
        if (!alta && actual is null) throw new ReglaException("La cuenta ya no existe.");
        var existe = actual is not null;

        // DatosValidos: la cuenta de un legajo se modifica desde el sistema de personal.
        if (await LegajoAsync(cn, tx, codigo, ct) is { } legajo)
            throw new ReglaException($"La cuenta está configurada para el legajo {legajo.Legajo} de la empresa {legajo.Empresa}: los cambios deben hacerse desde el sistema de personal.");

        if (principal.Length > 0)
        {
            var rel = await PrimeraAsync<(bool Baja, bool Titulo)>(cn, new CommandDefinition(
                "SELECT CONVERT(bit,ISNULL(Dada_De_Baja,0)), CONVERT(bit,ISNULL(TITULO,0)) FROM dbo.MA_CUENTAS WHERE CODIGO = @Codigo;", new { Codigo = principal }, tx, cancellationToken: ct));
            if (rel is null) throw new ReglaException("La cuenta relacionada ingresada no existe.");
            if (rel.Value.Baja) throw new ReglaException("La cuenta relacionada se encuentra dada de baja.");
            if (rel.Value.Titulo) throw new ReglaException("La cuenta relacionada debe ser una cuenta imputable.");
        }
        if (opcional.Length > 0 && await PrimeraAsync<(string Codigo, string Descripcion)>(cn, new CommandDefinition("""
                SELECT TOP 1 LTRIM(RTRIM(CODIGO)), LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) FROM dbo.MA_CUENTAS
                WHERE LTRIM(RTRIM(CodigoOpcional)) = @Opcional AND CODIGO <> @Codigo;
                """, new { Opcional = opcional, Codigo = codigo }, tx, cancellationToken: ct)) is { } repetido)
            throw new ReglaException($"El código opcional ingresado se repite para la cuenta {repetido.Codigo} {repetido.Descripcion}. Modificalo y volvé a intentarlo.");

        var padre = PlanCuentasReglas.Padre(codigo, e);
        var datosPadre = padre.Length == 0 ? null : await cn.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT CONVERT(bit,ISNULL(TITULO,0)) FROM dbo.MA_CUENTAS WHERE CODIGO = @Codigo AND ISNULL(Dada_De_Baja,0) = 0;", new { Codigo = padre }, tx, cancellationToken: ct));
        var nivelInferior = await ExisteNivelInferiorAsync(cn, tx, codigo, ct);
        if (c.Titulo)
        {
            if (PlanCuentasReglas.ErrorTitulo(codigo, e, datosPadre is not null, datosPadre == true) is { } t) throw new ReglaException(t);
            if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS WHERE CUENTA = @Codigo) THEN 1 ELSE 0 END;",
                    new { Codigo = codigo }, tx, cancellationToken: ct)) == 1)
                throw new ReglaException("La cuenta no puede cambiarse a título porque tiene movimientos registrados.");
        }
        else
        {
            if (PlanCuentasReglas.ErrorImputable(codigo, e, datosPadre is not null, datosPadre == true) is { } i) throw new ReglaException(i);
            if (nivelInferior) throw new ReglaException("No puede cambiar el tipo de cuenta porque existe un nivel inferior.");
        }

        var monedas = await MonedasAsync(cn, tx, ct);
        string moneda;
        if (ConfiguracionSql.EsSi(cfg, "USAMULTILESMONEDAS"))
            moneda = monedas.FirstOrDefault(m => m.Codigo.Trim() == (c.Moneda ?? string.Empty).Trim())?.Codigo ?? throw new ReglaException("Elegí la moneda de la cuenta.");
        else
            moneda = existe && !string.IsNullOrEmpty(actual!.Value.Moneda) ? actual.Value.Moneda! : MonedaDefault(monedas, ConfiguracionSql.Valor(cfg, "MONEDAVENTAS"));

        var tipoVista = c.Titulo ? null : medio.MedioDePago == "DB" ? "BC" : await TipoVistaAsync(cn, tx, codigo, cfg, ct);
        var usaCCostos = !c.Titulo && c.UsaCCostos && await HayCentrosCostoAsync(cn, tx, ct);
        var cols = await ColumnasAsync(cn, tx, ct);
        var p = new DynamicParameters();
        var campos = new List<(string Columna, string Parametro)>();
        void Campo(string columna, object? valor)
        {
            if (!cols.Contains(columna)) return;
            var nombre = "@p" + campos.Count;
            p.Add(nombre, valor);
            campos.Add(($"[{columna}]", nombre));
        }
        Campo("CODIGO", codigo);
        Campo("DESCRIPCION", descripcion);
        Campo("TITULO", c.Titulo);
        Campo("AJUSTE", !c.Titulo && c.Ajuste);
        Campo("INDICE", !c.Titulo && c.Ajuste ? (short)Math.Clamp(c.Indice, 1, 4) : (short)0);
        Campo("BLOQUEO", existe && !c.Titulo && c.Bloqueo);
        Campo("DV", (short)0);
        Campo("Bienes", !c.Titulo && c.Bienes);
        Campo("Dada_De_Baja", false);
        Campo("Libro_Iva_Compras", existe && actual!.Value.LibroCompras);
        Campo("Libro_Iva_Ventas", existe && actual!.Value.LibroVentas);
        Campo("PideVencimiento", c.PideVencimiento);
        Campo("MedioDePago", medio.MedioDePago);
        if (medio.MedioDePago == "TJ") Campo("NoPideDatosTarjeta", c.NoPideDatosTarjeta);
        Campo("CodTarjeta_ConcAuto", tarjeta);
        Campo("CuentaPrincipal", principal);
        Campo("CodigoOpcional", opcional);
        Campo("TipoVista", tipoVista);
        Campo("usaCCostos", usaCCostos);
        Campo("CajaYBanco", c.CajaYBanco);
        Campo("Moneda", moneda);
        foreach (var x in c.Personalizados ?? [])
        {
            var col = cols.FirstOrDefault(k => k.Equals(x.Nombre?.Trim(), StringComparison.OrdinalIgnoreCase) && k.StartsWith("P_", StringComparison.OrdinalIgnoreCase));
            if (col is not null && (x.Valor ?? string.Empty).Trim().Length > 0) Campo(col, x.Valor!.Trim());
        }
        var sql = existe
            ? $"UPDATE dbo.MA_CUENTAS SET {string.Join(", ", campos.Skip(1).Select(x => $"{x.Columna} = {x.Parametro}"))}{(cols.Contains("FechaHora_Modificacion") ? ", FechaHora_Modificacion = GETDATE()" : "")} WHERE CODIGO = @p0;"
            : $"INSERT INTO dbo.MA_CUENTAS ({string.Join(", ", campos.Select(x => x.Columna))}{(cols.Contains("FechaHora_Grabacion") ? ", FechaHora_Grabacion" : "")}) VALUES ({string.Join(", ", campos.Select(x => x.Parametro))}{(cols.Contains("FechaHora_Grabacion") ? ", GETDATE()" : "")});";
        await cn.ExecuteAsync(new CommandDefinition(sql, p, tx, cancellationToken: ct));

        // Tipos de cuenta que el legacy marca además en la configuración.
        if (medio.Clave == "SU")
            await ConfiguracionSql.GrabarAsync(cn, tx, PlanCuentasReglas.ClaveRetencionOtra(codigo), "SU", "DATOS", ".", ct);
        if (medio.Clave == "SC")
        {
            await ConfiguracionSql.GrabarAsync(cn, tx, "CtaSolicitudCredito", codigo, "DATOS", ".", ct);
            await TalonarioSolicitudCreditoAsync(cn, tx, ct);
        }

        // Centros de costo de la cuenta imputable (MA_CUENTASCCOSTO), igual que el legacy.
        if (!c.Titulo)
        {
            var tieneCentros = await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.MA_CUENTASCCOSTO WHERE CODIGO = @Codigo;", new { Codigo = codigo }, tx, cancellationToken: ct)) > 0;
            if (!tieneCentros && usaCCostos)
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.MA_CUENTASCCOSTO (CODIGO, CCOSTO, PORCENTAJE)
                    SELECT TOP 1 @Codigo, CODIGO, 0 FROM dbo.TA_CCOSTO ORDER BY CODIGO;
                    """, new { Codigo = codigo }, tx, cancellationToken: ct));
            else if (tieneCentros && !usaCCostos && await HayCentrosCostoAsync(cn, tx, ct))
                await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.MA_CUENTASCCOSTO WHERE CODIGO = @Codigo;", new { Codigo = codigo }, tx, cancellationToken: ct));
        }

        // Alta: los grupos de usuarios que tienen un nivel superior de la cuenta la reciben (ActualizarCuentasGrupos).
        if (!existe) await CuentasGruposAsync(cn, tx, codigo, e, ct);

        var accion = existe ? "M" : "A";
        await AccionAsync(cn, tx, accion, codigo, $"{codigo} {PlanCuentasReglas.Formatear(codigo, e)}", usuario, ct);
        await tx.CommitAsync(ct);
        await events.LogAuditAsync("PlanCuentas", existe ? (alta ? "Reactivacion" : "Modificacion") : "Alta", "MA_CUENTAS", codigo,
            $"{(existe ? alta ? "Reutilizó la cuenta dada de baja" : "Modificó la cuenta" : "Agregó la cuenta")} {codigo} {descripcion}.",
            new { codigo, descripcion, c.Titulo, medio = medio.Clave, tipoVista, moneda }, CancellationToken.None);
        return (await LeerAsync(cn, null, codigo, e, cfg, ct))!;
    }, ct);

    // ── Baja ────────────────────────────────────────────────────────────────────────────────────

    public Task<PlanCuentaBajaInfo> ConsultarBajaAsync(string codigo, CancellationToken ct = default) => EjecutarAsync("ConsultarBaja", true, async cn =>
        await AnalizarBajaAsync(cn, null, PlanCuentasReglas.Normalizar(codigo), ct), ct);

    public Task<PlanCuentaBajaResultado> EliminarAsync(string codigo, bool confirmaCashFlow, CancellationToken ct = default) => EjecutarAsync("Eliminar", true, async cn =>
    {
        var usuario = users.GetCurrentUserName().Trim();
        var c = PlanCuentasReglas.Normalizar(codigo);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var info = await AnalizarBajaAsync(cn, tx, c, ct, bloquear: true);
        if (info.Bloqueo.Length > 0) throw new ReglaException(info.Bloqueo);
        if (info.RenglonesCashFlow > 0 && !confirmaCashFlow)
            throw new ReglaException("La cuenta está asociada a un Cash-Flow: confirmá que se borren sus renglones.");
        var e = PlanCuentasReglas.EstructuraDesde(await ConfiguracionSql.LeerAsync(cn, tx, PlanCuentasReglas.ClavesEstructura, ct));
        var resultado = info.SeMarcaDeBaja ? PlanCuentaBajaResultado.MarcadaDeBaja : PlanCuentaBajaResultado.Borrada;
        if (info.RenglonesCashFlow > 0)
            await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.[MA_CASH-DETALLE] WHERE LTRIM(RTRIM(CUENTA)) = @Codigo;", new { Codigo = c }, tx, cancellationToken: ct));
        if (info.SeMarcaDeBaja)
            await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.MA_CUENTAS SET Dada_De_Baja = 1, FechaHora_Modificacion = GETDATE() WHERE CODIGO = @Codigo;",
                new { Codigo = c }, tx, cancellationToken: ct));
        else
            await cn.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.MA_CUENTASCCOSTO WHERE CODIGO = @Codigo;
                DELETE FROM dbo.MA_CUENTAS_USUARIOS WHERE Cuenta = @Codigo;
                IF OBJECT_ID(N'dbo.MA_CUENTASADIC', N'U') IS NOT NULL EXEC sys.sp_executesql N'DELETE FROM dbo.MA_CUENTASADIC WHERE CODIGO = @Codigo;', N'@Codigo nvarchar(30)', @Codigo;
                DELETE FROM dbo.MA_CUENTAS WHERE CODIGO = @Codigo;
                """, new { Codigo = c }, tx, cancellationToken: ct));
        await AccionAsync(cn, tx, "B", c, $"{c} {PlanCuentasReglas.Formatear(c, e)}", usuario, ct);
        await tx.CommitAsync(ct);
        await events.LogAuditAsync("PlanCuentas", info.SeMarcaDeBaja ? "Baja" : "Eliminar", "MA_CUENTAS", c,
            $"{(info.SeMarcaDeBaja ? "Dio de baja" : "Borró")} la cuenta {c} {info.Descripcion}{(info.RenglonesCashFlow > 0 ? $" y {info.RenglonesCashFlow} renglón(es) de Cash-Flow" : "")}.",
            new { codigo = c, info.Descripcion, info.RenglonesCashFlow, resultado = resultado.ToString() }, CancellationToken.None);
        return resultado;
    }, ct);

    /// <summary>
    /// Reglas de la baja del legacy: no se borra con niveles inferiores; una cuenta con movimientos no se borra sino que se
    /// marca dada de baja, y solo si no tiene movimientos en el ejercicio activo y su saldo de apertura es cero.
    /// Además (protección propia) no se borra la cuenta de un cliente, proveedor o legajo.
    /// </summary>
    private static async Task<PlanCuentaBajaInfo> AnalizarBajaAsync(SqlConnection cn, SqlTransaction? tx, string codigo, CancellationToken ct, bool bloquear = false)
    {
        var fila = await PrimeraAsync<(string Descripcion, bool Baja)>(cn, new CommandDefinition(
            $"SELECT LTRIM(RTRIM(ISNULL(DESCRIPCION,''))), CONVERT(bit,ISNULL(Dada_De_Baja,0)) FROM dbo.MA_CUENTAS {(bloquear ? "WITH (UPDLOCK, HOLDLOCK)" : "")} WHERE CODIGO = @Codigo;",
            new { Codigo = codigo }, tx, cancellationToken: ct)) ?? throw new ReglaException("La cuenta ya no existe.");
        var bloqueo = string.Empty;
        if (await ExisteNivelInferiorAsync(cn, tx, codigo, ct))
            bloqueo = ("No se puede eliminar esta cuenta porque posee un nivel inferior. Eliminá primero las demás cuentas e intentalo nuevamente.");
        else if (await LegajoAsync(cn, tx, codigo, ct) is { } legajo)
            bloqueo = ($"La cuenta está configurada para el legajo {legajo.Legajo} de la empresa {legajo.Empresa}: no se puede eliminar.");
        else if (await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
                     SELECT CASE WHEN OBJECT_ID(N'dbo.VT_CLIENTES', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.VT_CLIENTES WHERE CODIGO = @Codigo) THEN N'cliente'
                                 WHEN OBJECT_ID(N'dbo.VT_PROVEEDORES', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.VT_PROVEEDORES WHERE CODIGO = @Codigo) THEN N'proveedor' END;
                     """, new { Codigo = codigo }, tx, cancellationToken: ct)) is { } tercero)
            bloqueo = ($"Es la cuenta de un {tercero}: se da de baja desde el maestro de {(tercero == "cliente" ? "clientes" : "proveedores")}.");
        if (fila.Baja && bloqueo.Length == 0) bloqueo = "La cuenta ya está dada de baja.";

        var seMarca = false;
        if (bloqueo.Length == 0 && await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS WHERE CUENTA = @Codigo) THEN 1 ELSE 0 END;",
                new { Codigo = codigo }, tx, cancellationToken: ct)) == 1)
        {
            var periodo = (await ConfiguracionSql.LeerAsync(cn, tx, ["PERIODO"], ct)).GetValueOrDefault("PERIODO", "");
            var r = await cn.QuerySingleAsync<(int Movimientos, decimal Apertura)>(new CommandDefinition("""
                SELECT ISNULL(SUM(CASE WHEN ISNULL(ES_SALDO_APERTURA,0) = 0 THEN 1 ELSE 0 END), 0),
                       ISNULL(SUM(CASE WHEN ISNULL(ES_SALDO_APERTURA,0) = 1 THEN CONVERT(decimal(19,4), ISNULL(IMPORTE,0)) ELSE 0 END), 0)
                FROM dbo.MV_ASIENTOS WHERE CUENTA = @Codigo AND CONVERT(int, PERIODO) = @Periodo;
                """, new { Codigo = codigo, Periodo = int.TryParse(periodo, out var pe) ? pe : -1 }, tx, cancellationToken: ct));
            if (r.Movimientos > 0) bloqueo = "La cuenta que intentás eliminar tiene movimientos en este ejercicio.";
            else if (r.Apertura != 0) bloqueo = "La cuenta que intentás eliminar no tiene saldo 0 de apertura.";
            else seMarca = true;
        }
        var cash = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.[MA_CASH-DETALLE]', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM dbo.[MA_CASH-DETALLE] WHERE LTRIM(RTRIM(CUENTA)) = @Codigo) END;",
            new { Codigo = codigo }, tx, cancellationToken: ct));
        return new PlanCuentaBajaInfo { Codigo = codigo, Descripcion = fila.Descripcion, Bloqueo = bloqueo, SeMarcaDeBaja = seMarca, RenglonesCashFlow = cash };
    }

    // ── Auxiliares ──────────────────────────────────────────────────────────────────────────────

    private static Task<bool> ExisteNivelInferiorAsync(SqlConnection cn, SqlTransaction? tx, string codigo, CancellationToken ct)
        => cn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM dbo.MA_CUENTAS
                WHERE LEFT(LTRIM(RTRIM(CODIGO)), @Largo) = @Codigo AND LEN(LTRIM(RTRIM(CODIGO))) > @Largo) THEN 1 ELSE 0 END);
            """, new { Codigo = codigo, Largo = codigo.Length }, tx, cancellationToken: ct));

    private static async Task<(string Legajo, string Empresa)?> LegajoAsync(SqlConnection cn, SqlTransaction? tx, string codigo, CancellationToken ct)
        => !await TablaExisteAsync(cn, tx, "P_MA_Legajos", ct) ? null : await PrimeraAsync<(string Legajo, string Empresa)>(cn, new CommandDefinition("""
            SELECT TOP 1 LTRIM(RTRIM(CONVERT(nvarchar(50), Legajo))), LTRIM(RTRIM(CONVERT(nvarchar(50), IdEmpresa)))
            FROM dbo.P_MA_Legajos WHERE LTRIM(RTRIM(CUENTA)) = @Codigo;
            """, new { Codigo = codigo }, tx, cancellationToken: ct));

    private static async Task<T?> PrimeraAsync<T>(SqlConnection cn, CommandDefinition cmd) where T : struct
        => (await cn.QueryAsync<T>(cmd)).Select(x => (T?)x).FirstOrDefault();

    private static Task<bool> TablaExisteAsync(SqlConnection cn, SqlTransaction? tx, string tabla, CancellationToken ct)
        => cn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT CONVERT(bit, CASE WHEN OBJECT_ID(@Tabla, N'U') IS NULL THEN 0 ELSE 1 END);",
            new { Tabla = "dbo.[" + tabla + "]" }, tx, cancellationToken: ct));

    private static async Task<HashSet<string>> ColumnasAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => (await cn.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MA_CUENTAS');", transaction: tx, cancellationToken: ct)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<PlanCuentaOpcion>> MonedasAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => !await TablaExisteAsync(cn, tx, "TA_MONEDAS", ct) ? [] : (await cn.QueryAsync<PlanCuentaOpcion>(new CommandDefinition(
            "SELECT CODIGO Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion FROM dbo.TA_MONEDAS ORDER BY CODIGO;",
            transaction: tx, cancellationToken: ct))).ToList();

    /// <summary>MonedaVentas (número) como código de TA_MONEDAS; si no está, la primera moneda o «1».</summary>
    private static string MonedaDefault(IReadOnlyList<PlanCuentaOpcion> monedas, string monedaVentas)
    {
        var buscada = int.TryParse(monedaVentas, out var n) && n > 0 ? n.ToString() : "1";
        return monedas.FirstOrDefault(m => m.Codigo.Trim() == buscada)?.Codigo ?? monedas.FirstOrDefault()?.Codigo ?? CodigoPk.Format(buscada, 4);
    }

    private static Task<bool> HayCentrosCostoAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => cn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.TA_CCOSTO', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.TA_CCOSTO) THEN 1 ELSE 0 END);",
            transaction: tx, cancellationToken: ct));

    /// <summary>
    /// ObtieneVistaRangoVto: el rango de datos adicionales que contiene la cuenta define la vista (NN si el rango no
    /// tiene vista); si no hay rango, los títulos configurados de clientes, proveedores, bancos, etc.
    /// </summary>
    private static async Task<string?> TipoVistaAsync(SqlConnection cn, SqlTransaction tx, string codigo, IReadOnlyDictionary<string, string> cfg, CancellationToken ct)
    {
        var rango = !await TablaExisteAsync(cn, tx, "TA_RANGOS_DATOS_ADIC", ct) ? null : await PrimeraAsync<(string Rango, string Vista, string? TipoVista)>(cn, new CommandDefinition("""
                SELECT TOP 1 LTRIM(RTRIM(ISNULL(r.DESCRIPCION,''))), LTRIM(RTRIM(ISNULL(r.VISTA,''))), LTRIM(RTRIM(v.TipoVista))
                FROM dbo.TA_RANGOS_DATOS_ADIC r
                LEFT JOIN dbo.TA_VISTAS v ON LTRIM(RTRIM(v.NOMBRE)) = LTRIM(RTRIM(r.VISTA))
                WHERE @Codigo >= LTRIM(RTRIM(ISNULL(r.[CUENTA-DESDE],''))) AND @Codigo <= LTRIM(RTRIM(ISNULL(r.[CUENTA-HASTA],'')))
                ORDER BY r.[CUENTA-DESDE];
            """, new { Codigo = codigo }, tx, cancellationToken: ct));
        var vista = rango is { } r ? r.Rango.Length > 0 && r.Vista.Length == 0 ? "NN" : r.TipoVista ?? string.Empty : string.Empty;
        if (vista.Length == 0) vista = PlanCuentasReglas.TipoVistaPorTitulos(codigo, cfg);
        return vista;
    }

    /// <summary>generarTalonarioSC: el talonario SC (Solicitud de Crédito) que usa la cuenta de solicitud de crédito.</summary>
    private static Task TalonarioSolicitudCreditoAsync(SqlConnection cn, SqlTransaction tx, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.V_TA_CPTE', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO)) = N'SC')
                EXEC sys.sp_executesql N'INSERT INTO dbo.V_TA_CPTE (CODIGO, DESCRIPCION, SISTEMA, PIDEVENCIMIENTO, MODIFICANUMERACION, X_ULTIMO_NRO, X_NRO_DESDE,
                    X_NRO_HASTA, X_SUC_DEFAULT, LETRAS, TALONARIO, EXTERNO)
                    VALUES (N''SC'', N''Solicitud de Credito'', N''Contable'', 0, 1, 1, 1, 99999999, 1, N''X'', 0, 0);';
            """, transaction: tx, cancellationToken: ct));

    /// <summary>ActualizarCuentasGrupos: la cuenta nueva se habilita a los grupos que ya tienen un nivel superior; un capítulo nuevo, a todos.</summary>
    private static async Task CuentasGruposAsync(SqlConnection cn, SqlTransaction tx, string codigo, PlanCuentasEstructura e, CancellationToken ct)
    {
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.MA_CUENTAS_USUARIOS', N'U') IS NULL THEN 0 ELSE 1 END;",
                transaction: tx, cancellationToken: ct)) == 0) return;
        var prefijos = Enumerable.Range(1, Math.Max(0, codigo.Length - 1)).Select(i => codigo[..i]).ToArray();
        var grupos = prefijos.Length == 0 ? [] : (await cn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT LTRIM(RTRIM(GrupoUser)) FROM dbo.MA_CUENTAS_USUARIOS WHERE LTRIM(RTRIM(Cuenta)) IN @Prefijos AND ISNULL(LTRIM(RTRIM(GrupoUser)),'') <> '';",
            new { Prefijos = prefijos }, tx, cancellationToken: ct))).ToList();
        if (grupos.Count == 0 && e.Digitos.Count > 0 && codigo.Length == e.Digitos[0])
            grupos = (await cn.QueryAsync<string>(new CommandDefinition(
                "SELECT DISTINCT LTRIM(RTRIM(GRUPO)) FROM dbo.TA_USUARIOS WHERE ISNULL(LTRIM(RTRIM(GRUPO)),'') <> '';", transaction: tx, cancellationToken: ct))).ToList();
        foreach (var g in grupos)
            await cn.ExecuteAsync(new CommandDefinition("""
                IF NOT EXISTS (SELECT 1 FROM dbo.MA_CUENTAS_USUARIOS WHERE LTRIM(RTRIM(GrupoUser)) = @Grupo AND LTRIM(RTRIM(Cuenta)) = @Codigo)
                    INSERT INTO dbo.MA_CUENTAS_USUARIOS (GrupoUser, Cuenta) VALUES (@Grupo, @Codigo);
                """, new { Grupo = g, Codigo = codigo }, tx, cancellationToken: ct));
    }

    /// <summary>Registro de acciones del legacy (RegistraBuchon) en V_MV_CPTEACCIONES.</summary>
    private static Task AccionAsync(SqlConnection cn, SqlTransaction tx, string accion, string codigo, string mensaje, string usuario, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.V_MV_CPTEACCIONES (TC, IDCOMPROBANTE, IDCOMPLEMENTO, TIPO_ACCION, FECHAHORA, USUARIO, PC, SYSTEMUSER, Cuenta, Proceso)
            VALUES (N'', N'', 0, @Accion, GETDATE(), @Usuario, HOST_NAME(), @Mensaje, @Codigo, N'AlfaCore: plan de cuentas');
            """, new { Accion = accion, Usuario = usuario, Mensaje = mensaje.Length > 250 ? mensaje[..250] : mensaje, Codigo = codigo }, tx, cancellationToken: ct));

    private async Task<T> EjecutarAsync<T>(string accion, bool requiereAbm, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var id)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            if (requiereAbm && !await permissions.HasAccessAsync(MenuAbm, ct))
                throw new ReglaException("Tu usuario no tiene permiso para actualizar el plan de cuentas.");
            if (!requiereAbm && !await permissions.HasAccessAsync(MenuAbm, ct) && !await permissions.HasAccessAsync(ContabilidadReportesService.MenuPlanCuentas, ct))
                throw new ReglaException("Tu usuario no tiene permiso para el plan de cuentas.");
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            var r = await operacion(cn);
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != id)
                throw new ReglaException("Cambió la sesión. Volvé a abrir el plan de cuentas en la base activa.");
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (SqlException ex) when (ex.Number == 50000)
        {
            // Mensajes de los triggers del legacy (ej. TRG_MA_CUENTAS_NOMBRE).
            throw new ReglaException(ex.Message.Trim());
        }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("PlanCuentas", accion, ex, "No se pudo completar la operación con el plan de cuentas.", ct: ct);
            throw new AppUserFacingException("No se pudo completar la operación con el plan de cuentas.", codigo, ex);
        }
    }
}

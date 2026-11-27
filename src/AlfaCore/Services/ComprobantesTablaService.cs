using System.Data;
using System.Globalization;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IComprobantesTablaService
{
    Task<ComprobantesTablaCatalogo> CatalogoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ComprobanteTalonario>> ListarAsync(CancellationToken ct = default);
    Task<ComprobanteEdicion> ObtenerAsync(string codigo, int talonario, CancellationToken ct = default);
    /// <summary>Alta: si el código ya existe propone el próximo talonario y copia sus datos (como el legacy al tipear el código).</summary>
    Task<ComprobanteEdicion> PrepararAltaAsync(string codigo, CancellationToken ct = default);
    Task<ComprobanteTalonario> GuardarAsync(ComprobanteTalonario datos, bool nuevo, CancellationToken ct = default);
    Task EliminarAsync(string codigo, int talonario, CancellationToken ct = default);
}

/// <summary>
/// Tabla de comprobantes (V_TA_CPTE), como «Tabla de Comprobantes» del legacy (D010310, V_TA_CPTES.vbp, TA_Cpte.frm).
/// Cada fila es un comprobante (CODIGO) con uno de sus talonarios; la clave es CODIGO + Talonario.
/// El trigger FMT_V_TA_Cpte copia la fila a TA_COMPROBANTES leyendo TOP 1 de INSERTED: se graba siempre de a una fila.
/// </summary>
public sealed class ComprobantesTablaService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions, IAppEventService events)
    : IComprobantesTablaService
{
    internal const string MenuComprobantes = "D010310";
    public static readonly string[] LetrasOrden = ["A", "B", "C", "E", "M", "R", "X"];
    /// <summary>Orden en que el legacy deja las letras de un comprobante externo.</summary>
    public const string LetrasExterno = "ABCXREM";
    public static readonly string[] Sistemas = ["Ventas", "Stock", "Producción", "Compras", "Contable"];
    internal const int MaxNumero = 99_999_999;

    private sealed class ReglaException(string message) : InvalidOperationException(message);

    /// <summary>Atributos que el legacy fija por código sin importar lo que se configure (ActualizarDatos, «Estos tipos de comprobantes son Fijos»).</summary>
    internal sealed record AtributosFijos(string Sistema, string? EntradaSalida, string? DebeHaber);

    internal static AtributosFijos? Fijos(string codigo) => codigo.Trim().ToUpperInvariant() switch
    {
        "IR" or "SI" or "AJP" => new("Stock", "E", ""),
        "VS" or "AJN" => new("Stock", "S", ""),
        "RM" or "FC" or "ND" or "FP" or "TK" or "TKFC" => new("Ventas", "S", "D"),
        "NC" or "NCFP" => new("Ventas", "E", "H"),
        "CB" or "CBCT" => new("Ventas", null, "H"),
        "PG" or "OPG" => new("Compras", null, "D"),
        "FCC" or "NDC" or "RMC" or "FPC" => new("Compras", "E", "H"),
        "NCC" or "NCCP" => new("Compras", "S", "D"),
        _ => null
    };

    internal static string? SignoFijo(string codigo) => codigo.Trim().ToUpperInvariant() switch
    {
        "FCC" or "NDC" or "FC" or "TK" or "TKFC" or "FP" or "FPC" => "1",
        "NC" or "NCC" or "NCP" or "NCCP" => "-1",
        _ => null
    };

    /// <summary>Qué atributos admite cada sistema (VerTipoSistema del legacy).</summary>
    public static (bool Stock, bool Cuenta) AtributosDelSistema(string sistema) => NormalizarSistema(sistema) switch
    {
        "Ventas" or "Compras" => (true, true),
        "Stock" or "Producción" => (true, false),
        _ => (false, false)
    };

    internal static string NormalizarSistema(string? sistema)
    {
        var s = (sistema ?? "").Trim();
        if (s.StartsWith("Produc", StringComparison.OrdinalIgnoreCase)) return "Producción";
        return Sistemas.FirstOrDefault(x => x.Equals(s, StringComparison.OrdinalIgnoreCase)) ?? s;
    }

    /// <summary>Letras normalizadas: la letra por defecto primero; si el conjunto y la letra por defecto no cambiaron, conserva el orden original.</summary>
    internal static string OrdenarLetras(IEnumerable<string> habilitadas, string? porDefecto, string? original)
    {
        var set = habilitadas.Select(l => l.Trim().ToUpperInvariant()).Where(l => LetrasOrden.Contains(l)).Distinct().ToList();
        if (set.Count == 0) return string.Empty;
        var def = set.Contains((porDefecto ?? "").Trim().ToUpperInvariant()) ? porDefecto!.Trim().ToUpperInvariant() : null;
        var orig = (original ?? "").Trim().ToUpperInvariant();
        if (orig.Length == set.Count && set.All(l => orig.Contains(l, StringComparison.Ordinal)) && (def is null || orig[..1] == def))
            return orig;
        var ordenadas = LetrasOrden.Where(set.Contains).ToList();
        if (def is not null) { ordenadas.Remove(def); ordenadas.Insert(0, def); }
        return string.Concat(ordenadas);
    }

    /// <summary>Validaciones del legacy (CampoValida) que no dependen de la base. Normaliza los datos.</summary>
    internal static void Validar(ComprobanteTalonario t, int anchoCpteAfip)
    {
        t.Codigo = (t.Codigo ?? "").Trim().ToUpperInvariant();
        t.Descripcion = (t.Descripcion ?? "").Trim();
        t.Sistema = NormalizarSistema(t.Sistema);
        t.Unidad = (t.Unidad ?? "").Trim();
        t.Deposito = (t.Deposito ?? "").Trim();
        t.EntradaSalida = (t.EntradaSalida ?? "").Trim().ToUpperInvariant();
        t.DebeHaber = (t.DebeHaber ?? "").Trim().ToUpperInvariant();
        t.Letras = (t.Letras ?? "").Trim().ToUpperInvariant();

        if (t.Codigo.Length == 0) throw new ReglaException("Debe informar el código.");
        if (t.Codigo.Length > 4 || !t.Codigo.All(char.IsLetterOrDigit)) throw new ReglaException("El código tiene de 1 a 4 letras o dígitos.");
        if (t.Descripcion.Length == 0) throw new ReglaException("Debe informar la descripción.");
        if (t.Descripcion.Length > 50) throw new ReglaException("La descripción puede tener hasta 50 caracteres.");
        if (t.Talonario is < 0 or > 9999) throw new ReglaException("El número de talonario va de 0 a 9999.");
        if (!Sistemas.Contains(t.Sistema)) throw new ReglaException("Elegí el sistema del comprobante.");
        if (t.EntradaSalida is not ("" or "E" or "S")) throw new ReglaException("Stock: entrada, salida o ninguno.");
        if (t.DebeHaber is not ("" or "D" or "H")) throw new ReglaException("Cuenta: debe, haber o ninguno.");
        if (t.Letras.Any(c => !LetrasOrden.Contains(c.ToString()))) throw new ReglaException("Hay una letra que no existe.");

        // Lo que el sistema no admite queda en «ninguno» (VerTipoSistema); los códigos fijos mandan.
        var (stock, cuenta) = AtributosDelSistema(t.Sistema);
        if (!stock) t.EntradaSalida = string.Empty;
        if (!cuenta) t.DebeHaber = string.Empty;
        if (Fijos(t.Codigo) is { } f)
        {
            t.Sistema = f.Sistema;
            if (f.EntradaSalida is not null) t.EntradaSalida = f.EntradaSalida;
            if (f.DebeHaber is not null) t.DebeHaber = f.DebeHaber;
        }
        if (SignoFijo(t.Codigo) is { } signo) t.Signo = signo;
        if (t.EntradaSalida.Length == 0) t.Deposito = string.Empty;

        if (t.Externo)
        {
            // Comprobante externo: no valida numeración, todas las letras habilitadas.
            if (t.PuntoVenta <= 0) t.PuntoVenta = 1;
            if (!LetrasOrden.All(l => t.Letras.Contains(l, StringComparison.Ordinal))) t.Letras = LetrasExterno;
        }
        if (t.PuntoVenta is <= 0 or > 9999) throw new ReglaException("El punto de venta debe ser mayor a cero y menor a 9999.");

        foreach (var n in t.Numeracion)
        {
            n.Observaciones = (n.Observaciones ?? "").Trim();
            n.CpteAfip = (n.CpteAfip ?? "").Trim();
            if (n.Observaciones.Length > 100) throw new ReglaException($"Letra {n.Letra}: las observaciones pueden tener hasta 100 caracteres.");
            if (n.CpteAfip.Length > anchoCpteAfip || (n.CpteAfip.Length > 0 && !n.CpteAfip.All(char.IsDigit)))
                throw new ReglaException($"Letra {n.Letra}: el código de comprobante AFIP es numérico de hasta {anchoCpteAfip} dígitos.");
            if (t.Externo || !t.Letras.Contains(n.Letra, StringComparison.Ordinal)) continue;
            if (n.Desde < 0 || n.Hasta < 0 || n.Proximo < 0) throw new ReglaException($"Letra {n.Letra}: los números no pueden ser negativos.");
            if (n.Desde > MaxNumero || n.Hasta > MaxNumero || n.Proximo > MaxNumero) throw new ReglaException($"Letra {n.Letra}: el número máximo es {MaxNumero}.");
            // Sin rango (0 a 0) el legacy no controla nada: hay talonarios así que se usan igual.
            if (n.Desde == 0 && n.Hasta == 0) continue;
            if (n.Hasta < n.Desde) throw new ReglaException($"Letra {n.Letra}: el número desde es mayor que el hasta.");
            if (n.Proximo < n.Desde || n.Proximo > n.Hasta)
                throw new ReglaException($"Letra {n.Letra}: el próximo número ({n.Proximo}) no forma parte del rango {n.Desde} a {n.Hasta}.");
        }
    }

    /// <summary>ExisteEnOtroRango: con el mismo código y punto de venta, el rango de una letra no puede cruzarse con el de otro talonario.</summary>
    internal static (ComprobanteTalonario Otro, string Letra)? RangoSuperpuesto(ComprobanteTalonario t, IEnumerable<ComprobanteTalonario> otros)
    {
        if (t.Externo) return null;
        foreach (var o in otros.Where(o => o.Talonario != t.Talonario && o.PuntoVenta == t.PuntoVenta && o.Codigo.Trim().Equals(t.Codigo.Trim(), StringComparison.OrdinalIgnoreCase)))
            foreach (var n in t.Numeracion.Where(n => t.Letras.Contains(n.Letra, StringComparison.Ordinal) && (n.Desde > 0 || n.Hasta > 0)))
            {
                var x = o.Numeracion.FirstOrDefault(y => y.Letra == n.Letra);
                if (x is null || (x.Desde == 0 && x.Hasta == 0)) continue;
                if (n.Desde <= x.Hasta && x.Desde <= n.Hasta) return (o, n.Letra);
            }
        return null;
    }

    private const string SelectTalonarios = """
        SELECT LTRIM(RTRIM(CODIGO)) Codigo, Talonario, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion, LTRIM(RTRIM(ISNULL(SISTEMA,''))) Sistema,
               LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad, ISNULL(A_SUC_DEFAULT,0) A_SUC_DEFAULT, ISNULL(B_SUC_DEFAULT,0) B_SUC_DEFAULT, ISNULL(C_SUC_DEFAULT,0) C_SUC_DEFAULT,
               ISNULL(X_SUC_DEFAULT,0) X_SUC_DEFAULT, ISNULL(R_SUC_DEFAULT,0) R_SUC_DEFAULT, ISNULL(E_SUC_DEFAULT,0) E_SUC_DEFAULT,
               LTRIM(RTRIM(ISNULL(IdDeposito,''))) Deposito, CONVERT(bit, ISNULL(Externo,0)) Externo, CONVERT(bit, ISNULL(ModificaNumeracion,0)) ModificaNumeracion,
               CONVERT(bit, ISNULL(PIDEVENCIMIENTO,0)) PideVencimiento, LTRIM(RTRIM(ISNULL(LETRAS,''))) Letras, LTRIM(RTRIM(ISNULL(ES,''))) EntradaSalida,
               LTRIM(RTRIM(ISNULL(DEBEHABER,''))) DebeHaber, LTRIM(RTRIM(Signo)) Signo,
               ISNULL(A_NRO_DESDE,0) A_NRO_DESDE, ISNULL(A_NRO_HASTA,0) A_NRO_HASTA, ISNULL(A_ULTIMO_NRO,0) A_ULTIMO_NRO, VenceA, ISNULL(ObservacionesA,'') ObservacionesA, LTRIM(RTRIM(ISNULL(CpteDGIA,''))) CpteDGIA,
               ISNULL(B_NRO_DESDE,0) B_NRO_DESDE, ISNULL(B_NRO_HASTA,0) B_NRO_HASTA, ISNULL(B_ULTIMO_NRO,0) B_ULTIMO_NRO, VenceB, ISNULL(ObservacionesB,'') ObservacionesB, LTRIM(RTRIM(ISNULL(CpteDGIB,''))) CpteDGIB,
               ISNULL(C_NRO_DESDE,0) C_NRO_DESDE, ISNULL(C_NRO_HASTA,0) C_NRO_HASTA, ISNULL(C_ULTIMO_NRO,0) C_ULTIMO_NRO, VenceC, ISNULL(ObservacionesC,'') ObservacionesC, LTRIM(RTRIM(ISNULL(CpteDGIC,''))) CpteDGIC,
               ISNULL(E_NRO_DESDE,0) E_NRO_DESDE, ISNULL(E_NRO_HASTA,0) E_NRO_HASTA, ISNULL(E_ULTIMO_NRO,0) E_ULTIMO_NRO, VenceE, ISNULL(ObservacionesE,'') ObservacionesE, LTRIM(RTRIM(ISNULL(CpteDGIE,''))) CpteDGIE,
               ISNULL(M_NRO_DESDE,0) M_NRO_DESDE, ISNULL(M_NRO_HASTA,0) M_NRO_HASTA, ISNULL(M_ULTIMO_NRO,0) M_ULTIMO_NRO, VenceM, ISNULL(CONVERT(nvarchar(200),ObservacionesM),'') ObservacionesM, LTRIM(RTRIM(ISNULL(CpteDGIM,''))) CpteDGIM,
               ISNULL(R_NRO_DESDE,0) R_NRO_DESDE, ISNULL(R_NRO_HASTA,0) R_NRO_HASTA, ISNULL(R_ULTIMO_NRO,0) R_ULTIMO_NRO, VenceR, ISNULL(ObservacionesR,'') ObservacionesR, LTRIM(RTRIM(ISNULL(CpteDGIR,''))) CpteDGIR,
               ISNULL(X_NRO_DESDE,0) X_NRO_DESDE, ISNULL(X_NRO_HASTA,0) X_NRO_HASTA, ISNULL(X_ULTIMO_NRO,0) X_ULTIMO_NRO, VenceX, ISNULL(ObservacionesX,'') ObservacionesX, LTRIM(RTRIM(ISNULL(CpteDGIX,''))) CpteDGIX
        FROM dbo.V_TA_CPTE
        """;

    private static ComprobanteTalonario Mapear(IDictionary<string, object?> r)
    {
        static int I(object? v) => v is null or DBNull ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
        static string S(object? v) => v is null or DBNull ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim();
        var pv = I(r["A_SUC_DEFAULT"]);
        // Compatibilidad del legacy (AsignarDatos): si la A no tiene sucursal se toma la de otra letra.
        if (pv == 0) pv = new[] { "B", "C", "X", "R", "E" }.Select(l => I(r[$"{l}_SUC_DEFAULT"])).FirstOrDefault(x => x != 0);
        var signo = S(r["Signo"]);
        return new ComprobanteTalonario
        {
            Codigo = S(r["Codigo"]).ToUpperInvariant(), Talonario = I(r["Talonario"]), Descripcion = S(r["Descripcion"]),
            Sistema = NormalizarSistema(S(r["Sistema"])), Unidad = S(r["Unidad"]), PuntoVenta = pv, Deposito = S(r["Deposito"]),
            Externo = r["Externo"] is true, ModificaNumeracion = r["ModificaNumeracion"] is true, PideVencimiento = r["PideVencimiento"] is true,
            Letras = S(r["Letras"]).ToUpperInvariant(), EntradaSalida = S(r["EntradaSalida"]).ToUpperInvariant(), DebeHaber = S(r["DebeHaber"]).ToUpperInvariant(),
            Signo = signo.Length == 0 ? null : signo,
            Numeracion = LetrasOrden.Select(l => new ComprobanteLetra
            {
                Letra = l, Desde = I(r[$"{l}_NRO_DESDE"]), Hasta = I(r[$"{l}_NRO_HASTA"]), Proximo = I(r[$"{l}_ULTIMO_NRO"]),
                Vence = r[$"Vence{l}"] as DateTime?, Observaciones = S(r[$"Observaciones{l}"]), CpteAfip = S(r[$"CpteDGI{l}"])
            }).ToList()
        };
    }

    private static async Task<List<ComprobanteTalonario>> LeerAsync(SqlConnection cn, string where, object? args, IDbTransaction? tx, CancellationToken ct)
        => (await cn.QueryAsync(new CommandDefinition(SelectTalonarios + " " + where, args, tx, cancellationToken: ct)))
            .Select(r => Mapear((IDictionary<string, object?>)r)).ToList();

    public Task<ComprobantesTablaCatalogo> CatalogoAsync(CancellationToken ct = default) => EjecutarAsync("Catalogo", async cn => new ComprobantesTablaCatalogo
    {
        Unidades = (await cn.QueryAsync<ComprobantesOpcion>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(Codigo)) Codigo, LTRIM(RTRIM(ISNULL(Descripcion,''))) Descripcion FROM dbo.V_TA_UnidadNegocio ORDER BY Codigo;", cancellationToken: ct))).ToList(),
        Depositos = (await cn.QueryAsync<ComprobantesOpcion>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(IdDeposito)) Codigo, LTRIM(RTRIM(ISNULL(Descripcion,''))) Descripcion FROM dbo.V_TA_Deposito ORDER BY IdDeposito;", cancellationToken: ct))).ToList()
    }, ct);

    public Task<IReadOnlyList<ComprobanteTalonario>> ListarAsync(CancellationToken ct = default) => EjecutarAsync("Listar", async cn =>
        (IReadOnlyList<ComprobanteTalonario>)await LeerAsync(cn, "ORDER BY SISTEMA, CODIGO, Talonario;", null, null, ct), ct);

    public Task<ComprobanteEdicion> ObtenerAsync(string codigo, int talonario, CancellationToken ct = default) => EjecutarAsync("Obtener", async cn =>
    {
        var cod = (codigo ?? "").Trim();
        var todos = await LeerAsync(cn, "WHERE LTRIM(RTRIM(CODIGO)) = @Codigo;", new { Codigo = cod }, null, ct);
        var t = todos.FirstOrDefault(x => x.Talonario == talonario) ?? throw new ReglaException("El talonario ya no existe.");
        return await ArmarEdicionAsync(cn, t, nuevo: false, codigoExistente: true, todos.Count - 1, ct);
    }, ct);

    public Task<ComprobanteEdicion> PrepararAltaAsync(string codigo, CancellationToken ct = default) => EjecutarAsync("PrepararAlta", async cn =>
    {
        var cod = (codigo ?? "").Trim().ToUpperInvariant();
        if (cod.Length == 0 || cod.Length > 4 || !cod.All(char.IsLetterOrDigit)) throw new ReglaException("El código tiene de 1 a 4 letras o dígitos.");
        var todos = await LeerAsync(cn, "WHERE LTRIM(RTRIM(CODIGO)) = @Codigo ORDER BY Talonario;", new { Codigo = cod }, null, ct);
        var nuevo = new ComprobanteTalonario
        {
            Codigo = cod, Sistema = Fijos(cod)?.Sistema ?? "Ventas", PuntoVenta = 1, Letras = string.Empty,
            Numeracion = LetrasOrden.Select(l => new ComprobanteLetra { Letra = l }).ToList()
        };
        if (todos.LastOrDefault() is { } ultimo)
        {
            if (todos.Any(x => x.Externo)) throw new ReglaException($"El comprobante {cod} es externo: no lleva más de un talonario.");
            // Como el legacy al tipear un código existente: toma la descripción y el sistema; además sus atributos y letras.
            nuevo.Talonario = todos.Max(x => x.Talonario) + 1;
            nuevo.Descripcion = ultimo.Descripcion; nuevo.Sistema = ultimo.Sistema; nuevo.Unidad = ultimo.Unidad;
            nuevo.EntradaSalida = ultimo.EntradaSalida; nuevo.DebeHaber = ultimo.DebeHaber; nuevo.Signo = ultimo.Signo;
            nuevo.PideVencimiento = ultimo.PideVencimiento; nuevo.ModificaNumeracion = ultimo.ModificaNumeracion; nuevo.Letras = ultimo.Letras;
        }
        else if (Fijos(cod) is { } f)
        {
            nuevo.EntradaSalida = f.EntradaSalida ?? ""; nuevo.DebeHaber = f.DebeHaber ?? "";
        }
        return await ArmarEdicionAsync(cn, nuevo, nuevo: true, codigoExistente: todos.Count > 0, todos.Count, ct);
    }, ct);

    private static async Task<ComprobanteEdicion> ArmarEdicionAsync(SqlConnection cn, ComprobanteTalonario t, bool nuevo, bool codigoExistente, int otros, CancellationToken ct)
    {
        var movimientos = codigoExistente && await TieneMovimientosAsync(cn, t.Codigo, null, ct);
        var fijos = Fijos(t.Codigo);
        var (stock, cuenta) = AtributosDelSistema(t.Sistema);
        var e = new ComprobanteEdicion
        {
            Datos = t, EsNuevo = nuevo, CodigoExistente = codigoExistente, OtrosTalonarios = otros, TieneMovimientos = movimientos,
            SistemaEditable = nuevo && !codigoExistente && fijos is null,
            EntradaSalidaEditable = !movimientos && fijos?.EntradaSalida is null && stock,
            DebeHaberEditable = !movimientos && fijos?.DebeHaber is null && cuenta,
            AnchoCpteAfip = await AnchoColumnaAsync(cn, "CpteDGIA", null, ct) is > 0 and var a ? a : 2
        };
        e.MotivoAtributos = movimientos ? "El comprobante tiene movimientos de stock o asientos: no se puede cambiar Debe/Haber ni Entrada/Salida."
            : fijos is not null ? "Comprobante del sistema: parte de estos valores son fijos."
            : string.Empty;
        return e;
    }

    /// <summary>TieneMovimientosCpte del legacy: el código (en cualquier talonario) tiene movimientos de stock o asientos.</summary>
    private static async Task<bool> TieneMovimientosAsync(SqlConnection cn, string codigo, IDbTransaction? tx, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int?>(new CommandDefinition("""
            SELECT TOP 1 1 FROM (
                SELECT TOP 1 1 Hay FROM dbo.V_MV_STOCK WITH (NOLOCK) WHERE TC = @Codigo OR TC = @Padded
                UNION ALL
                SELECT TOP 1 1 FROM dbo.MV_ASIENTOS WITH (NOLOCK) WHERE TC = @Codigo OR TC = @Padded) x;
            """, new { Codigo = codigo.Trim(), Padded = CodigoPk.Format(codigo, 4) }, tx, commandTimeout: 120, cancellationToken: ct)) is not null;

    private static async Task<int> AnchoColumnaAsync(SqlConnection cn, string columna, IDbTransaction? tx, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'V_TA_CPTE' AND COLUMN_NAME = @Columna;",
            new { Columna = columna }, tx, cancellationToken: ct)) ?? 0;

    public Task<ComprobanteTalonario> GuardarAsync(ComprobanteTalonario t, bool nuevo, CancellationToken ct = default) => EjecutarAsync("Guardar", async cn =>
    {
        var anchoAfip = await AnchoColumnaAsync(cn, "CpteDGIA", null, ct);
        t.Codigo = (t.Codigo ?? "").Trim().ToUpperInvariant();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var mismos = await LeerAsync(cn, "WITH (UPDLOCK, HOLDLOCK) WHERE LTRIM(RTRIM(CODIGO)) = @Codigo;", new { t.Codigo }, tx, ct);
        var previo = mismos.FirstOrDefault(x => x.Talonario == t.Talonario);
        if (nuevo && previo is not null) throw new ReglaException($"Ya existe el comprobante {t.Codigo} con el talonario {t.Talonario}.");
        if (!nuevo && previo is null) throw new ReglaException("El talonario ya no existe.");
        var otros = mismos.Where(x => x.Talonario != t.Talonario).ToList();

        // Como el legacy: el sistema sale del código cuando ya existe, y con movimientos no cambian Debe/Haber ni Entrada/Salida.
        if ((previo ?? otros.FirstOrDefault()) is { } existente)
        {
            if (Sistemas.Contains(existente.Sistema)) t.Sistema = existente.Sistema;
            if (await TieneMovimientosAsync(cn, t.Codigo, tx, ct))
            {
                t.EntradaSalida = existente.EntradaSalida;
                t.DebeHaber = existente.DebeHaber;
            }
        }
        Validar(t, anchoAfip > 0 ? anchoAfip : 2);
        if (t.Externo && otros.Count > 0) throw new ReglaException($"El comprobante {t.Codigo} tiene otros talonarios: no puede ser externo.");
        if (!t.Externo && otros.Any(x => x.Externo)) throw new ReglaException($"El comprobante {t.Codigo} es externo: no lleva más de un talonario.");
        if (RangoSuperpuesto(t, otros) is { } sup)
            throw new ReglaException($"Letra {sup.Letra}: el rango se superpone con el talonario {sup.Otro.Talonario} del mismo punto de venta ({sup.Otro.PuntoVenta}).");

        if (t.Unidad.Length > 0 && await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo)) = @Unidad;", new { t.Unidad }, tx, cancellationToken: ct)) == 0)
            throw new ReglaException($"La unidad de negocio {t.Unidad} no existe.");
        if (t.Deposito.Length > 0 && await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.V_TA_Deposito WHERE LTRIM(RTRIM(IdDeposito)) = @Deposito;", new { t.Deposito }, tx, cancellationToken: ct)) == 0)
            throw new ReglaException($"El depósito {t.Deposito} no existe.");

        if (!nuevo && SignoFijo(t.Codigo) is null) t.Signo = previo!.Signo;
        t.Letras = OrdenarLetras(t.Letras.Select(c => c.ToString()), t.Letras.Length > 0 ? t.Letras[..1] : null, previo?.Letras);

        var p = new DynamicParameters();
        p.Add("Codigo", nuevo ? CodigoPk.Format(t.Codigo, await AnchoColumnaAsync(cn, "CODIGO", tx, ct)) : null);
        p.Add("CodigoBuscar", t.Codigo);
        p.Add("Talonario", t.Talonario);
        p.Add("Descripcion", t.Descripcion);
        p.Add("Sistema", t.Sistema);
        p.Add("DebeHaber", t.DebeHaber);
        p.Add("ES", t.EntradaSalida);
        p.Add("PideVencimiento", t.PideVencimiento);
        p.Add("ModificaNumeracion", t.ModificaNumeracion);
        p.Add("Pv", t.PuntoVenta);
        p.Add("Letras", t.Letras);
        p.Add("Signo", t.Signo);
        p.Add("Deposito", t.Deposito.Length == 0 ? null : CodigoPk.Format(t.Deposito, await AnchoColumnaAsync(cn, "IdDeposito", tx, ct)));
        p.Add("Externo", t.Externo);
        p.Add("Unidad", t.Unidad.Length == 0 ? null : CodigoPk.Format(t.Unidad, await AnchoColumnaAsync(cn, "UNEGOCIO", tx, ct)));
        foreach (var n in t.Numeracion.Where(n => LetrasOrden.Contains(n.Letra)))
        {
            p.Add($"{n.Letra}Desde", n.Desde); p.Add($"{n.Letra}Hasta", n.Hasta); p.Add($"{n.Letra}Proximo", n.Proximo);
            p.Add($"{n.Letra}Vence", n.Vence?.Date); p.Add($"{n.Letra}Obs", n.Observaciones.Length == 0 ? null : n.Observaciones);
            p.Add($"{n.Letra}Afip", n.CpteAfip.Length == 0 ? null : n.CpteAfip);
        }

        var columnasLetra = LetrasOrden.SelectMany(l => new[]
        {
            ($"{l}_NRO_DESDE", $"@{l}Desde"), ($"{l}_NRO_HASTA", $"@{l}Hasta"), ($"{l}_ULTIMO_NRO", $"@{l}Proximo"), ($"{l}_SUC_DEFAULT", "@Pv"),
            ($"Vence{l}", $"@{l}Vence"), ($"Observaciones{l}", $"@{l}Obs"), ($"CpteDGI{l}", $"@{l}Afip")
        }).ToList();
        var columnas = new List<(string Col, string Val)>
        {
            ("DESCRIPCION", "@Descripcion"), ("SISTEMA", "@Sistema"), ("DEBEHABER", "@DebeHaber"), ("ES", "@ES"), ("PIDEVENCIMIENTO", "@PideVencimiento"),
            ("ModificaNumeracion", "@ModificaNumeracion"), ("LETRAS", "@Letras"), ("Signo", "@Signo"), ("IdDeposito", "@Deposito"), ("Externo", "@Externo"), ("UNEGOCIO", "@Unidad")
        };
        columnas.AddRange(columnasLetra);
        var sql = nuevo
            ? $"INSERT INTO dbo.V_TA_CPTE (CODIGO, Talonario, FechaHora_Grabacion, {string.Join(", ", columnas.Select(c => c.Col))}) VALUES (@Codigo, @Talonario, GETDATE(), {string.Join(", ", columnas.Select(c => c.Val))});"
            : $"UPDATE dbo.V_TA_CPTE SET FechaHora_Modificacion = GETDATE(), {string.Join(", ", columnas.Select(c => $"{c.Col} = {c.Val}"))} WHERE LTRIM(RTRIM(CODIGO)) = @CodigoBuscar AND Talonario = @Talonario;";
        // El trigger FMT_V_TA_Cpte suma sus propias filas (TA_COMPROBANTES): se cuenta solo la sentencia propia.
        var filas = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SET NOCOUNT ON; DECLARE @Filas int; {sql} SET @Filas = @@ROWCOUNT; SELECT @Filas;", p, tx, cancellationToken: ct));
        if (filas != 1) throw new ReglaException("No se pudo grabar el talonario (cambió mientras lo editabas). Volvé a abrirlo.");
        await tx.CommitAsync(ct);

        await events.LogAuditAsync("TablaComprobantes", nuevo ? "Alta" : "Modificacion", "V_TA_CPTE", $"{t.Codigo}|{t.Talonario}",
            $"{(nuevo ? "Agregó" : "Modificó")} el comprobante {t.Codigo} talonario {t.Talonario} (punto de venta {t.PuntoVenta}, letras {t.Letras}).",
            new { t.Codigo, t.Talonario, t.Sistema, t.PuntoVenta, t.Letras, t.EntradaSalida, t.DebeHaber, t.Externo, Antes = previo }, CancellationToken.None);
        return (await LeerAsync(cn, "WHERE LTRIM(RTRIM(CODIGO)) = @Codigo AND Talonario = @Talonario;", new { t.Codigo, t.Talonario }, null, ct)).Single();
    }, ct);

    public Task EliminarAsync(string codigo, int talonario, CancellationToken ct = default) => EjecutarAsync("Eliminar", async cn =>
    {
        var cod = (codigo ?? "").Trim();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var mismos = await LeerAsync(cn, "WITH (UPDLOCK, HOLDLOCK) WHERE LTRIM(RTRIM(CODIGO)) = @Codigo;", new { Codigo = cod }, tx, ct);
        var t = mismos.FirstOrDefault(x => x.Talonario == talonario) ?? throw new ReglaException("El talonario ya no existe.");
        // El legacy borra sin controlar; acá no se deja sin talonario un comprobante con movimientos
        // ni se borra uno de ventas que ya emitió comprobantes con su punto de venta.
        if (mismos.Count == 1 && await TieneMovimientosAsync(cn, cod, tx, ct))
            throw new ReglaException($"El comprobante {cod} tiene movimientos y este es su único talonario: no se puede borrar.");
        if (t.Sistema == "Ventas" && !t.Externo && await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT TOP 1 1 FROM dbo.V_MV_Cpte WITH (NOLOCK) WHERE TC IN (@Codigo, @Padded) AND LEFT(IDCOMPROBANTE, 4) = @Pv;",
                new { Codigo = cod, Padded = CodigoPk.Format(cod, 4), Pv = t.PuntoVenta.ToString("0000", CultureInfo.InvariantCulture) }, tx, commandTimeout: 120, cancellationToken: ct)) is not null)
            throw new ReglaException($"Ya hay comprobantes {cod} emitidos con el punto de venta {t.PuntoVenta}: no se puede borrar el talonario.");
        await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO)) = @Codigo AND Talonario = @Talonario;",
            new { Codigo = cod, Talonario = talonario }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        await events.LogAuditAsync("TablaComprobantes", "Eliminar", "V_TA_CPTE", $"{cod}|{talonario}",
            $"Borró el comprobante {cod} talonario {talonario} (punto de venta {t.PuntoVenta}).", t, CancellationToken.None);
        return true;
    }, ct);

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            if (!await permissions.HasAccessAsync(MenuComprobantes, ct)) throw new ReglaException("Tu usuario no tiene permiso para la tabla de comprobantes.");
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            return await operacion(cn);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("TablaComprobantes", accion, ex, "No se pudo completar la operación con la tabla de comprobantes.", ct: ct);
            throw new AppUserFacingException("No se pudo completar la operación con la tabla de comprobantes.", codigo, ex);
        }
    }
}

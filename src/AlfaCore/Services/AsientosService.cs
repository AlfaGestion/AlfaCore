using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IAsientosService
{
    Task<AsientosCatalogo> CatalogoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AsientoResumen>> BuscarAsync(AsientosFiltro filtro, CancellationToken ct = default);
    Task<AsientoDetalle> GetAsync(AsientoClave clave, CancellationToken ct = default);
    /// <summary>Alta (Clave null) o modificación de un asiento manual. Devuelve el asiento grabado.</summary>
    Task<AsientoDetalle> GuardarAsync(AsientoDetalle asiento, CancellationToken ct = default);
    Task EliminarAsync(AsientoClave clave, string huella, CancellationToken ct = default);
    /// <summary>Cruce del asiento de una factura con el último Mis Comprobantes del mes bajado de ARCA.</summary>
    Task<AsientoControlArca> ControlArcaAsync(AsientoClave clave, CancellationToken ct = default);
}

/// <summary>
/// Registraciones directas al diario general, como «Carga de asientos» del legacy (D9550, MV_ASIENTOS.exe, Cn041a.frm),
/// solo la parte de asientos (sin las solapas de libro de IVA ni liquidación).
/// Los asientos manuales (talonarios contables: CC, CJA, VR, CMV, TRC…) se dan de alta, modifican y anulan acá. Los que
/// generó un comprobante (facturas de compra o venta, cobranzas, pagos, conciliaciones, sueldos…) se muestran pero no se
/// modifican: el asiento tiene que seguir coincidiendo con el comprobante, el libro de IVA y la cuenta corriente.
/// </summary>
public sealed class AsientosService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions,
    IAppEventService events) : IAsientosService
{
    internal const string MenuAsientos = "D9550";
    /// <summary>Para ver asientos alcanza con el diario; para cargar hace falta la opción del legacy.</summary>
    internal static readonly string[] MenusConsulta = [MenuAsientos, "D957002", "D957003"];
    /// <summary>Talonarios contables que no son asientos manuales (liquidación de tarjetas y asiento resumen).</summary>
    internal static readonly string[] TalonariosExcluidos = ["LT", "RS", "ÿRS"];
    /// <summary>Medios de pago que en el legacy piden datos aparte (cheques, cupones de tarjeta): se registran desde Caja.</summary>
    internal static readonly string[] MediosConDatosExtra = ["CH", "TJ"];
    internal const int MaximoRenglones = 200, LargoDetalle = 200;
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    // ── Catálogo ────────────────────────────────────────────────────────────────────────────────

    private sealed class UsuarioBase
    {
        public string Unidad { get; set; } = ""; public string Caja { get; set; } = "";
        public bool Admin { get; set; } public bool Modificar { get; set; } public bool Anular { get; set; }
    }

    private async Task<UsuarioBase> UsuarioAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<UsuarioBase>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad, LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(10), IdCaja),''))) Caja,
                   CONVERT(bit, CASE WHEN ISNULL(AdminCentral,0) <> 0 OR ISNULL(Administrador,0) <> 0 THEN 1 ELSE 0 END) Admin,
                   CONVERT(bit, CASE WHEN ISNULL(AstModificar,0) <> 0 OR ISNULL(Administrador,0) <> 0 THEN 1 ELSE 0 END) Modificar,
                   CONVERT(bit, CASE WHEN ISNULL(AstAnular,0) <> 0 OR ISNULL(Administrador,0) <> 0 THEN 1 ELSE 0 END) Anular
            FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE))) = UPPER(@Usuario);
            """, new { Usuario = users.GetCurrentUserName() }, tx, cancellationToken: ct))
            ?? throw new ReglaException("Tu usuario no está definido en la base activa.");

    private static Task<IEnumerable<AsientoCuenta>> CuentasAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => cn.QueryAsync<AsientoCuenta>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion, LTRIM(RTRIM(ISNULL(MedioDePago,''))) MedioDePago,
                   CONVERT(bit, CASE WHEN ISNULL(PideVencimiento,0) <> 0 THEN 1 ELSE 0 END) PideVencimiento
            FROM dbo.MA_CUENTAS
            WHERE ISNULL(TITULO,0) = 0 AND ISNULL(Dada_De_Baja,0) = 0 AND ISNULL(BLOQUEO,0) = 0
              AND LTRIM(RTRIM(ISNULL(Moneda,'1'))) IN ('','1')
            ORDER BY CODIGO;
            """, transaction: tx, cancellationToken: ct));

    private static async Task<List<AsientoTalonario>> TalonariosAsync(SqlConnection cn, SqlTransaction? tx, string unidad, CancellationToken ct)
    {
        var filas = await cn.QueryAsync<(string Codigo, string Descripcion, string Letras, int Sucursal, int Proximo, string Unidad)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)), LTRIM(RTRIM(ISNULL(DESCRIPCION,''))), LTRIM(RTRIM(ISNULL(LETRAS,''))), CONVERT(int, ISNULL(X_SUC_DEFAULT,1)), CONVERT(int, ISNULL(X_ULTIMO_NRO,0)),
                   LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))
            FROM dbo.V_TA_CPTE WHERE UPPER(LTRIM(RTRIM(ISNULL(SISTEMA,'')))) = 'CONTABLE' AND CHARINDEX('X', ISNULL(LETRAS,'')) > 0;
            """, transaction: tx, cancellationToken: ct));
        // Un talonario por código: el de la unidad del usuario, o el general.
        return filas.Where(f => !TalonariosExcluidos.Contains(f.Codigo, StringComparer.OrdinalIgnoreCase))
            .GroupBy(f => f.Codigo, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(f => f.Unidad.TrimStart('0') == unidad.TrimStart('0') ? 0 : f.Unidad is "" or "0" ? 1 : 2).First())
            .Select(f => new AsientoTalonario(f.Codigo, f.Descripcion, f.Letras, f.Sucursal, f.Proximo, f.Unidad))
            .OrderBy(t => t.Codigo == "CC" ? 0 : 1).ThenBy(t => t.Codigo).ToList();
    }

    public Task<AsientosCatalogo> CatalogoAsync(CancellationToken ct = default) => EjecutarAsync("Catalogo", MenusConsulta, async cn =>
    {
        var u = await UsuarioAsync(cn, null, ct);
        var unidades = (await cn.QueryAsync<CompraCargaOpcion>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(Codigo)) Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion FROM dbo.V_TA_UnidadNegocio
            WHERE @Admin = 1 OR LTRIM(RTRIM(Codigo)) = @Unidad ORDER BY Codigo;
            """, new { u.Admin, u.Unidad }, cancellationToken: ct))).ToList();
        var cargar = await permissions.HasAccessAsync(MenuAsientos, ct);
        return new AsientosCatalogo
        {
            Cuentas = (await CuentasAsync(cn, null, ct)).ToList(), Talonarios = await TalonariosAsync(cn, null, u.Unidad, ct), Unidades = unidades,
            UnidadUsuario = u.Unidad, PuedeCargar = cargar, PuedeModificar = cargar && u.Modificar, PuedeAnular = cargar && u.Anular
        };
    }, ct);

    // ── Búsqueda ────────────────────────────────────────────────────────────────────────────────

    private sealed class ResumenSql
    {
        public string Periodo { get; set; } = ""; public int Mes { get; set; } public int Numero { get; set; } public string TipoReg { get; set; } = "";
        public DateTime Fecha { get; set; } public string Tc { get; set; } = ""; public string Comprobante { get; set; } = ""; public string Detalle { get; set; } = "";
        public decimal Total { get; set; } public int Renglones { get; set; } public string Usuario { get; set; } = ""; public string Unidad { get; set; } = "";
        public string LivaTipo { get; set; } = ""; public int Bloqueos { get; set; }
    }

    /// <summary>Marcas de un asiento que lo hacen de solo lectura acá (se calculan por asiento, sobre todos sus renglones).</summary>
    private const string BloqueosSql = """
        MAX(CASE WHEN ISNULL(LTRIM(RTRIM(a.LIVA_TIPO)),'') <> '' THEN 1 ELSE 0 END)
        + MAX(CASE WHEN ISNULL(a.Conciliado,0) <> 0 THEN 1 ELSE 0 END)
        + MAX(CASE WHEN LTRIM(RTRIM(ISNULL(a.TJ_Observaciones,''))) = 'ASIENTO_SISPER' THEN 1 ELSE 0 END)
        + MAX(CASE WHEN LTRIM(RTRIM(ISNULL(a.Tc_Origen,''))) = 'mutl' THEN 1 ELSE 0 END)
        + MAX(CASE WHEN ISNULL(a.EsCierre,0) <> 0 THEN 1 ELSE 0 END)
        + CASE WHEN COUNT(DISTINCT ISNULL(a.IdCajas,'')) > 1 THEN 1 ELSE 0 END
        + CASE WHEN COUNT(DISTINCT LTRIM(RTRIM(a.TC)) + a.SUCURSAL + a.NUMERO + a.LETRA) > 1 THEN 1 ELSE 0 END
        """;

    public Task<IReadOnlyList<AsientoResumen>> BuscarAsync(AsientosFiltro f, CancellationToken ct = default) => EjecutarAsync("Buscar", MenusConsulta, async cn =>
    {
        if (f.Hasta < f.Desde || (f.Hasta - f.Desde).TotalDays > 400) throw new ReglaException("Elegí un rango de hasta un año.");
        var u = await UsuarioAsync(cn, null, ct);
        var manuales = (await TalonariosAsync(cn, null, u.Unidad, ct)).Select(t => t.Codigo).ToArray();
        var texto = (f.Texto ?? "").Trim();
        int? mes = null, numero = null;
        if (Regex.Match(texto, @"^(\d{1,2})\s*-\s*(\d{1,7})$") is { Success: true } m) { mes = int.Parse(m.Groups[1].Value); numero = int.Parse(m.Groups[2].Value); texto = ""; }
        var like = texto.Length == 0 ? "" : "%" + texto.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
        var origen = (f.Origen ?? "MANUALES").ToUpperInvariant();
        var filas = await cn.QueryAsync<ResumenSql>(new CommandDefinition($"""
            SELECT TOP (500) LTRIM(RTRIM(a.PERIODO)) Periodo, CONVERT(int, a.MES_OPERATIVO) Mes, CONVERT(int, a.[NUMERO ASIENTO]) Numero, ISNULL(a.TIPO_REG,'') TipoReg,
                   MIN(a.FECHA) Fecha, MAX(LTRIM(RTRIM(a.TC))) Tc,
                   MAX(LTRIM(RTRIM(a.SUCURSAL)) + '-' + LTRIM(RTRIM(a.NUMERO)) + ' ' + LTRIM(RTRIM(a.LETRA))) Comprobante,
                   -- El detalle del asiento (los conceptos de renglón terminan en «.»; si todos los tienen, el primero).
                   ISNULL(MAX(CASE WHEN RIGHT(RTRIM(CONVERT(nvarchar(200), a.DETALLE)), 1) <> '.' THEN LEFT(LTRIM(RTRIM(CONVERT(nvarchar(200), a.DETALLE))), 120) END),
                          MAX(LEFT(LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(200), a.DETALLE),''))), 120))) Detalle,
                   SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE 0 END) Total, COUNT(*) Renglones,
                   MAX(LTRIM(RTRIM(ISNULL(a.USUARIO_LOGEADO,'')))) Usuario, MAX(LTRIM(RTRIM(ISNULL(a.UNEGOCIO,'')))) Unidad,
                   MAX(LTRIM(RTRIM(ISNULL(a.LIVA_TIPO,'')))) LivaTipo,
                   {BloqueosSql} Bloqueos
            FROM dbo.MV_ASIENTOS a
            WHERE a.FECHA >= @Desde AND a.FECHA < DATEADD(day, 1, @Hasta) AND ISNULL(a.EsResumen,0) = 0
              AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) = @Unidad)
              AND (@Mes IS NULL OR (a.MES_OPERATIVO = @Mes AND a.[NUMERO ASIENTO] = @Numero))
            GROUP BY a.PERIODO, a.MES_OPERATIVO, a.[NUMERO ASIENTO], a.TIPO_REG
            HAVING (@Cuenta = '' OR MAX(CASE WHEN LTRIM(RTRIM(a.CUENTA)) = @Cuenta THEN 1 ELSE 0 END) = 1)
               AND (@Like = '' OR MAX(CASE WHEN CONVERT(nvarchar(200), a.DETALLE) LIKE @Like OR a.CUENTA LIKE @Like OR a.USUARIO_LOGEADO LIKE @Like
                        OR LTRIM(RTRIM(a.TC)) + ' ' + LTRIM(RTRIM(a.SUCURSAL)) + '-' + LTRIM(RTRIM(a.NUMERO)) LIKE @Like OR a.NUMERO LIKE @Like THEN 1 ELSE 0 END) = 1)
               AND (@Origen = 'TODOS'
                    OR (@Origen = 'MANUALES' AND LTRIM(MAX(ISNULL(a.TIPO_REG,''))) = '' AND MAX(LTRIM(RTRIM(a.TC))) IN @Manuales AND {BloqueosSql} = 0)
                    OR (@Origen = 'COMPROBANTES' AND NOT (LTRIM(MAX(ISNULL(a.TIPO_REG,''))) = '' AND MAX(LTRIM(RTRIM(a.TC))) IN @Manuales AND {BloqueosSql} = 0)))
            ORDER BY MIN(a.FECHA) DESC, a.MES_OPERATIVO DESC, a.[NUMERO ASIENTO] DESC;
            """, new { Desde = f.Desde.Date, Hasta = f.Hasta.Date, Unidad = (f.Unidad ?? "").Trim(), Cuenta = (f.Cuenta ?? "").Trim(), Like = like, Mes = mes, Numero = numero,
                Origen = origen, Manuales = manuales.Length == 0 ? ["CC"] : manuales }, commandTimeout: 120, cancellationToken: ct));
        return (IReadOnlyList<AsientoResumen>)filas.Select(x => new AsientoResumen
        {
            Periodo = x.Periodo, Mes = x.Mes, Numero = x.Numero, TipoReg = x.TipoReg.Trim(), Fecha = x.Fecha, Tc = x.Tc, Comprobante = x.Comprobante,
            Detalle = x.Detalle.TrimEnd('.'), Total = x.Total, Renglones = x.Renglones, Usuario = x.Usuario, Unidad = x.Unidad, LivaTipo = x.LivaTipo,
            Manual = x.Bloqueos == 0 && x.TipoReg.Trim().Length == 0 && manuales.Contains(x.Tc, StringComparer.OrdinalIgnoreCase)
        }).ToList();
    }, ct);

    // ── Detalle ─────────────────────────────────────────────────────────────────────────────────

    private sealed class RenglonSql
    {
        public int Secuencia { get; set; } public DateTime Fecha { get; set; } public string Tc { get; set; } = ""; public string Sucursal { get; set; } = "";
        public string Numero { get; set; } = ""; public string Letra { get; set; } = ""; public string Cuenta { get; set; } = ""; public string CuentaNombre { get; set; } = "";
        public string MedioDePago { get; set; } = ""; public string Dh { get; set; } = ""; public decimal Importe { get; set; } public string Detalle { get; set; } = "";
        public DateTime? Vencimiento { get; set; } public string Unidad { get; set; } = ""; public string Caja { get; set; } = ""; public string Usuario { get; set; } = "";
        public DateTime? Grabado { get; set; } public DateTime? Modificado { get; set; } public string LivaTipo { get; set; } = ""; public bool Conciliado { get; set; }
        public string Observaciones { get; set; } = ""; public string TcOrigen { get; set; } = ""; public bool Cierre { get; set; } public bool Resumen { get; set; }
        public bool Apertura { get; set; } public string CabCuenta { get; set; } = ""; public string CabNombre { get; set; } = ""; public string CabCuit { get; set; } = "";
        public decimal LivaTotal { get; set; } public string Cae { get; set; } = ""; public string Moneda { get; set; } = "";
    }

    private static async Task<List<RenglonSql>> RenglonesAsync(SqlConnection cn, SqlTransaction? tx, AsientoClave k, bool bloquear, CancellationToken ct)
        => (await cn.QueryAsync<RenglonSql>(new CommandDefinition($"""
            SELECT CONVERT(int, a.SECUENCIA) Secuencia, a.FECHA Fecha, LTRIM(RTRIM(a.TC)) Tc, LTRIM(RTRIM(a.SUCURSAL)) Sucursal, LTRIM(RTRIM(a.NUMERO)) Numero,
                   LTRIM(RTRIM(a.LETRA)) Letra, LTRIM(RTRIM(a.CUENTA)) Cuenta, LTRIM(RTRIM(ISNULL(c.DESCRIPCION,''))) CuentaNombre,
                   LTRIM(RTRIM(ISNULL(c.MedioDePago,''))) MedioDePago, a.[DEBE-HABER] Dh, ISNULL(a.IMPORTE,0) Importe,
                   ISNULL(CONVERT(nvarchar(200), a.DETALLE),'') Detalle, a.VENCIMIENTO Vencimiento, LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) Unidad,
                   LTRIM(RTRIM(ISNULL(a.IdCajas,''))) Caja, LTRIM(RTRIM(ISNULL(a.USUARIO_LOGEADO,''))) Usuario, a.FechaHora_Grabacion Grabado,
                   a.FechaHora_Modificacion Modificado, LTRIM(RTRIM(ISNULL(a.LIVA_TIPO,''))) LivaTipo, CONVERT(bit, ISNULL(a.Conciliado,0)) Conciliado,
                   LTRIM(RTRIM(ISNULL(a.TJ_Observaciones,''))) Observaciones, LTRIM(RTRIM(ISNULL(a.Tc_Origen,''))) TcOrigen,
                   CONVERT(bit, ISNULL(a.EsCierre,0)) Cierre, CONVERT(bit, ISNULL(a.EsResumen,0)) Resumen, CONVERT(bit, ISNULL(a.ES_SALDO_APERTURA,0)) Apertura,
                   LTRIM(RTRIM(ISNULL(a.CABCUENTA,''))) CabCuenta, LTRIM(RTRIM(ISNULL(a.CABNOMBRE,''))) CabNombre, LTRIM(RTRIM(ISNULL(a.CABCUIT,''))) CabCuit,
                   ISNULL(a.LIVA_TOTAL,0) LivaTotal, LTRIM(RTRIM(ISNULL(a.NroCAI,''))) Cae, LTRIM(RTRIM(ISNULL(a.MONEDA,''))) Moneda
            FROM dbo.MV_ASIENTOS a {(bloquear ? "WITH (UPDLOCK, HOLDLOCK)" : "")}
            LEFT JOIN dbo.MA_CUENTAS c ON c.CODIGO = a.CUENTA
            WHERE LTRIM(RTRIM(a.PERIODO)) = @Periodo AND a.MES_OPERATIVO = @Mes AND a.[NUMERO ASIENTO] = @Numero
              AND ISNULL(LTRIM(RTRIM(a.TIPO_REG)),'') = @TipoReg
            ORDER BY a.SECUENCIA;
            """, new { Periodo = k.Periodo.Trim(), k.Mes, k.Numero, TipoReg = k.TipoReg.Trim() }, tx, commandTimeout: 60, cancellationToken: ct))).ToList();

    /// <summary>Por qué un asiento no se modifica desde acá; vacío = es un asiento manual.</summary>
    internal static string MotivoSoloLectura(IReadOnlyList<(string Tc, string TipoReg, string LivaTipo, bool Conciliado, string Observaciones, string TcOrigen,
        bool Cierre, bool Resumen, string Caja, string Comprobante, string MedioDePago)> r, IReadOnlyCollection<string> talonariosManuales)
    {
        if (r.Count == 0) return "El asiento no existe.";
        var tc = r[0].Tc;
        if (r.Any(x => x.Resumen)) return "Es un asiento resumen: no se modifica.";
        if (r.Any(x => x.Cierre)) return "Es un asiento de cierre de ejercicio.";
        var liva = r.Select(x => x.LivaTipo).FirstOrDefault(x => x.Length > 0) ?? "";
        if (liva.Equals("COMPRAS", StringComparison.OrdinalIgnoreCase)) return "Es el asiento de un comprobante de compras: se corrige desde la compra (libro de IVA compras), para que siga coincidiendo con el comprobante y la cuenta corriente.";
        if (liva.Equals("VENTAS", StringComparison.OrdinalIgnoreCase)) return "Es el asiento de un comprobante de ventas: se corrige desde la venta (anulación o nota de crédito), para que siga coincidiendo con el comprobante y la cuenta corriente.";
        if (liva.Length > 0) return "Es el asiento de un comprobante del libro de IVA.";
        if (r.Any(x => x.Observaciones == "ASIENTO_SISPER")) return "Lo generó la liquidación de sueldos.";
        if (r.Any(x => x.Conciliado)) return "Tiene movimientos conciliados con el banco.";
        if (r.Any(x => x.TcOrigen.Equals("mutl", StringComparison.OrdinalIgnoreCase))) return "Lo generó el módulo de mutuales.";
        if (r.Select(x => x.Comprobante).Distinct().Count() > 1) return "El asiento agrupa más de un comprobante.";
        if (r.Select(x => x.Caja).Distinct().Count() > 1) return "Es una transferencia entre cajas.";
        var tipoReg = r[0].TipoReg.Trim();
        if (tipoReg.Length > 0) return $"Es del subdiario {tipoReg} (cobranzas, pagos u otro módulo): se modifica desde su comprobante.";
        if (tc.Equals("LT", StringComparison.OrdinalIgnoreCase)) return "Es una liquidación de tarjetas: se maneja desde su propia opción.";
        if (!talonariosManuales.Contains(tc, StringComparer.OrdinalIgnoreCase)) return $"Lo generó un comprobante {tc}: se modifica desde ese módulo.";
        if (r.Any(x => MediosConDatosExtra.Contains(x.MedioDePago, StringComparer.OrdinalIgnoreCase))) return "Tiene cheques o cupones de tarjeta, que llevan datos aparte: se modifica en el Alfa Gestión.";
        return "";
    }

    internal static string Huella(IEnumerable<(string Cuenta, string Dh, decimal Importe, string Detalle, DateTime Fecha)> r)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", r.Select(x =>
            $"{x.Cuenta}|{x.Dh}|{x.Importe.ToString("0.00", CultureInfo.InvariantCulture)}|{x.Detalle}|{x.Fecha:yyyyMMdd}")))))[..32];

    public Task<AsientoDetalle> GetAsync(AsientoClave clave, CancellationToken ct = default) => EjecutarAsync("Consultar", MenusConsulta, async cn =>
    {
        var u = await UsuarioAsync(cn, null, ct);
        var manuales = (await TalonariosAsync(cn, null, u.Unidad, ct)).Select(t => t.Codigo).ToList();
        return Armar(clave, await RenglonesAsync(cn, null, clave, false, ct), manuales);
    }, ct);

    private static AsientoDetalle Armar(AsientoClave clave, List<RenglonSql> r, IReadOnlyCollection<string> manuales)
    {
        if (r.Count == 0) throw new ReglaException("El asiento no existe (puede haberse anulado).");
        var p = r[0];
        // Como el legacy: el concepto de un renglón se guarda en DETALLE terminado en «.»; el resto lleva el detalle del asiento.
        var detalle = r.Select(x => x.Detalle.Trim()).FirstOrDefault(d => d.Length > 0 && !d.EndsWith('.')) ?? r[0].Detalle.Trim().TrimEnd('.');
        var cab = r.FirstOrDefault(x => x.LivaTipo.Length > 0) ?? p;
        var d = new AsientoDetalle
        {
            Clave = clave, Fecha = p.Fecha, Tc = p.Tc, Sucursal = p.Sucursal, Numero = p.Numero, Letra = p.Letra, Detalle = detalle,
            Vencimiento = p.Vencimiento?.Date == p.Fecha.Date ? null : p.Vencimiento, Unidad = p.Unidad, Usuario = p.Usuario, Grabado = p.Grabado,
            Modificado = r.Max(x => x.Modificado),
            Renglones = r.Select(x => new AsientoRenglon
            {
                Cuenta = x.Cuenta, CuentaNombre = x.CuentaNombre,
                Detalle = x.Detalle.Trim().EndsWith('.') && x.Detalle.Trim().TrimEnd('.') != detalle ? x.Detalle.Trim().TrimEnd('.') : "",
                Debe = x.Dh == "D" ? x.Importe : 0, Haber = x.Dh == "D" ? 0 : x.Importe
            }).ToList(),
            MotivoSoloLectura = MotivoSoloLectura(r.Select(x => (x.Tc, clave.TipoReg, x.LivaTipo, x.Conciliado, x.Observaciones, x.TcOrigen, x.Cierre, x.Resumen, x.Caja,
                x.Tc + x.Sucursal + x.Numero + x.Letra, x.MedioDePago)).ToList(), manuales),
            LivaTipo = cab.LivaTipo, CuentaComprobante = cab.CabCuenta, NombreComprobante = cab.CabNombre, CuitComprobante = cab.CabCuit,
            TotalComprobante = cab.LivaTotal, Cae = cab.Cae,
            Huella = Huella(r.Select(x => (x.Cuenta, x.Dh, x.Importe, x.Detalle, x.Fecha)))
        };
        d.Editable = d.MotivoSoloLectura.Length == 0;
        return d;
    }

    // ── Validación ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Validaciones de renglones que no dependen de la base (las del legacy al grabar).</summary>
    internal static void ValidarRenglones(AsientoDetalle a, IReadOnlyDictionary<string, AsientoCuenta> cuentas, ISet<string> cuentasTerceros)
    {
        var detalle = (a.Detalle ?? "").Trim();
        if (detalle.Length is < 3 or > LargoDetalle - 1) throw new ReglaException("Escribí el detalle del asiento (concepto).");
        var renglones = a.Renglones.Where(r => r.Cuenta.Trim().Length > 0 || r.Debe != 0 || r.Haber != 0).ToList();
        if (renglones.Count < 2) throw new ReglaException("El asiento necesita al menos dos renglones.");
        if (renglones.Count > MaximoRenglones) throw new ReglaException($"El asiento puede tener hasta {MaximoRenglones} renglones.");
        for (var i = 0; i < renglones.Count; i++)
        {
            var r = renglones[i]; var n = i + 1;
            if (!cuentas.TryGetValue(r.Cuenta.Trim(), out var cuenta))
                throw new ReglaException($"Renglón {n}: la cuenta {r.Cuenta.Trim()} no existe, es un título, está dada de baja o bloqueada, o no es en pesos.");
            if (MediosConDatosExtra.Contains(cuenta.MedioDePago, StringComparer.OrdinalIgnoreCase))
                throw new ReglaException($"Renglón {n}: {cuenta.Descripcion} es de cheques o tarjetas, que llevan datos aparte: registralo desde Caja y bancos o el Alfa Gestión.");
            if (r.Debe < 0 || r.Haber < 0) throw new ReglaException($"Renglón {n}: los importes no pueden ser negativos.");
            if ((r.Debe != 0) == (r.Haber != 0)) throw new ReglaException($"Renglón {n}: cargá el importe en el debe o en el haber (uno solo).");
            if (decimal.Round(r.Debe, 2) != r.Debe || decimal.Round(r.Haber, 2) != r.Haber) throw new ReglaException($"Renglón {n}: los importes van con dos decimales.");
            if (r.Debe + r.Haber > 999_999_999_999m) throw new ReglaException($"Renglón {n}: el importe es demasiado grande.");
            if ((r.Detalle ?? "").Trim().Length > LargoDetalle - 1) throw new ReglaException($"Renglón {n}: el concepto es demasiado largo.");
            if (cuenta.PideVencimiento && (a.Vencimiento is null || a.Vencimiento.Value.Date < a.Fecha.Date))
                throw new ReglaException($"La cuenta {cuenta.Descripcion} pide vencimiento: cargalo (no puede ser anterior a la fecha).");
        }
        var repetidas = renglones.Select(r => r.Cuenta.Trim()).Where(cuentasTerceros.Contains).GroupBy(c => c).FirstOrDefault(g => g.Count() > 1);
        if (repetidas is not null) throw new ReglaException($"La cuenta de cliente o proveedor {repetidas.Key} está repetida: va una sola vez por asiento.");
        var debe = renglones.Sum(r => r.Debe); var haber = renglones.Sum(r => r.Haber);
        if (debe <= 0 || debe != haber) throw new ReglaException($"El asiento no balancea: debe {debe:N2} y haber {haber:N2}.");
        a.Renglones = renglones;
    }

    /// <summary>Fecha: ejercicio abierto, fecha operativa, libro diario rubricado y cierre de caja (como el trigger MV_Asientos_ValidaFechas).</summary>
    private static async Task<string> ValidarFechaAsync(SqlConnection cn, SqlTransaction tx, DateTime fecha, bool tocaCaja, CancellationToken ct)
    {
        var periodo = await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "SELECT TOP (1) CONVERT(int, PERIODO) FROM dbo.MV_EJERCICIOS WITH (UPDLOCK, HOLDLOCK) WHERE @Fecha BETWEEN [FECHA DESDE] AND [FECHA HASTA] AND ISNULL(CERRADO,0) = 0 ORDER BY PERIODO DESC;",
            new { Fecha = fecha.Date }, tx, cancellationToken: ct)) ?? throw new ReglaException("La fecha no pertenece a un ejercicio abierto.");
        var cfg = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('FECHAOPERDESDE','FECHAOPERHASTA','CIERRECAJA');
            """, transaction: tx, cancellationToken: ct))).GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor);
        DateTime? F(string k) => DateTime.TryParseExact(cfg.GetValueOrDefault(k, ""), ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
        if (F("FECHAOPERDESDE") is { } desde && fecha.Date < desde || F("FECHAOPERHASTA") is { } hasta && fecha.Date > hasta)
            throw new ReglaException($"La fecha está fuera de la fecha operativa ({cfg.GetValueOrDefault("FECHAOPERDESDE")} al {cfg.GetValueOrDefault("FECHAOPERHASTA")}).");
        if (tocaCaja && F("CIERRECAJA") is { } caja && fecha.Date <= caja)
            throw new ReglaException($"La caja está cerrada al {caja:dd/MM/yyyy}: no se pueden registrar movimientos de caja o bancos hasta esa fecha.");
        var rubricado = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.TA_ARCHIVOLEGAL', N'U') IS NULL SELECT 0
            ELSE SELECT COUNT(*) FROM dbo.TA_ARCHIVOLEGAL WHERE LTRIM(RTRIM(PERIODO)) = @Periodo AND LTRIM(RTRIM(TIPO)) = 'DG';
            """, new { Periodo = fecha.ToString("MM/yyyy", CultureInfo.InvariantCulture) }, tx, cancellationToken: ct));
        if (rubricado > 0) throw new ReglaException($"El libro diario de {fecha:MM/yyyy} ya se emitió (archivo legal): no se pueden registrar asientos en ese mes.");
        return periodo.ToString(CultureInfo.InvariantCulture);
    }

    // ── Grabar ──────────────────────────────────────────────────────────────────────────────────

    public Task<AsientoDetalle> GuardarAsync(AsientoDetalle a, CancellationToken ct = default) => EjecutarAsync("Guardar", [MenuAsientos], async cn =>
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var u = await UsuarioAsync(cn, tx, ct);
        var talonarios = await TalonariosAsync(cn, tx, u.Unidad, ct);
        var cuentas = (await CuentasAsync(cn, tx, ct)).ToDictionary(c => c.Codigo, StringComparer.OrdinalIgnoreCase);
        var terceros = (await cn.QueryAsync<string>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.VT_CLIENTES UNION SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.VT_PROVEEDORES;
            """, transaction: tx, commandTimeout: 60, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ValidarRenglones(a, cuentas, terceros);
        var tocaCaja = a.Renglones.Any(r => cuentas[r.Cuenta.Trim()].MedioDePago.Length > 0);
        var periodo = await ValidarFechaAsync(cn, tx, a.Fecha, tocaCaja, ct);
        var detalle = a.Detalle.Trim();
        var total = a.Renglones.Sum(r => r.Debe);
        var usuario = users.GetCurrentUserName();
        AsientoClave clave; string tc, suc, nro, letra, unidad, caja, usuarioGrabo; DateTime? grabado = null; bool apertura = false; string accion;

        if (a.Clave is { } k)
        {
            accion = "M";
            if (!u.Modificar) throw new ReglaException("Tu usuario no tiene permiso para modificar asientos (Modificar asientos en el usuario).");
            var previo = await RenglonesAsync(cn, tx, k, bloquear: true, ct);
            var motivo = MotivoSoloLectura(previo.Select(x => (x.Tc, k.TipoReg, x.LivaTipo, x.Conciliado, x.Observaciones, x.TcOrigen, x.Cierre, x.Resumen, x.Caja,
                x.Tc + x.Sucursal + x.Numero + x.Letra, x.MedioDePago)).ToList(), talonarios.Select(t => t.Codigo).ToList());
            if (motivo.Length > 0) throw new ReglaException(motivo);
            if (Huella(previo.Select(x => (x.Cuenta, x.Dh, x.Importe, x.Detalle, x.Fecha))) != a.Huella)
                throw new ReglaException("Otro usuario modificó este asiento mientras lo editabas. Volvé a abrirlo.");
            // El número de asiento es por período y mes: cambiar de mes es otro asiento.
            if (periodo != k.Periodo.Trim() || a.Fecha.Month != k.Mes)
                throw new ReglaException("Para pasar el asiento a otro mes o ejercicio, anulalo y cargalo de nuevo con la fecha nueva.");
            var p = previo[0];
            (tc, suc, nro, letra, unidad, caja, usuarioGrabo, grabado, apertura) = (p.Tc, p.Sucursal, p.Numero, p.Letra, p.Unidad, p.Caja, p.Usuario, p.Grabado, p.Apertura);
            clave = k;
            await cn.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO)) = @Periodo AND MES_OPERATIVO = @Mes AND [NUMERO ASIENTO] = @Numero AND ISNULL(LTRIM(RTRIM(TIPO_REG)),'') = @TipoReg;
                """, new { Periodo = k.Periodo.Trim(), k.Mes, k.Numero, TipoReg = k.TipoReg.Trim() }, tx, cancellationToken: ct));
        }
        else
        {
            accion = "A";
            var tal = talonarios.FirstOrDefault(t => t.Codigo.Equals((a.Tc ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ReglaException("Elegí el comprobante contable del asiento (CC, CJA…).");
            unidad = (a.Unidad ?? "").Trim().Length == 0 ? u.Unidad : a.Unidad.Trim();
            if (!u.Admin && unidad.TrimStart('0') != u.Unidad.TrimStart('0')) throw new ReglaException("Solo un administrador puede registrar en otra unidad de negocio.");
            // Número del comprobante: el contador del talonario, compartido con el Alfa Gestión (igual que la conciliación bancaria).
            var t = await cn.QuerySingleAsync<(int Sucursal, int Proximo, int Desde, int Hasta, DateTime? Vence)>(new CommandDefinition("""
                SELECT TOP (1) CONVERT(int, ISNULL(X_SUC_DEFAULT,1)), CONVERT(int, ISNULL(X_ULTIMO_NRO,0)), CONVERT(int, ISNULL(X_NRO_DESDE,0)), CONVERT(int, ISNULL(X_NRO_HASTA,0)), VenceX
                FROM dbo.V_TA_CPTE WITH (UPDLOCK, HOLDLOCK)
                WHERE LTRIM(RTRIM(CODIGO)) = @Codigo AND LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) = @Unidad;
                """, new { tal.Codigo, tal.Unidad }, tx, cancellationToken: ct));
            if (t.Sucursal is < 1 or > 9999) throw new ReglaException($"El talonario {tal.Codigo} tiene una sucursal inválida.");
            if (t.Vence?.Date < a.Fecha.Date) throw new ReglaException($"El talonario {tal.Codigo} está vencido.");
            suc = t.Sucursal.ToString("D4", CultureInfo.InvariantCulture);
            var max = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT ISNULL(MAX(CONVERT(int, CASE WHEN LTRIM(RTRIM(NUMERO)) NOT LIKE '%[^0-9]%' THEN NULLIF(LTRIM(RTRIM(NUMERO)),'') ELSE '0' END)),0)
                FROM dbo.MV_ASIENTOS WITH (UPDLOCK, HOLDLOCK) WHERE TC = @Codigo AND SUCURSAL = @Suc AND LETRA = 'X';
                """, new { tal.Codigo, Suc = suc }, tx, cancellationToken: ct));
            var numero = Math.Max(Math.Max(t.Proximo, Math.Max(t.Desde, 1)), checked(max + 1));
            if ((t.Hasta > 0 && numero > t.Hasta) || numero > 99_999_999) throw new ReglaException($"El talonario {tal.Codigo} agotó su numeración.");
            (tc, nro, letra, caja, usuarioGrabo) = (tal.Codigo, numero.ToString("D8", CultureInfo.InvariantCulture), "X", u.Caja.Length == 0 ? "1" : u.Caja, usuario);
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.V_TA_CPTE SET X_ULTIMO_NRO = @Proximo WHERE LTRIM(RTRIM(CODIGO)) = @Codigo AND LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) = @Unidad;
                """, new { Proximo = checked(numero + 1), tal.Codigo, tal.Unidad }, tx, cancellationToken: ct));
            clave = new AsientoClave(periodo, a.Fecha.Month, await NumeracionAsiento.SiguienteAsync(cn, tx, periodo, a.Fecha.Month, "", ct), "");
        }

        for (var i = 0; i < a.Renglones.Count; i++)
        {
            var r = a.Renglones[i];
            var concepto = (r.Detalle ?? "").Trim();
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.MV_ASIENTOS (CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE, TC, SUCURSAL, NUMERO, LETRA,
                    [DEBE-HABER], IMPORTE, MONEDA, COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO, FechaHora_Grabacion, FechaHora_Modificacion,
                    FechaSubdiario, USUARIO_LOGEADO, IdCajas, UNEGOCIO, VENCIMIENTO, ES_SALDO_APERTURA)
                VALUES (@Cuenta, @Secuencia, @Mes, @Asiento, @Fecha, @Detalle, @Tc, @Suc, @Nro, @Letra,
                    @Dh, @Importe, @Moneda, 1, @Periodo, @Total, @TipoReg, 0, ISNULL(@Grabado, GETDATE()), @Modificado,
                    @Fecha, @Usuario, @Caja, @Unidad, @Vencimiento, @Apertura);
                """, new
            {
                Cuenta = r.Cuenta.Trim(), Secuencia = i + 1, clave.Mes, Asiento = clave.Numero, Fecha = a.Fecha.Date,
                // Como el legacy: el concepto del renglón con «.» al final; si no tiene, el detalle del asiento.
                Detalle = concepto.Length > 0 ? concepto + "." : detalle,
                Tc = tc, Suc = suc, Nro = nro, Letra = letra, Dh = r.Debe > 0 ? "D" : "H", Importe = r.Debe > 0 ? r.Debe : r.Haber,
                Moneda = CodigoPk.Format("1", 4), Periodo = clave.Periodo, Total = total, TipoReg = CodigoPk.Format(clave.TipoReg, 4),
                Grabado = grabado, Modificado = accion == "M" ? DateTime.Now : (DateTime?)null, Usuario = usuarioGrabo,
                Caja = CodigoPk.Format(caja, 4), Unidad = CodigoPk.Format(unidad, 4), Vencimiento = (a.Vencimiento ?? a.Fecha).Date, Apertura = apertura
            }, tx, cancellationToken: ct));
        }
        var control = await cn.QuerySingleAsync<(int Renglones, decimal Saldo)>(new CommandDefinition("""
            SELECT COUNT(*), ISNULL(SUM(CASE WHEN [DEBE-HABER] = 'D' THEN IMPORTE ELSE -IMPORTE END),0) FROM dbo.MV_ASIENTOS
            WHERE LTRIM(RTRIM(PERIODO)) = @Periodo AND MES_OPERATIVO = @Mes AND [NUMERO ASIENTO] = @Numero AND ISNULL(LTRIM(RTRIM(TIPO_REG)),'') = @TipoReg;
            """, new { Periodo = clave.Periodo, clave.Mes, clave.Numero, TipoReg = clave.TipoReg.Trim() }, tx, cancellationToken: ct));
        if (control.Renglones != a.Renglones.Count || control.Saldo != 0) throw new ReglaException("El asiento no quedó completo y balanceado. No se grabó.");
        await AccionAsync(cn, tx, accion, tc, suc + nro + letra, clave, a.Fecha, a.Renglones[0].Cuenta.Trim(), usuario, ct);
        await tx.CommitAsync(ct);
        await events.LogAuditAsync("Asientos", accion == "A" ? "Alta" : "Modificacion", "MV_ASIENTOS", clave.Texto,
            $"{(accion == "A" ? "Registró" : "Modificó")} el asiento {clave.Texto} ({tc} {suc}-{nro}) por {total:N2}: {detalle}.",
            new { clave.Periodo, clave.Mes, clave.Numero, tc, suc, nro, total, renglones = a.Renglones.Count }, CancellationToken.None);
        return Armar(clave, await RenglonesAsync(cn, null, clave, false, ct), talonarios.Select(t => t.Codigo).ToList());
    }, ct);

    private static Task AccionAsync(SqlConnection cn, SqlTransaction tx, string accion, string tc, string idComprobante, AsientoClave k, DateTime fecha, string cuenta,
        string usuario, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.V_MV_CPTEACCIONES (TC, IDCOMPROBANTE, IDCOMPLEMENTO, TIPO_ACCION, FECHAHORA, USUARIO, PC, SYSTEMUSER, Periodo, Tipo_Reg, Fecha,
                Mes_Operativo, NUMEROASIENTO, Cuenta, Proceso)
            VALUES (@Tc, @Id, 0, @Accion, GETDATE(), @Usuario, HOST_NAME(), SYSTEM_USER, @Periodo, @TipoReg, @Fecha, @Mes, @Numero, @Cuenta, N'AlfaCore: asientos');
            """, new { Tc = tc, Id = idComprobante, Accion = accion, Usuario = usuario, Periodo = k.Periodo, TipoReg = CodigoPk.Format(k.TipoReg, 4), Fecha = fecha.Date,
                k.Mes, k.Numero, Cuenta = cuenta }, tx, cancellationToken: ct));

    // ── Anular ──────────────────────────────────────────────────────────────────────────────────

    public Task EliminarAsync(AsientoClave k, string huella, CancellationToken ct = default) => EjecutarAsync("Anular", [MenuAsientos], async cn =>
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var u = await UsuarioAsync(cn, tx, ct);
        if (!u.Anular) throw new ReglaException("Tu usuario no tiene permiso para anular asientos (Anular asientos en el usuario).");
        var talonarios = await TalonariosAsync(cn, tx, u.Unidad, ct);
        var previo = await RenglonesAsync(cn, tx, k, bloquear: true, ct);
        var motivo = MotivoSoloLectura(previo.Select(x => (x.Tc, k.TipoReg, x.LivaTipo, x.Conciliado, x.Observaciones, x.TcOrigen, x.Cierre, x.Resumen, x.Caja,
            x.Tc + x.Sucursal + x.Numero + x.Letra, x.MedioDePago)).ToList(), talonarios.Select(t => t.Codigo).ToList());
        if (motivo.Length > 0) throw new ReglaException(motivo);
        if (Huella(previo.Select(x => (x.Cuenta, x.Dh, x.Importe, x.Detalle, x.Fecha))) != huella)
            throw new ReglaException("Otro usuario modificó este asiento. Volvé a abrirlo antes de anularlo.");
        var p = previo[0];
        var cuentas = (await CuentasAsync(cn, tx, ct)).ToDictionary(c => c.Codigo, StringComparer.OrdinalIgnoreCase);
        await ValidarFechaAsync(cn, tx, p.Fecha, previo.Any(x => cuentas.TryGetValue(x.Cuenta, out var c) && c.MedioDePago.Length > 0), ct);
        try
        {
            // Como el legacy: se borra físicamente (no hay contra-asiento). Centros de costo con el mismo subdiario.
            await cn.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO)) = @Periodo AND MES_OPERATIVO = @Mes AND [NUMERO ASIENTO] = @Numero AND ISNULL(LTRIM(RTRIM(TIPO_REG)),'') = @TipoReg;
                IF OBJECT_ID(N'dbo.MV_ASIENTOSCCOSTO', N'U') IS NOT NULL
                    DELETE FROM dbo.MV_ASIENTOSCCOSTO WHERE LTRIM(RTRIM(PERIODO)) = @Periodo AND MES_OPERATIVO = @Mes AND [NUMERO ASIENTO] = @Numero AND ISNULL(LTRIM(RTRIM(TIPO_REG)),'') = @TipoReg;
                """, new { Periodo = k.Periodo.Trim(), k.Mes, k.Numero, TipoReg = k.TipoReg.Trim() }, tx, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number is 50000 or 3609)
        {
            throw new ReglaException("La base no permite eliminar asientos (configuración BLOQUEA_ELIMINACION del Alfa Gestión).");
        }
        await AccionAsync(cn, tx, "B", p.Tc, p.Sucursal + p.Numero + p.Letra, k, p.Fecha, p.Cuenta, users.GetCurrentUserName(), ct);
        await tx.CommitAsync(ct);
        await events.LogAuditAsync("Asientos", "Anulacion", "MV_ASIENTOS", k.Texto,
            $"Anuló el asiento {k.Texto} ({p.Tc} {p.Sucursal}-{p.Numero}) por {previo.Where(x => x.Dh == "D").Sum(x => x.Importe):N2}.",
            new { k.Periodo, k.Mes, k.Numero, renglones = previo.Select(x => new { x.Cuenta, x.Dh, x.Importe, x.Detalle }) }, CancellationToken.None);
        return true;
    }, ct);

    // ── Control con ARCA (asientos de facturas) ──────────────────────────────────────────────────

    public Task<AsientoControlArca> ControlArcaAsync(AsientoClave clave, CancellationToken ct = default) => EjecutarAsync("ControlArca", MenusConsulta, async cn =>
    {
        var r = await RenglonesAsync(cn, null, clave, false, ct);
        var cab = r.FirstOrDefault(x => x.LivaTipo.Length > 0);
        if (cab is null) return new AsientoControlArca { Mensaje = "No es el asiento de una factura." };
        var recibidos = cab.LivaTipo.Equals("COMPRAS", StringComparison.OrdinalIgnoreCase);
        var config = await ArcaDescargaService.LeerConfigAsync(cn, ct);
        var periodo = cab.Fecha.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var archivo = ArcaDescargaService.Listar(config.Carpeta, config.CuitRepresentado).FirstOrDefault(x => x.Recibidos == recibidos && x.Periodo == periodo);
        if (archivo is null)
            return new AsientoControlArca
            {
                Estado = "SIN_ARCHIVO",
                Mensaje = $"No hay Mis Comprobantes {(recibidos ? "recibidos" : "emitidos")} de {cab.Fecha:MM/yyyy} bajado. Bajalo desde Control ARCA y volvé a controlar."
            };
        var arca = ControlArcaService.Leer(await File.ReadAllBytesAsync(Path.Combine(config.Carpeta, archivo.Nombre), ct), archivo.Nombre);
        return ControlarComprobante(arca, cab.Tc, cab.Letra, cab.Sucursal, cab.Numero, cab.CabCuit, cab.LivaTotal, cab.Cae, cab.Fecha, archivo.Nombre, recibidos);
    }, ct);

    internal static AsientoControlArca ControlarComprobante(ArcaMisComprobantesArchivo arca, string tc, string letra, string sucursal, string numero, string cuit,
        decimal total, string cae, DateTime fecha, string archivo, bool recibidos)
    {
        var clase = tc.ToUpperInvariant() switch { "NC" or "NCC" or "NCE" => "NC", "ND" or "NDC" or "NDE" => "ND", _ => "FC" };
        _ = int.TryParse(sucursal, out var pv); _ = long.TryParse(numero, out var nro);
        var doc = new string((cuit ?? "").Where(char.IsDigit).ToArray());
        var c = arca.Comprobantes.FirstOrDefault(x => x.Clase == clase && (x.Letra == letra || x.Letra.Length == 0) && x.PuntoVenta == pv
            && nro >= x.NumeroDesde && nro <= x.NumeroHasta && (!recibidos || doc.Length == 0 || x.DocContraparte == doc));
        if (c is null)
            return new AsientoControlArca
            {
                Estado = "NO_INFORMADO", Archivo = archivo,
                Mensaje = recibidos ? "ARCA no informa este comprobante como recibido: revisá el CUIT, el número o si es un comprobante no electrónico."
                                    : "ARCA no informa este comprobante como emitido (¿sin CAE o de otro punto de venta?)."
            };
        var obs = new List<string>();
        var desde = c.NumeroDesde != c.NumeroHasta;
        if (!desde && total != 0 && Math.Abs(Math.Abs(total) - c.TotalPesos) > 1m) obs.Add($"El total difiere: ARCA {c.TotalPesos:N2}, asiento {Math.Abs(total):N2}.");
        if (cae.Trim().Length > 0 && c.Cae.Trim().Length > 0 && cae.Trim() != c.Cae.Trim()) obs.Add($"El CAE difiere: ARCA {c.Cae}, sistema {cae}.");
        if (c.Fecha.Date != fecha.Date && (c.Fecha.Year != fecha.Year || c.Fecha.Month != fecha.Month)) obs.Add($"ARCA lo informa en {c.Fecha:dd/MM/yyyy}.");
        if (c.Total == 0) obs.Add("ARCA lo informa con importe 0.");
        return new AsientoControlArca
        {
            Estado = obs.Count == 0 ? "COINCIDE" : "DIFIERE", Archivo = archivo, TotalArca = desde ? null : c.TotalPesos, CaeArca = c.Cae, FechaArca = c.Fecha,
            Observaciones = obs,
            Mensaje = obs.Count == 0 ? $"Coincide con Mis Comprobantes de ARCA{(desde ? " (informado en un rango agrupado)" : "")}." : "Hay diferencias con Mis Comprobantes de ARCA."
        };
    }

    // ── Ejecución ───────────────────────────────────────────────────────────────────────────────

    private async Task<T> EjecutarAsync<T>(string accion, IReadOnlyList<string> menus, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var sesion)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            var permitido = false;
            foreach (var m in menus) if (await permissions.HasAccessAsync(m, ct)) { permitido = true; break; }
            if (!permitido) throw new ReglaException(menus.Count == 1 ? "Tu usuario no tiene permiso para cargar asientos (Carga de asientos)." : "Tu usuario no tiene permiso para ver los asientos.");
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            var resultado = await operacion(cn);
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != sesion) throw new OperationCanceledException("La sesión cambió.");
            return resultado;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (SqlException ex) when (ex.Number == 50000)
        {
            // Mensajes de los triggers del Alfa Gestión (MV_Asientos_ValidaFechas): se muestran tal cual.
            throw new ReglaException(ex.Message);
        }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("Asientos", accion, ex, "No se pudo completar la operación con el asiento.", ct: ct);
            throw new AppUserFacingException("No se pudo completar la operación con el asiento.", codigo, ex);
        }
    }
}

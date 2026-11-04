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
    Task<AsientoModelo> ModeloAsync(string nombre, CancellationToken ct = default);
    /// <summary>Crea o reemplaza un modelo (MA_MODELOS) con los renglones dados.</summary>
    Task GuardarModeloAsync(AsientoModelo modelo, CancellationToken ct = default);
    Task EliminarModeloAsync(string nombre, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, decimal>> CotizacionesAsync(DateTime fecha, CancellationToken ct = default);
    Task<IReadOnlyList<ChequeCartera>> ChequesEnCarteraAsync(string cuenta, CancellationToken ct = default);
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
    internal const string MenuAsientos = "D9550", MenuModelos = "D9540";
    /// <summary>Para ver asientos alcanza con el diario; para cargar hace falta la opción del legacy.</summary>
    internal static readonly string[] MenusConsulta = [MenuAsientos, "D957002", "D957003"];
    /// <summary>Talonarios contables que no son asientos manuales (liquidación de tarjetas y asiento resumen).</summary>
    internal static readonly string[] TalonariosExcluidos = ["LT", "RS", "ÿRS"];
    /// <summary>Medios de pago que piden datos aparte y todavía no se cargan acá (cupones de tarjeta): se registran desde Caja o el legacy.</summary>
    /// <summary>Clearing de un cheque (horas), como el legacy (FrmCpteBancario).</summary>
    internal static readonly int[] Clearings = [0, 24, 48, 72, 96, 120, 144, 168];
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
                   CONVERT(bit, CASE WHEN ISNULL(PideVencimiento,0) <> 0 THEN 1 ELSE 0 END) PideVencimiento,
                   CASE WHEN LTRIM(RTRIM(ISNULL(Moneda,''))) = '' THEN N'1' ELSE LTRIM(RTRIM(Moneda)) END Moneda, LTRIM(RTRIM(ISNULL(TipoVista,''))) TipoVista
            FROM dbo.MA_CUENTAS
            WHERE ISNULL(TITULO,0) = 0 AND ISNULL(Dada_De_Baja,0) = 0 AND ISNULL(BLOQUEO,0) = 0
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
            Monedas = (await cn.QueryAsync<AsientoMoneda>(new CommandDefinition(
                "IF OBJECT_ID(N'dbo.TA_MONEDAS', N'U') IS NULL SELECT N'1' Codigo, N'$' Descripcion ELSE SELECT LTRIM(RTRIM(CODIGO)) Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion FROM dbo.TA_MONEDAS ORDER BY CODIGO;",
                cancellationToken: ct))).ToList(),
            Bancos = (await cn.QueryAsync<ChequeraBanco>(new CommandDefinition(
                "IF OBJECT_ID(N'dbo.V_TA_Bancos', N'U') IS NULL SELECT TOP 0 N'' IdBanco, N'' Descripcion ELSE SELECT LTRIM(RTRIM(IdBanco)) IdBanco, LTRIM(RTRIM(ISNULL(Descripcion,''))) Descripcion FROM dbo.V_TA_Bancos ORDER BY Descripcion;",
                cancellationToken: ct))).ToList(),
            Centros = await CentrosAsync(cn, null, ct), CentrosPorCuenta = await CentrosPorCuentaAsync(cn, null, u.Unidad, ct), Modelos = await ModelosAsync(cn, ct),
            PuedeGuardarModelos = await permissions.HasAccessAsync(MenuModelos, ct) || (cargar && u.Modificar),
            UnidadUsuario = u.Unidad, PuedeCargar = cargar, PuedeModificar = cargar && u.Modificar, PuedeAnular = cargar && u.Anular
        };
    }, ct);

    private static async Task<IReadOnlyList<CompraCargaOpcion>> CentrosAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => (await cn.QueryAsync<CompraCargaOpcion>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.TA_CCOSTO', N'U') IS NULL SELECT TOP 0 N'' Codigo, N'' Descripcion
            ELSE SELECT LTRIM(RTRIM(CODIGO)) Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion FROM dbo.TA_CCOSTO WHERE ISNULL(TITULO,0) = 0 ORDER BY CODIGO;
            """, transaction: tx, cancellationToken: ct))).ToList();

    /// <summary>
    /// Distribución sugerida por cuenta, como el legacy (ClsCCosto): MA_CUENTASCCOSTO (o MA_CUENTASCCOSTO_UNEG para la unidad del usuario
    /// cuando la cuenta es variable por unidad) y, si no tiene, la plantilla MA_CUENTAS_CCDIST indicada en MA_CUENTAS.CC_DIST.
    /// </summary>
    private static async Task<IReadOnlyList<AsientoCentroDefault>> CentrosPorCuentaAsync(SqlConnection cn, SqlTransaction? tx, string unidad, CancellationToken ct)
        => (await cn.QueryAsync<AsientoCentroDefault>(new CommandDefinition("""
            DECLARE @r TABLE (Cuenta nvarchar(20), CCosto nvarchar(10), Porcentaje decimal(9,4), PideConfirmacion bit);
            IF OBJECT_ID(N'dbo.MA_CUENTASCCOSTO', N'U') IS NOT NULL
            BEGIN
                INSERT INTO @r
                SELECT LTRIM(RTRIM(c.CODIGO)), LTRIM(RTRIM(c.CCOSTO)), CONVERT(decimal(9,4), ISNULL(c.PORCENTAJE,0)), CONVERT(bit, ISNULL(c.PideConfirmacion,0))
                FROM dbo.MA_CUENTASCCOSTO c
                WHERE NOT (ISNULL(c.VariablePorUD,0) = 1 AND OBJECT_ID(N'dbo.MA_CUENTASCCOSTO_UNEG', N'U') IS NOT NULL);
                IF OBJECT_ID(N'dbo.MA_CUENTASCCOSTO_UNEG', N'U') IS NOT NULL
                    INSERT INTO @r
                    SELECT LTRIM(RTRIM(u.CODIGO)), LTRIM(RTRIM(u.CCOSTO)), CONVERT(decimal(9,4), ISNULL(u.PORCENTAJE,0)), 0
                    FROM dbo.MA_CUENTASCCOSTO_UNEG u
                    WHERE LTRIM(RTRIM(u.UNEGOCIO)) = @Unidad
                      AND EXISTS (SELECT 1 FROM dbo.MA_CUENTASCCOSTO c WHERE c.CODIGO = u.CODIGO AND ISNULL(c.VariablePorUD,0) = 1);
            END
            IF OBJECT_ID(N'dbo.MA_CUENTAS_CCDIST', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MA_CUENTAS', N'CC_DIST') IS NOT NULL
                INSERT INTO @r
                SELECT LTRIM(RTRIM(m.CODIGO)), LTRIM(RTRIM(d.CCosto)), CONVERT(decimal(9,4), ISNULL(d.Porcentaje,0)), CONVERT(bit, ISNULL(d.PideConfirmacion,0))
                FROM dbo.MA_CUENTAS m JOIN dbo.MA_CUENTAS_CCDIST d ON LTRIM(RTRIM(d.Codigo)) = LTRIM(RTRIM(m.CC_DIST))
                WHERE ISNULL(LTRIM(RTRIM(m.CC_DIST)),'') <> '' AND NOT EXISTS (SELECT 1 FROM @r x WHERE x.Cuenta = LTRIM(RTRIM(m.CODIGO)));
            SELECT Cuenta, CCosto, Porcentaje, PideConfirmacion FROM @r ORDER BY Cuenta, Porcentaje DESC, CCosto;
            """, new { Unidad = unidad.Trim() }, tx, cancellationToken: ct))).ToList();

    /// <summary>Importes de cada centro a partir del porcentaje; el redondeo va al centro de mayor porcentaje (cierra con el renglón).</summary>
    internal static void DistribuirImportes(AsientoRenglon r)
    {
        var importe = r.Debe + r.Haber;
        foreach (var c in r.Centros) c.Importe = Math.Round(importe * c.Porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
        if (r.Centros.Count > 0 && r.Centros.Sum(c => c.Porcentaje) == 100m)
            r.Centros.OrderByDescending(c => c.Porcentaje).First().Importe += importe - r.Centros.Sum(c => c.Importe);
    }

    /// <summary>Sugerencia para un renglón: los centros con porcentaje de la cuenta (los de 0 % quedan como opción en la pantalla).</summary>
    internal static List<AsientoCentro> CentrosSugeridos(string cuenta, IEnumerable<AsientoCentroDefault> defaults, IReadOnlyList<CompraCargaOpcion> centros)
        => defaults.Where(d => d.Cuenta.Equals(cuenta.Trim(), StringComparison.OrdinalIgnoreCase) && d.Porcentaje > 0)
            .Select(d => new AsientoCentro { CCosto = d.CCosto, Porcentaje = d.Porcentaje, Descripcion = centros.FirstOrDefault(c => c.Codigo == d.CCosto)?.Descripcion ?? "" })
            .ToList();

    // ── Moneda extranjera y cheques ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Cotización de cada moneda para la fecha (TA_COTIZACION: una fila por día, columnas MONEDA1..MONEDAn). El legacy busca el día exacto;
    /// acá se toma la última cargada hasta esa fecha, y en la pantalla se puede corregir.
    /// </summary>
    internal static async Task<Dictionary<string, decimal>> CotizacionesAsync(SqlConnection cn, SqlTransaction? tx, DateTime fecha, CancellationToken ct)
    {
        var res = new Dictionary<string, decimal> { ["1"] = 1m };
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.TA_COTIZACION', N'U') IS NULL THEN 0 ELSE 1 END;", transaction: tx, cancellationToken: ct)) == 0)
            return res;
        await using var cmd = new SqlCommand("SELECT TOP (1) * FROM dbo.TA_COTIZACION WHERE FECHA_HORA < DATEADD(day, 1, @Fecha) ORDER BY FECHA_HORA DESC;", cn, tx);
        cmd.Parameters.AddWithValue("@Fecha", fecha.Date);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return res;
        for (var i = 0; i < r.FieldCount; i++)
        {
            var nombre = r.GetName(i);
            if (nombre.StartsWith("MONEDA", StringComparison.OrdinalIgnoreCase) && int.TryParse(nombre[6..], out var n) && !await r.IsDBNullAsync(i, ct)
                && Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture) is var v && v > 0)
                res[n.ToString(CultureInfo.InvariantCulture)] = v;
        }
        return res;
    }

    public Task<IReadOnlyDictionary<string, decimal>> CotizacionesAsync(DateTime fecha, CancellationToken ct = default) => EjecutarAsync("Cotizaciones", MenusConsulta,
        async cn => (IReadOnlyDictionary<string, decimal>)await CotizacionesAsync(cn, null, fecha, ct), ct);

    private const string CarteraSql = """
        SELECT LTRIM(RTRIM(c.CUENTA)) Cuenta, LTRIM(RTRIM(c.NroComprobanteBancario)) NroComprobante, LTRIM(RTRIM(c.IdBanco)) IdBanco,
               LTRIM(RTRIM(ISNULL(c.BANCO_DESCRIPCION,''))) Banco, c.Saldo Importe, c.VENCIMIENTO Vencimiento, LTRIM(RTRIM(ISNULL(c.TJ_Observaciones,''))) Cuit,
               ISNULL(c.Clearing,0) Clearing, c.FECHA Fecha,
               LTRIM(RTRIM(c.TC)) + ' ' + LTRIM(RTRIM(c.SUCURSAL)) + '-' + LTRIM(RTRIM(c.NUMERO)) Origen,
               LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(200), c.DETALLE),''))) Detalle
        FROM dbo.V_ChequesEnCartera c
        """;

    /// <summary>Cheques de terceros en cartera de una cuenta (lo que se puede entregar, depositar o rechazar).</summary>
    public Task<IReadOnlyList<ChequeCartera>> ChequesEnCarteraAsync(string cuenta, CancellationToken ct = default) => EjecutarAsync("Cartera", MenusConsulta, async cn =>
        (IReadOnlyList<ChequeCartera>)(await cn.QueryAsync<ChequeCartera>(new CommandDefinition(CarteraSql + " WHERE c.CUENTA = @Cuenta ORDER BY c.VENCIMIENTO, c.FECHA;",
            new { Cuenta = (cuenta ?? "").Trim() }, commandTimeout: 120, cancellationToken: ct))).ToList(), ct);

    /// <summary>Datos del cheque y moneda que no dependen de la base.</summary>
    internal static void ValidarChequeYMoneda(AsientoRenglon r, AsientoCuenta cuenta, int n, IReadOnlyCollection<string> bancos, string? monedaAsiento = null, decimal cotizacionAsiento = 1m)
    {
        if (monedaAsiento is { Length: > 0 } and not "1")
        {
            // Asiento cargado en otra moneda: todos los renglones llevan la moneda y cotización del encabezado (la conversión a pesos la hace ValidarRenglones).
            if (cuenta.EsCheque) throw new ReglaException($"Renglón {n}: los cheques se cargan en un asiento en pesos (o en una cuenta de cheques en moneda extranjera).");
            r.Moneda = monedaAsiento; r.Cotizacion = cotizacionAsiento; r.ImporteMoneda = r.Debe + r.Haber;
        }
        else if (cuenta.EnMonedaExtranjera)
        {
            if (r.Cotizacion <= 0 || r.ImporteMoneda <= 0) throw new ReglaException($"Renglón {n}: {cuenta.Descripcion} es en moneda extranjera: cargá la cantidad y la cotización.");
            if (decimal.Round(r.ImporteMoneda, 2) != r.ImporteMoneda || decimal.Round(r.Cotizacion, 6) != r.Cotizacion) throw new ReglaException($"Renglón {n}: revisá la cantidad y la cotización.");
            var pesos = Math.Round(r.ImporteMoneda * r.Cotizacion, 2, MidpointRounding.AwayFromZero);
            // El importe en pesos sale siempre de cantidad × cotización (como FrmCotiz del legacy).
            if (r.Haber > 0) r.Haber = pesos; else r.Debe = pesos;
            r.Moneda = cuenta.Moneda.Trim();
        }
        else { r.Moneda = "1"; r.Cotizacion = 1; r.ImporteMoneda = 0; }
        if (!cuenta.EsCheque) { r.Cheque = null; return; }
        var ch = r.Cheque ?? throw new ReglaException($"Renglón {n}: {cuenta.Descripcion} es de cheques: cargá los datos del cheque.");
        ch.Serie = (ch.Serie ?? "").Trim().ToUpperInvariant(); ch.Numero = (ch.Numero ?? "").Trim(); ch.IdBanco = (ch.IdBanco ?? "").Trim();
        ch.Cuit = new string((ch.Cuit ?? "").Where(char.IsDigit).ToArray());
        if (ch.Serie.Length > 1) throw new ReglaException($"Renglón {n}: la serie del cheque es una sola letra.");
        if (ch.Numero.Length is < 1 or > 20 || !ch.Numero.All(char.IsDigit)) throw new ReglaException($"Renglón {n}: cargá el número del cheque (solo dígitos).");
        if (ch.IdBanco.Length == 0 || (bancos.Count > 0 && !bancos.Contains(ch.IdBanco))) throw new ReglaException($"Renglón {n}: elegí el banco del cheque.");
        if (ch.Vencimiento is null) throw new ReglaException($"Renglón {n}: cargá la fecha de pago del cheque.");
        if (!Clearings.Contains(ch.Clearing)) throw new ReglaException($"Renglón {n}: el clearing es 0, 24, 48, 72, 96, 120, 144 o 168 horas.");
        if (ch.Cuit.Length is not (0 or 11)) throw new ReglaException($"Renglón {n}: el CUIT del librador tiene 11 dígitos.");
    }

    /// <summary>
    /// Controles de cheques contra la base (FrmCpteBancario del legacy), sin contar el propio asiento cuando se modifica:
    /// entrada (debe) que no esté ya en cartera; salida de terceros (haber) que esté en cartera de esa cuenta y por el mismo importe;
    /// salida propia (cuenta bancaria) dentro de una chequera del banco y serie, y no emitida antes.
    /// </summary>
    private static async Task ValidarChequesAsync(SqlConnection cn, SqlTransaction tx, IReadOnlyList<AsientoRenglon> renglones, IReadOnlyDictionary<string, AsientoCuenta> cuentas,
        AsientoClave? propio, CancellationToken ct)
    {
        var excluir = propio is null ? "" : " AND NOT (LTRIM(RTRIM(c.PERIODO)) = @P AND c.MES_OPERATIVO = @M AND c.[NUMERO ASIENTO] = @N)";
        var claves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < renglones.Count; i++)
        {
            var r = renglones[i]; var n = i + 1;
            if (r.Cheque is not { } ch) continue;
            var cuenta = cuentas[r.Cuenta.Trim()];
            if (!claves.Add($"{ch.IdBanco}|{ch.NroComprobante}")) throw new ReglaException($"Renglón {n}: el cheque {ch.NroComprobante} ya está cargado en este asiento.");
            var p = new DynamicParameters(new { Nro = ch.NroComprobante, Banco = CodigoPk.Format(ch.IdBanco, 4), Cuenta = r.Cuenta.Trim(), Importe = r.Debe + r.Haber,
                P = propio?.Periodo.Trim(), M = propio?.Mes, N = propio?.Numero });
            if (r.Debe > 0)
            {
                var enCartera = await cn.QueryFirstOrDefaultAsync<(string Cuenta, string Origen)?>(new CommandDefinition($"""
                    SELECT TOP (1) LTRIM(RTRIM(c.CUENTA)), LTRIM(RTRIM(c.TC)) + ' ' + LTRIM(RTRIM(c.SUCURSAL)) + '-' + LTRIM(RTRIM(c.NUMERO))
                    FROM dbo.V_ChequesEnCartera c WHERE LTRIM(RTRIM(c.NroComprobanteBancario)) = @Nro AND c.IdBanco = @Banco{excluir};
                    """, p, tx, cancellationToken: ct));
                if (enCartera is { } e) throw new ReglaException($"Renglón {n}: el cheque {ch.NroComprobante} de ese banco ya está en cartera (entró con {e.Origen}).");
            }
            else if (!cuenta.Bancaria)
            {
                var importes = (await cn.QueryAsync<decimal>(new CommandDefinition($"""
                    SELECT c.Saldo FROM dbo.V_ChequesEnCartera c
                    WHERE LTRIM(RTRIM(c.NroComprobanteBancario)) = @Nro AND c.IdBanco = @Banco AND c.CUENTA = @Cuenta;
                    """, p, tx, cancellationToken: ct))).ToList();
                // Al modificar, el cheque que este mismo asiento entregaba vuelve a estar en cartera.
                if (propio is not null && importes.Count == 0)
                    importes = (await cn.QueryAsync<decimal>(new CommandDefinition("""
                        SELECT c.IMPORTE FROM dbo.MV_ASIENTOS c
                        WHERE c.[DEBE-HABER] = 'H' AND LTRIM(RTRIM(c.NroComprobanteBancario)) = @Nro AND c.IdBanco = @Banco AND c.CUENTA = @Cuenta
                          AND LTRIM(RTRIM(c.PERIODO)) = @P AND c.MES_OPERATIVO = @M AND c.[NUMERO ASIENTO] = @N;
                        """, p, tx, cancellationToken: ct))).ToList();
                if (importes.Count == 0) throw new ReglaException($"Renglón {n}: el cheque {ch.NroComprobante} no está en la cartera de {cuenta.Descripcion}.");
                if (!importes.Contains(r.Haber)) throw new ReglaException($"Renglón {n}: el cheque {ch.NroComprobante} está en cartera por {importes[0]:N2}: el importe tiene que ser el mismo.");
            }
            else
            {
                if (!long.TryParse(ch.Numero, out var numero)) throw new ReglaException($"Renglón {n}: el número del cheque no es válido.");
                var enChequera = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT COUNT(*) FROM dbo.V_TA_CTRL_CHEQUERAS WHERE IDBANCO = @Banco AND LTRIM(RTRIM(SERIE)) = @Serie AND @Numero BETWEEN NUMERO_DESDE AND NUMERO_HASTA;
                    """, new { Banco = CodigoPk.Format(ch.IdBanco, 4), ch.Serie, Numero = (double)numero }, tx, cancellationToken: ct));
                if (enChequera == 0) throw new ReglaException($"Renglón {n}: el cheque {ch.NroComprobante} no está en ninguna chequera de ese banco y serie (Chequeras).");
                var emitido = await cn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition($"""
                    SELECT TOP (1) LTRIM(RTRIM(c.TC)) + ' ' + LTRIM(RTRIM(c.SUCURSAL)) + '-' + LTRIM(RTRIM(c.NUMERO)) + ' del ' + CONVERT(varchar(10), c.FECHA, 103)
                    FROM dbo.V_ChequesFirmados c WHERE c.CUENTA = @Cuenta AND LTRIM(RTRIM(c.NroComprobanteBancario)) = @Nro{excluir};
                    """, p, tx, cancellationToken: ct));
                if (emitido is not null) throw new ReglaException($"Renglón {n}: el cheque propio {ch.NroComprobante} ya fue emitido ({emitido}).");
            }
        }
    }

    /// <summary>Un asiento por el que entró un cheque que después salió (entregado, depositado) no se modifica ni se anula.</summary>
    private static async Task ValidarChequesSinSalidaAsync(SqlConnection cn, SqlTransaction tx, AsientoClave k, CancellationToken ct)
    {
        var salida = await cn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition("""
            SELECT TOP (1) LTRIM(RTRIM(e.NroComprobanteBancario)) + ' (salió con ' + LTRIM(RTRIM(s.TC)) + ' ' + LTRIM(RTRIM(s.SUCURSAL)) + '-' + LTRIM(RTRIM(s.NUMERO)) + ')'
            FROM dbo.MV_ASIENTOS e
            JOIN dbo.MV_ASIENTOS s ON s.[DEBE-HABER] = 'H' AND s.CUENTA = e.CUENTA AND s.NroComprobanteBancario = e.NroComprobanteBancario AND s.IdBanco = e.IdBanco
                AND s.IMPORTE = e.IMPORTE AND NOT (s.PERIODO = e.PERIODO AND s.MES_OPERATIVO = e.MES_OPERATIVO AND s.[NUMERO ASIENTO] = e.[NUMERO ASIENTO])
            WHERE e.[DEBE-HABER] = 'D' AND ISNULL(e.IdBanco,'') <> '' AND ISNULL(e.NroComprobanteBancario,'') <> ''
              AND LTRIM(RTRIM(e.PERIODO)) = @P AND e.MES_OPERATIVO = @M AND e.[NUMERO ASIENTO] = @N AND ISNULL(LTRIM(RTRIM(e.TIPO_REG)),'') = @T;
            """, new { P = k.Periodo.Trim(), M = k.Mes, N = k.Numero, T = k.TipoReg.Trim() }, tx, cancellationToken: ct));
        if (salida is not null) throw new ReglaException($"El cheque {salida} ya no está en cartera: el asiento por el que entró no se puede modificar ni anular.");
    }

    // ── Modelos de asiento (MA_MODELOS, D9540) ───────────────────────────────────────────────────

    /// <summary>Modelos de asientos directos (sin subdiario, ni libro de IVA, ni liquidación de tarjetas).</summary>
    private static async Task<IReadOnlyList<string>> ModelosAsync(SqlConnection cn, CancellationToken ct)
        => (await cn.QueryAsync<string>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.MA_MODELOS', N'U') IS NULL SELECT TOP 0 N''
            ELSE SELECT LTRIM(RTRIM(NOMBRE)) FROM dbo.MA_MODELOS
                 WHERE LTRIM(RTRIM(ISNULL(TIPO_REG,''))) = '' AND ISNULL(AlLibroDeIva,0) = 0 AND ISNULL(EsLiqTarjetas,0) = 0
                 GROUP BY LTRIM(RTRIM(NOMBRE)) ORDER BY 1;
            """, cancellationToken: ct))).ToList();

    private sealed class ModeloSql
    {
        public string Cuenta { get; set; } = ""; public string Dh { get; set; } = ""; public decimal Importe { get; set; } public string Detalle { get; set; } = "";
        public string Tc { get; set; } = ""; public bool ModificaTc { get; set; } public bool ModificaCuentas { get; set; } public bool AgregaCuentas { get; set; }
        public bool Especial { get; set; }
    }

    private static async Task<List<ModeloSql>> ModeloFilasAsync(SqlConnection cn, SqlTransaction? tx, string nombre, CancellationToken ct)
        => (await cn.QueryAsync<ModeloSql>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CUENTA)) Cuenta, [DEBE-HABER] Dh, ISNULL(IMPORTE,0) Importe, LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(200), DETALLE),''))) Detalle,
                   LTRIM(RTRIM(ISNULL(TCDefault,''))) Tc, CONVERT(bit, ISNULL(ModificaTC,1)) ModificaTc, CONVERT(bit, ISNULL(ModificaCtas,1)) ModificaCuentas,
                   CONVERT(bit, ISNULL(AgregaCtas,1)) AgregaCuentas,
                   CONVERT(bit, CASE WHEN ISNULL(AlLibroDeIva,0) <> 0 OR ISNULL(EsLiqTarjetas,0) <> 0 OR LTRIM(RTRIM(ISNULL(TIPO_REG,''))) <> '' THEN 1 ELSE 0 END) Especial
            FROM dbo.MA_MODELOS WHERE LTRIM(RTRIM(NOMBRE)) = @Nombre ORDER BY SECUENCIA;
            """, new { Nombre = nombre.Trim() }, tx, cancellationToken: ct))).ToList();

    public Task<AsientoModelo> ModeloAsync(string nombre, CancellationToken ct = default) => EjecutarAsync("Modelo", [MenuAsientos], async cn =>
    {
        var filas = await ModeloFilasAsync(cn, null, nombre, ct);
        if (filas.Count == 0) throw new ReglaException("El modelo no existe.");
        if (filas.Any(f => f.Especial)) throw new ReglaException("Ese modelo es de libro de IVA o de liquidación de tarjetas: se usa en el Alfa Gestión.");
        var cuentas = (await CuentasAsync(cn, null, ct)).ToDictionary(c => c.Codigo, StringComparer.OrdinalIgnoreCase);
        var f0 = filas[0];
        return new AsientoModelo
        {
            Nombre = nombre.Trim(), Tc = f0.Tc, ModificaTc = f0.ModificaTc, ModificaCuentas = f0.ModificaCuentas, AgregaCuentas = f0.AgregaCuentas,
            Renglones = filas.Select(f => new AsientoRenglon
            {
                Cuenta = f.Cuenta, CuentaNombre = cuentas.TryGetValue(f.Cuenta, out var c) ? c.Descripcion : "(cuenta no disponible)",
                Detalle = f.Detalle.TrimEnd('.'), Debe = f.Dh == "D" ? f.Importe : 0, Haber = f.Dh == "D" ? 0 : f.Importe,
                DeModelo = true, CuentaFija = !f0.ModificaCuentas, AlHaber = f.Dh != "D"
            }).ToList()
        };
    }, ct);

    public Task GuardarModeloAsync(AsientoModelo m, CancellationToken ct = default) => EjecutarAsync("GuardarModelo", [MenuAsientos, MenuModelos], async cn =>
    {
        var nombre = (m.Nombre ?? "").Trim().ToUpperInvariant();
        if (nombre.Length is < 3 or > 50) throw new ReglaException("Poné un nombre al modelo (entre 3 y 50 letras).");
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var u = await UsuarioAsync(cn, tx, ct);
        if (!(await permissions.HasAccessAsync(MenuModelos, ct) || (await permissions.HasAccessAsync(MenuAsientos, ct) && u.Modificar)))
            throw new ReglaException("Tu usuario no tiene permiso para guardar modelos de asientos.");
        var cuentas = (await CuentasAsync(cn, tx, ct)).ToDictionary(c => c.Codigo, StringComparer.OrdinalIgnoreCase);
        var renglones = m.Renglones.Where(r => r.Cuenta.Trim().Length > 0).ToList();
        if (renglones.Count < 2) throw new ReglaException("El modelo necesita al menos dos cuentas.");
        foreach (var r in renglones)
        {
            if (!cuentas.ContainsKey(r.Cuenta.Trim())) throw new ReglaException($"La cuenta {r.Cuenta.Trim()} no es imputable o no está activa.");
            if (r.Debe < 0 || r.Haber < 0 || (r.Debe != 0 && r.Haber != 0)) throw new ReglaException($"La cuenta {r.Cuenta.Trim()}: el importe va en el debe o en el haber.");
        }
        var talonarios = await TalonariosAsync(cn, tx, u.Unidad, ct);
        var tc = talonarios.FirstOrDefault(t => t.Codigo.Equals((m.Tc ?? "").Trim(), StringComparison.OrdinalIgnoreCase))?.Codigo ?? "CC";
        var previo = await ModeloFilasAsync(cn, tx, nombre, ct);
        if (previo.Any(f => f.Especial)) throw new ReglaException("Ya hay un modelo con ese nombre de libro de IVA o de liquidación de tarjetas: elegí otro nombre.");
        // Como el legacy (Modelo.frm): se reemplaza entero por nombre.
        await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.MA_MODELOS WHERE LTRIM(RTRIM(NOMBRE)) = @Nombre;", new { Nombre = nombre }, tx, cancellationToken: ct));
        for (var i = 0; i < renglones.Count; i++)
        {
            var r = renglones[i];
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.MA_MODELOS (TIPO_REG, NOMBRE, CUENTA, [DEBE-HABER], IMPORTE, DETALLE, SECUENCIA, FechaHora_Grabacion, TCDefault, ModificaTC, ModificaCtas,
                    AgregaCtas, AnclarEnMenu, AlLibroDeIva, EsLiqTarjetas)
                VALUES (N'    ', @Nombre, @Cuenta, @Dh, @Importe, @Detalle, @Secuencia, GETDATE(), @Tc, @ModificaTc, @ModificaCuentas, @AgregaCuentas, 0, 0, 0);
                """, new
            {
                Nombre = nombre, Cuenta = r.Cuenta.Trim(), Dh = r.Haber > 0 || (r.Debe == 0 && r.AlHaber) ? "H" : "D", Importe = r.Debe + r.Haber, Detalle = (r.Detalle ?? "").Trim(), Secuencia = i + 1,
                Tc = tc, m.ModificaTc, m.ModificaCuentas, m.AgregaCuentas
            }, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
        await events.LogAuditAsync("Asientos", "GuardarModelo", "MA_MODELOS", nombre, $"Guardó el modelo de asiento {nombre} ({renglones.Count} cuentas).",
            new { nombre, tc, renglones = renglones.Select(r => new { r.Cuenta, r.Debe, r.Haber }) }, CancellationToken.None);
        return true;
    }, ct);

    public Task EliminarModeloAsync(string nombre, CancellationToken ct = default) => EjecutarAsync("EliminarModelo", [MenuAsientos, MenuModelos], async cn =>
    {
        var u = await UsuarioAsync(cn, null, ct);
        if (!(await permissions.HasAccessAsync(MenuModelos, ct) || (await permissions.HasAccessAsync(MenuAsientos, ct) && u.Modificar)))
            throw new ReglaException("Tu usuario no tiene permiso para borrar modelos de asientos.");
        var filas = await ModeloFilasAsync(cn, null, nombre, ct);
        if (filas.Count == 0) return true;
        if (filas.Any(f => f.Especial)) throw new ReglaException("Ese modelo es de libro de IVA o de liquidación de tarjetas: se borra en el Alfa Gestión.");
        await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.MA_MODELOS WHERE LTRIM(RTRIM(NOMBRE)) = @Nombre;", new { Nombre = nombre.Trim() }, cancellationToken: ct));
        await events.LogAuditAsync("Asientos", "EliminarModelo", "MA_MODELOS", nombre.Trim(), $"Borró el modelo de asiento {nombre.Trim()}.", new { nombre }, CancellationToken.None);
        return true;
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
        public decimal Cotizacion { get; set; } public string NroBancario { get; set; } = ""; public string IdBanco { get; set; } = ""; public string Banco { get; set; } = "";
        public int Clearing { get; set; }
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
                   ISNULL(a.LIVA_TOTAL,0) LivaTotal, LTRIM(RTRIM(ISNULL(a.NroCAI,''))) Cae, LTRIM(RTRIM(ISNULL(a.MONEDA,''))) Moneda,
                   ISNULL(a.COTIZACION,1) Cotizacion, LTRIM(RTRIM(ISNULL(a.NroComprobanteBancario,''))) NroBancario, LTRIM(RTRIM(ISNULL(a.IdBanco,''))) IdBanco,
                   LTRIM(RTRIM(ISNULL(b.Descripcion,''))) Banco, ISNULL(a.Clearing,0) Clearing
            FROM dbo.MV_ASIENTOS a {(bloquear ? "WITH (UPDLOCK, HOLDLOCK)" : "")}
            LEFT JOIN dbo.MA_CUENTAS c ON c.CODIGO = a.CUENTA
            LEFT JOIN dbo.V_TA_Bancos b ON b.IdBanco = a.IdBanco
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
        return "";
    }

    internal static string Huella(IEnumerable<(string Cuenta, string Dh, decimal Importe, string Detalle, DateTime Fecha)> r)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", r.Select(x =>
            $"{x.Cuenta}|{x.Dh}|{x.Importe.ToString("0.00", CultureInfo.InvariantCulture)}|{x.Detalle}|{x.Fecha:yyyyMMdd}")))))[..32];

    public Task<AsientoDetalle> GetAsync(AsientoClave clave, CancellationToken ct = default) => EjecutarAsync("Consultar", MenusConsulta, async cn =>
    {
        var u = await UsuarioAsync(cn, null, ct);
        var manuales = (await TalonariosAsync(cn, null, u.Unidad, ct)).Select(t => t.Codigo).ToList();
        var d = Armar(clave, await RenglonesAsync(cn, null, clave, false, ct), manuales);
        await CargarCentrosAsync(cn, d, ct);
        return d;
    }, ct);

    private static async Task CargarCentrosAsync(SqlConnection cn, AsientoDetalle d, CancellationToken ct)
    {
        if (d.Clave is not { } k) return;
        var filas = await cn.QueryAsync<(int Secuencia, string Cuenta, string CCosto, string Descripcion, decimal Porcentaje, decimal Importe)>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.MV_ASIENTOSCCOSTO', N'U') IS NULL SELECT TOP 0 0, N'', N'', N'', CONVERT(decimal(9,4),0), CONVERT(decimal(18,2),0)
            ELSE SELECT CONVERT(int, c.SECUENCIA), LTRIM(RTRIM(c.CUENTA)), LTRIM(RTRIM(c.CCOSTO)), LTRIM(RTRIM(ISNULL(t.DESCRIPCION,''))),
                        CONVERT(decimal(9,4), ISNULL(c.PORCENTAJE,0)), CONVERT(decimal(18,2), ISNULL(c.Importe,0))
                 FROM dbo.MV_ASIENTOSCCOSTO c LEFT JOIN dbo.TA_CCOSTO t ON t.CODIGO = c.CCOSTO
                 WHERE LTRIM(RTRIM(c.PERIODO)) = @Periodo AND c.MES_OPERATIVO = @Mes AND c.[NUMERO ASIENTO] = @Numero
                   AND ISNULL(LTRIM(RTRIM(c.Tipo_Reg)),'') = @TipoReg
                 ORDER BY c.SECUENCIA, c.PORCENTAJE DESC;
            """, new { Periodo = k.Periodo.Trim(), k.Mes, k.Numero, TipoReg = k.TipoReg.Trim() }, cancellationToken: ct));
        // El renglón se reconoce por su orden (SECUENCIA) y cuenta; si se grabó con huecos, por la cuenta.
        foreach (var g in filas.GroupBy(f => (f.Secuencia, f.Cuenta)))
        {
            var r = g.Key.Secuencia >= 1 && g.Key.Secuencia <= d.Renglones.Count && d.Renglones[g.Key.Secuencia - 1].Cuenta == g.Key.Cuenta
                ? d.Renglones[g.Key.Secuencia - 1] : d.Renglones.FirstOrDefault(x => x.Cuenta == g.Key.Cuenta && x.Centros.Count == 0);
            r?.Centros.AddRange(g.Select(f => new AsientoCentro { CCosto = f.CCosto, Descripcion = f.Descripcion, Porcentaje = f.Porcentaje, Importe = f.Importe }));
        }
    }

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
                Debe = x.Dh == "D" ? x.Importe : 0, Haber = x.Dh == "D" ? 0 : x.Importe,
                Moneda = x.Moneda.Length == 0 ? "1" : x.Moneda, Cotizacion = x.Cotizacion <= 0 ? 1 : x.Cotizacion,
                ImporteMoneda = x.Moneda is "" or "1" || x.Cotizacion <= 0 ? 0 : Math.Round(x.Importe / x.Cotizacion, 2),
                Cheque = x.NroBancario.Length == 0 || x.IdBanco.Length == 0 ? null : new AsientoCheque
                {
                    Serie = x.NroBancario.Contains('-') ? x.NroBancario[..x.NroBancario.IndexOf('-')] : "",
                    Numero = x.NroBancario.Contains('-') ? x.NroBancario[(x.NroBancario.IndexOf('-') + 1)..] : x.NroBancario,
                    IdBanco = x.IdBanco, Banco = x.Banco, Vencimiento = x.Vencimiento, Clearing = x.Clearing, Cuit = x.Observaciones
                }
            }).ToList(),
            MotivoSoloLectura = MotivoSoloLectura(r.Select(x => (x.Tc, clave.TipoReg, x.LivaTipo, x.Conciliado, x.Observaciones, x.TcOrigen, x.Cierre, x.Resumen, x.Caja,
                x.Tc + x.Sucursal + x.Numero + x.Letra, x.MedioDePago)).ToList(), manuales),
            LivaTipo = cab.LivaTipo, CuentaComprobante = cab.CabCuenta, NombreComprobante = cab.CabNombre, CuitComprobante = cab.CabCuit,
            TotalComprobante = cab.LivaTotal, Cae = cab.Cae,
            Huella = Huella(r.Select(x => (x.Cuenta, x.Dh, x.Importe, x.Detalle, x.Fecha)))
        };
        d.Editable = d.MotivoSoloLectura.Length == 0;
        DetectarMonedaAsiento(d);
        return d;
    }

    // ── Validación ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Validaciones de renglones que no dependen de la base (las del legacy al grabar).</summary>
    internal static void ValidarRenglones(AsientoDetalle a, IReadOnlyDictionary<string, AsientoCuenta> cuentas, ISet<string> cuentasTerceros,
        IReadOnlyList<CompraCargaOpcion>? centros = null, IReadOnlyList<AsientoCentroDefault>? centrosPorCuenta = null, IReadOnlyCollection<string>? bancos = null)
    {
        var detalle = (a.Detalle ?? "").Trim();
        if (detalle.Length is < 3 or > LargoDetalle - 1) throw new ReglaException("Escribí el detalle del asiento (concepto).");
        var moneda = a.EnOtraMoneda ? a.MonedaAsiento.Trim() : "1";
        if (moneda != "1" && (a.CotizacionAsiento <= 0 || decimal.Round(a.CotizacionAsiento, 6) != a.CotizacionAsiento || a.CotizacionAsiento > 1_000_000m))
            throw new ReglaException("Cargá la cotización de la moneda del asiento.");
        if (moneda == "1") { a.MonedaAsiento = "1"; a.CotizacionAsiento = 1m; }
        // Los renglones de un modelo que quedan sin importe no se graban (como el legacy).
        var renglones = a.Renglones.Where(r => (r.Cuenta.Trim().Length > 0 || r.Debe != 0 || r.Haber != 0) && !(r.DeModelo && r.Debe == 0 && r.Haber == 0)).ToList();
        if (renglones.Count < 2) throw new ReglaException("El asiento necesita al menos dos renglones.");
        if (renglones.Count > MaximoRenglones) throw new ReglaException($"El asiento puede tener hasta {MaximoRenglones} renglones.");
        for (var i = 0; i < renglones.Count; i++)
        {
            var r = renglones[i]; var n = i + 1;
            if (!cuentas.TryGetValue(r.Cuenta.Trim(), out var cuenta))
                throw new ReglaException($"Renglón {n}: la cuenta {r.Cuenta.Trim()} no existe, es un título o está dada de baja o bloqueada.");
            if (r.Debe < 0 || r.Haber < 0) throw new ReglaException($"Renglón {n}: los importes no pueden ser negativos.");
            if ((r.Debe != 0) == (r.Haber != 0)) throw new ReglaException($"Renglón {n}: cargá el importe en el debe o en el haber (uno solo).");
            if (decimal.Round(r.Debe, 2) != r.Debe || decimal.Round(r.Haber, 2) != r.Haber) throw new ReglaException($"Renglón {n}: los importes van con dos decimales.");
            if (r.Debe + r.Haber > 999_999_999_999m) throw new ReglaException($"Renglón {n}: el importe es demasiado grande.");
            if ((r.Detalle ?? "").Trim().Length > LargoDetalle - 1) throw new ReglaException($"Renglón {n}: el concepto es demasiado largo.");
            ValidarChequeYMoneda(r, cuenta, n, bancos ?? [], moneda, a.CotizacionAsiento);
            if (cuenta.PideVencimiento && !cuenta.EsCheque && (a.Vencimiento is null || a.Vencimiento.Value.Date < a.Fecha.Date))
                throw new ReglaException($"La cuenta {cuenta.Descripcion} pide vencimiento: cargalo (no puede ser anterior a la fecha).");
        }
        var repetidas = renglones.Select(r => r.Cuenta.Trim()).Where(cuentasTerceros.Contains).GroupBy(c => c).FirstOrDefault(g => g.Count() > 1);
        if (repetidas is not null) throw new ReglaException($"La cuenta de cliente o proveedor {repetidas.Key} está repetida: va una sola vez por asiento.");
        var debe = renglones.Sum(r => r.Debe); var haber = renglones.Sum(r => r.Haber);
        if (debe <= 0 || debe != haber) throw new ReglaException($"El asiento no balancea: debe {debe:N2} y haber {haber:N2}{(moneda == "1" ? "" : " (en la moneda del asiento)")}.");
        if (moneda != "1") PasarAPesos(renglones, a.CotizacionAsiento);
        for (var i = 0; i < renglones.Count; i++) ValidarCentros(renglones[i], i + 1, centros ?? [], centrosPorCuenta ?? []);
        a.Renglones = renglones;
    }

    /// <summary>
    /// Pasa a pesos un asiento cargado en otra moneda (importe × cotización, como ConvierteMoneda del legacy).
    /// El redondeo de cada renglón puede dejar unos centavos de diferencia: se ajustan en el renglón más grande para que balancee en pesos.
    /// </summary>
    internal static void PasarAPesos(IReadOnlyList<AsientoRenglon> renglones, decimal cotizacion)
    {
        foreach (var r in renglones)
        {
            r.Debe = Math.Round(r.Debe * cotizacion, 2, MidpointRounding.AwayFromZero);
            r.Haber = Math.Round(r.Haber * cotizacion, 2, MidpointRounding.AwayFromZero);
        }
        var dif = renglones.Sum(r => r.Debe) - renglones.Sum(r => r.Haber);
        if (dif > 0) renglones.Where(r => r.Debe > 0).MaxBy(r => r.Debe)!.Debe -= dif;
        else if (dif < 0) renglones.Where(r => r.Haber > 0).MaxBy(r => r.Haber)!.Haber += dif;
    }

    /// <summary>
    /// Asiento leído: si todos los renglones están en la misma moneda extranjera con la misma cotización, se muestra en esa moneda
    /// (debe y haber en la moneda; el centavo que se ajustó al pasar a pesos se vuelve a balancear).
    /// </summary>
    internal static void DetectarMonedaAsiento(AsientoDetalle d)
    {
        var r = d.Renglones;
        if (r.Count == 0 || r.Any(x => x.Moneda.Trim() is "" or "1" || x.Cotizacion <= 0)) return;
        var m = r[0].Moneda.Trim(); var cot = r[0].Cotizacion;
        if (r.Any(x => x.Moneda.Trim() != m || x.Cotizacion != cot)) return;
        d.MonedaAsiento = m; d.CotizacionAsiento = cot;
        foreach (var x in r)
        {
            var v = Math.Round((x.Debe + x.Haber) / cot, 2, MidpointRounding.AwayFromZero);
            if (x.Debe > 0) x.Debe = v; else x.Haber = v;
            x.ImporteMoneda = v;
        }
        var dif = r.Sum(x => x.Debe) - r.Sum(x => x.Haber);
        if (dif != 0 && Math.Abs(dif) <= 0.01m * r.Count)
        {
            if (dif > 0) { var x = r.Where(y => y.Debe > 0).MaxBy(y => y.Debe)!; x.Debe -= dif; x.ImporteMoneda = x.Debe; }
            else { var x = r.Where(y => y.Haber > 0).MaxBy(y => y.Haber)!; x.Haber += dif; x.ImporteMoneda = x.Haber; }
        }
    }

    /// <summary>
    /// Centros de costo de un renglón: optativos como en el legacy; si los hay, centros existentes, sin repetir, que sumen 100 %.
    /// Si el renglón no trae y la cuenta tiene una distribución del 100 % que no pide confirmación, se aplica sola (el legacy no mostraba la ventana).
    /// Los importes se recalculan siempre desde el porcentaje.
    /// </summary>
    internal static void ValidarCentros(AsientoRenglon r, int n, IReadOnlyList<CompraCargaOpcion> centros, IReadOnlyList<AsientoCentroDefault> centrosPorCuenta)
    {
        r.Centros = (r.Centros ?? []).Where(c => c.CCosto.Trim().Length > 0 || c.Porcentaje != 0).ToList();
        if (r.Centros.Count == 0)
        {
            var sugeridos = centrosPorCuenta.Where(d => d.Cuenta.Equals(r.Cuenta.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (sugeridos.Count > 0 && sugeridos.Sum(d => d.Porcentaje) == 100m && !sugeridos.Any(d => d.PideConfirmacion))
                r.Centros = CentrosSugeridos(r.Cuenta, sugeridos, centros);
        }
        if (r.Centros.Count == 0) return;
        foreach (var c in r.Centros)
        {
            c.CCosto = c.CCosto.Trim();
            if (centros.Count > 0 && centros.All(x => x.Codigo != c.CCosto)) throw new ReglaException($"Renglón {n}: el centro de costo {c.CCosto} no existe.");
            if (c.Porcentaje <= 0 || c.Porcentaje > 100 || decimal.Round(c.Porcentaje, 4) != c.Porcentaje) throw new ReglaException($"Renglón {n}: revisá los porcentajes de los centros de costo.");
        }
        if (r.Centros.GroupBy(c => c.CCosto).Any(g => g.Count() > 1)) throw new ReglaException($"Renglón {n}: un centro de costo está repetido.");
        var suma = r.Centros.Sum(c => c.Porcentaje);
        if (Math.Abs(suma - 100m) > 0.01m) throw new ReglaException($"Renglón {n}: los centros de costo tienen que sumar 100 % (suman {suma:0.##} %).");
        if (suma != 100m) r.Centros.OrderByDescending(c => c.Porcentaje).First().Porcentaje += 100m - suma;
        DistribuirImportes(r);
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

    public Task<AsientoDetalle> GuardarAsync(AsientoDetalle original, CancellationToken ct = default) => EjecutarAsync("Guardar", [MenuAsientos], async cn =>
    {
        // Se trabaja sobre una copia: la validación normaliza y pasa a pesos, y si algo falla el editor tiene que quedar como estaba.
        var a = System.Text.Json.JsonSerializer.Deserialize<AsientoDetalle>(System.Text.Json.JsonSerializer.Serialize(original))!;
        if (a.EnOtraMoneda && !(await cn.QueryAsync<string>(new CommandDefinition(
                "IF OBJECT_ID(N'dbo.TA_MONEDAS', N'U') IS NULL SELECT TOP 0 N'' ELSE SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.TA_MONEDAS;", cancellationToken: ct)))
                .Any(m => m.TrimStart('0') == a.MonedaAsiento.Trim().TrimStart('0')))
            throw new ReglaException("La moneda del asiento no existe.");
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var u = await UsuarioAsync(cn, tx, ct);
        var talonarios = await TalonariosAsync(cn, tx, u.Unidad, ct);
        var cuentas = (await CuentasAsync(cn, tx, ct)).ToDictionary(c => c.Codigo, StringComparer.OrdinalIgnoreCase);
        var terceros = (await cn.QueryAsync<string>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.VT_CLIENTES UNION SELECT LTRIM(RTRIM(CODIGO)) FROM dbo.VT_PROVEEDORES;
            """, transaction: tx, commandTimeout: 60, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bancos = (await cn.QueryAsync<string>(new CommandDefinition(
            "IF OBJECT_ID(N'dbo.V_TA_Bancos', N'U') IS NULL SELECT TOP 0 N'' ELSE SELECT LTRIM(RTRIM(IdBanco)) FROM dbo.V_TA_Bancos;", transaction: tx, cancellationToken: ct))).ToHashSet();
        ValidarRenglones(a, cuentas, terceros, await CentrosAsync(cn, tx, ct), await CentrosPorCuentaAsync(cn, tx, u.Unidad, ct), bancos);
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
            await ValidarChequesSinSalidaAsync(cn, tx, k, ct);
            await ValidarChequesAsync(cn, tx, a.Renglones, cuentas, k, ct);
            var p = previo[0];
            (tc, suc, nro, letra, unidad, caja, usuarioGrabo, grabado, apertura) = (p.Tc, p.Sucursal, p.Numero, p.Letra, p.Unidad, p.Caja, p.Usuario, p.Grabado, p.Apertura);
            clave = k;
            await cn.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO)) = @Periodo AND MES_OPERATIVO = @Mes AND [NUMERO ASIENTO] = @Numero AND ISNULL(LTRIM(RTRIM(TIPO_REG)),'') = @TipoReg;
                IF OBJECT_ID(N'dbo.MV_ASIENTOSCCOSTO', N'U') IS NOT NULL
                    DELETE FROM dbo.MV_ASIENTOSCCOSTO WHERE LTRIM(RTRIM(PERIODO)) = @Periodo AND MES_OPERATIVO = @Mes AND [NUMERO ASIENTO] = @Numero AND ISNULL(LTRIM(RTRIM(TIPO_REG)),'') = @TipoReg;
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
            await ValidarChequesAsync(cn, tx, a.Renglones, cuentas, null, ct);
            clave = new AsientoClave(periodo, a.Fecha.Month, await NumeracionAsiento.SiguienteAsync(cn, tx, periodo, a.Fecha.Month, "", ct), "");
        }

        for (var i = 0; i < a.Renglones.Count; i++)
        {
            var r = a.Renglones[i];
            var concepto = (r.Detalle ?? "").Trim();
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.MV_ASIENTOS (CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE, TC, SUCURSAL, NUMERO, LETRA,
                    [DEBE-HABER], IMPORTE, MONEDA, COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO, FechaHora_Grabacion, FechaHora_Modificacion,
                    FechaSubdiario, USUARIO_LOGEADO, IdCajas, UNEGOCIO, VENCIMIENTO, ES_SALDO_APERTURA, NroComprobanteBancario, IdBanco, Clearing, TJ_Observaciones)
                VALUES (@Cuenta, @Secuencia, @Mes, @Asiento, @Fecha, @Detalle, @Tc, @Suc, @Nro, @Letra,
                    @Dh, @Importe, @Moneda, @Cotizacion, @Periodo, @Total, @TipoReg, 0, ISNULL(@Grabado, GETDATE()), @Modificado,
                    @Fecha, @Usuario, @Caja, @Unidad, @Vencimiento, @Apertura, @NroBancario, @IdBanco, @Clearing, @Librador);
                """, new
            {
                Cuenta = r.Cuenta.Trim(), Secuencia = i + 1, clave.Mes, Asiento = clave.Numero, Fecha = a.Fecha.Date,
                // Como el legacy: el concepto del renglón con «.» al final; si no tiene, el detalle del asiento.
                Detalle = concepto.Length > 0 ? concepto + "." : detalle,
                Tc = tc, Suc = suc, Nro = nro, Letra = letra, Dh = r.Debe > 0 ? "D" : "H", Importe = r.Debe > 0 ? r.Debe : r.Haber,
                Moneda = CodigoPk.Format(r.Moneda, 4), r.Cotizacion, Periodo = clave.Periodo, Total = total, TipoReg = CodigoPk.Format(clave.TipoReg, 4),
                NroBancario = r.Cheque?.NroComprobante, IdBanco = r.Cheque is { } chq ? CodigoPk.Format(chq.IdBanco, 4) : null, Clearing = r.Cheque?.Clearing,
                Librador = r.Cheque is { Cuit.Length: > 0 } chl ? chl.Cuit : null,
                Grabado = grabado, Modificado = accion == "M" ? DateTime.Now : (DateTime?)null, Usuario = usuarioGrabo,
                Caja = CodigoPk.Format(caja, 4), Unidad = CodigoPk.Format(unidad, 4), Vencimiento = (r.Cheque?.Vencimiento ?? a.Vencimiento ?? a.Fecha).Date, Apertura = apertura
            }, tx, cancellationToken: ct));
            foreach (var c in r.Centros)
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.MV_ASIENTOSCCOSTO (PERIODO, MES_OPERATIVO, [NUMERO ASIENTO], CUENTA, SECUENCIA, [DEBE-HABER], MONEDA, CCOSTO, PORCENTAJE,
                        FechaHora_Grabacion, Tipo_Reg, Importe, Fecha, UNEGOCIO)
                    VALUES (@Periodo, @Mes, @Asiento, @Cuenta, @Secuencia, @Dh, @Moneda, @CCosto, @Porcentaje, GETDATE(), @TipoReg, @Importe, @Fecha, @Unidad);
                    """, new
                {
                    Periodo = clave.Periodo, clave.Mes, Asiento = clave.Numero, Cuenta = r.Cuenta.Trim(), Secuencia = i + 1, Dh = r.Debe > 0 ? "D" : "H",
                    Moneda = CodigoPk.Format(r.Moneda, 4), CCosto = CodigoPk.Format(c.CCosto, 4), Porcentaje = (double)c.Porcentaje, TipoReg = CodigoPk.Format(clave.TipoReg, 4),
                    c.Importe, Fecha = a.Fecha.Date, Unidad = CodigoPk.Format(unidad, 4)
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
        var resultado = Armar(clave, await RenglonesAsync(cn, null, clave, false, ct), talonarios.Select(t => t.Codigo).ToList());
        await CargarCentrosAsync(cn, resultado, ct);
        return resultado;
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
        await ValidarChequesSinSalidaAsync(cn, tx, k, ct);
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

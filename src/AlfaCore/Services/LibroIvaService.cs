using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using AlfaCore.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface ILibroIvaService
{
    Task<IReadOnlyList<LibroIvaUnidad>> GetUnidadesAsync(bool compras = false, CancellationToken ct = default);
    Task<LibroIvaResultado> GetAsync(LibroIvaFiltro filtro, CancellationToken ct = default);
    /// <summary>Pasa la fecha de cierre (CIERREVENTAS o CIERRECOMPRAS, las del legacy) a <paramref name="hasta"/>.
    /// El trigger de MV_ASIENTOS rechaza desde ahí asientos de ese libro con fecha igual o anterior.</summary>
    Task<DateTime> CerrarAsync(bool compras, DateTime hasta, CancellationToken ct = default);
    Task<LibroIvaArchivo> ExportarTxtArcaAsync(LibroIvaFiltro filtro, CancellationToken ct = default);
    Task<LibroIvaArchivo> ExportarExcelAsync(LibroIvaFiltro filtro, CancellationToken ct = default);
    Task<LibroIvaArchivo> ExportarPdfAsync(LibroIvaFiltro filtro, CancellationToken ct = default);
}

/// <summary>
/// Libros de IVA Ventas y Compras con las columnas que pide ARCA (Libro IVA Digital), desde los asientos
/// (MV_ASIENTOS con LIVA_TIPO = VENTAS o COMPRAS, como los libros del legacy). Un solo modelo en lugar de los
/// de configuración del VB6 (LIVA_MOD_ALTER*, LIVAC_MOD_ALTER). Permisos: los menús legacy «Reporte Libro de
/// IVA Ventas» (D6009) y «Reporte Libro de IVA Compras» (D5004); cerrar pide «Fechas de Cierre» (D9882).
/// </summary>
public sealed class LibroIvaService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events, IDocumentPdfService pdf) : ILibroIvaService
{
    public const string MenuLibro = "D6009";
    public const string MenuLibroCompras = "D5004";
    private static string Nombre(bool compras) => compras ? "Libro de IVA Compras" : "Libro de IVA Ventas";
    private static string ClaveCierre(bool compras) => compras ? "CIERRECOMPRAS" : "CIERREVENTAS";
    public const string MenuFechasCierre = "D9882";
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");
    private sealed class ReglaLibroException(string message) : InvalidOperationException(message);

    public Task<IReadOnlyList<LibroIvaUnidad>> GetUnidadesAsync(bool compras = false, CancellationToken ct = default)
        => EjecutarAsync<IReadOnlyList<LibroIvaUnidad>>(compras, "Unidades", async cn => (await cn.QueryAsync<LibroIvaUnidad>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(Codigo)) Codigo,
                   COALESCE(NULLIF(LTRIM(RTRIM(RAZON_SOCIAL)),''), NULLIF(LTRIM(RTRIM(Descripcion)),''), LTRIM(RTRIM(Codigo))) Descripcion,
                   ISNULL(LTRIM(RTRIM(CUIT)),'') Cuit
            FROM dbo.V_TA_UnidadNegocio
            ORDER BY CASE WHEN ISNUMERIC(Codigo) = 1 THEN 0 ELSE 1 END, RIGHT(REPLICATE(' ', 8) + LTRIM(RTRIM(Codigo)), 8);
            """, cancellationToken: ct))).ToList(), ct);

    public Task<LibroIvaResultado> GetAsync(LibroIvaFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync(filtro.Compras, "Consultar", cn => LeerAsync(cn, filtro, ct), ct);

    public Task<DateTime> CerrarAsync(bool compras, DateTime hasta, CancellationToken ct = default)
        => EjecutarAsync(compras, compras ? "CerrarCompras" : "CerrarVentas", async cn =>
        {
            if (!await permissions.HasAccessAsync(MenuFechasCierre, ct))
                throw new ReglaLibroException("Tu usuario no tiene permiso para cambiar las fechas de cierre (menú «Fechas de Cierre»).");
            hasta = hasta.Date;
            var hoy = await cn.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT CONVERT(date,GETDATE());", cancellationToken: ct));
            if (hasta >= hoy.Date) throw new ReglaLibroException("Solo se pueden cerrar días anteriores a hoy.");
            var actual = await LeerCierreAsync(cn, ct, compras: compras);
            var libro = compras ? "compras" : "ventas";
            if (actual is { } a && hasta <= a)
                throw new ReglaLibroException($"Las {libro} ya están cerradas hasta el {a:dd/MM/yyyy}. Para reabrir un período usá «Fechas de Cierre» de Alfa Gestión.");
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion, FechaHora_Modificacion)
                    VALUES (N'CONTABILIDAD', @Clave, @Valor, GETDATE(), GETDATE());
                """, new { Valor = hasta.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Clave = ClaveCierre(compras) }, cancellationToken: ct));
            await events.LogAuditAsync(compras ? "LibroIvaCompras" : "LibroIvaVentas", compras ? "CerrarCompras" : "CerrarVentas", "TA_CONFIGURACION", ClaveCierre(compras),
                $"Cerró {libro} hasta el {hasta:dd/MM/yyyy} (antes {(actual is { } x ? x.ToString("dd/MM/yyyy") : "sin cierre")}).",
                new { Hasta = hasta, Anterior = actual }, CancellationToken.None);
            return hasta;
        }, ct);

    // ── Lectura ─────────────────────────────────────────────────────────────────────────────────

    internal static async Task<DateTime?> LeerCierreAsync(SqlConnection cn, CancellationToken ct, System.Data.IDbTransaction? tx = null, bool compras = false)
    {
        var valor = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;",
            new { Clave = ClaveCierre(compras) }, transaction: tx, cancellationToken: ct));
        return DateTime.TryParseExact(valor, ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) && f.Year > 2000 ? f.Date : null;
    }

    private async Task<LibroIvaResultado> LeerAsync(SqlConnection cn, LibroIvaFiltro filtro, CancellationToken ct)
    {
        var desde = filtro.Desde.Date; var hasta = filtro.Hasta.Date;
        if (hasta < desde) (desde, hasta) = (hasta, desde);
        if ((hasta - desde).TotalDays > 400) throw new ReglaLibroException("Elegí un período de hasta un año (normalmente, un mes).");
        var unidad = (filtro.UnidadNegocio ?? string.Empty).Trim();
        var compras = filtro.Compras;
        // Ventas: clientes y V_MV_CPTE. Compras: proveedores y C_MV_Cpte (el número es el del comprobante del proveedor).
        var terceros = compras ? "dbo.VT_PROVEEDORES" : "dbo.VT_CLIENTES";
        var cpte = compras
            ? "SELECT TOP (1) ABS(ISNULL(x.ImporteImpuestosInternos,0)) ImporteImpuestosInternos FROM dbo.C_MV_Cpte x WHERE x.TC = a.TC AND x.SUCURSAL = a.SUCURSAL AND x.NUMERO = a.NUMERO AND x.LETRA = a.LETRA AND x.CUENTA = a.CUENTA"
            : "SELECT TOP (1) ABS(ISNULL(x.ImporteImpuestosInternos,0)) ImporteImpuestosInternos FROM dbo.V_MV_CPTE x WHERE x.TC = a.TC AND x.SUCURSAL = a.SUCURSAL AND x.NUMERO = a.NUMERO AND x.LETRA = a.LETRA AND ISNULL(x.IDCOMPLEMENTO,0) = 0";
        var filas = (await cn.QueryAsync<FilaSql>(new CommandDefinition($"""
            SELECT a.FECHA Fecha, LTRIM(RTRIM(a.TC)) Tc, LTRIM(RTRIM(ISNULL(a.LETRA,''))) Letra, LTRIM(RTRIM(ISNULL(a.SUCURSAL,''))) Sucursal,
                   LTRIM(RTRIM(ISNULL(a.NUMERO,''))) Numero, LTRIM(RTRIM(ISNULL(a.CUENTA,''))) Cuenta,
                   COALESCE(NULLIF(LTRIM(RTRIM(a.CABNOMBRE)),''), NULLIF(LTRIM(RTRIM(cl.RAZON_SOCIAL)),''), LTRIM(RTRIM(a.CUENTA))) RazonSocial,
                   LTRIM(RTRIM(ISNULL(a.CABCUIT,''))) Cuit, ISNULL(LTRIM(RTRIM(ci.DESCRIPCION)), LTRIM(RTRIM(ISNULL(a.CABCONDIVA,'')))) CondicionIva,
                   LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) UnidadNegocio,
                   ISNULL(a.LIVA_ImpNetoGrav,0) NetoGravado, ISNULL(a.LIVA_ImpNetoNGrav,0) NoGravado, ISNULL(a.LIVA_EXENTO,0) Exento,
                   ISNULL(a.LIVA_AlicIVA,0) A1, ISNULL(a.LIVA_ImpIVA,0) + ISNULL(a.LIVA_ImpIVARec,0) I1,
                   ISNULL(a.LIVA_AlicIva2,0) A2, ISNULL(a.LIVA_ImpIva2,0) I2, ISNULL(a.LIVA_AlicIVA3,0) A3, ISNULL(a.LIVA_ImpIVA3,0) I3,
                   ISNULL(a.LIVA_AlicIVA4,0) A4, ISNULL(a.LIVA_ImpIVA4,0) I4,
                   ISNULL(a.LIVA_Ret_Perc,0) PercepcionIva, ISNULL(a.LIVA_Ret_IBtos,0) PercepcionIibb, ISNULL(a.LIVA_Ret_Ganancias,0) OtrasPercepciones,
                   ISNULL(c.ImporteImpuestosInternos,0) * CASE WHEN ISNULL(a.LIVA_TOTAL,0) < 0 THEN -1 ELSE 1 END ImpuestosInternos,
                   ISNULL(a.LIVA_TOTAL,0) Total
            FROM dbo.MV_ASIENTOS a
            LEFT JOIN {terceros} cl ON cl.CODIGO = a.CUENTA
            LEFT JOIN dbo.TA_CONDIVA ci ON LTRIM(RTRIM(CONVERT(nvarchar(20), ci.CODIGO))) = LTRIM(RTRIM(a.CABCONDIVA))
            OUTER APPLY ({cpte}) c
            WHERE a.LIVA_TIPO = @Tipo AND a.FECHA >= @Desde AND a.FECHA < DATEADD(day, 1, @Hasta)
              AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) = @Unidad)
              AND (@FiltrarUnidades = 0 OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) IN @Unidades)
              AND (@SoloSinUnidad = 0 OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) = '')
            ORDER BY a.FECHA, a.TC, a.LETRA, a.SUCURSAL, a.NUMERO;
            """, new { Desde = desde, Hasta = hasta, Unidad = unidad, filtro.SoloSinUnidad, FiltrarUnidades = filtro.UnidadesNegocio.Count > 0, Unidades = filtro.UnidadesNegocio, Tipo = compras ? "COMPRAS" : "VENTAS" }, commandTimeout: 120, cancellationToken: ct))).ToList();

        var resultado = new LibroIvaResultado
        {
            Filtro = new() { Desde = desde, Hasta = hasta, UnidadNegocio = unidad, Compras = compras, SoloSinUnidad = filtro.SoloSinUnidad, General = filtro.General, UnidadesNegocio = filtro.UnidadesNegocio.ToArray() },
            Filas = filas.Select(f => Convertir(f, compras)).ToList(),
            Cierre = await LeerCierreAsync(cn, ct, compras: compras),
            PuedeCerrar = await permissions.HasAccessAsync(MenuFechasCierre, ct)
        };
        resultado.TotalesPorAlicuota = resultado.Filas.SelectMany(f => f.Alicuotas).GroupBy(a => a.Alicuota).OrderByDescending(g => g.Key)
            .Select(g => new LibroIvaAlicuota(g.Key, g.Sum(x => x.Neto), g.Sum(x => x.Iva))).ToList();
        resultado.SinCodigoArca = resultado.Filas.Count(f => f.CodigoArca == 0);
        (resultado.Emisor, resultado.CuitEmisor, resultado.CondicionIvaEmisor) = await EmisorAsync(cn, unidad, ct);
        resultado.Unidades = await ResumirUnidadesAsync(cn, resultado, ct);
        return resultado;
    }

    /// <summary>Agrupa las filas por unidad de negocio con su razón social y CUIT (sin unidad: los de la empresa).</summary>
    private static async Task<IReadOnlyList<LibroIvaUnidadResumen>> ResumirUnidadesAsync(SqlConnection cn, LibroIvaResultado r, CancellationToken ct)
    {
        var grupos = r.Filas.GroupBy(f => f.UnidadNegocio, StringComparer.OrdinalIgnoreCase).ToList();
        if (grupos.Count == 0) return [];
        var tabla = (await cn.QueryAsync<LibroIvaUnidad>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(Codigo)) Codigo,
                   COALESCE(NULLIF(LTRIM(RTRIM(RAZON_SOCIAL)),''), NULLIF(LTRIM(RTRIM(Descripcion)),''), LTRIM(RTRIM(Codigo))) Descripcion,
                   ISNULL(LTRIM(RTRIM(CUIT)),'') Cuit
            FROM dbo.V_TA_UnidadNegocio;
            """, cancellationToken: ct))).GroupBy(u => u.Codigo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return grupos.Select(g =>
            {
                var u = tabla.GetValueOrDefault(g.Key);
                var nombre = u?.Descripcion is { Length: > 0 } d ? d : g.Key.Length > 0 ? g.Key : r.Emisor;
                var cuit = u?.Cuit is { Length: > 0 } c ? c : r.CuitEmisor;
                return new LibroIvaUnidadResumen(g.Key, nombre, cuit, g.Count(), g.Sum(f => f.NetoGravado), g.Sum(f => f.Iva), g.Sum(f => f.Total));
            })
            .OrderBy(u => int.TryParse(u.Codigo, out var n) ? n : int.MaxValue).ThenBy(u => u.Codigo).ToList();
    }

    /// <summary>Texto corto para títulos: "CONSOLIDADO · 3 unidades de negocio · 2 CUITs".</summary>
    internal static string TextoConsolidado(LibroIvaResultado r)
        => $"CONSOLIDADO · {r.Unidades.Count} unidades de negocio · {(r.CantidadCuits == 1 ? "1 CUIT" : $"{r.CantidadCuits} CUITs")}";

    private static async Task<(string, string, string)> EmisorAsync(SqlConnection cn, string unidad, CancellationToken ct)
    {
        var cfg = (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'NOMBRE', N'CUIT', N'WSFE_CUIT', N'CONDIVA', N'CONDIVAEMPRESA', N'CONDICIONIVA_UN_' + @Unidad);
            """, new { Unidad = unidad }, cancellationToken: ct))).GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor, StringComparer.OrdinalIgnoreCase);
        string nombre = cfg.GetValueOrDefault("NOMBRE", ""), cuit = cfg.GetValueOrDefault("WSFE_CUIT") is { Length: > 0 } w ? w : cfg.GetValueOrDefault("CUIT", "");
        var cond = cfg.GetValueOrDefault("CONDIVAEMPRESA") is { Length: > 0 } ce && ce != "0" ? ce : cfg.GetValueOrDefault("CONDIVA", "");
        if (unidad.Length > 0)
        {
            var u = await cn.QuerySingleOrDefaultAsync<(string Nombre, string Cuit)>(new CommandDefinition("""
                SELECT TOP (1) COALESCE(NULLIF(LTRIM(RTRIM(RAZON_SOCIAL)),''), LTRIM(RTRIM(ISNULL(Descripcion,'')))), LTRIM(RTRIM(ISNULL(CUIT,'')))
                FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo)) = @Unidad;
                """, new { Unidad = unidad }, cancellationToken: ct));
            if (u.Nombre is { Length: > 0 }) nombre = u.Nombre;
            if (u.Cuit is { Length: > 0 }) cuit = u.Cuit;
            if (cfg.GetValueOrDefault("CONDICIONIVA_UN_" + unidad) is { Length: > 0 } cu) cond = cu;
        }
        var condTexto = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) LTRIM(RTRIM(DESCRIPCION)) FROM dbo.TA_CONDIVA WHERE LTRIM(RTRIM(CONVERT(nvarchar(20), CODIGO))) = @Cond;",
            new { Cond = cond }, cancellationToken: ct)) ?? "";
        return (nombre, cuit, condTexto);
    }

    // El legacy puede guardar impuestos internos en LIVA_EXENTO, también en ventas. Solo se separan cuando los importes
    // demuestran esa inclusión; se conserva la fiscalidad de lectores que ya separaron ambos conceptos.
    internal static decimal ExentoCompras(decimal exento,decimal internos,decimal resto,decimal total)
        =>internos!=0&&Math.Sign(exento)==Math.Sign(internos)&&Math.Abs(exento)>=Math.Abs(internos)&&resto+exento==total?exento-internos:exento;

    // Los asientos usan Currency (4 decimales); los libros y ARCA suman comprobantes en centavos.
    internal static decimal MonedaLibro(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    private static LibroIvaFila Convertir(FilaSql s, bool compras)
    {
        var fila = new LibroIvaFila
        {
            Fecha = s.Fecha, Tc = s.Tc, Letra = s.Letra, Cuenta = s.Cuenta, RazonSocial = s.RazonSocial, Cuit = s.Cuit,
            CondicionIva = s.CondicionIva, UnidadNegocio = s.UnidadNegocio,
            PuntoVenta = int.TryParse(s.Sucursal, out var pv) ? pv : 0, Numero = long.TryParse(s.Numero, out var n) ? n : 0,
            NetoGravado = MonedaLibro(s.NetoGravado), NoGravado = MonedaLibro(s.NoGravado), Exento = MonedaLibro(ExentoCompras(s.Exento,s.ImpuestosInternos,s.NetoGravado+s.NoGravado+s.I1+s.I2+s.I3+s.I4+s.PercepcionIva+s.PercepcionIibb+s.OtrasPercepciones,s.Total)),
            PercepcionIva = MonedaLibro(s.PercepcionIva), PercepcionIibb = MonedaLibro(s.PercepcionIibb), OtrasPercepciones = MonedaLibro(s.OtrasPercepciones),
            ImpuestosInternos = MonedaLibro(s.ImpuestosInternos), Total = MonedaLibro(s.Total), CodigoArca = compras ? CodigoArcaCompras(s.Tc, s.Letra) : CodigoArca(s.Tc, s.Letra)
        };
        // En compras, las facturas B y C no discriminan IVA: no hay crédito fiscal ni alícuotas.
        fila.Alicuotas = compras && s.Letra.Trim().ToUpperInvariant() is "B" or "C" ? []
            : RepartirAlicuotas(fila.NetoGravado, s.Letra, [(s.A1, MonedaLibro(s.I1)), (s.A2, MonedaLibro(s.I2)), (s.A3, MonedaLibro(s.I3)), (s.A4, MonedaLibro(s.I4))]);
        return fila;
    }

    /// <summary>El asiento guarda el neto gravado total y el IVA de hasta cuatro alícuotas: el neto de cada
    /// alícuota sale de su IVA (IVA × 100 / alícuota) y la última absorbe el redondeo para que sumen el total.
    /// Letra C (monotributo) no discrimina IVA: sin alícuotas.</summary>
    internal static IReadOnlyList<LibroIvaAlicuota> RepartirAlicuotas(decimal netoGravado, string letra, IReadOnlyList<(decimal Alicuota, decimal Iva)> datos)
    {
        if (letra.Trim().Equals("C", StringComparison.OrdinalIgnoreCase)) return [];
        var conIva = datos.Where(d => d.Iva != 0 && d.Alicuota > 0).GroupBy(d => d.Alicuota).Select(g => (Alicuota: g.Key, Iva: g.Sum(x => x.Iva))).ToList();
        if (conIva.Count == 0) return netoGravado == 0 ? [] : [new LibroIvaAlicuota(0, netoGravado, 0)];
        if (conIva.Count == 1) return [new LibroIvaAlicuota(conIva[0].Alicuota, netoGravado, conIva[0].Iva)];
        var lista = new List<LibroIvaAlicuota>();
        var acumulado = 0m;
        for (var i = 0; i < conIva.Count; i++)
        {
            var neto = i == conIva.Count - 1 ? netoGravado - acumulado : Math.Round(conIva[i].Iva * 100 / conIva[i].Alicuota, 2, MidpointRounding.AwayFromZero);
            acumulado += neto;
            lista.Add(new LibroIvaAlicuota(conIva[i].Alicuota, neto, conIva[i].Iva));
        }
        return lista;
    }

    /// <summary>Tabla de comprobantes de ARCA para facturas, notas de débito y de crédito A, B y C.</summary>
    internal static int CodigoArca(string tc, string letra) => (tc.Trim().ToUpperInvariant(), letra.Trim().ToUpperInvariant()) switch
    {
        ("FC", "A") => 1, ("ND", "A") => 2, ("NC", "A") => 3,
        ("FC", "B") => 6, ("ND", "B") => 7, ("NC", "B") => 8,
        ("FC", "C") => 11, ("ND", "C") => 12, ("NC", "C") => 13,
        ("FC", "M") => 51, ("ND", "M") => 52, ("NC", "M") => 53,
        _ => 0
    };

    /// <summary>Compras (FCC/NCC/NDC del legacy; sin TC = factura cargada por otro proceso; RC = recibo).</summary>
    internal static int CodigoArcaCompras(string tc, string letra) => (tc.Trim().ToUpperInvariant() switch
    {
        "FCC" or "" => "FC", "NCC" => "NC", "NDC" => "ND", "RC" => "RC", var otro => otro
    }, letra.Trim().ToUpperInvariant()) switch
    {
        ("RC", "A") => 4, ("RC", "B") => 9, ("RC", "C") => 15,
        var (t, l) => CodigoArca(t, l)
    };

    internal static int CodigoAlicuota(decimal alicuota) => alicuota switch
    {
        0m => 3, 10.5m => 4, 21m => 5, 27m => 6, 5m => 8, 2.5m => 9, _ => 0
    };

    // ── Exportaciones ───────────────────────────────────────────────────────────────────────────

    /// <summary>Archivos del Libro IVA Digital de ARCA (importación de comprobantes de ventas):
    /// LIBRO_IVA_DIGITAL_VENTAS_CBTE.txt y LIBRO_IVA_DIGITAL_VENTAS_ALICUOTAS.txt, en un zip.</summary>
    public Task<LibroIvaArchivo> ExportarTxtArcaAsync(LibroIvaFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync(filtro.Compras, "ExportarTxt", async cn =>
        {
            var r = await LeerAsync(cn, filtro, ct);
            if (r.CantidadCuits > 1)
                throw new ReglaLibroException("El Libro IVA Digital se presenta por CUIT: elegí una unidad de negocio.");
            if (r.Unidades.Count > 0) r.CuitEmisor = r.Unidades[0].Cuit;
            var errores = LibroIvaArcaValidator.Validar(r);
            if (errores.Count > 0)
            {
                var mensaje = $"No se generó el TXT ARCA: {errores.Count} observación(es). " + string.Join("; ", errores.Take(8));
                await events.LogErrorAsync(filtro.Compras ? "LibroIvaCompras" : "LibroIvaVentas", "ValidarArca",
                    new InvalidOperationException(string.Join(Environment.NewLine, errores)), mensaje, ct: ct);
                throw new LibroIvaArcaValidationException(mensaje, errores);
            }
            var (cbte, alic) = r.Filtro.Compras ? ArmarTxtCompras(r.Filas) : ArmarTxt(r.Filas);
            LibroIvaArcaValidator.ValidarFormato(r, cbte, alic);
            var libro = r.Filtro.Compras ? "COMPRAS" : "VENTAS";
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var (nombre, texto) in new[] { ($"LIBRO_IVA_DIGITAL_{libro}_CBTE.txt", cbte), ($"LIBRO_IVA_DIGITAL_{libro}_ALICUOTAS.txt", alic) })
                {
                    await using var s = zip.CreateEntry(nombre).Open();
                    var bytes = Encoding.Latin1.GetBytes(texto);
                    await s.WriteAsync(bytes, ct);
                }
            }
            return new LibroIvaArchivo($"LibroIvaDigital_{(r.Filtro.Compras ? "Compras" : "Ventas")}_{LibroIvaArcaValidator.Digitos(r.CuitEmisor)}_{r.Filtro.Desde:yyyyMM}.zip", "application/zip", ms.ToArray());
        }, ct);

    internal static (string Comprobantes, string Alicuotas) ArmarTxt(IEnumerable<LibroIvaFila> filas)
    {
        var cbte = new StringBuilder(); var alic = new StringBuilder();
        foreach (var f in filas.Where(x => x.CodigoArca > 0))
        {
            var (codDoc, nroDoc) = Documento(f.Cuit);
            var exento = Math.Abs(f.Exento); var noGravado = Math.Abs(f.NoGravado);
            var alicuotas = f.Alicuotas.Where(a => CodigoAlicuota(a.Alicuota) > 0).ToList();
            var codigoOperacion = alicuotas.Count > 0 || f.Letra == "C" ? (f.Letra == "C" ? "0" : " ") : exento != 0 ? "E" : noGravado != 0 ? "N" : " ";
            cbte.Append(f.Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
                .Append(f.CodigoArca.ToString("000"))
                .Append(f.PuntoVenta.ToString("00000"))
                .Append(f.Numero.ToString(new string('0', 20)))
                .Append(f.Numero.ToString(new string('0', 20)))
                .Append(codDoc.ToString("00"))
                .Append(nroDoc.PadLeft(20, '0'))
                .Append(Texto(f.RazonSocial, 30))
                .Append(Importe(f.Total))
                .Append(Importe(noGravado))
                .Append(Importe(0))                       // percepción a no categorizados
                .Append(Importe(exento))
                .Append(Importe(f.PercepcionIva + f.OtrasPercepciones))
                .Append(Importe(f.PercepcionIibb))
                .Append(Importe(0))                       // percepciones municipales
                .Append(Importe(f.ImpuestosInternos))
                .Append("PES")
                .Append("0001000000")                     // tipo de cambio 1
                .Append(f.Letra == "C" ? "0" : Math.Max(alicuotas.Count, 1).ToString(CultureInfo.InvariantCulture))
                .Append(codigoOperacion)
                .Append(Importe(0))                       // otros tributos
                .Append("00000000")                       // vencimiento de pago
                .Append("\r\n");
            if (f.Letra == "C") continue;
            if (alicuotas.Count == 0) alicuotas = [new LibroIvaAlicuota(0, 0, 0)];
            foreach (var a in alicuotas)
                alic.Append(f.CodigoArca.ToString("000"))
                    .Append(f.PuntoVenta.ToString("00000"))
                    .Append(f.Numero.ToString(new string('0', 20)))
                    .Append(Importe(a.Neto))
                    .Append(CodigoAlicuota(a.Alicuota).ToString("0000"))
                    .Append(Importe(a.Iva))
                    .Append("\r\n");
        }
        return (cbte.ToString(), alic.ToString());
    }

    /// <summary>Libro IVA Digital, compras: LIBRO_IVA_DIGITAL_COMPRAS_CBTE (325) y _ALICUOTAS (84).
    /// Facturas B y C: sin alícuotas ni crédito fiscal.</summary>
    internal static (string Comprobantes, string Alicuotas) ArmarTxtCompras(IEnumerable<LibroIvaFila> filas)
    {
        var cbte = new StringBuilder(); var alic = new StringBuilder();
        foreach (var f in filas.Where(x => x.CodigoArca > 0))
        {
            var (codDoc, nroDoc) = Documento(f.Cuit);
            var exento = Math.Abs(f.Exento); var noGravado = Math.Abs(f.NoGravado);
            var discrimina = f.Letra.Trim().ToUpperInvariant() is "A" or "M";
            var alicuotas = discrimina ? f.Alicuotas.Where(a => CodigoAlicuota(a.Alicuota) > 0).ToList() : [];
            if (discrimina && alicuotas.Count == 0) alicuotas = [new LibroIvaAlicuota(0, 0, 0)];
            var codigoOperacion = discrimina && alicuotas.Any(a => a.Iva != 0) ? " " : exento != 0 ? "E" : noGravado != 0 ? "N" : "0";
            cbte.Append(f.Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
                .Append(f.CodigoArca.ToString("000"))
                .Append(f.PuntoVenta.ToString("00000"))
                .Append(f.Numero.ToString(new string('0', 20)))
                .Append(new string(' ', 16))                 // despacho de importación
                .Append(codDoc.ToString("00"))
                .Append(nroDoc.PadLeft(20, '0'))
                .Append(Texto(f.RazonSocial, 30))
                .Append(Importe(f.Total))
                .Append(Importe(noGravado))
                .Append(Importe(exento))
                .Append(Importe(f.PercepcionIva))
                .Append(Importe(f.OtrasPercepciones))         // otros impuestos nacionales (ganancias)
                .Append(Importe(f.PercepcionIibb))
                .Append(Importe(0))                          // impuestos municipales
                .Append(Importe(f.ImpuestosInternos))
                .Append("PES")
                .Append("0001000000")
                .Append(alicuotas.Count.ToString(CultureInfo.InvariantCulture))
                .Append(codigoOperacion)
                .Append(Importe(alicuotas.Sum(a => a.Iva)))  // crédito fiscal computable
                .Append(Importe(0))                          // otros tributos
                .Append("00000000000")                       // CUIT emisor/corredor
                .Append(new string(' ', 30))                 // denominación emisor/corredor
                .Append(Importe(0))                          // IVA comisión
                .Append("\r\n");
            foreach (var a in alicuotas)
                alic.Append(f.CodigoArca.ToString("000"))
                    .Append(f.PuntoVenta.ToString("00000"))
                    .Append(f.Numero.ToString(new string('0', 20)))
                    .Append(codDoc.ToString("00"))
                    .Append(nroDoc.PadLeft(20, '0'))
                    .Append(Importe(a.Neto))
                    .Append(CodigoAlicuota(a.Alicuota).ToString("0000"))
                    .Append(Importe(a.Iva))
                    .Append("\r\n");
        }
        return (cbte.ToString(), alic.ToString());
    }

    private static (int Codigo, string Numero) Documento(string cuit)
    {
        var digitos = new string((cuit ?? string.Empty).Where(char.IsDigit).ToArray());
        // Consumidor final genérico (99999999995) o sin documento: ARCA espera 99 / 0.
        if (digitos.Length == 0 || digitos.All(c => c == '0') || digitos == "99999999995") return (99, "0");
        return digitos.Length switch
        {
            11 => (80, digitos),
            >= 7 and <= 8 => (96, digitos),
            _ => (99, "0")
        };
    }

    private static string Importe(decimal valor) => Math.Round(Math.Abs(valor) * 100, 0, MidpointRounding.AwayFromZero).ToString("000000000000000", CultureInfo.InvariantCulture);

    private static string Texto(string valor, int largo)
    {
        var limpio = new string((valor ?? string.Empty).Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c >= ' ' && c < 127).ToArray()).Trim().ToUpperInvariant();
        return (limpio.Length > largo ? limpio[..largo] : limpio).PadRight(largo);
    }

    public Task<LibroIvaArchivo> ExportarExcelAsync(LibroIvaFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync(filtro.Compras, "ExportarExcel", async cn =>
        {
            var r = await LeerAsync(cn, filtro, ct);
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(r.Filtro.Compras ? "Libro IVA Compras" : "Libro IVA Ventas");
            ws.Cell(1, 1).Value = r.Consolidado ? $"{Nombre(r.Filtro.Compras)} · {TextoConsolidado(r)}" : $"{Nombre(r.Filtro.Compras)} · {r.Emisor} · CUIT {r.CuitEmisor}";
            if (r.Filtro.General) ws.Cell(1, 1).Value = "General · " + ws.Cell(1, 1).GetString();
            ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13;
            if (r.Consolidado) ws.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#9A3412");
            ws.Cell(2, 1).Value = r.Consolidado
                ? $"Período {r.Filtro.Desde:dd/MM/yyyy} al {r.Filtro.Hasta:dd/MM/yyyy} · " + string.Join(" | ", r.Unidades.Select(u => $"U.N. {(u.Codigo.Length > 0 ? u.Codigo : "-")} {u.Nombre} CUIT {u.Cuit}"))
                : $"Período {r.Filtro.Desde:dd/MM/yyyy} al {r.Filtro.Hasta:dd/MM/yyyy} · {r.CondicionIvaEmisor}";
            string[] titulos = ["Fecha", "Comprobante", "Número", r.Filtro.Compras ? "Proveedor" : "Cliente", "CUIT", "Cond. IVA", "Neto gravado", "No gravado", "Exento",
                "IVA 21%", "IVA 10,5%", "IVA 27%", "IVA otras", "Percepciones", "Imp. internos", "Total", .. (r.Consolidado ? new[] { "Unidad negocio", "CUIT unidad" } : [])];
            var cuitUnidad = r.Unidades.ToDictionary(u => u.Codigo, u => u.Cuit, StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < titulos.Length; c++) ws.Cell(4, c + 1).Value = titulos[c];
            ws.Range(4, 1, 4, titulos.Length).Style.Font.Bold = true;
            ws.Range(4, 1, 4, titulos.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8ECF2");
            var fila = 5;
            foreach (var f in r.Filas)
            {
                ws.Cell(fila, 1).Value = f.Fecha; ws.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";
                ws.Cell(fila, 2).Value = $"{f.Tc} {f.Letra}"; ws.Cell(fila, 3).Value = f.Comprobante;
                ws.Cell(fila, 4).Value = f.RazonSocial; ws.Cell(fila, 5).Value = f.Cuit; ws.Cell(fila, 6).Value = f.CondicionIva;
                decimal[] valores = [f.NetoGravado, f.NoGravado, f.Exento, f.IvaDe(21), f.IvaDe(10.5m), f.IvaDe(27),
                    f.Iva - f.IvaDe(21) - f.IvaDe(10.5m) - f.IvaDe(27), f.PercepcionIva + f.PercepcionIibb + f.OtrasPercepciones, f.ImpuestosInternos, f.Total];
                for (var c = 0; c < valores.Length; c++) ws.Cell(fila, 7 + c).Value = valores[c];
                if (r.Consolidado) { ws.Cell(fila, 17).Value = f.UnidadNegocio; ws.Cell(fila, 18).Value = cuitUnidad.GetValueOrDefault(f.UnidadNegocio, ""); }
                fila++;
            }
            ws.Cell(fila, 4).Value = "Totales"; ws.Row(fila).Style.Font.Bold = true;
            for (var c = 7; c <= 16; c++) ws.Cell(fila, c).FormulaA1 = $"SUM({ws.Cell(5, c).Address}:{ws.Cell(fila - 1, c).Address})";
            ws.Range(5, 7, fila, 16).Style.NumberFormat.Format = "#,##0.00";
            fila += 2;
            ws.Cell(fila++, 1).Value = "Totales por alícuota"; ws.Cell(fila - 1, 1).Style.Font.Bold = true;
            foreach (var a in r.TotalesPorAlicuota)
            {
                ws.Cell(fila, 1).Value = a.Alicuota == 0 ? "0% / sin IVA" : $"{a.Alicuota.ToString("0.##", Ar)}%";
                ws.Cell(fila, 2).Value = a.Neto; ws.Cell(fila, 3).Value = a.Iva;
                ws.Range(fila, 2, fila, 3).Style.NumberFormat.Format = "#,##0.00"; fila++;
            }
            if (r.Consolidado)
            {
                fila++;
                ws.Cell(fila++, 1).Value = "Totales por unidad de negocio"; ws.Cell(fila - 1, 1).Style.Font.Bold = true;
                string[] tu = ["U.N.", "Razón social", "CUIT", "Cptes.", "Neto gravado", "IVA", "Total"];
                for (var c = 0; c < tu.Length; c++) ws.Cell(fila, c + 1).Value = tu[c];
                ws.Range(fila, 1, fila, tu.Length).Style.Font.Bold = true; fila++;
                foreach (var u in r.Unidades)
                {
                    ws.Cell(fila, 1).Value = u.Codigo.Length > 0 ? u.Codigo : "-"; ws.Cell(fila, 2).Value = u.Nombre; ws.Cell(fila, 3).Value = u.Cuit;
                    ws.Cell(fila, 4).Value = u.Cantidad; ws.Cell(fila, 5).Value = u.Neto; ws.Cell(fila, 6).Value = u.Iva; ws.Cell(fila, 7).Value = u.Total;
                    ws.Range(fila, 5, fila, 7).Style.NumberFormat.Format = "#,##0.00"; fila++;
                }
            }
            ws.Columns().AdjustToContents(); ws.Column(4).Width = Math.Min(ws.Column(4).Width, 45);
            ws.SheetView.FreezeRows(4);
            using var ms = new MemoryStream(); wb.SaveAs(ms);
            return new LibroIvaArchivo($"{(r.Filtro.Compras ? "LibroIvaCompras" : "LibroIvaVentas")}_{r.Filtro.Desde:yyyyMM}{(r.Filtro.General ? "_General" : r.Filtro.SoloSinUnidad ? "_SinUnidad" : r.Filtro.UnidadNegocio.Length > 0 ? "_U" + r.Filtro.UnidadNegocio : "")}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ms.ToArray());
        }, ct);

    public Task<LibroIvaArchivo> ExportarPdfAsync(LibroIvaFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync(filtro.Compras, "ExportarPdf", async cn =>
        {
            var r = await LeerAsync(cn, filtro, ct);
            var bytes = await pdf.GenerateAsync(ArmarHtml(r), new DocumentPdfFooterOptions(true, true, r.Emisor), ct);
            return new LibroIvaArchivo($"{(r.Filtro.Compras ? "LibroIvaCompras" : "LibroIvaVentas")}_{r.Filtro.Desde:yyyyMM}{(r.Filtro.General ? "_General" : r.Filtro.SoloSinUnidad ? "_SinUnidad" : r.Filtro.UnidadNegocio.Length > 0 ? "_U" + r.Filtro.UnidadNegocio : "")}.pdf", "application/pdf", bytes);
        }, ct);

    internal static string ArmarHtml(LibroIvaResultado r)
    {
        static string M(decimal v) => v == 0 ? "" : v.ToString("N2", Ar);
        static string E(string s) => WebUtility.HtmlEncode(s);
        var cons = r.Consolidado;
        var k = cons ? 1 : 0; // en el consolidado la primera columna es la unidad de negocio
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html><head><meta charset="utf-8"><style>
            @page { size: A4 landscape; margin: 10mm 8mm 14mm 8mm; }
            body { font-family: Arial, sans-serif; font-size: 7.5pt; color: #111; margin: 0; }
            h1 { font-size: 12pt; margin: 0 0 2px; } .sub { color: #444; margin-bottom: 6px; font-size: 8pt; }
            h1 .cons { display: inline-block; margin-left: 8px; padding: 1px 8px; border-radius: 3px; background: #b45309; color: #fff; font-size: 10pt; vertical-align: middle; }
            .aviso { border: 1.5px solid #b45309; background: #fff7ed; padding: 5px 8px; margin: 4px 0 8px; }
            .aviso b { color: #9a3412; } .aviso p { margin: 0 0 4px; font-size: 8pt; }
            .aviso table { width: auto; } .aviso th { background: #fde7cf; } .aviso td, .aviso th { padding: 2px 10px 2px 4px; }
            table { width: 100%; border-collapse: collapse; } thead { display: table-header-group; }
            th { background: #e8ecf2; text-align: left; padding: 3px 3px; border-bottom: 1px solid #999; font-size: 7pt; }
            td { padding: 2px 3px; border-bottom: 1px solid #e3e3e3; } .n { text-align: right; white-space: nowrap; }
            tr { page-break-inside: avoid; } tfoot td { font-weight: bold; border-top: 1px solid #333; background: #f3f5f8; }
            .alic { margin-top: 10px; width: auto; } .alic td, .alic th { padding: 3px 10px; }
            """);
        sb.Append($".det td:nth-child(-n+{3 + k}), .det td:nth-child({5 + k}) {{ white-space: nowrap; }} .det td:nth-child({6 + k}) {{ font-size: 6.5pt; }}");
        sb.Append("</style></head><body>");
        if (r.Filtro.General) sb.Append("<h1>General · Todas las unidades de negocio</h1>");
        var periodo = $"Período {r.Filtro.Desde:dd/MM/yyyy} al {r.Filtro.Hasta:dd/MM/yyyy} · {r.Filas.Count} comprobantes";
        if (cons)
        {
            sb.Append($"<h1>{Nombre(r.Filtro.Compras)} <span class=\"cons\">{TextoConsolidado(r)}</span></h1><div class=\"sub\">{E(r.Emisor)} · {periodo}</div>");
            sb.Append("<div class=\"aviso\"><p><b>Atención: este libro reúne comprobantes de varias unidades de negocio")
              .Append(r.CantidadCuits > 1 ? " con distinto CUIT" : "")
              .Append(".</b> Es un listado de control; para presentar en ARCA se genera un archivo por CUIT.</p>")
              .Append("<table><thead><tr><th>U.N.</th><th>Razón social</th><th>CUIT</th><th class=\"n\">Cptes.</th><th class=\"n\">Neto grav.</th><th class=\"n\">IVA</th><th class=\"n\">Total</th></tr></thead><tbody>");
            foreach (var u in r.Unidades)
                sb.Append($"<tr><td>{E(u.Codigo.Length > 0 ? u.Codigo : "—")}</td><td>{E(u.Nombre)}</td><td>{E(u.Cuit)}</td><td class=\"n\">{u.Cantidad}</td><td class=\"n\">{u.Neto.ToString("N2", Ar)}</td><td class=\"n\">{u.Iva.ToString("N2", Ar)}</td><td class=\"n\">{u.Total.ToString("N2", Ar)}</td></tr>");
            sb.Append("</tbody></table></div>");
        }
        else
            sb.Append($"<h1>{Nombre(r.Filtro.Compras)}</h1><div class=\"sub\">{E(r.Emisor)} · CUIT {E(r.CuitEmisor)} · {E(r.CondicionIvaEmisor)} · {periodo}</div>");
        sb.Append($"<table class=\"det\"><thead><tr>{(cons ? "<th>U.N.</th>" : "")}<th>Fecha</th><th>Cpte.</th><th>Número</th><th>{(r.Filtro.Compras ? "Proveedor" : "Cliente")}</th><th>CUIT</th><th>Cond. IVA</th><th class=\"n\">Neto grav.</th><th class=\"n\">No grav.</th><th class=\"n\">Exento</th><th class=\"n\">IVA 21%</th><th class=\"n\">IVA 10,5%</th><th class=\"n\">IVA otras</th><th class=\"n\">Percep.</th><th class=\"n\">Imp. int.</th><th class=\"n\">Total</th></tr></thead><tbody>");
        foreach (var f in r.Filas)
        {
            var otras = f.Iva - f.IvaDe(21) - f.IvaDe(10.5m);
            sb.Append($"<tr>{(cons ? $"<td>{E(f.UnidadNegocio)}</td>" : "")}<td>{f.Fecha:dd/MM/yy}</td><td>{E(f.Tc)} {E(f.Letra)}</td><td>{f.Comprobante}</td><td>{E(f.RazonSocial)}</td><td>{E(f.Cuit)}</td><td>{E(f.CondicionIva)}</td>")
              .Append($"<td class=\"n\">{M(f.NetoGravado)}</td><td class=\"n\">{M(f.NoGravado)}</td><td class=\"n\">{M(f.Exento)}</td><td class=\"n\">{M(f.IvaDe(21))}</td><td class=\"n\">{M(f.IvaDe(10.5m))}</td><td class=\"n\">{M(otras)}</td>")
              .Append($"<td class=\"n\">{M(f.PercepcionIva + f.PercepcionIibb + f.OtrasPercepciones)}</td><td class=\"n\">{M(f.ImpuestosInternos)}</td><td class=\"n\">{M(f.Total)}</td></tr>");
        }
        var o = r.Iva - r.Filas.Sum(f => f.IvaDe(21)) - r.Filas.Sum(f => f.IvaDe(10.5m));
        sb.Append($"</tbody><tfoot><tr><td colspan=\"{6 + k}\">Totales</td><td class=\"n\">{M(r.NetoGravado)}</td><td class=\"n\">{M(r.NoGravado)}</td><td class=\"n\">{M(r.Exento)}</td><td class=\"n\">{M(r.Filas.Sum(f => f.IvaDe(21)))}</td><td class=\"n\">{M(r.Filas.Sum(f => f.IvaDe(10.5m)))}</td><td class=\"n\">{M(o)}</td><td class=\"n\">{M(r.Percepciones)}</td><td class=\"n\">{M(r.ImpuestosInternos)}</td><td class=\"n\">{M(r.Total)}</td></tr></tfoot></table>");
        if (r.TotalesPorAlicuota.Count > 0)
        {
            sb.Append("<table class=\"alic\"><thead><tr><th>Alícuota</th><th class=\"n\">Neto gravado</th><th class=\"n\">IVA</th></tr></thead><tbody>");
            foreach (var a in r.TotalesPorAlicuota)
                sb.Append($"<tr><td>{(a.Alicuota == 0 ? "0%" : a.Alicuota.ToString("0.##", Ar) + "%")}</td><td class=\"n\">{a.Neto.ToString("N2", Ar)}</td><td class=\"n\">{a.Iva.ToString("N2", Ar)}</td></tr>");
            sb.Append("</tbody></table>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ── Ejecución con permiso ───────────────────────────────────────────────────────────────────

    private async Task<T> EjecutarAsync<T>(bool compras, string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var id))
            throw new ReglaLibroException("Ingresá en la base activa para consultar el libro.");
        try
        {
            await using var cn = new SqlConnection(sessions.GetConnectionString()); await cn.OpenAsync(ct);
            if (!await permissions.HasAccessAsync(compras ? MenuLibroCompras : MenuLibro, ct))
                throw new ReglaLibroException($"Tu usuario no tiene permiso para el {Nombre(compras)}.");
            var r = await operacion(cn);
            if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != id)
                throw new ReglaLibroException("Cambió la sesión. Volvé a generar el libro en la base activa.");
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaLibroException) { throw; }
        catch (LibroIvaArcaValidationException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync(compras ? "LibroIvaCompras" : "LibroIvaVentas", accion, ex, $"No se pudo generar el {Nombre(compras)}.", ct: ct);
            throw new AppUserFacingException($"No se pudo generar el {Nombre(compras)}.", codigo, ex);
        }
    }

    private sealed class FilaSql
    {
        public DateTime Fecha { get; set; }
        public string Tc { get; set; } = ""; public string Letra { get; set; } = ""; public string Sucursal { get; set; } = ""; public string Numero { get; set; } = "";
        public string Cuenta { get; set; } = ""; public string RazonSocial { get; set; } = ""; public string Cuit { get; set; } = "";
        public string CondicionIva { get; set; } = ""; public string UnidadNegocio { get; set; } = "";
        public decimal NetoGravado { get; set; } public decimal NoGravado { get; set; } public decimal Exento { get; set; }
        public decimal A1 { get; set; } public decimal I1 { get; set; } public decimal A2 { get; set; } public decimal I2 { get; set; }
        public decimal A3 { get; set; } public decimal I3 { get; set; } public decimal A4 { get; set; } public decimal I4 { get; set; }
        public decimal PercepcionIva { get; set; } public decimal PercepcionIibb { get; set; } public decimal OtrasPercepciones { get; set; }
        public decimal ImpuestosInternos { get; set; } public decimal Total { get; set; }
    }
}

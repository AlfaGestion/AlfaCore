using System.Globalization;
using System.IO.Compression;
using System.Text;
using AlfaCore.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IRetencionesService
{
    Task<RetencionesConfig> GetConfigAsync(CancellationToken ct = default);
    Task GuardarConfigAsync(RetencionesConfig config, CancellationToken ct = default);
    Task<RetencionesResultado> GetAsync(RetencionesFiltro filtro, CancellationToken ct = default);
    /// <summary>EXCEL; sufridas: SIFERE_RET, SIFERE_PERC, IVA_RET, IVA_PERC; practicadas: SICORE_GAN, SICORE_SUJ, SICORE_PERC_IVA,
    /// ARBA_RET, ARBA_PERC, ARCIBA, SIRCAR_RET, SIRCAR_PERC.</summary>
    Task<ContabilidadArchivo> ExportarAsync(RetencionesFiltro filtro, string formato, CancellationToken ct = default);
}

/// <summary>
/// Retenciones y percepciones sufridas y practicadas, como los reportes RP_RetPercSufridas / RP_RetPercRealizadas del
/// legacy: líneas de asiento en cuentas con medio de pago RI (IVA), RB (IIBB), RG (Ganancias) u RO (otras); las de activo
/// (1…) son sufridas y las de pasivo (2…) practicadas. Exporta SIFERE WEB (IIBB sufridas), F.2002 Mis Aplicaciones Web
/// (IVA sufridas) y, como agente, SICORE, ARBA, ARCIBA y SIRCAR, con los formatos de UT_SIAP_CITI.
/// </summary>
public sealed class RetencionesService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions,
    IAppEventService events) : IRetencionesService
{
    /// <summary>SIFERE WEB, F.2002 IVA sufridas, ARBA/ARCIBA/SICORE, libros de IVA o reportes contables.</summary>
    internal static readonly string[] Menus = ["D016021", "D016022", "D016026", "D016027", "D016028", "D016029", "D016057", "D6009", "D5004", "D957004"];
    internal const string ClaveJurisdiccion = "ALFACORE_SIFERE_JURISDICCION", ClaveRegimenRet = "Ret_IVASUFRIDO_CodigoRegimen",
        ClaveRegimenPerc = "Perc_IVASUFRIDO_CodigoRegimen";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    /// <summary>Códigos de jurisdicción de la Comisión Arbitral (SIFERE).</summary>
    internal static readonly IReadOnlyList<(string Codigo, string Nombre)> Jurisdicciones =
    [
        ("901", "Ciudad de Buenos Aires"), ("902", "Buenos Aires"), ("903", "Catamarca"), ("904", "Córdoba"), ("905", "Corrientes"),
        ("906", "Chaco"), ("907", "Chubut"), ("908", "Entre Ríos"), ("909", "Formosa"), ("910", "Jujuy"), ("911", "La Pampa"),
        ("912", "La Rioja"), ("913", "Mendoza"), ("914", "Misiones"), ("915", "Neuquén"), ("916", "Río Negro"), ("917", "Salta"),
        ("918", "San Juan"), ("919", "San Luis"), ("920", "Santa Cruz"), ("921", "Santa Fe"), ("922", "Santiago del Estero"),
        ("923", "Tierra del Fuego"), ("924", "Tucumán")
    ];

    /// <summary>Provincia de TA_ESTADOS (por nombre, o su CodJurisdiccion si está cargado) → código SIFERE.</summary>
    internal static string JurisdiccionDe(string descripcion, string codJurisdiccion)
    {
        var cod = new string((codJurisdiccion ?? "").Where(char.IsDigit).ToArray());
        if (cod.Length == 3 && cod[0] == '9') return cod;
        var d = Norm(descripcion);
        if (d.Length == 0 || d == ".") return "";
        if (d.Contains("capital") || d.Contains("caba") || d.Contains("ciudad")) return "901";
        if (d.StartsWith("buenos aires") || d == "bsas" || d.StartsWith("bs as") || d.StartsWith("bs.as")) return "902";
        if (d.Contains("santiago")) return "922";
        if (d.Contains("fuego")) return "923";
        foreach (var (codigo, nombre) in Jurisdicciones.Skip(2))
            if (d.StartsWith(Norm(nombre))) return codigo;
        return "";
    }

    private static string Norm(string s) => new string((s ?? "").Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Trim().TrimEnd('.').ToLowerInvariant();

    // ── Configuración ───────────────────────────────────────────────────────────────────────────

    public Task<RetencionesConfig> GetConfigAsync(CancellationToken ct = default) => EjecutarAsync("Configuracion", async cn =>
    {
        var v = await ConfigAsync(cn, ct);
        var unidades = (await cn.QueryAsync<CompraCargaOpcion>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(Codigo)) Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion FROM dbo.V_TA_UnidadNegocio ORDER BY Codigo;", cancellationToken: ct))).ToList();
        var retenciones = (await cn.QueryAsync<(string Id, string Desc)>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(CAST(IdRetencion AS nvarchar(20)))), LTRIM(RTRIM(ISNULL(Descripcion,''))) FROM dbo.V_TA_RETENCIONES ORDER BY IdRetencion;", cancellationToken: ct))).ToList();
        return new RetencionesConfig
        {
            JurisdiccionDefecto = v.GetValueOrDefault(ClaveJurisdiccion.ToUpperInvariant(), ""),
            RegimenRetencionIva = v.GetValueOrDefault(ClaveRegimenRet.ToUpperInvariant(), ""),
            RegimenPercepcionIva = v.GetValueOrDefault(ClaveRegimenPerc.ToUpperInvariant(), ""),
            ArbaRetencionesMensual = !v.GetValueOrDefault(ClaveArbaRetMensual, "SI").Equals("NO", StringComparison.OrdinalIgnoreCase),
            ArbaPercepcionesMensual = !v.GetValueOrDefault(ClaveArbaPercMensual, "SI").Equals("NO", StringComparison.OrdinalIgnoreCase),
            ArbaRetencionesActividad = v.GetValueOrDefault(ClaveArbaRetActividad, ""),
            ArbaPercepcionesActividad = v.GetValueOrDefault(ClaveArbaPercActividad, ""),
            SircarRegimen = v.GetValueOrDefault(ClaveSircarRegimen, ""),
            SircarJurisdiccion = v.GetValueOrDefault(ClaveSircarJurisdiccion, ""),
            Sicore = retenciones.Select(r => new RetencionSicoreCodigo
            {
                IdRetencion = r.Id, Descripcion = r.Desc,
                Impuesto = v.GetValueOrDefault("RETGANCI_" + r.Id, ""), Regimen = v.GetValueOrDefault("RETGANCR_" + r.Id, "")
            }).ToList(),
            Jurisdicciones = Jurisdicciones, Unidades = unidades
        };
    }, ct);

    public Task GuardarConfigAsync(RetencionesConfig c, CancellationToken ct = default) => EjecutarAsync("GuardarConfiguracion", async cn =>
    {
        foreach (var j in new[] { c.JurisdiccionDefecto, c.SircarJurisdiccion })
            if (j.Length > 0 && Jurisdicciones.All(x => x.Codigo != j)) throw new ReglaException("Elegí una jurisdicción válida.");
        foreach (var r in new[] { c.RegimenRetencionIva, c.RegimenPercepcionIva, c.SircarRegimen }.Concat(c.Sicore.Select(s => s.Regimen)))
            if (r.Trim().Length > 3 || !r.Trim().All(char.IsDigit)) throw new ReglaException("El régimen es un código de hasta tres dígitos.");
        foreach (var s in c.Sicore)
            if (s.Impuesto.Trim().Length > 4 || !s.Impuesto.Trim().All(char.IsDigit)) throw new ReglaException("El código de impuesto SICORE es de hasta cuatro dígitos (217 = Ganancias).");
        foreach (var a in new[] { c.ArbaRetencionesActividad, c.ArbaPercepcionesActividad })
            if (a.Trim().Length > 20 || a.Trim().Any(ch => !char.IsLetterOrDigit(ch))) throw new ReglaException("El código de actividad de ARBA solo lleva letras y números.");
        var valores = new List<(string Clave, string Valor)>
        {
            (ClaveJurisdiccion, c.JurisdiccionDefecto), (ClaveRegimenRet, c.RegimenRetencionIva.Trim()), (ClaveRegimenPerc, c.RegimenPercepcionIva.Trim()),
            (ClaveArbaRetMensual, c.ArbaRetencionesMensual ? "SI" : "NO"), (ClaveArbaPercMensual, c.ArbaPercepcionesMensual ? "SI" : "NO"),
            (ClaveArbaRetActividad, c.ArbaRetencionesActividad.Trim()), (ClaveArbaPercActividad, c.ArbaPercepcionesActividad.Trim()),
            (ClaveSircarRegimen, c.SircarRegimen.Trim()), (ClaveSircarJurisdiccion, c.SircarJurisdiccion)
        };
        foreach (var s in c.Sicore.Where(s => s.IdRetencion.Trim().Length > 0))
        {
            valores.Add(("RETGANCI_" + s.IdRetencion.Trim(), s.Impuesto.Trim()));
            valores.Add(("RETGANCR_" + s.IdRetencion.Trim(), s.Regimen.Trim()));
        }
        foreach (var (clave, valor) in valores)
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);
                IF @@ROWCOUNT = 0 INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion) VALUES (N'CONTABILIDAD', @Clave, @Valor, GETDATE());
                """, new { Clave = clave, Valor = valor }, cancellationToken: ct));
        await events.LogAuditAsync("Retenciones", "GuardarConfiguracion", "TA_CONFIGURACION", "RETENCIONES", "Configuró las exportaciones de retenciones y percepciones.",
            valores.ToDictionary(x => x.Clave, x => x.Valor), CancellationToken.None);
        return true;
    }, ct);

    internal const string ClaveArbaRetMensual = "IIBB_PRES_RETENCIONES_MENSUAL", ClaveArbaPercMensual = "IIBB_PRES_PERCEPCIONES_MENSUAL",
        ClaveArbaRetActividad = "IIBB_RETENCIONES_COD_ACTIVIDAD", ClaveArbaPercActividad = "IIBB_PERCEPCIONES_COD_ACTIVIDAD",
        ClaveSircarRegimen = "ALFACORE_SIRCAR_REGIMEN", ClaveSircarJurisdiccion = "ALFACORE_SIRCAR_JURISDICCION";

    private static async Task<Dictionary<string, string>> ConfigAsync(SqlConnection cn, CancellationToken ct)
        => (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (UPPER(@j), UPPER(@r), UPPER(@p), 'CUIT', 'CONDIVA', 'SIFEREWEB_FECHACONTABLE',
                    'IIBB_PRES_RETENCIONES_MENSUAL', 'IIBB_PRES_PERCEPCIONES_MENSUAL', 'IIBB_RETENCIONES_COD_ACTIVIDAD', 'IIBB_PERCEPCIONES_COD_ACTIVIDAD',
                    'ALFACORE_SIRCAR_REGIMEN', 'ALFACORE_SIRCAR_JURISDICCION')
               OR UPPER(LTRIM(RTRIM(CLAVE))) LIKE 'RETGANC[IR][_]%';
            """, new { j = ClaveJurisdiccion, r = ClaveRegimenRet, p = ClaveRegimenPerc }, cancellationToken: ct)))
            .GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor, StringComparer.OrdinalIgnoreCase);

    // ── Consulta ────────────────────────────────────────────────────────────────────────────────

    public Task<RetencionesResultado> GetAsync(RetencionesFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Consultar", cn => LeerAsync(cn, filtro, ct), ct);

    private static async Task<RetencionesResultado> LeerAsync(SqlConnection cn, RetencionesFiltro f, CancellationToken ct)
    {
        if (f.Hasta < f.Desde || (f.Hasta - f.Desde).TotalDays > 400) throw new ReglaException("Elegí un rango de hasta un año.");
        if (f.Alcance is not ("SUFRIDAS" or "PRACTICADAS" or "TODAS")) throw new ReglaException("Alcance inválido.");
        // Base imponible de las cobranzas: la función del legacy (FN_BaseImponible), si está instalada.
        var baseCobranza = await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID('dbo.FN_BaseImponible') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 1
            ? "dbo.FN_BaseImponible(a.TC, a.SUCURSAL, a.NUMERO, a.LETRA)" : "NULL";
        var sql = $"""
            SELECT a.FECHA Fecha, ISNULL(a.FechaSubdiario, a.FECHA) FechaContable, LTRIM(RTRIM(ISNULL(a.TC,''))) Tc, LTRIM(RTRIM(ISNULL(a.SUCURSAL,''))) Sucursal,
                   LTRIM(RTRIM(ISNULL(a.NUMERO,''))) Numero, LTRIM(RTRIM(ISNULL(a.LETRA,''))) Letra, LTRIM(RTRIM(a.CUENTA)) Cuenta,
                   LTRIM(RTRIM(ISNULL(c.DESCRIPCION,''))) CuentaNombre, LTRIM(RTRIM(c.MedioDePago)) Medio, a.[DEBE-HABER] Dh, ISNULL(a.IMPORTE,0) Importe,
                   LTRIM(RTRIM(ISNULL(a.TJ_Autorizo,''))) Certificado, LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) Unidad,
                   ISNULL(NULLIF(LTRIM(RTRIM(h.CABCUENTA)),''), LTRIM(RTRIM(ISNULL(h.CUENTA,'')))) Contraparte,
                   LTRIM(RTRIM(COALESCE(NULLIF(LTRIM(RTRIM(h.CABNOMBRE)),''), cl.RAZON_SOCIAL, pr.RAZON_SOCIAL, a.DETALLE, ''))) Nombre,
                   COALESCE(NULLIF(LTRIM(RTRIM(h.CABCUIT)),''), cl.NUMERO_DOCUMENTO, pr.NUMERO_DOCUMENTO, '') Cuit,
                   LTRIM(RTRIM(COALESCE(NULLIF(LTRIM(RTRIM(h.CABCONDIVA)),''), cl.IVA, pr.IVA, ''))) CondicionIva,
                   LTRIM(RTRIM(ISNULL(h.LIVA_TIPO,''))) LivaTipo,
                   CASE WHEN LTRIM(RTRIM(a.TC)) IN ('CB','CBCT') THEN {baseCobranza} ELSE h.LIVA_ImpNetoGrav END Base,
                   ISNULL(e.DESCRIPCION,'') Provincia, ISNULL(e.CodJurisdiccion,'') CodJurisdiccion
            FROM dbo.MV_ASIENTOS a
            JOIN dbo.MA_CUENTAS c ON c.CODIGO = a.CUENTA
            OUTER APPLY (SELECT TOP (1) x.CABCUENTA, x.CUENTA, x.CABNOMBRE, x.CABCUIT, x.CABCONDIVA, x.LIVA_TIPO, x.LIVA_ImpNetoGrav
                         FROM dbo.MV_ASIENTOS x
                         WHERE x.PERIODO = a.PERIODO AND x.MES_OPERATIVO = a.MES_OPERATIVO AND x.[NUMERO ASIENTO] = a.[NUMERO ASIENTO]
                           AND x.TIPO_REG = a.TIPO_REG AND x.TC = a.TC AND x.SUCURSAL = a.SUCURSAL AND x.NUMERO = a.NUMERO AND x.LETRA = a.LETRA
                         ORDER BY x.SECUENCIA) h
            LEFT JOIN dbo.VT_CLIENTES cl ON cl.CODIGO = ISNULL(NULLIF(h.CABCUENTA,''), h.CUENTA)
            LEFT JOIN dbo.VT_PROVEEDORES pr ON pr.CODIGO = ISNULL(NULLIF(h.CABCUENTA,''), h.CUENTA)
            LEFT JOIN dbo.MA_CUENTASADIC ad ON ad.CODIGO = a.CUENTA
            LEFT JOIN dbo.TA_ESTADOS e ON e.CODIGO = ad.PROVINCIA AND LTRIM(RTRIM(ISNULL(ad.PROVINCIA,''))) <> ''
            WHERE LTRIM(RTRIM(c.MedioDePago)) IN ('RI','RB','RG','RO') AND ISNULL(a.IMPORTE,0) <> 0
              AND a.FECHA >= @Desde AND a.FECHA < DATEADD(day, 1, @Hasta)
              AND ISNULL(a.EsResumen,0) = 0 AND ISNULL(a.EsCierre,0) = 0 AND ISNULL(a.ES_SALDO_APERTURA,0) = 0
              AND LEFT(LTRIM(a.CUENTA),1) IN ('1','2')
              AND (@Alcance = 'TODAS' OR (@Alcance = 'SUFRIDAS' AND LEFT(LTRIM(a.CUENTA),1) = '1') OR (@Alcance = 'PRACTICADAS' AND LEFT(LTRIM(a.CUENTA),1) = '2'))
              AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) = @Unidad)
            ORDER BY a.FECHA, a.TC, a.SUCURSAL, a.NUMERO, a.SECUENCIA;
            """;
        var filas = (await cn.QueryAsync<FilaSql>(new CommandDefinition(sql,
            new { Desde = f.Desde.Date, Hasta = f.Hasta.Date, f.Alcance, Unidad = f.Unidad.Trim() }, commandTimeout: 180, cancellationToken: ct)))
            .Select(Convertir).ToList();
        var avisos = new List<string>();
        var sinJur = filas.Count(x => x.Impuesto == "IIBB" && x.Sufrida && x.Jurisdiccion.Length == 0);
        if (sinJur > 0) avisos.Add($"{sinJur} retención(es)/percepción(es) de IIBB sin jurisdicción: la cuenta no tiene provincia. Para SIFERE se usa la jurisdicción por defecto.");
        var sinCert = filas.Count(x => x.Sufrida && x.Clase == "Retención" && x.Certificado.Length == 0);
        if (sinCert > 0) avisos.Add($"{sinCert} retención(es) sufrida(s) sin número de certificado: SIFERE usa el número del recibo.");
        return new RetencionesResultado
        {
            Filtro = f, Filas = filas, Avisos = avisos,
            Totales = filas.GroupBy(x => (x.Impuesto, x.Clase, x.Sufrida)).OrderBy(g => !g.Key.Sufrida).ThenBy(g => g.Key.Impuesto).ThenBy(g => g.Key.Clase)
                .Select(g => new RetencionesTotal(g.Key.Impuesto, g.Key.Clase, g.Key.Sufrida, g.Count(), g.Sum(x => x.Importe))).ToList()
        };
    }

    private sealed class FilaSql
    {
        public DateTime Fecha { get; set; } public DateTime FechaContable { get; set; } public string Tc { get; set; } = ""; public string Sucursal { get; set; } = "";
        public string Numero { get; set; } = ""; public string Letra { get; set; } = ""; public string Cuenta { get; set; } = ""; public string CuentaNombre { get; set; } = "";
        public string Medio { get; set; } = ""; public string Dh { get; set; } = ""; public decimal Importe { get; set; } public string Certificado { get; set; } = "";
        public string Unidad { get; set; } = ""; public string Contraparte { get; set; } = ""; public string Nombre { get; set; } = ""; public string Cuit { get; set; } = "";
        public string CondicionIva { get; set; } = ""; public string LivaTipo { get; set; } = ""; public decimal? Base { get; set; }
        public string Provincia { get; set; } = ""; public string CodJurisdiccion { get; set; } = "";
    }

    internal static string Origen(string tc, string livaTipo) => tc.ToUpperInvariant() switch
    {
        "CB" or "CBCT" or "RC" => "Cobranza",
        "FCC" or "NCC" or "NDC" or "FPC" or "NCFPC" or "CC" => "Compra",
        "PG" or "OPG" => "Pago",
        "FC" or "NC" or "ND" or "TK" or "TKFC" or "FCE" => "Venta",
        _ => livaTipo.Equals("Compras", StringComparison.OrdinalIgnoreCase) ? "Compra" : livaTipo.Equals("Ventas", StringComparison.OrdinalIgnoreCase) ? "Venta" : "Asiento"
    };

    private static RetencionFila Convertir(FilaSql s)
    {
        var sufrida = s.Cuenta.StartsWith('1');
        var origen = Origen(s.Tc, s.LivaTipo);
        // Sufrida (activo): Debe suma. Practicada (pasivo): Haber suma. El contrario es devolución (NC).
        var signo = (sufrida ? s.Dh == "D" : s.Dh == "H") ? 1 : -1;
        return new RetencionFila
        {
            Fecha = s.Fecha, FechaContable = s.FechaContable, Tc = s.Tc, Sucursal = s.Sucursal, Numero = s.Numero, Letra = s.Letra, Origen = origen,
            Impuesto = s.Medio switch { "RI" => "IVA", "RB" => "IIBB", "RG" => "Ganancias", _ => "Otros" },
            Clase = origen is "Compra" or "Venta" ? "Percepción" : "Retención", Sufrida = sufrida,
            Cuenta = s.Cuenta, CuentaNombre = s.CuentaNombre, Contraparte = s.Contraparte, Nombre = s.Nombre.Trim(), Cuit = Digitos(s.Cuit),
            CondicionIva = s.CondicionIva, Importe = signo * Math.Abs(s.Importe), Base = s.Base is { } b && b != 0 ? Math.Abs(b) : null,
            Certificado = s.Certificado, Unidad = s.Unidad,
            Jurisdiccion = JurisdiccionDe(s.Provincia, s.CodJurisdiccion),
            JurisdiccionNombre = Jurisdicciones.FirstOrDefault(j => j.Codigo == JurisdiccionDe(s.Provincia, s.CodJurisdiccion)).Nombre ?? ""
        };
    }

    // ── Exportaciones ───────────────────────────────────────────────────────────────────────────

    public Task<ContabilidadArchivo> ExportarAsync(RetencionesFiltro filtro, string formato, CancellationToken ct = default)
        => EjecutarAsync("Exportar", async cn =>
        {
            var cfg = await ConfigAsync(cn, ct);
            var practicada = FormatosPracticadas.Contains(formato);
            var f = new RetencionesFiltro { Desde = filtro.Desde, Hasta = filtro.Hasta, Unidad = filtro.Unidad,
                Alcance = formato == "EXCEL" ? filtro.Alcance : practicada ? "PRACTICADAS" : "SUFRIDAS" };
            if (f.Hasta < f.Desde || (f.Hasta - f.Desde).TotalDays > 400) throw new ReglaException("Elegí un rango de hasta un año.");
            var r = practicada ? null : await LeerAsync(cn, f, ct);
            var mes = MesTag(f.Desde);
            var archivo = r is null ? await ExportarPracticadaAsync(cn, f, formato, cfg, ct) : formato switch
            {
                "EXCEL" => Excel(r),
                "SIFERE_RET" => Zip($"SIFERE_RETENCIONES_{mes}.zip", SifereRetenciones(r.Filas, cfg, mes)),
                "SIFERE_PERC" => Zip($"SIFERE_PERCEPCIONES_{mes}.zip", SiferePercepciones(r.Filas, await PercepcionesPorProvinciaAsync(cn, r.Filas, ct), cfg, mes)),
                "IVA_RET" => Txt($"RET-IVA{mes}.TXT", IvaSufridas(r.Filas, retenciones: true, cfg.GetValueOrDefault(ClaveRegimenRet.ToUpperInvariant(), ""))),
                "IVA_PERC" => Txt($"PER-IVA{mes}.TXT", IvaSufridas(r.Filas, retenciones: false, cfg.GetValueOrDefault(ClaveRegimenPerc.ToUpperInvariant(), ""))),
                _ => throw new ReglaException("Formato de exportación desconocido.")
            };
            await events.LogAuditAsync("Retenciones", "Exportar", "MV_ASIENTOS", formato,
                $"Exportó {formato} de retenciones y percepciones ({f.Desde:dd/MM/yyyy}–{f.Hasta:dd/MM/yyyy}).", new { formato, f.Desde, f.Hasta, f.Unidad, archivo.Nombre }, CancellationToken.None);
            return archivo;
        }, ct);

    private static string MesTag(DateTime d) => new[] { "ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC" }[d.Month - 1] + d.Year.ToString(Inv);

    /// <summary>NN-NNNNNNNN-N (o trece ceros si no es un CUIT).</summary>
    internal static string CuitGuiones(string cuit)
    {
        var d = Digitos(cuit);
        return d.Length == 11 ? $"{d[..2]}-{d[2..10]}-{d[10]}" : new string('0', 13);
    }

    /// <summary>Importe SIFERE: 8 enteros, coma, 2 decimales (00001234,56); negativo con «-» al principio.</summary>
    internal static string ImporteSifere(decimal v)
    {
        var txt = Math.Abs(Math.Round(v, 2)).ToString("00000000.00", Inv).Replace('.', ',');
        return v < 0 ? "-" + txt[1..] : txt;
    }

    /// <summary>Solo dígitos, con ceros a la izquierda (si sobra, quedan los últimos).</summary>
    internal static string Num(string s, int largo)
    {
        var d = Digitos(s);
        return d.Length > largo ? d[^largo..] : d.PadLeft(largo, '0');
    }

    /// <summary>SIFERE WEB retenciones de IIBB sufridas (cobranzas): un archivo por jurisdicción, registro de 79 caracteres.</summary>
    internal static IReadOnlyList<(string Nombre, string Contenido)> SifereRetenciones(IEnumerable<RetencionFila> filas, IReadOnlyDictionary<string, string> cfg, string mes)
    {
        var fechaContable = !cfg.GetValueOrDefault("SIFEREWEB_FECHACONTABLE", "").Equals("SI", StringComparison.OrdinalIgnoreCase);
        var porDefecto = cfg.GetValueOrDefault(ClaveJurisdiccion.ToUpperInvariant(), "");
        var condEmpresa = cfg.GetValueOrDefault("CONDIVA", "").Trim();
        var lineas = new List<(string Jur, string Linea)>();
        foreach (var x in filas.Where(x => x.Sufrida && x.Impuesto == "IIBB" && x.Origen == "Cobranza"))
        {
            var jur = x.Jurisdiccion.Length > 0 ? x.Jurisdiccion : porDefecto;
            if (jur.Length == 0) throw new ReglaException("Hay retenciones de IIBB sin jurisdicción: elegí la jurisdicción por defecto en Configurar.");
            var letra = x.Letra is "A" or "B" or "C" or "M" ? x.Letra : condEmpresa != "1" ? "C" : x.CondicionIva == "1" ? "A" : "B";
            var constancia = x.Certificado.Any(char.IsDigit) ? x.Certificado : x.Numero;
            lineas.Add((jur, jur + CuitGuiones(x.Cuit) + (fechaContable ? x.FechaContable : x.Fecha).ToString("dd/MM/yyyy", Inv)
                + Num(x.Sucursal, 4) + Num(constancia, 16) + "R" + letra + (Num(x.Sucursal, 4) + Num(x.Numero, 16)) + ImporteSifere(x.Importe)));
        }
        return lineas.GroupBy(l => l.Jur).OrderBy(g => g.Key)
            .Select(g => ($"RETENCION_{mes}_JUR{g.Key}.TXT", string.Concat(g.Select(l => l.Linea + "\r\n")))).ToList();
    }

    /// <summary>SIFERE WEB percepciones de IIBB sufridas (compras): por jurisdicción, registro de 51 caracteres.</summary>
    internal static IReadOnlyList<(string Nombre, string Contenido)> SiferePercepciones(IEnumerable<RetencionFila> filas,
        IReadOnlyDictionary<(string Tc, string Id, string Cuenta), List<(string Jur, decimal Importe)>> detalle, IReadOnlyDictionary<string, string> cfg, string mes)
    {
        var fechaContable = !cfg.GetValueOrDefault("SIFEREWEB_FECHACONTABLE", "").Equals("SI", StringComparison.OrdinalIgnoreCase);
        var porDefecto = cfg.GetValueOrDefault(ClaveJurisdiccion.ToUpperInvariant(), "");
        var lineas = new List<(string Jur, string Linea)>();
        foreach (var x in filas.Where(x => x.Sufrida && x.Impuesto == "IIBB" && x.Origen is "Compra" or "Pago"))
        {
            var tipo = x.Tc switch { "FCC" => "F", "NCC" => "C", "NDC" => "D", "PG" or "OPG" => "R", _ => "O" };
            var signo = x.Importe < 0 ? -1 : 1;
            var partes = detalle.TryGetValue((x.Tc, x.Sucursal + x.Numero + x.Letra, x.Contraparte), out var d) && d.Count > 0
                ? d.Select(p => (p.Jur, Importe: signo * Math.Abs(p.Importe))).ToList()
                : [(x.Jurisdiccion.Length > 0 ? x.Jurisdiccion : porDefecto, x.Importe)];
            foreach (var (jurPar, importe) in partes)
            {
                var jur = jurPar.Length > 0 ? jurPar : porDefecto;
                if (jur.Length == 0) throw new ReglaException("Hay percepciones de IIBB sin jurisdicción: elegí la jurisdicción por defecto en Configurar.");
                lineas.Add((jur, jur + CuitGuiones(x.Cuit) + (fechaContable ? x.FechaContable : x.Fecha).ToString("dd/MM/yyyy", Inv)
                    + Num(x.Sucursal, 4) + Num(x.Numero, 8) + tipo + (x.Letra.Length > 0 ? x.Letra[..1] : " ") + ImporteSifere(importe)));
            }
        }
        return lineas.GroupBy(l => l.Jur).OrderBy(g => g.Key)
            .Select(g => ($"PERCEPCION_{mes}_JUR{g.Key}.TXT", string.Concat(g.Select(l => l.Linea + "\r\n")))).ToList();
    }

    /// <summary>Detalle por provincia de C_MV_CPTE_PERCEPCIONES (carga de percepciones del legacy).</summary>
    private static async Task<Dictionary<(string Tc, string Id, string Cuenta), List<(string Jur, decimal Importe)>>> PercepcionesPorProvinciaAsync(
        SqlConnection cn, IReadOnlyList<RetencionFila> filas, CancellationToken ct)
    {
        var claves = filas.Where(x => x.Sufrida && x.Impuesto == "IIBB" && x.Origen is "Compra" or "Pago").Select(x => x.Sucursal + x.Numero + x.Letra).Distinct().ToArray();
        var res = new Dictionary<(string, string, string), List<(string, decimal)>>();
        if (claves.Length == 0 || await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID('dbo.C_MV_CPTE_PERCEPCIONES') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 0)
            return res;
        var rows = await cn.QueryAsync<(string Tc, string Id, string Cuenta, string Provincia, string CodJur, decimal Importe)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(p.TC)), LTRIM(RTRIM(p.IDCOMPROBANTE)), LTRIM(RTRIM(p.CUENTA)), ISNULL(e.DESCRIPCION,''), ISNULL(e.CodJurisdiccion,''),
                   ISNULL(p.PERCEPCION,0) * CASE WHEN LTRIM(RTRIM(ISNULL(c.MONEDA,'1'))) IN ('','1') THEN 1 ELSE ISNULL(NULLIF(c.COTIZACION,0),1) END
            FROM dbo.C_MV_CPTE_PERCEPCIONES p
            LEFT JOIN dbo.C_MV_CPTE c ON c.TC = p.TC AND c.IDCOMPROBANTE = p.IDCOMPROBANTE AND c.CUENTA = p.CUENTA
            LEFT JOIN dbo.TA_ESTADOS e ON e.CODIGO = p.PROVINCIA
            WHERE LTRIM(RTRIM(p.IDCOMPROBANTE)) IN @claves AND ISNULL(p.PERCEPCION,0) <> 0;
            """, new { claves }, cancellationToken: ct));
        foreach (var r in rows)
        {
            var k = (r.Tc, r.Id, r.Cuenta);
            if (!res.TryGetValue(k, out var l)) res[k] = l = [];
            l.Add((JurisdiccionDe(r.Provincia, r.CodJur), r.Importe));
        }
        return res;
    }

    /// <summary>F.2002 Mis Aplicaciones Web: retenciones (cobranzas) o percepciones (compras) de IVA sufridas, 56 caracteres.</summary>
    internal static string IvaSufridas(IEnumerable<RetencionFila> filas, bool retenciones, string regimen)
    {
        if (regimen.Trim().Length == 0) throw new ReglaException($"Configurá el código de régimen de {(retenciones ? "retenciones" : "percepciones")} de IVA (Configurar).");
        var sb = new StringBuilder();
        foreach (var x in filas.Where(x => x.Sufrida && x.Impuesto == "IVA" && (retenciones ? x.Origen != "Compra" : x.Origen == "Compra")))
        {
            var numero = Num(x.Sucursal, 5) + Num(x.Numero, 11);
            var importe = Math.Abs(Math.Round(x.Importe, 2)).ToString("000000000000.00", Inv).Replace('.', ',');
            if (x.Importe < 0) importe = "-" + importe[1..];
            sb.Append(regimen.Trim().PadRight(3)[..3]).Append(Digitos(x.Cuit).PadRight(11)[..11]).Append(x.Fecha.ToString("dd/MM/yyyy", Inv))
              .Append(numero).Append(importe.PadLeft(16)).Append("\r\n");
        }
        return sb.ToString();
    }

    // ── Practicadas (agente de retención / percepción) ──────────────────────────────────────────

    /// <summary>Formatos de practicadas: no salen de las líneas de asiento sino de las cabeceras de pagos y del libro de ventas.</summary>
    internal static readonly string[] FormatosPracticadas = ["SICORE_GAN", "SICORE_SUJ", "SICORE_PERC_IVA", "ARBA_RET", "ARBA_PERC", "ARCIBA", "SIRCAR_RET", "SIRCAR_PERC"];

    private async Task<ContabilidadArchivo> ExportarPracticadaAsync(SqlConnection cn, RetencionesFiltro f, string formato, IReadOnlyDictionary<string, string> cfg, CancellationToken ct)
    {
        var mes = MesTag(f.Desde);
        var cuitEmpresa = Digitos(cfg.GetValueOrDefault("CUIT", ""));
        switch (formato)
        {
            case "SICORE_GAN":
                return Txt($"SICORE-RETENCIONES-{mes}.TXT", SicoreGanancias(await PagosAsync(cn, f, "RETGAN", ct), cfg));
            case "SICORE_SUJ":
                return Txt($"SICORE-SUJETOSRETENIDOS-{mes}.TXT", SicoreSujetos(await PagosAsync(cn, f, "RETGAN", ct)));
            case "SICORE_PERC_IVA":
                return Txt($"SICORE-PERCEPCIONIVA-{mes}.TXT", SicorePercepcionIva(await PagosAsync(cn, f, "RETIVA", ct)));
            case "ARBA_RET":
            {
                var txt = ArbaRetenciones(await PagosAsync(cn, f, "RETIBR", ct));
                var nombre = NombreArba(cuitEmpresa, f.Desde, cfg.GetValueOrDefault(ClaveArbaRetMensual, "SI"), cfg.GetValueOrDefault(ClaveArbaRetActividad, ""));
                return Zip(nombre + ".zip", txt.Length == 0 ? [] : [(nombre + ".TXT", txt)]);
            }
            case "ARBA_PERC":
            {
                var txt = ArbaPercepciones(await VentasAsync(cn, f, ct));
                var nombre = NombreArba(cuitEmpresa, f.Desde, cfg.GetValueOrDefault(ClaveArbaPercMensual, "SI"), cfg.GetValueOrDefault(ClaveArbaPercActividad, ""));
                return Zip(nombre + ".zip", txt.Length == 0 ? [] : [(nombre + ".TXT", txt)]);
            }
            case "ARCIBA":
                return Txt($"ARCIBA_{mes}.TXT", Arciba(await VentasAsync(cn, f, ct)));
            case "SIRCAR_RET":
                return Zip($"SIRCAR_RETENCIONES_{mes}.zip", SircarRetenciones(await PagosAsync(cn, f, "RETIBR", ct), cfg, mes));
            case "SIRCAR_PERC":
                return Zip($"SIRCAR_PERCEPCIONES_{mes}.zip", SircarPercepciones(await VentasAsync(cn, f, ct), cfg, mes));
            default:
                throw new ReglaException("Formato de exportación desconocido.");
        }
    }

    /// <summary>
    /// Cabeceras de V_MV_CPTE con una retención practicada: RETGAN/RETIBR en órdenes de pago y pagos (como SICORE y VT_AFIP_02),
    /// RETIVA en facturas, notas de crédito y débito de venta (percepción de IVA).
    /// </summary>
    private static async Task<List<RetencionPago>> PagosAsync(SqlConnection cn, RetencionesFiltro f, string prefijo, CancellationToken ct)
    {
        if (prefijo is not ("RETGAN" or "RETIBR" or "RETIVA")) throw new ArgumentOutOfRangeException(nameof(prefijo));
        var tcs = prefijo == "RETIVA"
            ? "LTRIM(RTRIM(v.TC)) IN ('FC','TK','TKFC','NC','ND','FCE')"
            : "((LTRIM(RTRIM(v.TC)) = 'OPG' AND LTRIM(RTRIM(ISNULL(v.TCORIGEN,''))) = 'OPG') OR LTRIM(RTRIM(v.TC)) = 'PG')";
        var sql = $"""
            SELECT LTRIM(RTRIM(v.TC)) Tc, LTRIM(RTRIM(ISNULL(v.SUCURSAL,''))) Sucursal, LTRIM(RTRIM(ISNULL(v.NUMERO,''))) Numero, LTRIM(RTRIM(ISNULL(v.LETRA,''))) Letra,
                   v.FECHA Fecha, LTRIM(RTRIM(v.CUENTA)) Cuenta, LTRIM(RTRIM(ISNULL(v.NOMBRE,''))) Nombre, LTRIM(RTRIM(ISNULL(v.DOMICILIO,''))) Domicilio,
                   LTRIM(RTRIM(ISNULL(v.LOCALIDAD,''))) Localidad, ISNULL(e.DESCRIPCION,'') Provincia, ISNULL(e.CodJurisdiccion,'') CodJurisdiccion,
                   LTRIM(RTRIM(ISNULL(v.CODIGOPOSTAL,''))) CodigoPostal, ISNULL(v.DOCUMENTONUMERO,'') Cuit, LTRIM(RTRIM(ISNULL(CAST(v.CONDICIONIVA AS nvarchar(10)),''))) CondicionIva,
                   ISNULL(v.IMPORTE,0) Importe, v.IMPORTE_S_IVA ImporteSinIva, LTRIM(RTRIM(ISNULL(CAST(v.{prefijo}_IdRetencion AS nvarchar(20)),''))) IdRetencion,
                   ISNULL(v.{prefijo}_BaseImponible,0) Base, ISNULL(v.{prefijo}_Importe,0) Retencion, ISNULL(v.{prefijo}_ALICUOTA,0) Alicuota,
                   LTRIM(RTRIM(ISNULL(CAST(v.{prefijo}_NRO AS nvarchar(30)),''))) Certificado, LTRIM(RTRIM(ISNULL(CAST(v.AlicIva AS nvarchar(10)),''))) AlicuotaIva
            FROM dbo.V_MV_CPTE v
            LEFT JOIN dbo.TA_ESTADOS e ON e.CODIGO = v.IDPROVINCIA
            WHERE {tcs} AND ISNULL(v.{prefijo}_Importe,0) > 0 AND ISNULL(v.ANULADA,0) = 0
              AND v.FECHA >= @Desde AND v.FECHA < DATEADD(day, 1, @Hasta)
              AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(v.UNEGOCIO,''))) = @Unidad)
            ORDER BY v.FECHA, v.TC, v.SUCURSAL, v.NUMERO;
            """;
        return (await cn.QueryAsync<RetencionPago>(new CommandDefinition(sql, new { Desde = f.Desde.Date, Hasta = f.Hasta.Date, Unidad = f.Unidad.Trim() },
            commandTimeout: 180, cancellationToken: ct))).ToList();
    }

    /// <summary>Comprobantes del libro de IVA ventas con percepción de IIBB (ARBA, ARCIBA, SIRCAR).</summary>
    private static async Task<List<PercepcionVenta>> VentasAsync(SqlConnection cn, RetencionesFiltro f, CancellationToken ct)
        => (await cn.QueryAsync<PercepcionVenta>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(a.TC)) Tc, LTRIM(RTRIM(ISNULL(a.SUCURSAL,''))) Sucursal, LTRIM(RTRIM(ISNULL(a.NUMERO,''))) Numero, LTRIM(RTRIM(ISNULL(a.LETRA,''))) Letra,
                   a.FECHA Fecha, ISNULL(a.FechaSubdiario, a.FECHA) FechaContable, ISNULL(NULLIF(LTRIM(RTRIM(a.CABCUIT)),''), ISNULL(ad.NUMERO_DOCUMENTO,'')) Cuit,
                   LTRIM(RTRIM(ISNULL(a.CABNOMBRE,''))) Nombre, LTRIM(RTRIM(ISNULL(NULLIF(LTRIM(RTRIM(a.CABCONDIVA)),''), ISNULL(CAST(ad.IVA AS nvarchar(10)),'')))) CondicionIva,
                   LTRIM(RTRIM(ISNULL(CAST(ad.DOCUMENTO_TIPO AS nvarchar(10)),''))) DocumentoTipo, LTRIM(RTRIM(ISNULL(o.SituacionIB,''))) SituacionIb,
                   CAST(CASE WHEN ISNULL(CAST(o.Convenio AS nvarchar(10)),'') IN ('1','S','SI','True') THEN 1 ELSE 0 END AS bit) Convenio,
                   LTRIM(RTRIM(ISNULL(CAST(v.CodCpteAfip AS nvarchar(10)),''))) CodCpteAfip,
                   ISNULL(a.LIVA_ImpNetoGrav,0) NetoGravado, ISNULL(a.LIVA_ImpNetoNGrav,0) NetoNoGravado, ISNULL(a.LIVA_AlicIVA,0) AlicuotaIva,
                   ISNULL(a.LIVA_ImpIVA,0) + ISNULL(a.LIVA_ImpIva2,0) + ISNULL(a.LIVA_ImpIVA3,0) + ISNULL(a.LIVA_ImpIVA4,0) Iva,
                   ISNULL(a.LIVA_Ret_Perc,0) PercepcionIva, ISNULL(a.LIVA_Ret_IBtos,0) PercepcionIibb, ISNULL(a.LIVA_TOTAL,0) Total,
                   NULLIF(v.RETIBR_BaseImponible,0) BaseIibb, NULLIF(v.RETIBR_ALICUOTA,0) AlicuotaIibb, v.IMPORTE_S_IVA ImporteSinIva,
                   ISNULL(e.DESCRIPCION,'') Provincia, ISNULL(e.CodJurisdiccion,'') CodJurisdiccion
            FROM dbo.LibroIvaVentas a
            LEFT JOIN dbo.MA_CUENTASADIC ad ON ad.CODIGO = a.CUENTA
            LEFT JOIN dbo.MA_CUENTASOBS o ON o.Codigo = a.CUENTA
            LEFT JOIN dbo.V_MV_CPTE v ON v.TC = a.TC AND v.IDCOMPROBANTE = a.SUCURSAL + a.NUMERO + a.LETRA AND v.CUENTA = a.CUENTA
            LEFT JOIN dbo.TA_ESTADOS e ON e.CODIGO = v.IDPROVINCIA
            WHERE a.FECHA >= @Desde AND a.FECHA < DATEADD(day, 1, @Hasta) AND ISNULL(a.LIVA_Ret_IBtos,0) <> 0 AND ABS(ISNULL(a.LIVA_TOTAL,0)) > 0.01
              AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) = @Unidad)
            ORDER BY a.FECHA, a.TC, a.SUCURSAL, a.NUMERO;
            """, new { Desde = f.Desde.Date, Hasta = f.Hasta.Date, Unidad = f.Unidad.Trim() }, commandTimeout: 180, cancellationToken: ct))).ToList();

    /// <summary>Importe con coma decimal y ceros a la izquierda: entero de <paramref name="enteros"/> dígitos, coma, 2 decimales.</summary>
    internal static string ImporteComa(decimal v, int enteros) => Math.Abs(Math.Round(v, 2)).ToString(new string('0', enteros) + ".00", Inv).Replace('.', ',');

    private static string Fecha(DateTime d) => d.ToString("dd/MM/yyyy", Inv);
    private static string Ajustar(string s, int largo) => (s ?? "").Length >= largo ? s![..largo] : (s ?? "").PadRight(largo);
    private static string SinAcentos(string s) => new string((s ?? "").Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('ñ', 'n').Replace('Ñ', 'N');

    /// <summary>Condición frente al impuesto de SICORE desde la condición de IVA del legacy (1→01, 2→02, 6→03, 3/4/5/7→10).</summary>
    internal static string CondicionSicore(string condIva) => condIva.Trim() switch
    {
        "2" => "02", "6" => "03", "3" or "4" or "5" or "7" => "10", _ => "01"
    };

    /// <summary>SICORE retenciones de Ganancias practicadas (OPG/PG), registro de 145 caracteres.</summary>
    internal static string SicoreGanancias(IEnumerable<RetencionPago> pagos, IReadOnlyDictionary<string, string> cfg)
    {
        var sb = new StringBuilder();
        var sinRegimen = new SortedSet<string>();
        foreach (var p in pagos)
        {
            var impuesto = cfg.GetValueOrDefault("RETGANCI_" + p.IdRetencion, "").Trim();
            var regimen = cfg.GetValueOrDefault("RETGANCR_" + p.IdRetencion, "").Trim();
            if (regimen.Length == 0) { sinRegimen.Add(p.IdRetencion.Length > 0 ? p.IdRetencion : "(sin código)"); continue; }
            if (impuesto.Length == 0) impuesto = "217";
            sb.Append("06").Append(Fecha(p.Fecha)).Append(Ajustar(Num(p.Sucursal, 4) + Num(p.Numero, 8) + p.Letra, 16))
              .Append(ImporteComa(p.ImporteSinIva is { } s && s != 0 ? s : p.Importe, 13))
              .Append(impuesto.PadLeft(4, '0')[^4..]).Append(regimen.PadLeft(3, '0')[^3..]).Append('1')
              .Append(ImporteComa(p.Base, 11)).Append(Fecha(p.Fecha)).Append(CondicionSicore(p.CondicionIva)).Append('0')
              .Append(ImporteComa(p.Retencion, 11)).Append(new string(' ', 6)).Append(new string(' ', 10)).Append("80")
              .Append(Ajustar(Digitos(p.Cuit), 20)).Append(new string(' ', 14)).Append("\r\n");
        }
        if (sinRegimen.Count > 0)
            throw new ReglaException($"Falta el código de régimen SICORE de la(s) retención(es) {string.Join(", ", sinRegimen)}: cargalo en Configurar.");
        return sb.ToString();
    }

    /// <summary>Código de provincia de SICORE (00 Capital … 24 Tierra del Fuego).</summary>
    internal static string ProvinciaSicore(string provincia) => JurisdiccionDe(provincia, "") switch
    {
        "901" => "00", "902" => "01", "903" => "02", "904" => "03", "905" => "04", "908" => "05", "910" => "06", "913" => "07", "912" => "08",
        "917" => "09", "918" => "10", "919" => "11", "921" => "12", "922" => "13", "924" => "14", "906" => "16", "907" => "17", "909" => "18",
        "914" => "19", "915" => "20", "911" => "21", "916" => "22", "920" => "23", "923" => "24", _ => "  "
    };

    /// <summary>SICORE sujetos retenidos: un registro de 83 caracteres por CUIT (el legacy repetía uno por pago).</summary>
    internal static string SicoreSujetos(IEnumerable<RetencionPago> pagos)
    {
        var sb = new StringBuilder();
        foreach (var p in pagos.Where(p => Digitos(p.Cuit).Length == 11).GroupBy(p => Digitos(p.Cuit)).Select(g => g.Last()))
            sb.Append(Digitos(p.Cuit)).Append(Ajustar(SinAcentos(p.Nombre), 20)).Append(Ajustar(SinAcentos(p.Domicilio), 20))
              .Append(Ajustar(SinAcentos(p.Localidad), 20)).Append(ProvinciaSicore(p.Provincia)).Append(Ajustar(p.CodigoPostal, 8)).Append("80").Append("\r\n");
        return sb.ToString();
    }

    /// <summary>SICORE percepción de IVA practicada en ventas (régimen 602, impuesto 767), 145 caracteres.</summary>
    internal static string SicorePercepcionIva(IEnumerable<RetencionPago> ventas)
    {
        var sb = new StringBuilder();
        foreach (var p in ventas)
        {
            var codigo = p.Tc switch { "NC" => "03", "ND" => "04", _ => "01" };
            sb.Append(codigo).Append(Fecha(p.Fecha)).Append(Num(p.Sucursal, 5) + Num(p.Numero, 11)).Append(ImporteComa(p.Importe, 13))
              .Append("0767").Append("602").Append('2').Append(ImporteComa(p.Importe - p.Retencion, 11)).Append(Fecha(p.Fecha))
              .Append(p.AlicuotaIva.Trim().StartsWith("21") ? "13" : "14").Append('0').Append(ImporteComa(p.Retencion, 11))
              .Append("  0,00").Append(new string(' ', 10)).Append("80").Append(Ajustar(Digitos(p.Cuit), 20)).Append('0').Append(new string(' ', 13)).Append("\r\n");
        }
        return sb.ToString();
    }

    private static readonly string[] Meses = ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];

    /// <summary>Nombre del lote ARBA: AR-CUIT-AAAAMM{0 mensual | 1/2 quincena}-ACTIVIDAD-LOTE{MES}{1|2}.</summary>
    internal static string NombreArba(string cuit, DateTime desde, string mensual, string actividad)
    {
        if (Digitos(cuit).Length != 11) throw new ReglaException("Falta el CUIT de la empresa en la configuración general (clave CUIT).");
        if (actividad.Trim().Length == 0) throw new ReglaException("Configurá el código de actividad de ARBA (Configurar).");
        var quincena = desde.Day == 1 ? "1" : "2";
        var lote = mensual.Equals("NO", StringComparison.OrdinalIgnoreCase) ? quincena : "0";
        return $"AR-{Digitos(cuit)}-{desde:yyyyMM}{lote}-{actividad.Trim()}-LOTE{Meses[desde.Month - 1]}{quincena}";
    }

    /// <summary>ARBA retenciones de IIBB practicadas (pagos), 47 caracteres.</summary>
    internal static string ArbaRetenciones(IEnumerable<RetencionPago> pagos)
    {
        var sb = new StringBuilder();
        foreach (var p in pagos)
            sb.Append(CuitGuiones(p.Cuit)).Append(Fecha(p.Fecha)).Append(Num(p.Sucursal, 4)).Append(Num(p.Numero, 8))
              .Append(ImporteComa(p.Retencion, 8)).Append('A').Append("\r\n");
        return sb.ToString();
    }

    /// <summary>ARBA percepciones de IIBB practicadas (ventas), 71 caracteres; las NC llevan «-» al principio de los importes.</summary>
    internal static string ArbaPercepciones(IEnumerable<PercepcionVenta> ventas)
    {
        var sb = new StringBuilder();
        foreach (var v in ventas.Where(v => v.NetoGravado != 0))
        {
            var tipo = v.Tc switch { "NC" => "C", "ND" => "D", "CB" => "R", _ => "F" };
            var monto = Math.Abs(Math.Round(v.NetoGravado, 2)).ToString("00000000000.00", Inv);
            var importe = Math.Abs(Math.Round(v.PercepcionIibb, 2)).ToString("0000000000.00", Inv);
            if (v.Tc == "NC") { monto = "-" + monto[1..]; importe = "-" + importe[1..]; }
            sb.Append(CuitGuiones(v.Cuit)).Append(Fecha(v.Fecha)).Append(tipo).Append(Ajustar(v.Letra, 1)).Append(Num(v.Sucursal, 5)).Append(Num(v.Numero, 8))
              .Append(monto).Append(ImporteComa(v.AlicuotaIva, 2)).Append(importe).Append('A').Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>ARCIBA (AGIP, norma 029): percepciones de IIBB practicadas en ventas, registro de 226 caracteres. Sin notas de crédito, como el legacy.</summary>
    internal static string Arciba(IEnumerable<PercepcionVenta> ventas)
    {
        var sb = new StringBuilder();
        foreach (var v in ventas.Where(v => v.Tc != "NC" && v.PercepcionIibb > 0))
        {
            var discrimina = v.Letra is "A" or "M";
            var montoCpte = v.Total - v.PercepcionIibb - v.PercepcionIva - (v.Letra == "B" ? v.Iva : 0);
            var sujeto = v.Letra == "B" ? montoCpte - v.NetoNoGravado : montoCpte - v.Iva - v.NetoNoGravado;
            if (sujeto <= 0) sujeto = v.NetoGravado;
            var alicuota = sujeto == 0 ? 0 : Math.Round(v.PercepcionIibb * 100 / sujeto, 2);
            var tipoCpte = v.Tc is "FC" or "FCE" or "TK" or "TKFC" ? (v.CodCpteAfip is "201" or "206" or "211" ? "10" : "01") : "09";
            var tipoDoc = v.DocumentoTipo switch { "1" => "3", "2" => "2", _ => "1" };
            var cuit = Digitos(v.Cuit);
            var situacionIb = v.Convenio ? "2" : v.CondicionIva == "5" ? "5" : v.SituacionIb is "00" or "0" ? "1" : "4";
            var situacionIva = v.CondicionIva switch { "1" => "1", "4" => v.Letra == "A" ? "1" : "3", "5" or "6" => "4", _ => v.Letra == "A" ? "1" : "4" };
            sb.Append('2').Append("029").Append(Fecha(v.FechaContable)).Append(tipoCpte).Append(Ajustar(v.Letra, 1)).Append(Num(v.Numero, 16))
              .Append(Fecha(v.FechaContable)).Append(ImporteComa(montoCpte, 13)).Append(new string(' ', 16)).Append(tipoDoc).Append(cuit.PadLeft(11, '0')[^11..])
              .Append(situacionIb).Append(situacionIb == "4" ? new string('0', 11) : cuit.PadLeft(11, '0')[^11..]).Append(situacionIva)
              .Append(Ajustar(SinAcentos(v.Nombre), 30)).Append(ImporteComa(v.NetoNoGravado, 13)).Append(ImporteComa(discrimina ? v.Iva : 0, 13))
              .Append(ImporteComa(sujeto, 13)).Append(ImporteComa(alicuota, 2)).Append(ImporteComa(v.PercepcionIibb, 13)).Append(ImporteComa(v.PercepcionIibb, 13))
              .Append(' ').Append(new string(' ', 10)).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Punto(decimal v) => Math.Round(v, 2).ToString("0.00", Inv);

    /// <summary>SIRCAR retenciones de IIBB practicadas (pagos): CSV por jurisdicción.</summary>
    internal static IReadOnlyList<(string Nombre, string Contenido)> SircarRetenciones(IEnumerable<RetencionPago> pagos, IReadOnlyDictionary<string, string> cfg, string mes)
    {
        var (regimen, fija) = Sircar(cfg);
        var lineas = new List<(string Jur, string Linea)>();
        foreach (var p in pagos)
        {
            var jur = fija.Length > 0 ? fija : JurisdiccionDe(p.Provincia, p.CodJurisdiccion);
            if (jur.Length == 0) throw new ReglaException($"El pago {p.Tc} {p.Sucursal}-{p.Numero} no tiene provincia: elegí la jurisdicción de SIRCAR en Configurar.");
            var alicuota = p.Alicuota != 0 ? p.Alicuota : p.Base == 0 ? 0 : p.Retencion * 100 / p.Base;
            lineas.Add((jur, string.Join(",", "1", "1", Num(p.Sucursal, 4) + Num(p.Numero, 8), Digitos(p.Cuit), Fecha(p.Fecha),
                Punto(p.Base), Punto(alicuota), Punto(p.Retencion), regimen, jur)));
        }
        return PorJurisdiccion(lineas, "RETENCION", mes);
    }

    /// <summary>SIRCAR percepciones de IIBB practicadas (ventas): CSV por jurisdicción; NC en negativo.</summary>
    internal static IReadOnlyList<(string Nombre, string Contenido)> SircarPercepciones(IEnumerable<PercepcionVenta> ventas, IReadOnlyDictionary<string, string> cfg, string mes)
    {
        var (regimen, fija) = Sircar(cfg);
        var lineas = new List<(string Jur, string Linea)>();
        foreach (var v in ventas)
        {
            var e = v.Letra == "E";
            var (tipo, signo) = v.Tc switch
            {
                "FC" or "TK" or "TKFC" or "FCE" => (e ? "5" : "1", 1m), "NC" => (e ? "106" : "102", -1m), "ND" => ("2", 1m), _ => ("", 0m)
            };
            if (tipo.Length == 0) continue;
            var jur = fija.Length > 0 ? fija : JurisdiccionDe(v.Provincia, v.CodJurisdiccion);
            if (jur.Length == 0) throw new ReglaException($"El comprobante {v.Tc} {v.Sucursal}-{v.Numero} no tiene provincia: elegí la jurisdicción de SIRCAR en Configurar.");
            var percibido = Math.Abs(v.PercepcionIibb);
            decimal baseImp, alicuota;
            if (v.AlicuotaIibb is { } a && a != 0) { alicuota = a; baseImp = v.BaseIibb ?? (percibido * 100 / a); }
            else { baseImp = Math.Abs(v.ImporteSinIva is { } s && s != 0 ? s : v.NetoGravado); alicuota = baseImp == 0 ? 0 : percibido * 100 / baseImp; }
            lineas.Add((jur, string.Join(",", tipo, v.Letra, Num(v.Sucursal, 4) + Num(v.Numero, 8), Digitos(v.Cuit), Fecha(v.Fecha),
                Punto(signo * Math.Abs(baseImp)), Punto(alicuota), Punto(signo * percibido), regimen, jur)));
        }
        return PorJurisdiccion(lineas, "PERCEPCION", mes);
    }

    private static (string Regimen, string Jurisdiccion) Sircar(IReadOnlyDictionary<string, string> cfg)
    {
        var regimen = cfg.GetValueOrDefault(ClaveSircarRegimen, "").Trim();
        if (regimen.Length == 0) throw new ReglaException("Configurá el tipo de régimen de SIRCAR (Configurar).");
        return (regimen.PadLeft(3, '0'), cfg.GetValueOrDefault(ClaveSircarJurisdiccion, "").Trim());
    }

    /// <summary>Un archivo por jurisdicción, con el número de renglón dentro de cada archivo.</summary>
    private static IReadOnlyList<(string Nombre, string Contenido)> PorJurisdiccion(List<(string Jur, string Linea)> lineas, string prefijo, string mes)
        => lineas.GroupBy(l => l.Jur).OrderBy(g => g.Key)
            .Select(g => ($"{prefijo}_{mes}_JUR{g.Key}.TXT", string.Concat(g.Select((l, i) => $"{i + 1},{l.Linea}\r\n")))).ToList();

    private static ContabilidadArchivo Txt(string nombre, string contenido)
    {
        if (contenido.Length == 0) throw new ReglaException("No hay movimientos para exportar en ese período.");
        return new ContabilidadArchivo(nombre, "text/plain", Encoding.Latin1.GetBytes(contenido));
    }

    private static ContabilidadArchivo Zip(string nombre, IReadOnlyList<(string Nombre, string Contenido)> archivos)
    {
        if (archivos.Count == 0) throw new ReglaException("No hay movimientos para exportar en ese período.");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (n, c) in archivos)
            {
                using var w = zip.CreateEntry(n).Open();
                var b = Encoding.Latin1.GetBytes(c); w.Write(b, 0, b.Length);
            }
        return new ContabilidadArchivo(nombre, "application/zip", ms.ToArray());
    }

    private static ContabilidadArchivo Excel(RetencionesResultado r)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Retenciones");
        ws.Cell(1, 1).Value = $"Retenciones y percepciones {(r.Filtro.Alcance == "TODAS" ? "" : r.Filtro.Alcance.ToLowerInvariant())} · {r.Filtro.Desde:dd/MM/yyyy} al {r.Filtro.Hasta:dd/MM/yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        string[] t = ["Fecha", "Sufrida/Practicada", "Impuesto", "Clase", "Origen", "Comprobante", "CUIT", "Nombre", "Cuenta", "Jurisdicción", "Certificado", "Base", "Importe"];
        for (var i = 0; i < t.Length; i++) ws.Cell(3, i + 1).Value = t[i];
        ws.Range(3, 1, 3, t.Length).Style.Font.Bold = true;
        ws.Range(3, 1, 3, t.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8ECF2");
        var fila = 4;
        foreach (var x in r.Filas)
        {
            ws.Cell(fila, 1).Value = x.Fecha; ws.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(fila, 2).Value = x.Sufrida ? "Sufrida" : "Practicada"; ws.Cell(fila, 3).Value = x.Impuesto; ws.Cell(fila, 4).Value = x.Clase;
            ws.Cell(fila, 5).Value = x.Origen; ws.Cell(fila, 6).Value = x.Comprobante; ws.Cell(fila, 7).Value = x.Cuit; ws.Cell(fila, 8).Value = x.Nombre;
            ws.Cell(fila, 9).Value = $"{x.Cuenta} {x.CuentaNombre}"; ws.Cell(fila, 10).Value = x.JurisdiccionNombre; ws.Cell(fila, 11).Value = x.Certificado;
            if (x.Base is { } b) ws.Cell(fila, 12).Value = b;
            ws.Cell(fila, 13).Value = x.Importe;
            fila++;
        }
        ws.Range(4, 12, fila, 13).Style.NumberFormat.Format = "#,##0.00";
        fila++;
        foreach (var tot in r.Totales)
        {
            ws.Cell(fila, 8).Value = $"Total {(tot.Sufrida ? "sufridas" : "practicadas")} · {tot.Impuesto} · {tot.Clase} ({tot.Cantidad})";
            ws.Cell(fila, 13).Value = tot.Importe; ws.Cell(fila, 13).Style.NumberFormat.Format = "#,##0.00"; ws.Row(fila).Style.Font.Bold = true;
            fila++;
        }
        ws.Columns().AdjustToContents(); ws.SheetView.FreezeRows(3);
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        return new ContabilidadArchivo($"Retenciones_{r.Filtro.Desde:yyyyMM}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ms.ToArray());
    }

    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            var permitido = false;
            foreach (var m in Menus) if (await permissions.HasAccessAsync(m, ct)) { permitido = true; break; }
            if (!permitido) throw new ReglaException("Tu usuario no tiene permiso para retenciones y percepciones (SIFERE, IVA sufridas o libros de IVA).");
            await using var cn = new SqlConnection(sessions.GetConnectionString()); await cn.OpenAsync(ct);
            return await operacion(cn);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("Retenciones", accion, ex, "No se pudieron obtener las retenciones y percepciones.", ct: ct);
            throw new AppUserFacingException("No se pudieron obtener las retenciones y percepciones.", codigo, ex);
        }
    }
    private Task EjecutarAsync(string accion, Func<SqlConnection, Task<bool>> operacion, CancellationToken ct) => EjecutarAsync<bool>(accion, operacion, ct);
}

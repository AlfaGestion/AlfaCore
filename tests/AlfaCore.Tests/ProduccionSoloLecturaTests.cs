using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>Corre solo si ALFACORE_TEST_PROD_SQL apunta a una base de producción.</summary>
public sealed class ProduccionSoloLecturaFactAttribute : FactAttribute
{
    public ProduccionSoloLecturaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_PROD_SQL")))
            Skip = "Definí ALFACORE_TEST_PROD_SQL (base de producción, solo lectura).";
    }
}

/// <summary>
/// Pruebas contra una base de producción con datos abundantes, sin grabar nada: usa directamente las funciones de lectura
/// y armado de archivos (sin auditoría ni permisos, que escriben en la base). Los archivos quedan en ALFACORE_TEST_OUT.
/// ALFACORE_TEST_ARCA=1 además baja Mis Comprobantes de ARCA con el script configurado en la base y los compara.
/// </summary>
public sealed class ProduccionSoloLecturaTests(ITestOutputHelper output)
{
    private static string Salida()
    {
        var dir = Environment.GetEnvironmentVariable("ALFACORE_TEST_OUT") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "AlfaCoreProduccion");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task<SqlConnection> AbrirAsync()
    {
        var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_PROD_SQL"));
        await cn.OpenAsync();
        return cn;
    }

    private static void Guardar(string nombre, string contenido) => File.WriteAllText(Path.Combine(Salida(), nombre), contenido, System.Text.Encoding.Latin1);

    [ProduccionSoloLecturaFact]
    public async Task RetencionesSufridasYArchivos()
    {
        await using var cn = await AbrirAsync();
        var cfg = new Dictionary<string, string>(await RetencionesService.ConfigAsync(cn, default), StringComparer.OrdinalIgnoreCase);
        output.WriteLine($"Base {cn.Database} · CUIT {cfg.GetValueOrDefault("CUIT")} · CONDIVA {cfg.GetValueOrDefault("CONDIVA")}");
        foreach (var (anio, mes) in new[] { (2026, 9), (2026, 10) })
        {
            var desde = new DateTime(anio, mes, 1);
            var f = new RetencionesFiltro { Desde = desde, Hasta = desde.AddMonths(1).AddDays(-1), Alcance = "TODAS" };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await RetencionesService.LeerAsync(cn, f, default);
            output.WriteLine($"\n== {desde:MM/yyyy}: {r.Filas.Count} movimientos en {sw.ElapsedMilliseconds} ms");
            foreach (var t in r.Totales) output.WriteLine($"  {(t.Sufrida ? "Sufrida" : "Practicada")} {t.Impuesto} {t.Clase}: {t.Cantidad} · {t.Importe:N2}");
            foreach (var a in r.Avisos) output.WriteLine("  Aviso: " + a);
            foreach (var g in r.Filas.GroupBy(x => (x.Origen, x.Tc))) output.WriteLine($"  Origen {g.Key.Origen} ({g.Key.Tc}): {g.Count()}");
            foreach (var x in r.Filas.Take(8))
                output.WriteLine($"  {x.Fecha:dd/MM} {x.Impuesto} {x.Origen} {x.Comprobante} {x.Cuit} {x.Nombre} [{x.JurisdiccionNombre}] cert '{x.Certificado}' {x.Importe:N2} base {x.Base:N2}");
            Assert.All(r.Filas, x => Assert.True(x.Cuenta.StartsWith('1') == x.Sufrida));
            var sinCuit = r.Filas.Count(x => x.Cuit.Length != 11);
            output.WriteLine($"  Sin CUIT de 11 dígitos: {sinCuit}");

            // Si no hay jurisdicción por defecto, se usa 902 solo para probar el archivo (no se graba).
            var cfgPrueba = new Dictionary<string, string>(cfg, StringComparer.OrdinalIgnoreCase);
            if (cfgPrueba.GetValueOrDefault(RetencionesService.ClaveJurisdiccion, "").Length == 0) cfgPrueba[RetencionesService.ClaveJurisdiccion] = "902";
            var tag = $"{desde:yyyyMM}";
            foreach (var (nombre, contenido) in RetencionesService.SifereRetenciones(r.Filas, cfgPrueba, tag))
            {
                Guardar(nombre, contenido);
                var lineas = contenido.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                Assert.All(lineas, l => Assert.Equal(79, l.Length));
                output.WriteLine($"  {nombre}: {lineas.Length} líneas · {lineas.FirstOrDefault()}");
            }
            var detalle = await RetencionesService.PercepcionesPorProvinciaAsync(cn, r.Filas, default);
            output.WriteLine($"  Detalle por provincia (C_MV_CPTE_PERCEPCIONES): {detalle.Count} comprobantes");
            foreach (var (nombre, contenido) in RetencionesService.SiferePercepciones(r.Filas, detalle, cfgPrueba, tag))
            {
                Guardar(nombre, contenido);
                var lineas = contenido.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                Assert.All(lineas, l => Assert.Equal(51, l.Length));
                Assert.DoesNotContain(lineas, l => l.Substring(3, 13) == "0000000000000");
                output.WriteLine($"  {nombre}: {lineas.Length} líneas · {lineas.FirstOrDefault()}");
            }
            foreach (var ret in new[] { true, false })
            {
                var txt = RetencionesService.IvaSufridas(r.Filas, ret, "999");
                if (txt.Length == 0) continue;
                Guardar($"{(ret ? "RET" : "PER")}-IVA{tag}.TXT", txt);
                Assert.All(txt.Split("\r\n", StringSplitOptions.RemoveEmptyEntries), l => Assert.Equal(56, l.Length));
                output.WriteLine($"  F.2002 {(ret ? "retenciones" : "percepciones")}: {txt.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length} líneas");
            }
        }
    }

    [ProduccionSoloLecturaFact]
    public async Task PracticadasConLasConsultasReales()
    {
        await using var cn = await AbrirAsync();
        var cfg = new Dictionary<string, string>(await RetencionesService.ConfigAsync(cn, default), StringComparer.OrdinalIgnoreCase)
        { [RetencionesService.ClaveSircarRegimen] = "1" };
        // Mes con más retenciones de IIBB practicadas en pagos, y mes con más percepciones en ventas.
        var mesPagos = await MesConMasAsync(cn, "SELECT YEAR(FECHA)*100+MONTH(FECHA) FROM dbo.V_MV_CPTE WHERE TC IN ('OPG','PG') AND RETIBR_Importe > 0");
        var mesVentas = await MesConMasAsync(cn, "SELECT YEAR(FECHA)*100+MONTH(FECHA) FROM dbo.LibroIvaVentas WHERE LIVA_Ret_IBtos <> 0");
        output.WriteLine($"Mes con retenciones IIBB en pagos: {mesPagos} · con percepciones IIBB en ventas: {mesVentas}");
        if (mesPagos is { } mp)
        {
            var f = Filtro(mp);
            var pagos = await RetencionesService.PagosAsync(cn, f, "RETIBR", default);
            output.WriteLine($"  Pagos con retención IIBB: {pagos.Count}");
            foreach (var p in pagos.Take(5)) output.WriteLine($"   {p.Fecha:dd/MM/yyyy} {p.Tc} {p.Sucursal}-{p.Numero} {p.Cuit} {p.Nombre} [{p.Provincia}] base {p.Base:N2} ret {p.Retencion:N2} alic {p.Alicuota}");
            var arba = RetencionesService.ArbaRetenciones(pagos);
            Guardar($"ARBA_RET_{mp}.TXT", arba);
            Assert.All(arba.Split("\r\n", StringSplitOptions.RemoveEmptyEntries), l => Assert.Equal(47, l.Length));
            output.WriteLine("  ARBA ret: " + arba.Split("\r\n")[0]);
            try
            {
                foreach (var (n, c) in RetencionesService.SircarRetenciones(pagos, cfg, mp.ToString())) { Guardar(n, c); output.WriteLine($"  {n}: {c.Split("\r\n")[0]}"); }
            }
            catch (InvalidOperationException ex) { output.WriteLine("  SIRCAR ret: " + ex.Message); }
        }
        if (mesVentas is { } mv)
        {
            var ventas = await RetencionesService.VentasAsync(cn, Filtro(mv), default);
            output.WriteLine($"  Ventas con percepción IIBB: {ventas.Count}");
            var arba = RetencionesService.ArbaPercepciones(ventas);
            Assert.All(arba.Split("\r\n", StringSplitOptions.RemoveEmptyEntries), l => Assert.Equal(71, l.Length));
            var arciba = RetencionesService.Arciba(ventas);
            Assert.All(arciba.Split("\r\n", StringSplitOptions.RemoveEmptyEntries), l => Assert.Equal(226, l.Length));
            Guardar($"ARBA_PERC_{mv}.TXT", arba); Guardar($"ARCIBA_{mv}.TXT", arciba);
        }
        var ivaVentas = await RetencionesService.PagosAsync(cn, Filtro(202609), "RETIVA", default);
        var gan = await RetencionesService.PagosAsync(cn, Filtro(202609), "RETGAN", default);
        output.WriteLine($"  09/2026: percepciones IVA en ventas {ivaVentas.Count} · retenciones Ganancias en pagos {gan.Count}");
    }

    private static RetencionesFiltro Filtro(int aaaamm)
    {
        var d = new DateTime(aaaamm / 100, aaaamm % 100, 1);
        return new RetencionesFiltro { Desde = d, Hasta = d.AddMonths(1).AddDays(-1), Alcance = "PRACTICADAS" };
    }

    private static async Task<int?> MesConMasAsync(SqlConnection cn, string sqlMeses)
    {
        await using var cmd = new SqlCommand($"SELECT TOP (1) m FROM ({sqlMeses}) x(m) GROUP BY m ORDER BY COUNT(*) DESC, m DESC;", cn) { CommandTimeout = 300 };
        return await cmd.ExecuteScalarAsync() is int v ? v : null;
    }

    [ProduccionSoloLecturaFact]
    public async Task ArcaMisComprobantesBajaYCompara()
    {
        if (Environment.GetEnvironmentVariable("ALFACORE_TEST_ARCA") != "1") { output.WriteLine("ALFACORE_TEST_ARCA no está en 1: no se baja de ARCA."); return; }
        await using var cn = await AbrirAsync();
        var c = await ArcaDescargaService.LeerConfigAsync(cn, default);
        output.WriteLine($"CUIT ingreso {c.CuitLogin} · representado {c.CuitRepresentado} · clave {(c.TieneClave ? "cargada" : "FALTA")} · python {(c.PythonExiste ? "ok" : "FALTA")} · carpeta {c.Carpeta}");
        var anio = int.Parse(Environment.GetEnvironmentVariable("ALFACORE_TEST_ARCA_PERIODO")?[..4] ?? "2026");
        var mes = int.Parse(Environment.GetEnvironmentVariable("ALFACORE_TEST_ARCA_PERIODO")?[4..] ?? "10");
        foreach (var recibidos in new[] { true, false })
        {
            var tipo = recibidos ? "RECIBIDOS" : "EMITIDOS";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ArcaArchivoDescargado archivo;
            try { archivo = await ArcaDescargaService.DescargarEnConexionAsync(cn, recibidos, anio, mes, visible: false, default); }
            catch (InvalidOperationException ex) { output.WriteLine($"\n{tipo}: no se pudo bajar: {ex.Message}"); continue; }
            output.WriteLine($"\n{tipo}: {archivo.Nombre} ({archivo.Bytes:N0} bytes) en {sw.Elapsed.TotalSeconds:N0} s");
            var carpeta = ArcaDescargaService.CarpetaEfectiva(c.Carpeta, recibidos ? c.ScriptRecibidos : c.ScriptEmitidos);
            var arca = ControlArcaService.Leer(await File.ReadAllBytesAsync(Path.Combine(carpeta, archivo.Nombre)), archivo.Nombre);
            sw.Restart();
            var r = await ControlArcaService.ControlarEnConexionAsync(cn, arca, default);
            output.WriteLine($"  ARCA {r.ComprobantesArca} cptes · {r.TotalArca:N2} | sistema {r.ComprobantesSistema} · {r.TotalSistema:N2} | {r.Desde:dd/MM}–{r.Hasta:dd/MM} en {sw.ElapsedMilliseconds} ms");
            if (r.Aviso is { Length: > 0 }) output.WriteLine("  Aviso: " + r.Aviso);
            foreach (var e in Enum.GetValues<ControlArcaEstado>()) output.WriteLine($"  {e}: {r.Cantidad(e)}");
            foreach (var x in r.Filas.Where(x => x.Estado != ControlArcaEstado.Coincide).Take(12))
                output.WriteLine($"   {x.Estado} {x.Clase} {x.Letra} {x.PuntoVenta:0000}-{x.Numero:00000000} {x.Cuit} {x.Nombre} ARCA {x.TotalArca:N2} sist {x.TotalSistema:N2} {x.ComprobanteSistema} {x.Observacion}");
            if (recibidos)
            {
                // Armado del asiento de importación de los faltantes (sin grabar): debe balancear.
                var faltan = r.Filas.Where(x => x.Estado == ControlArcaEstado.FaltaEnSistema && x.Arca is not null).ToList();
                var errores = 0;
                foreach (var x in faltan)
                {
                    var a = ControlArcaImportacionService.Armar(x.Arca!, "211010001", "511010001", "113010001", "113010002");
                    if (a.Error is { Length: > 0 }) { errores++; output.WriteLine($"   No se arma {x.Clase} {x.Letra} {x.PuntoVenta}-{x.Numero}: {a.Error}"); continue; }
                    var debe = a.Lineas.Where(l => l.DebeHaber == "D").Sum(l => l.Importe);
                    var haber = a.Lineas.Where(l => l.DebeHaber == "H").Sum(l => l.Importe);
                    Assert.Equal(Math.Round(debe, 2), Math.Round(haber, 2));
                }
                output.WriteLine($"  Faltantes importables: {faltan.Count - errores} de {faltan.Count} (asientos balanceados)");
            }
        }
    }
}

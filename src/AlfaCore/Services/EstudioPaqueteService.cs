using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AlfaCore.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IEstudioPaqueteService
{
    Task<EstudioPaqueteOpciones> OpcionesAsync(int anio, int mes, string unidad, CancellationToken ct = default);
    Task<EstudioPaqueteResultado> GenerarAsync(EstudioPaqueteSolicitud solicitud, IProgress<EstudioPaqueteAvance>? avance = null, CancellationToken ct = default);
}

/// <summary>
/// Paquete mensual para el estudio contable, como «Datos para Contadores» (D9832, UT_EstudiosContables) del legacy:
/// libros de IVA en PDF y Excel, Libro IVA Digital, asientos del mes (sin tiques, proformas ni cobranzas), detalle de ventas
/// y compras, más los PDF de las facturas emitidas y los archivos de las compras recibidas. El zip se arma en disco
/// (puede pesar cientos de MB) y se baja por un enlace con token que vence.
/// </summary>
public sealed class EstudioPaqueteService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions,
    IAppEventService events, ILibroIvaService librosIva, IContabilidadReportesService reportes, IFacturaDocumentService facturas,
    IInterfacesService interfaces, EstudioPaqueteDescargas descargas) : IEstudioPaqueteService
{
    internal const string MenuPaquete = "D9832";
    internal static readonly string[] Menus = [MenuPaquete, "D6009", "D5004", "D957002"];
    /// <summary>Como el legacy: el Excel de asientos deja afuera tiques, proformas y cobranzas.</summary>
    internal static readonly string[] TcsFueraDeAsientos = ["TK", "TKFC", "FP", "NCFP", "CB", "CBFP", "CBCT"];
    internal const int MaximoFacturas = 3000;
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    public Task<EstudioPaqueteOpciones> OpcionesAsync(int anio, int mes, string unidad, CancellationToken ct = default) => EjecutarAsync("Opciones", async cn =>
    {
        var (desde, hasta) = Periodo(anio, mes);
        var unidades = (await cn.QueryAsync<CompraCargaOpcion>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(Codigo)) Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion FROM dbo.V_TA_UnidadNegocio ORDER BY Codigo;", cancellationToken: ct))).ToList();
        var ventas = await cn.QuerySingleAsync<(int A, int Total)>(new CommandDefinition($"""
            SELECT ISNULL(SUM(CASE WHEN LTRIM(RTRIM(LETRA)) IN ('A','M') THEN 1 ELSE 0 END),0), COUNT(*) FROM dbo.V_MV_CPTE
            WHERE {FiltroVentas};
            """, new { Desde = desde, Hasta = hasta, Unidad = unidad.Trim() }, commandTimeout: 120, cancellationToken: ct));
        var compras = await ComprasConArchivoAsync(cn, desde, hasta, unidad, ct);
        return new EstudioPaqueteOpciones(unidades, ventas.A, ventas.Total, compras.Count);
    }, ct);

    private const string FiltroVentas = """
        LTRIM(RTRIM(TC)) IN ('FC','NC','ND') AND ISNULL(ANULADA,0) = 0 AND FECHA >= @Desde AND FECHA < DATEADD(day, 1, @Hasta)
          AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) = @Unidad)
        """;

    internal static (DateTime Desde, DateTime Hasta) Periodo(int anio, int mes)
    {
        if (anio is < 2000 or > 2100 || mes is < 1 or > 12) throw new ReglaException("Elegí un mes válido.");
        var desde = new DateTime(anio, mes, 1);
        return (desde, desde.AddMonths(1).AddDays(-1));
    }

    public Task<EstudioPaqueteResultado> GenerarAsync(EstudioPaqueteSolicitud s, IProgress<EstudioPaqueteAvance>? avance = null, CancellationToken ct = default)
        => EjecutarAsync("Generar", async cn =>
        {
            var (desde, hasta) = Periodo(s.Anio, s.Mes);
            var unidad = s.Unidad.Trim();
            var empresa = (await cn.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT TOP (1) LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('RAZONSOCIAL','NOMBREEMPRESA','EMPRESA') AND ISNULL(VALOR,'') <> '' ORDER BY CLAVE DESC;",
                cancellationToken: ct)) ?? "").Trim();
            var nombre = $"Paquete_estudio_{desde:yyyyMM}{(unidad.Length > 0 ? "_UN" + unidad : "")}.zip";
            var avisos = new List<string>();
            var (ruta, token) = descargas.Nuevo(nombre, users.GetCurrentUserName());
            var archivos = 0;
            try
            {
                await using (var fs = new FileStream(ruta, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    void Agregar(string entrada, byte[] contenido, bool comprimir = true)
                    {
                        var e = zip.CreateEntry(entrada, comprimir ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
                        using var w = e.Open(); w.Write(contenido, 0, contenido.Length);
                        archivos++;
                    }
                    async Task Paso(string titulo, Func<Task> paso)
                    {
                        avance?.Report(new EstudioPaqueteAvance(titulo, 0, 0));
                        try { await paso(); }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception ex) when (ex is InvalidOperationException or AppUserFacingException)
                        { avisos.Add($"{titulo}: {ex.Message}"); }
                    }
                    var filtroIva = (bool compras) => new LibroIvaFiltro { Compras = compras, Desde = desde, Hasta = hasta, UnidadNegocio = unidad };

                    if (s.LibrosIva)
                        foreach (var compras in new[] { false, true })
                        {
                            var libro = compras ? "compras" : "ventas";
                            await Paso($"Libro IVA {libro} (PDF)", async () => { var a = await librosIva.ExportarPdfAsync(filtroIva(compras), ct); Agregar($"01_Libros_IVA/{a.Nombre}", a.Contenido); });
                            await Paso($"Libro IVA {libro} (Excel)", async () => { var a = await librosIva.ExportarExcelAsync(filtroIva(compras), ct); Agregar($"01_Libros_IVA/{a.Nombre}", a.Contenido, false); });
                        }

                    if (s.LibroIvaDigital)
                        foreach (var compras in new[] { false, true })
                        {
                            var libro = compras ? "compras" : "ventas";
                            // El Libro IVA Digital va por CUIT: si la empresa tiene uno solo sale de una vez; si tiene varios, por unidad.
                            LibroIvaArchivo? unico = null;
                            if (unidad.Length > 0)
                                await Paso($"Libro IVA Digital {libro}", async () => unico = await librosIva.ExportarTxtArcaAsync(filtroIva(compras), ct));
                            else
                                try { unico = await librosIva.ExportarTxtArcaAsync(filtroIva(compras), ct); }
                                catch (AppUserFacingException ex) { avisos.Add($"Libro IVA Digital {libro}: {ex.Message}"); continue; }
                                catch (InvalidOperationException) { /* varios CUIT: va por unidad */ }
                            if (unico is not null) { Agregar($"02_Libro_IVA_Digital/{unico.Nombre}", unico.Contenido, false); continue; }
                            if (unidad.Length > 0) continue;
                            var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var compartidos = new List<string>();
                            foreach (var u in (await librosIva.GetUnidadesAsync(compras, ct)).Select(x => x.Codigo).Distinct())
                                await Paso($"Libro IVA Digital {libro} (unidad {u})", async () =>
                                {
                                    var a = await librosIva.ExportarTxtArcaAsync(new LibroIvaFiltro { Compras = compras, Desde = desde, Hasta = hasta, UnidadNegocio = u }, ct);
                                    var nombreLid = a.Nombre;
                                    if (!nombres.Add(nombreLid)) { nombreLid = Path.GetFileNameWithoutExtension(a.Nombre) + $"_UN{u}.zip"; compartidos.Add(u); }
                                    Agregar($"02_Libro_IVA_Digital/{nombreLid}", a.Contenido, false);
                                });
                            if (compartidos.Count > 0)
                                avisos.Add($"Libro IVA Digital {libro}: varias unidades comparten CUIT (unidades {string.Join(", ", compartidos)}); quedaron en archivos separados (_UN): unilos antes de importarlos en ARCA.");
                        }

                    if (s.Asientos)
                        await Paso("Asientos del mes (Excel)", async () =>
                        {
                            var periodo = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
                                "SELECT TOP (1) CONVERT(int, PERIODO) FROM dbo.MV_EJERCICIOS WHERE @Desde BETWEEN [FECHA DESDE] AND [FECHA HASTA] ORDER BY PERIODO DESC;",
                                new { Desde = desde }, cancellationToken: ct)) ?? throw new ReglaException("El mes no pertenece a ningún ejercicio.");
                            var f = new ContabilidadFiltro
                            {
                                Periodo = periodo, Desde = desde, Hasta = hasta, Unidades = unidad.Length > 0 ? [unidad] : [], ExcluirTcs = [.. TcsFueraDeAsientos]
                            };
                            var a = await reportes.ExportarDiarioAsync(f, false, false, ct);
                            Agregar($"03_Asientos/Asientos_{desde:yyyyMM}.xlsx", a.Contenido, false);
                        });

                    if (s.Detalles)
                        foreach (var (vista, titulo) in new[] { ("VT_DETALLExLIBROIVA", "ventas"), ("VT_DETALLExLIBROIVA_CPRA", "compras") })
                            await Paso($"Detalle de {titulo} (Excel)", async () =>
                            {
                                var xlsx = await DetalleAsync(cn, vista, $"Detalle de {titulo}", desde, hasta, unidad, ct);
                                if (xlsx is null) throw new ReglaException("la base no tiene la vista del detalle.");
                                Agregar($"04_Detalles/Detalle_{titulo}_{desde:yyyyMM}.xlsx", xlsx, false);
                            });

                    if (s.FacturasVenta)
                        await Paso("Facturas de venta (PDF)", async () =>
                        {
                            var lista = (await cn.QueryAsync<(string Tc, string Id, string Unidad)>(new CommandDefinition($"""
                                SELECT LTRIM(RTRIM(TC)), LTRIM(RTRIM(IDCOMPROBANTE)), LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) FROM dbo.V_MV_CPTE
                                WHERE {FiltroVentas} AND (@SoloA = 0 OR LTRIM(RTRIM(LETRA)) IN ('A','M'))
                                ORDER BY FECHA, TC, IDCOMPROBANTE;
                                """, new { Desde = desde, Hasta = hasta, Unidad = unidad, SoloA = s.FacturasVentaSoloA }, commandTimeout: 120, cancellationToken: ct))).ToList();
                            if (lista.Count > MaximoFacturas)
                            {
                                avisos.Add($"Facturas de venta: son {lista.Count}; el paquete incluye las primeras {MaximoFacturas}. Generalo por unidad de negocio o solo las A.");
                                lista = lista.Take(MaximoFacturas).ToList();
                            }
                            var errores = new List<string>();
                            for (var i = 0; i < lista.Count; i++)
                            {
                                ct.ThrowIfCancellationRequested();
                                var (tc, id, un) = lista[i];
                                if (i % 10 == 0) avance?.Report(new EstudioPaqueteAvance("Facturas de venta (PDF)", i, lista.Count));
                                try { Agregar($"05_Facturas_venta/{tc}_{NombreComprobante(id)}.pdf", await facturas.GeneratePdfAsync(tc, id, un, ct), false); }
                                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                                catch (Exception ex) { errores.Add($"{tc} {NombreComprobante(id)} ({ex.Message})"); }
                            }
                            if (errores.Count > 0)
                                avisos.Add($"Facturas de venta: {errores.Count} no se pudieron generar, por ejemplo {string.Join("; ", errores.Take(3))}.");
                        });

                    if (s.ComprobantesCompra)
                        await Paso("Comprobantes de compra", async () =>
                        {
                            var lista = await ComprasConArchivoAsync(cn, desde, hasta, unidad, ct);
                            var sinArchivo = 0;
                            for (var i = 0; i < lista.Count; i++)
                            {
                                ct.ThrowIfCancellationRequested();
                                if (i % 10 == 0) avance?.Report(new EstudioPaqueteAvance("Comprobantes de compra", i, lista.Count));
                                var x = lista[i];
                                var archivo = await interfaces.GetAttachmentForServeAsync(x.IdAdjunto, ct);
                                if (archivo is null || archivo.RutaCompleta.Contains("://", StringComparison.Ordinal) || !File.Exists(archivo.RutaCompleta)) { sinArchivo++; continue; }
                                var ext = Path.GetExtension(archivo.NombreArchivo is { Length: > 0 } n ? n : archivo.RutaCompleta);
                                Agregar($"06_Comprobantes_compra/{Seguro(x.Proveedor)}_{NombreComprobante(x.Comprobante)}_{x.IdAdjunto}{ext}",
                                    await File.ReadAllBytesAsync(archivo.RutaCompleta, ct), ext is not (".pdf" or ".jpg" or ".jpeg" or ".png" or ".zip"));
                            }
                            if (sinArchivo > 0) avisos.Add($"Comprobantes de compra: {sinArchivo} archivo(s) no están en este servidor (FTP o borrados).");
                            if (lista.Count == 0) avisos.Add("Comprobantes de compra: ninguna compra del mes se cargó con archivo en AlfaCore.");
                        });

                    var leeme = new StringBuilder();
                    leeme.AppendLine($"Paquete para el estudio contable - {empresa}");
                    leeme.AppendLine($"Período: {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}{(unidad.Length > 0 ? $" - Unidad de negocio {unidad}" : "")}");
                    leeme.AppendLine($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm} por {users.GetCurrentUserName()}");
                    leeme.AppendLine();
                    leeme.AppendLine("01 Libros de IVA ventas y compras (PDF y Excel)");
                    leeme.AppendLine("02 Libro IVA Digital (archivos para importar en ARCA)");
                    leeme.AppendLine($"03 Asientos del mes, sin {string.Join(", ", TcsFueraDeAsientos)} (como el legacy). Máximo {ContabilidadReportesService.MaximoRenglonesDiario:N0} renglones.");
                    leeme.AppendLine("04 Detalle por artículo de ventas y compras");
                    leeme.AppendLine($"05 Facturas, notas de crédito y débito emitidas{(s.FacturasVentaSoloA ? " (solo A y M)" : "")}");
                    leeme.AppendLine("06 Archivos de las compras cargadas en AlfaCore");
                    if (avisos.Count > 0) { leeme.AppendLine(); leeme.AppendLine("Avisos:"); foreach (var a in avisos) leeme.AppendLine(" - " + a); }
                    Agregar("LEEME.txt", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(leeme.ToString())).ToArray());
                }
            }
            catch
            {
                descargas.Descartar(token);
                throw;
            }
            var bytes = new FileInfo(ruta).Length;
            // Para el estado del mes en el portal del estudio: "AAAAMM|dd/MM/yyyy HH:mm".
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'ALFACORE_PAQUETE_ESTUDIO_ULTIMO';
                IF @@ROWCOUNT = 0 INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion) VALUES (N'CONTABILIDAD', N'ALFACORE_PAQUETE_ESTUDIO_ULTIMO', @Valor, GETDATE());
                """, new { Valor = FormattableString.Invariant($"{desde:yyyyMM}|{DateTime.Now:dd/MM/yyyy HH:mm}") }, cancellationToken: CancellationToken.None));
            await events.LogAuditAsync("EstudioPaquete", "Generar", "PAQUETE_ESTUDIO", $"{desde:yyyyMM}",
                $"Generó el paquete para el estudio de {desde:MM/yyyy} ({archivos} archivos).", new { s.Anio, s.Mes, unidad, archivos, bytes, avisos }, CancellationToken.None);
            return new EstudioPaqueteResultado(token, nombre, bytes, archivos, avisos);
        }, ct);

    /// <summary>«000100001234A» → «0001-00001234A».</summary>
    internal static string NombreComprobante(string id)
        => id.Length >= 12 && id[..12].All(char.IsDigit) ? $"{id[..4]}-{id[4..12]}{id[12..]}" : Seguro(id);

    internal static string Seguro(string s)
    {
        var limpio = new string((s ?? "").Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray()).Trim('_');
        return limpio.Length > 40 ? limpio[..40] : limpio.Length == 0 ? "sin_nombre" : limpio;
    }

    private sealed record CompraArchivo(long IdAdjunto, string Comprobante, string Proveedor);

    /// <summary>Compras registradas en AlfaCore desde Recepción documental, con su archivo (ALFACORE_COMPRA_OPERACION).</summary>
    private static async Task<List<CompraArchivo>> ComprasConArchivoAsync(SqlConnection cn, DateTime desde, DateTime hasta, string unidad, CancellationToken ct)
    {
        var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT CASE WHEN OBJECT_ID('dbo.ALFACORE_COMPRA_OPERACION') IS NULL OR OBJECT_ID('dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO') IS NULL THEN 0 ELSE 1 END;
            """, cancellationToken: ct));
        if (existe == 0) return [];
        return (await cn.QueryAsync<CompraArchivo>(new CommandDefinition("""
            SELECT DISTINCT adj.IdAdjunto, LTRIM(RTRIM(o.Comprobante)) Comprobante, LTRIM(RTRIM(ISNULL(NULLIF(a.CABNOMBRE,''), a.CUENTA))) Proveedor
            FROM dbo.ALFACORE_COMPRA_OPERACION o
            JOIN dbo.MV_ASIENTOS a ON a.PERIODO = o.Periodo AND a.MES_OPERATIVO = o.Mes AND a.[NUMERO ASIENTO] = o.Asiento AND a.TIPO_REG = o.TipoRegistro AND a.SECUENCIA = 1
            JOIN dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO adj ON adj.IdComprobanteRecibido = o.DocumentoId AND ISNULL(adj.Eliminado,0) = 0
            WHERE o.DocumentoId IS NOT NULL AND a.FECHA >= @Desde AND a.FECHA < DATEADD(day, 1, @Hasta)
              AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) = @Unidad);
            """, new { Desde = desde, Hasta = hasta, Unidad = unidad.Trim() }, commandTimeout: 120, cancellationToken: ct))).ToList();
    }

    /// <summary>Volcado a Excel de la vista de detalle del legacy (VT_DETALLExLIBROIVA / _CPRA), como hacía el VB6.</summary>
    private static async Task<byte[]?> DetalleAsync(SqlConnection cn, string vista, string titulo, DateTime desde, DateTime hasta, string unidad, CancellationToken ct)
    {
        if (vista is not ("VT_DETALLExLIBROIVA" or "VT_DETALLExLIBROIVA_CPRA")) throw new ArgumentOutOfRangeException(nameof(vista));
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT CASE WHEN OBJECT_ID('dbo.{vista}') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 0) return null;
        await using var cmd = new SqlCommand($"""
            SELECT * FROM dbo.{vista} WHERE FECHA >= @Desde AND FECHA < DATEADD(day, 1, @Hasta)
              AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) = @Unidad)
            ORDER BY FECHA, TC, IDCOMPROBANTE;
            """, cn) { CommandTimeout = 300 };
        cmd.Parameters.AddWithValue("@Desde", desde); cmd.Parameters.AddWithValue("@Hasta", hasta); cmd.Parameters.AddWithValue("@Unidad", unidad.Trim());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(titulo.Length > 31 ? titulo[..31] : titulo);
        for (var c = 0; c < r.FieldCount; c++) ws.Cell(1, c + 1).Value = r.GetName(c);
        ws.Row(1).Style.Font.Bold = true;
        var fila = 2;
        while (await r.ReadAsync(ct))
        {
            for (var c = 0; c < r.FieldCount; c++)
            {
                if (await r.IsDBNullAsync(c, ct)) continue;
                var cell = ws.Cell(fila, c + 1);
                switch (r.GetValue(c))
                {
                    case DateTime d: cell.Value = d; cell.Style.DateFormat.Format = "dd/MM/yyyy"; break;
                    case decimal m: cell.Value = m; break;
                    case double db: cell.Value = db; break;
                    case float fl: cell.Value = fl; break;
                    case int n: cell.Value = n; break;
                    case long l: cell.Value = l; break;
                    case short sh: cell.Value = sh; break;
                    case bool b: cell.Value = b; break;
                    case var o: cell.Value = Convert.ToString(o)?.Trim() ?? ""; break;
                }
            }
            fila++;
        }
        ws.SheetView.FreezeRows(1);
        if (fila < 5000) ws.Columns().AdjustToContents();
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        return ms.ToArray();
    }

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            var permitido = false;
            foreach (var m in Menus) if (await permissions.HasAccessAsync(m, ct)) { permitido = true; break; }
            if (!permitido) throw new ReglaException("Tu usuario no tiene permiso para generar el paquete del estudio (Datos para contadores o libros de IVA).");
            await using var cn = new SqlConnection(sessions.GetConnectionString()); await cn.OpenAsync(ct);
            return await operacion(cn);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("EstudioPaquete", accion, ex, "No se pudo generar el paquete para el estudio.", ct: ct);
            throw new AppUserFacingException("No se pudo generar el paquete para el estudio.", codigo, ex);
        }
    }
}

/// <summary>Zips generados, en disco (App_Data/estudio_paquetes), con un token por descarga que vence a las 6 horas.</summary>
public sealed class EstudioPaqueteDescargas
{
    private sealed record Item(string Ruta, string Nombre, string Usuario, DateTime Vence);
    private readonly ConcurrentDictionary<string, Item> _items = new();
    private readonly string _carpeta;
    internal static readonly TimeSpan Vigencia = TimeSpan.FromHours(6);

    public EstudioPaqueteDescargas(IWebHostEnvironment env)
    {
        _carpeta = Path.Combine(env.ContentRootPath, "App_Data", "estudio_paquetes");
        Directory.CreateDirectory(_carpeta);
        Limpiar();
    }

    internal (string Ruta, string Token) Nuevo(string nombre, string usuario)
    {
        Limpiar();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var ruta = Path.Combine(_carpeta, token + ".zip");
        _items[token] = new Item(ruta, nombre, usuario, DateTime.UtcNow + Vigencia);
        return (ruta, token);
    }

    internal void Descartar(string token)
    {
        if (_items.TryRemove(token, out var i)) try { File.Delete(i.Ruta); } catch (IOException) { /* se borra en la próxima limpieza */ }
    }

    /// <summary>Ruta y nombre del zip si el token es válido y no venció.</summary>
    public (string Ruta, string Nombre)? Obtener(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 48 || !token.All(Uri.IsHexDigit)) return null;
        if (!_items.TryGetValue(token, out var i) || i.Vence < DateTime.UtcNow || !File.Exists(i.Ruta)) return null;
        return (i.Ruta, i.Nombre);
    }

    private void Limpiar()
    {
        foreach (var (k, v) in _items) if (v.Vence < DateTime.UtcNow) Descartar(k);
        try
        {
            foreach (var f in new DirectoryInfo(_carpeta).EnumerateFiles("*.zip"))
                if (f.LastWriteTimeUtc < DateTime.UtcNow - Vigencia - TimeSpan.FromHours(1)) try { f.Delete(); } catch (IOException) { }
        }
        catch (DirectoryNotFoundException) { }
    }
}

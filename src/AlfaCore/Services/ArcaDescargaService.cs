using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IArcaDescargaService
{
    Task<ArcaDescargaConfig> GetConfigAsync(CancellationToken ct = default);
    Task GuardarConfigAsync(ArcaDescargaConfig config, CancellationToken ct = default);
    Task<IReadOnlyList<ArcaArchivoDescargado>> ArchivosAsync(CancellationToken ct = default);
    Task<byte[]> LeerArchivoAsync(string nombre, CancellationToken ct = default);
    /// <summary>Corre el script de pre-proceso (descarga de Mis Comprobantes) y devuelve el archivo bajado.</summary>
    Task<ArcaArchivoDescargado> DescargarAsync(bool recibidos, int anio, int mes, CancellationToken ct = default);
}

/// <summary>
/// Descarga de «Mis Comprobantes» con el script de pre-proceso del legacy (scripts_preproceso\arca_comprobantes.py, con su
/// python portable al lado). Usa las mismas claves de TA_CONFIGURACION que EM_ImportacionComprobantes: ARCA_CUIT_LOGIN,
/// ARCA_CLAVE (ofuscada con Codifica), ARCA_CUIT_REPRESENTADO, ARCA_CARPETA_DESTINO y PreProceso_ARCA_RECIBIDOS/EMITIDOS.
/// La clave nunca se devuelve ni se registra.
/// </summary>
public sealed class ArcaDescargaService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions,
    IAppEventService events, ArcaDescargaRegistro registro) : IArcaDescargaService
{
    internal const string ClaveAutoRecibidos = "ALFACORE_ARCA_AUTO_RECIBIDOS", ClaveAutoEmitidos = "ALFACORE_ARCA_AUTO_EMITIDOS",
        ClaveAutoHora = "ALFACORE_ARCA_AUTO_HORA", ClaveUltimaRecibidos = "ALFACORE_ARCA_ULTIMA_RECIBIDOS", ClaveUltimaEmitidos = "ALFACORE_ARCA_ULTIMA_EMITIDOS";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnCurso = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly Regex NombreArchivo = new(@"^ARCA_(RECIBIDOS|EMITIDOS)_(\d{11})_(\d{6})\.xlsx?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    public Task<ArcaDescargaConfig> GetConfigAsync(CancellationToken ct = default)
        => EjecutarAsync("Configuracion", cn => LeerConfigAsync(cn, ct), ct);

    public Task GuardarConfigAsync(ArcaDescargaConfig c, CancellationToken ct = default)
        => EjecutarAsync("GuardarConfiguracion", async cn =>
        {
            var cuitLogin = Digitos(c.CuitLogin); var cuitRep = Digitos(c.CuitRepresentado);
            if (cuitLogin.Length is not (0 or 11) || cuitRep.Length is not (0 or 11)) throw new ReglaException("Los CUIT deben tener 11 dígitos.");
            if (!TimeSpan.TryParseExact(c.HoraAuto.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out _)) throw new ReglaException("Indicá la hora en formato HH:MM.");
            if ((c.AutoRecibidos || c.AutoEmitidos) && (cuitLogin.Length == 0 || cuitRep.Length == 0)) throw new ReglaException("Para la descarga automática completá los CUIT y la clave fiscal.");
            var antes = await LeerConfigAsync(cn, ct);
            var valores = new List<(string Clave, string Valor)>
            {
                ("ARCA_CUIT_LOGIN", cuitLogin), ("ARCA_CUIT_REPRESENTADO", cuitRep), ("ARCA_CARPETA_DESTINO", c.Carpeta.Trim()),
                ("PreProceso_ARCA_RECIBIDOS", c.ScriptRecibidos.Trim()), ("PreProceso_ARCA_EMITIDOS", c.ScriptEmitidos.Trim()),
                (ClaveAutoRecibidos, c.AutoRecibidos ? "SI" : "NO"), (ClaveAutoEmitidos, c.AutoEmitidos ? "SI" : "NO"), (ClaveAutoHora, c.HoraAuto.Trim())
            };
            if (!string.IsNullOrEmpty(c.ClaveNueva)) valores.Add(("ARCA_CLAVE", Codifica(c.ClaveNueva.Trim())));
            if ((c.AutoRecibidos || c.AutoEmitidos) && !antes.TieneClave && string.IsNullOrEmpty(c.ClaveNueva))
                throw new ReglaException("Para la descarga automática cargá la clave fiscal.");
            foreach (var (clave, valor) in valores) await GuardarClaveAsync(cn, clave, valor, ct);
            registro.Actualizar(sessions.GetActiveSession()?.BaseId ?? 0, c.AutoRecibidos || c.AutoEmitidos);
            await events.LogAuditAsync("ControlArca", "ConfigurarDescarga", "TA_CONFIGURACION", "ARCA",
                "Configuró la descarga de Mis Comprobantes de ARCA.",
                new { CuitLogin = cuitLogin, CuitRepresentado = cuitRep, c.Carpeta, c.ScriptRecibidos, c.ScriptEmitidos, c.AutoRecibidos, c.AutoEmitidos, c.HoraAuto,
                      CambioClave = !string.IsNullOrEmpty(c.ClaveNueva) }, CancellationToken.None);
            return true;
        }, ct);

    public Task<IReadOnlyList<ArcaArchivoDescargado>> ArchivosAsync(CancellationToken ct = default)
        => EjecutarAsync<IReadOnlyList<ArcaArchivoDescargado>>("Archivos", async cn => Listar(await CarpetaAsync(cn, ct), (await LeerConfigAsync(cn, ct)).CuitRepresentado), ct);

    public Task<byte[]> LeerArchivoAsync(string nombre, CancellationToken ct = default)
        => EjecutarAsync("LeerArchivo", async cn =>
        {
            if (!NombreArchivo.IsMatch(nombre ?? "")) throw new ReglaException("Archivo no válido.");
            var ruta = Path.Combine(await CarpetaAsync(cn, ct), nombre!);
            if (!File.Exists(ruta)) throw new ReglaException("El archivo ya no está en la carpeta de descargas.");
            return await File.ReadAllBytesAsync(ruta, ct);
        }, ct);

    public Task<ArcaArchivoDescargado> DescargarAsync(bool recibidos, int anio, int mes, CancellationToken ct = default)
        => EjecutarAsync("Descargar", async cn =>
        {
            var r = await DescargarEnConexionAsync(cn, recibidos, anio, mes, visible: false, ct);
            await events.LogAuditAsync("ControlArca", "Descargar", "MIS_COMPROBANTES", r.Nombre,
                $"Descargó de ARCA Mis Comprobantes {(recibidos ? "Recibidos" : "Emitidos")} {mes:00}/{anio}.", new { r.Nombre, r.Bytes }, CancellationToken.None);
            return r;
        }, ct);

    // ── Núcleo (también lo usa la descarga automática, sin usuario) ──────────────────────────────

    internal static async Task<ArcaDescargaConfig> LeerConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var v = await ValoresAsync(cn, ct);
        string G(string k) => v.GetValueOrDefault(k.ToUpperInvariant(), "");
        var c = new ArcaDescargaConfig
        {
            CuitLogin = G("ARCA_CUIT_LOGIN"), TieneClave = G("ARCA_CLAVE").Length > 0, CuitRepresentado = G("ARCA_CUIT_REPRESENTADO"),
            Carpeta = G("ARCA_CARPETA_DESTINO"), ScriptRecibidos = G("PreProceso_ARCA_RECIBIDOS"), ScriptEmitidos = G("PreProceso_ARCA_EMITIDOS"),
            AutoRecibidos = G(ClaveAutoRecibidos).Equals("SI", StringComparison.OrdinalIgnoreCase),
            AutoEmitidos = G(ClaveAutoEmitidos).Equals("SI", StringComparison.OrdinalIgnoreCase),
            HoraAuto = G(ClaveAutoHora) is { Length: 5 } h ? h : "07:00",
            UltimaAutoRecibidos = G(ClaveUltimaRecibidos), UltimaAutoEmitidos = G(ClaveUltimaEmitidos)
        };
        c.ScriptRecibidosExiste = c.ScriptRecibidos.Length > 0 && File.Exists(c.ScriptRecibidos);
        c.ScriptEmitidosExiste = c.ScriptEmitidos.Length > 0 && File.Exists(c.ScriptEmitidos);
        var listo = ScriptConPython(c, recibidos: true) ?? ScriptConPython(c, recibidos: false);
        c.Python = listo is { } l ? Python(l)! : Python(c.ScriptRecibidosExiste ? c.ScriptRecibidos : c.ScriptEmitidos) ?? "";
        c.PythonExiste = c.Python.Length > 0 && File.Exists(c.Python);
        var carpeta = CarpetaEfectiva(c.Carpeta, listo ?? (c.ScriptRecibidosExiste ? c.ScriptRecibidos : c.ScriptEmitidos));
        c.CarpetaExiste = carpeta.Length > 0 && Directory.Exists(carpeta);
        if (c.Carpeta.Length == 0) c.Carpeta = carpeta;
        return c;
    }

    internal static async Task<ArcaArchivoDescargado> DescargarEnConexionAsync(SqlConnection cn, bool recibidos, int anio, int mes, bool visible, CancellationToken ct)
    {
        if (anio is < 2000 or > 2100 || mes is < 1 or > 12) throw new ReglaException("Período inválido.");
        var c = await LeerConfigAsync(cn, ct);
        var configurado = recibidos ? c.ScriptRecibidos : c.ScriptEmitidos;
        if (configurado.Length == 0 && (recibidos ? c.ScriptEmitidos : c.ScriptRecibidos).Length == 0)
            throw new ReglaException("No hay un script de pre-proceso configurado para esta opción.");
        // El mismo script baja recibidos y emitidos (el tipo va por parámetro): si el configurado no está en este servidor
        // o no tiene el python portable al lado, se usa el del otro tipo.
        var script = ScriptConPython(c, recibidos) ?? ScriptConPython(c, !recibidos)
            ?? throw new ReglaException(File.Exists(configurado) || File.Exists(recibidos ? c.ScriptEmitidos : c.ScriptRecibidos)
                ? "No se encuentra python.exe junto al script (carpeta python). Instalá el python portable del legacy."
                : $"No se encuentra el script en este servidor: {(configurado.Length > 0 ? configurado : recibidos ? c.ScriptEmitidos : c.ScriptRecibidos)}");
        var python = Python(script)!;
        var clave = Decodifica((await ValoresAsync(cn, ct)).GetValueOrDefault("ARCA_CLAVE", ""));
        if (c.CuitLogin.Length == 0 || clave.Length == 0 || c.CuitRepresentado.Length == 0)
            throw new ReglaException("Faltan datos de acceso a ARCA (CUIT de ingreso, clave fiscal o CUIT representado).");
        var carpeta = CarpetaEfectiva(c.Carpeta, script);
        Directory.CreateDirectory(carpeta);
        var tipo = recibidos ? "RECIBIDOS" : "EMITIDOS";
        var periodo = $"{anio:0000}{mes:00}";
        var destino = Path.Combine(carpeta, $"ARCA_{tipo}_{Digitos(c.CuitRepresentado)}_{periodo}.xls");
        var semaforo = EnCurso.GetOrAdd(carpeta, _ => new SemaphoreSlim(1, 1));
        if (!await semaforo.WaitAsync(TimeSpan.Zero, ct)) throw new ReglaException("Ya hay una descarga de ARCA en curso. Esperá a que termine.");
        try
        {
            var psi = new ProcessStartInfo(python)
            {
                WorkingDirectory = Path.GetDirectoryName(script)!, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var a in new[] { script, c.CuitLogin, clave, c.CuitRepresentado, periodo, tipo, carpeta.TrimEnd('\\', '/'), visible ? "SI" : "NO" })
                psi.ArgumentList.Add(a);
            var inicio = DateTime.Now;
            using var p = Process.Start(psi) ?? throw new ReglaException("No se pudo iniciar el script de descarga.");
            _ = p.StandardOutput.ReadToEndAsync(CancellationToken.None); _ = p.StandardError.ReadToEndAsync(CancellationToken.None);
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct); limite.CancelAfter(Timeout);
            try { await p.WaitForExitAsync(limite.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ya terminó */ }
                if (ct.IsCancellationRequested) throw;
                throw new ReglaException("El script superó el tiempo máximo (15 minutos) y se canceló.");
            }
            if (p.ExitCode != 0) throw new ReglaException(MensajeSalida(p.ExitCode) + $" Revisá arca_comprobantes.log en {carpeta}.");
            var info = new FileInfo(destino);
            if (!info.Exists || info.LastWriteTime < inicio.AddMinutes(-1)) throw new ReglaException("El script terminó pero no dejó el archivo esperado: " + info.Name);
            return new ArcaArchivoDescargado(info.Name, recibidos, periodo, info.LastWriteTime, info.Length);
        }
        finally { semaforo.Release(); }
    }

    /// <summary>Script configurado para el tipo, si existe en este servidor y tiene python.exe al lado.</summary>
    internal static string? ScriptConPython(ArcaDescargaConfig c, bool recibidos)
    {
        var s = recibidos ? c.ScriptRecibidos : c.ScriptEmitidos;
        return s.Length > 0 && File.Exists(s) && Python(s) is { } py && File.Exists(py) ? s : null;
    }

    internal static string MensajeSalida(int codigo) => codigo switch
    {
        1 => "Parámetros incorrectos del script.",
        2 => "ARCA rechazó el ingreso: revisá el CUIT y la clave fiscal.",
        3 => "No se pudo elegir el CUIT representado en ARCA.",
        4 => "No se pudo abrir el servicio Mis Comprobantes (¿está adherido en ARCA?).",
        5 => "Falló la consulta del período en Mis Comprobantes.",
        6 => "No se pudo leer la tabla o generar el Excel (el script usa Excel instalado en el servidor).",
        _ => $"El script terminó con error (código {codigo})."
    };

    internal static IReadOnlyList<ArcaArchivoDescargado> Listar(string carpeta, string cuitRepresentado)
    {
        if (carpeta.Length == 0 || !Directory.Exists(carpeta)) return [];
        var cuit = Digitos(cuitRepresentado);
        return new DirectoryInfo(carpeta).EnumerateFiles("ARCA_*.xls*")
            .Select(f => (f, m: NombreArchivo.Match(f.Name)))
            .Where(x => x.m.Success && (cuit.Length == 0 || x.m.Groups[2].Value == cuit))
            .Select(x => new ArcaArchivoDescargado(x.f.Name, x.m.Groups[1].Value.Equals("RECIBIDOS", StringComparison.OrdinalIgnoreCase), x.m.Groups[3].Value, x.f.LastWriteTime, x.f.Length))
            .OrderByDescending(x => x.Periodo).ThenBy(x => x.Recibidos ? 0 : 1).Take(48).ToList();
    }

    /// <summary>python portable del legacy: …\python\python.exe al lado de la carpeta del script (App.path del exe).</summary>
    internal static string? Python(string script)
    {
        if (script.Length == 0) return null;
        var dir = Path.GetDirectoryName(script);
        foreach (var d in new[] { dir, Path.GetDirectoryName(dir ?? "") })
            if (!string.IsNullOrEmpty(d) && Path.Combine(d, "python", "python.exe") is var py && File.Exists(py)) return py;
        return string.IsNullOrEmpty(Path.GetDirectoryName(dir ?? "")) ? null : Path.Combine(Path.GetDirectoryName(dir!)!, "python", "python.exe");
    }

    /// <summary>ARCA_CARPETA_DESTINO o, como el legacy, App.path\ARCA_Descargas.</summary>
    internal static string CarpetaEfectiva(string carpeta, string script)
    {
        if (carpeta.Trim().Length > 0) return carpeta.Trim();
        var dir = Path.GetDirectoryName(Path.GetDirectoryName(script) ?? "");
        return string.IsNullOrEmpty(dir) ? "" : Path.Combine(dir, "ARCA_Descargas");
    }

    private static async Task<string> CarpetaAsync(SqlConnection cn, CancellationToken ct)
    {
        var c = await LeerConfigAsync(cn, ct);
        return CarpetaEfectiva(c.Carpeta, c.ScriptRecibidos.Length > 0 ? c.ScriptRecibidos : c.ScriptEmitidos);
    }

    private static async Task<Dictionary<string, string>> ValoresAsync(SqlConnection cn, CancellationToken ct)
        => (await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) LIKE 'ARCA[_]%' OR UPPER(LTRIM(RTRIM(CLAVE))) LIKE 'PREPROCESO[_]ARCA[_]%'
               OR UPPER(LTRIM(RTRIM(CLAVE))) LIKE 'ALFACORE[_]ARCA[_]AUTO%' OR UPPER(LTRIM(RTRIM(CLAVE))) LIKE 'ALFACORE[_]ARCA[_]ULTIMA%';
            """, cancellationToken: ct))).GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor, StringComparer.OrdinalIgnoreCase);

    internal static Task GuardarClaveAsync(SqlConnection cn, string clave, string valor, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);
            IF @@ROWCOUNT = 0 INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion) VALUES (N'CONTABILIDAD', @Clave, @Valor, GETDATE());
            """, new { Clave = clave, Valor = valor }, cancellationToken: ct));

    /// <summary>Utl.Codifica del legacy (DLL_Util): caracteres invertidos, cada uno como Asc + largo en tres dígitos.</summary>
    internal static string Codifica(string palabra)
    {
        var bytes = Encoding.Latin1.GetBytes(palabra);
        var sb = new StringBuilder();
        for (var i = bytes.Length - 1; i >= 0; i--) sb.Append((bytes[i] + bytes.Length).ToString("000", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>Utl.Decodifica del legacy.</summary>
    internal static string Decodifica(string palabra)
    {
        palabra = (palabra ?? "").Trim();
        if (palabra.Length < 3) return "";
        var largo = palabra.Length / 3;
        var bytes = new List<byte>();
        for (var x = palabra.Length; x >= 3; x -= 3)
            if (int.TryParse(palabra.AsSpan(x - 3, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n - largo is >= 0 and <= 255)
                bytes.Add((byte)(n - largo));
        return Encoding.Latin1.GetString(bytes.ToArray());
    }

    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            if (!await permissions.HasAccessAsync(ControlArcaImportacionService.MenuImportar, ct))
                throw new ReglaException("Tu usuario no tiene permiso para la descarga de ARCA (Importar Comprobantes).");
            await using var cn = new SqlConnection(sessions.GetConnectionString()); await cn.OpenAsync(ct);
            return await operacion(cn);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("ControlArca", accion, ex, "No se pudo completar la descarga de ARCA.", ct: ct);
            throw new AppUserFacingException("No se pudo completar la descarga de ARCA.", codigo, ex);
        }
    }
    private Task EjecutarAsync(string accion, Func<SqlConnection, Task<bool>> operacion, CancellationToken ct) => EjecutarAsync<bool>(accion, operacion, ct);
}

/// <summary>Bases con descarga automática (archivo local del servidor AlfaCore, que es donde corre el script).</summary>
public sealed class ArcaDescargaRegistro(IWebHostEnvironment env)
{
    private readonly object _lock = new();
    private string Ruta => Path.Combine(env.ContentRootPath, "App_Data", "arca_descarga_auto.json");

    public IReadOnlySet<int> Bases()
    {
        lock (_lock)
        {
            try { return File.Exists(Ruta) ? JsonSerializer.Deserialize<HashSet<int>>(File.ReadAllText(Ruta)) ?? [] : []; }
            catch { return new HashSet<int>(); }
        }
    }

    public void Actualizar(int idBase, bool activa)
    {
        lock (_lock)
        {
            var actual = new HashSet<int>(Bases());
            if (!(activa ? actual.Add(idBase) : actual.Remove(idBase))) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            File.WriteAllText(Ruta, JsonSerializer.Serialize(actual));
        }
    }
}

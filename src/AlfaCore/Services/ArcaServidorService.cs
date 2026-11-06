using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

/// <summary>Valores propios de esta instalación (Administrar → Servidor). Vacío = usar lo que trae AlfaCore.</summary>
public sealed record ArcaServidorConfiguracion
{
    public string PythonExe { get; init; } = "";
    public string Script { get; init; } = "";
    public string Carpeta { get; init; } = "";
    /// <summary>Meses de descargas que se conservan por base (0 = no borrar nunca).</summary>
    public int RetencionMeses { get; init; } = 13;
    /// <summary>Días que se conservan capturas, logs intermedios y archivos de diagnóstico.</summary>
    public int DiagnosticoDias { get; init; } = 7;
}

/// <summary>Script, python y carpeta que se usan realmente en este servidor para una base.</summary>
public sealed record ArcaServidorEstado(string Script, bool ScriptExiste, string Python, bool PythonExiste, string Carpeta,
    int RetencionMeses = 13, int DiagnosticoDias = 7);

/// <summary>
/// Descarga de Mis Comprobantes: el script (App_Data\scripts\arca\arca_comprobantes.py) viaja con AlfaCore y no depende del
/// legacy. Lo técnico (python, script alternativo, carpeta raíz de descargas) lo configura solo un superadministrador en
/// Administrar → Servidor y se guarda en App_Data\servidor\arca.json, que se preserva al actualizar. El cliente carga solo
/// sus datos de acceso a ARCA. No lee ni escribe las claves PreProceso_ARCA_* del Alfa Gestión.
/// </summary>
public sealed class ArcaServidorService(IWebHostEnvironment environment, IAppUserSessionService users, IAppEventService events)
{
    private static readonly SemaphoreSlim Escritura = new(1, 1);
    private string Archivo => Path.Combine(environment.ContentRootPath, "App_Data", "servidor", "arca.json");
    private string Propio => Path.Combine(environment.ContentRootPath, "App_Data", "scripts", "arca");

    public ArcaServidorConfiguracion Leer()
    {
        if (!File.Exists(Archivo)) return new();
        return JsonSerializer.Deserialize<ArcaServidorConfiguracion>(File.ReadAllText(Archivo, Encoding.UTF8))
            ?? throw new InvalidOperationException("Revisá la configuración de ARCA en Administrar → Servidor.");
    }

    /// <summary>Sin chequeo de usuario: lo usan la pantalla del cliente y la descarga automática.</summary>
    public ArcaServidorEstado Estado(int idBase)
    {
        var c = Leer();
        var script = c.Script.Length > 0 ? c.Script : Path.Combine(Propio, "arca_comprobantes.py");
        var python = c.PythonExe.Length > 0 ? c.PythonExe : PythonIncluido(Propio);
        var raiz = c.Carpeta.Length > 0 ? c.Carpeta : Path.Combine(environment.ContentRootPath, "App_Data", "arca_descargas");
        return new(script, File.Exists(script), python, File.Exists(python), idBase > 0 ? Path.Combine(raiz, $"base-{idBase}") : raiz,
            c.RetencionMeses, c.DiagnosticoDias);
    }

    internal static string PythonIncluido(string carpeta)
    {
        var portable = Path.Combine(carpeta, "python", "python.exe");
        var entorno = Path.Combine(carpeta, "venv", "Scripts", "python.exe");
        return File.Exists(portable) ? portable : File.Exists(entorno) ? entorno : portable;
    }

    public Task<ArcaServidorConfiguracion> ObtenerAsync(CancellationToken ct = default)
        => Registrar("Leer", () => { ExigirAdministrador(); return Task.FromResult(Leer()); });

    private void ExigirAdministrador()
    {
        if (!users.IsAuthenticated || users.CurrentUser?.SuperAdmin != true)
            throw new InvalidOperationException("Solo un superadministrador puede configurar la descarga de ARCA del servidor.");
    }

    internal static ArcaServidorConfiguracion Validar(ArcaServidorConfiguracion config)
    {
        var n = new ArcaServidorConfiguracion
        {
            PythonExe = config.PythonExe.Trim(), Script = config.Script.Trim(), Carpeta = config.Carpeta.Trim(),
            RetencionMeses = config.RetencionMeses, DiagnosticoDias = config.DiagnosticoDias
        };
        if (n.RetencionMeses is < 0 or > 240) throw new InvalidOperationException("Los meses de conservación deben estar entre 0 (no borrar) y 240.");
        if (n.DiagnosticoDias is < 1 or > 365) throw new InvalidOperationException("Los días de conservación de diagnóstico deben estar entre 1 y 365.");
        if (n.PythonExe.Length > 0 && (!Path.IsPathFullyQualified(n.PythonExe) || !File.Exists(n.PythonExe)
            || !Path.GetFileName(n.PythonExe).Equals("python.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Indicá la ruta completa de un python.exe existente en este servidor.");
        if (n.Script.Length > 0 && (!Path.IsPathFullyQualified(n.Script) || !n.Script.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || !File.Exists(n.Script)))
            throw new InvalidOperationException("El script debe ser un archivo .py existente en este servidor.");
        if (n.Carpeta.Length > 0 && !Path.IsPathFullyQualified(n.Carpeta))
            throw new InvalidOperationException("La carpeta de descargas debe ser una ruta completa de este servidor.");
        return n;
    }

    public Task GuardarAsync(ArcaServidorConfiguracion config, CancellationToken ct = default) => Registrar("Guardar", async () =>
    {
        ExigirAdministrador();
        var n = Validar(config);
        await Escritura.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
            var temporal = Archivo + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporal, JsonSerializer.Serialize(n, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), ct);
                File.Move(temporal, Archivo, true);
            }
            finally { if (File.Exists(temporal)) File.Delete(temporal); }
        }
        finally { Escritura.Release(); }
        await events.LogAuditAsync("AdministrarServidor", "GuardarArca", "InstalacionServidor", Environment.MachineName,
            "Configuración del servidor para la descarga de Mis Comprobantes actualizada.", n, ct);
        return true;
    });

    /// <summary>Verifica que python tenga playwright y pywin32 (Excel) y que el script exista. No entra a ARCA.</summary>
    public Task<string> ProbarAsync(ArcaServidorConfiguracion config, CancellationToken ct = default) => Registrar("Probar", async () =>
    {
        ExigirAdministrador();
        var n = Validar(config);
        var e = Estado(0);
        var python = n.PythonExe.Length > 0 ? n.PythonExe : e.Python;
        var script = n.Script.Length > 0 ? n.Script : e.Script;
        if (!File.Exists(script)) throw new InvalidOperationException("No se encuentra el script de descarga en este servidor: " + script);
        if (!File.Exists(python)) throw new InvalidOperationException("No se encuentra python.exe en este servidor: " + python);
        var info = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        info.Environment["PYTHONUTF8"] = "1"; info.Environment["PYTHONDONTWRITEBYTECODE"] = "1"; info.Environment["PLAYWRIGHT_BROWSERS_PATH"] = "0";
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("import sys,playwright,win32com.client; print('Python '+sys.version.split()[0]+' · playwright y pywin32 disponibles')");
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct); limite.CancelAfter(TimeSpan.FromSeconds(30));
        using var proceso = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar Python con la identidad del servicio de AlfaCore.");
        try
        {
            var salida = proceso.StandardOutput.ReadToEndAsync(limite.Token); var error = proceso.StandardError.ReadToEndAsync(limite.Token);
            await proceso.WaitForExitAsync(limite.Token);
            if (proceso.ExitCode != 0)
                throw new InvalidOperationException("Falta playwright o pywin32 en ese python: " + (await error).Trim().Split('\n').LastOrDefault()?.Trim());
            return (await salida).Trim();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new InvalidOperationException("La prueba de Python superó los 30 segundos. Revisá la instalación en el servidor."); }
        finally { if (!proceso.HasExited) { proceso.Kill(entireProcessTree: true); await proceso.WaitForExitAsync(CancellationToken.None); } }
    });

    private async Task<T> Registrar<T>(string accion, Func<Task<T>> trabajo)
    {
        try { return await trabajo(); }
        catch (OperationCanceledException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var mensaje = ex is InvalidOperationException ? ex.Message : "No se pudo completar la configuración de ARCA. Revisá la instalación y los permisos del servicio.";
            var codigo = await events.LogErrorAsync("AdministrarServidor", accion, ex, mensaje);
            throw new AppUserFacingException(mensaje, codigo, ex);
        }
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed record LectoresServidorConfiguracion
{
    public string PythonExe {get;init;}="";
    public string ComprasScriptPath {get;init;}="";
    public string ComprasWorkDir {get;init;}="";
}

// Configuración de esta instalación, compartida entre bases. App_Data se preserva al actualizar.
public sealed class LectoresServidorService(IWebHostEnvironment environment,IAppUserSessionService users,IAppEventService events)
{
    private static readonly SemaphoreSlim Escritura=new(1,1);
    private string Archivo=>Path.Combine(environment.ContentRootPath,"App_Data","servidor","lectores.json");

    public LectoresServidorConfiguracion Leer()
    {
        if(!File.Exists(Archivo))return new();
        return JsonSerializer.Deserialize<LectoresServidorConfiguracion>(File.ReadAllText(Archivo,Encoding.UTF8))
            ??throw new InvalidOperationException("Revisá la configuración de lectores en Administrar → Servidor.");
    }

    public Task<LectoresServidorConfiguracion> ObtenerAsync(CancellationToken ct=default)
        =>Registrar("Leer",()=>{ExigirAdministrador();return Task.FromResult(Leer());},ct);

    internal static string ResolverPython(string? servidor,string? anterior)
        =>!string.IsNullOrWhiteSpace(servidor)?servidor.Trim():!string.IsNullOrWhiteSpace(anterior)?anterior.Trim():"python";
    public string Python(string? anterior)=>ResolverPython(Leer().PythonExe,anterior);

    private void ExigirAdministrador()
    {
        if(!users.IsAuthenticated||users.CurrentUser?.SuperAdmin!=true)
            throw new InvalidOperationException("Solo un superadministrador puede configurar o probar los lectores del servidor.");
    }

    internal static LectoresServidorConfiguracion Validar(LectoresServidorConfiguracion config)
    {
        var normalizada=new LectoresServidorConfiguracion
        {
            PythonExe=config.PythonExe.Trim(),ComprasScriptPath=config.ComprasScriptPath.Trim(),ComprasWorkDir=config.ComprasWorkDir.Trim()
        };
        if(!Path.IsPathFullyQualified(normalizada.PythonExe)||!File.Exists(normalizada.PythonExe)
            ||!Path.GetFileName(normalizada.PythonExe).Equals("python.exe",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Indicá la ruta completa de python.exe instalado en este servidor.");
        if(normalizada.ComprasScriptPath.Length>0&&(!Path.IsPathFullyQualified(normalizada.ComprasScriptPath)
            ||!normalizada.ComprasScriptPath.EndsWith(".py",StringComparison.OrdinalIgnoreCase)||!File.Exists(normalizada.ComprasScriptPath)))
            throw new InvalidOperationException("El script de compras debe ser un archivo .py existente en este servidor.");
        if(normalizada.ComprasWorkDir.Length>0&&(!Path.IsPathFullyQualified(normalizada.ComprasWorkDir)||!Directory.Exists(normalizada.ComprasWorkDir)))
            throw new InvalidOperationException("La carpeta de trabajo debe existir en este servidor.");
        return normalizada;
    }

    public Task GuardarAsync(LectoresServidorConfiguracion config,CancellationToken ct=default)=>Registrar("Guardar",async()=>
    {
        ExigirAdministrador();var normalizada=Validar(config);
        await Escritura.WaitAsync(ct);
        try
        {
            ExigirAdministrador();
            Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
            var anterior=File.Exists(Archivo)?await File.ReadAllBytesAsync(Archivo,ct):null;
            var temporal=Archivo+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                await File.WriteAllTextAsync(temporal,JsonSerializer.Serialize(normalizada,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false),ct);
                ExigirAdministrador();ct.ThrowIfCancellationRequested();File.Move(temporal,Archivo,true);
                try
                {
                    await events.LogAuditAsync("AdministrarServidor","GuardarLectores","InstalacionServidor",Environment.MachineName,
                        "Configuración compartida de lectores documentales actualizada.",normalizada,ct);
                }
                catch
                {
                    // Si no puede auditarse, se restituye la configuración que seguía vigente.
                    if(anterior is null)File.Delete(Archivo);
                    else {await File.WriteAllBytesAsync(temporal,anterior,CancellationToken.None);File.Move(temporal,Archivo,true);}
                    throw;
                }
            }
            finally{if(File.Exists(temporal))File.Delete(temporal);}
        }
        finally{Escritura.Release();}
        return true;
    },ct);

    public Task<string> ProbarAsync(LectoresServidorConfiguracion config,CancellationToken ct=default)=>Registrar("Probar",async()=>
    {
        ExigirAdministrador();var normalizada=Validar(config);
        var info=new ProcessStartInfo(normalizada.PythonExe)
        {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        info.Environment["PYTHONUTF8"]="1";info.Environment["PYTHONDONTWRITEBYTECODE"]="1";
        info.ArgumentList.Add("-c");info.ArgumentList.Add("import sys,pypdf; print('Python '+sys.version.split()[0]+' · pypdf disponible')");
        using var limite=CancellationTokenSource.CreateLinkedTokenSource(ct);limite.CancelAfter(TimeSpan.FromSeconds(30));
        using var proceso=Process.Start(info)??throw new InvalidOperationException("No se pudo iniciar Python con la identidad del servicio de AlfaCore.");
        try
        {
            var salida=proceso.StandardOutput.ReadToEndAsync(limite.Token);var error=proceso.StandardError.ReadToEndAsync(limite.Token);
            await proceso.WaitForExitAsync(limite.Token);
            var resultado=await salida;var detalle=await error;
            if(proceso.ExitCode!=0)throw PdfPythonProceso.ErrorLector(detalle,proceso.ExitCode);
            ExigirAdministrador();return resultado.Trim();
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested)
        {throw new InvalidOperationException("La prueba de Python superó los 30 segundos. Revisá la instalación en el servidor.");}
        finally{if(!proceso.HasExited){proceso.Kill(entireProcessTree:true);await proceso.WaitForExitAsync(CancellationToken.None);}}
    },ct);

    private async Task<T> Registrar<T>(string accion,Func<Task<T>> trabajo,CancellationToken ct)
    {
        try{return await trabajo();}
        catch(OperationCanceledException){throw;}
        catch(AppUserFacingException){throw;}
        catch(Exception ex)
        {
            var mensaje=ex is InvalidOperationException?ex.Message:"No se pudo completar la configuración de lectores. Revisá la instalación y los permisos del servicio.";
            var codigo=await events.LogErrorAsync("AdministrarServidor",accion,ex,mensaje,ct:ct);
            throw new AppUserFacingException(mensaje,codigo,ex);
        }
    }
}

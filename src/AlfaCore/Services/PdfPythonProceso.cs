using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AlfaCore.Models;

namespace AlfaCore.Services;

// Transporte local compartido por extractos y liquidaciones. Nunca publica documentos ni ejecuta un shell.
internal static class PdfPythonProceso
{
    internal static async Task<string> LeerAsync(IWebHostEnvironment environment,ConciliacionArchivo archivo,
        string python,string carpeta,CancellationToken ct)
    {
        var script=Path.Combine(environment.ContentRootPath,"App_Data",carpeta,"leer_pdf.py");
        if(!File.Exists(script))throw new InvalidOperationException($"Falta el lector PDF en el servidor. Volvé a publicar AlfaCore incluyendo App_Data/{carpeta}/leer_pdf.py.");
        if(string.IsNullOrWhiteSpace(python))throw new InvalidOperationException("Configurá el ejecutable Python de Recepción documental en el servidor de AlfaCore.");
        var info=new ProcessStartInfo(python) {RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        info.Environment["PYTHONDONTWRITEBYTECODE"]="1";
        info.Environment["PYTHONUTF8"]="1";
        info.Environment["PYTHONIOENCODING"]="utf-8";
        info.ArgumentList.Add(script);info.ArgumentList.Add(Path.GetFileName(archivo.Nombre));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var process=Iniciar(info);
        try
        {
            var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr=process.StandardError.ReadToEndAsync(timeout.Token);
            IOException? escritura=null;
            try{await process.StandardInput.BaseStream.WriteAsync(archivo.Contenido,timeout.Token);}
            catch(IOException ex){escritura=ex;}
            finally{try{process.StandardInput.Close();}catch(IOException ex){escritura??=ex;}}
            await process.WaitForExitAsync(timeout.Token);
            var output=await stdout;var error=await stderr;
            if(process.ExitCode!=0||escritura is not null)throw ErrorLector(error,process.ExitCode,escritura);
            return output;
        }
        catch(OperationCanceledException ex) when(!ct.IsCancellationRequested)
        {throw new InvalidOperationException("El lector PDF demoró más de dos minutos. Revisá la instalación de Python del servidor o intentá con un PDF más pequeño.",ex);}
        finally
        {
            if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}
        }
    }
    private static Process Iniciar(ProcessStartInfo info)
    {
        try{return Process.Start(info)??throw new InvalidOperationException("No se pudo iniciar el lector PDF.");}
        catch(Win32Exception ex){throw new InvalidOperationException("No se pudo iniciar Python en el servidor de AlfaCore. Revisá el ejecutable de Recepción documental o TARJETAS-PYTHON y los permisos del servicio.",new Exception($"Python configurado: {info.FileName}. {ex.Message}",ex));}
    }
    internal static InvalidOperationException ErrorLector(string error,int salida,Exception? escritura=null)
    {
        var mensaje=error.Contains("pypdf",StringComparison.OrdinalIgnoreCase)&&error.Contains("ModuleNotFoundError",StringComparison.Ordinal)
            ?"Falta instalar pypdf en el Python del servidor de AlfaCore. Instalalo con el mismo python.exe de Recepción documental configurado para este módulo."
            : error.Contains("Microsoft Store",StringComparison.OrdinalIgnoreCase)||salida==9009
            ?"Python no está disponible para el servicio de AlfaCore. Revisá la ruta completa de python.exe configurada en Recepción documental o, para tarjetas, en TARJETAS-PYTHON."
            : error.Contains("Permission denied",StringComparison.OrdinalIgnoreCase)
            ?"El servicio de AlfaCore no tiene permiso para ejecutar el lector PDF. Revisá los permisos de Python y App_Data."
            : error.StartsWith("Extracto:",StringComparison.Ordinal)&&error.Length<400
            ?error.Trim()
            : error.StartsWith("El documento es un extracto de cuenta corriente",StringComparison.Ordinal)
            ?"El PDF es un extracto de cuenta corriente; seleccioná una liquidación de tarjetas."
            : error.StartsWith("Formato no reconocido.",StringComparison.Ordinal)
            ?"El PDF no corresponde a un formato de liquidación de tarjetas reconocido. Revisá el archivo seleccionado."
            :"El lector PDF se cerró sin completar la lectura. Revisá su instalación en el servidor; se registró el detalle para soporte.";
        return new(mensaje,new Exception($"Lector Python: salida={salida}; stderr={error}",escritura));
    }
}

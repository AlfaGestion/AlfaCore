using System.Text.Json;
using System.Text.Json.Serialization;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class TarjetasPdfLector(IWebHostEnvironment environment)
{
    internal static string ResolverPython(string? tarjetas,string? recepcion)
    {
        var propio=tarjetas?.Trim()??"";
        // La actualización inicial guarda "python": ese valor hereda la instalación de Recepción documental.
        if(propio.Length>0&&!propio.Equals("python",StringComparison.OrdinalIgnoreCase))return propio;
        return string.IsNullOrWhiteSpace(recepcion)?"python":recepcion.Trim();
    }
    public async Task<TarjetasLectura> LeerAsync(ConciliacionArchivo archivo,string python,CancellationToken ct)
    {
        if(!Path.GetExtension(archivo.Nombre).Equals(".pdf",StringComparison.OrdinalIgnoreCase) || archivo.Contenido.Length is 0 or >20*1024*1024 ||
            !archivo.Contenido.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidOperationException("Seleccioná un PDF válido de hasta 20 MB.");
        var output=await PdfPythonProceso.LeerAsync(environment,archivo,python,"tarjetas",ct);
        var result=JsonSerializer.Deserialize<TarjetasLectura>(output,new JsonSerializerOptions {NumberHandling=JsonNumberHandling.AllowReadingFromString})
            ?? throw new InvalidOperationException("El lector no devolvió una liquidación válida.");
        if(result.Detalles.Count is 0 or >10000 || result.Bruto<=0 || result.Detalles.Any(d=>d.Importe==0 || d.Cantidad<=0))
            throw new InvalidOperationException("El documento no contiene un detalle conciliable. Requiere revisión del formato.");
        result.DetalleCompleto=result.DetalleCompleto && result.Detalles.Sum(d=>d.Importe)==result.Bruto;
        return result;
    }
}

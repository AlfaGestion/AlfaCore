using System.Text.Json;
using System.Text.Json.Serialization;
using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class ConciliacionExtractoPdfLector(IWebHostEnvironment environment)
{
    public async Task<ConciliacionLectura> LeerAsync(ConciliacionArchivo archivo,BankSource? banco,string python,CancellationToken ct)
    {
        if(!archivo.Contenido.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidOperationException("El archivo seleccionado no tiene una firma PDF válida.");
        var output=await PdfPythonProceso.LeerAsync(environment,archivo,python,"extractos",ct);
        var options=new JsonSerializerOptions {NumberHandling=JsonNumberHandling.AllowReadingFromString};
        options.Converters.Add(new JsonStringEnumConverter());
        var result=JsonSerializer.Deserialize<Resultado>(output,options)
            ??throw new InvalidOperationException("El lector no devolvió un extracto válido.");
        if(result.Banco is not (BankSource.Nacion or BankSource.Hipotecario)||result.Movimientos.Count is 0 or >10000||
            result.Movimientos.Any(m=>m.Source!=result.Banco||m.Amount==0||m.Description.Length==0||m.MovementDate.Year<1900)||
            result.SaldoInicial+result.Movimientos.Sum(m=>m.Amount)!=result.SaldoFinal)
            throw new InvalidOperationException("El detalle del PDF no coincide con los saldos del extracto. No se importó el archivo.");
        if(banco.HasValue&&banco.Value!=result.Banco)
            throw new InvalidOperationException($"El PDF corresponde a {result.Banco}; cambiá el formato bancario seleccionado.");
        return new(result.Banco,result.Movimientos,$"{result.Banco}: {result.Movimientos.Count} movimientos listos para importar. Saldos inicial y final verificados; los saldos no se importan como movimientos.");
    }
    private sealed class Resultado
    {
        public BankSource Banco {get;set;}
        public List<NormalizedMovementDto> Movimientos {get;set;}=[];
        public decimal SaldoInicial {get;set;}
        public decimal SaldoFinal {get;set;}
    }
}

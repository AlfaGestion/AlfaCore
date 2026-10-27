using System.Security.Cryptography;
using AlfaCore.Models;
using Dapper;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService
{
    public Task<ConciliacionArchivo> OriginalAsync(long id,CancellationToken ct=default)
        => Ejecutar("DescargarOriginal",async(cn,token)=>
        {
            if (!await cn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT CONVERT(bit,CASE WHEN COL_LENGTH('dbo.BK_ImportacionExtracto','ArchivoOriginal') IS NOT NULL THEN 1 ELSE 0 END)",cancellationToken:token)))
                throw new InvalidOperationException("Esta importación no conserva el archivo original. Aplicá las actualizaciones para las próximas importaciones.");
            var original=await cn.QuerySingleOrDefaultAsync<OriginalBancario>(new CommandDefinition("SELECT ArchivoNombre Nombre,ArchivoOriginal Contenido,HashArchivo Hash FROM dbo.BK_ImportacionExtracto WHERE ImportacionId=@id",new{id},cancellationToken:token));
            if(original?.Contenido is not {Length:>0})
                throw new InvalidOperationException("Esta importación histórica no conserva el original. No puede reconstruirse a partir de los movimientos.");
            if(!string.Equals(original.Hash,Convert.ToHexString(SHA256.HashData(original.Contenido)),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El archivo conservado no coincide con su huella de importación. Solicitá su revisión a soporte.");
            return new ConciliacionArchivo(Path.GetFileName(original.Nombre),original.Contenido);
        },ct);

    private sealed class OriginalBancario
    {
        public string Nombre {get;set;}="";
        public byte[]? Contenido {get;set;}
        public string Hash {get;set;}="";
    }
}

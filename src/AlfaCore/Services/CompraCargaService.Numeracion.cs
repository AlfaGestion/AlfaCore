using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    private static async Task<(string Sucursal,string Numero)> NumeroInternoAsync(SqlConnection c,CompraCargaBorrador d,CompraCargaTipo tipo,CancellationToken ct,SqlTransaction? tx=null)
    {
        if(d.Letra.Length!=1||!char.IsAsciiLetterUpper(d.Letra[0]))throw new InvalidOperationException("La numeración interna de esta carga admite una letra de comprobante.");
        // V_TA_CPTE solo tiene contadores A y C: la C se reserva para monotributo, el resto usa el contador A (como el legacy con X y R).
        var columna=d.Letra=="C"?"C":"A";
        var datos=await c.QuerySingleAsync<ContadorCompra>(Cmd($"""
            SELECT ISNULL({columna}_ULTIMO_NRO,1) Siguiente,LTRIM(RTRIM(ISNULL({columna}_SUC_DEFAULT,''))) Sucursal
            FROM dbo.V_TA_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(CODIGO))=@Codigo AND Talonario=@Talonario AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@Unidad;
            """,tipo,ct,tx));
        if(!int.TryParse(datos.Sucursal,out var suc)||suc is <1 or >9999)throw new InvalidOperationException("Configurá el punto de venta del comprobante interno en su talonario.");
        var sucursal=suc.ToString("0000");
        var maximo=await c.ExecuteScalarAsync<long>(Cmd("""
            SELECT ISNULL(MAX(CONVERT(bigint,CASE WHEN NUMERO NOT LIKE '%[^0-9]%' AND NUMERO<>'' THEN NUMERO ELSE '0' END)),0)
            FROM dbo.C_MV_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC))=@tc AND SUCURSAL=@sucursal AND LETRA=@letra;
            """,new{tc=d.Tipo.Trim(),sucursal,letra=d.Letra},ct,tx));
        var numero=Math.Max(Math.Max(datos.Siguiente,1),maximo+1);
        if(numero>99999999)throw new InvalidOperationException("Se agotó la numeración del comprobante interno.");
        return(sucursal,numero.ToString("00000000"));
    }
    private sealed class ContadorCompra{public long Siguiente{get;set;}public string Sucursal{get;set;}="";}
}

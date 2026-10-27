using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Comparte la asignación contable con Cobranzas; los contadores comerciales se validan aparte.
internal static class NumeracionAsiento
{
    internal static async Task<int> SiguienteAsync(SqlConnection cn,SqlTransaction tx,string periodo,int mes,string tipo,CancellationToken ct)
    {
        var bloqueo=await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@recurso,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; SELECT @r;
            """,new{recurso=$"ALFACORE_NUMERO_ASIENTO:{periodo}:{mes}:{tipo.Trim()}"},tx,cancellationToken:ct));
        if(bloqueo<0)throw new InvalidOperationException("Hay otra numeración contable en proceso. Volvé a intentar.");
        var ultimo=await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT ISNULL(MAX([NUMERO ASIENTO]),0) FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK)
            WHERE LTRIM(RTRIM(PERIODO))=@periodo AND MES_OPERATIVO=@mes AND LTRIM(RTRIM(TIPO_REG))=@tipo;
            """,new{periodo,mes,tipo=tipo.Trim()},tx,cancellationToken:ct));
        return checked(ultimo+1);
    }
}

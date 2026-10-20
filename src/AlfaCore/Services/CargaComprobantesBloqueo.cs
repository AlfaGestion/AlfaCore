using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Bloqueos de sesión ordenados, también durante CAE y recuperación; libera adquisiciones parciales.</summary>
internal sealed class CargaComprobantesBloqueo(SqlConnection cn) : IAsyncDisposable
{
    private readonly List<string> adquiridos = [];
    public static async Task<CargaComprobantesBloqueo> TomarAsync(SqlConnection cn, IEnumerable<string> recursos, CancellationToken ct)
    {
        var bloqueo = new CargaComprobantesBloqueo(cn);
        try
        {
            foreach (var recurso in recursos.Distinct().Order(StringComparer.Ordinal))
            {
                var resultado = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@Recurso,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @r;
                    """, new { Recurso = recurso }, cancellationToken: ct));
                if (resultado < 0) throw new CargaComprobantesReglaException("Uno de los documentos se está facturando o recuperando desde otra ventana.");
                bloqueo.adquiridos.Add(recurso);
            }
            return bloqueo;
        }
        catch { await bloqueo.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var recurso in adquiridos.AsEnumerable().Reverse())
            await cn.ExecuteAsync(new CommandDefinition("EXEC sys.sp_releaseapplock @Resource=@Recurso,@LockOwner='Session';",
                new { Recurso = recurso }, cancellationToken: CancellationToken.None));
        adquiridos.Clear();
    }
}

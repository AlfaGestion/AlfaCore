using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CobranzasService
{
    internal sealed record AjusteConfig(string Cuenta="",decimal Limite=1m);
    private const string AjusteConfigClave="CONCILIACION-COBRANZA-AJUSTE";
    internal const string CuentasAjusteSql="""
        SELECT LTRIM(RTRIM(c.CODIGO)) Codigo,c.DESCRIPCION Descripcion
        FROM dbo.MA_CUENTAS c
        WHERE ISNULL(c.TITULO,0)=0 AND ISNULL(c.Dada_De_Baja,0)=0 AND LTRIM(RTRIM(c.Moneda))='1'
          AND ISNULL(LTRIM(RTRIM(c.MedioDePago)),'')=''
          AND NOT EXISTS(SELECT 1 FROM dbo.VT_CLIENTES x WHERE LTRIM(RTRIM(x.CODIGO))=LTRIM(RTRIM(c.CODIGO)))
          AND NOT EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES x WHERE LTRIM(RTRIM(x.CODIGO))=LTRIM(RTRIM(c.CODIGO)))
          AND (UPPER(c.DESCRIPCION) LIKE '%REDONDE%' OR UPPER(c.DESCRIPCION) LIKE '%AJUST%'
            OR UPPER(c.DESCRIPCION) LIKE '%GASTO%' OR UPPER(c.DESCRIPCION) LIKE '%DIFEREN%'
            OR UPPER(c.DESCRIPCION) LIKE '%OTROS EGRESOS%' OR UPPER(c.DESCRIPCION) LIKE '%OTROS INGRESOS%')
          AND UPPER(c.DESCRIPCION) NOT LIKE '%CAMBIO%'
          AND (' '+UPPER(c.DESCRIPCION)+' ') NOT LIKE '% IVA %' AND UPPER(c.DESCRIPCION) NOT LIKE '%I.V.A%'
          AND UPPER(c.DESCRIPCION) NOT LIKE '%IMPUEST%' AND UPPER(c.DESCRIPCION) NOT LIKE '%RETENC%'
        ORDER BY CASE WHEN UPPER(c.DESCRIPCION) LIKE '%REDONDE%' THEN 0 ELSE 1 END,c.DESCRIPCION,c.CODIGO;
        """;

    private static async Task<IReadOnlyList<ConciliacionAsientoCuenta>> CuentasAjusteAsync(SqlConnection cn,SqlTransaction? tx,CancellationToken ct)
        =>(await cn.QueryAsync<ConciliacionAsientoCuenta>(Cmd(CuentasAjusteSql,null,tx,ct))).AsList();

    private static async Task<AjusteConfig> LeerAjusteConfigAsync(SqlConnection cn,SqlTransaction? tx,CancellationToken ct)
    {
        var json=await cn.ExecuteScalarAsync<string?>(Cmd("SELECT CONVERT(nvarchar(max),ValorAux) FROM dbo.TA_CONFIGURACION WHERE CLAVE=@clave;",new{clave=AjusteConfigClave},tx,ct));
        var config=string.IsNullOrWhiteSpace(json)?new AjusteConfig():JsonSerializer.Deserialize<AjusteConfig>(json)
            ??throw new InvalidOperationException("Revisá CONCILIACION-COBRANZA-AJUSTE: la configuración está vacía.");
        if(config.Limite<0||config.Limite>100||decimal.Round(config.Limite,2)!=config.Limite)
            throw new InvalidOperationException("El límite de ajuste de cobranzas debe estar entre 0 y 100 pesos, con hasta dos decimales.");
        return config with{Cuenta=config.Cuenta?.Trim()??""};
    }

    private static async Task RecordarCuentaAjusteAsync(SqlConnection cn,SqlTransaction tx,string cuenta,CancellationToken ct)
    {
        var actual=await LeerAjusteConfigAsync(cn,tx,ct);
        var json=JsonSerializer.Serialize(actual with{Cuenta=cuenta});
        await cn.ExecuteAsync(Cmd("""
            UPDATE dbo.TA_CONFIGURACION SET ValorAux=@json,FechaHora_Modificacion=GETDATE() WHERE CLAVE=@clave;
            IF @@ROWCOUNT=0
                INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,ValorAux,DESCRIPCION)
                VALUES('CAJA',@clave,'JSON',@json,N'Ajustes de centavos en cobranzas');
            """,new{clave=AjusteConfigClave,json},tx,ct));
    }
}

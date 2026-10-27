using System.Data;
using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService
{
    public Task<ConciliacionCriterios> CriteriosAsync(CancellationToken ct=default)
        => Ejecutar<ConciliacionCriterios>("ConsultarCriterios",(cn,token)=>LeerCriterios(cn,null,token),ct);

    internal static async Task<ConciliacionCriterios> LeerCriterios(SqlConnection cn,SqlTransaction? tx,CancellationToken ct)
    {
        var rows=await cn.QueryAsync<(string Clave,string Valor)>(new CommandDefinition("""
            SELECT CLAVE,LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE CLAVE IN
                ('CONCILIACION-DIAS-BANCO','CONCILIACION-TOLERANCIA-BANCO','CONCILIACION-COMPARAR-FECHA','CONCILIACION-COMPARAR-REFERENCIA');
            """,transaction:tx,cancellationToken:ct));
        var valores=rows.ToDictionary(r=>r.Clave.Trim(),r=>r.Valor,StringComparer.OrdinalIgnoreCase);
        var c=new ConciliacionCriterios(
            int.Parse(valores.GetValueOrDefault("CONCILIACION-DIAS-BANCO","3"),CultureInfo.InvariantCulture),
            decimal.Parse(valores.GetValueOrDefault("CONCILIACION-TOLERANCIA-BANCO","1.00"),CultureInfo.InvariantCulture),
            valores.GetValueOrDefault("CONCILIACION-COMPARAR-FECHA","SI")=="SI",
            valores.GetValueOrDefault("CONCILIACION-COMPARAR-REFERENCIA","NO")=="SI");
        ValidarCriterios(c);return c;
    }

    internal static void ValidarCriterios(ConciliacionCriterios c)
    {
        if(c.Dias is <0 or >31 || c.Tolerancia is <0 or >100 || decimal.Round(c.Tolerancia,2)!=c.Tolerancia)
            throw new InvalidOperationException("Ingresá un margen de 0 a 31 días y una tolerancia de $0 a $100, con hasta dos decimales.");
    }

    public Task<ConciliacionCriterios> GuardarCriteriosAsync(ConciliacionCriterios criterios,CancellationToken ct=default)
        => Ejecutar<ConciliacionCriterios>("GuardarCriterios",async(cn,token)=>
        {
            ValidarCriterios(criterios);
            var session=sessions.GetActiveSession()!.Id;
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var valores=new[]{
                ("CONCILIACION-DIAS-BANCO",criterios.Dias.ToString(CultureInfo.InvariantCulture)),
                ("CONCILIACION-TOLERANCIA-BANCO",criterios.Tolerancia.ToString("0.00",CultureInfo.InvariantCulture)),
                ("CONCILIACION-COMPARAR-FECHA",criterios.CompararFecha ? "SI" : "NO"),
                ("CONCILIACION-COMPARAR-REFERENCIA",criterios.CompararReferencia ? "SI" : "NO")};
            foreach(var (clave,valor) in valores)
                await cn.ExecuteAsync(new CommandDefinition("""
                    IF EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WITH(UPDLOCK,HOLDLOCK) WHERE CLAVE=@clave)
                        UPDATE dbo.TA_CONFIGURACION SET VALOR=@valor,FechaHora_Modificacion=GETDATE() WHERE CLAVE=@clave;
                    ELSE INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION) VALUES('CAJA',@clave,@valor,N'Criterio de conciliación banco/Alfa');
                    """,new{clave,valor},tx,cancellationToken:token));
            VerificarSesion(session);token.ThrowIfCancellationRequested();tx.Commit();return criterios;
        },ct);

}

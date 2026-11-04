using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    internal static decimal[] ImportesCentros(decimal total,IReadOnlyList<decimal> porcentajes)
    {
        if(total<0||porcentajes.Count==0||porcentajes.Any(p=>p<=0)||porcentajes.Sum()!=100)throw new InvalidOperationException("La distribución debe sumar 100 % con porcentajes positivos.");
        var importes=new decimal[porcentajes.Count];var porcentaje=0m;var aplicado=0m;
        for(var i=0;i<porcentajes.Count;i++)
        {
            porcentaje+=porcentajes[i];var acumulado=i==porcentajes.Count-1?total:CompraCargaPlanificador.Moneda(total*porcentaje/100);
            importes[i]=acumulado-aplicado;aplicado=acumulado;
        }
        return importes;
    }
    private static async Task RegistrarCentrosAsync(SqlConnection c,SqlTransaction tx,CompraCargaBorrador d,
        CompraCargaAsiento linea,string periodo,string tipo,int asiento,CancellationToken ct)
    {
        var centros=d.CentrosCosto.Where(cc=>cc.Cuenta==linea.Cuenta).ToList();
        if(centros.Count==0)return;
        var siguiente=await c.ExecuteScalarAsync<int>(Cmd("""
            SELECT ISNULL(MAX(SECUENCIA),0) FROM dbo.MV_ASIENTOSCCOSTO WITH(UPDLOCK,HOLDLOCK)
            WHERE PERIODO=@periodo AND MES_OPERATIVO=@mes AND [NUMERO ASIENTO]=@asiento AND LTRIM(RTRIM(Tipo_Reg))=@tipo;
            """,new{periodo,mes=d.Fecha.Month,asiento,tipo},ct,tx));
        var total=linea.Debe+linea.Haber;var importes=ImportesCentros(total,centros.Select(c=>c.Porcentaje).ToArray());
        for(var i=0;i<centros.Count;i++)
        {
            var cc=centros[i];
            var importe=importes[i];
            await c.ExecuteAsync(Cmd("""
                INSERT dbo.MV_ASIENTOSCCOSTO(PERIODO,MES_OPERATIVO,[NUMERO ASIENTO],CUENTA,SECUENCIA,[DEBE-HABER],MONEDA,CCOSTO,
                  PORCENTAJE,FechaHora_Grabacion,Tipo_Reg,Importe,Fecha,UNEGOCIO)
                VALUES(@periodo,@mes,@asiento,@cuenta,@secuencia,@sentido,@moneda,@centro,@porcentaje,GETDATE(),@tipo,@importe,@fecha,@unidad);
                """,new{periodo,mes=d.Fecha.Month,asiento,cuenta=linea.Cuenta.Trim(),secuencia=++siguiente,
                    sentido=linea.Debe>0?"D":"H",moneda=CodigoPk.Format("1",4),centro=CodigoPk.Format(cc.Centro,4),porcentaje=cc.Porcentaje,
                    tipo=CodigoPk.Format(tipo,4),importe,fecha=d.Fecha.Date,unidad=CodigoPk.Format(d.Unidad,4)},ct,tx));
        }
    }
}

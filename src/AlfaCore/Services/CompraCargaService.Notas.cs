using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService
{
    public sealed record FacturaNota(int Id,string Comprobante,DateTime Fecha,decimal Saldo,string Tipo);
    public Task<IReadOnlyList<FacturaNota>> FacturasParaNotaAsync(string proveedor,string unidad,string texto,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar<IReadOnlyList<FacturaNota>>(baseEsperada,"FacturasParaNota",async(c,t)=>
        {
            var contexto=await Contexto(c,t);
            if(!contexto.Unidades.Any(u=>u.Codigo==unidad)||texto.Length>100)throw new InvalidOperationException("Revisá la unidad y la búsqueda.");
            return (await c.QueryAsync<FacturaNota>(Cmd("""
                SELECT TOP(30) f.ID Id,f.IDCOMPROBANTE Comprobante,f.FECHA Fecha,v.SALDO Saldo,LTRIM(RTRIM(v.TC)) Tipo
                FROM dbo.C_MV_CPTE f JOIN dbo.CO_CPTES_SALDOS_UNEG v ON LTRIM(RTRIM(v.CUENTA))=LTRIM(RTRIM(f.CUENTA))
                  AND v.SUCURSAL+v.NUMERO+v.LETRA=f.IDCOMPROBANTE AND LTRIM(RTRIM(v.TC)) IN ('FCC',ISNULL((SELECT LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE CLAVE='EQU_FCC'),'FCC'))
                WHERE LTRIM(RTRIM(f.TC))='FCC' AND LTRIM(RTRIM(f.CUENTA))=@proveedor AND LTRIM(RTRIM(f.UNEGOCIO))=@unidad
                  AND LTRIM(RTRIM(v.UNEGOCIO))=@unidad AND ISNULL(f.ANULADA,0)=0 AND v.SALDO>0 AND f.IDCOMPROBANTE LIKE @patron
                ORDER BY f.FECHA DESC,f.ID DESC;
                """,new{proveedor=proveedor.Trim(),unidad=unidad.Trim(),patron="%"+texto.Trim()+"%"},t))).AsList();
        },ct);

    private static async Task AplicarNotaAsync(SqlConnection c,SqlTransaction tx,CompraCargaBorrador d,CompraCargaVista vista,string tcContable,CancellationToken ct)
    {
        if(d.NotaFacturaId is null)
        {
            if(d.NotaAplicar!=0)throw new InvalidOperationException("Seleccioná la factura a la que se aplica la nota.");
            return;
        }
        if(d.Tipo!="NCC"||d.NotaAplicar<=0||d.NotaAplicar>vista.Total||d.NotaAplicar!=CompraCargaPlanificador.Moneda(d.NotaAplicar))throw new InvalidOperationException("La aplicación debe ser positiva, en centavos y no mayor al total de la nota de crédito.");
        var factura=await c.QuerySingleOrDefaultAsync<FacturaNota>(Cmd("""
            SELECT f.ID Id,f.IDCOMPROBANTE Comprobante,f.FECHA Fecha,v.SALDO Saldo,LTRIM(RTRIM(v.TC)) Tipo
            FROM dbo.C_MV_CPTE f WITH(UPDLOCK,HOLDLOCK) JOIN dbo.CO_CPTES_SALDOS_UNEG v ON LTRIM(RTRIM(v.CUENTA))=LTRIM(RTRIM(f.CUENTA))
              AND v.SUCURSAL+v.NUMERO+v.LETRA=f.IDCOMPROBANTE AND LTRIM(RTRIM(v.TC)) IN ('FCC',ISNULL((SELECT LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE CLAVE='EQU_FCC'),'FCC'))
            WHERE f.ID=@id AND LTRIM(RTRIM(f.TC))='FCC' AND LTRIM(RTRIM(f.CUENTA))=@cuenta
              AND LTRIM(RTRIM(f.UNEGOCIO))=@unidad AND LTRIM(RTRIM(v.UNEGOCIO))=@unidad AND ISNULL(f.ANULADA,0)=0 AND ISNULL(f.Bloqueado,0)=0 AND ISNULL(f.Cerrada,0)=0;
            """,new{id=d.NotaFacturaId,cuenta=d.Proveedor.Trim(),unidad=d.Unidad.Trim()},ct,tx))??throw new InvalidOperationException("La factura asociada no está disponible en la misma cuenta y unidad.");
        if(factura.Fecha.Date>d.Fecha.Date||d.NotaAplicar>factura.Saldo)throw new InvalidOperationException("Revisá la fecha de la nota y el saldo actual de la factura asociada.");
        await c.ExecuteAsync(Cmd("""
            INSERT dbo.MV_APLICACION(CUENTA,TC,SUCURSAL,NUMERO,LETRA,TCO_ORIGEN,SUCURSAL_ORIGEN,NUMERO_ORIGEN,LETRA_ORIGEN,IDCOMPLEMENTO_ORIGEN,
              IMPORTE,IDComprobante,IDComprobante_Origen,UNEGOCIO,TC_Print,IdComprobante_Print,Secuencia)
            VALUES(@cuenta,@tc,@suc,@nro,@letra,@origenTc,@origenSuc,@origenNro,@origenLetra,0,@importe,@id,@origenId,@unidad,@tc,@id,1);
            """,new{cuenta=d.Proveedor.Trim(),tc=tcContable.Trim(),suc=vista.IdComprobante[..4],nro=vista.IdComprobante.Substring(4,8),letra=d.Letra,
                origenTc=factura.Tipo.Trim(),origenSuc=factura.Comprobante[..4],origenNro=factura.Comprobante.Substring(4,8),origenLetra=factura.Comprobante[12..],
                importe=d.NotaAplicar,id=vista.IdComprobante,origenId=factura.Comprobante,unidad=CodigoPk.Format(d.Unidad,4)},ct,tx));
    }
}

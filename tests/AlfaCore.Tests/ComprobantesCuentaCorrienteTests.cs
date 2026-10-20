using AlfaCore.Models;
using AlfaCore.Services;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

public sealed partial class CuentaCorrienteVentasTests
{
    [CargaComprobantesSqlFact]
    public async Task ResumidaCoincideConVistaVB6YAnaliticoIncluyeCanceladosYNotasDeCredito()
    {
        using var runtime=new CargaComprobantesArcaRuntime();var service=CrearServicio(runtime);
        await using var cn=ConexionPruebas();await cn.OpenAsync();
        var f=new FiltrosReporteCompras { FechaDesde=null,FechaHasta=null,TamanioPagina=1 };
        var r=await service.GetComprobantesCuentaCorrienteAsync(f,false);
        var totals=await cn.QuerySingleAsync<(int Cantidad,decimal Importe,decimal Aplicado,decimal Pendiente)>("""
            SELECT COUNT(*),SUM(v.IMPORTE),SUM(v.PAGO),SUM(v.SALDO) FROM dbo.VE_CPTES_SALDOS_UNEG v WHERE v.SALDO>0
            AND EXISTS(SELECT 1 FROM dbo.VT_CLIENTES p WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(v.CUENTA)))
            """);
        Assert.Equal(totals.Cantidad,r.TotalRegistros);Assert.Equal(totals.Importe,r.ImporteTotal);
        Assert.Equal(totals.Aplicado,r.AplicadoTotal);Assert.Equal(totals.Pendiente,r.PendienteTotal);
        Assert.Single(r.Items);Assert.True(r.Items[0].Pendiente>0);f.Pagina=2;
        var pag2=await service.GetComprobantesCuentaCorrienteAsync(f,false);Assert.NotEqual(r.Items[0].Orden,pag2.Items[0].Orden);Assert.Equal(r.PendienteTotal,pag2.PendienteTotal);

        var cancelado=await cn.QuerySingleAsync<(string Cuenta,string Tc,string Id,DateTime Fecha)>("""
            SELECT TOP 1 LTRIM(RTRIM(v.CUENTA)),LTRIM(RTRIM(v.TC)),v.SUCURSAL+v.NUMERO+v.LETRA,v.FECHA
            FROM dbo.VE_CPTES_SALDOS_UNEG v WHERE v.SALDO=0 AND v.IMPORTE>0
            AND EXISTS(SELECT 1 FROM dbo.VT_CLIENTES p WHERE p.CTACODIGO=v.CUENTA)
            AND EXISTS(SELECT 1 FROM dbo.MV_APLICACION a WHERE a.CUENTA=v.CUENTA AND a.TCO_ORIGEN=v.TC
                AND a.SUCURSAL_ORIGEN=v.SUCURSAL AND a.NUMERO_ORIGEN=v.NUMERO AND a.LETRA_ORIGEN=v.LETRA AND a.TC IN ('NC','NCFP'))
            ORDER BY v.FECHA DESC;
            """);
        f=new() { Proveedor=cancelado.Cuenta,TipoComprobante=cancelado.Tc,FechaDesde=cancelado.Fecha.Date,FechaHasta=cancelado.Fecha.Date,TamanioPagina=500 };
        var analitico=await service.GetComprobantesCuentaCorrienteAsync(f,true);
        var doc=Assert.Single(analitico.Items,x=>x.IdComprobante==cancelado.Id);
        Assert.Equal(0,doc.Pendiente);Assert.Contains(doc.Aplicaciones,a=>a.Tc is "NC" or "NCFP");
        Assert.Equal(doc.Aplicado,doc.Aplicaciones.Sum(x=>x.Importe));Assert.Equal(0,doc.DiferenciaAplicaciones);
        var parcial=doc.Importe;
        foreach(var a in doc.Aplicaciones){parcial-=a.Importe;Assert.Equal(parcial,a.SaldoRestante);}
        Assert.Equal(doc.Pendiente,parcial);
        Assert.DoesNotContain((await service.GetComprobantesCuentaCorrienteAsync(f,false)).Items,x=>x.IdComprobante==cancelado.Id);
        using var wb=new XLWorkbook(new MemoryStream(new ReporteComprasExcelExporter().ExportarComprobantesCuentaCorriente(analitico.Items,f,true)));
        Assert.Equal(2,wb.Worksheets.Count);Assert.Equal("Pendiente",wb.Worksheet("Comprobantes").Cell(3,9).GetString());
        Assert.Equal(analitico.Items.Sum(x=>x.Importe),wb.Worksheet("Comprobantes").LastRowUsed()!.Cell(7).GetValue<decimal>());
        Assert.Equal(analitico.Items.Sum(x=>x.Aplicaciones.Count),wb.Worksheet("Aplicaciones").LastRowUsed()!.RowNumber()-3);
        f.UnidadesNegocio=["__NO_EXISTE__"];Assert.Empty((await service.GetComprobantesCuentaCorrienteAsync(f,true)).Items);
        f.UnidadesNegocio=[];f.Vendedor="__NO_EXISTE__";Assert.Empty((await service.GetComprobantesCuentaCorrienteAsync(f,false)).Items);
    }

    [CargaComprobantesSqlFact]
    public async Task AplicacionesParcialesCancelacionSobreaplicacionYAmbosSentidosConRollback()
    {
        await using var cn=ConexionPruebas();await cn.OpenAsync();
        var key=await cn.QuerySingleAsync<ReferenciaPrueba>("""
            SELECT TOP 1 LTRIM(RTRIM(v.CUENTA)) Cuenta,LTRIM(RTRIM(v.TC)) Tc,v.SUCURSAL Sucursal,v.NUMERO Numero,v.LETRA Letra,
                v.IMPORTE Importe,v.FECHA Fecha,a.ID IdAplicacion,LTRIM(RTRIM(a.TC)) ContraTc,a.SUCURSAL ContraSucursal,a.NUMERO ContraNumero,a.LETRA ContraLetra
            FROM dbo.VE_CPTES_SALDOS_UNEG v JOIN dbo.MV_APLICACION a ON a.CUENTA=v.CUENTA AND a.TCO_ORIGEN=v.TC
                AND a.SUCURSAL_ORIGEN=v.SUCURSAL AND a.NUMERO_ORIGEN=v.NUMERO AND a.LETRA_ORIGEN=v.LETRA
            WHERE v.TC='FC' AND a.TC IN ('CB','NC') AND v.IMPORTE>100
                AND EXISTS(SELECT 1 FROM dbo.VT_CLIENTES p WHERE p.CTACODIGO=v.CUENTA) ORDER BY a.ID DESC;
            """);
        var antes=await cn.QuerySingleAsync<(int Cantidad,decimal Importe)>("SELECT COUNT(*),SUM(ISNULL(IMPORTE,0)) FROM dbo.MV_APLICACION");
        var f=new FiltrosReporteCompras { Proveedor=key.Cuenta,TipoComprobante=key.Tc,FechaDesde=key.Fecha.Date,FechaHasta=key.Fecha.Date,TamanioPagina=500 };
        await using(var tx=(SqlTransaction)await cn.BeginTransactionAsync())
        {
            try
            {
                await cn.ExecuteAsync("""
                    DELETE FROM dbo.MV_APLICACION WHERE ID<>@IdAplicacion AND LTRIM(RTRIM(CUENTA))=@Cuenta
                    AND ((TC=@Tc AND SUCURSAL=@Sucursal AND NUMERO=@Numero AND LETRA=@Letra)
                        OR (TCO_ORIGEN=@Tc AND SUCURSAL_ORIGEN=@Sucursal AND NUMERO_ORIGEN=@Numero AND LETRA_ORIGEN=@Letra));
                    """,key,tx);
                foreach(var monto in new[]{decimal.Round(key.Importe/4,4),key.Importe,key.Importe+1})
                {
                    await cn.ExecuteAsync("UPDATE dbo.MV_APLICACION SET IMPORTE=@Monto WHERE ID=@Id",new { Monto=monto,Id=key.IdAplicacion },tx);
                    var a=await ReporteComercialService.LeerComprobantesCuentaCorrienteAsync(cn,f,true,transaction:tx);
                    var d=Assert.Single(a.Items,x=>x.IdComprobante==key.Sucursal+key.Numero+key.Letra);
                    Assert.Equal(key.Importe-monto,d.Pendiente);Assert.Equal(monto,d.Aplicado);Assert.Single(d.Aplicaciones);Assert.Equal(0,d.DiferenciaAplicaciones);
                    var pendientes=await ReporteComercialService.LeerComprobantesCuentaCorrienteAsync(cn,f,false,transaction:tx);
                    Assert.Equal(monto<key.Importe,pendientes.Items.Any(x=>x.IdComprobante==d.IdComprobante));
                }
                // La misma aplicación invertida debe seguir siendo contable; un origen NP/RM no lo sería.
                await cn.ExecuteAsync("""
                    UPDATE dbo.MV_APLICACION SET TC=@Tc,SUCURSAL=@Sucursal,NUMERO=@Numero,LETRA=@Letra,
                        TCO_ORIGEN=@ContraTc,SUCURSAL_ORIGEN=@ContraSucursal,NUMERO_ORIGEN=@ContraNumero,LETRA_ORIGEN=@ContraLetra,
                        IMPORTE=@Importe/4 WHERE ID=@IdAplicacion;
                    """,key,tx);
                var inverso=await ReporteComercialService.LeerComprobantesCuentaCorrienteAsync(cn,f,true,transaction:tx);
                var invertido=Assert.Single(inverso.Items,x=>x.IdComprobante==key.Sucursal+key.Numero+key.Letra);
                Assert.Single(invertido.Aplicaciones);Assert.Equal(0,invertido.DiferenciaAplicaciones);
                Assert.Equal(key.ContraTc,invertido.Aplicaciones[0].Tc);
            }
            finally { await tx.RollbackAsync(); }
        }
        Assert.Equal(antes,await cn.QuerySingleAsync<(int Cantidad,decimal Importe)>("SELECT COUNT(*),SUM(ISNULL(IMPORTE,0)) FROM dbo.MV_APLICACION"));
    }

    private sealed class ReferenciaPrueba
    {
        public string Cuenta { get; set; }=""; public string Tc { get; set; }="";
        public string Sucursal { get; set; }="";public string Numero { get; set; }="";public string Letra { get; set; }="";
        public DateTime Fecha { get; set; } public decimal Importe { get; set; } public int IdAplicacion { get; set; }
        public string ContraTc { get; set; }="";public string ContraSucursal { get; set; }="";
        public string ContraNumero { get; set; }="";public string ContraLetra { get; set; }="";
    }
}

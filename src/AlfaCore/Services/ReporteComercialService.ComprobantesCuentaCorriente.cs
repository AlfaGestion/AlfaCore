using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ReporteComercialService
{
    public Task<ComprobantesCuentaCorrienteResultDto> GetComprobantesCuentaCorrienteAsync(
        FiltrosReporteCompras f,bool analitico,CancellationToken ct=default)
        => EjecutarAsync(true,analitico?"CuentaCorrienteAnalitica":"CuentaCorrienteResumida",
            cn=>LeerComprobantesCuentaCorrienteAsync(cn,f,analitico,ct),ct,f);

    public Task<ComprobantesCuentaCorrienteResultDto> GetComprobantesCuentaCorrienteComprasAsync(
        FiltrosReporteCompras f,bool analitico,CancellationToken ct=default)
        => EjecutarAsync(false,analitico?"CuentaCorrienteAnalitica":"CuentaCorrienteResumida",
            cn=>LeerComprobantesCuentaCorrienteAsync(cn,f,analitico,ct,ventas:false),ct);

    internal static async Task<ComprobantesCuentaCorrienteResultDto> LeerComprobantesCuentaCorrienteAsync(
        SqlConnection cn,FiltrosReporteCompras f,bool analitico,CancellationToken ct=default,SqlTransaction? transaction=null,bool ventas=true)
    {
        var parametros=new DynamicParameters(Parametros(f));parametros.Add("Analitico",analitico);
        // La vista oficial es la misma que alimenta el generador NW del VB6. Sus saldos
        // son actuales; las fechas filtran emisión, no reconstruyen aplicaciones pasadas.
        var sql=$"""
            SELECT LTRIM(RTRIM(v.CUENTA)) Cuenta,
                COALESCE((SELECT MAX(p.RAZON_SOCIAL) FROM {Terceros(ventas)} p
                    WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(v.CUENTA))),v.CUENTA) RazonSocial,
                v.FECHA Fecha,v.VENCIMIENTO Vencimiento,LTRIM(RTRIM(v.TC)) Tc,
                v.SUCURSAL,v.NUMERO,v.LETRA,v.SUCURSAL+v.NUMERO+v.LETRA IdComprobante,
                CONVERT(decimal(19,4),v.IMPORTE) Importe,CONVERT(decimal(19,4),v.PAGO) Aplicado,
                CONVERT(decimal(19,4),v.SALDO) Pendiente
            INTO #DocumentosCC FROM {(ventas ? "dbo.VE_CPTES_SALDOS_UNEG" : "dbo.CO_CPTES_SALDOS_UNEG")} v
            {FiltroCarteraComercial(ventas,"v.CUENTA","v.UNEGOCIO")}
                {FiltroProformas(ventas,"v.TC")}
                AND (@FechaDesde IS NULL OR v.FECHA>=@FechaDesde)
                AND (@FechaHasta IS NULL OR v.FECHA<DATEADD(day,1,@FechaHasta))
                AND (@TipoComprobante IS NULL OR LTRIM(RTRIM(v.TC))=@TipoComprobante)
                AND (@Analitico=1 OR v.SALDO>0);
            DELETE FROM #DocumentosCC WHERE @Proveedor IS NOT NULL
                AND Cuenta NOT LIKE @Busqueda ESCAPE '\' AND RazonSocial NOT LIKE @Busqueda ESCAPE '\';
            IF EXISTS(SELECT 1 FROM #DocumentosCC GROUP BY Cuenta,Tc,SUCURSAL,NUMERO,LETRA HAVING COUNT(*)>1)
            BEGIN
                RAISERROR('Hay comprobantes con referencias contables duplicadas. Revisar antes de analizar sus aplicaciones.',16,1);
                RETURN;
            END;

            SELECT COUNT(*) TotalRegistros,COUNT(DISTINCT Cuenta) CantidadClientes,
                ISNULL(SUM(Importe),0) ImporteTotal,ISNULL(SUM(Aplicado),0) AplicadoTotal,
                ISNULL(SUM(Pendiente),0) PendienteTotal FROM #DocumentosCC;
            WITH Numerados AS (
                SELECT *,ROW_NUMBER() OVER(ORDER BY Cuenta,Fecha,Tc,SUCURSAL,NUMERO,LETRA) Orden,
                    SUM(Pendiente) OVER(PARTITION BY Cuenta) PendienteCliente FROM #DocumentosCC
            )
            SELECT CONVERT(int,Orden) Orden,Cuenta,RazonSocial,Fecha,Vencimiento,Tc,SUCURSAL,NUMERO,LETRA,IdComprobante,
                Importe,Aplicado,Pendiente,PendienteCliente INTO #PaginaCC
            FROM Numerados WHERE Orden>@Skip AND Orden<=CONVERT(bigint,@Skip)+@PageSize;

            -- Las referencias se resuelven una sola vez por clave. Nunca se multiplican
            -- los importes aplicados por contrapartidas o renglones del asiento.
            SELECT LTRIM(RTRIM(a.CUENTA)) Cuenta,LTRIM(RTRIM(a.TC)) Tc,a.SUCURSAL,a.NUMERO,a.LETRA,
                CASE WHEN COUNT(*)=1 THEN MIN(a.FECHA) END Fecha,COUNT(*) Cantidad
            INTO #ReferenciasCC FROM dbo.MV_ASIENTOS a
            WHERE @Analitico=1 AND a.EsResumen=0 AND EXISTS(SELECT 1 FROM #PaginaCC d WHERE d.Cuenta=LTRIM(RTRIM(a.CUENTA)))
            GROUP BY LTRIM(RTRIM(a.CUENTA)),LTRIM(RTRIM(a.TC)),a.SUCURSAL,a.NUMERO,a.LETRA;

            SELECT d.Orden OrdenDocumento,a.ID IdAplicacion,d.Importe,d.Aplicado,d.Cuenta,
                CASE WHEN d.Tc=LTRIM(RTRIM(a.TCO_ORIGEN)) AND d.SUCURSAL=a.SUCURSAL_ORIGEN
                    AND d.NUMERO=a.NUMERO_ORIGEN AND d.LETRA=a.LETRA_ORIGEN THEN 1 ELSE 0 END EsOrigen,
                a.TC,a.SUCURSAL,a.NUMERO,a.LETRA,a.TCO_ORIGEN,a.SUCURSAL_ORIGEN,a.NUMERO_ORIGEN,a.LETRA_ORIGEN,
                CONVERT(decimal(19,4),ISNULL(a.IMPORTE,0)) ImporteAplicado
            INTO #EnlacesCC FROM #PaginaCC d JOIN dbo.MV_APLICACION a ON d.Cuenta=LTRIM(RTRIM(a.CUENTA))
                AND ((d.Tc=LTRIM(RTRIM(a.TCO_ORIGEN)) AND d.SUCURSAL=a.SUCURSAL_ORIGEN AND d.NUMERO=a.NUMERO_ORIGEN AND d.LETRA=a.LETRA_ORIGEN)
                    OR (d.Tc=LTRIM(RTRIM(a.TC)) AND d.SUCURSAL=a.SUCURSAL AND d.NUMERO=a.NUMERO AND d.LETRA=a.LETRA
                        AND LTRIM(RTRIM(a.TCO_ORIGEN)) NOT IN ('OT','PR','RM','RMC','NP','OC')))
            WHERE @Analitico=1 AND LTRIM(RTRIM(a.TC))<>'VJ'
                {FiltroProformas(ventas,"a.TC")}
                {FiltroProformas(ventas,"a.TCO_ORIGEN")};

            SELECT e.OrdenDocumento,e.IdAplicacion,LTRIM(RTRIM(CASE WHEN e.EsOrigen=1 THEN e.TC ELSE e.TCO_ORIGEN END)) Tc,
                CASE WHEN e.EsOrigen=1 THEN e.SUCURSAL+e.NUMERO+e.LETRA
                    ELSE e.SUCURSAL_ORIGEN+e.NUMERO_ORIGEN+e.LETRA_ORIGEN END IdComprobante,
                r.Fecha,CONVERT(bit,CASE WHEN ISNULL(r.Cantidad,0)<>1 THEN 1 ELSE 0 END) ReferenciaAmbigua,
                e.ImporteAplicado Importe,e.Importe ImporteDocumento,
                ROW_NUMBER() OVER(PARTITION BY e.OrdenDocumento ORDER BY
                    CASE WHEN r.Fecha IS NULL THEN 1 ELSE 0 END,r.Fecha,e.IdAplicacion) OrdenAplicacion
            INTO #AplicacionesOrdenadasCC FROM #EnlacesCC e LEFT JOIN #ReferenciasCC r ON r.Cuenta=e.Cuenta
                AND r.Tc=LTRIM(RTRIM(CASE WHEN e.EsOrigen=1 THEN e.TC ELSE e.TCO_ORIGEN END))
                AND r.SUCURSAL=CASE WHEN e.EsOrigen=1 THEN e.SUCURSAL ELSE e.SUCURSAL_ORIGEN END
                AND r.NUMERO=CASE WHEN e.EsOrigen=1 THEN e.NUMERO ELSE e.NUMERO_ORIGEN END
                AND r.LETRA=CASE WHEN e.EsOrigen=1 THEN e.LETRA ELSE e.LETRA_ORIGEN END;
            CREATE UNIQUE CLUSTERED INDEX IX_AplicacionesOrdenadasCC ON #AplicacionesOrdenadasCC(OrdenDocumento,OrdenAplicacion);
            WITH Acumulados AS (
                SELECT OrdenDocumento,OrdenAplicacion,CONVERT(decimal(38,4),Importe) Acumulado
                FROM #AplicacionesOrdenadasCC WHERE OrdenAplicacion=1
                UNION ALL
                SELECT n.OrdenDocumento,n.OrdenAplicacion,CONVERT(decimal(38,4),CONVERT(decimal(37,4),a.Acumulado)+n.Importe)
                FROM Acumulados a JOIN #AplicacionesOrdenadasCC n
                    ON n.OrdenDocumento=a.OrdenDocumento AND n.OrdenAplicacion=a.OrdenAplicacion+1
            )
            SELECT n.OrdenDocumento,n.IdAplicacion,n.Tc,n.IdComprobante,n.Fecha,n.ReferenciaAmbigua,n.Importe,
                n.ImporteDocumento-CONVERT(decimal(37,4),a.Acumulado) SaldoRestante
            INTO #AplicacionesCC FROM #AplicacionesOrdenadasCC n JOIN Acumulados a
                ON a.OrdenDocumento=n.OrdenDocumento AND a.OrdenAplicacion=n.OrdenAplicacion OPTION(MAXRECURSION 0);
            SELECT p.Orden,p.Cuenta,p.RazonSocial,p.Fecha,p.Vencimiento,p.Tc,p.IdComprobante,
                p.Importe,p.Aplicado,p.Pendiente,p.PendienteCliente,
                CASE WHEN @Analitico=1 THEN p.Aplicado-ISNULL((SELECT SUM(a.Importe) FROM #AplicacionesCC a WHERE a.OrdenDocumento=p.Orden),0)
                    ELSE CONVERT(decimal(38,4),0) END DiferenciaAplicaciones
            FROM #PaginaCC p ORDER BY p.Orden;
            SELECT * FROM #AplicacionesCC ORDER BY OrdenDocumento,CASE WHEN Fecha IS NULL THEN 1 ELSE 0 END,Fecha,IdAplicacion;
            DROP TABLE #AplicacionesCC; DROP TABLE #AplicacionesOrdenadasCC; DROP TABLE #EnlacesCC; DROP TABLE #ReferenciasCC; DROP TABLE #PaginaCC; DROP TABLE #DocumentosCC;
            """;
        using var r=await cn.QueryMultipleAsync(new CommandDefinition(sql,parametros,transaction,60,cancellationToken:ct));
        var t=await r.ReadSingleAsync<ComprobantesCuentaCorrienteResultDto>();
        var docs=(await r.ReadAsync<ComprobanteCuentaCorrienteDto>()).ToList();
        var enlaces=(await r.ReadAsync<AplicacionCuentaCorrienteDto>()).ToLookup(x=>x.OrdenDocumento);
        foreach(var doc in docs)doc.Aplicaciones=enlaces[doc.Orden].ToList();
        return new ComprobantesCuentaCorrienteResultDto { Items=docs,TotalRegistros=t.TotalRegistros,
            CantidadClientes=t.CantidadClientes,ImporteTotal=t.ImporteTotal,AplicadoTotal=t.AplicadoTotal,PendienteTotal=t.PendienteTotal,
            PaginaActual=f.Pagina,TamanioPagina=f.TamanioPagina };
    }
}

using AlfaCore.Models;
using Dapper;

namespace AlfaCore.Services;

public sealed partial class ReporteComercialService
{
    // Semijoin: nunca multiplica movimientos por referencias repetidas del maestro.
    private static string FiltroCarteraVentas(string cuenta,string unidad,bool requiereCliente=true,bool filtrarUnidad=true)
        => FiltroCarteraComercial(true,cuenta,unidad,requiereCliente,filtrarUnidad);

    private static string FiltroCarteraComercial(bool ventas,string cuenta,string unidad,bool requiereCliente=true,bool filtrarUnidad=true) => $"""
        WHERE {(filtrarUnidad ? $"(@SinFiltroUnidad=1 OR LTRIM(RTRIM(ISNULL({unidad},''))) IN @Unidades)" : "1=1")}
        AND (@CuentaExacta IS NULL OR LTRIM(RTRIM({cuenta}))=@CuentaExacta)
        AND ({(requiereCliente ? "" : "(@Vendedor IS NULL AND @SinVendedor=0 AND @Zona IS NULL AND @SinZona=0 AND @Provincia IS NULL) OR ")}
            EXISTS (SELECT 1 FROM {Terceros(ventas)} p
            WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM({cuenta}))
            {(ventas ? """
              AND (@Vendedor IS NULL OR LTRIM(RTRIM(p.IdVendedor))=@Vendedor)
              AND (@SinVendedor=0 OR NULLIF(LTRIM(RTRIM(p.IdVendedor)),'') IS NULL)
              AND (@Provincia IS NULL OR LTRIM(RTRIM(p.PROVINCIA))=@Provincia)
              AND (@Zona IS NULL OR LTRIM(RTRIM(p.ZONA))=@Zona)
              AND (@SinZona=0 OR NULLIF(LTRIM(RTRIM(p.ZONA)),'') IS NULL)
              """ : "")}))
        """;

    private Task<CuentaCorrienteResultDto> GetCuentaCorrienteDetalladaAsync(bool ventas,FiltrosReporteCompras f, CancellationToken ct)
        => EjecutarAsync(ventas,"CuentaCorriente",async cn=>
        {
            // Una única materialización de la fuente sirve para apertura, totales y página.
            // EsResumen replica la regla de la vista acumulada legacy. No excluir aperturas
            // y cierres aisladamente: eso alteraría la continuidad entre ejercicios.
            var sql=$"""
                SELECT LTRIM(RTRIM(a.CUENTA)) Cuenta,a.CUENTA CuentaOriginal,a.FECHA,
                    LTRIM(RTRIM(a.TC)) Tc,a.SUCURSAL,a.NUMERO,a.LETRA,a.PERIODO,a.MES_OPERATIVO,
                    a.[NUMERO ASIENTO] Asiento,a.SECUENCIA,a.TIPO_REG,
                    a.[DEBE-HABER] DebeHaber,CONVERT(decimal(19,4),a.IMPORTE) Importe,
                    CONVERT(decimal(19,4),CASE WHEN a.[DEBE-HABER]='{(ventas ? "D" : "H")}' THEN a.IMPORTE ELSE -a.IMPORTE END) Signado
                INTO #MovimientosVentas FROM dbo.MV_ASIENTOS a
                {FiltroCarteraComercial(ventas,"a.CUENTA", "a.UNEGOCIO")}
                {FiltroProformas(ventas,"a.TC")}
                AND a.EsResumen=0 AND a.FECHA IS NOT NULL
                AND (@FechaHasta IS NULL OR a.FECHA<DATEADD(day,1,@FechaHasta));

                SELECT m.Cuenta,COALESCE(MAX(p.RazonSocial),m.Cuenta) RazonSocial,
                    SUM(CASE WHEN @FechaDesde IS NOT NULL AND m.FECHA<@FechaDesde THEN m.Signado ELSE 0 END) SaldoInicial,
                    SUM(CASE WHEN @FechaDesde IS NULL OR m.FECHA>=@FechaDesde THEN m.Signado ELSE 0 END) Movimiento,
                    SUM(m.Signado) SaldoFinal
                INTO #CuentasVentas FROM #MovimientosVentas m
                LEFT JOIN (SELECT LTRIM(RTRIM(CTACODIGO)) Cuenta,MAX(RAZON_SOCIAL) RazonSocial
                    FROM {Terceros(ventas)} GROUP BY LTRIM(RTRIM(CTACODIGO))) p ON p.Cuenta=m.Cuenta
                GROUP BY m.Cuenta
                HAVING SUM(CASE WHEN @FechaDesde IS NULL OR m.FECHA>=@FechaDesde THEN 1 ELSE 0 END)>0
                    OR SUM(CASE WHEN @FechaDesde IS NOT NULL AND m.FECHA<@FechaDesde THEN m.Signado ELSE 0 END)<>0;

                -- Sin SUM() OVER(ORDER BY) ni OFFSET (SQL 2008 / compat 80): el acumulado se calcula solo para la página.
                SELECT m.*,c.RazonSocial,c.SaldoInicial,c.SaldoFinal,
                    ROW_NUMBER() OVER(PARTITION BY m.Cuenta ORDER BY m.FECHA,m.PERIODO,m.MES_OPERATIVO,
                        m.Asiento,m.SECUENCIA,m.Tc,m.SUCURSAL,m.NUMERO,m.LETRA,m.TIPO_REG,m.CuentaOriginal) Orden,
                    COUNT(*) OVER(PARTITION BY m.Cuenta) Cantidad
                INTO #NumeradosVentas FROM #MovimientosVentas m JOIN #CuentasVentas c ON c.Cuenta=m.Cuenta
                WHERE @FechaDesde IS NULL OR m.FECHA>=@FechaDesde;

                SELECT Cuenta,RazonSocial,FECHA Fecha,Tc,SUCURSAL+NUMERO+LETRA IdComprobante,
                    DebeHaber,Importe,Signado ImporteSignado,SaldoInicial,
                    SaldoFinal SaldoFinalCuenta,CONVERT(bit,CASE WHEN Orden=Cantidad THEN 1 ELSE 0 END) UltimaLineaCuenta,
                    CONVERT(bit,0) EsSaldoInicial,Orden
                INTO #LineasVentas FROM #NumeradosVentas
                UNION ALL
                SELECT c.Cuenta,c.RazonSocial,COALESCE(@FechaDesde,@FechaHasta,CONVERT(datetime,'19000101')),
                    '', '', '', 0,0,c.SaldoInicial,c.SaldoFinal,CONVERT(bit,1),CONVERT(bit,1),0
                FROM #CuentasVentas c WHERE NOT EXISTS(SELECT 1 FROM #MovimientosVentas m
                    WHERE m.Cuenta=c.Cuenta AND (@FechaDesde IS NULL OR m.FECHA>=@FechaDesde));

                -- Cuenta/cliente selecciona cuentas completas. TC solo limita las filas visibles:
                -- no se recalcula deuda ficticia suprimiendo pagos de otro tipo.
                DELETE l FROM #LineasVentas l WHERE
                    (@Proveedor IS NOT NULL AND l.Cuenta NOT LIKE @Busqueda ESCAPE '\' AND l.RazonSocial NOT LIKE @Busqueda ESCAPE '\')
                    OR (@TipoComprobante IS NOT NULL AND l.Tc<>@TipoComprobante);
                SELECT COUNT(*) TotalRegistros FROM #LineasVentas;
                SELECT ISNULL(SUM(c.SaldoInicial),0) SaldoInicialTotal,ISNULL(SUM(c.Movimiento),0) SaldoTotal,
                    ISNULL(SUM(c.SaldoFinal),0) SaldoFinalTotal FROM #CuentasVentas c
                    WHERE EXISTS(SELECT 1 FROM #LineasVentas l WHERE l.Cuenta=c.Cuenta);
                SELECT * INTO #PaginaVentas FROM (
                    SELECT l.*,ROW_NUMBER() OVER(ORDER BY l.Cuenta,l.Orden) FilaPagina FROM #LineasVentas l) p
                WHERE FilaPagina>@Skip AND FilaPagina<=@Skip+@PageSize;
                SELECT p.Cuenta,p.RazonSocial,p.Fecha,p.Tc,p.IdComprobante,p.DebeHaber,p.Importe,p.ImporteSignado,p.SaldoInicial,
                    p.SaldoInicial+ISNULL(a.Acumulado,0)-p.ImporteSignado SaldoAnteriorLinea,p.SaldoInicial+ISNULL(a.Acumulado,0) SaldoParcial,
                    p.SaldoFinalCuenta,p.UltimaLineaCuenta,p.EsSaldoInicial,p.Orden
                FROM #PaginaVentas p
                LEFT JOIN (SELECT x.FilaPagina,SUM(n.Signado) Acumulado FROM #PaginaVentas x
                    JOIN #NumeradosVentas n ON n.Cuenta=x.Cuenta AND n.Orden<=x.Orden GROUP BY x.FilaPagina) a ON a.FilaPagina=p.FilaPagina
                ORDER BY p.FilaPagina;
                DROP TABLE #PaginaVentas; DROP TABLE #LineasVentas; DROP TABLE #NumeradosVentas; DROP TABLE #CuentasVentas; DROP TABLE #MovimientosVentas;
                """;
            using var r=await cn.QueryMultipleAsync(new CommandDefinition(sql,Parametros(f),commandTimeout:60,cancellationToken:ct));
            var count=await r.ReadSingleAsync<int>();
            var totals=await r.ReadSingleAsync<CuentaCorrienteResultDto>();
            return new CuentaCorrienteResultDto { TotalRegistros=count, SaldoTotal=totals.SaldoTotal,
                SaldoInicialTotal=totals.SaldoInicialTotal,SaldoFinalTotal=totals.SaldoFinalTotal,
                Items=(await r.ReadAsync<CuentaCorrienteFilaDto>()).ToList(),PaginaActual=f.Pagina,TamanioPagina=f.TamanioPagina };
        },ct,f);
}

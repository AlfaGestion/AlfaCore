using AlfaCore.Models;
using Dapper;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService
{
    // Misma fuente y exclusión EsResumen que la cuenta corriente comercial. Los saldos
    // incluyen toda la historia anterior al corte y nunca suman monedas distintas.
    private const string CuentaCorrienteBaseSql = """
        DECLARE @cliente nvarchar(30),@fecha date,@importe money;
        SELECT @fecha=m.FechaMovimiento,@importe=m.Importe FROM dbo.BK_ExtractoMovimiento m WHERE m.MovimientoId=@movimientoId;
        SELECT TOP(1) @cliente=p.ClienteCodigo FROM dbo.BK_MovimientoMatch p
        WHERE p.MovimientoId=@movimientoId AND p.EstadoPropuesta<>'RECLASIFICADO' ORDER BY p.MatchId DESC;
        SELECT @importe ImporteBanco,
            CONVERT(bit,CASE WHEN LTRIM(RTRIM(a.TC)) IN ('CB','CBFP','CBCT') AND a.SECUENCIA=1 AND a.[DEBE-HABER]='H'
                AND a.FECHA>=DATEADD(day,-15,@fecha) AND a.FECHA<DATEADD(day,16,@fecha) THEN 1 ELSE 0 END) ReciboCercano,
            a.PERIODO Periodo,a.MES_OPERATIVO Mes,a.[NUMERO ASIENTO] Asiento,a.TIPO_REG TipoRegistro,a.SECUENCIA Secuencia,
            a.FECHA Fecha,LTRIM(RTRIM(a.TC)) Tc,
            ISNULL(a.SUCURSAL,'')+ISNULL(a.NUMERO,'')+ISNULL(a.LETRA,'') Comprobante,
            ISNULL(a.DETALLE,'') Detalle,ISNULL(LTRIM(RTRIM(a.MONEDA)),'') CodigoMoneda,
            COALESCE(NULLIF(LTRIM(RTRIM(mon.DESCRIPCION)),''),NULLIF(LTRIM(RTRIM(a.MONEDA)),''),'Sin informar') Moneda,
            CONVERT(decimal(19,4),CASE WHEN a.[DEBE-HABER]='D' THEN a.IMPORTE ELSE 0 END) Debe,
            CONVERT(decimal(19,4),CASE WHEN a.[DEBE-HABER]='H' THEN a.IMPORTE ELSE 0 END) Haber,
            ISNULL(a.USUARIO_LOGEADO,'') Usuario,ISNULL(LTRIM(RTRIM(a.IDCAJAS)),'') Caja,
            ISNULL(LTRIM(RTRIM(a.UNEGOCIO)),'') Unidad,
            CONVERT(bit,CASE WHEN LTRIM(RTRIM(a.TC)) IN ('CB','CBFP','CBCT') AND a.SECUENCIA=1
                AND ABS(ABS(a.IMPORTE)-@importe)<=0.01 AND a.FECHA>=DATEADD(day,-15,@fecha)
                AND a.FECHA<DATEADD(day,16,@fecha) THEN 1 ELSE 0 END) PosibleCoincidencia
        INTO #CtaBase FROM dbo.MV_ASIENTOS a
        LEFT JOIN dbo.TA_MONEDAS mon ON LTRIM(RTRIM(mon.CODIGO))=LTRIM(RTRIM(a.MONEDA))
        WHERE LTRIM(RTRIM(a.CUENTA))=LTRIM(RTRIM(@cliente)) AND a.EsResumen=0 AND a.FECHA IS NOT NULL
            AND EXISTS(SELECT 1 FROM dbo.TA_USUARIOS u WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@usuario)
                AND (ISNULL(u.AdminCentral,0)=1 OR LTRIM(RTRIM(u.UNEGOCIO))=LTRIM(RTRIM(a.UNEGOCIO))));
        """;

    public Task<ConciliacionCuentaCorriente> CuentaCorrienteAsync(long movimientoId, int cantidad = 30,
        DateTime? hasta = null, CancellationToken ct = default)
        => Ejecutar("CuentaCorriente", async (cn, token) =>
        {
            if (cantidad is not (30 or 50 or 100)) throw new InvalidOperationException("Elegí 30, 50 o 100 movimientos.");
            using var r = await cn.QueryMultipleAsync(new CommandDefinition(CuentaCorrienteBaseSql + """
                DECLARE @corte date=COALESCE(@hasta,DATEADD(day,15,@fecha));
                SELECT *,SUM(Debe-Haber) OVER(PARTITION BY CodigoMoneda ORDER BY Fecha,Periodo,Mes,Asiento,
                    Secuencia,Tc,Comprobante,TipoRegistro ROWS UNBOUNDED PRECEDING) Saldo
                INTO #Cta FROM #CtaBase WHERE Fecha<DATEADD(day,1,@corte);
                SELECT TOP(@cantidad) * FROM #Cta ORDER BY Fecha DESC,Periodo DESC,Mes DESC,Asiento DESC,
                    Secuencia DESC,Tc DESC,Comprobante DESC,TipoRegistro DESC;
                -- Independiente del límite/corte de la grilla: no esconder el recibo que motivó el aviso.
                SELECT *,CONVERT(decimal(19,4),0) Saldo FROM #CtaBase WHERE ReciboCercano=1 AND Haber>0 AND CodigoMoneda='1'
                    ORDER BY PosibleCoincidencia DESC,ABS(Haber-ImporteBanco),ABS(DATEDIFF(day,Fecha,@fecha)),Fecha DESC,Periodo,Mes,Asiento,Secuencia;
                SELECT MAX(Moneda) Moneda,SUM(Debe-Haber) Saldo FROM #Cta GROUP BY CodigoMoneda ORDER BY CodigoMoneda;
                """, new { movimientoId, cantidad, hasta = hasta?.Date, usuario = users.GetCurrentUserName() },
                commandTimeout: 120, cancellationToken: token));
            return new ConciliacionCuentaCorriente((await r.ReadAsync<ConciliacionCtaMovimiento>()).AsList(),
                (await r.ReadAsync<ConciliacionCtaMovimiento>()).AsList(), (await r.ReadAsync<ConciliacionCtaSaldo>()).AsList());
        }, ct);

    public Task<ConciliacionAsientoDetalle> DetalleAsientoAsync(long movimientoId, ConciliacionAsientoClave clave,
        CancellationToken ct = default)
        => Ejecutar("DetalleAsiento", async (cn, token) =>
        {
            using var r = await cn.QueryMultipleAsync(new CommandDefinition(CuentaCorrienteBaseSql + """
                SELECT * INTO #Elegido FROM #CtaBase WHERE Periodo=@Periodo AND Mes=@Mes AND Asiento=@Asiento
                    AND TipoRegistro=@TipoRegistro AND Secuencia=@Secuencia;
                SELECT *,CONVERT(decimal(19,4),0) Saldo FROM #Elegido;
                SELECT ISNULL((SELECT TOP(1) f.COMENTARIOS FROM dbo.V_MV_CPTE f JOIN #Elegido e
                    ON LTRIM(RTRIM(f.TC))=e.Tc AND f.IDCOMPROBANTE=e.Comprobante
                    AND LTRIM(RTRIM(f.CUENTA))=LTRIM(RTRIM(@cliente)) AND ISNULL(LTRIM(RTRIM(f.UNEGOCIO)),'')=e.Unidad
                    WHERE f.IDCOMPLEMENTO=0),'');
                SELECT LTRIM(RTRIM(a.CUENTA)) Cuenta,ISNULL(c.DESCRIPCION,'') Nombre,ISNULL(c.MEDIODEPAGO,'') Medio,
                    a.[DEBE-HABER] Signo,a.IMPORTE Importe,
                    COALESCE(NULLIF(LTRIM(RTRIM(mon.DESCRIPCION)),''),NULLIF(LTRIM(RTRIM(a.MONEDA)),''),'Sin informar') Moneda,
                    ISNULL(a.NroComprobanteBancario,'') Referencia
                FROM dbo.MV_ASIENTOS a JOIN #Elegido e ON a.PERIODO=e.Periodo AND a.MES_OPERATIVO=e.Mes
                    AND a.[NUMERO ASIENTO]=e.Asiento AND a.TIPO_REG=e.TipoRegistro
                    AND LTRIM(RTRIM(a.TC))=e.Tc AND ISNULL(a.SUCURSAL,'')+ISNULL(a.NUMERO,'')+ISNULL(a.LETRA,'')=e.Comprobante
                LEFT JOIN dbo.MA_CUENTAS c ON LTRIM(RTRIM(c.CODIGO))=LTRIM(RTRIM(a.CUENTA))
                LEFT JOIN dbo.TA_MONEDAS mon ON LTRIM(RTRIM(mon.CODIGO))=LTRIM(RTRIM(a.MONEDA))
                WHERE LTRIM(RTRIM(a.CUENTA))<>LTRIM(RTRIM(@cliente)) AND ISNULL(LTRIM(RTRIM(a.UNEGOCIO)),'')=e.Unidad
                ORDER BY a.SECUENCIA,a.CUENTA;
                SELECT LTRIM(RTRIM(a.TCO_ORIGEN)) Tc,a.IDCOMPROBANTE_ORIGEN Comprobante,a.IMPORTE Importe,
                    f.FECHA Fecha,ISNULL(f.COMENTARIOS,'') Comentarios
                FROM dbo.MV_APLICACION a JOIN #Elegido e ON LTRIM(RTRIM(a.TC))=e.Tc AND a.IDCOMPROBANTE=e.Comprobante
                    AND LTRIM(RTRIM(a.CUENTA))=LTRIM(RTRIM(@cliente))
                    AND (LTRIM(RTRIM(a.UNEGOCIO))=e.Unidad OR (NULLIF(LTRIM(RTRIM(a.UNEGOCIO)),'') IS NULL
                        AND NOT EXISTS(SELECT 1 FROM dbo.MV_ASIENTOS otro
                            WHERE LTRIM(RTRIM(otro.CUENTA))=LTRIM(RTRIM(@cliente)) AND LTRIM(RTRIM(otro.TC))=e.Tc
                                AND ISNULL(otro.SUCURSAL,'')+ISNULL(otro.NUMERO,'')+ISNULL(otro.LETRA,'')=e.Comprobante
                                AND ISNULL(LTRIM(RTRIM(otro.UNEGOCIO)),'')<>e.Unidad)))
                LEFT JOIN dbo.V_MV_CPTE f ON f.TC=a.TCO_ORIGEN AND f.IDCOMPROBANTE=a.IDCOMPROBANTE_ORIGEN
                    AND f.IDCOMPLEMENTO=a.IDCOMPLEMENTO_ORIGEN AND LTRIM(RTRIM(f.CUENTA))=LTRIM(RTRIM(@cliente))
                    AND EXISTS(SELECT 1 FROM dbo.TA_USUARIOS u WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@usuario)
                        AND (ISNULL(u.AdminCentral,0)=1 OR LTRIM(RTRIM(u.UNEGOCIO))=LTRIM(RTRIM(f.UNEGOCIO))))
                ORDER BY a.TCO_ORIGEN,a.IDCOMPROBANTE_ORIGEN;
                """, new { movimientoId, clave.Periodo, clave.Mes, clave.Asiento, clave.TipoRegistro, clave.Secuencia,
                usuario = users.GetCurrentUserName() }, commandTimeout: 120, cancellationToken: token));
            var movimiento = await r.ReadSingleOrDefaultAsync<ConciliacionCtaMovimiento>()
                ?? throw new InvalidOperationException("El asiento no está disponible para este cliente y usuario.");
            var comentarios = await r.ReadSingleAsync<string>();
            return new ConciliacionAsientoDetalle(movimiento, comentarios,
                (await r.ReadAsync<ConciliacionContrapartida>()).AsList(), (await r.ReadAsync<ConciliacionCtaAplicacion>()).AsList());
        }, ct);
}

-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
SELECT CONVERT(NVARCHAR(10),FECHA,103) as fecha,tc,idcomprobante,cuenta,isnull(nombre,'') as nombre,
    convert(varchar,convert(decimal(15,2),case when DH='H' then IMPORTE * -1 else IMPORTE end)) as importe,
    isnull(ltrim(idvendedor) + ' - ' + nvendedor,'') as vendedor,ltrim(unegocio) + ' - ' + nunegocio as unegocio,usuario,
    isnull(convert(NVARCHAR(18),FechaHora_grabacion,103) + ' ' + convert(NVARCHAR(18),FechaHora_grabacion,108),'') as fechahora  INTO #Reporte FROM(
    SELECT dbo.MV_ASIENTOS.[DEBE-HABER] AS DH, dbo.MV_ASIENTOS.FECHA,V_TA_VENDEDORES.Nombre as nvendedor,V_TA_UnidadNegocio.Descripcion as nunegocio, dbo.MV_ASIENTOS.TC, dbo.MV_ASIENTOS.SUCURSAL + dbo.MV_ASIENTOS.NUMERO + dbo.MV_ASIENTOS.LETRA AS IDCOMPROBANTE,
    dbo.MV_ASIENTOS.UNEGOCIO, dbo.MV_ASIENTOS.IdVendedor, dbo.MV_ASIENTOS.USUARIO_LOGEADO AS Usuario, dbo.MV_ASIENTOS.CUENTA,
    dbo.MA_CUENTAS.DESCRIPCION AS NOMBRE, dbo.MV_ASIENTOS.IMPORTE, dbo.V_MV_Cpte.ANULADA, v_mv_cpte.FechaHora_grabacion
    FROM dbo.MV_ASIENTOS INNER JOIN  dbo.MA_CUENTAS ON dbo.MV_ASIENTOS.CUENTA = dbo.MA_CUENTAS.CODIGO LEFT OUTER JOIN  dbo.V_MV_Cpte
    ON dbo.MV_ASIENTOS.TC = dbo.V_MV_Cpte.TC AND dbo.MV_ASIENTOS.SUCURSAL = dbo.V_MV_Cpte.SUCURSAL AND  dbo.MV_ASIENTOS.NUMERO = dbo.V_MV_Cpte.NUMERO
    AND dbo.MV_ASIENTOS.LETRA = dbo.V_MV_Cpte.LETRA AND  dbo.MV_ASIENTOS.Cuenta = dbo.V_MV_Cpte.Cuenta
    LEFT JOIN V_TA_VENDEDORES on V_TA_VENDEDORES.IdVendedor = MV_ASIENTOS.IdVendedor
    LEFT JOIN V_TA_UnidadNegocio on V_TA_UnidadNegocio.Codigo = MV_ASIENTOS.UNEGOCIO
    WHERE (dbo.MV_ASIENTOS.SECUENCIA = 1)  AND (@unidad='' OR LTRIM(RTRIM(dbo.MV_ASIENTOS.UNEGOCIO))=@unidad) AND dbo.MV_ASIENTOS.FECHA=@fecha AND (@idcaja='' OR LTRIM(RTRIM(idcajas))=@idcaja) AND ((@cobranzas=1 AND dbo.MV_ASIENTOS.TC IN ('CBCT','CB','CBFP')) OR (@cobranzas=0 AND dbo.MV_ASIENTOS.TC IN ('TK','TKFC','FP','FC','CBCT','CB','CBFP','NC','NCFP')))
    ) AS A;

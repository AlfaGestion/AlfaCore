-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
SELECT a.fecha, a.tc, a.SUCURSAL+a.NUMERO+a.LETRA AS idcomprobante, a.cuenta, b.DESCRIPCION AS nombre,
CAST(a.importe AS decimal(19,2)) AS importe INTO #Reporte
FROM dbo.MV_ASIENTOS a INNER JOIN dbo.MA_CUENTAS b ON a.CUENTA=b.CODIGO
WHERE a.SECUENCIA=1 AND a.FECHA=@fecha AND (@unidad='' OR LTRIM(RTRIM(a.UNEGOCIO))=@unidad) AND (@idcaja='' OR LTRIM(RTRIM(a.IdCajas))=@idcaja)
AND ((@cobranzas=1 AND a.TC IN ('CB','CBFP') AND NOT EXISTS
(SELECT 1 FROM dbo.MV_APLICACION c WHERE c.TC=a.TC AND c.SUCURSAL=a.SUCURSAL AND c.NUMERO=a.NUMERO AND c.LETRA=a.LETRA AND ISNULL(c.TCO_ORIGEN,'')<>''))
OR (@cobranzas=0 AND a.TC IN ('TK','TKFC','FP','FC') AND NOT EXISTS
(SELECT 1 FROM dbo.MV_APLICACION c WHERE c.TCO_ORIGEN=a.TC AND c.SUCURSAL_ORIGEN=a.SUCURSAL AND c.NUMERO_ORIGEN=a.NUMERO AND c.LETRA_ORIGEN=a.LETRA AND ISNULL(c.TC,'')<>'')));

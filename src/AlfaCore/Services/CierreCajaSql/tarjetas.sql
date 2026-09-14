-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
SELECT  a.CUENTA + ' ' + b.DESCRIPCION AS tarjeta, convert(varchar,convert(decimal(15,2),sum(a.IMPORTE))) as importe, a.idcajas
     INTO #Reporte FROM dbo.MV_ASIENTOS a INNER JOIN dbo.MA_CUENTAS b ON a.CUENTA = b.CODIGO
    WHERE (b.MedioDePago = N'TJ')  AND (a.TC = 'CB' OR a.TC = 'CBCT' OR a.TC = 'CBFP')
     AND a.fecha=@fecha AND (@unidad='' OR LTRIM(RTRIM(a.UNEGOCIO))=@unidad) AND (@idcaja='' OR LTRIM(RTRIM(a.idcajas))=@idcaja)
    GROUP BY A.CUENTA, B.DESCRIPCION, a.idcajas;

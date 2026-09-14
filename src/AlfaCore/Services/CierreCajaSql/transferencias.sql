-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
SELECT CONVERT(NVARCHAR(10),A.fecha,103) as fecha, A.cuenta, dbo.MA_CUENTAS.descripcion,convert(varchar,convert(decimal(15,2),A.importe)) AS egreso, convert(varchar,convert(decimal(15,2),B.importe)) AS ingreso,
    A.IdCajas AS origen, B.IdCajas AS destino, A.moneda ,convert(varchar,convert(decimal(15,2),A.cotizacion)) as cotizacion , a.tc, a.sucursal, a.numero, a.letra
     INTO #Reporte FROM (SELECT TC, SUCURSAL, NUMERO, LETRA, IdCajas, CUENTA, FECHA, [DEBE-HABER], SUM(IMPORTE) AS Importe, MONEDA, COTIZACION
    From dbo.MV_ASIENTOS WHERE ([DEBE-HABER] = 'H') AND (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) GROUP BY TC, SUCURSAL, NUMERO, LETRA, IdCajas, CUENTA, FECHA, [DEBE-HABER], MONEDA, COTIZACION) AS A
    FULL OUTER JOIN
    (SELECT TC, SUCURSAL, NUMERO, LETRA, IdCajas, CUENTA, FECHA, [DEBE-HABER], SUM(IMPORTE) AS Importe, MONEDA, COTIZACION
    FROM dbo.MV_ASIENTOS AS MV_ASIENTOS_1 WHERE ([DEBE-HABER] = 'D') GROUP BY TC, SUCURSAL, NUMERO, LETRA, IdCajas, CUENTA, FECHA, [DEBE-HABER], MONEDA, COTIZACION) AS B
    ON A.IdCajas <> B.IdCajas AND A.CUENTA = B.CUENTA AND A.TC = B.TC AND A.SUCURSAL = B.SUCURSAL AND A.NUMERO = B.NUMERO AND A.LETRA = B.LETRA AND A.MONEDA = B.MONEDA AND A.COTIZACION = B.COTIZACION
    INNER JOIN dbo.MA_CUENTAS ON dbo.MA_CUENTAS.CODIGO = A.CUENTA
    WHERE (NOT (B.IdCajas IS NULL)) AND (NOT (dbo.MA_CUENTAS.MedioDePago IS NULL)) AND (dbo.MA_CUENTAS.MedioDePago <> '')
     AND a.fecha=@fecha AND (@idcaja='' OR LTRIM(RTRIM(a.idcajas))=@idcaja);

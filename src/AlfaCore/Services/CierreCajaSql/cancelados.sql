-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
SELECT dbo.V_MV_CpteAcciones.tc, dbo.V_MV_CpteAcciones.idcomprobante,
	convert(NVARCHAR(18),dbo.V_MV_CpteAcciones.FECHAHORA,103) + ' ' + convert(NVARCHAR(18),dbo.V_MV_CpteAcciones.FECHAHORA,108) as fechahora,
	dbo.V_MV_CpteAcciones.usuario, dbo.V_MV_CpteAcciones.pc ,dbo.V_MV_CpteAcciones.SYSTEMUSER AS detalle
     INTO #Reporte FROM dbo.V_MV_CpteAcciones LEFT OUTER JOIN dbo.MV_ASIENTOS ON dbo.V_MV_CpteAcciones.TC = dbo.MV_ASIENTOS.TC
    WHERE (@unidad='' OR LTRIM(RTRIM(dbo.MV_ASIENTOS.UNEGOCIO))=@unidad) AND (dbo.V_MV_CpteAcciones.TIPO_ACCION = N'IN') AND (dbo.MV_ASIENTOS.SUCURSAL + dbo.MV_ASIENTOS.NUMERO + dbo.MV_ASIENTOS.LETRA = dbo.V_MV_CpteAcciones.IDCOMPROBANTE)
    GROUP BY dbo.V_MV_CpteAcciones.TC, dbo.V_MV_CpteAcciones.IDCOMPROBANTE, dbo.V_MV_CpteAcciones.FECHAHORA, dbo.V_MV_CpteAcciones.USUARIO, dbo.V_MV_CpteAcciones.Pc ,
    dbo.MV_ASIENTOS.IdCajas, dbo.V_MV_CpteAcciones.SYSTEMUSER
    HAVING  dbo.V_MV_CpteAcciones.FECHAHORA >= @fecha AND dbo.V_MV_CpteAcciones.FECHAHORA < DATEADD(day,1,@fecha) AND (@idcaja='' OR LTRIM(RTRIM(dbo.MV_ASIENTOS.IdCajas))=@idcaja);

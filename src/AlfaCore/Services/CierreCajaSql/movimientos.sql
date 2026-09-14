-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
CREATE TABLE #temp
	(
		[cuenta] [nvarchar] (15) COLLATE  Latin1_General_CI_AI,
		[descripcion] [nvarchar] (250) COLLATE  Latin1_General_CI_AI ,
		[detalle] [nvarchar] (150) COLLATE  Latin1_General_CI_AI ,
		[fecha] [nvarchar] (250) COLLATE  Latin1_General_CI_AI ,
		[tc] [nvarchar] (4) COLLATE  Latin1_General_CI_AI ,
		[idcomprobante] [nvarchar] (13) COLLATE  Latin1_General_CI_AI ,
		[importe] [money],
		[usuario] [nvarchar] (250) COLLATE  Latin1_General_CI_AI
	)
	DECLARE @nombre NVARCHAR(50)
	DECLARE @cuentaEfectivo NVARCHAR(15)
	DECLARE @efectivoCobrado MONEY
	DECLARE @ingresos MONEY
	DECLARE @egresos MONEY
	DECLARE @transferencias MONEY
	DECLARE @fondoFijo MONEY
	DECLARE @billetesRendidos MONEY
	DECLARE @medioDePagoContado NVARCHAR(15)
	SET @medioDePagoContado = (SELECT VALOR
	FROM TA_CONFIGURACION
	WHERE CLAVE='MedioDePagoContado')
	SET @cuentaEfectivo = (SELECT CODIGO
	FROM MA_CUENTAS
	WHERE CodigoOpcional=@medioDePagoContado)
	IF @tipo='E'
		BEGIN
		IF @idcaja = ''
			INSERT INTO #temp
		SELECT CUENTA, MA_CUENTAS.DESCRIPCION, DETALLE, convert(NVARCHAR(18),MV_ASIENTOS.FechaHora_Grabacion ,103) + ' ' + convert(NVARCHAR(18),MV_ASIENTOS.FechaHora_Grabacion ,108) as FECHA, TC, SUCURSAL+NUMERO+LETRA AS IDCOMPROBANTE, IMPORTE, USUARIO_LOGEADO
		FROM MV_ASIENTOS INNER JOIN MA_CUENTAS ON MV_ASIENTOS.CUENTA = MA_CUENTAS.CODIGO
		where (@unidad='' OR LTRIM(RTRIM(MV_ASIENTOS.UNEGOCIO))=@unidad) AND CUENTA = @cuentaEfectivo AND [DEBE-HABER] = 'H' and (fecha = @fecha) and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC','FP') AND ISNULL(Cpte_Origen,'')<>'TRANSF:'
		ELSE
			INSERT INTO #temp
		SELECT CUENTA, MA_CUENTAS.DESCRIPCION, DETALLE, convert(NVARCHAR(18),MV_ASIENTOS.FechaHora_Grabacion ,103) + ' ' + convert(NVARCHAR(18),MV_ASIENTOS.FechaHora_Grabacion ,108) as FECHA, TC, SUCURSAL+NUMERO+LETRA AS IDCOMPROBANTE, IMPORTE, USUARIO_LOGEADO
		FROM MV_ASIENTOS INNER JOIN MA_CUENTAS ON MV_ASIENTOS.CUENTA = MA_CUENTAS.CODIGO
		where (@unidad='' OR LTRIM(RTRIM(MV_ASIENTOS.UNEGOCIO))=@unidad) AND CUENTA = @cuentaEfectivo AND [DEBE-HABER] = 'H' and (fecha = @fecha) and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC','FP') AND ISNULL(Cpte_Origen,'')<>'TRANSF:' AND LTRIM(RTRIM(IDCAJAS)) = @idcaja
	END
	ELSE
		BEGIN
		IF @idcaja = ''
			INSERT INTO #temp
		SELECT CUENTA, MA_CUENTAS.DESCRIPCION, DETALLE, convert(NVARCHAR(18),MV_ASIENTOS.FechaHora_Grabacion ,103) + ' ' + convert(NVARCHAR(18),MV_ASIENTOS.FechaHora_Grabacion ,108) as FECHA, TC, SUCURSAL+NUMERO+LETRA AS IDCOMPROBANTE, IMPORTE, USUARIO_LOGEADO
		FROM MV_ASIENTOS INNER JOIN MA_CUENTAS ON MV_ASIENTOS.CUENTA = MA_CUENTAS.CODIGO
		where (@unidad='' OR LTRIM(RTRIM(MV_ASIENTOS.UNEGOCIO))=@unidad) AND CUENTA = @cuentaEfectivo AND [DEBE-HABER] = 'D' and (fecha = @fecha) and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC','FP')
		ELSE
			INSERT INTO #temp
		SELECT CUENTA, MA_CUENTAS.DESCRIPCION, DETALLE, convert(NVARCHAR(18),MV_ASIENTOS.FechaHora_Grabacion ,103) + ' ' + convert(NVARCHAR(18),MV_ASIENTOS.FechaHora_Grabacion ,108) as FECHA, TC, SUCURSAL+NUMERO+LETRA AS IDCOMPROBANTE, IMPORTE, USUARIO_LOGEADO
		FROM MV_ASIENTOS INNER JOIN MA_CUENTAS ON MV_ASIENTOS.CUENTA = MA_CUENTAS.CODIGO
		where (@unidad='' OR LTRIM(RTRIM(MV_ASIENTOS.UNEGOCIO))=@unidad) AND CUENTA = @cuentaEfectivo AND [DEBE-HABER] = 'D' and (fecha = @fecha) and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC','FP') AND LTRIM(RTRIM(IDCAJAS)) = @idcaja
	END
SELECT IDENTITY(int,1,1) AS _orden, * INTO #Reporte FROM #temp;

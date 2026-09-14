-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
CREATE TABLE #temp
	(
		[nombre] [nvarchar] (250) COLLATE  Latin1_General_CI_AI NOT NULL,
		[importe] [money]
	)
	DECLARE @nombre NVARCHAR(50)
	DECLARE @cuentaEfectivo NVARCHAR(15)
	DECLARE @efectivoCobrado MONEY
	DECLARE @ingresos MONEY
	DECLARE @egresos MONEY
	DECLARE @transferencias MONEY
	DECLARE @fondoFijo MONEY
	DECLARE @inicial MONEY = 0
	DECLARE @billetesRendidos MONEY
	DECLARE @medioDePagoContado NVARCHAR(15)
	DECLARE @resultado MONEY
	SET @medioDePagoContado = (SELECT VALOR
	FROM TA_CONFIGURACION
	WHERE CLAVE='MedioDePagoContado')
	SET @cuentaEfectivo = (SELECT CODIGO
	FROM MA_CUENTAS
	WHERE CodigoOpcional=@medioDePagoContado)
	IF @idcaja = ''
		SET @fondoFijo = isnull((Select SUM(cambioInicial)
	from V_Mv_CierreCaja
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND FechaOperativa = @fecha),0)
	ELSE
		SET @fondoFijo = isnull((Select SUM(cambioInicial)
	from V_Mv_CierreCaja
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND FechaOperativa = @fecha and LTRIM(RTRIM(idCaja)) = @idcaja),0)
	IF @initial = 1
		BEGIN
		IF @idcaja = ''
				SET @inicial = ISNULL((select sum(saldo) as importe
		from #ConsolidadoUnidad
		where cuenta = @cuentaEfectivo
			and fecha < @fecha),0)
			ELSE
				SET @inicial = ISNULL((select sum(saldo) as importe
		from #ConsolidadoUnidad
		where cuenta = @cuentaEfectivo
			and fecha < @fecha and LTRIM(RTRIM(idcajas)) = @idcaja),0)
	END
	ELSE
		SET @fondoFijo = 0
	INSERT INTO #temp
		(nombre,importe)
	VALUES
		('Fondo Fijo Inicial', isnull(@inicial + @fondoFijo,0))
	IF @idcaja = ''
		SET @efectivoCobrado = isnull((SELECT sum(Importe * case when [debe-haber] = 'D' then 1 else -1 end) as importe
	from mv_asientos
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND cuenta = @cuentaEfectivo and tc in ('CB','CBCT','CBFP') and fecha = @fecha and cotizacion = 1 and moneda = '   1'),0)
	ELSE
		SET @efectivoCobrado = isnull((SELECT sum(Importe * case when [debe-haber] = 'D' then 1 else -1 end) as importe
	from mv_asientos
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND cuenta = @cuentaEfectivo and tc in ('CB','CBCT','CBFP') and fecha = @fecha and LTRIM(RTRIM(idcajas)) = @idcaja and cotizacion = 1 and moneda = '   1'),0)
	INSERT INTO #temp
		(nombre,importe)
	VALUES
		('Total Efectivo Cobrado', @efectivoCobrado)
	IF @idcaja = ''
		SET @ingresos = ISNULL((SELECT sum(importe) as importe
	FROM MV_ASIENTOS
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND CUENTA = @cuentaEfectivo AND [DEBE-HABER] = 'D' and fecha = @fecha and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC')
		and cotizacion = 1 and moneda = '   1'
	group by Cuenta),0)
	ELSE
		SET @ingresos = ISNULL((SELECT sum(importe) as importe
	FROM MV_ASIENTOS
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND CUENTA = @cuentaEfectivo AND [DEBE-HABER] = 'D' and fecha = @fecha and LTRIM(RTRIM(idcajas)) = @idcaja and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC')
		and cotizacion = 1 and moneda = '   1'
	group by Cuenta),0)
	INSERT INTO #temp
		(nombre,importe)
	VALUES
		('Total Ingresos', @ingresos)
	IF @idcaja = ''
		SET @transferencias = ISNULL((Select sum(egreso) as importe
	from #TransferenciasUnidad
	where cuenta = @cuentaEfectivo
		and fecha = @fecha and cotizacion = 1 and moneda = '   1'),0)
	ELSE
		SET @transferencias = ISNULL((Select sum(egreso) as importe
	from #TransferenciasUnidad
	where cuenta = @cuentaEfectivo
		and fecha = @fecha and LTRIM(RTRIM(origen)) = @idcaja and cotizacion = 1 and moneda = '   1'),0)
	IF @idcaja = ''
		SET @egresos = ISNULL((SELECT sum(importe) as importe
	FROM MV_ASIENTOS
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND CUENTA = @cuentaEfectivo AND [DEBE-HABER] = 'H'
		and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC') and fecha = @fecha and cotizacion = 1 and moneda = '   1'
	group by Cuenta),0)
	ELSE
		SET @egresos = ISNULL((SELECT sum(importe) as importe
	FROM MV_ASIENTOS
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND CUENTA = @cuentaEfectivo AND [DEBE-HABER] = 'H'
		and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC') and fecha = @fecha and LTRIM(RTRIM(idcajas)) = @idcaja and cotizacion = 1 and moneda = '   1'
	group by Cuenta),0)
	INSERT INTO #temp
		(nombre,importe)
	VALUES
		('Total Egresos', @egresos - @transferencias)
	INSERT INTO #temp
		(nombre,importe)
	VALUES
		('Total Transferencias', @transferencias)
	IF @idcaja = ''
		SET @billetesRendidos = ISNULL((Select SUM(TotalEfectivoInformado) as importe
	from V_Mv_CierreCaja
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND FechaOperativa = @fecha),0)
	ELSE
		SET @billetesRendidos = ISNULL((Select SUM(TotalEfectivoInformado) as importe
	from V_Mv_CierreCaja
	where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND FechaOperativa = @fecha and LTRIM(RTRIM(IdCaja))=@idcaja),0)
	INSERT INTO #temp
		(nombre,importe)
	VALUES
		('Total Billetes Rendidos', @billetesRendidos)
	SET @resultado = isnull(@billetesRendidos + @egresos,0)
	SET @resultado = isnull(@resultado - @ingresos - @efectivoCobrado - @inicial - @fondoFijo,0)
	if @resultado < 0
		INSERT INTO #temp
		(nombre,importe)
	VALUES
		('Diferencia (FALTANTE DE CAJA)', @resultado *-1)
	ELSE
		INSERT INTO #temp
		(nombre,importe)
	VALUES
		('Diferencia (SOBRANTE DE CAJA)', @resultado)
SELECT IDENTITY(int,1,1) AS _orden, * INTO #Reporte FROM #temp;

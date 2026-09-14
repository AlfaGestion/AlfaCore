-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
CREATE TABLE #temp
	(
		[cuenta] [nvarchar] (15) COLLATE  Latin1_General_CI_AI NOT NULL,
		[descripcion] [nvarchar] (150) COLLATE  Latin1_General_CI_AI NOT NULL,
		[cobranzas] [MONEY],
		[ingresos] [MONEY],
		[egresos] [MONEY],
		[transferencias] [MONEY],
		[saldo] [MONEY],
		[saldoant] [MONEY],
		[moneda] [nvarchar] (4),
		[cotizacion] [money],
		[idcajas] [nvarchar] (4),
		[saldomoneda] [money]
	)
	DECLARE @cobranzas MONEY
	DECLARE @ingresos MONEY
	DECLARE @egresos MONEY
	DECLARE @transferencias MONEY
	DECLARE @saldoant MONEY
	DECLARE @fondoFijo MONEY
	DECLARE @saldo MONEY
	DECLARE @moneda NVARCHAR(4)
	DECLARE @cotizacion MONEY
	DECLARE @saldomoneda MONEY
	DECLARE @cuenta NVARCHAR(15)
	DECLARE @descripcion NVARCHAR(150)
	DECLARE @cuentaEfectivo NVARCHAR(15)
	DECLARE @medioDePagoContado NVARCHAR(15)
	DECLARE @cantidadDatos INT
	SET @medioDePagoContado = (SELECT VALOR
	FROM TA_CONFIGURACION
	WHERE CLAVE='MedioDePagoContado')
	SET @cuentaEfectivo = (SELECT CODIGO
	FROM MA_CUENTAS
	WHERE CodigoOpcional=@medioDePagoContado)
	IF @idcaja = ''
		BEGIN
		SELECT @cantidadDatos = COUNT(*)
		FROM #ConsolidadoUnidad
		WHERE FECHA=@fecha
		IF @cantidadDatos > 0
				BEGIN
			DECLARE CRS CURSOR LOCAL FAST_FORWARD FOR
					SELECT DISTINCT CUENTA, DESCRIPCION, MONEDA, COTIZACION, LTRIM(RTRIM(IDCAJAS)) as IDCAJAS
			FROM #ConsolidadoUnidad
			WHERE FECHA=@fecha
		END
			ELSE
				BEGIN
			DECLARE CRS CURSOR LOCAL FAST_FORWARD FOR
					SELECT @cuentaEfectivo as CUENTA, 'Efectivo' as DESCRIPCION, '   1' as MONEDA, 1 as COTIZACION, ISNULL(NULLIF(@idcaja,''),'1') as IDCAJAS WHERE @unidad='' OR EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(UNEGOCIO))=@unidad AND Fecha=@fecha AND (@idcaja='' OR LTRIM(RTRIM(IdCajas))=@idcaja))
		END
	END
	ELSE
		BEGIN
		SELECT @cantidadDatos = COUNT(*)
		FROM #ConsolidadoUnidad
		WHERE FECHA=@fecha AND LTRIM(RTRIM(IdCajas))=@idcaja
		IF @cantidadDatos > 0
				BEGIN
			DECLARE CRS CURSOR LOCAL FAST_FORWARD FOR
					SELECT DISTINCT CUENTA, DESCRIPCION, MONEDA, COTIZACION, LTRIM(RTRIM(IDCAJAS)) as IDCAJAS
			FROM #ConsolidadoUnidad
			WHERE FECHA=@fecha AND LTRIM(RTRIM(IDCAJAS))=@idcaja
		END
			ELSE
				BEGIN
			DECLARE CRS CURSOR LOCAL FAST_FORWARD FOR
					SELECT @cuentaEfectivo as CUENTA, 'Efectivo' as DESCRIPCION, '   1' as MONEDA, 1 as COTIZACION, ISNULL(NULLIF(@idcaja,''),'1') as IDCAJAS WHERE @unidad='' OR EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(UNEGOCIO))=@unidad AND Fecha=@fecha AND (@idcaja='' OR LTRIM(RTRIM(IdCajas))=@idcaja))
		END
	END
	OPEN CRS
	FETCH NEXT FROM CRS INTO @cuenta,@descripcion,@moneda,@cotizacion, @idcaja
	WHILE @@FETCH_STATUS = 0
	BEGIN
		SET @fondoFijo = CASE WHEN @cuenta = @cuentaEfectivo THEN ISNULL((SELECT SUM(cambioInicial) FROM V_Mv_CierreCaja where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND FechaOperativa = @fecha AND LTRIM(RTRIM(IdCaja)) = @idcaja),0) ELSE 0 END;
		SET @transferencias = isnull((Select convert(varchar,convert(decimal(15,2),sum(egreso))) as saldo
		from #TransferenciasUnidad
		where cuenta = @cuenta and fecha = @fecha and LTRIM(RTRIM(origen)) = @idcaja and cotizacion = @cotizacion and moneda = @moneda),0)
		SET @cobranzas = isnull((Select convert(varchar,convert(decimal(15,2),sum(Importe * case when [debe-haber] = 'D' then 1 else -1 end))) as saldo
		from mv_asientos
		where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND cuenta = @cuenta and tc in ('CB','CBCT','CBFP') and fecha = @fecha and LTRIM(RTRIM(idcajas)) = @idcaja and cotizacion = @cotizacion and moneda = @moneda ),0)
		SET @ingresos = isnull((SELECT sum(importe) as saldo
		FROM MV_ASIENTOS
		where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND CUENTA = @cuenta AND [DEBE-HABER] = 'D' and fecha = @fecha and LTRIM(RTRIM(idcajas)) = @idcaja and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC') and cotizacion = @cotizacion and moneda = @moneda
		group by Cuenta),0)
		IF @cuenta <> @cuentaEfectivo
			SET @saldoant = 0
		ELSE
			BEGIN
			IF @csaldoInicial = 1
					SET @saldoant = isnull((select sum(saldo) as saldo
			from #ConsolidadoUnidad
			where cuenta = @cuenta and fecha < @fecha and LTRIM(RTRIM(idcajas)) = @idcaja),0)
				ELSE
					SET @saldoant = 0
		END
		SET @egresos = isnull((SELECT sum(importe) as saldo
		FROM MV_ASIENTOS
		where (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND CUENTA = @cuenta AND [DEBE-HABER] = 'H' and not tc in('CB','CBCT','CBFP','TK ','TKFC','FC') and fecha = @fecha and LTRIM(RTRIM(idcajas)) = @idcaja and cotizacion = @cotizacion and moneda = @moneda
		group by Cuenta),0)
		SET @egresos = @egresos - @transferencias
		IF @csaldoInicial = 1
			SET @saldoant = @saldoant + @fondoFijo
		ELSE
			SET @saldoant = @fondoFijo
		SET @saldo = @saldoant + @cobranzas + @ingresos - @egresos - isnull(@transferencias,0)
		SET @saldomoneda = @saldo * @cotizacion
		INSERT INTO #temp
			(cuenta,descripcion,cobranzas,ingresos,egresos,transferencias,saldo,saldoant,moneda,cotizacion,idcajas,saldomoneda)
		VALUES
			(@cuenta, @descripcion, @cobranzas, @ingresos, @egresos, @transferencias, @saldo, @saldoant, @moneda, @cotizacion, @idcaja, @saldomoneda)
		FETCH NEXT FROM CRS INTO @cuenta,@descripcion,@moneda,@cotizacion,@idcaja
	END
	CLOSE CRS
	DEALLOCATE CRS
SELECT IDENTITY(int,1,1) AS _orden, * INTO #Reporte FROM #temp;

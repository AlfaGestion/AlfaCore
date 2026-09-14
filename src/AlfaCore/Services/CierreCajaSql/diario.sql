-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
CREATE TABLE #temp
	(
		[fecha] [nvarchar] (250) COLLATE  Latin1_General_CI_AI ,
		[rubro] [nvarchar] (250) COLLATE  Latin1_General_CI_AI NOT NULL,
		[descripcion] [nvarchar] (250) COLLATE  Latin1_General_CI_AI NOT NULL,
		[importe] [money],
		[efectivo] [money],
		[tarjeta] [money],
		[saldo] [money],
		[debito] [money],
		[nombre] [nvarchar] (250) COLLATE  Latin1_General_CI_AI NOT NULL,
		[formapago] [nvarchar] (250) COLLATE  Latin1_General_CI_AI NOT NULL,
		[factura] [nvarchar] (250) COLLATE  Latin1_General_CI_AI NOT NULL
	)
	DECLARE @fhDesde DATE
	DECLARE @fhHasta DATE
	IF @esMensual = 1
		BEGIN
		SET @fhDesde = DATEFROMPARTS(YEAR(@fecha),MONTH(@fecha),1);
		SET @fhHasta = EOMONTH(@fecha);
	END
	ELSE
		BEGIN
		SET @fhDesde = @fecha
		SET @fhHasta = @fecha
	END
	DECLARE @desrubro NVARCHAR(30)
	DECLARE @descripcion NVARCHAR(150)
	DECLARE @valorfinal MONEY
	DECLARE @efectivo MONEY
	DECLARE @tarjeta MONEY
	DECLARE @debito MONEY
	DECLARE @saldo MONEY
	DECLARE @nombre NVARCHAR(150)
	DECLARE @formapago NVARCHAR(100)
	DECLARE @tc NVARCHAR(4)
	DECLARE @idcomprobante NVARCHAR(13)
	DECLARE @cuenta NVARCHAR(15)
	IF @idcaja <>''
		DECLARE CRS CURSOR LOCAL FAST_FORWARD FOR
		SELECT CONVERT(NVARCHAR(15),Fecha,103) as fecha, DescRubro as rubro, descripcion, valorfinal, nombre, TC, IDCOMPROBANTE, cuenta
	FROM (
		SELECT A.TC, A.IDCOMPROBANTE, A.SUCURSAL, A.NUMERO, A.LETRA, A.Fecha, A.IDArticulo, A.IDVENDEDOR, A.Consumo, A.UNEGOCIO, A.ValorCosto, A.ValorVenta, A.cuenta,
			A.ArtMoneda, A.ArtCotizacion, A.NOMBRE, A.IVARI, A.DtoItem, A.Usuario, A.ValorVenta * (1 + A.IVARI / 100) * - 1 AS VALORFINAL,
			dbo.V_TA_Rubros.Descripcion AS DescRubro, dbo.V_TA_Rubros.IdRubro, dbo.V_MA_ARTICULOS.DESCRIPCION, dbo.MV_ASIENTOS.IdCajas
		FROM dbo.MV_ASIENTOS RIGHT OUTER JOIN
			(SELECT dbo.V_MV_Stock.TC, dbo.V_MV_Stock.IDCOMPROBANTE, dbo.V_MV_Cpte.SUCURSAL, dbo.V_MV_Cpte.NUMERO, dbo.V_MV_Cpte.LETRA,
				ISNULL(dbo.V_MV_Cpte.FECHA, dbo.V_MV_Stock.FECHA) AS Fecha, dbo.V_MV_Stock.IDArticulo, dbo.V_MV_Stock.IDVENDEDOR,
				SUM(dbo.V_MV_Stock.CantidadUD) AS Consumo, dbo.V_MV_Stock.UNEGOCIO, SUM(dbo.V_MV_Stock.Costo * dbo.V_MV_Stock.Cantidad) AS ValorCosto,
				SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * ISNULL(dbo.V_MV_Cpte.PorcDescuento1,
		0) / 100 * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * ISNULL(dbo.V_MV_Cpte.PorcDescuento2, 0)
		/ 100 * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * ISNULL(dbo.V_MV_Cpte.PorcDescuento3, 0)
		/ 100 * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * ISNULL(dbo.V_MV_Cpte.PorcDescuento4, 0)
		/ 100 * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * dbo.V_MV_Stock.DtoItem / 100 * dbo.V_MV_Stock.Cantidad) AS ValorVenta,
				dbo.V_MV_Stock.CuentaProveedor AS cuenta, dbo.V_MV_Stock.ArtMoneda, dbo.V_MV_Stock.ArtCotizacion, dbo.V_MV_Stock.IVARI,
				dbo.V_MV_Stock.DtoItem , dbo.V_MV_Cpte.Usuario, dbo.V_MV_Cpte.Nombre
			FROM dbo.V_MV_Stock LEFT OUTER JOIN
				dbo.V_MV_Cpte ON dbo.V_MV_Stock.TC = dbo.V_MV_Cpte.TC AND dbo.V_MV_Stock.IDCOMPROBANTE = dbo.V_MV_Cpte.IDCOMPROBANTE
			WHERE (@unidad='' OR LTRIM(RTRIM(dbo.V_MV_Stock.UNEGOCIO))=@unidad) AND (dbo.V_MV_Stock.Anulado = 0) AND (dbo.V_MV_Cpte.TC = 'FC' OR dbo.V_MV_Cpte.TC = 'FP' OR dbo.V_MV_Cpte.TC = 'TK' OR
				dbo.V_MV_Cpte.TC = 'TKFC' OR dbo.V_MV_Cpte.TC = 'NC' OR dbo.V_MV_Cpte.TC = 'NCFP') AND (dbo.V_MV_Cpte.FECHA >= @fhDesde and dbo.V_MV_CPTE.FECHA <= @fhHasta)
			GROUP BY dbo.V_MV_Stock.TC, dbo.V_MV_Stock.IDCOMPROBANTE, dbo.V_MV_Cpte.FECHA, dbo.V_MV_Stock.FECHA, dbo.V_MV_Stock.IDArticulo,
		dbo.V_MV_Stock.IDVENDEDOR, dbo.V_MV_Stock.CuentaProveedor, dbo.V_MV_Stock.ArtMoneda, dbo.V_MV_Stock.DtoItem,
		dbo.V_MV_Stock.ArtCotizacion, dbo.V_MV_Stock.IVARI, dbo.V_MV_Stock.UNEGOCIO, dbo.V_MV_Cpte.Usuario, dbo.V_MV_Cpte.NOMBRE,
		dbo.V_MV_Cpte.SUCURSAL, dbo.V_MV_Cpte.NUMERO, dbo.V_MV_Cpte.LETRA) AS A ON (@unidad='' OR LTRIM(RTRIM(dbo.MV_ASIENTOS.UNEGOCIO))=@unidad) AND dbo.MV_ASIENTOS.CUENTA = A.cuenta AND
				dbo.MV_ASIENTOS.TC = A.TC AND dbo.MV_ASIENTOS.SUCURSAL = A.SUCURSAL AND dbo.MV_ASIENTOS.NUMERO = A.NUMERO AND
				dbo.MV_ASIENTOS.LETRA = A.LETRA LEFT OUTER JOIN
			dbo.V_TA_Rubros LEFT OUTER JOIN
			dbo.V_MA_ARTICULOS ON dbo.V_TA_Rubros.IdRubro = dbo.V_MA_ARTICULOS.IDRUBRO ON A.IDArticulo = dbo.V_MA_ARTICULOS.IDARTICULO
		WHERE  LTRIM(RTRIM(IDCAJAS)) =  @idcaja ) AS A
	ORDER BY Fecha,TC,idcomprobante,descripcion
	ELSE
		DECLARE CRS CURSOR LOCAL FAST_FORWARD FOR
		SELECT CONVERT(NVARCHAR(15),Fecha,103) as fecha, DescRubro as rubro, descripcion, valorfinal, nombre, TC, IDCOMPROBANTE, cuenta
	FROM (
		SELECT A.TC, A.IDCOMPROBANTE, A.SUCURSAL, A.NUMERO, A.LETRA, A.Fecha, A.IDArticulo, A.IDVENDEDOR, A.Consumo, A.UNEGOCIO, A.ValorCosto, A.ValorVenta, A.cuenta,
			A.ArtMoneda, A.ArtCotizacion, A.NOMBRE, A.IVARI, A.DtoItem, A.Usuario, A.ValorVenta * (1 + A.IVARI / 100) * - 1 AS VALORFINAL,
			dbo.V_TA_Rubros.Descripcion AS DescRubro, dbo.V_TA_Rubros.IdRubro, dbo.V_MA_ARTICULOS.DESCRIPCION, dbo.MV_ASIENTOS.IdCajas
		FROM dbo.MV_ASIENTOS RIGHT OUTER JOIN
			(SELECT dbo.V_MV_Stock.TC, dbo.V_MV_Stock.IDCOMPROBANTE, dbo.V_MV_Cpte.SUCURSAL, dbo.V_MV_Cpte.NUMERO, dbo.V_MV_Cpte.LETRA,
				ISNULL(dbo.V_MV_Cpte.FECHA, dbo.V_MV_Stock.FECHA) AS Fecha, dbo.V_MV_Stock.IDArticulo, dbo.V_MV_Stock.IDVENDEDOR,
				SUM(dbo.V_MV_Stock.CantidadUD) AS Consumo, dbo.V_MV_Stock.UNEGOCIO, SUM(dbo.V_MV_Stock.Costo * dbo.V_MV_Stock.Cantidad) AS ValorCosto,
				SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * ISNULL(dbo.V_MV_Cpte.PorcDescuento1,
		0) / 100 * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * ISNULL(dbo.V_MV_Cpte.PorcDescuento2, 0)
		/ 100 * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * ISNULL(dbo.V_MV_Cpte.PorcDescuento3, 0)
		/ 100 * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * ISNULL(dbo.V_MV_Cpte.PorcDescuento4, 0)
		/ 100 * dbo.V_MV_Stock.Cantidad) - SUM(dbo.V_MV_Stock.IMPORTE_S_IVA * dbo.V_MV_Stock.DtoItem / 100 * dbo.V_MV_Stock.Cantidad) AS ValorVenta,
				dbo.V_MV_Stock.CuentaProveedor AS cuenta, dbo.V_MV_Stock.ArtMoneda, dbo.V_MV_Stock.ArtCotizacion, dbo.V_MV_Stock.IVARI,
				dbo.V_MV_Stock.DtoItem , dbo.V_MV_Cpte.Usuario, dbo.V_MV_Cpte.Nombre
			FROM dbo.V_MV_Stock LEFT OUTER JOIN
				dbo.V_MV_Cpte ON dbo.V_MV_Stock.TC = dbo.V_MV_Cpte.TC AND dbo.V_MV_Stock.IDCOMPROBANTE = dbo.V_MV_Cpte.IDCOMPROBANTE
			WHERE (@unidad='' OR LTRIM(RTRIM(dbo.V_MV_Stock.UNEGOCIO))=@unidad) AND (dbo.V_MV_Stock.Anulado = 0) AND (dbo.V_MV_Cpte.TC = 'FC' OR dbo.V_MV_Cpte.TC = 'FP' OR dbo.V_MV_Cpte.TC = 'TK' OR
				dbo.V_MV_Cpte.TC = 'TKFC' OR dbo.V_MV_Cpte.TC = 'NC' OR dbo.V_MV_Cpte.TC = 'NCFP') AND (dbo.V_MV_Cpte.FECHA >= @fhDesde and dbo.V_MV_CPTE.FECHA <= @fhHasta)
			GROUP BY dbo.V_MV_Stock.TC, dbo.V_MV_Stock.IDCOMPROBANTE, dbo.V_MV_Cpte.FECHA, dbo.V_MV_Stock.FECHA, dbo.V_MV_Stock.IDArticulo,
		dbo.V_MV_Stock.IDVENDEDOR, dbo.V_MV_Stock.CuentaProveedor, dbo.V_MV_Stock.ArtMoneda, dbo.V_MV_Stock.DtoItem,
		dbo.V_MV_Stock.ArtCotizacion, dbo.V_MV_Stock.IVARI, dbo.V_MV_Stock.UNEGOCIO, dbo.V_MV_Cpte.Usuario, dbo.V_MV_Cpte.NOMBRE,
		dbo.V_MV_Cpte.SUCURSAL, dbo.V_MV_Cpte.NUMERO, dbo.V_MV_Cpte.LETRA) AS A ON (@unidad='' OR LTRIM(RTRIM(dbo.MV_ASIENTOS.UNEGOCIO))=@unidad) AND dbo.MV_ASIENTOS.CUENTA = A.cuenta AND
				dbo.MV_ASIENTOS.TC = A.TC AND dbo.MV_ASIENTOS.SUCURSAL = A.SUCURSAL AND dbo.MV_ASIENTOS.NUMERO = A.NUMERO AND
				dbo.MV_ASIENTOS.LETRA = A.LETRA LEFT OUTER JOIN
			dbo.V_TA_Rubros LEFT OUTER JOIN
			dbo.V_MA_ARTICULOS ON dbo.V_TA_Rubros.IdRubro = dbo.V_MA_ARTICULOS.IDRUBRO ON A.IDArticulo = dbo.V_MA_ARTICULOS.IDARTICULO
		) AS A
	ORDER BY Fecha,TC,idcomprobante,descripcion
	DECLARE @aTc NVARCHAR(4)
	DECLARE @aSuc NVARCHAR(4)
	DECLARE @aNum NVARCHAR(8)
	DECLARE @aLet NVARCHAR(1)
	DECLARE @medioDePago NVARCHAR(4)
	DECLARE @tcApp NVARCHAR(4)
	DECLARE @importeApp MONEY
	DECLARE @sumaEF MONEY
	DECLARE @sumaVenta MONEY
	DECLARE @sumaDeb MONEY
	DECLARE @sumaDebitos MONEY
	DECLARE @nombreApp NVARCHAR(150)
	DECLARE @sumaTJ MONEY
	DECLARE @sumaCreditos MONEY
	DECLARE @saldoCpte MONEY
	DECLARE @saldoTmp MONEY
	DECLARE @fechaTmp NVARCHAR(20)
	DECLARE @cpteAnterior NVARCHAR(25)
	OPEN CRS
	FETCH NEXT FROM CRS INTO @fechaTmp ,@desrubro,@descripcion,@valorfinal,@nombre, @tc,@idcomprobante,@cuenta
	WHILE @@FETCH_STATUS = 0
	BEGIN
		SET @medioDePago = NULL; SET @importeApp = 0; SET @saldoTmp = 0;
		SET @sumaEF = 0
		SET @sumaDeb = 0
		SET @sumaTJ = 0
		SET @aTc = ''
		SET @aNum = ''
		SET @aLet = ''
		SET @aSuc = ''
		IF @tc = 'NC' or @tc='NCFP'
			BEGIN
			SELECT @aTc = TCO_ORIGEN, @aSuc = SUCURSAL_ORIGEN, @aNum = NUMERO_ORIGEN, @aLet = LETRA_ORIGEN
			FROM MV_APLICACION
			WHERE TC=@tc AND SUCURSAL=SUBSTRING(@idcomprobante,1,4) AND NUMERO=SUBSTRING(@idcomprobante,5,8) AND LETRA = SUBSTRING(@idcomprobante,13,1) AND importe > 0
			IF @aTc <>'' AND @aNum <> ''
					BEGIN
				SELECT @aTc = TC, @aSuc = SUCURSAL, @aNum = NUMERO, @aLet = LETRA
				FROM MV_APLICACION
				WHERE TCO_ORIGEN=@aTc AND SUCURSAL_ORIGEN=@aSuc AND NUMERO_ORIGEN=@aNum AND LETRA_ORIGEN = @aLet
			END
				ELSE
					SELECT @aTc = TCORIGEN, @aSuc = SUBSTRING(ComprobanteOrigen,1,4), @aNum = SUBSTRING(ComprobanteOrigen,5,8), @aLet = SUBSTRING(ComprobanteOrigen,13,1)
			FROM V_MV_CPTE
			WHERE TC=@tc AND IDCOMPROBANTE=@idcomprobante
			IF @aTc <>'' AND @aNum <> ''
						BEGIN
				SELECT @aTc = TC, @aSuc = SUCURSAL, @aNum = NUMERO, @aLet = LETRA
				FROM MV_APLICACION
				WHERE TCO_ORIGEN=@aTc AND SUCURSAL_ORIGEN=@aSuc AND NUMERO_ORIGEN=@aNum AND LETRA_ORIGEN = @aLet and IMPORTE > 0
			END
		END
		ELSE
			SELECT @aTc = TC, @aSuc = SUCURSAL, @aNum = NUMERO, @aLet = LETRA
		FROM MV_APLICACION
		WHERE TCO_ORIGEN=@tc AND SUCURSAL_ORIGEN=SUBSTRING(@idcomprobante,1,4)
			AND NUMERO_ORIGEN=SUBSTRING(@idcomprobante,5,8) AND LETRA_ORIGEN = SUBSTRING(@idcomprobante,13,1) AND IMPORTE <> 0
		BEGIN
			IF @aTc <> '' AND @aSuc <> '' AND @aNum <> '' AND @aLet <> ''
				SELECT @tcApp = v_cobranzasporusuario.TC, @medioDePago = isnull(v_cobranzasporusuario.MedioDePago, ''), @nombreApp = MA_CUENTAS.DESCRIPCION,
				@importeApp = (sum(Importe * CASE WHEN v_cobranzasporusuario.[debe-haber] = 'D' THEN 1 ELSE -1 END))
			FROM v_cobranzasporusuario LEFT JOIN MA_CUENTAS ON v_cobranzasporusuario.CUENTA = MA_CUENTAS.CODIGO
			WHERE TC=@aTc AND SUCURSAL=@aSuc AND LETRA=@aLet AND NUMERO=@aNum
			GROUP BY v_cobranzasporusuario.TC,v_cobranzasporusuario.SUCURSAL,v_cobranzasporusuario.NUMERO,v_cobranzasporusuario.LETRA,
				v_cobranzasporusuario.MedioDePago,v_cobranzasporusuario.cuenta,v_cobranzasporusuario.[debe-haber],MA_CUENTAS.DESCRIPCION
			ELSE
				BEGIN
				SET @medioDePago = ''
				SET @importeApp = 0
			END
			SET @medioDePago = ISNULL(@medioDePago, '')
			SET @importeApp = ISNULL(@importeApp,0)
			IF @medioDePago = 'EF'
				BEGIN
				IF @tc = 'NC' or @tc = 'NCFP'
						SET @sumaEF = @importeApp * -1
					ELSE
						SET @sumaEF = @importeApp
			END
			ELSE IF @medioDePago = 'TJ'
				BEGIN
				IF CHARINDEX('Debito',@nombreApp,1) > 0 or CHARINDEX(N'Débito',@nombreApp,1) > 0
						BEGIN
					IF @tc = 'NC' or @tc = 'NCFP'
								SET @sumaDeb = @importeApp * -1
							ELSE
								SET @sumaDeb =  @importeApp
				END
					ELSE
						BEGIN
					IF @tc = 'NC' or @tc = 'NCFP'
								SET @sumaTJ = @importeApp * -1
							ELSE
								SET @sumaTJ = @importeApp
				END
			END
			SELECT @saldoCpte = isnull(SUM(SALDO),0)
			FROM VE_CPTES_SALDOS
			WHERE TC=@tc AND SUCURSAL+NUMERO+LETRA=@idcomprobante AND CUENTA=@cuenta
			SET @saldoCpte = ISNULL(@saldoCpte,0)
			IF @saldoCpte > 0
				BEGIN
				SELECT @saldoTmp = isnull(importe,0)
				FROM V_MV_CPTE
				WHERE TCORIGEN=@tc AND COMPROBANTEORIGEN=@idcomprobante AND (TC='NC' OR TC='NCFP') AND IMPORTE>0
				SET @saldoCpte = @saldoCpte - isnull(@saldoTmp,0)
				IF @saldoCpte < 0 SET @saldoCpte = 0
			END
			IF @medioDePago = '' AND @saldoCpte = 0
				SET @sumaEF = @valorfinal
			BEGIN
				SET @sumaEF = ISNULL(@sumaEF,0)
				SET @sumaTJ = isnull(@sumaTJ,0)
				SET @sumaDeb = ISNULL(@sumaDeb,0)
				SET @formapago = 'Cta Cte'
				IF (@sumaEF > 0 AND @sumaTJ > 0) OR (@sumaEF >0 AND @sumaDeb>0) OR (@sumaTJ>0 AND @sumaDeb > 0)
					SET @formapago = 'Varios'
				IF @sumaEf > 0 AND @sumaTJ = 0 AND @sumaDeb = 0
					SET @formapago = 'Efectivo'
				IF @sumaEF = 0 AND @sumaTJ > 0 AND @sumaDeb = 0
					SET @formapago = 'Tarjeta'
				IF @sumaEF = 0 AND @sumaTJ = 0 AND @sumaDeb > 0
					SET @formapago = N'Débito'
			END
		END
		BEGIN
			IF isnull(@cpteAnterior,'') = @tc + '-' + @idcomprobante
				BEGIN
				SET @saldoCpte = 0;
				SET @sumaDeb = 0
				SET @sumaTJ = 0
				SET @sumaEF = 0
			END
		END
		SET @cpteAnterior = @tc + '-' + @idcomprobante
		INSERT INTO #temp
			(fecha,rubro,descripcion,importe,efectivo,tarjeta,saldo,debito,nombre,formapago,factura)
		VALUES
			(@fechaTmp , isnull(@desrubro,''), isnull(@descripcion,''), isnull(@valorfinal,0), @sumaEF, @sumaTJ, isnull(@saldoCpte,0), @sumaDeb, @nombre, @formapago, @tc + '-' + @idcomprobante)
		FETCH NEXT FROM CRS INTO @fechaTmp,@desrubro,@descripcion,@valorfinal,@nombre, @tc,@idcomprobante,@cuenta
	END
	CLOSE CRS
	DEALLOCATE CRS
SELECT IDENTITY(int,1,1) AS _orden, * INTO #Reporte FROM #temp;

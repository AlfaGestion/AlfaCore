
GO

/****** Object:  StoredProcedure [dbo].[wsSysMobileSPPedidosV_MV_CPTEINSUMOS]    Script Date: 12/05/2021 14:53:19 ******/
DROP PROCEDURE [dbo].[wsSysMobileSPPedidosV_MV_CPTEINSUMOS]
GO

/****** Object:  StoredProcedure [dbo].[wsSysMobileSPPedidosV_MV_CPTEINSUMOS]    Script Date: 12/05/2021 14:53:19 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO


--**************************************************
--ESTAMOS MANDANDO FIJA LA LISTA MAY
--**************************************************
-- =============================================
-- Author:		DIEGO MARTINEZ
-- Create date: 23/05/2014
-- Description:	ALTA PEDIDOS (cabecera)
--              @pIdMedicion, SE INTERPRETA COMO UN ALTA Y DEVUELVE LA IDENTIDAD EN
--              EL PARAMETRO @pIdMedicionRES
-- =============================================
CREATE PROCEDURE [dbo].[wsSysMobileSPPedidosV_MV_CPTEINSUMOS]
		@pIdCpte						 INT = null,
		@pIdArticulo					 NVARCHAR(25),
		@pCantidad						 FLOAT,
		@pImporteUnitario				 MONEY,
		@pPorcDescuento                  FLOAT,
		@pResultado 					 SMALLINT = NULL OUTPUT,
		@pMensaje 						 VARCHAR(255) = NULL OUTPUT,
		@pIdVMVCpteInsumosRES			 INT = NULL OUTPUT
AS
DECLARE @tc NVARCHAR(4)
DECLARE @idComprobante NVARCHAR(13)
DECLARE @idComplemento INT
DECLARE @tmpConfigIVA NVARCHAR(2)
DECLARE @idlista NVARCHAR(3)
DECLARE @descripcion NVARCHAR(50)
DECLARE @idUnidad NVARCHAR(4)
DECLARE @importe MONEY
DECLARE @importeCiva MONEY
DECLARE @idMoneda NVARCHAR(4)
DECLARE @total MONEY
DECLARE @exento BIT
DECLARE @clasePrecio INT
DECLARE @alicIva FLOAT
DECLARE @porcDto FLOAT
DECLARE @importeDto MONEY
DECLARE @unidadBase NVARCHAR(4)
DECLARE @cantKG FLOAT
DECLARE @pedidosPreparados NVARCHAR(2)
DECLARE @tmpConfig NVARCHAR(50)
DECLARE @condicionIva NVARCHAR(4)
DECLARE @err INT
DECLARE @IdCliente NVARCHAR(15)
SET NOCOUNT ON
	
--SET XACT_ABORT OFF
	--OBTENGO DATOS DEL COMPROBANTE DEL V_MV_CPTE
	SELECT @tc = TC,@idComprobante = IDCOMPROBANTE,
		   @idComplemento = IDCOMPLEMENTO,
		   @clasePrecio = CLASEPRECIO,
		   @condicionIva = CONDICIONIVA,
		   @IdCliente = CUENTA,
		   @idlista = IdLista
	FROM V_MV_CPTE
	WHERE ID = @pIdCpte
	
	SELECT  @pedidosPreparados = VALOR FROM TA_CONFIGURACION WHERE CLAVE='V_SoloPedidosPreparados'
	
	-- SETEO DE IDARTICULO
	SET @pIdArticulo = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pIdArticulo)),25)
	--SET @idlista = 'MAY'
	--OBTENGO DATOS DEL ARTICULO
	PRINT '1'
	SELECT @descripcion = DESCRIPCION, @unidadBase = IDUNIDAD,@idUnidad = UD_TTE, @alicIva = TASAIVA,@exento = EXENTO, @idMoneda = MONEDA
	FROM V_MA_ARTICULOS
	WHERE IDARTICULO = @pIdArticulo
	PRINT '2'
	PRINT @alicIva
	PRINT '3'
	--VALORES DEFAULT // VALIDACIONES
	IF ((@alicIva = NULL) OR (@alicIva IS NULL) OR (@alicIva = 0))
		BEGIN
			PRINT '3.1'
			SET @tmpConfig = dbo.FN_OBTIENE_VALOR_CONFIGURACION('PIVA','21')
			PRINT @tmpConfig
			PRINT '3.2'
			SET @alicIva = CAST(@tmpConfig AS FLOAT )
			PRINT @alicIva
			PRINT '4'
		END
	IF (@condicionIva = NULL) OR (@condicionIva IS NULL)
		SET @condicionIva = '   1'
	IF (@exento = NULL) OR (@exento IS NULL)
		SET @exento = 0
	/*SET @importeCiva = dbo.FN_PRECIO_CON_IVA(@pImporteUnitario,@alicIva)
	IF LTRIM(RTRIM(@condicionIva)) IN ('3','4','5')
		SET @importe = dbo.FN_PRECIO_CON_IVA(@pImporteUnitario,@alicIva)
	ELSE
		SET @importe = @pImporteUnitario
	SET @total = @importe * @pCantidad
*/
	--TODOS LOS ARTICULOS TIENEN IVA
	-- LE SACO IVA
	
	SET @tmpConfigIVA = dbo.FN_OBTIENE_VALOR_CONFIGURACION('MaestroArticuloConIVA','NO')
	PRINT @tmpConfigIVA
	
	IF @tmpConfigIVA = 'SI' 
		IF @exento=0
			BEGIN
				SET @pImporteUnitario = dbo.FN_PRECIO_SIN_IVA(@pImporteUnitario,@alicIva)	
				SET @importe =  dbo.FN_PRECIO_CON_IVA(@pImporteUnitario,@alicIva)--@pImporteUnitario
			END
		ELSE 
			BEGIN
				SET @pImporteUnitario =  dbo.FN_PRECIO_SIN_IVA(@pImporteUnitario,@alicIva)	
				SET @importe = @pImporteUnitario
			END
	ELSE
		BEGIN
			IF @clasePrecio = 2
				SET @tmpConfigIVA =  dbo.FN_OBTIENE_VALOR_CONFIGURACION('Clase2ConIVA','NO')
				if @tmpConfigIVA = 'SI'
					if @exento=0
						BEGIN
							SET @pImporteUnitario =  dbo.FN_PRECIO_SIN_IVA(@pImporteUnitario,@alicIva)	
							SET @importe = dbo.FN_PRECIO_CON_IVA(@pImporteUnitario,@alicIva)
						END
					ELSE
						BEGIN
							SET @pImporteUnitario =  dbo.FN_PRECIO_SIN_IVA(@pImporteUnitario,@alicIva)	
							SET @importe = @pImporteUnitario
						END
		END
	--SACAR IVA SI CUMPLE ALGUNA DE LAS 2 CONDICIONES
	
	/*
	IF LTRIM(RTRIM(@condicionIva)) IN ('3','4','5')
		if @exento = 0 
			SET @importe = dbo.FN_PRECIO_CON_IVA(@pImporteUnitario,@alicIva)
	ELSE
		SET @importe = @pImporteUnitario*/
	
	
	SET @total = @importe * @pCantidad
	
	if @pCantidad=0
		SET @pCantidad=1
	
	DECLARE @coeficiente FLOAT
	DECLARE @cantidadUD FLOAT
	--IF @pedidosPreparados='NO'
	--	SET @idUnidad = @unidadBase
	--ELSE
		--OBTENGO LA CANTIDAD DE KG SEGUN EQUIVALENCIA
		SELECT @coeficiente=COEFICIENTE FROM S_TA_EQUIV WHERE IDUNIDAD=@idUnidad AND IDUNIDAD_EQUIV=@unidadBase AND IDARTICULO= @pIdArticulo
		SET @cantKG = @pCantidad * @coeficiente
		
		--PARA QUE LA CANTIDADUD LA HAGA SEGUN LA EQUIVALENCIA
		SET @cantidadUD = @cantKG / @pCantidad  
	
SET DATEFORMAT YMD
SET NOCOUNT ON
BEGIN TRANSACTION
	BEGIN
		INSERT INTO V_MV_CPTEINSUMOS(  TC,IDCOMPROBANTE,IDCOMPLEMENTO,CLASEPRECIO,
									   IDARTICULO,DESCRIPCION,IDUNIDAD,ALICIVA,EXENTO,
									   CANTIDADUD,CANTIDAD,
									   IMPORTE_S_IVA,
									   IMPORTE,
									   PORCDTO,
									   IMPORTEDTO,
									   TOTAL,
									   IdLista,IDUNIDADBASE,EQUIV_UDBASE,CANT_BL,CANT_KG)
								VALUES (
									   @tc,@idComprobante,0,@clasePrecio,
									   @pIdArticulo,@descripcion,@idUnidad,@alicIva,@exento,
									   @cantidadUD,@pCantidad,
									   @pImporteUnitario,
									   @importe,
									   @pPorcDescuento,
									   0,
									   @total,
									   @idlista,@unidadBase,@pCantidad,1,@cantKG
										)
				IF @@error <> 0 GOTO ERRORES --OR @@ROWCOUNT <> 1
					/*BEGIN
						ROLLBACK TRANSACTION
						SET @pIdVMVCpteInsumosRES = NULL
						SET @pResultado = 21
						SET @pMensaje = 'No pudo darse de alta EL PEDIDO correctamente'
						RETURN
					END
					*/
		COMMIT TRANSACTION
		
		UPDATE V_MV_Cpte SET IMPORTE=IMPORTE+(@importe*@pCantidad), IMPORTE_S_IVA=IMPORTE_S_IVA+(@pImporteUnitario*@pCantidad)
		,ImporteInsumos=ImporteInsumos+(@importe*@pCantidad)
		WHERE TC=@tc AND IDCOMPROBANTE=@idComprobante AND IDCOMPLEMENTO=0
		
		
		SET @pResultado = 11
		SET @pMensaje = 'El Pedido se ha dado de alta con éxito'
		SET @pIdVMVCpteInsumosRES = @@IDENTITY
	END
RETURN
ERRORES:
ROLLBACK TRANSACTION
SET @pIdVMVCpteInsumosRES = NULL
SET @pResultado = 21
SET @pMensaje = 'No pudo darse de alta EL PEDIDO correctamente'

GO
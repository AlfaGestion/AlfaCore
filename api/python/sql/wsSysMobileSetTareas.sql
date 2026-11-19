GO

/****** Object:  StoredProcedure [dbo].[wsSysMobileSetCobranzas]    Script Date: 17/05/2021 11:56:36 ******/
DROP PROCEDURE [dbo].[wsSysMobileSetTareas]
GO

/****** Object:  StoredProcedure [dbo].[wsSysMobileSetCobranzas]    Script Date: 17/05/2021 11:56:36 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO



CREATE PROCEDURE [dbo].[wsSysMobileSetTareas]
		@pCliente						 nvarchar(15) = null,
		@pVendedor						 nvarchar(4) = null,			
		@pFecha	     					 datetime = null,		
		@pObs							 varchar(250),
        @pidTarea                        varchar(4) = null,
		@pResultado 					 smallint = NULL OUTPUT,
		@pMensaje 						 varchar(255) = NULL OUTPUT
AS
DECLARE @Tc NVARCHAR(4) 
DECLARE @IdComprobante NVARCHAR(13)
DECLARE @nombre NVARCHAR(50)
DECLARE @domicilio NVARCHAR(50)
DECLARE @calle NVARCHAR(50)
DECLARE @numero NVARCHAR(50)
DECLARE @piso NVARCHAR(50)
DECLARE @departamento NVARCHAR(50)
DECLARE @telefono NVARCHAR(100)
DECLARE @localidad NVARCHAR(50)
DECLARE @idProvincia NVARCHAR(4)
DECLARE @codigoPostal NVARCHAR(10)
DECLARE @documentoTipo NVARCHAR(4)
DECLARE @documentoNumero NVARCHAR(13)
DECLARE @condicionIva NVARCHAR(50)
DECLARE @idCondCpraVta NVARCHAR(4)
DECLARE @comentarios NVARCHAR(100)
DECLARE @clasePrecio int
DECLARE @idlista NVARCHAR(4)
DECLARE @FechaHoraGrabacion DATETIME
DECLARE @unegocio NVARCHAR(4)
DECLARE @USUARIO AS NVARCHAR(50)
DECLARE @descTarea as NVARCHAR(50)

SET NOCOUNT ON 
SET @Tc = 'OT'

SELECT  @nombre = RAZON_SOCIAL,
	    @calle = ISNULL(CALLE,'.'),
		@numero = ISNULL(NUMERO,'.'),
		@piso = ISNULL(PISO,''),
		@departamento = ISNULL(DEPARTAMENTO,''),
		@telefono = TELEFONO,
		@localidad = LOCALIDAD,
		@idProvincia = PROVINCIA,
		@codigoPostal = CPOSTAL,
		@documentoTipo = DOCUMENTO_TIPO,
		@documentoNumero = NUMERO_DOCUMENTO,
		@condicionIva = IVA,
		@idCondCpraVta = IDCOND_CPRA_VTA,
		@clasePrecio = CLASE,
		@comentarios = OBSERVACIONES,
		@idlista= IdLista
FROM VT_CLIENTES
WHERE CODIGO = @pCliente

SET @domicilio = @calle + ' ' + @numero 
IF (@piso <> '') SET @domicilio= @domicilio+ ' ' + LTRIM(RTRIM(@piso)) + ' Piso ' 
IF (@departamento <> '') SET @domicilio= @domicilio+ ' Dpto: ' +  LTRIM(RTRIM(@departamento))
IF (@condicionIva=NULL) SET @condicionIva = '   1'
SET @idComprobante = dbo.FN_OBTIENE_PROXIMO_NUMERO_CPTE (@tc,'9999','X')
IF (@clasePrecio IS NULL) OR (@clasePrecio = NULL)
	SET @clasePrecio = 1
	
IF @pObs = ''
	SET @pObs = 'Cob. ' + @nombre

SET @USUARIO = SYSTEM_USER
SET @pVendedor = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)),4)

SELECT @unegocio = VALOR FROM TA_CONFIGURACION WHERE CLAVE ='UNEGOCIO' --dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)),4)

SET @unegocio=dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)),4)

IF (@unegocio IS NULL) OR (@unegocio='')
	SET @unegocio='   1'

SET @FechaHoraGrabacion  = @pFecha
SET @pFecha = CONVERT(VARCHAR,@pFecha,103)

DECLARE @pIdReparto INT
SET @pIdReparto=0

SET @descTarea = ''
IF @pidTarea <> '' SELECT @descTarea = Descripcion FROM V_TA_TAREAS WHERE LTRIM(RTRIM(IDTAREA)) = ltrim(rtrim(@pidTarea))

--SET DATEFORMAT YMD
SET NOCOUNT ON
BEGIN TRANSACTION 
	BEGIN
		INSERT INTO V_MV_CPTE(  TC,IDCOMPROBANTE,IDCOMPLEMENTO,
								FECHA,FECHAESTFIN,CUENTA,
								NOMBRE,DOMICILIO,TELEFONO,
								LOCALIDAD,IDPROVINCIA,CODIGOPOSTAL,
								DOCUMENTOTIPO,DOCUMENTONUMERO,CONDICIONIVA,
								IDCOND_CPRA_VTA,COMENTARIOS,IMPORTE,IMPORTE_S_IVA,
								MONEDA,IDVENDEDOR,CLASEPRECIO,IdLista,FechaHora_Grabacion,IdReparto,UNEGOCIO,UNEGOCIO_DESTINO,Usuario,SUCURSAL,NUMERO,LETRA)					 
					  VALUES (
								@Tc,@IdComprobante,0,
								@pFecha,@pFecha,@pCliente,
								@Nombre,@Domicilio,@Telefono,
								@Localidad,@idProvincia,@codigoPostal,
								@documentoTipo,@documentoNumero,@condicionIva,
								@idCondCpraVta,'',0,0,
								'   1',@pVendedor,@clasePrecio,@idlista,@FechaHoraGrabacion,@pIdReparto,@unegocio,@unegocio,@USUARIO,
                                SUBSTRING(@IdComprobante,1,4),SUBSTRING(@IdComprobante,5,8),SUBSTRING(@IdComprobante,12,1))
        
        INSERT INTO V_MV_CPTETAREAS (TC,IDCOMPROBANTE,IDCOMPLEMENTO,IDTAREA,DESCRIPCION,HORAS,EXENTO,
        DESCRIPCIONADICIONAL,FECHAESTINICIO,SECUENCIA, USUARIO)
        VALUES (@Tc, @IdComprobante, 0, dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pidTarea)),4), @descTarea, 1, 0,
        @pObs,@FechaHoraGrabacion,1,@USUARIO)
	
				IF @@ERROR <> 0 OR @@ROWCOUNT <> 1
					BEGIN
						ROLLBACK TRANSACTION 
						
						SET @pResultado = 21
						SET @pMensaje = 'No pudo darse de alta EL PEDIDO correctamente'
					RETURN
				END
		COMMIT TRANSACTION 
		SET @pResultado = 11		
		SET @pMensaje = 'El Pedido se ha dado de alta con éxito'
		--PRINT 'El Pedido se ha dado de alta con éxito'
		
		--PRINT @pIdComprobanteRES 
	END


GO
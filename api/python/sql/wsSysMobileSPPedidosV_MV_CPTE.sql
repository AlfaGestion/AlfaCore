GO

/****** Object:  StoredProcedure [dbo].[wsSysMobileSPPedidosV_MV_CPTE]    Script Date: 27/11/2020 12:50:14 ******/
DROP PROCEDURE [dbo].[wsSysMobileSPPedidosV_MV_CPTE]
GO

/****** Object:  StoredProcedure [dbo].[wsSysMobileSPPedidosV_MV_CPTE]    Script Date: 27/11/2020 12:50:14 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO



CREATE PROCEDURE [dbo].[wsSysMobileSPPedidosV_MV_CPTE]
		@pCliente						 nvarchar(15) = null,
		@pVendedor						 nvarchar(4) = null,			
		@pFecha	     					 datetime = null,
		@pReparto							bit=0,			
		@pResultado 					 smallint = NULL OUTPUT,
		@pMensaje 						 varchar(255) = NULL OUTPUT,
		@pIdComprobanteRES				 int = NULL OUTPUT
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

SET NOCOUNT ON 
SET @Tc = 'NP'
--SET @idlista = 'MAY'
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
		/*,
		@pVendedor=IdVendedor*/
FROM VT_CLIENTES
WHERE CODIGO = @pCliente
SET @domicilio = @calle + ' ' + @numero 
IF (@piso <> '') SET @domicilio= @domicilio+ ' ' + LTRIM(RTRIM(@piso)) + ' Piso ' 
IF (@departamento <> '') SET @domicilio= @domicilio+ ' Dpto: ' +  LTRIM(RTRIM(@departamento))
IF (@condicionIva=NULL) SET @condicionIva = '   1'
SET @idComprobante = dbo.FN_OBTIENE_PROXIMO_NUMERO_CPTE (@tc,'9999','X')
IF (@clasePrecio IS NULL) OR (@clasePrecio = NULL)
	SET @clasePrecio = 1
	
SET @USUARIO = SYSTEM_USER
SET @pVendedor = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)),4)

SELECT @unegocio = VALOR FROM TA_CONFIGURACION WHERE CLAVE ='UNEGOCIO' --dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)),4)

SET @unegocio=dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@unegocio)),4)

IF (@unegocio IS NULL) OR (@unegocio='')
	SET @unegocio='   1'

SET @FechaHoraGrabacion  = @pFecha
SET @pFecha = CONVERT(VARCHAR,@pFecha,103)

DECLARE @pIdReparto INT

IF (@pReparto=1) SET @pIdReparto=-1
IF (@pReparto=0) SET @pIdReparto=0

DECLARE @CHOFER VARCHAR(10)
SET @CHOFER=''

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
								MONEDA,IDVENDEDOR,CLASEPRECIO,IdLista,FechaHora_Grabacion,IdReparto,CHOFER,UNEGOCIO,UNEGOCIO_DESTINO,Usuario)					 
					  VALUES (
								@Tc,@IdComprobante,0,
								@pFecha,@pFecha,@pCliente,
								@Nombre,@Domicilio,@Telefono,
								@Localidad,@idProvincia,@codigoPostal,
								@documentoTipo,@documentoNumero,@condicionIva,
								@idCondCpraVta,@comentarios,0,0,
								'   1',@pVendedor,@clasePrecio,@idlista,@FechaHoraGrabacion,@pIdReparto,@CHOFER,@unegocio,@unegocio,@USUARIO)
									
		
				IF @@ERROR <> 0 OR @@ROWCOUNT <> 1
					BEGIN
						ROLLBACK TRANSACTION 
						SET @pIdComprobanteRES = NULL
						SET @pResultado = 21
						SET @pMensaje = 'No pudo darse de alta EL PEDIDO correctamente'
					RETURN
				END
		COMMIT TRANSACTION 
		SET @pResultado = 11		
		SET @pMensaje = 'El Pedido se ha dado de alta con �xito'
		--PRINT 'El Pedido se ha dado de alta con �xito'
		SET @pIdComprobanteRES = @@IDENTITY
		--PRINT @pIdComprobanteRES 
	END


GO




GO

/****** Object:  StoredProcedure [dbo].[sp_web_altaClienteAlfa]    Script Date: 23/7/2024 17:26:41 ******/
DROP PROCEDURE [dbo].[sp_web_altaClienteAlfa]
GO

/****** Object:  StoredProcedure [dbo].[sp_web_altaClienteAlfa]    Script Date: 23/7/2024 17:26:41 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO



CREATE PROCEDURE [dbo].[sp_web_altaClienteAlfa]
	@pNombre					nvarchar(50),
	@pEmail						nvarchar(250) = null,
	@pTel 						nvarchar(50) = null,
	@pCuit	 					nvarchar(13) = null,
	@pIva						nvarchar(4) = null,
	@pCodigoCuenta				varchar(9) = NULL OUTPUT
AS

SET NOCOUNT ON
DECLARE @CUENTA NVARCHAR(9)


DECLARE	@pCalle	     				nvarchar(50) 
DECLARE	@pNumero					nvarchar(6) 
DECLARE	@pLocalidad                 nvarchar(50)  
DECLARE	@pContacto 					nvarchar(70) 
DECLARE	@pObs 						nvarchar(200) 
DECLARE	@pVendedor					nvarchar(4)  
DECLARE	@pCp						nvarchar(4) 
DECLARE @pTipoDoc                   nvarchar(4)  
--DECLARE	@pCodigoCuenta				varchar(9) 

SET @pCalle = '.'
SET @pNumero = '1'
SET @pLocalidad = '.'
SET @pContacto = ''
SET @pObs = ''
SET @pVendedor = ''
SET @pCp = '1'

IF @pVendedor <> ''
	SET @pVendedor = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)),4)

SET @CUENTA = (SELECT MAX(CODIGO) +1 FROM VT_CLIENTES with(nolock) WHERE CODIGO LIKE '11201%')

IF @pIva = '' SET @pIva = '   1'
IF @pTipoDoc = '' SET @pTipoDoc = '   1'

SET @pIva = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pIva)),4)
SET @pTipoDoc = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pTipoDoc)),4)


SET NOCOUNT ON
BEGIN TRANSACTION
BEGIN
	
	BEGIN
		INSERT INTO MA_CUENTAS([CODIGO],[DV],[DESCRIPCION],[TITULO],[AJUSTE],[INDICE],[BLOQUEO],[MANUAL],[MANUAL_HABER],[FechaHora_Grabacion],[FechaHora_Modificacion],[Libro_Iva_Compras],[Libro_Iva_Ventas],[Bienes],[Codigo_Deprec_Acumulada],[Codigo_Deprec_Ejercicio],[Dada_De_Baja],[TipoVista],[PideVencimiento],[CuentaPrincipal],[CajaYBanco],[CodigoOpcional],[MedioDePago],[Moneda],[NoPideDatosTarjeta],[Terceros],[CodTarjeta_ConcAuto],[usaCCostos],[CC_DIST]) 
		VALUES(@CUENTA,Null,@pNombre,0,0,'0',0,'',Null,Null,Null,0,1,0,Null,Null,0,'CL',1,'',0,'',Null,Null,0,0,Null,0,Null)

		INSERT INTO MA_CUENTASADIC([CODIGO],[CONTACTO],[CALLE],[NUMERO],[PISO],[DEPARTAMENTO],[CPOSTAL],
		[LOCALIDAD],[PROVINCIA],[PAIS],[TELEFONO],[FAX],[MAIL],[DOCUMENTO_TIPO],[NUMERO_DOCUMENTO],[IVA],
		[OBSERVACIONES],[Limite_Credito],[idCond_Cpra_Vta],[IDCategoria],[FechaHora_Grabacion],[FechaHora_Modificacion],
		[IdLista],[Clase],[IdVendedor],[IdMotivoVta],[IdMotivoCpra],[ExentoIvaServicios],[ExentoIvaArticulos],
		[ExentoIVAOtros],[Descuento],[RETIVA_IdRetencion],[RETIBR_IdRetencion],[RETGAN_IdRetencion],[LugarEntrega],
		[CALLE_FIS],[NUMERO_FIS],[PISO_FIS],[DEPARTAMENTO_FIS],[CPOSTAL_FIS],[LOCALIDAD_FIS],[PROVINCIA_FIS],
		[PAIS_FIS],[TELEFONO_FIS],[FAX_FIS],[CodigoImputacion],[Consignacion],[ZONA],[RETIVA_NROINS],[RETIBR_NROINS],
		[RETGAN_NROINS],[Clave],[IdVendedor1],[IdVendedor2],[IdVendedor3],[IdVendedor4],[IdVendedor5],[Limite_Credito1],
		[Limite_Credito2],[Limite_Credito3],[Limite_Credito4],[Limite_Credito5],[DatosPago],[WEB],[FCC_Sin_OC],[Dto1],
		[Dto2],[Dto3],[OCCambiaCosto],[idCond_Cpra_Vta_Vdor1],[idCond_Cpra_Vta_Vdor2],[idCond_Cpra_Vta_Vdor3],[idCond_Cpra_Vta_Vdor4],
		[idCond_Cpra_Vta_Vdor5],[EmiteSobresPorOP],[ProveedorLocal],[ProveedorCompartido],[idDepositoEntregaOC],[FhVtoExento],
		[RETIBR_IdRetencionOnLine],[SIAP_IVA_REG_RET_PERC],[IdListaRMTRF],[Clasificacion],[RETSUSS_IdRetencion]) 
		VALUES
		(@CUENTA,@pContacto,@pCalle,@pNumero,Null,Null,@pCp,@pLocalidad,'   1','   1',@pTel,Null,@pEmail,@pTipoDoc,@pCuit,
		@pIva,@pObs,0,'   1',Null,GETDATE(),Null,Null,'1',@pVendedor,'   1',Null,0,0,0,0,Null,Null,Null,Null,
		Null,Null,Null,Null,Null,Null,Null,Null,Null,Null,'',0,Null,Null,Null,Null,Null,Null,Null,Null,Null,
		Null,0,0,0,0,0,Null,Null,0,0,0,0,0,Null,Null,Null,Null,Null,0,0,0,Null,Null,Null,Null,Null,Null,Null)
	END

	IF @@ERROR <> 0 OR @@ROWCOUNT <> 1
					BEGIN
		ROLLBACK TRANSACTION

		SET @pCodigoCuenta = NULL
		RETURN
	END
	COMMIT TRANSACTION
	SET @pCodigoCuenta = @CUENTA

END


GO



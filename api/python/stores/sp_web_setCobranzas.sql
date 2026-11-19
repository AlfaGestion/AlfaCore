
DECLARE
	@Tc 							 nvarchar(4),
	@pCliente						 nvarchar(15),
	@pVendedor						 nvarchar(4),
	@pFecha	     					 datetime ,
	@pImporte						 real,
	@pObs							 varchar(250),
	@pMp							 varchar(100),
	@idWeb							 varchar(100),
	@pChequeNumero					 nvarchar(50),
	@pChequeVencimiento				 datetime,
	@pChequeIdBanco				     nvarchar(4) 

	SET @Tc = '#TC'
	SET @pCliente = '#CLIENTE'
	SET @pVendedor = '#VENDEDOR'
	SET @pFecha = '#FECHA'	
    SET @pImporte = #IMPORTE	
    SET @pObs = '#OBSERVACIONES'
	SET @pMp = '#CUENTA_MP'
	SET @idWeb = '#IDWEB'
	SET @pChequeNumero = '#CHEQUE_NUMERO'
	SET @pChequeVencimiento = '#CHEQUE_VENCIMIENTO'	
    SET @pChequeIdBanco = '#CHEQUE_IDBANCO' 

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


IF @Tc = '' SET @Tc = 'CB'

SELECT @nombre = ISNULL(RAZON_SOCIAL,''),
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

IF @pChequeIdBanco <> ''
	SET @pChequeIdBanco = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pChequeIdBanco)),4)
ELSE
	SET @pChequeIdBanco = Null

IF @pObs = ''
	SET @pObs = 'Cob. ' + @nombre

SET @USUARIO = SYSTEM_USER
SET @pVendedor = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)),4)

SELECT @unegocio = VALOR
FROM TA_CONFIGURACION
WHERE CLAVE ='UNEGOCIO'

SET @unegocio=dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@unegocio)),4)

IF (@unegocio IS NULL) OR (@unegocio='')
	SET @unegocio='   1'

SET @FechaHoraGrabacion  = @pFecha
SET @pFecha = CONVERT(VARCHAR,@pFecha,103)

DECLARE @pIdReparto INT
SET @pIdReparto=0

DECLARE @NroAsiento INT
DECLARE @Periodo NVARCHAR(6)
DECLARE @CuentaMP NVARCHAR(15)
DECLARE @Mes_operativo INT

SET @CuentaMP = @pMp
IF @CuentaMP = '' SET @CuentaMP = (SELECT VALOR
FROM TA_CONFIGURACION
WHERE CLAVE='CUENTA_CAJA')
--Efectivo
IF @CuentaMP = '' SET @CuentaMP	= '111010001'

SET @Mes_operativo = month(@pFecha)

SET @Periodo = (SELECT top 1
	periodo
FROM MV_EJERCICIOS
WHERE [FECHA DESDE]<=@pFecha and [FECHA HASTA]>=@pFecha)
IF (@Periodo is null)
	SET @Periodo = '0'

SET @NroAsiento = (SELECT MAX([NUMERO ASIENTO])
FROM MV_ASIENTOS
WHERE MES_OPERATIVO=@Mes_operativo AND RTRIM(LTRIM(TIPO_REG))='CB' AND RTRIM(LTRIM(PERIODO))= @Periodo)
IF @NroAsiento is null or @NroAsiento = ''
	SET @NroAsiento = 1


BEGIN TRANSACTION
BEGIN
	INSERT INTO V_MV_CPTE
		( TC,IDCOMPROBANTE,IDCOMPLEMENTO,
		FECHA,FECHAESTFIN,CUENTA,
		NOMBRE,DOMICILIO,TELEFONO,
		LOCALIDAD,IDPROVINCIA,CODIGOPOSTAL,
		DOCUMENTOTIPO,DOCUMENTONUMERO,CONDICIONIVA,
		IDCOND_CPRA_VTA,COMENTARIOS,IMPORTE,IMPORTE_S_IVA,
		MONEDA,IDVENDEDOR,CLASEPRECIO,IdLista,FechaHora_Grabacion,IdReparto,UNEGOCIO,UNEGOCIO_DESTINO,
        Usuario,
		SUCURSAL,NUMERO,LETRA,OBSERVACIONES)
	VALUES
		(
			@Tc, @IdComprobante, 0,
			@pFecha, @pFecha, @pCliente,
			@Nombre, @Domicilio, @Telefono,
			@Localidad, @idProvincia, @codigoPostal,
			@documentoTipo, @documentoNumero, @condicionIva,
			@idCondCpraVta, @pObs, @pImporte, 0,
			'   1', @pVendedor, @clasePrecio, @idlista, @FechaHoraGrabacion, @pIdReparto, @unegocio, @unegocio, @USUARIO,
			SUBSTRING(@IdComprobante,1,4), SUBSTRING(@IdComprobante,5,8), SUBSTRING(@IdComprobante,12,1),'Pedido Web Nro: ' + @idWeb)

	INSERT INTO MV_ASIENTOS
		(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO], FECHA, DETALLE,TC,SUCURSAL,
		NUMERO,LETRA,[DEBE-HABER],IMPORTE,MONEDA,COTIZACION,PERIODO,CABIMPORTE,TIPO_REG,CONTABILIZADO,
		FECHAHORA_GRABACION,FechaSubdiario, USUARIO_LOGEADO, IDCAJAS)
	VALUES
		(@CuentaMP, 1, @Mes_operativo, @NroAsiento, @pFecha, @pObs, @Tc, SUBSTRING(@IdComprobante,1,4), SUBSTRING(@IdComprobante,5,8), RIGHT(@IdComprobante,1), 'D', @pImporte, '   1', 1, @Periodo, @pImporte,
			@Tc, 0, @pFecha, @FechaHoraGrabacion, @USUARIO, '   1')

	INSERT INTO MV_ASIENTOS
		(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO], FECHA, DETALLE,TC,SUCURSAL,
		NUMERO,LETRA,[DEBE-HABER],IMPORTE,MONEDA,COTIZACION,PERIODO,CABIMPORTE,TIPO_REG,CONTABILIZADO,
		FECHAHORA_GRABACION,FechaSubdiario, USUARIO_LOGEADO, IDCAJAS,NroComprobanteBancario,IdBanco,VENCIMIENTO)
	VALUES
		(@pCliente, 2, @Mes_operativo, @NroAsiento, @pFecha, @pObs, @Tc, SUBSTRING(@IdComprobante,1,4), SUBSTRING(@IdComprobante,5,8), RIGHT(@IdComprobante,1), 'H', @pImporte, '   1', 1, @Periodo, @pImporte,
			@Tc, 0, @pFecha, @FechaHoraGrabacion, @USUARIO, '   1',@pChequeNumero,@pChequeIdBanco,@pChequeVencimiento)

	IF @@ERROR <> 0 OR @@ROWCOUNT <> 1
        BEGIN
            ROLLBACK TRANSACTION
            SELECT 21 as '@pResultado', 'Ocurrio un error al crear la cobranza' as '@pMensaje'
        END
    ELSE
        BEGIN
            COMMIT TRANSACTION
            SELECT 11 as '@pResultado', 'El Pedido se ha dado de alta con exito' as '@pMensaje'
        END
END
-- Fix: FECHAHORA_GRABACION ahora graba fecha+hora (era solo fecha).
--      FechaSubdiario ahora graba solo fecha (igual que FECHA).
--      sp_web_creaLineaAsiento: agrega @pDetalle con default 'Cobranza POS'.
--      sp_web_CreaAsientoIngresoEgreso: agrega UNEGOCIO al INSERT.

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- sp_web_creaLineaAsiento
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[sp_web_creaLineaAsiento]
    @pIdCobranza    int           = null,
    @pImporte       real          = 0,
    @pCuentaMp      varchar(100),
    @pPrimero       int,
    @pChequeNro     varchar(50)   = null,
    @pResultado     smallint      = NULL OUTPUT,
    @pMensaje       varchar(255)  = NULL OUTPUT,
    @pDetalle       nvarchar(100) = 'Cobranza POS'
AS
DECLARE @FechaHoraGrabacion datetime
DECLARE @unegocio nvarchar(4)
DECLARE @USUARIO nvarchar(50)
DECLARE @Tc nvarchar(4)
DECLARE @IdComprobante nvarchar(13)
DECLARE @pCliente nvarchar(13)
DECLARE @pVendedor nvarchar(4)
DECLARE @pFecha datetime
DECLARE @secuencia int
DECLARE @SucCpte nvarchar(4)
DECLARE @NumCpte nvarchar(8)
DECLARE @LetCpte nvarchar(1)
DECLARE @TotalCpte real
DECLARE @idCaja nvarchar(4)

SET NOCOUNT ON

SELECT @Tc = TC,
       @pCliente = Cuenta,
       @pVendedor = IdVendedor,
       @unegocio = UNEGOCIO,
       @IdComprobante = IDCOMPROBANTE,
       @SucCpte = SUCURSAL,
       @NumCpte = NUMERO,
       @LetCpte = LETRA,
       @TotalCpte = ISNULL(IMPORTE, 0),
       @pFecha = FECHA
FROM V_MV_CPTE
WHERE ID = @pIdCobranza

SELECT @idCaja = ISNULL(idCaja, '1'),
       @USUARIO = NOMBRE
FROM TA_USUARIOS
WHERE LTRIM(idvendedor) = LTRIM(@pVendedor)

IF @idCaja IS NULL OR @idCaja = '' SET @idCaja = '1'
IF @USUARIO IS NULL SET @USUARIO = SYSTEM_USER

SET @pVendedor = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)), 4)
SET @idCaja = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@idCaja)), 4)

IF (@unegocio IS NULL) OR (@unegocio = '')
BEGIN
    SELECT @unegocio = VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'UNEGOCIO'
    SET @unegocio = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@unegocio)), 4)
END
IF (@unegocio IS NULL) OR (@unegocio = '') SET @unegocio = '   1'

-- FechaHoraGrabacion = fecha + hora exacta del momento de grabacion
-- pFecha             = solo la fecha (sin hora), igual que FECHA y FechaSubdiario
SET @FechaHoraGrabacion = GETDATE()
SET @pFecha = CONVERT(varchar, @FechaHoraGrabacion, 103)

DECLARE @NroAsiento int
DECLARE @Periodo nvarchar(6)
DECLARE @CuentaMP nvarchar(15)
DECLARE @Mes_operativo int
DECLARE @DEBEHABER nvarchar(1)

SET @CuentaMP = @pCuentaMp
IF @CuentaMP = '' SET @CuentaMP = (SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'CUENTA_CAJA')
IF @CuentaMP = '' SET @CuentaMP = '111010001'

SET @secuencia = 1

IF @pPrimero = 1
BEGIN
    SET @secuencia = 1
    SET @Mes_operativo = MONTH(@pFecha)
    SET @Periodo = (SELECT TOP 1 periodo FROM MV_EJERCICIOS WHERE [FECHA DESDE] <= @pFecha AND [FECHA HASTA] >= @pFecha)
    IF (@Periodo IS NULL) SET @Periodo = '0'
    SET @DEBEHABER = 'H'
    SET @CuentaMP = @pCliente
    SET @pImporte = @TotalCpte

    SET @NroAsiento = (
        SELECT MAX([NUMERO ASIENTO]) + 1
        FROM MV_ASIENTOS
        WHERE MES_OPERATIVO = @Mes_operativo
          AND RTRIM(LTRIM(TIPO_REG)) = 'CB'
          AND RTRIM(LTRIM(PERIODO)) = @Periodo
    )
    IF @NroAsiento IS NULL OR @NroAsiento = '' SET @NroAsiento = 1
END
ELSE
BEGIN
    SET @DEBEHABER = 'D'
    SELECT TOP 1
        @secuencia = SECUENCIA + 1,
        @Mes_operativo = MES_OPERATIVO,
        @Periodo = PERIODO,
        @NroAsiento = [NUMERO ASIENTO]
    FROM MV_ASIENTOS
    WHERE TC = @Tc
      AND SUCURSAL = SUBSTRING(@IdComprobante, 1, 4)
      AND NUMERO = SUBSTRING(@IdComprobante, 5, 8)
      AND LETRA = RIGHT(@IdComprobante, 1)
    ORDER BY SECUENCIA DESC
END
SET NOCOUNT ON

BEGIN TRANSACTION

INSERT INTO MV_ASIENTOS
(CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE, TC, SUCURSAL,
 NUMERO, LETRA, [DEBE-HABER], IMPORTE, MONEDA, COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO,
 FECHAHORA_GRABACION, FechaSubdiario, USUARIO_LOGEADO, IDCAJAS, NroComprobanteBancario, UNEGOCIO)
VALUES
(@CuentaMP, @secuencia, @Mes_operativo, @NroAsiento, @pFecha, @pDetalle, @Tc, SUBSTRING(@IdComprobante, 1, 4),
 SUBSTRING(@IdComprobante, 5, 8), RIGHT(@IdComprobante, 1), @DEBEHABER, @pImporte, '   1', 1, @Periodo, @TotalCpte,
 'CB', 0, @FechaHoraGrabacion, @pFecha, @USUARIO, @idCaja, @pChequeNro, @unegocio)

IF @@ERROR <> 0 OR @@ROWCOUNT <> 1
BEGIN
    ROLLBACK TRANSACTION
    SET @pResultado = 21
    SET @pMensaje = 'No pudo darse de alta el registro'
    RETURN
END

COMMIT TRANSACTION
SET @pResultado = 11
SET @pMensaje = 'El registro se ha dado de alta con exito'
GO

-- ============================================================
-- sp_web_CreaAsientoIngresoEgreso
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[sp_web_CreaAsientoIngresoEgreso]
    @pTipo              varchar(1)   = null,
    @pImporte           real         = 0,
    @pDetalle           varchar(100) = null,
    @pCuentaImputacion  varchar(15)  = null,
    @pResultado         smallint     = NULL OUTPUT,
    @pMensaje           varchar(255) = NULL OUTPUT
AS
DECLARE @FechaHoraGrabacion DATETIME
DECLARE @unegocio NVARCHAR(4)
DECLARE @USUARIO AS NVARCHAR(50)
DECLARE @Tc AS NVARCHAR(4)
DECLARE @pFecha AS DATETIME
DECLARE @NroAsiento INT
DECLARE @Periodo NVARCHAR(6)
DECLARE @CuentaMP NVARCHAR(15)
DECLARE @Mes_operativo INT
DECLARE @NUMERO NVARCHAR(8)
DECLARE @nvoNumero NVARCHAR(8)
DECLARE @SUCURSAL NVARCHAR(4)
DECLARE @LETRA NVARCHAR(1)

SET NOCOUNT ON

SET @USUARIO = SYSTEM_USER
SET @Tc = 'CJA'
SET @SUCURSAL = '0001'
SET @LETRA = 'X'

-- UNEGOCIO: desde config o default '   1'
IF (@unegocio IS NULL) OR (@unegocio = '')
BEGIN
    SELECT @unegocio = VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'UNEGOCIO'
    SET @unegocio = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@unegocio)), 4)
END
IF (@unegocio IS NULL) OR (@unegocio = '') SET @unegocio = '   1'

-- FechaHoraGrabacion = fecha + hora; pFecha = solo fecha (sin hora)
SET @FechaHoraGrabacion = GETDATE()
SET @pFecha = CONVERT(VARCHAR, @FechaHoraGrabacion, 103)

-- Cuenta de efectivo por default
SET @CuentaMP = (SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'CUENTA_CAJA')
IF @CuentaMP = '' OR @CuentaMP IS NULL SET @CuentaMP = '111010001'

SELECT @NUMERO = MAX(NUMERO) + 1 FROM MV_ASIENTOS WHERE TC = 'CJA' AND SUCURSAL = '0001' AND LETRA = 'X'
IF @NUMERO = '0' OR @NUMERO IS NULL SET @NUMERO = '00000001'

SET @nvoNumero = dbo.FN_FMT_LEERCODIGO(CAST(@NUMERO AS NVARCHAR(8)), 8)
SET @nvoNumero = REPLACE(@nvoNumero, ' ', '0')
SET @NUMERO = @nvoNumero
SET @Mes_operativo = MONTH(@pFecha)

SET @Periodo = (SELECT TOP 1 periodo FROM MV_EJERCICIOS WHERE [FECHA DESDE] <= @pFecha AND [FECHA HASTA] >= @pFecha)
IF (@Periodo IS NULL) SET @Periodo = '0'

SET @NroAsiento = (
    SELECT MAX([NUMERO ASIENTO]) + 1
    FROM MV_ASIENTOS
    WHERE MES_OPERATIVO = @Mes_operativo
      AND RTRIM(LTRIM(TIPO_REG)) = 'CB'
      AND RTRIM(LTRIM(PERIODO)) = @Periodo
)
IF @NroAsiento IS NULL OR @NroAsiento = '' SET @NroAsiento = 1

IF @pDetalle IS NULL OR @pDetalle = ''
    SET @pDetalle = 'Movimiento de caja'

SET NOCOUNT ON

BEGIN TRANSACTION;
    IF @pTipo = 'I'
        BEGIN
            INSERT INTO MV_ASIENTOS
            (CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE, TC, SUCURSAL,
             NUMERO, LETRA, [DEBE-HABER], IMPORTE, MONEDA, COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO,
             FECHAHORA_GRABACION, FechaSubdiario, USUARIO_LOGEADO, IDCAJAS, UNEGOCIO)
            VALUES
            (@CuentaMP, 1, @Mes_operativo, @NroAsiento, @pFecha, @pDetalle, @Tc, @SUCURSAL,
             @NUMERO, @LETRA, 'D', @pImporte, '   1', 1, @Periodo, @pImporte,
             'CJA', 0, @FechaHoraGrabacion, @pFecha, @USUARIO, '   1', @unegocio)

            INSERT INTO MV_ASIENTOS
            (CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE, TC, SUCURSAL,
             NUMERO, LETRA, [DEBE-HABER], IMPORTE, MONEDA, COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO,
             FECHAHORA_GRABACION, FechaSubdiario, USUARIO_LOGEADO, IDCAJAS, UNEGOCIO)
            VALUES
            (@pCuentaImputacion, 2, @Mes_operativo, @NroAsiento, @pFecha, @pDetalle, @Tc, @SUCURSAL,
             @NUMERO, @LETRA, 'H', @pImporte, '   1', 1, @Periodo, @pImporte,
             'CJA', 0, @FechaHoraGrabacion, @pFecha, @USUARIO, '   1', @unegocio)
        END
    ELSE
        BEGIN
            INSERT INTO MV_ASIENTOS
            (CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE, TC, SUCURSAL,
             NUMERO, LETRA, [DEBE-HABER], IMPORTE, MONEDA, COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO,
             FECHAHORA_GRABACION, FechaSubdiario, USUARIO_LOGEADO, IDCAJAS, UNEGOCIO)
            VALUES
            (@pCuentaImputacion, 1, @Mes_operativo, @NroAsiento, @pFecha, @pDetalle, @Tc, @SUCURSAL,
             @NUMERO, @LETRA, 'D', @pImporte, '   1', 1, @Periodo, @pImporte,
             'CJA', 0, @FechaHoraGrabacion, @pFecha, @USUARIO, '   1', @unegocio)

            INSERT INTO MV_ASIENTOS
            (CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE, TC, SUCURSAL,
             NUMERO, LETRA, [DEBE-HABER], IMPORTE, MONEDA, COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO,
             FECHAHORA_GRABACION, FechaSubdiario, USUARIO_LOGEADO, IDCAJAS, UNEGOCIO)
            VALUES
            (@CuentaMP, 2, @Mes_operativo, @NroAsiento, @pFecha, @pDetalle, @Tc, @SUCURSAL,
             @NUMERO, @LETRA, 'H', @pImporte, '   1', 1, @Periodo, @pImporte,
             'CJA', 0, @FechaHoraGrabacion, @pFecha, @USUARIO, '   1', @unegocio)
        END

    IF @@ERROR <> 0 OR @@ROWCOUNT <> 1
        BEGIN
            ROLLBACK TRANSACTION
            SET @pResultado = 21
            SET @pMensaje = ERROR_MESSAGE()
            RETURN
        END
    ELSE
        BEGIN
            COMMIT TRANSACTION
            SET @pResultado = 11
            SET @pMensaje = 'El registro se ha dado de alta con exito'
        END
GO

-- Fix: sp_web_CreaAsientoIngresoEgreso usaba SYSTEM_USER (usuario SQL "HIERROSUR")
-- en lugar del usuario del sistema (TA_USUARIOS) pasado desde la app.
-- Se agrega @pUsuario nvarchar(50) = NULL y se usa en USUARIO_LOGEADO.

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_web_CreaAsientoIngresoEgreso]
    @pTipo              varchar(1)   = null,
    @pImporte           real         = 0,
    @pDetalle           varchar(100) = null,
    @pCuentaImputacion  varchar(15)  = null,
    @pUsuario           nvarchar(50) = NULL,
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

-- Usa el usuario del sistema pasado por la app; si no viene, cae al usuario SQL
SET @USUARIO = ISNULL(NULLIF(LTRIM(RTRIM(@pUsuario)), ''), SYSTEM_USER)
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

-- NUMERO ASIENTO: secuencia propia de CJA para no colisionar con CBCT (TIPO_REG='CB')
SET @NroAsiento = (
    SELECT MAX([NUMERO ASIENTO]) + 1
    FROM MV_ASIENTOS
    WHERE MES_OPERATIVO = @Mes_operativo
      AND RTRIM(LTRIM(TIPO_REG)) = 'CJA'
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

/*
  Corrige bases donde sp_web_CreaAsientoFactura quedó con la versión anterior
  que leía la cuenta de ventas desde TA_CUENTASIVA.V_DFI_CTAVTA.

  La cuenta de ventas pertenece al asiento de la factura. El asiento de la
  cobranza continúa usando solamente la cuenta del cliente y la del medio de
  pago mediante sp_web_creaLineaAsiento.
*/
IF OBJECT_ID(N'dbo.sp_web_CreaAsientoFactura', N'P') IS NULL
    RETURN;

DECLARE @Definicion nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_web_CreaAsientoFactura', N'P'));
IF @Definicion IS NULL OR @Definicion LIKE N'%CUENTAVENTASDEFAULTINS%'
    RETURN;

DECLARE @ExpresionAnterior nvarchar(4000) =
    N'@CuentaVaria = NULLIF(LTRIM(RTRIM(V_DFI_CTAVTA)), ''''),';
DECLARE @ExpresionNueva nvarchar(4000) =
    N'@CuentaVaria = (SELECT TOP (1) NULLIF(LTRIM(RTRIM(VALOR)), '''')
                         FROM dbo.TA_CONFIGURACION
                         WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N''CUENTAVENTASDEFAULTINS''),';

IF CHARINDEX(@ExpresionAnterior, @Definicion) = 0
    THROW 51000, 'No se encontró V_DFI_CTAVTA en sp_web_CreaAsientoFactura para actualizarlo.', 1;

SET @Definicion = REPLACE(@Definicion, @ExpresionAnterior, @ExpresionNueva);
SET @Definicion = REPLACE(@Definicion, N'CREATE PROCEDURE', N'ALTER PROCEDURE');
SET @Definicion = REPLACE(@Definicion, N'CREATE   PROCEDURE', N'ALTER PROCEDURE');
EXEC sys.sp_executesql @Definicion;

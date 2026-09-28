/*
  Actualiza el asiento de facturas del POS para tomar la cuenta de ventas
  desde la configuración CUENTAVENTASDEFAULTINS.

  Se conserva la lectura de las cuentas de IVA desde TA_CUENTASIVA. El cambio
  evita que la cuenta de ventas dependa de V_DFI_CTAVTA.
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

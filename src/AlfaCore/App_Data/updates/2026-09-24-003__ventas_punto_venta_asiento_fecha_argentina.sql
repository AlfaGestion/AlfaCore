/*
  Evita conversiones implícitas de fechas dd/MM/yyyy en el asiento de la
  factura del POS. Algunas bases exponen FECHA y las fechas del ejercicio como
  nvarchar; el formato 103 es el formato argentino dd/MM/yyyy.
*/
IF OBJECT_ID(N'dbo.sp_web_CreaAsientoFactura', N'P') IS NULL
    RETURN;

DECLARE @Definicion nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_web_CreaAsientoFactura', N'P'));
IF @Definicion IS NULL
    RETURN;

IF @Definicion LIKE N'%TRY_CONVERT(datetime, FECHA, 103)%'
    RETURN;

DECLARE @FechaAnterior nvarchar(4000) = N'@Fecha = FECHA,';
DECLARE @FechaNueva nvarchar(4000) = N'@Fecha = TRY_CONVERT(datetime, FECHA, 103),';

IF CHARINDEX(@FechaAnterior, @Definicion) = 0
    THROW 51000, 'No se encontró la asignación de FECHA en sp_web_CreaAsientoFactura.', 1;

SET @Definicion = REPLACE(@Definicion, @FechaAnterior, @FechaNueva);
SET @Definicion = REPLACE(@Definicion,
    N'WHERE [FECHA DESDE] <= @Fecha AND [FECHA HASTA] >= @Fecha;',
    N'WHERE TRY_CONVERT(datetime, [FECHA DESDE], 103) <= @Fecha
      AND TRY_CONVERT(datetime, [FECHA HASTA], 103) >= @Fecha;');
SET @Definicion = REPLACE(@Definicion, N'CREATE PROCEDURE', N'ALTER PROCEDURE');
SET @Definicion = REPLACE(@Definicion, N'CREATE   PROCEDURE', N'ALTER PROCEDURE');
EXEC sys.sp_executesql @Definicion;

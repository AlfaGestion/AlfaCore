/* MV_ASIENTOS.FECHA debe coincidir con MA_CALENDARIO.FECHA a medianoche. */
SET NOCOUNT ON;

DECLARE @definicion nvarchar(max);
SET @definicion = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_web_creaLineaAsiento', N'P'));

IF @definicion IS NOT NULL
   AND @definicion LIKE N'%SET @pFecha = @FechaHoraGrabacion%'
BEGIN
    SET @definicion = REPLACE(@definicion, N'CREATE PROCEDURE', N'ALTER PROCEDURE');
    SET @definicion = REPLACE(@definicion, N'CREATE   PROCEDURE', N'ALTER PROCEDURE');
    SET @definicion = REPLACE(
        @definicion,
        N'SET @pFecha = @FechaHoraGrabacion',
        N'SET @pFecha = CONVERT(datetime, CONVERT(varchar(8), @FechaHoraGrabacion, 112), 112)');
    EXEC sys.sp_executesql @definicion;
END;

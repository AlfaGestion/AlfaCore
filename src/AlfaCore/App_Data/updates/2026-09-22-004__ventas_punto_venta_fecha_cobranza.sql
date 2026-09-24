/*
    Corrige la conversión de fechas de la cobranza del POS.

    Los procedimientos antiguos convertían GETDATE() a varchar con formato
    dd/mm/yyyy y luego lo asignaban nuevamente a datetime. Cuando la sesión
    SQL estaba en DATEFORMAT YMD, una fecha como 22/09/2026 producía el error
    "La conversión del tipo de datos varchar en datetime produjo un valor fuera
    de intervalo".

    Se conserva la firma y el cuerpo instalado, reemplazando únicamente esas
    conversiones por valores datetime directos. Es idempotente y sirve también
    para bases que ya tienen los procedimientos creados.
*/

SET NOCOUNT ON;

DECLARE @nombre nvarchar(256);
DECLARE @definicion nvarchar(max);

DECLARE @procedimientos TABLE (Nombre nvarchar(256) NOT NULL);
INSERT INTO @procedimientos (Nombre)
VALUES
    (N'dbo.sp_web_CreaCobPorFactura'),
    (N'dbo.sp_web_creaLineaAsiento');

DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT Nombre FROM @procedimientos;

OPEN c;
FETCH NEXT FROM c INTO @nombre;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @definicion = OBJECT_DEFINITION(OBJECT_ID(@nombre, N'P'));

    IF @definicion IS NOT NULL
       AND (
            @definicion LIKE N'%CONVERT%GETDATE()%103%'
            OR @definicion LIKE N'%CONVERT%FechaHoraGrabacion%103%'
       )
    BEGIN
        SET @definicion = REPLACE(@definicion, N'CREATE PROCEDURE', N'ALTER PROCEDURE');
        SET @definicion = REPLACE(@definicion, N'CREATE   PROCEDURE', N'ALTER PROCEDURE');
        SET @definicion = REPLACE(
            @definicion,
            N'SET @pFecha = CONVERT(varchar, GETDATE(), 103)',
            N'SET @pFecha = GETDATE()');
        SET @definicion = REPLACE(
            @definicion,
            N'SET @pFecha = CONVERT(VARCHAR, GETDATE(), 103)',
            N'SET @pFecha = GETDATE()');
        SET @definicion = REPLACE(
            @definicion,
            N'SET @pFecha = CONVERT(varchar, @FechaHoraGrabacion, 103)',
            N'SET @pFecha = CONVERT(datetime, CONVERT(varchar(8), @FechaHoraGrabacion, 112), 112)');
        SET @definicion = REPLACE(
            @definicion,
            N'SET @pFecha = CONVERT(VARCHAR, @FechaHoraGrabacion, 103)',
            N'SET @pFecha = CONVERT(datetime, CONVERT(varchar(8), @FechaHoraGrabacion, 112), 112)');

        EXEC sys.sp_executesql @definicion;
    END;

    FETCH NEXT FROM c INTO @nombre;
END;

CLOSE c;
DEALLOCATE c;

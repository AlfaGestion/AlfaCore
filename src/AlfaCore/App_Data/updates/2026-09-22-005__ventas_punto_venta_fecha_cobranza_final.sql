/*
    Corrección definitiva de fechas en los procedimientos de cobranza POS.
    Se crea un update nuevo porque los anteriores pueden estar marcados como
    aplicados en bases existentes y además otros updates vuelven a crear
    sp_web_creaLineaAsiento.
*/

SET NOCOUNT ON;

DECLARE @nombre nvarchar(256);
DECLARE @definicion nvarchar(max);

DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT Nombre
    FROM (VALUES
        (N'dbo.sp_web_CreaCobPorFactura'),
        (N'dbo.sp_web_creaLineaAsiento')
    ) AS Procedimientos(Nombre);

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
        SET @definicion = REPLACE(@definicion, N'SET @pFecha = CONVERT(varchar, GETDATE(), 103)', N'SET @pFecha = GETDATE()');
        SET @definicion = REPLACE(@definicion, N'SET @pFecha = CONVERT(VARCHAR, GETDATE(), 103)', N'SET @pFecha = GETDATE()');
        SET @definicion = REPLACE(@definicion, N'SET @pFecha = CONVERT(VARCHAR,GETDATE(),103)', N'SET @pFecha = GETDATE()');
        SET @definicion = REPLACE(@definicion, N'SET @pFecha = CONVERT(varchar, @FechaHoraGrabacion, 103)', N'SET @pFecha = CONVERT(datetime, CONVERT(varchar(8), @FechaHoraGrabacion, 112), 112)');
        SET @definicion = REPLACE(@definicion, N'SET @pFecha = CONVERT(VARCHAR, @FechaHoraGrabacion, 103)', N'SET @pFecha = CONVERT(datetime, CONVERT(varchar(8), @FechaHoraGrabacion, 112), 112)');
        SET @definicion = REPLACE(@definicion, N'SET @pFecha = CONVERT(VARCHAR,@FechaHoraGrabacion,103)', N'SET @pFecha = CONVERT(datetime, CONVERT(varchar(8), @FechaHoraGrabacion, 112), 112)');

        EXEC sys.sp_executesql @definicion;
    END;

    FETCH NEXT FROM c INTO @nombre;
END;

CLOSE c;
DEALLOCATE c;

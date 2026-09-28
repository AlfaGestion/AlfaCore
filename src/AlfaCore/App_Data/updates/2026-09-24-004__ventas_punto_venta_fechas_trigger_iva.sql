/*
  Corrige el trigger histórico de IVA y el asiento de factura para bases que
  exponen alguna fecha como nvarchar con formato dd/MM/yyyy.
*/

IF OBJECT_ID(N'dbo.FMT_V_MV_CPTE_ID', N'TR') IS NOT NULL
BEGIN
    DECLARE @Trigger nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.FMT_V_MV_CPTE_ID', N'TR'));

    IF @Trigger IS NOT NULL
    BEGIN
        SET @Trigger = REPLACE(@Trigger, N'WHERE FECHA >= @pFecha', N'WHERE TRY_CONVERT(datetime, FECHA, 103) >= @pFecha');
        SET @Trigger = REPLACE(@Trigger, N'AND FECHA < DATEADD(day, 1, @pFecha)', N'AND TRY_CONVERT(datetime, FECHA, 103) < DATEADD(day, 1, @pFecha)');
        SET @Trigger = REPLACE(@Trigger, N'CREATE TRIGGER', N'ALTER TRIGGER');
        SET @Trigger = REPLACE(@Trigger, N'CREATE  TRIGGER', N'ALTER TRIGGER');
        SET @Trigger = REPLACE(@Trigger, N'ALTER TRIGGER', N'ALTER TRIGGER');
        EXEC sys.sp_executesql @Trigger;
    END;
END;

IF OBJECT_ID(N'dbo.sp_web_CreaAsientoFactura', N'P') IS NOT NULL
BEGIN
    DECLARE @Procedimiento nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_web_CreaAsientoFactura', N'P'));

    IF @Procedimiento IS NOT NULL
    BEGIN
        SET @Procedimiento = REPLACE(@Procedimiento, N'@Fecha = FECHA,', N'@Fecha = TRY_CONVERT(datetime, FECHA, 103),');
        SET @Procedimiento = REPLACE(@Procedimiento, N'CONVERT(datetime, FECHA)', N'TRY_CONVERT(datetime, FECHA, 103)');
        SET @Procedimiento = REPLACE(@Procedimiento, N'[FECHA DESDE] <= @Fecha', N'TRY_CONVERT(datetime, [FECHA DESDE], 103) <= @Fecha');
        SET @Procedimiento = REPLACE(@Procedimiento, N'[FECHA HASTA] >= @Fecha', N'TRY_CONVERT(datetime, [FECHA HASTA], 103) >= @Fecha');
        SET @Procedimiento = REPLACE(@Procedimiento, N'CREATE PROCEDURE', N'ALTER PROCEDURE');
        SET @Procedimiento = REPLACE(@Procedimiento, N'CREATE   PROCEDURE', N'ALTER PROCEDURE');
        EXEC sys.sp_executesql @Procedimiento;
    END;
END;

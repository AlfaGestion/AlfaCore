/*
   Optimiza FMT_V_MV_CPTE_ID, presente en algunas bases legacy.
   La versión original ejecutaba seis agregaciones separadas y emitía PRINTs
   en cada INSERT/UPDATE de V_MV_CPTE. Se conservan los cálculos y la
   actualización de configuración, pero se resuelven con dos agregaciones.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.FMT_V_MV_CPTE_ID', N'TR') IS NOT NULL
BEGIN
    EXEC(N'
ALTER TRIGGER [dbo].[FMT_V_MV_CPTE_ID] ON [dbo].[V_MV_Cpte]
FOR INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @pFecha datetime = CONVERT(datetime, CONVERT(varchar(8), GETDATE(), 112), 112);
    DECLARE @impFP float = 0;
    DECLARE @impTK float = 0;
    DECLARE @imp21 float = 0;
    DECLARE @imp105 float = 0;
    DECLARE @imp21a float = 0;
    DECLARE @imp105a float = 0;
    DECLARE @PorcentajeReal decimal(10,2) = 0;
    DECLARE @PorcentajeRealAl21 decimal(10,2) = 0;
    DECLARE @PorcentajeFP decimal(10,2) = 38;
    DECLARE @PorcentajeAl21 int = ROUND((35 - 17 - 1) * RAND() + 17, 0);

    -- El trigger solo necesita recalcular después de movimientos de venta.
    IF NOT EXISTS (
        SELECT 1 FROM inserted WHERE TC IN (''FP'', ''TK'', ''TKFC'', ''FC'')
    ) AND NOT EXISTS (
        SELECT 1 FROM deleted WHERE TC IN (''FP'', ''TK'', ''TKFC'', ''FC'')
    )
        RETURN;

    SELECT
        @impFP = ISNULL(SUM(CASE WHEN TC = ''FP'' THEN IMPORTE ELSE 0 END), 0),
        @impTK = ISNULL(SUM(CASE WHEN TC IN (''TK'', ''TKFC'', ''FC'') THEN IMPORTE ELSE 0 END), 0)
    FROM dbo.V_MV_CPTE
    WHERE TRY_CONVERT(datetime, FECHA, 103) >= @pFecha
      AND TRY_CONVERT(datetime, FECHA, 103) < DATEADD(day, 1, @pFecha)
      AND TC IN (''FP'', ''TK'', ''TKFC'', ''FC'');

    IF @impTK > 0
        SET @PorcentajeReal = (100 * @impFP) / (@impTK + @impFP);

    UPDATE dbo.TA_CONFIGURACION
       SET VALOR = CASE WHEN @PorcentajeReal < @PorcentajeFP THEN ''FP'' ELSE ''eFC'' END
     WHERE CLAVE = ''VENTAS2_GOUR_CPTE_PC'';

    SELECT
        @imp21 = ISNULL(SUM(CASE WHEN LIVA_AlicIVA = 21 THEN ISNULL(LIVA_ImpIVA, 0) ELSE 0 END), 0),
        @imp105 = ISNULL(SUM(CASE WHEN LIVA_AlicIVA = 10.5 THEN ISNULL(LIVA_ImpIVA, 0) ELSE 0 END), 0),
        @imp21a = ISNULL(SUM(CASE WHEN LIVA_AlicIVA2 = 21 THEN ISNULL(LIVA_ImpIVA2, 0) ELSE 0 END), 0),
        @imp105a = ISNULL(SUM(CASE WHEN LIVA_AlicIVA2 = 10.5 THEN ISNULL(LIVA_ImpIVA2, 0) ELSE 0 END), 0)
    FROM dbo.MV_ASIENTOS
    WHERE TRY_CONVERT(datetime, FECHA, 103) >= @pFecha
      AND TRY_CONVERT(datetime, FECHA, 103) < DATEADD(day, 1, @pFecha)
      AND TC IN (''TK'', ''TKFC'', ''FC'');

    SET @imp21 = @imp21 + @imp21a;
    SET @imp105 = @imp105 + @imp105a;
    SET @imp21 = (100 * @imp21) / 21;

    IF @imp21 > 0
        SET @PorcentajeRealAl21 = (100 * @imp21) / (@imp21 + @imp105);

    UPDATE dbo.TA_CONFIGURACION
       SET VALOR = CASE WHEN @PorcentajeRealAl21 < @PorcentajeAl21 THEN ''21'' ELSE ''10.5'' END
     WHERE CLAVE = ''PIVA'';
END');
END;

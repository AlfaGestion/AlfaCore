SET NOCOUNT ON;

-- POS Salón: formato visual configurable para cada mesa.
-- La ausencia de la columna conserva el comportamiento cuadrado mediante ISNULL.
IF OBJECT_ID(N'dbo.POS_MESA', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF COL_LENGTH(N'dbo.POS_MESA', N'FORMATO') IS NULL
BEGIN
    ALTER TABLE dbo.POS_MESA ADD FORMATO nvarchar(12) NULL;
END;
GO

UPDATE dbo.POS_MESA
SET FORMATO = N'CUADRADA'
WHERE FORMATO IS NULL OR LTRIM(RTRIM(FORMATO)) = N'';
GO

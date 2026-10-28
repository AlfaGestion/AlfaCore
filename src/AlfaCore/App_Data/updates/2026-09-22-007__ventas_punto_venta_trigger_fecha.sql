/*
   Corrige bases que tienen el trigger legacy FMT_V_MV_CPTE_ID.

   Ese trigger asignaba CONVERT(VARCHAR(10), GETDATE(), 103) a una variable
   DATETIME. La cadena dd/mm/yyyy depende de DATEFORMAT y falla, por ejemplo,
   con el día 22 cuando la sesión está en YMD.
*/
SET NOCOUNT ON;

DECLARE @definicion nvarchar(max);
SET @definicion = OBJECT_DEFINITION(OBJECT_ID(N'dbo.FMT_V_MV_CPTE_ID', N'TR'));

-- Opt-out por base: ACTUALIZACIONES-PARCHES-TRIGGERS-LEGACY = N (grupo SISTEMA) deja intactos los
-- triggers del sistema legacy. Sin la clave, o con S, se aplica el parche.
IF @definicion IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'ACTUALIZACIONES-PARCHES-TRIGGERS-LEGACY' AND UPPER(LTRIM(RTRIM(VALOR))) = N'N')
   AND @definicion LIKE N'%CONVERT%GETDATE()%103%'
BEGIN
    SET @definicion = REPLACE(@definicion, N'CREATE TRIGGER', N'ALTER TRIGGER');
    SET @definicion = REPLACE(@definicion, N'CREATE   TRIGGER', N'ALTER TRIGGER');
    SET @definicion = REPLACE(
        @definicion,
        N'CONVERT(VARCHAR(10), GETDATE(), 103)',
        N'CONVERT(datetime, CONVERT(varchar(8), GETDATE(), 112), 112)');
    SET @definicion = REPLACE(
        @definicion,
        N'CONVERT(varchar(10), GETDATE(), 103)',
        N'CONVERT(datetime, CONVERT(varchar(8), GETDATE(), 112), 112)');

    EXEC sys.sp_executesql @definicion;
END;

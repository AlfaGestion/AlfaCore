/*
  Corrige el trigger histórico de asientos que hacía CAST(VALOR AS datetime)
  sobre fechas argentinas almacenadas como texto (dd/MM/yyyy). En conexiones
  con DATEFORMAT mdy, valores como 31/12/2024 provocaban "nvarchar en
  datetime fuera de intervalo" al crear el asiento de una factura.
*/
IF OBJECT_ID(N'dbo.MV_Asientos_ValidaFechas', N'TR') IS NULL
    RETURN;

DECLARE @Definicion nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.MV_Asientos_ValidaFechas', N'TR'));
IF @Definicion IS NULL
    RETURN;

SET @Definicion = REPLACE(
    @Definicion,
    N'CAST(VALOR AS DATETIME)',
    N'TRY_CONVERT(datetime, NULLIF(LTRIM(RTRIM(VALOR)), ''''), 103)');
SET @Definicion = REPLACE(
    @Definicion,
    N'CAST(VALOR AS datetime)',
    N'TRY_CONVERT(datetime, NULLIF(LTRIM(RTRIM(VALOR)), ''''), 103)');
SET @Definicion = REPLACE(@Definicion, N'CREATE TRIGGER', N'ALTER TRIGGER');
SET @Definicion = REPLACE(@Definicion, N'CREATE  TRIGGER', N'ALTER TRIGGER');

-- Bases en compatibilidad SQL 2008 (nivel 100) no reconocen TRY_CONVERT: CONVERT con estilo 103
-- interpreta igual dd/MM/yyyy sin depender del DATEFORMAT de la conexión (2026-10-06).
IF (SELECT compatibility_level FROM sys.databases WHERE database_id = DB_ID()) < 110
    SET @Definicion = REPLACE(@Definicion, N'TRY_CONVERT(', N'CONVERT(');

EXEC sys.sp_executesql @Definicion;

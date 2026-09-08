/*
   Plantillas WhatsApp por WABA.
   Aditiva e idempotente: no modifica ni elimina filas existentes.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS', N'U') IS NULL
    RETURN;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.CONV_PLANTILLAS', N'WabaId') IS NULL
    ALTER TABLE dbo.CONV_PLANTILLAS ADD WabaId varchar(40) NULL;

IF EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.CONV_PLANTILLAS')
      AND name = N'UQ_CONV_PLANTILLAS_NombreMeta_Idioma')
    ALTER TABLE dbo.CONV_PLANTILLAS DROP CONSTRAINT UQ_CONV_PLANTILLAS_NombreMeta_Idioma;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.CONV_PLANTILLAS')
      AND name = N'UX_CONV_PLANTILLAS_LegacyNombreMeta_Idioma')
    EXEC(N'
        CREATE UNIQUE INDEX UX_CONV_PLANTILLAS_LegacyNombreMeta_Idioma
            ON dbo.CONV_PLANTILLAS (NombreMeta, Idioma)
            WHERE WabaId IS NULL;');

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.CONV_PLANTILLAS')
      AND name = N'UX_CONV_PLANTILLAS_WabaId_NombreMeta_Idioma')
    EXEC(N'
        CREATE UNIQUE INDEX UX_CONV_PLANTILLAS_WabaId_NombreMeta_Idioma
            ON dbo.CONV_PLANTILLAS (WabaId, NombreMeta, Idioma)
            WHERE WabaId IS NOT NULL;');

COMMIT TRANSACTION;

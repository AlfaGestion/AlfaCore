-- Anulación lógica: conserva testigo, operaciones e historial; permite reimportar.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
IF OBJECT_ID(N'dbo.TJ_Importacion',N'U') IS NULL OR OBJECT_ID(N'dbo.TJ_Detalle',N'U') IS NULL RETURN;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.TJ_Importacion','Anulada') IS NULL
    EXEC(N'ALTER TABLE dbo.TJ_Importacion ADD Anulada bit NOT NULL CONSTRAINT DF_TJ_Importacion_Anulada DEFAULT(0) WITH VALUES');
IF COL_LENGTH('dbo.TJ_Importacion','UsuarioAnulacion') IS NULL
    EXEC(N'ALTER TABLE dbo.TJ_Importacion ADD UsuarioAnulacion nvarchar(50) NULL');
IF COL_LENGTH('dbo.TJ_Importacion','FechaAnulacion') IS NULL
    EXEC(N'ALTER TABLE dbo.TJ_Importacion ADD FechaAnulacion datetime2 NULL');
IF COL_LENGTH('dbo.TJ_Importacion','MotivoAnulacion') IS NULL
    EXEC(N'ALTER TABLE dbo.TJ_Importacion ADD MotivoAnulacion nvarchar(200) NULL');
IF COL_LENGTH('dbo.TJ_Detalle','ImportacionActiva') IS NULL
    EXEC(N'ALTER TABLE dbo.TJ_Detalle ADD ImportacionActiva bit NOT NULL CONSTRAINT DF_TJ_Detalle_ImportacionActiva DEFAULT(1) WITH VALUES');
-- Crear primero los reemplazos; toda la sustitución es transaccional.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TJ_Importacion') AND name=N'UX_TJ_Importacion_Activa')
    EXEC(N'CREATE UNIQUE INDEX UX_TJ_Importacion_Activa ON dbo.TJ_Importacion(Hash,Cuenta) WHERE Anulada=0');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TJ_Detalle') AND name=N'UX_TJ_Huella_ImportacionActiva')
    EXEC(N'CREATE UNIQUE INDEX UX_TJ_Huella_ImportacionActiva ON dbo.TJ_Detalle(Huella) WHERE ImportacionActiva=1');
IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.TJ_Importacion') AND name=N'UQ_TJ_Importacion')
    ALTER TABLE dbo.TJ_Importacion DROP CONSTRAINT UQ_TJ_Importacion;
IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.TJ_Detalle') AND name=N'UQ_TJ_Detalle')
    ALTER TABLE dbo.TJ_Detalle DROP CONSTRAINT UQ_TJ_Detalle;
COMMIT TRANSACTION;

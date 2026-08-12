/*
    CRM - Fase 2: motivos de pérdida.
    Catálogo configurable de motivos + columna en la oportunidad para registrar por qué se perdió.
    Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CRM_MOTIVOS_PERDIDA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_MOTIVOS_PERDIDA
    (
        IdMotivo int IDENTITY(1,1) NOT NULL,
        Nombre nvarchar(100) NOT NULL,
        Orden int NOT NULL CONSTRAINT DF_CRM_MOTIVOS_Orden DEFAULT (0),
        Activo bit NOT NULL CONSTRAINT DF_CRM_MOTIVOS_Activo DEFAULT (1),
        CONSTRAINT PK_CRM_MOTIVOS_PERDIDA PRIMARY KEY CLUSTERED (IdMotivo),
        CONSTRAINT UQ_CRM_MOTIVOS_PERDIDA_Nombre UNIQUE NONCLUSTERED (Nombre)
    );
END;
GO

IF COL_LENGTH('dbo.CRM_OPORTUNIDADES', 'IdMotivoPerdida') IS NULL
BEGIN
    ALTER TABLE dbo.CRM_OPORTUNIDADES ADD IdMotivoPerdida int NULL;
END;
GO

IF OBJECT_ID(N'dbo.CRM_MOTIVOS_PERDIDA', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_CRM_OPORTUNIDADES_MOTIVO', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CRM_OPORTUNIDADES WITH NOCHECK
    ADD CONSTRAINT FK_CRM_OPORTUNIDADES_MOTIVO FOREIGN KEY (IdMotivoPerdida)
        REFERENCES dbo.CRM_MOTIVOS_PERDIDA (IdMotivo);
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.CRM_MOTIVOS_PERDIDA WHERE Nombre = N'Precio')
    INSERT INTO dbo.CRM_MOTIVOS_PERDIDA (Nombre, Orden) VALUES (N'Precio', 10);
IF NOT EXISTS (SELECT 1 FROM dbo.CRM_MOTIVOS_PERDIDA WHERE Nombre = N'Eligió a la competencia')
    INSERT INTO dbo.CRM_MOTIVOS_PERDIDA (Nombre, Orden) VALUES (N'Eligió a la competencia', 20);
IF NOT EXISTS (SELECT 1 FROM dbo.CRM_MOTIVOS_PERDIDA WHERE Nombre = N'Sin respuesta del cliente')
    INSERT INTO dbo.CRM_MOTIVOS_PERDIDA (Nombre, Orden) VALUES (N'Sin respuesta del cliente', 30);
IF NOT EXISTS (SELECT 1 FROM dbo.CRM_MOTIVOS_PERDIDA WHERE Nombre = N'Fuera de tiempo')
    INSERT INTO dbo.CRM_MOTIVOS_PERDIDA (Nombre, Orden) VALUES (N'Fuera de tiempo', 40);
IF NOT EXISTS (SELECT 1 FROM dbo.CRM_MOTIVOS_PERDIDA WHERE Nombre = N'No era el cliente adecuado')
    INSERT INTO dbo.CRM_MOTIVOS_PERDIDA (Nombre, Orden) VALUES (N'No era el cliente adecuado', 50);
IF NOT EXISTS (SELECT 1 FROM dbo.CRM_MOTIVOS_PERDIDA WHERE Nombre = N'Otro')
    INSERT INTO dbo.CRM_MOTIVOS_PERDIDA (Nombre, Orden) VALUES (N'Otro', 90);
GO

-- Soporta dos cosas nuevas del sistema de landing pages por módulo (/landing/{slug}):
-- 1) que el registro público sepa qué módulo eligió el visitante en la landing, para activarle
--    la prueba automáticamente al confirmar el email (sin volver a preguntarle);
-- 2) que Administrar pueda suspender una landing sin tocar código.
-- Ejecutar manualmente contra ALFA_CENTRAL — no se aplica solo.

IF COL_LENGTH('dbo.RegistroPublicoPendiente', 'ModuloSlug') IS NULL
    ALTER TABLE dbo.RegistroPublicoPendiente ADD ModuloSlug nvarchar(50) NULL;
GO

IF OBJECT_ID(N'dbo.LandingModulos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LandingModulos
    (
        Slug            nvarchar(50) NOT NULL,
        Activo          bit          NOT NULL CONSTRAINT DF_LandingModulos_Activo DEFAULT (1),
        ModificadoUtc   datetime     NOT NULL CONSTRAINT DF_LandingModulos_ModificadoUtc DEFAULT (GETUTCDATE()),
        ModificadoPor   nvarchar(100) NULL,
        CONSTRAINT PK_LandingModulos PRIMARY KEY CLUSTERED (Slug ASC)
    );
END;
GO

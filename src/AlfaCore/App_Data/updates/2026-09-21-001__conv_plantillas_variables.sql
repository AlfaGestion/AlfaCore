/*
    Catálogo de variables de plantillas WhatsApp - mapping persistido.
    Asocia cada posición {{N}} del cuerpo (BODY) de una plantilla con una VariableKey
    estable del catálogo tipado en código (WhatsAppTemplateVariableCatalog).

    Compatibilidad: una plantilla sin filas en esta tabla se comporta 100% manual,
    igual que antes de esta migración. No se modifican ni se borran filas de
    CONV_PLANTILLAS. No se hace backfill heurístico.
*/

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS_VARIABLES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_PLANTILLAS_VARIABLES
    (
        IdPlantillaVariable bigint IDENTITY(1,1) NOT NULL,
        IdPlantilla bigint NOT NULL,
        Componente nvarchar(20) NOT NULL CONSTRAINT DF_CONV_PLANTILLAS_VARIABLES_Componente DEFAULT (N'BODY'),
        Posicion int NOT NULL,
        VariableKey nvarchar(80) NOT NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_PLANTILLAS_VARIABLES_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_PLANTILLAS_VARIABLES PRIMARY KEY CLUSTERED (IdPlantillaVariable),
        CONSTRAINT UQ_CONV_PLANTILLAS_VARIABLES_Posicion UNIQUE (IdPlantilla, Componente, Posicion),
        CONSTRAINT CK_CONV_PLANTILLAS_VARIABLES_Posicion CHECK (Posicion >= 1),
        CONSTRAINT CK_CONV_PLANTILLAS_VARIABLES_Componente CHECK (Componente IN (N'BODY', N'HEADER')),
        CONSTRAINT FK_CONV_PLANTILLAS_VARIABLES_PLANTILLA FOREIGN KEY (IdPlantilla)
            REFERENCES dbo.CONV_PLANTILLAS (IdPlantilla)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CONV_PLANTILLAS_VARIABLES') AND name = N'IX_CONV_PLANTILLAS_VARIABLES_Plantilla')
    CREATE NONCLUSTERED INDEX IX_CONV_PLANTILLAS_VARIABLES_Plantilla
        ON dbo.CONV_PLANTILLAS_VARIABLES (IdPlantilla, Componente) INCLUDE (Posicion, VariableKey);
GO

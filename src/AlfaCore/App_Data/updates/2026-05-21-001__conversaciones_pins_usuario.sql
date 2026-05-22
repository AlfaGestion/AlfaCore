/*
    Conversaciones - pins por usuario.
    Permite fijar conversaciones de forma independiente para cada usuario/sistema.
*/

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES_PIN_USUARIO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_CONVERSACIONES_PIN_USUARIO
    (
        Usuario nvarchar(120) NOT NULL,
        Sistema nvarchar(50) NOT NULL CONSTRAINT DF_CONV_CONV_PIN_USU_Sistema DEFAULT (N''),
        IdConversacion bigint NOT NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_CONV_PIN_USU_FhGrab DEFAULT (GETDATE()),
        CONSTRAINT PK_CONV_CONVERSACIONES_PIN_USUARIO PRIMARY KEY CLUSTERED (Usuario, Sistema, IdConversacion),
        CONSTRAINT FK_CONV_CONV_PIN_USU_CONVERSACION FOREIGN KEY (IdConversacion)
            REFERENCES dbo.CONV_CONVERSACIONES (IdConversacion)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CONV_CONV_PIN_USU_Orden'
      AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES_PIN_USUARIO')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_CONV_PIN_USU_Orden
        ON dbo.CONV_CONVERSACIONES_PIN_USUARIO (Usuario, Sistema, FechaHora_Grabacion DESC)
        INCLUDE (IdConversacion);
END;

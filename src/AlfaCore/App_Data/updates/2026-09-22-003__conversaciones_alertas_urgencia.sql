/*
    Tabla operativa para alertas de urgencia del bot a técnicos.
    Idempotencia: una alerta por IdMensajeOrigen + IdTecnico.
    Retry: un registro en ERROR puede reintentarse actualizando la misma fila; el UNIQUE no se rompe.
*/

IF OBJECT_ID(N'dbo.CONV_ALERTAS_URGENCIA_ENVIADAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ALERTAS_URGENCIA_ENVIADAS
    (
        IdAlerta bigint IDENTITY(1,1) NOT NULL,
        IdMensajeOrigen bigint NOT NULL,
        IdConversacion bigint NOT NULL,
        IdTecnico nvarchar(20) NOT NULL,
        FechaIntento datetime NOT NULL CONSTRAINT DF_CONV_ALERTAS_URG_FECHA_INTENTO DEFAULT (GETDATE()),
        FechaEnvio datetime NULL,
        Estado nvarchar(20) NOT NULL CONSTRAINT DF_CONV_ALERTAS_URG_ESTADO DEFAULT (N'PENDIENTE'),
        ErrorResumen nvarchar(500) NULL,
        WhatsAppMessageId nvarchar(200) NULL,
        PayloadJson nvarchar(max) NULL,
        CONSTRAINT PK_CONV_ALERTAS_URGENCIA_ENVIADAS PRIMARY KEY CLUSTERED (IdAlerta),
        CONSTRAINT CK_CONV_ALERTAS_URGENCIA_ESTADO CHECK (Estado IN (N'PENDIENTE', N'ENVIADO', N'ERROR'))
    );
END;

IF COL_LENGTH(N'dbo.CONV_ALERTAS_URGENCIA_ENVIADAS', N'WhatsAppMessageId') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_ALERTAS_URGENCIA_ENVIADAS
        ADD WhatsAppMessageId nvarchar(200) NULL;
END;

IF COL_LENGTH(N'dbo.CONV_ALERTAS_URGENCIA_ENVIADAS', N'PayloadJson') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_ALERTAS_URGENCIA_ENVIADAS
        ADD PayloadJson nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.CONV_ALERTAS_URGENCIA_ENVIADAS')
      AND name = N'UX_CONV_ALERTAS_URGENCIA_MENSAJE_TECNICO'
)
BEGIN
    CREATE UNIQUE INDEX UX_CONV_ALERTAS_URGENCIA_MENSAJE_TECNICO
        ON dbo.CONV_ALERTAS_URGENCIA_ENVIADAS (IdMensajeOrigen, IdTecnico);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.CONV_ALERTAS_URGENCIA_ENVIADAS')
      AND name = N'IX_CONV_ALERTAS_URGENCIA_CONVERSACION'
)
BEGIN
    CREATE INDEX IX_CONV_ALERTAS_URGENCIA_CONVERSACION
        ON dbo.CONV_ALERTAS_URGENCIA_ENVIADAS (IdConversacion, FechaIntento);
END;

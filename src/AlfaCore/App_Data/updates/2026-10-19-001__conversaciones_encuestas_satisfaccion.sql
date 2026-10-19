/*
    CONV_ENCUESTAS: encuesta de satisfacción al cerrar una conversación de WhatsApp (2026-10-19).

    Al cerrar, AlfaCore le manda al cliente "¿Cómo calificarías nuestra atención?" con tres botones
    (Muy bien / Regular / Mal); la respuesta, y un comentario opcional, quedan acá. Una fila por envío:
    una misma conversación de WhatsApp se cierra muchas veces. Se activa en Configuración →
    Automatización → Encuesta de satisfacción. Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.CONV_ENCUESTAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ENCUESTAS
    (
        IdEncuesta          bigint IDENTITY(1,1) NOT NULL,
        IdConversacion      bigint        NOT NULL,
        IdNumeroWhatsApp    int           NULL,
        Canal               nvarchar(30)  NOT NULL CONSTRAINT DF_CONV_ENCUESTAS_Canal DEFAULT (N'WHATSAPP'),
        IdMensaje           bigint        NULL,
        Estado              nvarchar(20)  NOT NULL CONSTRAINT DF_CONV_ENCUESTAS_Estado DEFAULT (N'ENVIANDO'),
        FechaEnvio          datetime      NOT NULL CONSTRAINT DF_CONV_ENCUESTAS_FechaEnvio DEFAULT (GETDATE()),
        UsuarioCierre       nvarchar(50)  NULL,
        IdTecnicoAtendio    nvarchar(20)  NULL,
        AtendidoPorAsistente bit          NOT NULL CONSTRAINT DF_CONV_ENCUESTAS_Asistente DEFAULT (0),
        Calificacion        tinyint       NULL,
        FechaRespuesta      datetime      NULL,
        EsperandoComentario bit           NOT NULL CONSTRAINT DF_CONV_ENCUESTAS_Esperando DEFAULT (0),
        Comentario          nvarchar(1000) NULL,
        FechaComentario     datetime      NULL,
        Error               nvarchar(500) NULL,
        CONSTRAINT PK_CONV_ENCUESTAS PRIMARY KEY CLUSTERED (IdEncuesta),
        CONSTRAINT CK_CONV_ENCUESTAS_Calificacion CHECK (Calificacion IS NULL OR Calificacion BETWEEN 1 AND 3),
        CONSTRAINT CK_CONV_ENCUESTAS_Estado CHECK (Estado IN (N'ENVIANDO', N'ENVIADA', N'RESPONDIDA', N'ERROR'))
    );

    CREATE INDEX IX_CONV_ENCUESTAS_Conversacion ON dbo.CONV_ENCUESTAS (IdConversacion, FechaEnvio DESC);
    CREATE INDEX IX_CONV_ENCUESTAS_FechaEnvio ON dbo.CONV_ENCUESTAS (FechaEnvio) INCLUDE (Calificacion, Estado, IdTecnicoAtendio);
END;

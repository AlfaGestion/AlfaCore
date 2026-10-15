/*
    CONV_ASISTENTE_CORRECCIONES: lo que el equipo de cada cliente le enseña a su asistente IA.

    Desde una respuesta del asistente en la conversación se propone qué tendría que haber hecho
    (responder otra cosa, pasarlo a una persona o no abrir ticket). Los administradores de
    Conversaciones la aprueban (si la propone un administrador queda aprobada). Las aprobadas se usan
    como guía, con prioridad, cuando llega una consulta parecida. Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ASISTENTE_CORRECCIONES
    (
        IdCorreccion        int IDENTITY(1,1) NOT NULL,
        IdConversacion      bigint        NULL,
        IdMensaje           bigint        NULL,
        PreguntaCliente     nvarchar(1000) NOT NULL,
        RespuestaAsistente  nvarchar(max) NULL,
        Accion              nvarchar(20)  NOT NULL CONSTRAINT DF_CONV_ASIST_CORR_Accion DEFAULT (N'RESPONDER'),
        RespuestaCorrecta   nvarchar(max) NULL,
        Estado              nvarchar(20)  NOT NULL CONSTRAINT DF_CONV_ASIST_CORR_Estado DEFAULT (N'PENDIENTE'),
        UsuarioPropone      nvarchar(50)  NULL,
        FechaPropuesta      datetime      NOT NULL CONSTRAINT DF_CONV_ASIST_CORR_FechaProp DEFAULT (GETDATE()),
        UsuarioRevisa       nvarchar(50)  NULL,
        FechaRevision       datetime      NULL,
        VecesUsada          int           NOT NULL CONSTRAINT DF_CONV_ASIST_CORR_Veces DEFAULT (0),
        UltimoUso           datetime      NULL,
        Baja                bit           NOT NULL CONSTRAINT DF_CONV_ASIST_CORR_Baja DEFAULT (0),
        CONSTRAINT PK_CONV_ASISTENTE_CORRECCIONES PRIMARY KEY CLUSTERED (IdCorreccion),
        CONSTRAINT CK_CONV_ASIST_CORR_Accion CHECK (Accion IN (N'RESPONDER', N'DERIVAR', N'SIN_TICKET')),
        CONSTRAINT CK_CONV_ASIST_CORR_Estado CHECK (Estado IN (N'PENDIENTE', N'APROBADA', N'RECHAZADA'))
    );

    CREATE INDEX IX_CONV_ASIST_CORR_Estado ON dbo.CONV_ASISTENTE_CORRECCIONES (Estado, Baja);
END;

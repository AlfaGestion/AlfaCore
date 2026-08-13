SET NOCOUNT ON;
GO

-- ============================================================
-- Conversaciones — Mensajes programados (envío diferido)
--
-- Se escribe el mensaje ahora y se guarda para enviarlo a una fecha/hora.
-- Un job en segundo plano (cada 1 min) toma los PENDIENTE vencidos y los
-- envía. Fuera de la ventana de 24 h de WhatsApp solo se puede enviar una
-- plantilla aprobada; por eso, si a la hora objetivo la ventana estará
-- cerrada, se programa como PLANTILLA (TipoEnvio) con sus variables.
-- ============================================================

IF OBJECT_ID(N'dbo.CONV_MENSAJES_PROGRAMADOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_MENSAJES_PROGRAMADOS
    (
        IdProgramado bigint IDENTITY(1, 1) NOT NULL,
        IdConversacion bigint NOT NULL,
        FechaProgramada datetime NOT NULL,
        TipoEnvio nvarchar(10) NOT NULL CONSTRAINT DF_CONV_MSGPROG_Tipo DEFAULT (N'TEXTO'),
        Texto nvarchar(max) NOT NULL CONSTRAINT DF_CONV_MSGPROG_Texto DEFAULT (N''),
        IdPlantilla bigint NULL,
        ValoresVariables nvarchar(max) NULL,
        Estado nvarchar(15) NOT NULL CONSTRAINT DF_CONV_MSGPROG_Estado DEFAULT (N'PENDIENTE'),
        MotivoError nvarchar(500) NULL,
        IdTecnicoAutor nvarchar(4) NULL,
        UsuarioAutor nvarchar(50) NULL,
        FechaCreacion datetime NOT NULL CONSTRAINT DF_CONV_MSGPROG_FCrea DEFAULT (GETDATE()),
        FechaEnvio datetime NULL,
        IdMensajeEnviado bigint NULL,
        CONSTRAINT PK_CONV_MENSAJES_PROGRAMADOS PRIMARY KEY CLUSTERED (IdProgramado),
        CONSTRAINT CK_CONV_MSGPROG_Tipo CHECK (TipoEnvio IN (N'TEXTO', N'PLANTILLA')),
        CONSTRAINT CK_CONV_MSGPROG_Estado CHECK (Estado IN (N'PENDIENTE', N'ENVIADO', N'CANCELADO', N'ERROR'))
    );

    CREATE NONCLUSTERED INDEX IX_CONV_MSGPROG_Pendientes
        ON dbo.CONV_MENSAJES_PROGRAMADOS (Estado, FechaProgramada)
        INCLUDE (IdConversacion);
END;
GO

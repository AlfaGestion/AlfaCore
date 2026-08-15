SET NOCOUNT ON;
GO

-- ============================================================
-- Conversaciones — Espera antes de responder (Asistente IA)
--
-- Cuando "esperar N minutos antes de responder" está configurado, el bot no
-- contesta apenas llega el mensaje: encola una fila acá y un job en segundo
-- plano (cada 1 min) la retoma cuando se cumple el tiempo, re-chequeando en
-- ese momento si sigue correspondiendo responder (por ejemplo, si un agente
-- humano ya tomó la conversación, no contesta). Da tiempo a que un agente
-- humano responda primero sin que el bot se le adelante.
-- ============================================================

IF OBJECT_ID(N'dbo.CONV_BOT_PENDIENTES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_BOT_PENDIENTES
    (
        IdPendiente bigint IDENTITY(1, 1) NOT NULL,
        IdConversacion bigint NOT NULL,
        FechaProgramada datetime NOT NULL,
        Estado nvarchar(15) NOT NULL CONSTRAINT DF_CONV_BOTPEND_Estado DEFAULT (N'PENDIENTE'),
        FechaCreacion datetime NOT NULL CONSTRAINT DF_CONV_BOTPEND_FCrea DEFAULT (GETDATE()),
        FechaProcesado datetime NULL,
        CONSTRAINT PK_CONV_BOT_PENDIENTES PRIMARY KEY CLUSTERED (IdPendiente),
        CONSTRAINT CK_CONV_BOTPEND_Estado CHECK (Estado IN (N'PENDIENTE', N'PROCESADO', N'CANCELADO'))
    );

    CREATE NONCLUSTERED INDEX IX_CONV_BOTPEND_Pendientes
        ON dbo.CONV_BOT_PENDIENTES (Estado, FechaProgramada)
        INCLUDE (IdConversacion);
END;
GO

SET NOCOUNT ON;
GO

-- ============================================================
-- Conversaciones — Asistente IA (config estilo "proyecto de GPT")
--
-- Fila única (Id = 1) con el comportamiento (persona/instrucciones), la
-- información del negocio pegada (nvarchar(max), fuente de verdad para
-- responder) y la política de respuesta:
--   SOLO_INFO      -> responde solo con la info/base; si no, escala.
--   GENERAL        -> si la info no alcanza, responde con IA general.
--   GENERAL_AVISA  -> responde con IA general aclarando que un humano confirma.
-- La activación y los guardarraíles del bot siguen en TA_CONFIGURACION
-- (CONV_BOT_*); acá va solo el "cerebro" (texto largo) del asistente.
-- ============================================================

IF OBJECT_ID(N'dbo.CONV_ASISTENTE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ASISTENTE
    (
        Id tinyint NOT NULL CONSTRAINT DF_CONV_ASISTENTE_Id DEFAULT (1),
        Comportamiento nvarchar(max) NOT NULL CONSTRAINT DF_CONV_ASISTENTE_Comportamiento DEFAULT (N''),
        Informacion nvarchar(max) NOT NULL CONSTRAINT DF_CONV_ASISTENTE_Informacion DEFAULT (N''),
        Politica nvarchar(20) NOT NULL CONSTRAINT DF_CONV_ASISTENTE_Politica DEFAULT (N'GENERAL_AVISA'),
        FechaModificacion datetime NULL,
        UsuarioModificacion nvarchar(50) NULL,
        CONSTRAINT PK_CONV_ASISTENTE PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_CONV_ASISTENTE_Id CHECK (Id = 1),
        CONSTRAINT CK_CONV_ASISTENTE_Politica CHECK (Politica IN (N'SOLO_INFO', N'GENERAL', N'GENERAL_AVISA'))
    );
END;
GO

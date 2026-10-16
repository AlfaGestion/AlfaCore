/*
    CONV_ASISTENTE_CORRECCIONES: suma la acción N'BUENA' (el equipo marca con 👍 una respuesta del
    asistente como correcta; se usa como ejemplo, nunca para responder directo). Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES', N'U') IS NULL
    RETURN;
GO

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES')
      AND name = N'CK_CONV_ASIST_CORR_Accion'
)
    ALTER TABLE dbo.CONV_ASISTENTE_CORRECCIONES DROP CONSTRAINT CK_CONV_ASIST_CORR_Accion;
GO

ALTER TABLE dbo.CONV_ASISTENTE_CORRECCIONES WITH NOCHECK
    ADD CONSTRAINT CK_CONV_ASIST_CORR_Accion CHECK (Accion IN (N'RESPONDER', N'DERIVAR', N'SIN_TICKET', N'BUENA'));
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.CONV_ASISTENTE_CORRECCIONES') AND name = N'IX_CONV_ASIST_CORR_Mensaje'
)
    CREATE INDEX IX_CONV_ASIST_CORR_Mensaje ON dbo.CONV_ASISTENTE_CORRECCIONES (IdConversacion, IdMensaje);
GO

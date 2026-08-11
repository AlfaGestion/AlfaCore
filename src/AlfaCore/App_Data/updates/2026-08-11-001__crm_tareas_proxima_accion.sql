/*
    CRM - Fase 1: tareas / próxima acción por oportunidad.
    Actividades PROGRAMADAS (a futuro, con vencimiento y estado de completado), a diferencia de
    CRM_ACTIVIDAD que es la bitácora inmutable de lo ya ocurrido. La "próxima acción" de una
    oportunidad es la tarea no completada más próxima por Vencimiento.
    Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CRM_TAREAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_TAREAS
    (
        IdTarea bigint IDENTITY(1,1) NOT NULL,
        IdOportunidad bigint NOT NULL,
        TipoAccion nvarchar(20) NOT NULL CONSTRAINT DF_CRM_TAREAS_Tipo DEFAULT (N'TAREA'),
        Titulo nvarchar(200) NOT NULL,
        Detalle nvarchar(max) NULL,
        Vencimiento datetime NOT NULL,
        IdTecnico nvarchar(20) NULL,
        Completada bit NOT NULL CONSTRAINT DF_CRM_TAREAS_Completada DEFAULT (0),
        FechaHoraCompletada datetime NULL,
        UsuarioCompleta nvarchar(80) NULL,
        UsuarioAlta nvarchar(80) NULL,
        Baja bit NOT NULL CONSTRAINT DF_CRM_TAREAS_Baja DEFAULT (0),
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_CRM_TAREAS_FHA DEFAULT (GETDATE()),
        FechaHoraModificacion datetime NULL,
        CONSTRAINT PK_CRM_TAREAS PRIMARY KEY CLUSTERED (IdTarea),
        CONSTRAINT FK_CRM_TAREAS_OP FOREIGN KEY (IdOportunidad) REFERENCES dbo.CRM_OPORTUNIDADES (IdOportunidad),
        CONSTRAINT CK_CRM_TAREAS_Tipo CHECK (TipoAccion IN (N'LLAMADA', N'EMAIL', N'REUNION', N'WHATSAPP', N'TAREA'))
    );
END;
GO

-- Próxima acción pendiente por oportunidad: se filtra por (IdOportunidad, Completada, Baja) y se
-- ordena por Vencimiento; el índice cubre ese acceso.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_TAREAS') AND name = N'IX_CRM_TAREAS_OP_Pendiente')
    CREATE NONCLUSTERED INDEX IX_CRM_TAREAS_OP_Pendiente
        ON dbo.CRM_TAREAS (IdOportunidad, Completada, Baja, Vencimiento);
GO

-- Agenda por técnico (para la futura bandeja "mis próximas acciones" y el reporte de vencidas).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_TAREAS') AND name = N'IX_CRM_TAREAS_Tecnico_Pendiente')
    CREATE NONCLUSTERED INDEX IX_CRM_TAREAS_Tecnico_Pendiente
        ON dbo.CRM_TAREAS (IdTecnico, Completada, Baja, Vencimiento);
GO

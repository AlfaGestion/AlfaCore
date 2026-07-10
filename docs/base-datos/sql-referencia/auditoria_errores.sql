/*
Estructura sugerida si luego querés persistir errores y auditoría en SQL
en lugar de dejarlo solo en App_Data/diagnostics/*.jsonl.
*/

IF NOT EXISTS (
    SELECT 1
    FROM sys.tables
    WHERE name = 'SYS_EventosAplicacion'
      AND schema_id = SCHEMA_ID('dbo')
)
BEGIN
    CREATE TABLE dbo.SYS_EventosAplicacion (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        FechaHora datetime NOT NULL DEFAULT GETDATE(),
        Tipo nvarchar(20) NOT NULL,
        Severidad nvarchar(20) NOT NULL,
        Modulo nvarchar(80) NOT NULL,
        Accion nvarchar(120) NOT NULL,
        EntidadTipo nvarchar(80) NULL,
        EntidadId nvarchar(120) NULL,
        Usuario nvarchar(120) NULL,
        ServidorSql nvarchar(120) NULL,
        BaseDatos nvarchar(120) NULL,
        RequestPath nvarchar(300) NULL,
        HttpMethod nvarchar(20) NULL,
        TraceId nvarchar(120) NULL,
        CorrelationId nvarchar(120) NULL,
        Mensaje nvarchar(1000) NULL,
        MensajeUsuario nvarchar(500) NULL,
        ExceptionType nvarchar(250) NULL,
        ExceptionMessage nvarchar(max) NULL,
        StackTrace nvarchar(max) NULL,
        DataJson nvarchar(max) NULL
    );

    CREATE INDEX IX_SYS_EventosAplicacion_FechaHora
        ON dbo.SYS_EventosAplicacion (FechaHora DESC);

    CREATE INDEX IX_SYS_EventosAplicacion_Modulo
        ON dbo.SYS_EventosAplicacion (Modulo, FechaHora DESC);
END
GO

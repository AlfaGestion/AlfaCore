-- MCP de consulta en lenguaje natural - Etapa 2 / script 3: auditoria de consultas.
-- Idempotente. La tabla vive en dbo (NO en el esquema dir) para que consultar_sql no pueda leerla.
-- El login del MCP recibe solo INSERT sobre esta tabla (ver 005), sin SELECT.
SET NOCOUNT ON;
GO
IF OBJECT_ID(N'dbo.IA_LOG_CONSULTAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IA_LOG_CONSULTAS
    (
        Id          bigint IDENTITY(1,1) NOT NULL,
        Fecha       datetime       NOT NULL CONSTRAINT DF_IA_LOG_CONSULTAS_Fecha DEFAULT (GETDATE()),
        Usuario     nvarchar(100)  NULL,      -- hoy: usuario fijo del MCP; con OAuth (etapa 5): claim del token
        Herramienta nvarchar(60)   NOT NULL,  -- ventas_resumen, ranking_articulos, consultar_sql, ...
        Parametros  nvarchar(max)  NULL,      -- JSON de argumentos (se omite si IA_MCP_LOG_PARAMETROS <> SI)
        DuracionMs  int            NULL,
        Filas       int            NULL,
        Error       nvarchar(max)  NULL,      -- NULL = ejecucion correcta
        CONSTRAINT PK_IA_LOG_CONSULTAS PRIMARY KEY CLUSTERED (Id)
    );
    CREATE NONCLUSTERED INDEX IX_IA_LOG_CONSULTAS_Fecha ON dbo.IA_LOG_CONSULTAS (Fecha DESC);
END
GO

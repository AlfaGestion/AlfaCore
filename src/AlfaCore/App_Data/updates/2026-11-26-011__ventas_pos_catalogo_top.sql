SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NULL
    RETURN;

IF EXISTS
(
    SELECT 1
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
      AND c.name = N'VALOR'
      AND c.max_length <> -1
)
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
          AND name = N'IX_TA_CONFIGURACION_CLAVE'
    )
        DROP INDEX IX_TA_CONFIGURACION_CLAVE ON dbo.TA_CONFIGURACION;

    DECLARE @Nulabilidad nvarchar(8) =
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND name = N'VALOR'
              AND is_nullable = 1
        ) THEN N'NULL' ELSE N'NOT NULL' END;

    EXEC(N'ALTER TABLE dbo.TA_CONFIGURACION ALTER COLUMN VALOR nvarchar(max) ' + @Nulabilidad + N';');

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
          AND name = N'IX_TA_CONFIGURACION_CLAVE'
    )
        CREATE NONCLUSTERED INDEX IX_TA_CONFIGURACION_CLAVE
            ON dbo.TA_CONFIGURACION (CLAVE)
            INCLUDE (VALOR);
END;

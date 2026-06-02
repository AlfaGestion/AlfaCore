/*
    Tareas - prioridades visuales y orden operativo.
*/

IF COL_LENGTH(N'dbo.ALFACORE_TAREAS', N'Prioridad') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_TAREAS
    ADD Prioridad nvarchar(10) NOT NULL
        CONSTRAINT DF_ALFACORE_TAREAS_Prioridad DEFAULT (N'MEDIA');
END;
GO

UPDATE dbo.ALFACORE_TAREAS
SET Prioridad = N'MEDIA'
WHERE Prioridad IS NULL
   OR LTRIM(RTRIM(Prioridad)) = N''
   OR UPPER(LTRIM(RTRIM(Prioridad))) NOT IN (N'ALTA', N'MEDIA', N'BAJA');
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_ALFACORE_TAREAS_Prioridad'
      AND parent_object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS')
)
BEGIN
    ALTER TABLE dbo.ALFACORE_TAREAS WITH CHECK
    ADD CONSTRAINT CK_ALFACORE_TAREAS_Prioridad
        CHECK (Prioridad IN (N'ALTA', N'MEDIA', N'BAJA'));
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_ALFACORE_TAREAS_PRIORIDAD'
      AND object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS')
)
BEGIN
    CREATE INDEX IX_ALFACORE_TAREAS_PRIORIDAD
        ON dbo.ALFACORE_TAREAS (Prioridad, Estado, Activa, FechaVencimiento);
END;
GO

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS', N'U') IS NULL
    BEGIN
        COMMIT TRAN;
        RETURN;
    END;

    IF COL_LENGTH(N'dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS', N'Detalle') IS NULL
    BEGIN
        ALTER TABLE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
        ADD Detalle nvarchar(max) NULL;
    END;

    IF COL_LENGTH(N'dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS', N'Orden') IS NULL
    BEGIN
        ALTER TABLE dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
        ADD Orden int NOT NULL
            CONSTRAINT DF_ALFACORE_TAREAS_NOTAS_Orden DEFAULT (0);
    END;

    ;WITH Pendientes AS
    (
        SELECT
            IdNota,
            ROW_NUMBER() OVER
            (
                PARTITION BY UPPER(LTRIM(RTRIM(Usuario))), ISNULL(Completada, 0)
                ORDER BY FechaHoraAlta DESC, IdNota DESC
            ) AS RowNum
        FROM dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS
        WHERE ISNULL(Activa, 1) = 1
          AND ISNULL(Orden, 0) = 0
    )
    UPDATE n
    SET Orden = p.RowNum * 10
    FROM dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS n
    INNER JOIN Pendientes p ON p.IdNota = n.IdNota;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_ALFACORE_TAREAS_NOTAS_USUARIO_ORDEN'
          AND object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS')
    )
    BEGIN
        CREATE INDEX IX_ALFACORE_TAREAS_NOTAS_USUARIO_ORDEN
        ON dbo.ALFACORE_TAREAS_NOTAS_RAPIDAS (Usuario, Activa, Completada, Orden, FechaHoraAlta);
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;

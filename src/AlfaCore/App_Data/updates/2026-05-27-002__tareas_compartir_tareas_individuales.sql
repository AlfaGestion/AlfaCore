SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_COMPARTIDOS', N'U') IS NOT NULL
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE name = N'CK_ALFACORE_TAREAS_COMPARTIDOS_Tipo'
              AND parent_object_id = OBJECT_ID(N'dbo.ALFACORE_TAREAS_COMPARTIDOS')
        )
        BEGIN
            ALTER TABLE dbo.ALFACORE_TAREAS_COMPARTIDOS
                DROP CONSTRAINT CK_ALFACORE_TAREAS_COMPARTIDOS_Tipo;
        END;

        ALTER TABLE dbo.ALFACORE_TAREAS_COMPARTIDOS WITH CHECK
            ADD CONSTRAINT CK_ALFACORE_TAREAS_COMPARTIDOS_Tipo
                CHECK (TipoObjeto IN (N'LISTA', N'NOTA', N'TAREA'));
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    -- THROW/re-throw requieren compat level >= 110 (SQL Server 2012); hay bases de clientes en
    -- 80/100 (ver dbo.databases.compatibility_level), así que se usa RAISERROR en su lugar.
    DECLARE @ErrorMessage nvarchar(4000) = ERROR_MESSAGE(),
            @ErrorSeverity int = ERROR_SEVERITY(),
            @ErrorState int = ERROR_STATE();
    RAISERROR(N'%s', @ErrorSeverity, @ErrorState, @ErrorMessage);
END CATCH;

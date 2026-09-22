SET NOCOUNT ON;

-- THROW/re-throw requieren compat level >= 110 (SQL Server 2012); hay bases de clientes en 80/100
-- (ver dbo.databases.compatibility_level), así que este script usa RAISERROR en su lugar.
IF OBJECT_ID(N'dbo.INT_TIPO_DOCUMENTO', N'U') IS NULL
BEGIN
    RAISERROR(N'No existe dbo.INT_TIPO_DOCUMENTO en la base activa.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM dbo.INT_TIPO_DOCUMENTO WHERE Codigo = N'CONTROLES_ARCA')
    BEGIN
        UPDATE dbo.INT_TIPO_DOCUMENTO
        SET
            Descripcion = N'Controles ARCA',
            Orden = 70,
            Activo = 1
        WHERE Codigo = N'CONTROLES_ARCA';
    END
    ELSE
    BEGIN
        INSERT INTO dbo.INT_TIPO_DOCUMENTO (Codigo, Descripcion, Orden, Activo)
        VALUES (N'CONTROLES_ARCA', N'Controles ARCA', 70, 1);
    END;

    UPDATE dbo.INT_TIPO_DOCUMENTO
    SET
        Descripcion = N'Otros',
        Orden = 80,
        Activo = 1
    WHERE Codigo = N'OTROS';

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRAN;

    DECLARE @ErrorMessage nvarchar(4000) = ERROR_MESSAGE(),
            @ErrorSeverity int = ERROR_SEVERITY(),
            @ErrorState int = ERROR_STATE();
    RAISERROR(N'%s', @ErrorSeverity, @ErrorState, @ErrorMessage);
END CATCH;

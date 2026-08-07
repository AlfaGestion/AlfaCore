/*
Permite modulos "solo funcionalidad" que no agregan un nodo de menu propio (ej.: AlfaKnowledge,
que es un boton/panel dentro de la pantalla de Conversaciones, no una seccion navegable del
menu). Ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md.

dbo.Modulos ya existe en produccion (modulos_modelo_inicial.sql se corrio el 2026-08-05), asi
que esto va en un script nuevo en vez de editar el original.

Ejecutar manualmente contra ALFA_CENTRAL.
*/

SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRAN;

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Modulos') AND name = N'MenuKeyRaiz' AND is_nullable = 0
    )
    BEGIN
        ALTER TABLE dbo.Modulos ALTER COLUMN MenuKeyRaiz nvarchar(100) NULL;
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
GO

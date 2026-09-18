/*
Permite importar desde Meta plantillas de categoría AUTHENTICATION.
Script idempotente para base tenant.
*/

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS', N'U') IS NULL
    RETURN;

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.CONV_PLANTILLAS')
      AND name = N'CK_CONV_PLANTILLAS_Categoria'
)
BEGIN
    ALTER TABLE dbo.CONV_PLANTILLAS DROP CONSTRAINT CK_CONV_PLANTILLAS_Categoria;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.CONV_PLANTILLAS')
      AND name = N'CK_CONV_PLANTILLAS_Categoria'
)
BEGIN
    ALTER TABLE dbo.CONV_PLANTILLAS WITH CHECK
    ADD CONSTRAINT CK_CONV_PLANTILLAS_Categoria
        CHECK (Categoria IN (N'MARKETING', N'UTILITY', N'AUTHENTICATION'));
END;

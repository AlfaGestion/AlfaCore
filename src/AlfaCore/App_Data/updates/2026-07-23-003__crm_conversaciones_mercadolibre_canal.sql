/*
    AlfaCore - Conversaciones
    Habilita el canal MERCADOLIBRE para preguntas de publicaciones.
*/

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.CONV_CANALES', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.CONV_CANALES WHERE Canal = N'MERCADOLIBRE')
        INSERT INTO dbo.CONV_CANALES (Canal, Nombre, Orden, Color, Icono)
        VALUES (N'MERCADOLIBRE', N'Mercado Libre', 40, N'#facc15', N'bi-bag-fill');
END;

DECLARE @ckName sysname;

SELECT TOP (1) @ckName = cc.name
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES')
  AND cc.definition LIKE N'%WHATSAPP%'
  AND cc.definition LIKE N'%INTERNO%'
  AND cc.definition NOT LIKE N'%MERCADOLIBRE%';

IF @ckName IS NOT NULL
BEGIN
    DECLARE @sql nvarchar(max) =
        N'ALTER TABLE dbo.CONV_CONVERSACIONES DROP CONSTRAINT ' + QUOTENAME(@ckName) + N';';
    EXEC sys.sp_executesql @sql;
END;

IF OBJECT_ID(N'dbo.CK_CONV_CONVERSACIONES_Canal', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_CONVERSACIONES WITH NOCHECK
    ADD CONSTRAINT CK_CONV_CONVERSACIONES_Canal
        CHECK (Canal IN (N'WHATSAPP', N'INSTAGRAM', N'FACEBOOK', N'MERCADOLIBRE', N'INTERNO'));
END;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'IdentificadorExternoConversacion') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_CONV_CONVERSACIONES_Canal_ExternoConversacion'
         AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_CONV_CONVERSACIONES_Canal_ExternoConversacion
        ON dbo.CONV_CONVERSACIONES (Canal, IdentificadorExternoConversacion, FechaHoraUltimoMensaje DESC)
        INCLUDE (IdConversacion, CodigoEstado, NombreVisible, IdentificadorExternoContacto);
END;

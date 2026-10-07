/*
    AlfaCore - Conversaciones
    Habilita el canal WEBCHAT (chat embebible en sitios externos, ej. WordPress) y agrega las
    columnas donde se guarda desde qué página / contexto escribió el visitante.

    Idempotente: puede ejecutarse varias veces.
*/

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.CONV_CANALES', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.CONV_CANALES WHERE Canal = N'WEBCHAT')
        INSERT INTO dbo.CONV_CANALES (Canal, Nombre, Orden, Color, Icono)
        VALUES (N'WEBCHAT', N'Chat del sitio web', 50, N'#0ea5e9', N'bi-globe2');
END;

-- El CHECK de canales se recrea solo si todavía no incluye WEBCHAT (mismo patrón que
-- 2026-07-23-003__crm_conversaciones_mercadolibre_canal.sql).
DECLARE @ckName sysname;

SELECT TOP (1) @ckName = cc.name
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES')
  AND cc.definition LIKE N'%WHATSAPP%'
  AND cc.definition LIKE N'%INTERNO%'
  AND cc.definition NOT LIKE N'%WEBCHAT%';

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
        CHECK (Canal IN (N'WHATSAPP', N'INSTAGRAM', N'FACEBOOK', N'MERCADOLIBRE', N'WEBCHAT', N'INTERNO'));
END;

-- Última página / contexto desde el que escribió el visitante del chat web. El detalle por
-- mensaje queda además en CONV_MENSAJES.PayloadJson.
IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'OrigenPaginaUrl') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD OrigenPaginaUrl nvarchar(1000) NULL;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'OrigenContexto') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD OrigenContexto nvarchar(200) NULL;

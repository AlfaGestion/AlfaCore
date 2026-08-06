SET NOCOUNT ON;

-- ============================================================
-- Conversaciones — marca interna por mensaje (Pendiente/Completada)
--
-- Uso: el agente marca mensajes puntuales del cliente como
-- "tarea pendiente" o "completada" para hacerles seguimiento sin
-- necesidad de crear un ticket. Es 100% interno — nunca se envía
-- al cliente por WhatsApp/Instagram/Facebook/MercadoLibre, a
-- diferencia de las reacciones.
-- ============================================================

IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF COL_LENGTH(N'dbo.CONV_MENSAJES', N'MarcaInterna') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_MENSAJES
        ADD MarcaInterna nvarchar(20) NULL;
END;
GO

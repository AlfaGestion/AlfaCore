/*
    CRM - Fase 3.3: token público para compartir la cotización.
    Permite un link no adivinable a una página pública de la cotización (para enviar
    por WhatsApp/email). Se genera on-demand la primera vez que se comparte.
    Idempotente.
*/

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.CRM_COTIZACION', 'PublicToken') IS NULL
BEGIN
    ALTER TABLE dbo.CRM_COTIZACION ADD PublicToken nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_COTIZACION') AND name = N'IX_CRM_COT_PublicToken')
    CREATE NONCLUSTERED INDEX IX_CRM_COT_PublicToken ON dbo.CRM_COTIZACION (PublicToken) WHERE PublicToken IS NOT NULL;
GO

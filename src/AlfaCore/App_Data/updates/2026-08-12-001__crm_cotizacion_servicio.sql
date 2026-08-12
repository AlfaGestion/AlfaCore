/*
    CRM - Fase 3.1: cotización por servicio.
    Distingue el tipo de cotización (ARTICULOS / SERVICIO) y guarda el cuerpo de la
    propuesta como HTML enriquecido (redactado a mano o con ayuda del asistente de IA).
    Idempotente.
*/

SET NOCOUNT ON;

IF COL_LENGTH('dbo.CRM_COTIZACION', 'Tipo') IS NULL
BEGIN
    ALTER TABLE dbo.CRM_COTIZACION
        ADD Tipo nvarchar(12) NOT NULL CONSTRAINT DF_CRM_COT_Tipo DEFAULT (N'ARTICULOS');
END;
GO

IF COL_LENGTH('dbo.CRM_COTIZACION', 'CuerpoServicio') IS NULL
BEGIN
    ALTER TABLE dbo.CRM_COTIZACION ADD CuerpoServicio nvarchar(max) NULL;
END;
GO

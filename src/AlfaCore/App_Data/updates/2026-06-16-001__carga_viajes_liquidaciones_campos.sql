-- ============================================================
-- Carga de Viajes - campos para liquidaciones de fletes
-- Agrega soporte para marcar viajes como pagados.
-- ============================================================

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.MV_VIAJES_CARGA', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF COL_LENGTH(N'dbo.MV_VIAJES_CARGA', N'FLETE_PAGADO') IS NULL
BEGIN
    ALTER TABLE dbo.MV_VIAJES_CARGA
        ADD FLETE_PAGADO bit NOT NULL
            CONSTRAINT DF_MV_VIAJES_CARGA_FLETE_PAGADO DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.MV_VIAJES_CARGA', N'FECHA_PAGO_FLETE') IS NULL
BEGIN
    ALTER TABLE dbo.MV_VIAJES_CARGA
        ADD FECHA_PAGO_FLETE smalldatetime NULL;
END;
GO

IF COL_LENGTH(N'dbo.MV_VIAJES_CARGA', N'USUARIO_PAGO_FLETE') IS NULL
BEGIN
    ALTER TABLE dbo.MV_VIAJES_CARGA
        ADD USUARIO_PAGO_FLETE nvarchar(50) NULL;
END;
GO

IF COL_LENGTH(N'dbo.MV_VIAJES_CARGA', N'OBSERVACION_PAGO_FLETE') IS NULL
BEGIN
    ALTER TABLE dbo.MV_VIAJES_CARGA
        ADD OBSERVACION_PAGO_FLETE nvarchar(250) NULL;
END;
GO

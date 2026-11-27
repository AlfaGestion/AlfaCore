/* Mercado Pago Point: conserva la metadata que VB6 obtiene de la order aprobada.
   Idempotente para bases que ya tienen MP_POINT_ORDENES. */
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.MP_POINT_ORDENES', N'U') IS NULL
    RETURN;

IF COL_LENGTH(N'dbo.MP_POINT_ORDENES', N'MedioPagoId') IS NULL
    ALTER TABLE dbo.MP_POINT_ORDENES ADD MedioPagoId nvarchar(50) NULL;

IF COL_LENGTH(N'dbo.MP_POINT_ORDENES', N'Cuotas') IS NULL
    ALTER TABLE dbo.MP_POINT_ORDENES ADD Cuotas int NULL;

IF COL_LENGTH(N'dbo.MP_POINT_ORDENES', N'ImporteAprobado') IS NULL
    ALTER TABLE dbo.MP_POINT_ORDENES ADD ImporteAprobado money NULL;

IF COL_LENGTH(N'dbo.MP_POINT_ORDENES', N'StatusDetail') IS NULL
    ALTER TABLE dbo.MP_POINT_ORDENES ADD StatusDetail nvarchar(100) NULL;

-- Amplía dbo.ClienteModulos para soportar el flujo de solicitud/aprobación de módulos
-- (pantalla /admin/solicitudes). Ejecutar manualmente contra ALFA_CENTRAL — no se aplica solo.

ALTER TABLE dbo.ClienteModulos DROP CONSTRAINT CK_ClienteModulos_Estado;
GO

ALTER TABLE dbo.ClienteModulos
    ADD CONSTRAINT CK_ClienteModulos_Estado
    CHECK (Estado IN (N'Solicitado', N'Activo', N'Suspendido', N'Rechazado'));
GO

ALTER TABLE dbo.ClienteModulos ADD SolicitadoUtc datetime NULL;
GO
ALTER TABLE dbo.ClienteModulos ADD SolicitadoPor nvarchar(100) NULL;
GO
ALTER TABLE dbo.ClienteModulos ADD DecididoUtc datetime NULL;
GO
ALTER TABLE dbo.ClienteModulos ADD DecididoPor nvarchar(100) NULL;
GO

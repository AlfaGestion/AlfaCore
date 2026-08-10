-- Amplía dbo.ClienteModulos para soportar la prueba gratuita de 30 días autoservicio, elegida
-- al registrarse en /registrarme (confirmada la cuenta por email). Ejecutar manualmente contra
-- ALFA_CENTRAL — no se aplica solo.

ALTER TABLE dbo.ClienteModulos DROP CONSTRAINT CK_ClienteModulos_Estado;
GO

ALTER TABLE dbo.ClienteModulos
    ADD CONSTRAINT CK_ClienteModulos_Estado
    CHECK (Estado IN (N'Solicitado', N'Activo', N'Prueba', N'Suspendido', N'Rechazado'));
GO

ALTER TABLE dbo.ClienteModulos ADD PruebaVenceUtc datetime NULL;
GO
ALTER TABLE dbo.ClienteModulos ADD UltimoRecordatorioUtc datetime NULL;
GO

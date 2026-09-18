/*
  Estado operativo de una integración WhatsApp ya activa (post-onboarding).
  Destino exclusivo: ALFA_CENTRAL.
  Script idempotente de referencia/versionado. NO es ejecutado automáticamente por AlfaCore.

  Distinto de dbo.WhatsAppEmbeddedOnboarding: ese registro es de un solo onboarding, expira y no
  tiene columnas WabaId/PhoneNumberId. Esta tabla persiste mientras el problema no se resuelva,
  con clave (IdBase, WabaId, PhoneNumberId) para aislar tenant/WABA/número.

  No modifica dbo.WhatsAppWabaOwnership, dbo.WhatsAppPhoneOwnership ni dbo.WhatsAppSecureVault.
*/
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.WhatsAppIntegrationHealth', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WhatsAppIntegrationHealth
    (
        IdBase int NOT NULL,
        WabaId varchar(40) NOT NULL,
        PhoneNumberId varchar(40) NOT NULL,
        State varchar(40) NOT NULL CONSTRAINT DF_WAIH_State DEFAULT ('OK'),
        Reason varchar(80) NULL,
        ErrorCode varchar(20) NOT NULL CONSTRAINT DF_WAIH_ErrorCode DEFAULT (''),
        DetailSummary nvarchar(500) NOT NULL CONSTRAINT DF_WAIH_DetailSummary DEFAULT (N''),
        -- Solo se persiste una URL ya validada (HTTPS + allowlist de host Meta/Facebook, ver
        -- WhatsAppMetaCtaLinks.ExtractSafeMetaCtaUrl). Nunca texto libre ni URLs sin validar.
        CtaUrl varchar(500) NULL,
        SourceMessageId varchar(200) NOT NULL CONSTRAINT DF_WAIH_SourceMessageId DEFAULT (''),
        CreatedAtUtc datetime2(3) NOT NULL,
        ModifiedAtUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_WhatsAppIntegrationHealth PRIMARY KEY (IdBase, WabaId, PhoneNumberId),
        CONSTRAINT FK_WAIH_Base FOREIGN KEY (IdBase) REFERENCES dbo.bases(id),
        CONSTRAINT FK_WAIH_Waba FOREIGN KEY (WabaId) REFERENCES dbo.WhatsAppWabaOwnership(WabaId),
        CONSTRAINT CK_WAIH_State CHECK (State IN ('OK', 'ACTION_REQUIRED')),
        CONSTRAINT CK_WAIH_Reason CHECK (Reason IS NULL OR Reason IN
            ('CUSTOMER_PAYMENT_SETUP_REQUIRED', 'REAUTHORIZATION_REQUIRED', 'CUSTOMER_ACTION_REQUIRED'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WhatsAppIntegrationHealth') AND name = N'IX_WAIH_IdBase')
    CREATE INDEX IX_WAIH_IdBase ON dbo.WhatsAppIntegrationHealth(IdBase);
GO

/*
  Rollback manual, únicamente si se confirma que no hay estados en uso:
  DROP TABLE dbo.WhatsAppIntegrationHealth;
  No ejecutar este bloque como parte del deploy normal.
*/

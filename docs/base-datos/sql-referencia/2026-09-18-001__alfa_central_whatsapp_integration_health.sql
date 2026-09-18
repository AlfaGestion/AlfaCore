/*
  Estado operativo de una integración WhatsApp ya activa (post-onboarding).
  Destino exclusivo: ALFA_CENTRAL.
  Script idempotente de referencia/versionado. NO es ejecutado automáticamente por AlfaCore.
  Compatible con SQL Server 2016 (sin sintaxis EXEC(QUOTENAME(...)) ni construcciones posteriores).

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
        -- Timestamp del evento de Meta (status.timestamp del webhook, no la hora local de
        -- procesamiento) que causó la transición a ACTION_REQUIRED más reciente. Un evento positivo
        -- posterior (sent/delivered/read) solo puede resolver el estado si su propio timestamp de
        -- Meta es posterior a este valor -- evita que un webhook viejo/reordenado limpie un fallo
        -- nuevo (ver ConversacionesService.TryResolvePaymentSetupIfConfirmedAsync).
        RequiredSinceUtc datetime2(3) NULL,
        -- NULL mientras State='ACTION_REQUIRED'. Se completa al resolver; la fila NO se borra para
        -- conservar evidencia histórica (quién tuvo el problema y cuándo se resolvió).
        ResolvedAtUtc datetime2(3) NULL,
        CreatedAtUtc datetime2(3) NOT NULL,
        ModifiedAtUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_WhatsAppIntegrationHealth PRIMARY KEY (IdBase, WabaId, PhoneNumberId),
        CONSTRAINT FK_WAIH_Base FOREIGN KEY (IdBase) REFERENCES dbo.bases(id),
        CONSTRAINT FK_WAIH_Waba FOREIGN KEY (WabaId) REFERENCES dbo.WhatsAppWabaOwnership(WabaId),
        CONSTRAINT FK_WAIH_Phone FOREIGN KEY (PhoneNumberId) REFERENCES dbo.WhatsAppPhoneOwnership(PhoneNumberId),
        CONSTRAINT CK_WAIH_State CHECK (State IN ('OK', 'ACTION_REQUIRED')),
        CONSTRAINT CK_WAIH_Reason CHECK (Reason IS NULL OR Reason IN
            ('CUSTOMER_PAYMENT_SETUP_REQUIRED', 'REAUTHORIZATION_REQUIRED', 'CUSTOMER_ACTION_REQUIRED'))
    );
END;
GO

-- Alta idempotente de columnas para el caso en que una versión anterior de este script ya haya
-- creado la tabla sin RequiredSinceUtc/ResolvedAtUtc (aditivo, no destructivo).
IF COL_LENGTH(N'dbo.WhatsAppIntegrationHealth', N'RequiredSinceUtc') IS NULL
    ALTER TABLE dbo.WhatsAppIntegrationHealth ADD RequiredSinceUtc datetime2(3) NULL;
GO

IF COL_LENGTH(N'dbo.WhatsAppIntegrationHealth', N'ResolvedAtUtc') IS NULL
    ALTER TABLE dbo.WhatsAppIntegrationHealth ADD ResolvedAtUtc datetime2(3) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WhatsAppIntegrationHealth') AND name = N'IX_WAIH_IdBase')
    CREATE INDEX IX_WAIH_IdBase ON dbo.WhatsAppIntegrationHealth(IdBase);
GO

/*
  Rollback manual, únicamente si se confirma que no hay estados en uso:
  DROP TABLE dbo.WhatsAppIntegrationHealth;
  No ejecutar este bloque como parte del deploy normal.
*/

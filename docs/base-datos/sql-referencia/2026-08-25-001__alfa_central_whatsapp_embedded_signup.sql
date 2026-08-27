/*
  ES-1 - Fundación central de WhatsApp Embedded Signup.
  Destino exclusivo: ALFA_CENTRAL.
  Script idempotente de referencia/versionado. NO es ejecutado automáticamente por AlfaCore.
*/
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.WhatsAppEmbeddedOnboarding', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WhatsAppEmbeddedOnboarding
    (
        IdOnboarding uniqueidentifier NOT NULL,
        IdBase int NOT NULL,
        IdCliente nvarchar(50) NOT NULL CONSTRAINT DF_WAEO_IdCliente DEFAULT (N''),
        UsuarioIniciador nvarchar(100) NOT NULL,
        CorrelationId nvarchar(64) NOT NULL,
        StateHash char(64) NOT NULL,
        StateConsumedAtUtc datetime2(3) NULL,
        ModoOnboarding varchar(40) NOT NULL CONSTRAINT DF_WAEO_ModoOnboarding DEFAULT ('STANDARD'),
        Estado varchar(40) NOT NULL,
        PasoActual varchar(80) NOT NULL CONSTRAINT DF_WAEO_PasoActual DEFAULT (''),
        MetaBusinessId varchar(40) NOT NULL CONSTRAINT DF_WAEO_MetaBusinessId DEFAULT (''),
        FechaInicioUtc datetime2(3) NOT NULL,
        FechaExpiracionUtc datetime2(3) NOT NULL,
        FechaModificacionUtc datetime2(3) NOT NULL,
        RetryCount int NOT NULL CONSTRAINT DF_WAEO_RetryCount DEFAULT (0),
        NextAttemptUtc datetime2(3) NULL,
        ErrorCode varchar(80) NOT NULL CONSTRAINT DF_WAEO_ErrorCode DEFAULT (''),
        ErrorSummary nvarchar(500) NOT NULL CONSTRAINT DF_WAEO_ErrorSummary DEFAULT (N''),
        IncidentId varchar(64) NOT NULL CONSTRAINT DF_WAEO_IncidentId DEFAULT (''),
        TokenReference nvarchar(250) NOT NULL CONSTRAINT DF_WAEO_TokenReference DEFAULT (N''),
        ActionRequiredReason varchar(80) NULL,
        ClaimedBy nvarchar(100) NULL,
        ClaimExpiresAtUtc datetime2(3) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_WhatsAppEmbeddedOnboarding PRIMARY KEY (IdOnboarding),
        CONSTRAINT FK_WAEO_Base FOREIGN KEY (IdBase) REFERENCES dbo.bases(id),
        CONSTRAINT CK_WAEO_RetryCount CHECK (RetryCount >= 0),
        CONSTRAINT CK_WAEO_Estado CHECK (Estado IN
        ('STARTED','AUTHORIZED','DISCOVERING_ASSETS','VALIDATING_OWNERSHIP','CONFIGURING_ACCESS','SUBSCRIBING_WABAS',
         'CHECKING_CUSTOMER_PAYMENT','DISCOVERING_PHONES','REGISTERING_PHONES','IMPORTING','READY','ACTION_REQUIRED',
         'FAILED_RETRYABLE','FAILED_FINAL','EXPIRED','CANCELLED')),
        CONSTRAINT CK_WAEO_ActionReason CHECK (ActionRequiredReason IS NULL OR ActionRequiredReason IN
        ('CUSTOMER_PAYMENT_SETUP_REQUIRED','REAUTHORIZATION_REQUIRED','CUSTOMER_ACTION_REQUIRED','WABA_CROSS_TENANT_CONFLICT','PHONE_CROSS_TENANT_CONFLICT'))
    );
END;
GO

IF COL_LENGTH(N'dbo.WhatsAppEmbeddedOnboarding', N'ModoOnboarding') IS NULL
    ALTER TABLE dbo.WhatsAppEmbeddedOnboarding ADD ModoOnboarding varchar(40) NOT NULL CONSTRAINT DF_WAEO_ModoOnboarding DEFAULT ('STANDARD');
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.WhatsAppEmbeddedOnboarding') AND name = N'CK_WAEO_ModoOnboarding')
    ALTER TABLE dbo.WhatsAppEmbeddedOnboarding WITH CHECK ADD CONSTRAINT CK_WAEO_ModoOnboarding CHECK (ModoOnboarding IN ('STANDARD','BUSINESS_APP_COEXISTENCE'));
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.WhatsAppEmbeddedOnboarding') AND name = N'CK_WAEO_Estado')
    ALTER TABLE dbo.WhatsAppEmbeddedOnboarding DROP CONSTRAINT CK_WAEO_Estado;
ALTER TABLE dbo.WhatsAppEmbeddedOnboarding WITH CHECK ADD CONSTRAINT CK_WAEO_Estado CHECK (Estado IN
('STARTED','AUTHORIZED','DISCOVERING_ASSETS','VALIDATING_OWNERSHIP','CONFIGURING_ACCESS','SUBSCRIBING_WABAS',
 'CHECKING_CUSTOMER_PAYMENT','DISCOVERING_PHONES','REGISTERING_PHONES','IMPORTING','SYNCING_HISTORY','SYNCING_CONTACTS','READY','ACTION_REQUIRED',
 'FAILED_RETRYABLE','FAILED_FINAL','EXPIRED','CANCELLED'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WhatsAppEmbeddedOnboarding') AND name = N'UX_WAEO_StateHash')
    CREATE UNIQUE INDEX UX_WAEO_StateHash ON dbo.WhatsAppEmbeddedOnboarding(StateHash);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WhatsAppEmbeddedOnboarding') AND name = N'IX_WAEO_Work')
    CREATE INDEX IX_WAEO_Work ON dbo.WhatsAppEmbeddedOnboarding(Estado, NextAttemptUtc, ClaimExpiresAtUtc, FechaModificacionUtc);
GO

IF OBJECT_ID(N'dbo.WhatsAppWabaOwnership', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WhatsAppWabaOwnership
    (
        WabaId varchar(40) NOT NULL,
        IdBase int NOT NULL,
        MetaBusinessId varchar(40) NOT NULL CONSTRAINT DF_WAWO_MetaBusinessId DEFAULT (''),
        FechaAltaUtc datetime2(3) NOT NULL,
        FechaModificacionUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_WhatsAppWabaOwnership PRIMARY KEY (WabaId),
        CONSTRAINT FK_WAWO_Base FOREIGN KEY (IdBase) REFERENCES dbo.bases(id)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WhatsAppWabaOwnership') AND name = N'IX_WAWO_IdBase')
    CREATE INDEX IX_WAWO_IdBase ON dbo.WhatsAppWabaOwnership(IdBase);
GO

IF OBJECT_ID(N'dbo.WhatsAppPhoneOwnership', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WhatsAppPhoneOwnership
    (
        PhoneNumberId varchar(40) NOT NULL,
        WabaId varchar(40) NOT NULL,
        IdBase int NOT NULL,
        FechaAltaUtc datetime2(3) NOT NULL,
        FechaModificacionUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_WhatsAppPhoneOwnership PRIMARY KEY (PhoneNumberId),
        CONSTRAINT FK_WhatsAppPhoneOwnership_Waba FOREIGN KEY (WabaId) REFERENCES dbo.WhatsAppWabaOwnership(WabaId),
        CONSTRAINT FK_WAPO_Base FOREIGN KEY (IdBase) REFERENCES dbo.bases(id)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WhatsAppPhoneOwnership') AND name = N'IX_WAPO_WabaId_IdBase')
    CREATE INDEX IX_WAPO_WabaId_IdBase ON dbo.WhatsAppPhoneOwnership(WabaId, IdBase);
GO

IF OBJECT_ID(N'dbo.WhatsAppSecureVault', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WhatsAppSecureVault
    (
        SecretReference char(32) NOT NULL,
        SecretType varchar(20) NOT NULL,
        IdBase int NOT NULL,
        IdOnboarding uniqueidentifier NULL,
        MetaBusinessId varchar(40) NOT NULL CONSTRAINT DF_WASV_MetaBusinessId DEFAULT (''),
        WabaId varchar(40) NOT NULL CONSTRAINT DF_WASV_WabaId DEFAULT (''),
        PhoneNumberId varchar(40) NOT NULL CONSTRAINT DF_WASV_PhoneNumberId DEFAULT (''),
        Purpose varchar(80) NOT NULL,
        ProtectedValue nvarchar(max) NOT NULL,
        ExpiresAtUtc datetime2(3) NULL,
        RevokedAtUtc datetime2(3) NULL,
        CreatedAtUtc datetime2(3) NOT NULL,
        ModifiedAtUtc datetime2(3) NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_WhatsAppSecureVault PRIMARY KEY (SecretReference),
        CONSTRAINT CK_WASV_SecretType CHECK (SecretType IN ('CREDENTIAL','PHONE_PIN')),
        CONSTRAINT FK_WASV_Onboarding FOREIGN KEY (IdOnboarding) REFERENCES dbo.WhatsAppEmbeddedOnboarding(IdOnboarding),
        CONSTRAINT FK_WASV_Base FOREIGN KEY (IdBase) REFERENCES dbo.bases(id)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WhatsAppSecureVault') AND name = N'IX_WASV_Context')
    CREATE INDEX IX_WASV_Context ON dbo.WhatsAppSecureVault(IdBase,IdOnboarding,WabaId,PhoneNumberId,SecretType) INCLUDE (ExpiresAtUtc,RevokedAtUtc);
GO

/*
  Rollback manual, únicamente si se confirma que no existen onboardings/credenciales útiles:
  DROP TABLE dbo.WhatsAppSecureVault;
  DROP TABLE dbo.WhatsAppPhoneOwnership;
  DROP TABLE dbo.WhatsAppWabaOwnership;
  DROP TABLE dbo.WhatsAppEmbeddedOnboarding;
  No ejecutar este bloque como parte del deploy normal.
*/

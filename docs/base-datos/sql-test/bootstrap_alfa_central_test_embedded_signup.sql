/*
  BOOTSTRAP EXCLUSIVO DE TEST — WhatsApp Embedded Signup ES-1.6

  Este archivo NO es una migración productiva y NO crea la base automáticamente.
  El operador debe crear previamente ALFA_CENTRAL_TEST en un SQL Server DEV/LOCAL
  autorizado, seleccionarla como catálogo actual y recién entonces ejecutar este archivo.

  Nunca ejecutar en ALFA_CENTRAL ni contra 10.8.0.31 / ALFA_CENTRAL.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @Catalogo sysname = DB_NAME();

IF UPPER(@Catalogo) = N'ALFA_CENTRAL'
    THROW 51000, 'SEGURIDAD: este bootstrap se niega a ejecutarse en ALFA_CENTRAL.', 1;

IF UPPER(@Catalogo) NOT LIKE N'%TEST%'
   AND UPPER(@Catalogo) NOT LIKE N'%DEV%'
   AND UPPER(@Catalogo) NOT LIKE N'%LOCAL%'
    THROW 51001, 'SEGURIDAD: el catálogo actual debe contener TEST, DEV o LOCAL.', 1;

IF UPPER(@Catalogo) <> N'ALFA_CENTRAL_TEST'
    THROW 51002, 'SEGURIDAD: este bootstrap específico debe ejecutarse únicamente en ALFA_CENTRAL_TEST.', 1;
GO

/* Contrato mínimo: ES solo referencia dbo.bases(id). Nombre existe para identificar fixtures. */
IF OBJECT_ID(N'dbo.bases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bases
    (
        id int NOT NULL,
        nombre nvarchar(100) NOT NULL,
        CONSTRAINT PK_bases PRIMARY KEY (id),
        CONSTRAINT UQ_bases_nombre UNIQUE (nombre)
    );
END;
ELSE IF COL_LENGTH(N'dbo.bases', N'id') IS NULL OR COL_LENGTH(N'dbo.bases', N'nombre') IS NULL
    THROW 51003, 'dbo.bases existente no cumple el contrato mínimo id/nombre.', 1;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 1900000001)
    INSERT dbo.bases (id, nombre) VALUES (1900000001, N'ES_TEST_TENANT_A');
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 1900000001 AND nombre = N'ES_TEST_TENANT_A')
    THROW 51004, 'El IdBase fixture 1900000001 ya existe con otra identidad.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 1900000002)
    INSERT dbo.bases (id, nombre) VALUES (1900000002, N'ES_TEST_TENANT_B');
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 1900000002 AND nombre = N'ES_TEST_TENANT_B')
    THROW 51005, 'El IdBase fixture 1900000002 ya existe con otra identidad.', 1;
GO

/* Esquema ES central: mismo diseño que el script de referencia 2026-08-25-001. */
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

SELECT DB_NAME() AS CatalogoValidado,
       (SELECT COUNT(*) FROM dbo.bases WHERE id IN (1900000001, 1900000002)) AS FixturesBase,
       OBJECT_ID(N'dbo.WhatsAppEmbeddedOnboarding', N'U') AS WhatsAppEmbeddedOnboarding,
       OBJECT_ID(N'dbo.WhatsAppWabaOwnership', N'U') AS WhatsAppWabaOwnership,
       OBJECT_ID(N'dbo.WhatsAppPhoneOwnership', N'U') AS WhatsAppPhoneOwnership,
       OBJECT_ID(N'dbo.WhatsAppSecureVault', N'U') AS WhatsAppSecureVault;
GO

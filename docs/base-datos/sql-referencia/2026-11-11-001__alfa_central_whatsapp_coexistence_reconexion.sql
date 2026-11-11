/* Ejecutar en la base CENTRAL configurada para Embedded Signup (no en las bases cliente).
   Conserva números, ownership, credenciales y conversaciones existentes. Idempotente. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WhatsAppEmbeddedOnboarding', N'U') IS NULL
BEGIN
    ROLLBACK;
    THROW 50000, 'Falta el esquema central de WhatsApp Embedded Signup.', 1;
END;
IF COL_LENGTH(N'dbo.WhatsAppEmbeddedOnboarding', N'ReconnectNumeroId') IS NULL
    ALTER TABLE dbo.WhatsAppEmbeddedOnboarding ADD ReconnectNumeroId int NULL;
IF COL_LENGTH(N'dbo.WhatsAppEmbeddedOnboarding', N'ReconnectPhoneNumberId') IS NULL
    ALTER TABLE dbo.WhatsAppEmbeddedOnboarding ADD ReconnectPhoneNumberId nvarchar(80) NULL;
IF COL_LENGTH(N'dbo.WhatsAppEmbeddedOnboarding', N'ReconnectWabaId') IS NULL
    ALTER TABLE dbo.WhatsAppEmbeddedOnboarding ADD ReconnectWabaId nvarchar(80) NULL;
IF OBJECT_ID(N'dbo.WhatsAppCoexistenceConnection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WhatsAppCoexistenceConnection
    (
        IdBase int NOT NULL,
        WabaId nvarchar(80) NOT NULL,
        State varchar(20) NOT NULL,
        Reason nvarchar(100) NOT NULL,
        EventTimestampUtc datetime2(7) NOT NULL,
        ModifiedAtUtc datetime2(7) NOT NULL,
        CONSTRAINT PK_WhatsAppCoexistenceConnection PRIMARY KEY (IdBase, WabaId),
        CONSTRAINT CK_WhatsAppCoexistenceConnection_State CHECK (State IN ('CONNECTED', 'RECOVERING', 'DISCONNECTED'))
    );
END;
COMMIT;

/*
  ES-2 - Tracking central de sync inicial de WhatsApp Business App Coexistence (smb_app_data).
  Destino exclusivo: ALFA_CENTRAL.
  Script idempotente de referencia/versionado. NO es ejecutado automáticamente por AlfaCore.

  Una fila por (IdBase, PhoneNumberId, SyncType) -- esa PK compuesta ES la garantía one-shot: sólo
  puede existir una solicitud de history y una de smb_app_state_sync por número, para siempre,
  independientemente de reinicios/reprocesamiento/redelivery de webhooks o de cuántos onboardings
  distintos haya tenido esa base a lo largo del tiempo.
*/
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WhatsAppEmbeddedCoexistenceSync
    (
        IdBase int NOT NULL,
        PhoneNumberId varchar(40) NOT NULL,
        SyncType varchar(30) NOT NULL,
        IdOnboarding uniqueidentifier NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_WAECS_Status DEFAULT ('PENDING'),
        RequestId varchar(120) NOT NULL CONSTRAINT DF_WAECS_RequestId DEFAULT (''),
        RequestedAtUtc datetime2(3) NULL,
        CompletedAtUtc datetime2(3) NULL,
        ErrorCode varchar(80) NOT NULL CONSTRAINT DF_WAECS_ErrorCode DEFAULT (''),
        ErrorSummary nvarchar(500) NOT NULL CONSTRAINT DF_WAECS_ErrorSummary DEFAULT (N''),
        FechaAltaUtc datetime2(3) NOT NULL,
        FechaModificacionUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_WhatsAppEmbeddedCoexistenceSync PRIMARY KEY (IdBase, PhoneNumberId, SyncType),
        CONSTRAINT FK_WAECS_Base FOREIGN KEY (IdBase) REFERENCES dbo.bases(id),
        CONSTRAINT FK_WAECS_Onboarding FOREIGN KEY (IdOnboarding) REFERENCES dbo.WhatsAppEmbeddedOnboarding(IdOnboarding),
        CONSTRAINT CK_WAECS_SyncType CHECK (SyncType IN ('HISTORY','SMB_APP_STATE_SYNC')),
        CONSTRAINT CK_WAECS_Status CHECK (Status IN ('PENDING','REQUESTED','IN_PROGRESS','COMPLETED','DECLINED','FAILED','EXPIRED'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync') AND name = N'IX_WAECS_IdBase')
    CREATE INDEX IX_WAECS_IdBase ON dbo.WhatsAppEmbeddedCoexistenceSync(IdBase);
GO

/*
  Rollback manual, únicamente si se confirma que no existe tracking útil:
  DROP TABLE dbo.WhatsAppEmbeddedCoexistenceSync;
  No ejecutar este bloque como parte del deploy normal.
*/

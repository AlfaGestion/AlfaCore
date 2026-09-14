/*
  ES-2 - Tracking central de sync inicial de WhatsApp Business App Coexistence (smb_app_data).
  Destino exclusivo: ALFA_CENTRAL.
  Script idempotente de referencia/versionado. NO es ejecutado automáticamente por AlfaCore.

  Una fila por (IdBase, IdOnboarding, PhoneNumberId, SyncType) -- esa PK compuesta ES la garantía
  one-shot, pero POR INTENTO DE ONBOARDING, no para siempre por número: Meta permite un history sync
  nuevo después de un offboard real + un nuevo consentimiento en un onboarding posterior sobre el mismo
  número. Reinicios/reprocesamiento/redelivery de webhooks dentro del MISMO onboarding nunca duplican
  la solicitud; las filas de onboardings anteriores para el mismo número se conservan siempre como
  historial (nunca se eliminan).

  CORRECCIÓN 2026-09-14: la PK original de este script (IdBase, PhoneNumberId, SyncType) era one-shot
  PARA SIEMPRE por número, lo cual bloqueaba incorrectamente un re-onboarding legítimo tras un offboard
  real. Ver 2026-09-14-001__alfa_central_whatsapp_coexistence_sync_pk_fix.sql para la migración sobre
  una tabla que ya fue creada con la PK vieja.
*/
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.WhatsAppEmbeddedCoexistenceSync', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WhatsAppEmbeddedCoexistenceSync
    (
        IdBase int NOT NULL,
        IdOnboarding uniqueidentifier NOT NULL,
        PhoneNumberId varchar(40) NOT NULL,
        SyncType varchar(30) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_WAECS_Status DEFAULT ('PENDING'),
        RequestId varchar(120) NOT NULL CONSTRAINT DF_WAECS_RequestId DEFAULT (''),
        RequestedAtUtc datetime2(3) NULL,
        CompletedAtUtc datetime2(3) NULL,
        ErrorCode varchar(80) NOT NULL CONSTRAINT DF_WAECS_ErrorCode DEFAULT (''),
        ErrorSummary nvarchar(500) NOT NULL CONSTRAINT DF_WAECS_ErrorSummary DEFAULT (N''),
        FechaAltaUtc datetime2(3) NOT NULL,
        FechaModificacionUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_WhatsAppEmbeddedCoexistenceSync PRIMARY KEY (IdBase, IdOnboarding, PhoneNumberId, SyncType),
        CONSTRAINT FK_WAECS_Base FOREIGN KEY (IdBase) REFERENCES dbo.bases(id),
        CONSTRAINT FK_WAECS_Onboarding FOREIGN KEY (IdOnboarding) REFERENCES dbo.WhatsAppEmbeddedOnboarding(IdOnboarding),
        CONSTRAINT CK_WAECS_SyncType CHECK (SyncType IN ('HISTORY','SMB_APP_STATE_SYNC')),
        CONSTRAINT CK_WAECS_Status CHECK (Status IN ('PENDING','REQUESTED','IN_PROGRESS','COMPLETED','DECLINED','FAILED','EXPIRED'))
    );
END;
GO

-- IdBase lidera la PK (y por lo tanto el índice clustered), así que una búsqueda por sólo IdBase
-- (GetForBaseAsync) ya puede hacer seek sobre la PK sin un índice adicional -- IX_WAECS_IdBase quedó
-- redundante tras la corrección de PK y no se crea más (ver script de fix para instalaciones previas).

/*
  Rollback manual, únicamente si se confirma que no existe tracking útil:
  DROP TABLE dbo.WhatsAppEmbeddedCoexistenceSync;
  No ejecutar este bloque como parte del deploy normal.
*/

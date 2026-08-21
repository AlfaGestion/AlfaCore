-- Guardia agregada 2026-08-21: este script solo altera CONV_WHATSAPP_NUMEROS, pero una base cuyo
-- FECHAUPDATE_CORE haya saltado 2026-08-10-003__conversaciones_whatsapp_multinumero.sql (ej. restaurada
-- desde un backup/plantilla vieja) llega hasta acá sin tener la tabla creada, y el ALTER TABLE de más
-- abajo tira error y bloquea el resto de la cola de actualizaciones. Se la crea acá también si falta,
-- con la misma definición que el script original, para que este script sea autosuficiente.
IF OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMEROS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_WHATSAPP_NUMEROS
    (
        IdNumero               int IDENTITY(1,1) NOT NULL,
        PhoneNumberId          nvarchar(50)  NOT NULL,
        Nombre                 nvarchar(100) NOT NULL,
        Activo                 bit           NOT NULL CONSTRAINT DF_CONV_WHATSAPP_NUMEROS_Activo DEFAULT (1),
        FechaHora_Grabacion    datetime      NOT NULL CONSTRAINT DF_CONV_WHATSAPP_NUMEROS_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime      NULL,
        CONSTRAINT PK_CONV_WHATSAPP_NUMEROS PRIMARY KEY CLUSTERED (IdNumero),
        CONSTRAINT UQ_CONV_WHATSAPP_NUMEROS_PhoneNumberId UNIQUE (PhoneNumberId)
    );
END;
GO

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebSessionMode') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebSessionMode nvarchar(30) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebPhoneNumber') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebPhoneNumber nvarchar(50) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebSessionStatus') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebSessionStatus nvarchar(30) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebDisplayName') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebDisplayName nvarchar(100) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebInstanceName') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebInstanceName nvarchar(100) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebPairingToken') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebPairingToken nvarchar(100) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebPairingCode') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebPairingCode nvarchar(50) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebPairingQrPayload') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebPairingQrPayload nvarchar(max) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebPairingGeneratedAtUtc') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebPairingGeneratedAtUtc datetime NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebPairingExpiresAtUtc') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebPairingExpiresAtUtc datetime NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebRuntimeState') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebRuntimeState nvarchar(50) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebLastError') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebLastError nvarchar(500) NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebWorkerProcessId') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebWorkerProcessId int NULL;

IF COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WebRuntimeUpdatedAtUtc') IS NULL
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WebRuntimeUpdatedAtUtc datetime NULL;
GO

UPDATE dbo.CONV_WHATSAPP_NUMEROS
SET
    WebSessionMode = ISNULL(NULLIF(LTRIM(RTRIM(WebSessionMode)), ''), 'QR'),
    WebSessionStatus = ISNULL(NULLIF(LTRIM(RTRIM(WebSessionStatus)), ''), 'DISCONNECTED')
WHERE
    ISNULL(NULLIF(LTRIM(RTRIM(WebSessionMode)), ''), '') = ''
    OR ISNULL(NULLIF(LTRIM(RTRIM(WebSessionStatus)), ''), '') = '';

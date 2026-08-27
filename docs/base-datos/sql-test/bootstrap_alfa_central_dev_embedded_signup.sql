/*
  BOOTSTRAP EXCLUSIVO DE STAGING/DEV — WhatsApp Embedded Signup.

  Ejecutar con sqlcmd desde esta carpeta porque incorpora el esquema de referencia
  mediante :r. No pertenece a App_Data/updates y nunca debe ejecutarse en producción.
*/
:setvar ExpectedDatabase "ALFA_CENTRAL_DEV"

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() <> N'$(ExpectedDatabase)'
    THROW 51100, 'SEGURIDAD: este bootstrap solo puede ejecutarse en ALFA_CENTRAL_DEV.', 1;

IF UPPER(DB_NAME()) = N'ALFA_CENTRAL'
    THROW 51101, 'SEGURIDAD: ejecución rechazada en ALFA_CENTRAL.', 1;
GO

IF OBJECT_ID(N'dbo.bases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bases
    (
        id int NOT NULL,
        idcliente nvarchar(50) NULL,
        nombre nvarchar(100) NOT NULL,
        dbserver nvarchar(255) NULL,
        dbname nvarchar(255) NULL,
        dbuser nvarchar(255) NULL,
        dbpassword nvarchar(500) NULL,
        WebhookToken varchar(64) NULL,
        CONSTRAINT PK_bases PRIMARY KEY (id),
        CONSTRAINT UQ_bases_nombre UNIQUE (nombre)
    );
END;
ELSE IF COL_LENGTH(N'dbo.bases', N'id') IS NULL OR COL_LENGTH(N'dbo.bases', N'nombre') IS NULL
    THROW 51102, 'dbo.bases no cumple el contrato mínimo id/nombre.', 1;
GO

IF COL_LENGTH(N'dbo.bases', N'idcliente') IS NULL ALTER TABLE dbo.bases ADD idcliente nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.bases', N'dbserver') IS NULL ALTER TABLE dbo.bases ADD dbserver nvarchar(255) NULL;
IF COL_LENGTH(N'dbo.bases', N'dbname') IS NULL ALTER TABLE dbo.bases ADD dbname nvarchar(255) NULL;
IF COL_LENGTH(N'dbo.bases', N'dbuser') IS NULL ALTER TABLE dbo.bases ADD dbuser nvarchar(255) NULL;
IF COL_LENGTH(N'dbo.bases', N'dbpassword') IS NULL ALTER TABLE dbo.bases ADD dbpassword nvarchar(500) NULL;
IF COL_LENGTH(N'dbo.bases', N'WebhookToken') IS NULL ALTER TABLE dbo.bases ADD WebhookToken varchar(64) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.bases') AND name = N'UX_bases_WebhookToken')
    CREATE UNIQUE INDEX UX_bases_WebhookToken ON dbo.bases(WebhookToken) WHERE WebhookToken IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 84)
    INSERT dbo.bases (id, idcliente, nombre, dbserver, dbname)
    VALUES (84, N'ES_LOCAL', N'ES_DEV_BASE_84', N'(localdb)\MSSQLLocalDB', N'ALFACORE_ES_TENANT_DEV');
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 84 AND nombre = N'ES_DEV_BASE_84')
    THROW 51103, 'El IdBase 84 ya existe con otra identidad en DEV.', 1;

UPDATE dbo.bases
SET idcliente = COALESCE(NULLIF(idcliente, N''), N'ES_LOCAL'),
    dbserver = COALESCE(NULLIF(dbserver, N''), N'(localdb)\MSSQLLocalDB'),
    dbname = COALESCE(NULLIF(dbname, N''), N'ALFACORE_ES_TENANT_DEV')
WHERE id = 84
  AND nombre = N'ES_DEV_BASE_84';
GO

/* Mantiene el esquema, constraints e índices del diseño central oficial. */
:r ..\sql-referencia\2026-08-25-001__alfa_central_whatsapp_embedded_signup.sql

IF DB_NAME() <> N'ALFA_CENTRAL_DEV'
    THROW 51104, 'El catálogo cambió durante la ejecución.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 84 AND nombre = N'ES_DEV_BASE_84')
    THROW 51105, 'No se pudo verificar el seed supervisado Base 84.', 1;

SELECT DB_NAME() AS Catalogo,
       (SELECT nombre FROM dbo.bases WHERE id = 84) AS Base84,
       OBJECT_ID(N'dbo.WhatsAppEmbeddedOnboarding', N'U') AS WhatsAppEmbeddedOnboarding,
       OBJECT_ID(N'dbo.WhatsAppWabaOwnership', N'U') AS WhatsAppWabaOwnership,
       OBJECT_ID(N'dbo.WhatsAppPhoneOwnership', N'U') AS WhatsAppPhoneOwnership,
       OBJECT_ID(N'dbo.WhatsAppSecureVault', N'U') AS WhatsAppSecureVault;
GO

/*
Actualiza Conversaciones para canales sociales.

Objetivo:
- permitir canales INSTAGRAM y FACEBOOK sin tocar el flujo de WHATSAPP
- agregar catálogo de canales y cuentas vinculadas por proveedor
- agregar campos genéricos para identificadores externos
*/

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.CONV_CANALES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_CANALES
    (
        Canal nvarchar(20) NOT NULL,
        Nombre nvarchar(80) NOT NULL,
        Activo bit NOT NULL CONSTRAINT DF_CONV_CANALES_Activo DEFAULT (1),
        Orden int NOT NULL CONSTRAINT DF_CONV_CANALES_Orden DEFAULT (0),
        Color nvarchar(30) NULL,
        Icono nvarchar(40) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_CANALES_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_CANALES PRIMARY KEY CLUSTERED (Canal)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.CONV_CANALES WHERE Canal = N'WHATSAPP')
    INSERT INTO dbo.CONV_CANALES (Canal, Nombre, Orden, Color, Icono) VALUES (N'WHATSAPP', N'WhatsApp', 10, N'#22c55e', N'bi-whatsapp');
IF NOT EXISTS (SELECT 1 FROM dbo.CONV_CANALES WHERE Canal = N'INSTAGRAM')
    INSERT INTO dbo.CONV_CANALES (Canal, Nombre, Orden, Color, Icono) VALUES (N'INSTAGRAM', N'Instagram', 20, N'#ec4899', N'bi-instagram');
IF NOT EXISTS (SELECT 1 FROM dbo.CONV_CANALES WHERE Canal = N'FACEBOOK')
    INSERT INTO dbo.CONV_CANALES (Canal, Nombre, Orden, Color, Icono) VALUES (N'FACEBOOK', N'Facebook', 30, N'#2563eb', N'bi-facebook');
IF NOT EXISTS (SELECT 1 FROM dbo.CONV_CANALES WHERE Canal = N'INTERNO')
    INSERT INTO dbo.CONV_CANALES (Canal, Nombre, Orden, Color, Icono) VALUES (N'INTERNO', N'Chat interno', 90, N'#38bdf8', N'bi-people-fill');

IF OBJECT_ID(N'dbo.CONV_CUENTAS_CANAL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_CUENTAS_CANAL
    (
        IdCuentaCanal int IDENTITY(1,1) NOT NULL,
        Canal nvarchar(20) NOT NULL,
        NombreCuenta nvarchar(120) NOT NULL,
        IdentificadorExterno nvarchar(150) NULL,
        FacebookPageId nvarchar(150) NULL,
        AccessToken nvarchar(max) NULL,
        RefreshToken nvarchar(max) NULL,
        TokenVence datetime NULL,
        Activa bit NOT NULL CONSTRAINT DF_CONV_CUENTAS_CANAL_Activa DEFAULT (1),
        PayloadJson nvarchar(max) NULL,
        UsuarioAccion nvarchar(50) NULL,
        SistemaAccion nvarchar(50) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_CUENTAS_CANAL_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_CUENTAS_CANAL PRIMARY KEY CLUSTERED (IdCuentaCanal),
        CONSTRAINT FK_CONV_CUENTAS_CANAL_CANAL FOREIGN KEY (Canal)
            REFERENCES dbo.CONV_CANALES (Canal)
    );
END;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'IdCuentaCanal') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD IdCuentaCanal int NULL;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'IdentificadorExternoContacto') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD IdentificadorExternoContacto nvarchar(150) NULL;

IF COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'IdentificadorExternoConversacion') IS NULL
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD IdentificadorExternoConversacion nvarchar(150) NULL;

IF COL_LENGTH(N'dbo.CONV_MENSAJES', N'MessageIdExterno') IS NULL
    ALTER TABLE dbo.CONV_MENSAJES ADD MessageIdExterno nvarchar(150) NULL;

IF COL_LENGTH(N'dbo.CONV_MENSAJES', N'ReplyToMessageIdExterno') IS NULL
    ALTER TABLE dbo.CONV_MENSAJES ADD ReplyToMessageIdExterno nvarchar(150) NULL;

IF OBJECT_ID(N'dbo.FK_CONV_CONVERSACIONES_CUENTA_CANAL', N'F') IS NULL
   AND COL_LENGTH(N'dbo.CONV_CONVERSACIONES', N'IdCuentaCanal') IS NOT NULL
BEGIN
    ALTER TABLE dbo.CONV_CONVERSACIONES WITH NOCHECK
    ADD CONSTRAINT FK_CONV_CONVERSACIONES_CUENTA_CANAL FOREIGN KEY (IdCuentaCanal)
        REFERENCES dbo.CONV_CUENTAS_CANAL (IdCuentaCanal);
END;

DECLARE @ckName sysname;

SELECT TOP (1) @ckName = cc.name
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES')
  AND cc.definition LIKE N'%WHATSAPP%'
  AND cc.definition LIKE N'%INTERNO%';

IF @ckName IS NOT NULL
BEGIN
    DECLARE @dropSql nvarchar(max) =
        N'ALTER TABLE dbo.CONV_CONVERSACIONES DROP CONSTRAINT ' + QUOTENAME(@ckName) + N';';
    EXEC sp_executesql @dropSql;
END;

IF OBJECT_ID(N'dbo.CK_CONV_CONVERSACIONES_Canal', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_CONVERSACIONES WITH NOCHECK
    ADD CONSTRAINT CK_CONV_CONVERSACIONES_Canal
        CHECK (Canal IN (N'WHATSAPP', N'INSTAGRAM', N'FACEBOOK', N'INTERNO'));
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_CONVERSACIONES_Canal_Externo' AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES'))
    CREATE NONCLUSTERED INDEX IX_CONV_CONVERSACIONES_Canal_Externo
        ON dbo.CONV_CONVERSACIONES (Canal, IdentificadorExternoContacto, FechaHoraUltimoMensaje DESC)
        INCLUDE (IdCuentaCanal, NombreVisible, CodigoEstado);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_CUENTAS_CANAL_Canal_Externo' AND object_id = OBJECT_ID(N'dbo.CONV_CUENTAS_CANAL'))
    CREATE NONCLUSTERED INDEX IX_CONV_CUENTAS_CANAL_Canal_Externo
        ON dbo.CONV_CUENTAS_CANAL (Canal, IdentificadorExterno)
        INCLUDE (NombreCuenta, Activa);


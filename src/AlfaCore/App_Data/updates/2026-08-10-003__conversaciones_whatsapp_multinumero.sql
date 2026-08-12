/*
Conversaciones: soporte para varios numeros de WhatsApp Business, cada uno atendido por uno o
varios usuarios del sistema, mas un flag de "administrador de conversaciones" (ve/responde por
cualquier numero).

Etapa 1 (esta migracion): solo modelo de datos, aditivo. No cambia el comportamiento actual --
el webhook y el envio saliente todavia usan el unico CONV_WHATSAPP_PHONE_NUMBER_ID configurado en
TA_CONFIGURACION. Se deja sembrada la primera fila de CONV_WHATSAPP_NUMEROS con ese valor para que
la pantalla de configuracion ya tenga algo para mostrar/editar.

Idempotente -- se puede reintentar sin romper datos.
*/

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

IF OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMERO_USUARIOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_WHATSAPP_NUMERO_USUARIOS
    (
        IdNumero            int          NOT NULL,
        Usuario             nvarchar(50) NOT NULL,
        Sistema             nvarchar(50) NOT NULL,
        FechaHora_Grabacion datetime     NOT NULL CONSTRAINT DF_CONV_WHATSAPP_NUMERO_USUARIOS_FhGrab DEFAULT (GETDATE()),
        CONSTRAINT PK_CONV_WHATSAPP_NUMERO_USUARIOS PRIMARY KEY CLUSTERED (IdNumero, Usuario, Sistema),
        CONSTRAINT FK_CONV_WHATSAPP_NUMERO_USUARIOS_NUMERO FOREIGN KEY (IdNumero)
            REFERENCES dbo.CONV_WHATSAPP_NUMEROS (IdNumero)
    );
END;
GO

IF OBJECT_ID(N'dbo.CONV_ADMINISTRADORES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ADMINISTRADORES
    (
        Usuario             nvarchar(50) NOT NULL,
        Sistema             nvarchar(50) NOT NULL,
        FechaHora_Grabacion datetime     NOT NULL CONSTRAINT DF_CONV_ADMINISTRADORES_FhGrab DEFAULT (GETDATE()),
        CONSTRAINT PK_CONV_ADMINISTRADORES PRIMARY KEY CLUSTERED (Usuario, Sistema)
    );
END;
GO

-- FKs opcionales a TA_USUARIOS (Nombre, Sistema): solo si la clave compuesta existe tal cual,
-- igual criterio que ya usa 2026-05-17-999__conversaciones_modelo_base.sql.
IF OBJECT_ID(N'dbo.TA_USUARIOS', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.TA_USUARIOS', 'NOMBRE') IS NOT NULL
   AND COL_LENGTH('dbo.TA_USUARIOS', 'SISTEMA') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.indexes i
       INNER JOIN sys.index_columns ic
           ON ic.object_id = i.object_id
          AND ic.index_id = i.index_id
       INNER JOIN sys.columns c
           ON c.object_id = ic.object_id
          AND c.column_id = ic.column_id
       WHERE i.object_id = OBJECT_ID(N'dbo.TA_USUARIOS')
         AND i.is_hypothetical = 0
         AND (i.is_primary_key = 1 OR i.is_unique_constraint = 1 OR i.is_unique = 1)
       GROUP BY i.object_id, i.index_id
       HAVING COUNT(*) = 2
          AND MAX(CASE WHEN UPPER(c.name) = N'NOMBRE' THEN 1 ELSE 0 END) = 1
          AND MAX(CASE WHEN UPPER(c.name) = N'SISTEMA' THEN 1 ELSE 0 END) = 1
   )
   AND OBJECT_ID(N'dbo.FK_CONV_WHATSAPP_NUMERO_USUARIOS_USUARIO', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_WHATSAPP_NUMERO_USUARIOS WITH NOCHECK
    ADD CONSTRAINT FK_CONV_WHATSAPP_NUMERO_USUARIOS_USUARIO FOREIGN KEY (Usuario, Sistema)
        REFERENCES dbo.TA_USUARIOS (NOMBRE, SISTEMA);
END;
GO

IF OBJECT_ID(N'dbo.TA_USUARIOS', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.TA_USUARIOS', 'NOMBRE') IS NOT NULL
   AND COL_LENGTH('dbo.TA_USUARIOS', 'SISTEMA') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.indexes i
       INNER JOIN sys.index_columns ic
           ON ic.object_id = i.object_id
          AND ic.index_id = i.index_id
       INNER JOIN sys.columns c
           ON c.object_id = ic.object_id
          AND c.column_id = ic.column_id
       WHERE i.object_id = OBJECT_ID(N'dbo.TA_USUARIOS')
         AND i.is_hypothetical = 0
         AND (i.is_primary_key = 1 OR i.is_unique_constraint = 1 OR i.is_unique = 1)
       GROUP BY i.object_id, i.index_id
       HAVING COUNT(*) = 2
          AND MAX(CASE WHEN UPPER(c.name) = N'NOMBRE' THEN 1 ELSE 0 END) = 1
          AND MAX(CASE WHEN UPPER(c.name) = N'SISTEMA' THEN 1 ELSE 0 END) = 1
   )
   AND OBJECT_ID(N'dbo.FK_CONV_ADMINISTRADORES_USUARIO', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_ADMINISTRADORES WITH NOCHECK
    ADD CONSTRAINT FK_CONV_ADMINISTRADORES_USUARIO FOREIGN KEY (Usuario, Sistema)
        REFERENCES dbo.TA_USUARIOS (NOMBRE, SISTEMA);
END;
GO

-- Columna en la conversacion para fijar, una unica vez al crearse, a que numero de WhatsApp
-- pertenece (la ventana de 24 h de Meta es por par numero-cliente). Nula para lo existente y para
-- canales que no son WhatsApp; el envio/webhook todavia no la usan (etapa 2).
IF COL_LENGTH('dbo.CONV_CONVERSACIONES', 'IdNumeroWhatsApp') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_CONVERSACIONES ADD IdNumeroWhatsApp int NULL;
END;
GO

IF OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMEROS', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_CONV_CONVERSACIONES_NUMERO_WHATSAPP', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_CONVERSACIONES WITH NOCHECK
    ADD CONSTRAINT FK_CONV_CONVERSACIONES_NUMERO_WHATSAPP FOREIGN KEY (IdNumeroWhatsApp)
        REFERENCES dbo.CONV_WHATSAPP_NUMEROS (IdNumero);
END;
GO

-- Semilla: si ya hay un CONV_WHATSAPP_PHONE_NUMBER_ID configurado (el unico numero de hoy) y
-- todavia no esta en CONV_WHATSAPP_NUMEROS, se agrega como primera fila para que el cliente que
-- ya tenia un solo numero siga viendolo en la pantalla nueva sin tener que cargarlo de nuevo.
IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMEROS'))
   AND EXISTS (
       SELECT 1 FROM dbo.TA_CONFIGURACION
       WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'CONV_WHATSAPP_PHONE_NUMBER_ID'
         AND LTRIM(RTRIM(ISNULL(VALOR, ''))) <> ''
   )
BEGIN
    INSERT INTO dbo.CONV_WHATSAPP_NUMEROS (PhoneNumberId, Nombre)
    SELECT LTRIM(RTRIM(cfg.VALOR)), N'Número principal'
    FROM dbo.TA_CONFIGURACION cfg
    WHERE UPPER(LTRIM(RTRIM(cfg.CLAVE))) = N'CONV_WHATSAPP_PHONE_NUMBER_ID'
      AND LTRIM(RTRIM(ISNULL(cfg.VALOR, ''))) <> ''
      AND NOT EXISTS (
          SELECT 1 FROM dbo.CONV_WHATSAPP_NUMEROS n
          WHERE n.PhoneNumberId = LTRIM(RTRIM(cfg.VALOR))
      );
END;
GO

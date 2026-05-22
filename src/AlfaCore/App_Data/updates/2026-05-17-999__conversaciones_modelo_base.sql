/*
Modelo base del modulo Conversaciones

Objetivo:
- crear las tablas principales si no existen
- no borrar ni recrear objetos existentes
- ser tolerante con bases donde falten objetos auxiliares

Notas:
- las FK opcionales se agregan solo si el objeto real y sus columnas existen
- no se asume tipo tecnico por prefijo
*/

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.CONV_ESTADOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ESTADOS
    (
        CodigoEstado nvarchar(20) NOT NULL,
        Descripcion nvarchar(100) NOT NULL,
        Orden int NOT NULL CONSTRAINT DF_CONV_ESTADOS_Orden DEFAULT (0),
        EsCerrado bit NOT NULL CONSTRAINT DF_CONV_ESTADOS_EsCerrado DEFAULT (0),
        Activo bit NOT NULL CONSTRAINT DF_CONV_ESTADOS_Activo DEFAULT (1),
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_ESTADOS_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_ESTADOS PRIMARY KEY CLUSTERED (CodigoEstado)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.CONV_ESTADOS WHERE CodigoEstado = N'ABIERTA')
    INSERT INTO dbo.CONV_ESTADOS (CodigoEstado, Descripcion, Orden, EsCerrado) VALUES (N'ABIERTA', N'Abierta', 10, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.CONV_ESTADOS WHERE CodigoEstado = N'PENDIENTE')
    INSERT INTO dbo.CONV_ESTADOS (CodigoEstado, Descripcion, Orden, EsCerrado) VALUES (N'PENDIENTE', N'Pendiente', 20, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.CONV_ESTADOS WHERE CodigoEstado = N'EN_GESTION')
    INSERT INTO dbo.CONV_ESTADOS (CodigoEstado, Descripcion, Orden, EsCerrado) VALUES (N'EN_GESTION', N'En gestion', 30, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.CONV_ESTADOS WHERE CodigoEstado = N'CERRADA')
    INSERT INTO dbo.CONV_ESTADOS (CodigoEstado, Descripcion, Orden, EsCerrado) VALUES (N'CERRADA', N'Cerrada', 40, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.CONV_ESTADOS WHERE CodigoEstado = N'ARCHIVADA')
    INSERT INTO dbo.CONV_ESTADOS (CodigoEstado, Descripcion, Orden, EsCerrado) VALUES (N'ARCHIVADA', N'Archivada', 50, 1);
GO

IF OBJECT_ID(N'dbo.CONV_ETIQUETAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ETIQUETAS
    (
        IdEtiqueta int IDENTITY(1,1) NOT NULL,
        Nombre nvarchar(60) NOT NULL,
        Color nvarchar(20) NULL,
        Activa bit NOT NULL CONSTRAINT DF_CONV_ETIQUETAS_Activa DEFAULT (1),
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_ETIQUETAS_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_ETIQUETAS PRIMARY KEY CLUSTERED (IdEtiqueta),
        CONSTRAINT UQ_CONV_ETIQUETAS_Nombre UNIQUE (Nombre)
    );
END;
GO

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_CONVERSACIONES
    (
        IdConversacion bigint IDENTITY(1,1) NOT NULL,
        Canal nvarchar(20) NOT NULL CONSTRAINT DF_CONV_CONVERSACIONES_Canal DEFAULT (N'WHATSAPP'),
        TelefonoWhatsApp nvarchar(30) NULL,
        NombreVisible nvarchar(120) NULL,
        ClienteCodigo nvarchar(15) NULL,
        ClienteSucursal int NULL,
        IdContacto int NULL,
        CodigoEstado nvarchar(20) NOT NULL,
        IdTecnico nvarchar(4) NULL,
        ResumenUltimoMensaje nvarchar(500) NULL,
        Prioridad nvarchar(15) NULL,
        Bloqueada bit NOT NULL CONSTRAINT DF_CONV_CONVERSACIONES_Bloqueada DEFAULT (0),
        Archivada bit NOT NULL CONSTRAINT DF_CONV_CONVERSACIONES_Archivada DEFAULT (0),
        FechaHoraPrimerMensaje datetime NULL,
        FechaHoraUltimoMensaje datetime NOT NULL CONSTRAINT DF_CONV_CONVERSACIONES_FhUltMsg DEFAULT (GETDATE()),
        FechaHoraCierre datetime NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_CONVERSACIONES_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_CONVERSACIONES PRIMARY KEY CLUSTERED (IdConversacion),
        CONSTRAINT CK_CONV_CONVERSACIONES_Canal CHECK (Canal IN (N'WHATSAPP', N'INTERNO')),
        CONSTRAINT CK_CONV_CONVERSACIONES_Prioridad CHECK (Prioridad IS NULL OR Prioridad IN (N'BAJA', N'MEDIA', N'ALTA', N'URGENTE')),
        CONSTRAINT FK_CONV_CONVERSACIONES_ESTADO FOREIGN KEY (CodigoEstado)
            REFERENCES dbo.CONV_ESTADOS (CodigoEstado)
    );
END;
GO

IF OBJECT_ID(N'dbo.CONV_ASIGNACIONES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ASIGNACIONES
    (
        IdAsignacion bigint IDENTITY(1,1) NOT NULL,
        IdConversacion bigint NOT NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_CONV_ASIGNACIONES_FechaHora DEFAULT (GETDATE()),
        IdTecnico nvarchar(4) NULL,
        UsuarioAccion nvarchar(50) NULL,
        SistemaAccion nvarchar(50) NULL,
        Observaciones nvarchar(500) NULL,
        CONSTRAINT PK_CONV_ASIGNACIONES PRIMARY KEY CLUSTERED (IdAsignacion),
        CONSTRAINT CK_CONV_ASIGNACIONES_Accion CHECK (
            (UsuarioAccion IS NULL AND SistemaAccion IS NULL)
            OR
            (UsuarioAccion IS NOT NULL AND SistemaAccion IS NOT NULL)
        ),
        CONSTRAINT FK_CONV_ASIGNACIONES_CONVERSACION FOREIGN KEY (IdConversacion)
            REFERENCES dbo.CONV_CONVERSACIONES (IdConversacion)
    );
END;
GO

IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_MENSAJES
    (
        IdMensaje bigint IDENTITY(1,1) NOT NULL,
        IdConversacion bigint NOT NULL,
        TelefonoWhatsApp nvarchar(30) NULL,
        WhatsAppMessageId nvarchar(150) NULL,
        WhatsAppReplyToMessageId nvarchar(150) NULL,
        MessageType nvarchar(20) NOT NULL,
        Direction nvarchar(15) NOT NULL,
        EstadoEnvio nvarchar(20) NULL,
        Texto nvarchar(max) NULL,
        PayloadJson nvarchar(max) NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_CONV_MENSAJES_FechaHora DEFAULT (GETDATE()),
        UsuarioAutor nvarchar(50) NULL,
        SistemaAutor nvarchar(50) NULL,
        IdTecnicoAutor nvarchar(4) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_MENSAJES_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_MENSAJES PRIMARY KEY CLUSTERED (IdMensaje),
        CONSTRAINT CK_CONV_MENSAJES_Direction CHECK (Direction IN (N'ENTRANTE', N'SALIENTE', N'NOTA_INTERNA')),
        CONSTRAINT CK_CONV_MENSAJES_MessageType CHECK (MessageType IN (N'TEXT', N'IMAGE', N'DOCUMENT', N'AUDIO', N'VIDEO', N'STICKER', N'LOCATION', N'CONTACT', N'SYSTEM', N'UNKNOWN')),
        CONSTRAINT CK_CONV_MENSAJES_UsuarioAutor CHECK (
            (UsuarioAutor IS NULL AND SistemaAutor IS NULL)
            OR
            (UsuarioAutor IS NOT NULL AND SistemaAutor IS NOT NULL)
        ),
        CONSTRAINT FK_CONV_MENSAJES_CONVERSACION FOREIGN KEY (IdConversacion)
            REFERENCES dbo.CONV_CONVERSACIONES (IdConversacion)
    );
END;
GO

IF OBJECT_ID(N'dbo.CONV_ADJUNTOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_ADJUNTOS
    (
        IdAdjunto bigint IDENTITY(1,1) NOT NULL,
        IdMensaje bigint NOT NULL,
        TipoArchivo nvarchar(20) NOT NULL,
        NombreArchivo nvarchar(255) NULL,
        MimeType nvarchar(100) NULL,
        UrlArchivo nvarchar(500) NULL,
        RutaLocal nvarchar(500) NULL,
        TamanoBytes bigint NULL,
        HashArchivo nvarchar(128) NULL,
        PayloadJson nvarchar(max) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_ADJUNTOS_FhGrab DEFAULT (GETDATE()),
        CONSTRAINT PK_CONV_ADJUNTOS PRIMARY KEY CLUSTERED (IdAdjunto),
        CONSTRAINT FK_CONV_ADJUNTOS_MENSAJE FOREIGN KEY (IdMensaje)
            REFERENCES dbo.CONV_MENSAJES (IdMensaje)
    );
END;
GO

IF OBJECT_ID(N'dbo.CONV_CONVERSACION_ETIQUETAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_CONVERSACION_ETIQUETAS
    (
        IdConversacion bigint NOT NULL,
        IdEtiqueta int NOT NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_CONV_ETQ_FhGrab DEFAULT (GETDATE()),
        CONSTRAINT PK_CONV_CONVERSACION_ETIQUETAS PRIMARY KEY CLUSTERED (IdConversacion, IdEtiqueta),
        CONSTRAINT FK_CONV_CONV_ETQ_CONVERSACION FOREIGN KEY (IdConversacion)
            REFERENCES dbo.CONV_CONVERSACIONES (IdConversacion),
        CONSTRAINT FK_CONV_CONV_ETQ_ETIQUETA FOREIGN KEY (IdEtiqueta)
            REFERENCES dbo.CONV_ETIQUETAS (IdEtiqueta)
    );
END;
GO

IF OBJECT_ID(N'dbo.CONV_WEBHOOK_LOG', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_WEBHOOK_LOG
    (
        IdWebhookLog bigint IDENTITY(1,1) NOT NULL,
        Proveedor nvarchar(30) NOT NULL CONSTRAINT DF_CONV_WEBHOOK_LOG_Proveedor DEFAULT (N'META_WHATSAPP'),
        Evento nvarchar(50) NULL,
        TelefonoWhatsApp nvarchar(30) NULL,
        WhatsAppMessageId nvarchar(150) NULL,
        PayloadJson nvarchar(max) NOT NULL,
        HeaderJson nvarchar(max) NULL,
        ProcesadoOk bit NULL,
        ErrorDescripcion nvarchar(1000) NULL,
        FechaHoraRecepcion datetime NOT NULL CONSTRAINT DF_CONV_WEBHOOK_LOG_FhRecepcion DEFAULT (GETDATE()),
        FechaHoraProcesamiento datetime NULL,
        Intentos int NOT NULL CONSTRAINT DF_CONV_WEBHOOK_LOG_Intentos DEFAULT (0),
        CONSTRAINT PK_CONV_WEBHOOK_LOG PRIMARY KEY CLUSTERED (IdWebhookLog)
    );
END;
GO

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_PLANTILLAS
    (
        IdPlantilla bigint IDENTITY(1,1) NOT NULL,
        NombreVisible nvarchar(120) NOT NULL,
        NombreMeta nvarchar(512) NOT NULL,
        Categoria nvarchar(30) NOT NULL CONSTRAINT DF_CONV_PLANTILLAS_Categoria DEFAULT (N'MARKETING'),
        Idioma nvarchar(20) NOT NULL CONSTRAINT DF_CONV_PLANTILLAS_Idioma DEFAULT (N'es_AR'),
        EncabezadoTexto nvarchar(60) NULL,
        CuerpoTexto nvarchar(max) NOT NULL,
        PieTexto nvarchar(60) NULL,
        EjemplosVariablesJson nvarchar(max) NULL,
        EstadoLocal nvarchar(20) NOT NULL CONSTRAINT DF_CONV_PLANTILLAS_EstadoLocal DEFAULT (N'BORRADOR'),
        EstadoMeta nvarchar(30) NOT NULL CONSTRAINT DF_CONV_PLANTILLAS_EstadoMeta DEFAULT (N'DRAFT'),
        MetaTemplateId nvarchar(100) NULL,
        MetaPayloadJson nvarchar(max) NULL,
        MetaRechazoMotivo nvarchar(500) NULL,
        Activa bit NOT NULL CONSTRAINT DF_CONV_PLANTILLAS_Activa DEFAULT (1),
        UsuarioAccion nvarchar(50) NULL,
        SistemaAccion nvarchar(50) NULL,
        FechaHoraSincronizacion datetime NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_PLANTILLAS_FhGrab DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CONV_PLANTILLAS PRIMARY KEY CLUSTERED (IdPlantilla),
        CONSTRAINT UQ_CONV_PLANTILLAS_NombreMeta_Idioma UNIQUE (NombreMeta, Idioma),
        CONSTRAINT CK_CONV_PLANTILLAS_Categoria CHECK (Categoria IN (N'MARKETING', N'UTILITY')),
        CONSTRAINT CK_CONV_PLANTILLAS_UsuarioAccion CHECK (
            (UsuarioAccion IS NULL AND SistemaAccion IS NULL)
            OR
            (UsuarioAccion IS NOT NULL AND SistemaAccion IS NOT NULL)
        )
    );
END;
GO

IF OBJECT_ID(N'dbo.CONV_STICKERS_FAVORITOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_STICKERS_FAVORITOS
    (
        IdFavorito bigint IDENTITY(1,1) NOT NULL,
        IdAdjunto bigint NOT NULL,
        Nombre nvarchar(120) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_STICKERS_FAVORITOS_FhGrab DEFAULT (GETDATE()),
        CONSTRAINT PK_CONV_STICKERS_FAVORITOS PRIMARY KEY CLUSTERED (IdFavorito),
        CONSTRAINT UQ_CONV_STICKERS_FAVORITOS_Adjunto UNIQUE (IdAdjunto),
        CONSTRAINT FK_CONV_STICKERS_FAVORITOS_ADJUNTO FOREIGN KEY (IdAdjunto)
            REFERENCES dbo.CONV_ADJUNTOS (IdAdjunto)
    );
END;
GO

IF OBJECT_ID(N'dbo.MA_CUENTAS', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.MA_CUENTAS', 'CODIGO') IS NOT NULL
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
       WHERE i.object_id = OBJECT_ID(N'dbo.MA_CUENTAS')
         AND i.is_hypothetical = 0
         AND (i.is_primary_key = 1 OR i.is_unique_constraint = 1 OR i.is_unique = 1)
       GROUP BY i.object_id, i.index_id
       HAVING COUNT(*) = 1
          AND MAX(CASE WHEN UPPER(c.name) = N'CODIGO' THEN 1 ELSE 0 END) = 1
   )
   AND OBJECT_ID(N'dbo.FK_CONV_CONVERSACIONES_CLIENTE', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_CONVERSACIONES WITH NOCHECK
    ADD CONSTRAINT FK_CONV_CONVERSACIONES_CLIENTE FOREIGN KEY (ClienteCodigo)
        REFERENCES dbo.MA_CUENTAS (CODIGO);
END;
GO

IF OBJECT_ID(N'dbo.MA_CONTACTOS', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.MA_CONTACTOS', 'id') IS NOT NULL
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
       WHERE i.object_id = OBJECT_ID(N'dbo.MA_CONTACTOS')
         AND i.is_hypothetical = 0
         AND (i.is_primary_key = 1 OR i.is_unique_constraint = 1 OR i.is_unique = 1)
       GROUP BY i.object_id, i.index_id
       HAVING COUNT(*) = 1
          AND MAX(CASE WHEN UPPER(c.name) = N'ID' THEN 1 ELSE 0 END) = 1
   )
   AND OBJECT_ID(N'dbo.FK_CONV_CONVERSACIONES_CONTACTO', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_CONVERSACIONES WITH NOCHECK
    ADD CONSTRAINT FK_CONV_CONVERSACIONES_CONTACTO FOREIGN KEY (IdContacto)
        REFERENCES dbo.MA_CONTACTOS (id);
END;
GO

IF OBJECT_ID(N'dbo.V_TA_Tecnicos', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.V_TA_Tecnicos', 'IdTecnico') IS NOT NULL
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
       WHERE i.object_id = OBJECT_ID(N'dbo.V_TA_Tecnicos')
         AND i.is_hypothetical = 0
         AND (i.is_primary_key = 1 OR i.is_unique_constraint = 1 OR i.is_unique = 1)
       GROUP BY i.object_id, i.index_id
       HAVING COUNT(*) = 1
          AND MAX(CASE WHEN UPPER(c.name) = N'IDTECNICO' THEN 1 ELSE 0 END) = 1
   )
   AND OBJECT_ID(N'dbo.FK_CONV_CONVERSACIONES_TECNICO', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_CONVERSACIONES WITH NOCHECK
    ADD CONSTRAINT FK_CONV_CONVERSACIONES_TECNICO FOREIGN KEY (IdTecnico)
        REFERENCES dbo.V_TA_Tecnicos (IdTecnico);
END;
GO

IF OBJECT_ID(N'dbo.V_TA_Tecnicos', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.V_TA_Tecnicos', 'IdTecnico') IS NOT NULL
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
       WHERE i.object_id = OBJECT_ID(N'dbo.V_TA_Tecnicos')
         AND i.is_hypothetical = 0
         AND (i.is_primary_key = 1 OR i.is_unique_constraint = 1 OR i.is_unique = 1)
       GROUP BY i.object_id, i.index_id
       HAVING COUNT(*) = 1
          AND MAX(CASE WHEN UPPER(c.name) = N'IDTECNICO' THEN 1 ELSE 0 END) = 1
   )
   AND OBJECT_ID(N'dbo.FK_CONV_ASIGNACIONES_TECNICO', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_ASIGNACIONES WITH NOCHECK
    ADD CONSTRAINT FK_CONV_ASIGNACIONES_TECNICO FOREIGN KEY (IdTecnico)
        REFERENCES dbo.V_TA_Tecnicos (IdTecnico);
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
   AND OBJECT_ID(N'dbo.FK_CONV_ASIGNACIONES_USUARIO_ACCION', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_ASIGNACIONES WITH NOCHECK
    ADD CONSTRAINT FK_CONV_ASIGNACIONES_USUARIO_ACCION FOREIGN KEY (UsuarioAccion, SistemaAccion)
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
   AND OBJECT_ID(N'dbo.FK_CONV_MENSAJES_USUARIO', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_MENSAJES WITH NOCHECK
    ADD CONSTRAINT FK_CONV_MENSAJES_USUARIO FOREIGN KEY (UsuarioAutor, SistemaAutor)
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
   AND OBJECT_ID(N'dbo.FK_CONV_PLANTILLAS_USUARIO', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_PLANTILLAS WITH NOCHECK
    ADD CONSTRAINT FK_CONV_PLANTILLAS_USUARIO FOREIGN KEY (UsuarioAccion, SistemaAccion)
        REFERENCES dbo.TA_USUARIOS (NOMBRE, SISTEMA);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_CONVERSACIONES_Telefono' AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES'))
    CREATE NONCLUSTERED INDEX IX_CONV_CONVERSACIONES_Telefono
        ON dbo.CONV_CONVERSACIONES (TelefonoWhatsApp, FechaHoraUltimoMensaje DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_CONVERSACIONES_Estado_Asignado' AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES'))
    CREATE NONCLUSTERED INDEX IX_CONV_CONVERSACIONES_Estado_Asignado
        ON dbo.CONV_CONVERSACIONES (CodigoEstado, IdTecnico, FechaHoraUltimoMensaje DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_CONVERSACIONES_Cliente' AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES'))
    CREATE NONCLUSTERED INDEX IX_CONV_CONVERSACIONES_Cliente
        ON dbo.CONV_CONVERSACIONES (ClienteCodigo, FechaHoraUltimoMensaje DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_MENSAJES_Conversacion_FechaHora' AND object_id = OBJECT_ID(N'dbo.CONV_MENSAJES'))
    CREATE NONCLUSTERED INDEX IX_CONV_MENSAJES_Conversacion_FechaHora
        ON dbo.CONV_MENSAJES (IdConversacion, FechaHora ASC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_MENSAJES_WhatsAppMessageId' AND object_id = OBJECT_ID(N'dbo.CONV_MENSAJES'))
    CREATE UNIQUE NONCLUSTERED INDEX IX_CONV_MENSAJES_WhatsAppMessageId
        ON dbo.CONV_MENSAJES (WhatsAppMessageId)
        WHERE WhatsAppMessageId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_ASIGNACIONES_Conversacion_Fecha' AND object_id = OBJECT_ID(N'dbo.CONV_ASIGNACIONES'))
    CREATE NONCLUSTERED INDEX IX_CONV_ASIGNACIONES_Conversacion_Fecha
        ON dbo.CONV_ASIGNACIONES (IdConversacion, FechaHora DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_WEBHOOK_LOG_MessageId' AND object_id = OBJECT_ID(N'dbo.CONV_WEBHOOK_LOG'))
    CREATE NONCLUSTERED INDEX IX_CONV_WEBHOOK_LOG_MessageId
        ON dbo.CONV_WEBHOOK_LOG (WhatsAppMessageId, FechaHoraRecepcion DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CONV_PLANTILLAS_EstadoMeta' AND object_id = OBJECT_ID(N'dbo.CONV_PLANTILLAS'))
    CREATE NONCLUSTERED INDEX IX_CONV_PLANTILLAS_EstadoMeta
        ON dbo.CONV_PLANTILLAS (EstadoMeta, Activa, FechaHora_Grabacion DESC);
GO

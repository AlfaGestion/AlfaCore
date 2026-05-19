/*
    Etapa inicial - Tickets de asistencia.
    Crea el modelo propio del modulo y deja estados base tipo helpdesk.
*/

IF OBJECT_ID(N'dbo.TICK_ESTADOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_ESTADOS
    (
        CodigoEstado nvarchar(30) NOT NULL,
        Nombre nvarchar(80) NOT NULL,
        Color nvarchar(20) NULL,
        EsCerrado bit NOT NULL CONSTRAINT DF_TICK_ESTADOS_EsCerrado DEFAULT (0),
        Orden int NOT NULL CONSTRAINT DF_TICK_ESTADOS_Orden DEFAULT (0),
        Activo bit NOT NULL CONSTRAINT DF_TICK_ESTADOS_Activo DEFAULT (1),
        FechaHora_Grabacion datetime NULL CONSTRAINT DF_TICK_ESTADOS_FHG DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_TICK_ESTADOS PRIMARY KEY CLUSTERED (CodigoEstado)
    );
END;

IF OBJECT_ID(N'dbo.TICK_TICKETS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_TICKETS
    (
        IdTicket bigint IDENTITY(1,1) NOT NULL,
        Numero int NOT NULL,
        Titulo nvarchar(180) NOT NULL,
        Descripcion ntext NULL,
        CodigoEstado nvarchar(30) NOT NULL,
        Prioridad int NOT NULL CONSTRAINT DF_TICK_TICKETS_Prioridad DEFAULT (1),
        IdTecnico nvarchar(20) NULL,
        ClienteCodigo nvarchar(20) NULL,
        IdContacto int NULL,
        IdConversacion bigint NULL,
        UsuarioAlta nvarchar(80) NULL,
        Baja bit NOT NULL CONSTRAINT DF_TICK_TICKETS_Baja DEFAULT (0),
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_TICK_TICKETS_FHA DEFAULT (GETDATE()),
        FechaHoraModificacion datetime NULL,
        FechaHoraCierre datetime NULL,
        CONSTRAINT PK_TICK_TICKETS PRIMARY KEY CLUSTERED (IdTicket),
        CONSTRAINT UQ_TICK_TICKETS_Numero UNIQUE NONCLUSTERED (Numero),
        CONSTRAINT FK_TICK_TICKETS_ESTADOS FOREIGN KEY (CodigoEstado) REFERENCES dbo.TICK_ESTADOS (CodigoEstado),
        CONSTRAINT CK_TICK_TICKETS_Prioridad CHECK (Prioridad BETWEEN 0 AND 3)
    );
END;

IF OBJECT_ID(N'dbo.TICK_ETIQUETAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_ETIQUETAS
    (
        IdEtiqueta int IDENTITY(1,1) NOT NULL,
        Nombre nvarchar(60) NOT NULL,
        Color nvarchar(20) NULL,
        Activa bit NOT NULL CONSTRAINT DF_TICK_ETIQUETAS_Activa DEFAULT (1),
        CONSTRAINT PK_TICK_ETIQUETAS PRIMARY KEY CLUSTERED (IdEtiqueta),
        CONSTRAINT UQ_TICK_ETIQUETAS_Nombre UNIQUE NONCLUSTERED (Nombre)
    );
END;

IF OBJECT_ID(N'dbo.TICK_TICKET_ETIQUETAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_TICKET_ETIQUETAS
    (
        IdTicket bigint NOT NULL,
        IdEtiqueta int NOT NULL,
        CONSTRAINT PK_TICK_TICKET_ETIQUETAS PRIMARY KEY CLUSTERED (IdTicket, IdEtiqueta),
        CONSTRAINT FK_TICK_TICKET_ETIQUETAS_TICKET FOREIGN KEY (IdTicket) REFERENCES dbo.TICK_TICKETS (IdTicket),
        CONSTRAINT FK_TICK_TICKET_ETIQUETAS_ETIQUETA FOREIGN KEY (IdEtiqueta) REFERENCES dbo.TICK_ETIQUETAS (IdEtiqueta)
    );
END;

IF OBJECT_ID(N'dbo.TICK_TICKET_MENSAJES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_TICKET_MENSAJES
    (
        IdTicket bigint NOT NULL,
        IdMensaje bigint NOT NULL,
        Orden int NOT NULL CONSTRAINT DF_TICK_TICKET_MENSAJES_Orden DEFAULT (0),
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_TICK_TICKET_MENSAJES_FHG DEFAULT (GETDATE()),
        CONSTRAINT PK_TICK_TICKET_MENSAJES PRIMARY KEY CLUSTERED (IdTicket, IdMensaje),
        CONSTRAINT FK_TICK_TICKET_MENSAJES_TICKET FOREIGN KEY (IdTicket) REFERENCES dbo.TICK_TICKETS (IdTicket)
    );
END;

IF OBJECT_ID(N'dbo.TICK_ACTIVIDAD', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_ACTIVIDAD
    (
        IdActividad bigint IDENTITY(1,1) NOT NULL,
        IdTicket bigint NOT NULL,
        TipoActividad nvarchar(30) NOT NULL,
        Descripcion ntext NULL,
        Usuario nvarchar(80) NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_TICK_ACTIVIDAD_FechaHora DEFAULT (GETDATE()),
        CONSTRAINT PK_TICK_ACTIVIDAD PRIMARY KEY CLUSTERED (IdActividad),
        CONSTRAINT FK_TICK_ACTIVIDAD_TICKET FOREIGN KEY (IdTicket) REFERENCES dbo.TICK_TICKETS (IdTicket)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.TICK_ESTADOS WHERE CodigoEstado = N'NUEVO')
    INSERT INTO dbo.TICK_ESTADOS (CodigoEstado, Nombre, Color, EsCerrado, Orden) VALUES (N'NUEVO', N'Nuevo', N'#38bdf8', 0, 10);

IF NOT EXISTS (SELECT 1 FROM dbo.TICK_ESTADOS WHERE CodigoEstado = N'EN_PROCESO')
    INSERT INTO dbo.TICK_ESTADOS (CodigoEstado, Nombre, Color, EsCerrado, Orden) VALUES (N'EN_PROCESO', N'En proceso', N'#22c55e', 0, 20);

IF NOT EXISTS (SELECT 1 FROM dbo.TICK_ESTADOS WHERE CodigoEstado = N'EN_ESPERA')
    INSERT INTO dbo.TICK_ESTADOS (CodigoEstado, Nombre, Color, EsCerrado, Orden) VALUES (N'EN_ESPERA', N'En espera', N'#f59e0b', 0, 30);

IF NOT EXISTS (SELECT 1 FROM dbo.TICK_ESTADOS WHERE CodigoEstado = N'ESCALADO')
    INSERT INTO dbo.TICK_ESTADOS (CodigoEstado, Nombre, Color, EsCerrado, Orden) VALUES (N'ESCALADO', N'Escalado', N'#ef4444', 0, 40);

IF NOT EXISTS (SELECT 1 FROM dbo.TICK_ESTADOS WHERE CodigoEstado = N'CERRADO')
    INSERT INTO dbo.TICK_ESTADOS (CodigoEstado, Nombre, Color, EsCerrado, Orden) VALUES (N'CERRADO', N'Cerrado', N'#94a3b8', 1, 90);

IF NOT EXISTS (SELECT 1 FROM dbo.TICK_ETIQUETAS WHERE Nombre = N'Incidente')
    INSERT INTO dbo.TICK_ETIQUETAS (Nombre, Color) VALUES (N'Incidente', N'#f43f5e');

IF NOT EXISTS (SELECT 1 FROM dbo.TICK_ETIQUETAS WHERE Nombre = N'Capacitacion')
    INSERT INTO dbo.TICK_ETIQUETAS (Nombre, Color) VALUES (N'Capacitacion', N'#8b5cf6');

IF NOT EXISTS (SELECT 1 FROM dbo.TICK_ETIQUETAS WHERE Nombre = N'Programacion')
    INSERT INTO dbo.TICK_ETIQUETAS (Nombre, Color) VALUES (N'Programacion', N'#14b8a6');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TICK_TICKETS') AND name = N'IX_TICK_TICKETS_Estado')
    CREATE NONCLUSTERED INDEX IX_TICK_TICKETS_Estado ON dbo.TICK_TICKETS (CodigoEstado, Baja, FechaHoraAlta DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TICK_TICKETS') AND name = N'IX_TICK_TICKETS_Tecnico')
    CREATE NONCLUSTERED INDEX IX_TICK_TICKETS_Tecnico ON dbo.TICK_TICKETS (IdTecnico, Baja, FechaHoraAlta DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TICK_TICKET_MENSAJES') AND name = N'IX_TICK_TICKET_MENSAJES_Mensaje')
    CREATE NONCLUSTERED INDEX IX_TICK_TICKET_MENSAJES_Mensaje ON dbo.TICK_TICKET_MENSAJES (IdMensaje);

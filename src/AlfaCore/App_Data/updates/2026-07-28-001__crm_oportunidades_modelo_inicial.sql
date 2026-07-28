/*
    CRM - modelo inicial de oportunidades.
    Crea un pipeline configurable con etapas, oportunidades, etiquetas,
    actividad y vínculo opcional a conversaciones/mensajes.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CRM_ETAPAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_ETAPAS
    (
        IdEtapa int IDENTITY(1,1) NOT NULL,
        Nombre nvarchar(80) NOT NULL,
        Color nvarchar(20) NULL,
        EsGanada bit NOT NULL CONSTRAINT DF_CRM_ETAPAS_EsGanada DEFAULT (0),
        EsPerdida bit NOT NULL CONSTRAINT DF_CRM_ETAPAS_EsPerdida DEFAULT (0),
        Orden int NOT NULL CONSTRAINT DF_CRM_ETAPAS_Orden DEFAULT (0),
        Activa bit NOT NULL CONSTRAINT DF_CRM_ETAPAS_Activa DEFAULT (1),
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CRM_ETAPAS_FHG DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CRM_ETAPAS PRIMARY KEY CLUSTERED (IdEtapa),
        CONSTRAINT CK_CRM_ETAPAS_Final CHECK (NOT (EsGanada = 1 AND EsPerdida = 1))
    );
END;
GO

IF OBJECT_ID(N'dbo.CRM_OPORTUNIDADES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_OPORTUNIDADES
    (
        IdOportunidad bigint IDENTITY(1,1) NOT NULL,
        Numero int NOT NULL,
        Titulo nvarchar(180) NOT NULL,
        Descripcion nvarchar(max) NULL,
        IdEtapa int NOT NULL,
        Prioridad int NOT NULL CONSTRAINT DF_CRM_OP_Prioridad DEFAULT (1),
        Probabilidad int NOT NULL CONSTRAINT DF_CRM_OP_Probabilidad DEFAULT (10),
        ImporteEstimado decimal(18,2) NOT NULL CONSTRAINT DF_CRM_OP_Importe DEFAULT (0),
        FechaCierreEstimada date NULL,
        IdTecnico nvarchar(20) NULL,
        ClienteCodigo nvarchar(20) NULL,
        IdContacto int NULL,
        CanalOrigen nvarchar(30) NULL,
        IdConversacion bigint NULL,
        UsuarioAlta nvarchar(80) NULL,
        Baja bit NOT NULL CONSTRAINT DF_CRM_OP_Baja DEFAULT (0),
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_CRM_OP_FHA DEFAULT (GETDATE()),
        FechaHoraModificacion datetime NULL,
        FechaHoraCierre datetime NULL,
        CONSTRAINT PK_CRM_OPORTUNIDADES PRIMARY KEY CLUSTERED (IdOportunidad),
        CONSTRAINT UQ_CRM_OPORTUNIDADES_Numero UNIQUE NONCLUSTERED (Numero),
        CONSTRAINT FK_CRM_OPORTUNIDADES_ETAPA FOREIGN KEY (IdEtapa) REFERENCES dbo.CRM_ETAPAS (IdEtapa),
        CONSTRAINT CK_CRM_OP_Prioridad CHECK (Prioridad BETWEEN 0 AND 3),
        CONSTRAINT CK_CRM_OP_Probabilidad CHECK (Probabilidad BETWEEN 0 AND 100),
        CONSTRAINT CK_CRM_OP_Importe CHECK (ImporteEstimado >= 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.CRM_ETIQUETAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_ETIQUETAS
    (
        IdEtiqueta int IDENTITY(1,1) NOT NULL,
        Nombre nvarchar(60) NOT NULL,
        Color nvarchar(20) NULL,
        Activa bit NOT NULL CONSTRAINT DF_CRM_ETIQUETAS_Activa DEFAULT (1),
        CONSTRAINT PK_CRM_ETIQUETAS PRIMARY KEY CLUSTERED (IdEtiqueta),
        CONSTRAINT UQ_CRM_ETIQUETAS_Nombre UNIQUE NONCLUSTERED (Nombre)
    );
END;
GO

IF OBJECT_ID(N'dbo.CRM_OPORTUNIDAD_ETIQUETAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_OPORTUNIDAD_ETIQUETAS
    (
        IdOportunidad bigint NOT NULL,
        IdEtiqueta int NOT NULL,
        CONSTRAINT PK_CRM_OPORTUNIDAD_ETIQUETAS PRIMARY KEY CLUSTERED (IdOportunidad, IdEtiqueta),
        CONSTRAINT FK_CRM_OP_ETIQ_OP FOREIGN KEY (IdOportunidad) REFERENCES dbo.CRM_OPORTUNIDADES (IdOportunidad),
        CONSTRAINT FK_CRM_OP_ETIQ_ETIQUETA FOREIGN KEY (IdEtiqueta) REFERENCES dbo.CRM_ETIQUETAS (IdEtiqueta)
    );
END;
GO

IF OBJECT_ID(N'dbo.CRM_OPORTUNIDAD_MENSAJES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_OPORTUNIDAD_MENSAJES
    (
        IdOportunidad bigint NOT NULL,
        IdMensaje bigint NOT NULL,
        Orden int NOT NULL CONSTRAINT DF_CRM_OP_MSG_Orden DEFAULT (0),
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CRM_OP_MSG_FHG DEFAULT (GETDATE()),
        CONSTRAINT PK_CRM_OPORTUNIDAD_MENSAJES PRIMARY KEY CLUSTERED (IdOportunidad, IdMensaje),
        CONSTRAINT FK_CRM_OP_MSG_OP FOREIGN KEY (IdOportunidad) REFERENCES dbo.CRM_OPORTUNIDADES (IdOportunidad)
    );
END;
GO

IF OBJECT_ID(N'dbo.CRM_ACTIVIDAD', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_ACTIVIDAD
    (
        IdActividad bigint IDENTITY(1,1) NOT NULL,
        IdOportunidad bigint NOT NULL,
        TipoActividad nvarchar(30) NOT NULL,
        Descripcion nvarchar(max) NULL,
        Usuario nvarchar(80) NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_CRM_ACTIVIDAD_FechaHora DEFAULT (GETDATE()),
        CONSTRAINT PK_CRM_ACTIVIDAD PRIMARY KEY CLUSTERED (IdActividad),
        CONSTRAINT FK_CRM_ACTIVIDAD_OP FOREIGN KEY (IdOportunidad) REFERENCES dbo.CRM_OPORTUNIDADES (IdOportunidad)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.CRM_ETAPAS WHERE Nombre = N'Nuevo')
    INSERT INTO dbo.CRM_ETAPAS (Nombre, Color, Orden) VALUES (N'Nuevo', N'#38BDF8', 10);

IF NOT EXISTS (SELECT 1 FROM dbo.CRM_ETAPAS WHERE Nombre = N'Calificado')
    INSERT INTO dbo.CRM_ETAPAS (Nombre, Color, Orden) VALUES (N'Calificado', N'#2563EB', 20);

IF NOT EXISTS (SELECT 1 FROM dbo.CRM_ETAPAS WHERE Nombre = N'Propuesta')
    INSERT INTO dbo.CRM_ETAPAS (Nombre, Color, Orden) VALUES (N'Propuesta', N'#F59E0B', 30);

IF NOT EXISTS (SELECT 1 FROM dbo.CRM_ETAPAS WHERE Nombre = N'Ganada')
    INSERT INTO dbo.CRM_ETAPAS (Nombre, Color, EsGanada, Orden) VALUES (N'Ganada', N'#22C55E', 1, 90);

IF NOT EXISTS (SELECT 1 FROM dbo.CRM_ETAPAS WHERE Nombre = N'Perdida')
    INSERT INTO dbo.CRM_ETAPAS (Nombre, Color, EsPerdida, Orden) VALUES (N'Perdida', N'#94A3B8', 1, 100);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.CRM_ETIQUETAS WHERE Nombre = N'Potencial')
    INSERT INTO dbo.CRM_ETIQUETAS (Nombre, Color) VALUES (N'Potencial', N'#2563EB');

IF NOT EXISTS (SELECT 1 FROM dbo.CRM_ETIQUETAS WHERE Nombre = N'Cliente actual')
    INSERT INTO dbo.CRM_ETIQUETAS (Nombre, Color) VALUES (N'Cliente actual', N'#22C55E');

IF NOT EXISTS (SELECT 1 FROM dbo.CRM_ETIQUETAS WHERE Nombre = N'Urgente')
    INSERT INTO dbo.CRM_ETIQUETAS (Nombre, Color) VALUES (N'Urgente', N'#EF4444');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_OPORTUNIDADES') AND name = N'IX_CRM_OP_Etapa')
    CREATE NONCLUSTERED INDEX IX_CRM_OP_Etapa ON dbo.CRM_OPORTUNIDADES (IdEtapa, Baja, FechaHoraAlta DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_OPORTUNIDADES') AND name = N'IX_CRM_OP_Tecnico')
    CREATE NONCLUSTERED INDEX IX_CRM_OP_Tecnico ON dbo.CRM_OPORTUNIDADES (IdTecnico, Baja, FechaHoraAlta DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_OPORTUNIDADES') AND name = N'IX_CRM_OP_Cliente')
    CREATE NONCLUSTERED INDEX IX_CRM_OP_Cliente ON dbo.CRM_OPORTUNIDADES (ClienteCodigo, Baja, FechaHoraAlta DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_OPORTUNIDAD_MENSAJES') AND name = N'IX_CRM_OP_MSG_Mensaje')
    CREATE NONCLUSTERED INDEX IX_CRM_OP_MSG_Mensaje ON dbo.CRM_OPORTUNIDAD_MENSAJES (IdMensaje);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_ACTIVIDAD') AND name = N'IX_CRM_ACTIVIDAD_OP_Fecha')
    CREATE NONCLUSTERED INDEX IX_CRM_ACTIVIDAD_OP_Fecha ON dbo.CRM_ACTIVIDAD (IdOportunidad, FechaHora DESC, IdActividad DESC);
GO

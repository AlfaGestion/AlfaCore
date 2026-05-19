/*
    Etapa inicial - Calendario de soporte.
    Crea eventos, guardias y recordatorios WhatsApp vinculados a plantillas.
*/

IF OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CAL_EVENTOS
    (
        IdEvento bigint IDENTITY(1,1) NOT NULL,
        Titulo nvarchar(180) NOT NULL,
        Tipo nvarchar(30) NOT NULL CONSTRAINT DF_CAL_EVENTOS_Tipo DEFAULT (N'GUARDIA'),
        FechaInicio datetime NOT NULL,
        FechaFin datetime NOT NULL,
        TodoElDia bit NOT NULL CONSTRAINT DF_CAL_EVENTOS_TodoElDia DEFAULT (1),
        IdTecnico nvarchar(20) NULL,
        TecnicoNombre nvarchar(120) NULL,
        TelefonoWhatsApp nvarchar(40) NULL,
        Estado nvarchar(30) NOT NULL CONSTRAINT DF_CAL_EVENTOS_Estado DEFAULT (N'CONFIRMADO'),
        Color nvarchar(20) NULL,
        Descripcion ntext NULL,
        UsuarioAlta nvarchar(80) NULL,
        Baja bit NOT NULL CONSTRAINT DF_CAL_EVENTOS_Baja DEFAULT (0),
        FechaHora_Grabacion datetime NULL CONSTRAINT DF_CAL_EVENTOS_FHG DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CAL_EVENTOS PRIMARY KEY CLUSTERED (IdEvento),
        CONSTRAINT CK_CAL_EVENTOS_Fechas CHECK (FechaFin >= FechaInicio),
        CONSTRAINT CK_CAL_EVENTOS_Tipo CHECK (Tipo IN (N'GUARDIA', N'REUNION', N'CAPACITACION', N'OTRO')),
        CONSTRAINT CK_CAL_EVENTOS_Estado CHECK (Estado IN (N'BORRADOR', N'CONFIRMADO', N'CANCELADO', N'FINALIZADO'))
    );
END;

IF OBJECT_ID(N'dbo.CAL_RECORDATORIOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CAL_RECORDATORIOS
    (
        IdRecordatorio bigint IDENTITY(1,1) NOT NULL,
        IdEvento bigint NOT NULL,
        Tipo nvarchar(30) NOT NULL CONSTRAINT DF_CAL_RECORDATORIOS_Tipo DEFAULT (N'WHATSAPP'),
        MinutosAntes int NOT NULL CONSTRAINT DF_CAL_RECORDATORIOS_Minutos DEFAULT (1440),
        IdPlantillaWhatsApp bigint NULL,
        NotificarResponsable bit NOT NULL CONSTRAINT DF_CAL_RECORDATORIOS_Responsable DEFAULT (1),
        FechaHoraProgramada datetime NULL,
        FechaHoraEnvio datetime NULL,
        EstadoEnvio nvarchar(30) NOT NULL CONSTRAINT DF_CAL_RECORDATORIOS_Estado DEFAULT (N'PENDIENTE'),
        UltimoError ntext NULL,
        Baja bit NOT NULL CONSTRAINT DF_CAL_RECORDATORIOS_Baja DEFAULT (0),
        FechaHora_Grabacion datetime NULL CONSTRAINT DF_CAL_RECORDATORIOS_FHG DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CAL_RECORDATORIOS PRIMARY KEY CLUSTERED (IdRecordatorio),
        CONSTRAINT FK_CAL_RECORDATORIOS_EVENTOS FOREIGN KEY (IdEvento) REFERENCES dbo.CAL_EVENTOS (IdEvento),
        CONSTRAINT CK_CAL_RECORDATORIOS_Tipo CHECK (Tipo IN (N'WHATSAPP')),
        CONSTRAINT CK_CAL_RECORDATORIOS_Estado CHECK (EstadoEnvio IN (N'PENDIENTE', N'ENVIADO', N'ERROR', N'OMITIDO')),
        CONSTRAINT CK_CAL_RECORDATORIOS_Minutos CHECK (MinutosAntes >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CAL_EVENTOS') AND name = N'IX_CAL_EVENTOS_Rango')
    CREATE NONCLUSTERED INDEX IX_CAL_EVENTOS_Rango ON dbo.CAL_EVENTOS (Baja, FechaInicio, FechaFin) INCLUDE (Tipo, IdTecnico, Estado);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CAL_EVENTOS') AND name = N'IX_CAL_EVENTOS_Tecnico')
    CREATE NONCLUSTERED INDEX IX_CAL_EVENTOS_Tecnico ON dbo.CAL_EVENTOS (IdTecnico, Baja, FechaInicio);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CAL_RECORDATORIOS') AND name = N'IX_CAL_RECORDATORIOS_Pendientes')
    CREATE NONCLUSTERED INDEX IX_CAL_RECORDATORIOS_Pendientes ON dbo.CAL_RECORDATORIOS (Baja, EstadoEnvio, FechaHoraProgramada) INCLUDE (IdEvento, Tipo);

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE = N'CALENDARIO-RECORDATORIO-WHATSAPP-HORAS')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
        (GRUPO, CLAVE, VALOR, ValorAux, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES
        (N'CALENDARIO', N'CALENDARIO-RECORDATORIO-WHATSAPP-HORAS', N'24', NULL, N'Horas antes de guardia', GETDATE(), GETDATE());
END;

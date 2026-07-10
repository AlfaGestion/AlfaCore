/*
    Reuniones publicas de Calendario.
    Permite publicar tipos de cita y registrar reservas generadas desde /reuniones.
*/

IF OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CAL_EVENTOS_Tipo' AND parent_object_id = OBJECT_ID(N'dbo.CAL_EVENTOS'))
BEGIN
    ALTER TABLE dbo.CAL_EVENTOS DROP CONSTRAINT CK_CAL_EVENTOS_Tipo;
END;

IF OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CAL_EVENTOS_Tipo' AND parent_object_id = OBJECT_ID(N'dbo.CAL_EVENTOS'))
BEGIN
    ALTER TABLE dbo.CAL_EVENTOS WITH CHECK ADD CONSTRAINT CK_CAL_EVENTOS_Tipo
        CHECK (Tipo IN (N'GUARDIA', N'VACACIONES', N'AUSENCIA', N'FERIADO', N'REUNION', N'CAPACITACION', N'OTRO'));
END;

IF OBJECT_ID(N'dbo.CAL_TIPOS_REUNION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CAL_TIPOS_REUNION
    (
        IdTipoReunion bigint IDENTITY(1,1) NOT NULL,
        Slug nvarchar(80) NOT NULL,
        Titulo nvarchar(180) NOT NULL,
        Descripcion ntext NULL,
        DuracionMinutos int NOT NULL CONSTRAINT DF_CAL_TIPOS_REUNION_Duracion DEFAULT (60),
        Modalidad nvarchar(80) NOT NULL CONSTRAINT DF_CAL_TIPOS_REUNION_Modalidad DEFAULT (N'En linea'),
        EnlaceVideollamada nvarchar(250) NULL,
        IdTecnico nvarchar(20) NULL,
        TecnicoNombre nvarchar(120) NULL,
        TelefonoWhatsApp nvarchar(40) NULL,
        EmailOperador nvarchar(160) NULL,
        DiasDisponibles nvarchar(30) NOT NULL CONSTRAINT DF_CAL_TIPOS_REUNION_Dias DEFAULT (N'1,2,3,4,5'),
        HoraDesde time(0) NOT NULL CONSTRAINT DF_CAL_TIPOS_REUNION_HoraDesde DEFAULT ('10:00'),
        HoraHasta time(0) NOT NULL CONSTRAINT DF_CAL_TIPOS_REUNION_HoraHasta DEFAULT ('16:00'),
        ReservasHastaDias int NOT NULL CONSTRAINT DF_CAL_TIPOS_REUNION_HastaDias DEFAULT (30),
        AnticipacionMinHoras int NOT NULL CONSTRAINT DF_CAL_TIPOS_REUNION_Anticipacion DEFAULT (24),
        Activo bit NOT NULL CONSTRAINT DF_CAL_TIPOS_REUNION_Activo DEFAULT (1),
        Color nvarchar(20) NULL,
        Baja bit NOT NULL CONSTRAINT DF_CAL_TIPOS_REUNION_Baja DEFAULT (0),
        FechaHora_Grabacion datetime NULL CONSTRAINT DF_CAL_TIPOS_REUNION_FHG DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CAL_TIPOS_REUNION PRIMARY KEY CLUSTERED (IdTipoReunion),
        CONSTRAINT UQ_CAL_TIPOS_REUNION_Slug UNIQUE (Slug),
        CONSTRAINT CK_CAL_TIPOS_REUNION_Duracion CHECK (DuracionMinutos BETWEEN 15 AND 480),
        CONSTRAINT CK_CAL_TIPOS_REUNION_Horas CHECK (HoraHasta > HoraDesde)
    );
END;

IF OBJECT_ID(N'dbo.CAL_RESERVAS_REUNION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CAL_RESERVAS_REUNION
    (
        IdReserva bigint IDENTITY(1,1) NOT NULL,
        IdTipoReunion bigint NOT NULL,
        IdEvento bigint NOT NULL,
        FechaInicio datetime NOT NULL,
        FechaFin datetime NOT NULL,
        ClienteNombre nvarchar(160) NOT NULL,
        RazonSocial nvarchar(180) NULL,
        Email nvarchar(160) NOT NULL,
        Telefono nvarchar(60) NOT NULL,
        TipoCapacitacion nvarchar(160) NULL,
        Observaciones ntext NULL,
        Estado nvarchar(30) NOT NULL CONSTRAINT DF_CAL_RESERVAS_REUNION_Estado DEFAULT (N'CONFIRMADA'),
        TokenReserva nvarchar(80) NOT NULL,
        Baja bit NOT NULL CONSTRAINT DF_CAL_RESERVAS_REUNION_Baja DEFAULT (0),
        FechaHora_Grabacion datetime NULL CONSTRAINT DF_CAL_RESERVAS_REUNION_FHG DEFAULT (GETDATE()),
        FechaHora_Modificacion datetime NULL,
        CONSTRAINT PK_CAL_RESERVAS_REUNION PRIMARY KEY CLUSTERED (IdReserva),
        CONSTRAINT FK_CAL_RESERVAS_REUNION_TIPO FOREIGN KEY (IdTipoReunion) REFERENCES dbo.CAL_TIPOS_REUNION (IdTipoReunion),
        CONSTRAINT FK_CAL_RESERVAS_REUNION_EVENTO FOREIGN KEY (IdEvento) REFERENCES dbo.CAL_EVENTOS (IdEvento),
        CONSTRAINT CK_CAL_RESERVAS_REUNION_Fechas CHECK (FechaFin > FechaInicio),
        CONSTRAINT CK_CAL_RESERVAS_REUNION_Estado CHECK (Estado IN (N'CONFIRMADA', N'CANCELADA'))
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CAL_RESERVAS_REUNION') AND name = N'IX_CAL_RESERVAS_REUNION_Rango')
    CREATE NONCLUSTERED INDEX IX_CAL_RESERVAS_REUNION_Rango ON dbo.CAL_RESERVAS_REUNION (Baja, Estado, FechaInicio, FechaFin) INCLUDE (IdTipoReunion, IdEvento);

IF NOT EXISTS (SELECT 1 FROM dbo.CAL_TIPOS_REUNION WHERE Slug = N'capacitacion-online')
BEGIN
    INSERT INTO dbo.CAL_TIPOS_REUNION
    (
        Slug, Titulo, Descripcion, DuracionMinutos, Modalidad, EnlaceVideollamada,
        IdTecnico, TecnicoNombre, TelefonoWhatsApp, EmailOperador, DiasDisponibles,
        HoraDesde, HoraHasta, ReservasHastaDias, AnticipacionMinHoras, Activo, Color,
        FechaHora_Grabacion, FechaHora_Modificacion
    )
    VALUES
    (
        N'capacitacion-online',
        N'Capacitacion Online',
        N'Espacio para que clientes reserven una capacitacion online con el equipo de AlfaNet.',
        60,
        N'Reunion en linea',
        N'Google Meet',
        NULL,
        N'Equipo AlfaNet',
        NULL,
        NULL,
        N'1,2,3,4,5',
        '10:00',
        '16:00',
        30,
        24,
        1,
        N'#22d3ee',
        GETDATE(),
        GETDATE()
    );
END;

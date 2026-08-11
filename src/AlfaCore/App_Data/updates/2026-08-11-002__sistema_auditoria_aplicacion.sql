SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.SYS_EventosAplicacion', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.SYS_EventosAplicacion
        (
            Id uniqueidentifier NOT NULL,
            FechaHora datetime2(3) NOT NULL CONSTRAINT DF_SYS_EventosAplicacion_FechaHora DEFAULT SYSUTCDATETIME(),
            Tipo nvarchar(20) NOT NULL,
            Severidad nvarchar(20) NOT NULL,
            Modulo nvarchar(80) NOT NULL,
            Accion nvarchar(120) NOT NULL,
            EntidadTipo nvarchar(80) NULL,
            EntidadId nvarchar(120) NULL,
            Usuario nvarchar(120) NULL,
            ServidorSql nvarchar(120) NULL,
            BaseDatos nvarchar(120) NULL,
            RequestPath nvarchar(300) NULL,
            HttpMethod nvarchar(20) NULL,
            TraceId nvarchar(120) NULL,
            CorrelationId nvarchar(120) NULL,
            Mensaje nvarchar(1000) NULL,
            MensajeUsuario nvarchar(500) NULL,
            ExceptionType nvarchar(250) NULL,
            ExceptionMessage nvarchar(max) NULL,
            StackTrace nvarchar(max) NULL,
            DataJson nvarchar(max) NULL,
            CONSTRAINT PK_SYS_EventosAplicacion PRIMARY KEY CLUSTERED (Id)
        );
    END;

    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'Id') IS NULL
        THROW 51000, 'SYS_EventosAplicacion existe sin la columna Id requerida.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.SYS_EventosAplicacion')
          AND name = N'Id'
          AND TYPE_NAME(system_type_id) <> N'uniqueidentifier'
    )
        THROW 51000, 'SYS_EventosAplicacion.Id existe con un tipo incompatible.', 1;

    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'FechaHora') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD FechaHora datetime2(3) NOT NULL CONSTRAINT DF_SYS_EventosAplicacion_FechaHora_Migracion DEFAULT SYSUTCDATETIME();
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'Tipo') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD Tipo nvarchar(20) NOT NULL CONSTRAINT DF_SYS_EventosAplicacion_Tipo DEFAULT N'Audit';
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'Severidad') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD Severidad nvarchar(20) NOT NULL CONSTRAINT DF_SYS_EventosAplicacion_Severidad DEFAULT N'Info';
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'Modulo') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD Modulo nvarchar(80) NOT NULL CONSTRAINT DF_SYS_EventosAplicacion_Modulo DEFAULT N'Sistema';
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'Accion') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD Accion nvarchar(120) NOT NULL CONSTRAINT DF_SYS_EventosAplicacion_Accion DEFAULT N'Evento';
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'EntidadTipo') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD EntidadTipo nvarchar(80) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'EntidadId') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD EntidadId nvarchar(120) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'Usuario') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD Usuario nvarchar(120) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'ServidorSql') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD ServidorSql nvarchar(120) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'BaseDatos') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD BaseDatos nvarchar(120) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'RequestPath') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD RequestPath nvarchar(300) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'HttpMethod') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD HttpMethod nvarchar(20) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'TraceId') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD TraceId nvarchar(120) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'CorrelationId') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD CorrelationId nvarchar(120) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'Mensaje') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD Mensaje nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'MensajeUsuario') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD MensajeUsuario nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'ExceptionType') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD ExceptionType nvarchar(250) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'ExceptionMessage') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD ExceptionMessage nvarchar(max) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'StackTrace') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD StackTrace nvarchar(max) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacion', N'DataJson') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacion ADD DataJson nvarchar(max) NULL;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.SYS_EventosAplicacion')
          AND name = N'IX_SYS_EventosAplicacion_Entidad_FechaHora'
    )
    BEGIN
        CREATE INDEX IX_SYS_EventosAplicacion_Entidad_FechaHora
            ON dbo.SYS_EventosAplicacion (EntidadTipo, EntidadId, FechaHora DESC, Id);
    END;

    IF OBJECT_ID(N'dbo.SYS_EventosAplicacionCambios', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.SYS_EventosAplicacionCambios
        (
            Id bigint IDENTITY(1,1) NOT NULL,
            EventoId uniqueidentifier NOT NULL,
            Campo nvarchar(120) NOT NULL,
            ValorAnterior nvarchar(max) NULL,
            ValorNuevo nvarchar(max) NULL,
            Orden smallint NOT NULL,
            CONSTRAINT PK_SYS_EventosAplicacionCambios PRIMARY KEY CLUSTERED (Id),
            CONSTRAINT FK_SYS_EventosAplicacionCambios_Evento
                FOREIGN KEY (EventoId) REFERENCES dbo.SYS_EventosAplicacion (Id)
        );
    END;

    IF COL_LENGTH(N'dbo.SYS_EventosAplicacionCambios', N'Id') IS NULL
        THROW 51001, 'SYS_EventosAplicacionCambios existe sin la columna Id requerida.', 1;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacionCambios', N'EventoId') IS NULL
        THROW 51001, 'SYS_EventosAplicacionCambios existe sin la columna EventoId requerida.', 1;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacionCambios', N'Campo') IS NULL
        THROW 51001, 'SYS_EventosAplicacionCambios existe sin la columna Campo requerida.', 1;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacionCambios', N'ValorAnterior') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacionCambios ADD ValorAnterior nvarchar(max) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacionCambios', N'ValorNuevo') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacionCambios ADD ValorNuevo nvarchar(max) NULL;
    IF COL_LENGTH(N'dbo.SYS_EventosAplicacionCambios', N'Orden') IS NULL
        ALTER TABLE dbo.SYS_EventosAplicacionCambios ADD Orden smallint NOT NULL CONSTRAINT DF_SYS_EventosAplicacionCambios_Orden DEFAULT 0;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.SYS_EventosAplicacionCambios')
          AND name = N'EventoId'
          AND TYPE_NAME(system_type_id) <> N'uniqueidentifier'
    )
        THROW 51001, 'SYS_EventosAplicacionCambios.EventoId existe con un tipo incompatible.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.SYS_EventosAplicacionCambios')
          AND referenced_object_id = OBJECT_ID(N'dbo.SYS_EventosAplicacion')
    )
    BEGIN
        ALTER TABLE dbo.SYS_EventosAplicacionCambios WITH CHECK
            ADD CONSTRAINT FK_SYS_EventosAplicacionCambios_Evento
            FOREIGN KEY (EventoId) REFERENCES dbo.SYS_EventosAplicacion (Id);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.SYS_EventosAplicacionCambios')
          AND name = N'IX_SYS_EventosAplicacionCambios_EventoId'
    )
    BEGIN
        CREATE INDEX IX_SYS_EventosAplicacionCambios_EventoId
            ON dbo.SYS_EventosAplicacionCambios (EventoId, Orden, Id);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

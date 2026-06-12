SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_PARTES_HORAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_PARTES_HORAS
    (
        IdParteHora bigint IDENTITY(1,1) NOT NULL,
        Fecha date NOT NULL,
        ClienteCodigo nvarchar(20) NULL,
        IdContacto int NULL,
        IdTicket bigint NULL,
        IdTecnico nvarchar(20) NULL,
        Usuario nvarchar(80) NOT NULL,
        TipoTrabajo nvarchar(30) NOT NULL CONSTRAINT DF_ALFACORE_PARTES_HORAS_Tipo DEFAULT (N'SOPORTE'),
        Minutos int NOT NULL,
        Facturable bit NOT NULL CONSTRAINT DF_ALFACORE_PARTES_HORAS_Facturable DEFAULT (1),
        Excedente bit NOT NULL CONSTRAINT DF_ALFACORE_PARTES_HORAS_Excedente DEFAULT (0),
        Estado nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_PARTES_HORAS_Estado DEFAULT (N'BORRADOR'),
        Descripcion nvarchar(1000) NULL,
        CostoHora decimal(18, 4) NULL,
        UsuarioAprobacion nvarchar(80) NULL,
        FechaHoraAprobacion datetime NULL,
        UsuarioAlta nvarchar(80) NULL,
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_ALFACORE_PARTES_HORAS_Alta DEFAULT (GETDATE()),
        UsuarioModificacion nvarchar(80) NULL,
        FechaHoraModificacion datetime NULL,
        Baja bit NOT NULL CONSTRAINT DF_ALFACORE_PARTES_HORAS_Baja DEFAULT (0),
        CONSTRAINT PK_ALFACORE_PARTES_HORAS PRIMARY KEY CLUSTERED (IdParteHora),
        CONSTRAINT CK_ALFACORE_PARTES_HORAS_Minutos CHECK (Minutos > 0 AND Minutos <= 1440),
        CONSTRAINT CK_ALFACORE_PARTES_HORAS_Estado CHECK (Estado IN (N'BORRADOR', N'PRESENTADO', N'APROBADO', N'RECHAZADO')),
        CONSTRAINT CK_ALFACORE_PARTES_HORAS_Tipo CHECK (TipoTrabajo IN (N'SOPORTE', N'DESARROLLO', N'IMPLEMENTACION', N'CAPACITACION', N'REUNION', N'VIAJE', N'INTERNO'))
    );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_PARTES_HORAS_CLIENTES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_PARTES_HORAS_CLIENTES
    (
        ClienteCodigo nvarchar(20) NOT NULL,
        HorasIncluidasMes int NOT NULL CONSTRAINT DF_ALFACORE_PARTES_CLIENTES_Horas DEFAULT (0),
        ToleranciaMinutos int NOT NULL CONSTRAINT DF_ALFACORE_PARTES_CLIENTES_Tolerancia DEFAULT (0),
        Activa bit NOT NULL CONSTRAINT DF_ALFACORE_PARTES_CLIENTES_Activa DEFAULT (1),
        Observaciones nvarchar(500) NULL,
        UsuarioAlta nvarchar(80) NULL,
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_ALFACORE_PARTES_CLIENTES_Alta DEFAULT (GETDATE()),
        UsuarioModificacion nvarchar(80) NULL,
        FechaHoraModificacion datetime NULL,
        CONSTRAINT PK_ALFACORE_PARTES_HORAS_CLIENTES PRIMARY KEY CLUSTERED (ClienteCodigo),
        CONSTRAINT CK_ALFACORE_PARTES_CLIENTES_Horas CHECK (HorasIncluidasMes >= 0),
        CONSTRAINT CK_ALFACORE_PARTES_CLIENTES_Tolerancia CHECK (ToleranciaMinutos >= 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_PARTES_HORAS') AND name = N'IX_ALFACORE_PARTES_HORAS_Fecha')
    CREATE INDEX IX_ALFACORE_PARTES_HORAS_Fecha ON dbo.ALFACORE_PARTES_HORAS (Fecha DESC, Baja, ClienteCodigo, IdTecnico);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_PARTES_HORAS') AND name = N'IX_ALFACORE_PARTES_HORAS_Ticket')
    CREATE INDEX IX_ALFACORE_PARTES_HORAS_Ticket ON dbo.ALFACORE_PARTES_HORAS (IdTicket, Baja, Fecha DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_PARTES_HORAS') AND name = N'IX_ALFACORE_PARTES_HORAS_ClienteMes')
    CREATE INDEX IX_ALFACORE_PARTES_HORAS_ClienteMes ON dbo.ALFACORE_PARTES_HORAS (ClienteCodigo, Fecha, Baja, Facturable) INCLUDE (Minutos, Excedente);
GO

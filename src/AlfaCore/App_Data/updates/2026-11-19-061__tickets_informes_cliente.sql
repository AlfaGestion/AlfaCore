/*
  Informes al cliente dentro de los tickets de asistencia.
  - TICK_INFORMES: un informe (HTML del editor de tickets) por ticket; BORRADOR o PUBLICADO.
    Publicado se ve en /{idweb}/informe/{token} (ALFA_CENTRAL.dbo.ALFA_PUBLIC_LINK, Tipo INFORME, IdReferencia = IdInforme).
  - TICK_INFORME_VISTAS: cada apertura del link publico, para saber si el cliente lo leyo.
  Idempotente.
*/
IF OBJECT_ID(N'dbo.TICK_TICKETS', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.TICK_INFORMES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_INFORMES (
        IdInforme int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TICK_INFORMES PRIMARY KEY,
        IdTicket bigint NOT NULL,
        Titulo nvarchar(200) NOT NULL,
        Html nvarchar(max) NULL,
        Estado varchar(12) NOT NULL CONSTRAINT DF_TICK_INFORMES_Estado DEFAULT ('BORRADOR'),
        GeneradoIA bit NOT NULL CONSTRAINT DF_TICK_INFORMES_IA DEFAULT (0),
        UsuarioAlta nvarchar(100) NULL,
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_TICK_INFORMES_Alta DEFAULT (GETDATE()),
        UsuarioModificacion nvarchar(100) NULL,
        FechaHoraModificacion datetime NULL,
        FechaHoraPublicacion datetime NULL,
        Baja bit NOT NULL CONSTRAINT DF_TICK_INFORMES_Baja DEFAULT (0)
    );
    CREATE INDEX IX_TICK_INFORMES_Ticket ON dbo.TICK_INFORMES (IdTicket, Baja);
END
GO

IF OBJECT_ID(N'dbo.TICK_INFORMES', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TICK_INFORME_VISTAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TICK_INFORME_VISTAS (
        IdVista bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TICK_INFORME_VISTAS PRIMARY KEY,
        IdInforme int NOT NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_TICK_INFORME_VISTAS_Fecha DEFAULT (GETDATE()),
        Navegador nvarchar(300) NULL
    );
    CREATE INDEX IX_TICK_INFORME_VISTAS_Informe ON dbo.TICK_INFORME_VISTAS (IdInforme, FechaHora);
END

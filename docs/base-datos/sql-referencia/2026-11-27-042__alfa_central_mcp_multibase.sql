-- MCP (antes dir-mcp/014): MULTI-BASE con ingreso por mail. Se corre en el ALMACEN OAuth = ALFA_CENTRAL (10.8.0.31).
-- Idempotente. Crea solo tablas MCP_* nuevas, agrega dos columnas a tablas MCP_* existentes y da permisos al login del almacen.
-- NO modifica users, bases ni Clientes: solo les da al login del MCP permiso de LECTURA sobre las columnas que necesita
-- (nunca password ni dbpassword).
--
-- IMPORTANTE: correrlo ANTES de publicar la version del MCP con ingreso por mail (esa version graba Base/Metodo en
-- MCP_CODIGO y MCP_REFRESH_TOKEN; sin estas columnas el login falla).
--
-- Ejecutar (sin contrasenas: solo el nombre del login):
--   sqlcmd -S 10.8.0.31 -d ALFA_CENTRAL -f i:65001 -i 2026-11-27-042__alfa_central_mcp_multibase.sql -v MCP_AUTH_LOGIN="mcp_auth_rw"
-- En SSMS: Consulta > Modo SQLCMD y completar el :setvar.
--
-- :setvar MCP_AUTH_LOGIN "mcp_auth_rw"
SET NOCOUNT ON;
GO
IF LEFT(N'$(MCP_AUTH_LOGIN)', 2) = N'$' + N'('
BEGIN
    RAISERROR(N'ABORTADO: la variable MCP_AUTH_LOGIN no fue reemplazada. En SSMS activar Consulta > Modo SQLCMD; o correr con sqlcmd -v.', 16, 1);
    SET NOEXEC ON;
END
GO
IF OBJECT_ID(N'dbo.bases', N'U') IS NULL OR OBJECT_ID(N'dbo.users', N'U') IS NULL OR OBJECT_ID(N'dbo.MCP_USUARIO', N'U') IS NULL
BEGIN
    RAISERROR(N'ABORTADO: esta no parece ALFA_CENTRAL (faltan dbo.bases, dbo.users o dbo.MCP_USUARIO del script 010).', 16, 1);
    SET NOEXEC ON;
END
GO

-- Bases habilitadas para el MCP: login de SOLO LECTURA (script 005 en esa base) y su clave CIFRADA con la clave del servidor web.
IF OBJECT_ID(N'dbo.MCP_BASE', N'U') IS NULL
    CREATE TABLE dbo.MCP_BASE
    (
        IdBase       int NOT NULL CONSTRAINT PK_MCP_BASE PRIMARY KEY,       -- = dbo.bases.id
        UsuarioSql   nvarchar(128) NOT NULL,                                -- normalmente mcp_dir_ro
        ClaveCifrada nvarchar(1000) NOT NULL,                               -- AES-GCM; nunca en claro
        Habilitada   bit NOT NULL CONSTRAINT DF_MCP_BASE_Habilitada DEFAULT (1),
        Alta         datetime NOT NULL CONSTRAINT DF_MCP_BASE_Alta DEFAULT (GETUTCDATE()),
        Modificado   datetime NULL
    );
GO

-- Quien puede consultar que base: el mail tiene que ser ademas usuario (dbo.users) del cliente dueno de la base.
IF OBJECT_ID(N'dbo.MCP_ACCESO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MCP_ACCESO
    (
        Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MCP_ACCESO PRIMARY KEY,
        Email         nvarchar(200) NOT NULL,                               -- en minusculas
        IdBase        int NOT NULL,                                         -- = dbo.bases.id
        Activo        bit NOT NULL CONSTRAINT DF_MCP_ACCESO_Activo DEFAULT (1),
        Alta          datetime NOT NULL CONSTRAINT DF_MCP_ACCESO_Alta DEFAULT (GETUTCDATE()),
        Invitado      datetime NULL,                                        -- UTC, ultimo mail de invitacion
        UltimoIngreso datetime NULL                                         -- UTC
    );
    CREATE UNIQUE INDEX UX_MCP_ACCESO_Email_Base ON dbo.MCP_ACCESO (Email, IdBase);
END
GO

-- Codigos de 6 digitos enviados por mail (solo el hash) y tickets de eleccion de base. Se purgan a los 2 dias.
IF OBJECT_ID(N'dbo.MCP_CODIGO_EMAIL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MCP_CODIGO_EMAIL
    (
        Id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MCP_CODIGO_EMAIL PRIMARY KEY,
        Email        nvarchar(200) NOT NULL,
        CodigoHash   nvarchar(64) NOT NULL,                                 -- SHA-256(mail|codigo); '' en las filas de ticket
        Expira       datetime NOT NULL,                                     -- UTC
        Intentos     int NOT NULL CONSTRAINT DF_MCP_CODIGO_EMAIL_Intentos DEFAULT (0),
        Usado        bit NOT NULL CONSTRAINT DF_MCP_CODIGO_EMAIL_Usado DEFAULT (0),
        Ip           nvarchar(64) NULL,
        Alta         datetime NOT NULL CONSTRAINT DF_MCP_CODIGO_EMAIL_Alta DEFAULT (GETUTCDATE()),
        TicketHash   char(64) NULL,
        TicketExpira datetime NULL
    );
    CREATE INDEX IX_MCP_CODIGO_EMAIL_Email ON dbo.MCP_CODIGO_EMAIL (Email, Alta);
    CREATE INDEX IX_MCP_CODIGO_EMAIL_Ticket ON dbo.MCP_CODIGO_EMAIL (TicketHash);
END
GO

-- La base y el metodo de ingreso viajan con cada autorizacion (un mail puede tener varias bases).
IF COL_LENGTH(N'dbo.MCP_CODIGO', N'Base') IS NULL         ALTER TABLE dbo.MCP_CODIGO ADD Base nvarchar(100) NULL;
GO
IF COL_LENGTH(N'dbo.MCP_CODIGO', N'Metodo') IS NULL       ALTER TABLE dbo.MCP_CODIGO ADD Metodo nvarchar(10) NULL;
GO
IF COL_LENGTH(N'dbo.MCP_REFRESH_TOKEN', N'Base') IS NULL  ALTER TABLE dbo.MCP_REFRESH_TOKEN ADD Base nvarchar(100) NULL;
GO
IF COL_LENGTH(N'dbo.MCP_REFRESH_TOKEN', N'Metodo') IS NULL ALTER TABLE dbo.MCP_REFRESH_TOKEN ADD Metodo nvarchar(10) NULL;
GO

-- Permisos del login del almacen OAuth.
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[MCP_BASE]         TO [$(MCP_AUTH_LOGIN)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[MCP_ACCESO]       TO [$(MCP_AUTH_LOGIN)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[MCP_CODIGO_EMAIL] TO [$(MCP_AUTH_LOGIN)];
-- Lectura de las tablas centrales de AlfaCore SOLO en las columnas necesarias (sin password ni dbpassword).
GRANT SELECT ON OBJECT::[dbo].[users]    ([user], [idcliente])                              TO [$(MCP_AUTH_LOGIN)];
GRANT SELECT ON OBJECT::[dbo].[bases]    ([id], [idcliente], [nombre], [dbserver], [dbname]) TO [$(MCP_AUTH_LOGIN)];
GRANT SELECT ON OBJECT::[dbo].[Clientes] ([idcliente], [nombre])                            TO [$(MCP_AUTH_LOGIN)];
GO

-- Resumen (revisar a ojo: tablas MCP_* completas; users/bases/Clientes solo SELECT por columna).
SELECT OBJECT_NAME(p.major_id) AS Sobre, COL_NAME(p.major_id, p.minor_id) AS Columna, p.permission_name AS Permiso
FROM sys.database_permissions p
JOIN sys.database_principals dp ON dp.principal_id = p.grantee_principal_id
WHERE dp.name = N'$(MCP_AUTH_LOGIN)'
ORDER BY 1, 2, 3;
GO
SET NOEXEC OFF;
GO

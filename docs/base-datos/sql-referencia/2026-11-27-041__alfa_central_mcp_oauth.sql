-- MCP etapa 5 / script 10: tablas del servidor OAuth (usuarios, clientes, codigos, refresh tokens, eventos).
-- Idempotente. NO tocar tablas de AlfaCore: son MCP_*. Se crean en la base que decidas para el almacen OAuth
-- (recomendado: ALFA_CENTRAL, para que despues sirva a todas las bases/tenants; para pruebas, la misma base de pruebas).
-- Sin contrasenas en este script: el login del almacen (ver abajo) se crea con variable sqlcmd o a mano.
--
-- (Antes: dir-mcp/010_oauth_tablas.sql; ya aplicado en ALFA_CENTRAL el 2026-11-27. Re-ejecutarlo no cambia nada.)
-- Ejecutar:  sqlcmd -S <servidor> -d <base_almacen> -f i:65001 -i 2026-11-27-041__alfa_central_mcp_oauth.sql
-- Despues, en SSMS o sqlcmd (Modo SQLCMD): dir-mcp/011_oauth_login_almacen.sql crea el login con permisos SOLO sobre estas tablas.
SET NOCOUNT ON;
GO
IF OBJECT_ID(N'dbo.MCP_USUARIO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MCP_USUARIO
    (
        Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MCP_USUARIO PRIMARY KEY,
        Usuario        nvarchar(100) NOT NULL,
        Nombre         nvarchar(100) NOT NULL,
        ClaveHash      nvarchar(300) NOT NULL,           -- pbkdf2-sha256$iter$sal$hash (nunca la clave)
        Activo         bit NOT NULL CONSTRAINT DF_MCP_USUARIO_Activo DEFAULT (1),
        Intentos       int NOT NULL CONSTRAINT DF_MCP_USUARIO_Intentos DEFAULT (0),
        BloqueadoHasta datetime NULL,                    -- UTC
        UltimoLogin    datetime NULL,                    -- UTC
        Base           nvarchar(100) NULL,               -- reservado: base/tenant a la que accede (multi-base)
        Alta           datetime NOT NULL CONSTRAINT DF_MCP_USUARIO_Alta DEFAULT (GETUTCDATE()),
        TotpSecreto    nvarchar(300) NULL,               -- segundo factor: secreto TOTP CIFRADO (AES-GCM); nunca en claro
        TotpUltimoPaso bigint NOT NULL CONSTRAINT DF_MCP_USUARIO_TotpPaso DEFAULT (0)   -- ultimo paso de 30 s usado (anti-replay)
    );
    CREATE UNIQUE INDEX UX_MCP_USUARIO_Usuario ON dbo.MCP_USUARIO (Usuario);
END
GO
-- Si la tabla ya existia de antes (sin segundo factor), se agregan las columnas sin tocar los datos.
IF COL_LENGTH(N'dbo.MCP_USUARIO', N'TotpSecreto') IS NULL
    ALTER TABLE dbo.MCP_USUARIO ADD TotpSecreto nvarchar(300) NULL;
GO
IF COL_LENGTH(N'dbo.MCP_USUARIO', N'TotpUltimoPaso') IS NULL
    ALTER TABLE dbo.MCP_USUARIO ADD TotpUltimoPaso bigint NOT NULL CONSTRAINT DF_MCP_USUARIO_TotpPaso DEFAULT (0);
GO
IF OBJECT_ID(N'dbo.MCP_CLIENTE_OAUTH', N'U') IS NULL
    CREATE TABLE dbo.MCP_CLIENTE_OAUTH
    (
        ClientId     nvarchar(100) NOT NULL CONSTRAINT PK_MCP_CLIENTE_OAUTH PRIMARY KEY,
        Nombre       nvarchar(100) NOT NULL,
        RedirectUris nvarchar(max) NOT NULL,             -- una URI por linea
        Alta         datetime NOT NULL                   -- UTC
    );
GO
IF OBJECT_ID(N'dbo.MCP_CODIGO', N'U') IS NULL
    CREATE TABLE dbo.MCP_CODIGO
    (
        CodigoHash    char(64) NOT NULL CONSTRAINT PK_MCP_CODIGO PRIMARY KEY,   -- SHA-256 del codigo (el codigo no se guarda)
        ClientId      nvarchar(100) NOT NULL,
        UsuarioId     int NOT NULL,
        RedirectUri   nvarchar(2000) NOT NULL,
        CodeChallenge nvarchar(128) NOT NULL,
        Scope         nvarchar(200) NOT NULL,
        Resource      nvarchar(500) NOT NULL,
        Expira        datetime NOT NULL,                 -- UTC
        Usado         bit NOT NULL CONSTRAINT DF_MCP_CODIGO_Usado DEFAULT (0)
    );
GO
IF OBJECT_ID(N'dbo.MCP_REFRESH_TOKEN', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MCP_REFRESH_TOKEN
    (
        TokenHash   char(64) NOT NULL CONSTRAINT PK_MCP_REFRESH_TOKEN PRIMARY KEY,  -- SHA-256 (el token no se guarda)
        ClientId    nvarchar(100) NOT NULL,
        UsuarioId   int NOT NULL,
        Scope       nvarchar(200) NOT NULL,
        Resource    nvarchar(500) NOT NULL,
        Expira      datetime NOT NULL,                   -- UTC
        Familia     uniqueidentifier NOT NULL,           -- cadena de rotaciones: si se reusa un token viejo se revoca toda
        Revocado    bit NOT NULL CONSTRAINT DF_MCP_REFRESH_Revocado DEFAULT (0),
        Reemplazado bit NOT NULL CONSTRAINT DF_MCP_REFRESH_Reemplazado DEFAULT (0),
        Alta        datetime NOT NULL CONSTRAINT DF_MCP_REFRESH_Alta DEFAULT (GETUTCDATE())
    );
    CREATE INDEX IX_MCP_REFRESH_Familia ON dbo.MCP_REFRESH_TOKEN (Familia);
    CREATE INDEX IX_MCP_REFRESH_UsuarioCliente ON dbo.MCP_REFRESH_TOKEN (UsuarioId, ClientId);
END
GO
IF OBJECT_ID(N'dbo.MCP_EVENTO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MCP_EVENTO
    (
        Id       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MCP_EVENTO PRIMARY KEY,
        Fecha    datetime NOT NULL CONSTRAINT DF_MCP_EVENTO_Fecha DEFAULT (GETUTCDATE()),   -- UTC
        Evento   nvarchar(50) NOT NULL,                  -- login_ok, login_fallido, usuario_bloqueado, codigo_reutilizado, refresh_reutilizado, ...
        Usuario  nvarchar(100) NULL,
        ClientId nvarchar(100) NULL,
        Ip       nvarchar(64) NULL,
        Detalle  nvarchar(1000) NULL
    );
    CREATE INDEX IX_MCP_EVENTO_Fecha ON dbo.MCP_EVENTO (Fecha DESC);
END
GO

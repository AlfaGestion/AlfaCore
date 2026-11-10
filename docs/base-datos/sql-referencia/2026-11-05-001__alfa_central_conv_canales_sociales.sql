-- Ejecutar contra ALFA_CENTRAL. No ejecutar en las bases de clientes.
-- AlfaCore lo aplica solo la primera vez que se usa la conexión guiada de Instagram/Facebook
-- (CentralCanalesSocialesSchema, mismo texto); también se puede correr a mano. Idempotente.
-- Conexión guiada de Instagram y Facebook Messenger (2026-11-05): los mensajes de todos los clientes
-- llegan a un único webhook de la app de ALFA NET; esta tabla dice a qué base pertenece cada cuenta.
--
-- CONV_CANALES_SOCIALES: una fila por conexión.
--   Canal INSTAGRAM o FACEBOOK (independientes: una base puede tener uno solo).
--   CuentaId: id de la cuenta profesional de Instagram (user_id) o id de la página de Facebook;
--             es el entry.id que manda Meta en el webhook.
--   CuentaIdAlterno: id con alcance de app de Instagram (por si Meta lo manda en lugar del user_id).
--   Estado ACTIVA o DESCONECTADA. Una cuenta solo puede estar ACTIVA en una base.
--   El token NO se guarda acá: vive en TA_CONFIGURACION de la base del cliente, como antes.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.bases', N'U') IS NULL
    THROW 50000, N'La base destino no contiene el catálogo central de bases (dbo.bases).', 1;

IF OBJECT_ID(N'dbo.CONV_CANALES_SOCIALES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_CANALES_SOCIALES
    (
        IdConexion          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CONV_CANALES_SOCIALES PRIMARY KEY,
        IdBase              int            NOT NULL,
        Canal               varchar(20)    NOT NULL,
        CuentaId            nvarchar(64)   NOT NULL,
        CuentaIdAlterno     nvarchar(64)   NULL,
        CuentaNombre        nvarchar(200)  NULL,
        CuentaUsuario       nvarchar(200)  NULL,
        FotoUrl             nvarchar(1000) NULL,
        Estado              varchar(15)    NOT NULL CONSTRAINT DF_CONV_CANALES_SOCIALES_Estado DEFAULT ('ACTIVA'),
        TokenVenceUtc       datetime2(0)   NULL,
        FechaRenovacionUtc  datetime2(0)   NULL,
        UltimoError         nvarchar(500)  NULL,
        ConectadoPor        nvarchar(100)  NULL,
        FechaConexion       datetime       NOT NULL CONSTRAINT DF_CONV_CANALES_SOCIALES_FechaConexion DEFAULT (GETDATE()),
        DesconectadoPor     nvarchar(100)  NULL,
        FechaDesconexion    datetime       NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CONV_CANALES_SOCIALES') AND name = N'UX_CONV_CANALES_SOCIALES_Cuenta')
    CREATE UNIQUE INDEX UX_CONV_CANALES_SOCIALES_Cuenta ON dbo.CONV_CANALES_SOCIALES (Canal, CuentaId) WHERE Estado = 'ACTIVA';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CONV_CANALES_SOCIALES') AND name = N'IX_CONV_CANALES_SOCIALES_Base')
    CREATE INDEX IX_CONV_CANALES_SOCIALES_Base ON dbo.CONV_CANALES_SOCIALES (IdBase, Canal, Estado);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CONV_CANALES_SOCIALES') AND name = N'IX_CONV_CANALES_SOCIALES_Alterno')
    CREATE INDEX IX_CONV_CANALES_SOCIALES_Alterno ON dbo.CONV_CANALES_SOCIALES (Canal, CuentaIdAlterno) WHERE CuentaIdAlterno IS NOT NULL;

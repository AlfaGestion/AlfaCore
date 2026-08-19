-- ============================================================
-- Portal Cliente - recuperacion de contraseña
--
-- Tabla minima para tokens de reset de clave (MA_CUENTASADIC.CLAVE).
-- Solo se guarda el hash SHA-256 del token, nunca el token en texto
-- plano. Cada token tiene vencimiento y queda marcado como usado
-- despues de aplicarse (o al generarse uno nuevo para el mismo
-- cliente). No toca MA_CUENTASADIC ni el circuito de login actual.
-- ============================================================

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_CLIENTE_RESET_TOKEN
    (
        IdToken                 int IDENTITY(1,1) NOT NULL,
        CodigoCliente            nvarchar(30) NOT NULL,
        IdWeb                    nvarchar(100) NULL,
        IdBase                   int NULL,
        TokenHash                nvarchar(64) NOT NULL,
        FechaHora_Creacion       datetime NOT NULL CONSTRAINT DF_ALFACORE_CLIENTE_RESET_TOKEN_FHCreacion DEFAULT (GETDATE()),
        FechaHora_Expiracion     datetime NOT NULL,
        Usado                    bit NOT NULL CONSTRAINT DF_ALFACORE_CLIENTE_RESET_TOKEN_Usado DEFAULT (0),
        FechaHora_Uso            datetime NULL,
        CONSTRAINT PK_ALFACORE_CLIENTE_RESET_TOKEN PRIMARY KEY CLUSTERED (IdToken ASC)
    );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'UX_ALFACORE_CLIENTE_RESET_TOKEN_HASH'
         AND object_id = OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN')
   )
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_ALFACORE_CLIENTE_RESET_TOKEN_HASH
        ON dbo.ALFACORE_CLIENTE_RESET_TOKEN (TokenHash ASC);
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_ALFACORE_CLIENTE_RESET_TOKEN_CLIENTE'
         AND object_id = OBJECT_ID(N'dbo.ALFACORE_CLIENTE_RESET_TOKEN')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_ALFACORE_CLIENTE_RESET_TOKEN_CLIENTE
        ON dbo.ALFACORE_CLIENTE_RESET_TOKEN (CodigoCliente ASC, Usado ASC);
END;
GO

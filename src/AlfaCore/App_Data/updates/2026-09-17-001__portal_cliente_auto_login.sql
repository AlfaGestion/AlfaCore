/* Token temporal de acceso automático al Portal Cliente.
   El servicio también contiene una guardia de compatibilidad para instalaciones antiguas. */
IF OBJECT_ID(N'dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN
    (
        IdToken int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN PRIMARY KEY,
        CodigoCliente nvarchar(30) NOT NULL,
        RazonSocial nvarchar(250) NOT NULL,
        Email nvarchar(320) NOT NULL,
        IdWeb nvarchar(100) NOT NULL,
        IdBase int NOT NULL,
        TokenHash nvarchar(64) NOT NULL,
        FechaHora_Creacion datetime NOT NULL CONSTRAINT DF_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN_Creacion DEFAULT (GETDATE()),
        FechaHora_Expiracion datetime NOT NULL,
        Usado bit NOT NULL CONSTRAINT DF_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN_Usado DEFAULT (0),
        FechaHora_Uso datetime NULL
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN_HASH'
      AND object_id = OBJECT_ID(N'dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN')
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_ALFACORE_CLIENTE_AUTOLOGIN_TOKEN_HASH
        ON dbo.ALFACORE_CLIENTE_AUTOLOGIN_TOKEN (TokenHash);
END;
GO

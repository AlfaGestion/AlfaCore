SET NOCOUNT ON;

-- Clientes que comparten tarifas (Carga de viajes > Tarifas).
-- Un cliente principal puede tener clientes vinculados: sus tarifas de cliente se duplican y actualizan en ellos.
-- TA_TARIFA.IdTarifaOrigen identifica, en el cliente vinculado, la tarifa del principal de la que es copia.

IF OBJECT_ID(N'dbo.TA_TARIFA', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.TA_TARIFA_CLIENTES_VINCULADOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TA_TARIFA_CLIENTES_VINCULADOS
    (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TA_TARIFA_CLIENTES_VINCULADOS PRIMARY KEY,
        ClientePrincipal nvarchar(15) NOT NULL,
        ClienteVinculado nvarchar(15) NOT NULL,
        FECHAHORA_ALTA datetime NOT NULL CONSTRAINT DF_TA_TARIFA_CLIENTES_VINCULADOS_FECHAHORA_ALTA DEFAULT (GETDATE()),
        USUARIO nvarchar(50) NULL,
        CONSTRAINT UQ_TA_TARIFA_CLIENTES_VINCULADOS_ClienteVinculado UNIQUE (ClienteVinculado)
    );
END;
GO

IF OBJECT_ID(N'dbo.TA_TARIFA', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.TA_TARIFA', N'IdTarifaOrigen') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD IdTarifaOrigen int NULL;
END;
GO

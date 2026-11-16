SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET NOCOUNT ON;

-- ============================================================
-- Modulo Ventas / POS - Salon, etapas 2 y 3
--  * POS_COMANDA: una por cada envio a cocina/barra (por destino). Es
--    el "ticket" que ve la pantalla de cocina y que imprime la
--    comandera (IMPRESA la marca la estacion que la reclama primero).
--  * POS_ARTICULO_AGOTADO: platos agotados por punto de venta; no se
--    pueden pedir y desaparecen de la carta QR.
--  * POS_MESA_LLAMADO: "llamar al mozo" / "pedir la cuenta" desde la
--    carta QR de la mesa.
--  * POS_CARTA_FAMILIA: familias que forman la carta QR (sin filas =
--    todas las familias con precio).
--  * POS_PUNTOVENTA.QR_MODO: NO / CARTA (solo ver) / PEDIDO (ver y pedir).
--    QR_AUTOACEPTA = 1: lo pedido por QR sale a cocina sin que el mozo
--    lo confirme.
-- Idempotente: puede ejecutarse varias veces sin romper datos.
-- ============================================================

IF OBJECT_ID(N'dbo.POS_COMANDA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_COMANDA(
        ID                  int IDENTITY(1,1) NOT NULL,
        IDPEDIDO            int          NOT NULL,
        IDPUNTOVENTA        int          NOT NULL,
        DESTINO             nvarchar(10) NOT NULL,
        FECHAHORA           datetime     NOT NULL CONSTRAINT DF_POS_COMANDA_FECHAHORA DEFAULT (GETDATE()),
        USUARIO             nvarchar(50) NULL,
        IMPRESA             bit          NOT NULL CONSTRAINT DF_POS_COMANDA_IMPRESA DEFAULT (0),
        FECHAHORA_IMPRESION datetime     NULL,
        ESTACION            nvarchar(50) NULL,
        CONSTRAINT PK_POS_COMANDA PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_COMANDA_PV_DESTINO ON dbo.POS_COMANDA(IDPUNTOVENTA, DESTINO, IMPRESA, FECHAHORA);
    CREATE NONCLUSTERED INDEX IX_POS_COMANDA_IDPEDIDO ON dbo.POS_COMANDA(IDPEDIDO);
END;
GO

IF COL_LENGTH(N'dbo.POS_PEDIDO_DET', N'IDCOMANDA') IS NULL
    ALTER TABLE dbo.POS_PEDIDO_DET ADD IDCOMANDA int NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_POS_PEDIDO_DET_IDCOMANDA' AND object_id = OBJECT_ID(N'dbo.POS_PEDIDO_DET'))
    CREATE NONCLUSTERED INDEX IX_POS_PEDIDO_DET_IDCOMANDA ON dbo.POS_PEDIDO_DET(IDCOMANDA);
GO

IF OBJECT_ID(N'dbo.POS_ARTICULO_AGOTADO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_ARTICULO_AGOTADO(
        IDPUNTOVENTA    int          NOT NULL,
        IDARTICULO      nvarchar(25) NOT NULL,
        FECHAHORA       datetime     NOT NULL CONSTRAINT DF_POS_ARTICULO_AGOTADO_FECHAHORA DEFAULT (GETDATE()),
        USUARIO         nvarchar(50) NULL,
        CONSTRAINT PK_POS_ARTICULO_AGOTADO PRIMARY KEY CLUSTERED (IDPUNTOVENTA ASC, IDARTICULO ASC)
    );
END;
GO

IF OBJECT_ID(N'dbo.POS_MESA_LLAMADO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_MESA_LLAMADO(
        ID                  int IDENTITY(1,1) NOT NULL,
        IDPUNTOVENTA        int          NOT NULL,
        IDMESA              int          NOT NULL,
        IDPEDIDO            int          NULL,
        TIPO                nvarchar(10) NOT NULL,
        FECHAHORA           datetime     NOT NULL CONSTRAINT DF_POS_MESA_LLAMADO_FECHAHORA DEFAULT (GETDATE()),
        ATENDIDO            bit          NOT NULL CONSTRAINT DF_POS_MESA_LLAMADO_ATENDIDO DEFAULT (0),
        FECHAHORA_ATENCION  datetime     NULL,
        USUARIO_ATENCION    nvarchar(50) NULL,
        CONSTRAINT PK_POS_MESA_LLAMADO PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_MESA_LLAMADO_PV ON dbo.POS_MESA_LLAMADO(IDPUNTOVENTA, ATENDIDO);
END;
GO

IF OBJECT_ID(N'dbo.POS_CARTA_FAMILIA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_CARTA_FAMILIA(
        IDPUNTOVENTA    int          NOT NULL,
        IDFAMILIA       nvarchar(30) NOT NULL,
        ORDEN           int          NOT NULL CONSTRAINT DF_POS_CARTA_FAMILIA_ORDEN DEFAULT (0),
        CONSTRAINT PK_POS_CARTA_FAMILIA PRIMARY KEY CLUSTERED (IDPUNTOVENTA ASC, IDFAMILIA ASC)
    );
END;
GO

IF COL_LENGTH(N'dbo.POS_PUNTOVENTA', N'QR_MODO') IS NULL
    ALTER TABLE dbo.POS_PUNTOVENTA ADD QR_MODO nvarchar(10) NULL;
GO
IF COL_LENGTH(N'dbo.POS_PUNTOVENTA', N'QR_AUTOACEPTA') IS NULL
    ALTER TABLE dbo.POS_PUNTOVENTA ADD QR_AUTOACEPTA bit NULL;
GO
IF COL_LENGTH(N'dbo.POS_PUNTOVENTA', N'QR_MENSAJE') IS NULL
    ALTER TABLE dbo.POS_PUNTOVENTA ADD QR_MENSAJE nvarchar(200) NULL;
GO

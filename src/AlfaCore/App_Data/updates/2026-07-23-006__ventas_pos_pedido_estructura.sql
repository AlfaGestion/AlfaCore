SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET NOCOUNT ON;

-- ============================================================
-- Modulo Ventas / POS - Fase 1: Pedidos Delivery / Take Away
-- Un pedido se carga con articulos y queda en curso (PENDIENTE /
-- EN_PREPARACION / LISTO / EN_CAMINO); el comprobante de venta se
-- genera recien al cerrarlo (ENTREGADO), reutilizando el mismo
-- motor de ventas del mostrador (PuntoVentaService.CreateSaleAsync).
-- Prefijo POS_ para identificar facil las tablas del modulo.
-- Idempotente: puede ejecutarse varias veces sin romper datos.
-- ============================================================

IF OBJECT_ID(N'dbo.POS_PEDIDO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_PEDIDO(
        ID                      int IDENTITY(1,1) NOT NULL,
        TC                      nvarchar(4)   NULL,
        IDCOMPROBANTE           nvarchar(13)  NULL,
        IDPUNTOVENTA            int           NOT NULL,
        MODALIDAD               nvarchar(20)  NOT NULL,
        IDCLIENTE               nvarchar(15)  NULL,
        NOMBRECLIENTE           nvarchar(100) NOT NULL,
        TELEFONO                nvarchar(20)  NULL,
        DIRECCION               nvarchar(250) NULL,
        FECHA                   datetime      NOT NULL CONSTRAINT DF_POS_PEDIDO_FECHA DEFAULT (GETDATE()),
        HORARIOPROGRAMADO       datetime      NULL,
        ESTADO                  nvarchar(20)  NOT NULL CONSTRAINT DF_POS_PEDIDO_ESTADO DEFAULT (N'PENDIENTE'),
        OBSERVACIONES           nvarchar(500) NULL,
        UNEGOCIO                nvarchar(4)   NULL,
        IDCAJA                  nvarchar(4)   NULL,
        USUARIO                 nvarchar(50)  NULL,
        FECHAHORA_ALTA          datetime      NULL CONSTRAINT DF_POS_PEDIDO_ALTA DEFAULT (GETDATE()),
        FECHAHORA_MODIFICACION  datetime      NULL,
        CONSTRAINT PK_POS_PEDIDO PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_PEDIDO_IDPUNTOVENTA_ESTADO ON dbo.POS_PEDIDO(IDPUNTOVENTA, ESTADO);
END;

-- Modalidad valida: DELIVERY / TAKEAWAY. Estado valido: PENDIENTE /
-- EN_PREPARACION / LISTO / EN_CAMINO / ENTREGADO / ANULADO.
-- Se validan en la capa de aplicacion (no con CHECK) para poder
-- agregar valores nuevos sin otra migracion.

IF OBJECT_ID(N'dbo.POS_PEDIDO_DET', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_PEDIDO_DET(
        ID              int IDENTITY(1,1) NOT NULL,
        IDPEDIDO        int           NOT NULL,
        IDARTICULO      nvarchar(25)  NOT NULL,
        DESCRIPCION     nvarchar(200) NULL,
        IDUNIDAD        nvarchar(4)   NULL,
        PRESENTACION    nvarchar(50)  NULL,
        CANTIDAD        float         NOT NULL CONSTRAINT DF_POS_PEDIDO_DET_CANTIDAD DEFAULT (1),
        PRECIO          money         NOT NULL CONSTRAINT DF_POS_PEDIDO_DET_PRECIO DEFAULT (0),
        TASAIVA         money         NOT NULL CONSTRAINT DF_POS_PEDIDO_DET_TASAIVA DEFAULT (0),
        EXENTO          bit           NOT NULL CONSTRAINT DF_POS_PEDIDO_DET_EXENTO DEFAULT (0),
        FAMILIA         nvarchar(100) NULL,
        OBSERVACIONES   nvarchar(250) NULL,
        CONSTRAINT PK_POS_PEDIDO_DET PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_PEDIDO_DET_IDPEDIDO ON dbo.POS_PEDIDO_DET(IDPEDIDO);
END;

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET NOCOUNT ON;

-- ============================================================
-- Modulo Ventas / POS - Fase 0: Punto de Venta configurable
-- Introduce el concepto de "Punto de Venta" (modo Mostrador /
-- Salon / Delivery) y, para el modo Salon, la jerarquia
-- Sector -> Mesa (ej. Terraza, Planta Baja, Planta Alta).
-- Prefijo POS_ para identificar facil las tablas del modulo.
-- Idempotente: puede ejecutarse varias veces sin romper datos.
-- ============================================================

IF OBJECT_ID(N'dbo.POS_PUNTOVENTA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_PUNTOVENTA(
        ID                      int IDENTITY(1,1) NOT NULL,
        CODIGO                  nvarchar(4)  NOT NULL,
        NOMBRE                  nvarchar(50) NOT NULL,
        MODO                    nvarchar(20) NOT NULL CONSTRAINT DF_POS_PUNTOVENTA_MODO DEFAULT (N'MOSTRADOR'),
        UNEGOCIO                nvarchar(4)  NULL,
        IDCAJA                  nvarchar(4)  NULL,
        SUCURSAL                nvarchar(4)  NULL,
        ACTIVO                  bit          NOT NULL CONSTRAINT DF_POS_PUNTOVENTA_ACTIVO DEFAULT (1),
        USUARIO                 nvarchar(50) NULL,
        FECHAHORA_ALTA          datetime     NULL CONSTRAINT DF_POS_PUNTOVENTA_ALTA DEFAULT (GETDATE()),
        FECHAHORA_MODIFICACION  datetime     NULL,
        CONSTRAINT PK_POS_PUNTOVENTA PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UX_POS_PUNTOVENTA_CODIGO ON dbo.POS_PUNTOVENTA(CODIGO);
END;

-- Modo valido: MOSTRADOR / SALON / DELIVERY (validado en la capa de aplicacion,
-- no con CHECK, para poder agregar modos nuevos sin otra migracion).

IF OBJECT_ID(N'dbo.POS_SECTOR', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_SECTOR(
        ID                      int IDENTITY(1,1) NOT NULL,
        CODIGO                  nvarchar(4)  NOT NULL,
        IDPUNTOVENTA            int          NOT NULL,
        NOMBRE                  nvarchar(50) NOT NULL,
        ICONO                   nvarchar(50)  NULL,
        IMAGEN                  nvarchar(300) NULL,
        ORDEN                   int          NOT NULL CONSTRAINT DF_POS_SECTOR_ORDEN DEFAULT (0),
        ACTIVO                  bit          NOT NULL CONSTRAINT DF_POS_SECTOR_ACTIVO DEFAULT (1),
        FECHAHORA_ALTA          datetime     NULL CONSTRAINT DF_POS_SECTOR_ALTA DEFAULT (GETDATE()),
        FECHAHORA_MODIFICACION  datetime     NULL,
        CONSTRAINT PK_POS_SECTOR PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_SECTOR_IDPUNTOVENTA ON dbo.POS_SECTOR(IDPUNTOVENTA);
END;

IF OBJECT_ID(N'dbo.POS_MESA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_MESA(
        ID                      int IDENTITY(1,1) NOT NULL,
        CODIGO                  nvarchar(4)  NOT NULL,
        IDSECTOR                int          NOT NULL,
        NOMBRE                  nvarchar(50) NOT NULL,
        CAPACIDAD               int           NULL,
        POSX                    float         NULL,
        POSY                    float         NULL,
        WIDTH                   float         NULL,
        HEIGHT                  float         NULL,
        ICONO                   nvarchar(50)  NULL,
        IMAGEN                  nvarchar(300) NULL,
        ACTIVO                  bit          NOT NULL CONSTRAINT DF_POS_MESA_ACTIVO DEFAULT (1),
        FECHAHORA_ALTA          datetime     NULL CONSTRAINT DF_POS_MESA_ALTA DEFAULT (GETDATE()),
        FECHAHORA_MODIFICACION  datetime     NULL,
        CONSTRAINT PK_POS_MESA PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_MESA_IDSECTOR ON dbo.POS_MESA(IDSECTOR);
END;

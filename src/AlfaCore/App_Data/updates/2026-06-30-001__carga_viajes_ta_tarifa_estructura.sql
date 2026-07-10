-- ============================================================
-- TA_TARIFA — estructura completa con adicionales fijos
-- y columna USUARIOMODIFICACION
-- ============================================================

SET NOCOUNT ON;

-- ── 1. Crear tabla si no existe ───────────────────────────────
IF OBJECT_ID(N'dbo.TA_TARIFA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TA_TARIFA
    (
        ID                      int           IDENTITY(1,1) NOT NULL,
        IdLista                 nvarchar(4)   NOT NULL,
        Nombre                  nvarchar(50)  NULL,
        Importe                 money         NOT NULL CONSTRAINT DF_TA_TARIFA_IMPORTE          DEFAULT (0),
        IDCLIENTE               nvarchar(15)  NULL,
        IDCHOFER                nvarchar(4)   NULL,
        IDDESTINO               nvarchar(4)   NULL,
        IDTIPOVEHICULO          nvarchar(4)   NULL,
        TarifaFletero           bit           NOT NULL CONSTRAINT DF_TA_TARIFA_TARIFAFLETERO    DEFAULT (0),
        PorcentajeAdic          money         NOT NULL CONSTRAINT DF_TA_TARIFA_PORC0            DEFAULT (0),
        PorcentajeAdic1         money         NOT NULL CONSTRAINT DF_TA_TARIFA_PORC1            DEFAULT (0),
        PorcentajeAdic2         money         NOT NULL CONSTRAINT DF_TA_TARIFA_PORC2            DEFAULT (0),
        PorcentajeAdic3         money         NOT NULL CONSTRAINT DF_TA_TARIFA_PORC3            DEFAULT (0),
        PorcentajeAdic4         money         NOT NULL CONSTRAINT DF_TA_TARIFA_PORC4            DEFAULT (0),
        ACTIVO                  bit           NOT NULL CONSTRAINT DF_TA_TARIFA_ACTIVO           DEFAULT (1),
        FECHAHORA_ALTA          datetime      NULL,
        FECHAHORA_MODIFICACION  datetime      NULL,
        AdicionalFijo1Descripcion nvarchar(100) NULL,
        AdicionalFijo1Importe   money         NOT NULL CONSTRAINT DF_TA_TARIFA_AdicionalFijo1Importe DEFAULT (0),
        AdicionalFijo2Descripcion nvarchar(100) NULL,
        AdicionalFijo2Importe   money         NOT NULL CONSTRAINT DF_TA_TARIFA_AdicionalFijo2Importe DEFAULT (0),
        AdicionalFijo3Descripcion nvarchar(100) NULL,
        AdicionalFijo3Importe   money         NOT NULL CONSTRAINT DF_TA_TARIFA_AdicionalFijo3Importe DEFAULT (0),
        USUARIOMODIFICACION     nvarchar(50)  NULL,
        CONSTRAINT PK_TA_TARIFA PRIMARY KEY CLUSTERED (ID ASC)
    );
END;
GO

-- ── 2. Si ya existía, agregar columnas faltantes ─────────────

IF COL_LENGTH(N'dbo.TA_TARIFA', N'PorcentajeAdic') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD PorcentajeAdic money NOT NULL CONSTRAINT DF_TA_TARIFA_PORC0 DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'PorcentajeAdic1') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD PorcentajeAdic1 money NOT NULL CONSTRAINT DF_TA_TARIFA_PORC1 DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'PorcentajeAdic2') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD PorcentajeAdic2 money NOT NULL CONSTRAINT DF_TA_TARIFA_PORC2 DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'PorcentajeAdic3') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD PorcentajeAdic3 money NOT NULL CONSTRAINT DF_TA_TARIFA_PORC3 DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'PorcentajeAdic4') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD PorcentajeAdic4 money NOT NULL CONSTRAINT DF_TA_TARIFA_PORC4 DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'ACTIVO') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD ACTIVO bit NOT NULL CONSTRAINT DF_TA_TARIFA_ACTIVO DEFAULT (1);
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'FECHAHORA_ALTA') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD FECHAHORA_ALTA datetime NULL;
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'FECHAHORA_MODIFICACION') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD FECHAHORA_MODIFICACION datetime NULL;
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'AdicionalFijo1Descripcion') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD AdicionalFijo1Descripcion nvarchar(100) NULL;
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'AdicionalFijo1Importe') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD AdicionalFijo1Importe money NOT NULL CONSTRAINT DF_TA_TARIFA_AdicionalFijo1Importe DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'AdicionalFijo2Descripcion') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD AdicionalFijo2Descripcion nvarchar(100) NULL;
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'AdicionalFijo2Importe') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD AdicionalFijo2Importe money NOT NULL CONSTRAINT DF_TA_TARIFA_AdicionalFijo2Importe DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'AdicionalFijo3Descripcion') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD AdicionalFijo3Descripcion nvarchar(100) NULL;
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'AdicionalFijo3Importe') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD AdicionalFijo3Importe money NOT NULL CONSTRAINT DF_TA_TARIFA_AdicionalFijo3Importe DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.TA_TARIFA', N'USUARIOMODIFICACION') IS NULL
BEGIN
    ALTER TABLE dbo.TA_TARIFA
        ADD USUARIOMODIFICACION nvarchar(50) NULL;
END;
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
SET NOCOUNT ON;

-- Fase 0.5 - Turno de caja (apertura/cierre) para POS_PUNTOVENTA.
-- Resuelve la "fecha operativa": un punto de venta puede abrir un día y seguir
-- trabajando después de medianoche del día siguiente; la fecha operativa queda
-- fija desde la apertura del turno y no cambia aunque el reloj real avance de día.
-- Toda venta (Mostrador o cierre de pedido de Delivery/Take Away/Salón) se ata
-- al turno abierto del punto de venta en el momento del cobro.
-- Idempotente: puede ejecutarse varias veces sin romper datos.

IF OBJECT_ID(N'dbo.POS_TURNO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_TURNO (
        ID                       int IDENTITY(1,1) NOT NULL,
        IDPUNTOVENTA             int NOT NULL,
        FECHAOPERATIVA           date NOT NULL,
        FECHAHORA_APERTURA       datetime NOT NULL,
        FECHAHORA_CIERRE         datetime NULL,
        USUARIO_APERTURA         nvarchar(50) NULL,
        USUARIO_CIERRE           nvarchar(50) NULL,
        SALDOINICIAL             money NOT NULL CONSTRAINT DF_POS_TURNO_SALDOINICIAL DEFAULT (0),
        SALDOFINAL_DECLARADO     money NULL,
        SALDOFINAL_TEORICO       money NULL,
        DIFERENCIA               money NULL,
        ESTADO                   nvarchar(20) NOT NULL CONSTRAINT DF_POS_TURNO_ESTADO DEFAULT ('ABIERTO'),
        OBSERVACIONES            nvarchar(500) NULL,
        FECHAHORA_ALTA           datetime NULL CONSTRAINT DF_POS_TURNO_ALTA DEFAULT (GETDATE()),
        FECHAHORA_MODIFICACION   datetime NULL,
        CONSTRAINT PK_POS_TURNO PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_TURNO_PUNTOVENTA_ESTADO
        ON dbo.POS_TURNO(IDPUNTOVENTA, ESTADO);
END;

IF OBJECT_ID(N'dbo.POS_DENOMINACION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_DENOMINACION (
        ID       int IDENTITY(1,1) NOT NULL,
        VALOR    money NOT NULL,
        TIPO     nvarchar(10) NOT NULL CONSTRAINT DF_POS_DENOMINACION_TIPO DEFAULT ('BILLETE'),
        ORDEN    int NOT NULL CONSTRAINT DF_POS_DENOMINACION_ORDEN DEFAULT (0),
        ACTIVO   bit NOT NULL CONSTRAINT DF_POS_DENOMINACION_ACTIVO DEFAULT (1),
        CONSTRAINT PK_POS_DENOMINACION PRIMARY KEY CLUSTERED (ID ASC)
    );

    INSERT INTO dbo.POS_DENOMINACION (VALOR, TIPO, ORDEN) VALUES
        (10000, 'BILLETE', 1),
        (2000,  'BILLETE', 2),
        (1000,  'BILLETE', 3),
        (500,   'BILLETE', 4),
        (200,   'BILLETE', 5),
        (100,   'BILLETE', 6),
        (10,    'MONEDA',  7),
        (5,     'MONEDA',  8),
        (2,     'MONEDA',  9),
        (1,     'MONEDA',  10);
END;

IF OBJECT_ID(N'dbo.POS_TURNO_BILLETES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_TURNO_BILLETES (
        ID              int IDENTITY(1,1) NOT NULL,
        IDTURNO         int NOT NULL,
        MOMENTO         nvarchar(10) NOT NULL,
        IDDENOMINACION  int NOT NULL,
        CANTIDAD        int NOT NULL CONSTRAINT DF_POS_TURNO_BILLETES_CANTIDAD DEFAULT (0),
        CONSTRAINT PK_POS_TURNO_BILLETES PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_TURNO_BILLETES_TURNO
        ON dbo.POS_TURNO_BILLETES(IDTURNO, MOMENTO);
END;

IF OBJECT_ID(N'dbo.POS_TURNO_MOVIMIENTO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_TURNO_MOVIMIENTO (
        ID                int IDENTITY(1,1) NOT NULL,
        IDTURNO           int NOT NULL,
        TC                nvarchar(4) NULL,
        IDCOMPROBANTE     nvarchar(13) NULL,
        IMPORTETOTAL      money NOT NULL CONSTRAINT DF_POS_TURNO_MOVIMIENTO_TOTAL DEFAULT (0),
        IMPORTEEFECTIVO   money NOT NULL CONSTRAINT DF_POS_TURNO_MOVIMIENTO_EFECTIVO DEFAULT (0),
        FECHAHORA         datetime NOT NULL CONSTRAINT DF_POS_TURNO_MOVIMIENTO_FECHA DEFAULT (GETDATE()),
        CONSTRAINT PK_POS_TURNO_MOVIMIENTO PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_TURNO_MOVIMIENTO_TURNO
        ON dbo.POS_TURNO_MOVIMIENTO(IDTURNO);
END;

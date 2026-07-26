SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
SET NOCOUNT ON;

-- Fase 0.5 (revisión) - El turno de caja pasa de ser por punto de venta a ser por
-- caja individual: un punto de venta puede tener varias cajas físicas (V_Ta_Cajas /
-- TA_USUARIOS.IDCAJA en el sistema de escritorio) y cada una necesita su propio
-- saldo inicial y su propio arqueo. La caja se resuelve del usuario logueado
-- (PuntoVentaContextDto.CajaActual, ya existente), no se elige a mano.
-- Además se agrega "Corte X": un arqueo parcial que NO cierra el turno, usado para
-- relevos de cajero o controles durante el día, con el usuario que lo hizo.
-- Idempotente: puede ejecutarse varias veces sin romper datos.

IF COL_LENGTH(N'dbo.POS_TURNO', N'IDCAJA') IS NULL
    ALTER TABLE dbo.POS_TURNO ADD IDCAJA nvarchar(4) NOT NULL CONSTRAINT DF_POS_TURNO_IDCAJA DEFAULT ('1') WITH VALUES;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_POS_TURNO_PUNTOVENTA_ESTADO' AND object_id = OBJECT_ID(N'dbo.POS_TURNO'))
    DROP INDEX IX_POS_TURNO_PUNTOVENTA_ESTADO ON dbo.POS_TURNO;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_POS_TURNO_PUNTOVENTA_CAJA_ESTADO' AND object_id = OBJECT_ID(N'dbo.POS_TURNO'))
    CREATE NONCLUSTERED INDEX IX_POS_TURNO_PUNTOVENTA_CAJA_ESTADO
        ON dbo.POS_TURNO(IDPUNTOVENTA, IDCAJA, ESTADO);

IF OBJECT_ID(N'dbo.POS_TURNO_CORTE_X', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_TURNO_CORTE_X (
        ID                 int IDENTITY(1,1) NOT NULL,
        IDTURNO            int NOT NULL,
        USUARIO            nvarchar(50) NULL,
        FECHAHORA          datetime NOT NULL CONSTRAINT DF_POS_TURNO_CORTEX_FECHA DEFAULT (GETDATE()),
        SALDOTEORICO       money NOT NULL CONSTRAINT DF_POS_TURNO_CORTEX_TEORICO DEFAULT (0),
        SALDOCONTADO       money NULL,
        DIFERENCIA         money NULL,
        OBSERVACIONES      nvarchar(500) NULL,
        CONSTRAINT PK_POS_TURNO_CORTE_X PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_TURNO_CORTE_X_TURNO
        ON dbo.POS_TURNO_CORTE_X(IDTURNO);
END;

IF OBJECT_ID(N'dbo.POS_TURNO_CORTE_X_BILLETES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.POS_TURNO_CORTE_X_BILLETES (
        ID              int IDENTITY(1,1) NOT NULL,
        IDCORTE         int NOT NULL,
        IDDENOMINACION  int NOT NULL,
        CANTIDAD        int NOT NULL CONSTRAINT DF_POS_TURNO_CORTEX_BILLETES_CANTIDAD DEFAULT (0),
        CONSTRAINT PK_POS_TURNO_CORTE_X_BILLETES PRIMARY KEY CLUSTERED (ID ASC)
    );

    CREATE NONCLUSTERED INDEX IX_POS_TURNO_CORTE_X_BILLETES_CORTE
        ON dbo.POS_TURNO_CORTE_X_BILLETES(IDCORTE);
END;

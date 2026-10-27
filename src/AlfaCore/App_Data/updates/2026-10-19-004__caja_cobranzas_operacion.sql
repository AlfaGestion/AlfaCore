SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Journal compartido: un recibo confirmado por operación y por movimiento bancario.
IF OBJECT_ID(N'dbo.ALFACORE_COBRANZA_OPERACION',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_COBRANZA_OPERACION
    (
        Operacion UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        MovimientoId BIGINT NULL,
        FacturaId INT NOT NULL,
        CobranzaId INT NOT NULL,
        Comprobante NVARCHAR(13) NOT NULL,
        NumeroAsiento INT NOT NULL,
        Importe DECIMAL(18,2) NOT NULL,
        SolicitudHash VARCHAR(64) NOT NULL,
        Unidad NVARCHAR(4) NOT NULL,
        Usuario NVARCHAR(50) NOT NULL,
        FechaUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ALFACORE_COBRANZA_OPERACION') AND name=N'UX_ALFACORE_COBRANZA_Movimiento')
    CREATE UNIQUE INDEX UX_ALFACORE_COBRANZA_Movimiento ON dbo.ALFACORE_COBRANZA_OPERACION(MovimientoId) WHERE MovimientoId IS NOT NULL;
GO

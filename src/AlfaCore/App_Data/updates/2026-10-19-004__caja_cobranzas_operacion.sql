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
-- Compat 80: un índice filtrado hace fallar los INSERT/UPDATE de SqlClient (1934, ARITHABORT);
-- ahí no se crea y la unicidad por movimiento queda a cargo de CobranzasService.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ALFACORE_COBRANZA_OPERACION') AND name=N'UX_ALFACORE_COBRANZA_Movimiento')
   AND (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) >= 90
    CREATE UNIQUE INDEX UX_ALFACORE_COBRANZA_Movimiento ON dbo.ALFACORE_COBRANZA_OPERACION(MovimientoId) WHERE MovimientoId IS NOT NULL;
GO

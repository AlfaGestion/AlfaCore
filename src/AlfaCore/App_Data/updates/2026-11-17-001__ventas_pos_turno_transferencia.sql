SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET NOCOUNT ON;

-- ============================================================
-- Modulo Ventas / POS - transferencia a otra caja al cerrar el turno
--  * POS_TURNO.FONDO_QUEDA: efectivo que queda en la caja para el
--    turno siguiente (la apertura lo propone como saldo inicial).
--  * POS_TURNO.TRANSF_IDCAJA / TRANSF_CPTE / TRANSF_IMPORTE: caja
--    destino, comprobante (TC de EQU_CYB_Transferencias, CJA) e
--    importe total de la transferencia hecha al cerrar. El asiento
--    se graba en MV_ASIENTOS igual que CybTrans del legacy: misma
--    cuenta del medio de pago, H en la caja origen y D en la destino
--    (lo lee VT_TRANSFERENCIASCAJA).
-- Idempotente: puede ejecutarse varias veces sin romper datos.
-- ============================================================

IF OBJECT_ID(N'dbo.POS_TURNO', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.POS_TURNO', N'FONDO_QUEDA') IS NULL
        ALTER TABLE dbo.POS_TURNO ADD FONDO_QUEDA decimal(18, 2) NULL;

    IF COL_LENGTH(N'dbo.POS_TURNO', N'TRANSF_IDCAJA') IS NULL
        ALTER TABLE dbo.POS_TURNO ADD TRANSF_IDCAJA nvarchar(4) NULL;

    IF COL_LENGTH(N'dbo.POS_TURNO', N'TRANSF_CPTE') IS NULL
        ALTER TABLE dbo.POS_TURNO ADD TRANSF_CPTE nvarchar(30) NULL;

    IF COL_LENGTH(N'dbo.POS_TURNO', N'TRANSF_IMPORTE') IS NULL
        ALTER TABLE dbo.POS_TURNO ADD TRANSF_IMPORTE decimal(18, 2) NULL;
END
GO

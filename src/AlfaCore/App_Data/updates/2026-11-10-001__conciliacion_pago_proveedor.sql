/*
    2026-11-10 · Conciliación bancaria: pago a proveedores desde un egreso del extracto.
    La orden de pago (OPG/PGFP, OrdenPagoService) puede nacer de un movimiento bancario: el journal ALFACORE_PAGO_OPERACION
    guarda el MovimientoId del extracto (BK_ExtractoMovimiento) para vincularlo y poder liberarlo si la orden se anula.
    Idempotente. Compatibility level 80/100: sin índices filtrados.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_PAGO_OPERACION', N'U') IS NULL RETURN;

IF COL_LENGTH(N'dbo.ALFACORE_PAGO_OPERACION', N'MovimientoId') IS NULL
    ALTER TABLE dbo.ALFACORE_PAGO_OPERACION ADD MovimientoId bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_PAGO_OPERACION') AND name = N'IX_ALFACORE_PAGO_OPERACION_Movimiento')
    EXEC sys.sp_executesql N'CREATE INDEX IX_ALFACORE_PAGO_OPERACION_Movimiento ON dbo.ALFACORE_PAGO_OPERACION (MovimientoId);';

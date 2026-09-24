/*
  Acelera el visor genérico de comprobantes.

  Las tablas de comprobantes y MV_APLICACION ya cuentan con índices por sus
  claves naturales en las bases instaladas. El asiento, en cambio, tenía
  índices separados por cada columna; el visor lo busca por la clave completa
  del comprobante.
*/
IF OBJECT_ID(N'dbo.MV_ASIENTOS', N'U') IS NULL
    RETURN;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.MV_ASIENTOS')
      AND name = N'IX_MV_ASIENTOS_VisorComprobante'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_MV_ASIENTOS_VisorComprobante
        ON dbo.MV_ASIENTOS (TC, SUCURSAL, NUMERO, LETRA)
        INCLUDE ([NUMERO ASIENTO], FECHA, FechaHora_Grabacion, CUENTA,
                 DETALLE, [DEBE-HABER], IMPORTE, USUARIO_LOGEADO, SECUENCIA);
END;

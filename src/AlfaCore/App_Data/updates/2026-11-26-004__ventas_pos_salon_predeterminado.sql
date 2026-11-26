/*
    2026-11-26 · POS Salón: armado automático del salón.
    - TA_CONFIGURACION (grupo POS): cómo se arma el salón al crear un punto de venta en modo Salón
      (sector "Salón" con barra y mesas distribuidas en el plano) y la capacidad por defecto de las mesas
      que se agregan con "Cantidad de mesas". Solo se insertan si faltan.
    No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
BEGIN
    DECLARE @k TABLE (Clave nvarchar(100), Valor nvarchar(150));
    INSERT INTO @k (Clave, Valor) VALUES
        (N'POS-SALON-SECTOR-NOMBRE', N'Salón'),
        (N'POS-SALON-MESAS-INICIALES', N'5'),
        (N'POS-SALON-CAPACIDAD-MESA', N'4'),
        (N'POS-SALON-CREAR-BARRA', N'S'),
        (N'POS-SALON-CAPACIDAD-BARRA', N'6');

    INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, GRUPO, FechaHora_Grabacion, FechaHora_Modificacion)
    SELECT k.Clave, k.Valor, N'POS', GETDATE(), GETDATE()
    FROM @k k
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE UPPER(LTRIM(RTRIM(c.CLAVE))) = UPPER(k.Clave));
END;
GO

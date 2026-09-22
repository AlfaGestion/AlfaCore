/*
    Mercado Pago Point -- Fase 2 (ajuste): la clave MERCADOPAGO_CODIGO_MEDIO_PAGO vincula la
    integración con UN medio de pago existente de dbo.MA_CUENTAS (el Codigo que ya usa el selector de
    medios de pago del POS). No hay convención fija tipo "MPPOINT": se confirmó probando contra una
    base real que los clientes ya tienen sus propios códigos para Mercado Pago/QR (ej. "QR", "MP")
    clasificados contablemente como MedioDePago='EF' (efectivo-equivalente), no como un tipo aparte.
*/

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'MERCADOPAGO_CODIGO_MEDIO_PAGO')
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
    VALUES (N'MERCADOPAGO', N'MERCADOPAGO_CODIGO_MEDIO_PAGO', N'', N'Codigo de dbo.MA_CUENTAS del medio de pago que dispara el cobro con la terminal Point. Vacío = sin vincular todavía.', GETDATE());

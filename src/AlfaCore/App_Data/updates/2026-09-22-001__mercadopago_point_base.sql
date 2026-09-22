/*
    Mercado Pago Point (terminal física de tarjeta/QR) en el Punto de Venta web -- Fase 1.

    Portado desde C:\dev\AlfaMercadoPagoPoint (proyecto propio, Python + .NET Framework 4.8/COM para
    el sistema de escritorio VB6). Un solo terminal fijo por base en esta etapa -- sin selección de
    terminal en runtime.

    dbo.MP_POINT_ORDENES: correlación local con las "orders" de Mercado Pago. No se extiende
    PuntoVentaPaymentLineDto (solo tiene CodigoMedioPago/DescripcionMedioPago/Importe hoy, y lo usan
    varios flujos de cobro existentes) -- la trazabilidad del pago (order id, payment id, estado) vive
    acá aparte, enlazada a IdComprobante recién cuando la venta se termina de grabar.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.MP_POINT_ORDENES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MP_POINT_ORDENES
    (
        IdOrdenLocal            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MP_POINT_ORDENES PRIMARY KEY,
        IdOrderMp               nvarchar(50)  NOT NULL,
        ExternalReference       nvarchar(100) NOT NULL,
        IdComprobante           int NULL,
        Estado                  nvarchar(30)  NOT NULL,
        Importe                 money         NOT NULL,
        PaymentId               nvarchar(50)  NULL,
        MedioPago               nvarchar(30)  NULL,
        TipoTarjeta             nvarchar(30)  NULL,
        FechaCreacionUtc        datetime      NOT NULL CONSTRAINT DF_MP_POINT_ORDENES_FechaCreacion DEFAULT (GETUTCDATE()),
        FechaActualizacionUtc   datetime      NOT NULL CONSTRAINT DF_MP_POINT_ORDENES_FechaActualizacion DEFAULT (GETUTCDATE()),
        RawUltimaRespuesta      nvarchar(max) NULL
    );
    CREATE UNIQUE INDEX UQ_MP_POINT_ORDENES_IdOrderMp ON dbo.MP_POINT_ORDENES (IdOrderMp);
    CREATE INDEX IX_MP_POINT_ORDENES_ExternalReference ON dbo.MP_POINT_ORDENES (ExternalReference);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'MERCADOPAGO_ACCESS_TOKEN')
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
    VALUES (N'MERCADOPAGO', N'MERCADOPAGO_ACCESS_TOKEN', N'', N'Access token de la cuenta de Mercado Pago (Point). Vacío = módulo apagado.', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'MERCADOPAGO_TERMINAL_ID')
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
    VALUES (N'MERCADOPAGO', N'MERCADOPAGO_TERMINAL_ID', N'', N'ID de la terminal Point asociada a este punto de venta.', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'MERCADOPAGO_POS_EXTERNAL_ID')
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
    VALUES (N'MERCADOPAGO', N'MERCADOPAGO_POS_EXTERNAL_ID', N'', N'external_id del punto de venta (POS) en Mercado Pago, informativo.', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'MERCADOPAGO_WEBHOOK_SECRET')
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
    VALUES (N'MERCADOPAGO', N'MERCADOPAGO_WEBHOOK_SECRET', N'', N'Secreto para validar la firma HMAC del webhook de Mercado Pago (header x-signature).', GETDATE());

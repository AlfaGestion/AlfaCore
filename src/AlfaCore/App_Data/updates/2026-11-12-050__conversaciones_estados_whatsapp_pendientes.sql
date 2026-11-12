/*
    Conversaciones - Estados de WhatsApp (enviado/entregado/leído) que llegan antes que su mensaje.
    Con coexistencia, Meta manda por webhooks separados la copia del mensaje escrito en la app del celular
    (smb_message_echoes) y su estado. Si el estado se procesa primero no encuentra el mensaje y se perdía:
    el mensaje quedaba en "Enviado" con una sola tilde aunque el cliente ya lo hubiera leído.
    Ahora el estado queda acá hasta que se graba el mensaje con ese WhatsAppMessageId, y ahí se aplica.
    Lo que no encuentra mensaje en 7 días se borra solo. Idempotente.
*/
IF OBJECT_ID(N'dbo.CONV_MENSAJES_ESTADO_PENDIENTE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_MENSAJES_ESTADO_PENDIENTE
    (
        WhatsAppMessageId nvarchar(150) NOT NULL,
        EstadoEnvio nvarchar(30) NOT NULL,
        PayloadJson nvarchar(max) NULL,
        FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_MENSAJES_ESTADO_PENDIENTE_FHG DEFAULT (GETDATE()),
        CONSTRAINT PK_CONV_MENSAJES_ESTADO_PENDIENTE PRIMARY KEY CLUSTERED (WhatsAppMessageId)
    );
END;
GO

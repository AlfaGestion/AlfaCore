/*
    CONV_MENSAJES.MessageType: suma N'ORDER' (pedido de catálogo de WhatsApp).

    Desde 2026-09-14 ConversacionesService.NormalizeMessageType clasifica los pedidos de catálogo
    como 'ORDER', pero la restricción CK_CONV_MENSAJES_MessageType (2026-07-22-002) no lo incluía:
    el INSERT del webhook fallaba ("conflicto con la restricción CHECK") y el pedido no se guardaba.
    Visto en producción (DISTRIWALTERP, 2026-10). Meta reintenta los webhooks fallidos durante unos
    días, así que los pedidos más recientes pueden volver a llegar después de esta actualización.

    Se recrea con la lista completa que puede devolver NormalizeMessageType. WITH NOCHECK: no
    revalida filas viejas (si alguna base tuviera un valor fuera de la lista, el script no falla);
    las filas nuevas sí se controlan. Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL
    RETURN;
GO

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.CONV_MENSAJES')
      AND name = N'CK_CONV_MENSAJES_MessageType'
)
    ALTER TABLE dbo.CONV_MENSAJES DROP CONSTRAINT CK_CONV_MENSAJES_MessageType;
GO

ALTER TABLE dbo.CONV_MENSAJES WITH NOCHECK
    ADD CONSTRAINT CK_CONV_MENSAJES_MessageType
    CHECK
    (
        MessageType IN
        (
            N'TEXT',
            N'IMAGE',
            N'DOCUMENT',
            N'AUDIO',
            N'VIDEO',
            N'STICKER',
            N'LOCATION',
            N'CONTACT',
            N'REACTION',
            N'ORDER',
            N'SYSTEM',
            N'UNKNOWN'
        )
    );
GO

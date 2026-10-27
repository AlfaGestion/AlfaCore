/*
    CONV_BOT_PEDIDOS: borradores de pedidos que arma el asistente de Conversaciones (2026-10-27).

    El cliente identificado pide artículos por chat ("quiero 10 pilas AA"); el asistente arma un
    borrador con los artículos del catálogo web (mismos precios y controles que el carrito) y se lo
    muestra. Recién cuando el cliente confirma ("sí", "confirmo") se graba la Nota de Pedido con el
    mismo circuito del carrito web. Una fila PENDIENTE por conversación: un pedido nuevo reemplaza al
    anterior. Se activa en Configuración → Asistente IA → Herramientas → Tomar pedidos. Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.CONV_BOT_PEDIDOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_BOT_PEDIDOS
    (
        IdBorrador          int IDENTITY(1,1) NOT NULL,
        IdConversacion      bigint        NOT NULL,
        ClienteCodigo       nvarchar(30)  NOT NULL,
        IdInsertCatalogo    int           NOT NULL,
        LineasJson          nvarchar(4000) NOT NULL,
        Total               decimal(18,2) NOT NULL CONSTRAINT DF_CONV_BOT_PEDIDOS_Total DEFAULT (0),
        Estado              nvarchar(15)  NOT NULL CONSTRAINT DF_CONV_BOT_PEDIDOS_Estado DEFAULT (N'PENDIENTE'),
        FechaAlta           datetime      NOT NULL CONSTRAINT DF_CONV_BOT_PEDIDOS_FechaAlta DEFAULT (GETDATE()),
        FechaEstado         datetime      NULL,
        IdComprobanteTexto  nvarchar(40)  NULL,
        Error               nvarchar(500) NULL,
        CONSTRAINT PK_CONV_BOT_PEDIDOS PRIMARY KEY CLUSTERED (IdBorrador),
        CONSTRAINT CK_CONV_BOT_PEDIDOS_Estado CHECK (Estado IN (N'PENDIENTE', N'CONFIRMADO', N'CANCELADO', N'REEMPLAZADO', N'ERROR'))
    );

    CREATE INDEX IX_CONV_BOT_PEDIDOS_Conversacion ON dbo.CONV_BOT_PEDIDOS (IdConversacion, Estado);
END;

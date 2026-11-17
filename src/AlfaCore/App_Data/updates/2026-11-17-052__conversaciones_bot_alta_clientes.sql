/*
    Asistente de Conversaciones: alta de clientes desde el chat y dirección de entrega del pedido (2026-11-17).

    CONV_BOT_CLIENTES: borrador del alta que arma el asistente cuando escribe alguien que no es cliente
    (nombre, DNI y dirección pedidos por chat). Se graba el Cliente recién cuando el contacto confirma
    ("sí"); la IA nunca da de alta sola. Una fila PENDIENTE por conversación: un borrador nuevo reemplaza
    al anterior. Se activa en Configuración → Asistente IA → Herramientas → Dar de alta clientes.

    CONV_BOT_PEDIDOS.DireccionEntrega: dónde entregar el pedido armado por chat (por defecto la dirección
    del cliente); se copia a las observaciones de la Nota de Pedido al confirmar.

    Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.CONV_BOT_CLIENTES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_BOT_CLIENTES
    (
        IdBorrador          int IDENTITY(1,1) NOT NULL,
        IdConversacion      bigint         NOT NULL,
        Nombre              nvarchar(50)   NOT NULL,
        Documento           nvarchar(13)   NULL,
        Calle               nvarchar(50)   NOT NULL,
        Numero              nvarchar(6)    NULL,
        Localidad           nvarchar(50)   NOT NULL,
        Referencia          nvarchar(200)  NULL,
        Telefono            nvarchar(50)   NULL,
        Estado              nvarchar(15)   NOT NULL CONSTRAINT DF_CONV_BOT_CLIENTES_Estado DEFAULT (N'PENDIENTE'),
        FechaAlta           datetime       NOT NULL CONSTRAINT DF_CONV_BOT_CLIENTES_FechaAlta DEFAULT (GETDATE()),
        FechaEstado         datetime       NULL,
        ClienteCodigo       nvarchar(30)   NULL,
        Error               nvarchar(500)  NULL,
        CONSTRAINT PK_CONV_BOT_CLIENTES PRIMARY KEY CLUSTERED (IdBorrador),
        CONSTRAINT CK_CONV_BOT_CLIENTES_Estado CHECK (Estado IN (N'PENDIENTE', N'CONFIRMADO', N'CANCELADO', N'REEMPLAZADO', N'ERROR'))
    );

    CREATE INDEX IX_CONV_BOT_CLIENTES_Conversacion ON dbo.CONV_BOT_CLIENTES (IdConversacion, Estado);
END;

IF OBJECT_ID(N'dbo.CONV_BOT_PEDIDOS', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.CONV_BOT_PEDIDOS', 'DireccionEntrega') IS NULL
    ALTER TABLE dbo.CONV_BOT_PEDIDOS ADD DireccionEntrega nvarchar(200) NULL;

-- Etapa "pendiente de confirmar" del registro público (/registrarme), separada de dbo.Clientes/
-- dbo.users. Antes, un registro sin confirmar ya creaba un cliente oficial en ALFANET2007 (CUIT
-- ocupado) y filas en ALFA_CENTRAL.Clientes/users marcadas verified=0 — si nunca confirmaba el
-- email (bot, typo, abandono), quedaba basura permanente en ambos lados. Ahora nada de eso se
-- crea hasta que el usuario confirma el email: mientras tanto, los datos viven acá.
-- Ejecutar manualmente contra ALFA_CENTRAL — no se aplica solo.

IF OBJECT_ID(N'dbo.RegistroPublicoPendiente', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RegistroPublicoPendiente
    (
        VerifiedCode        nvarchar(64)   NOT NULL,
        Nombre              nvarchar(50)   NOT NULL,
        Telefono            nvarchar(50)   NOT NULL,
        Email               nvarchar(250)  NOT NULL,
        Password            nvarchar(100)  NOT NULL,
        Cuit                nvarchar(20)   NULL,
        Iva                 nvarchar(10)   NULL,
        -- Si sp_web_altaClienteAlfa ya se llamó para este pendiente pero el aprovisionamiento
        -- posterior falló, guarda el idCliente ya emitido para reintentar SIN pedir uno nuevo
        -- (evita acumular altas oficiales huérfanas en ALFANET2007 en cada reintento).
        IdClienteReservado  nvarchar(9)    NULL,
        CreatedUtc          datetime       NOT NULL CONSTRAINT DF_RegistroPublicoPendiente_CreatedUtc DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_RegistroPublicoPendiente PRIMARY KEY CLUSTERED (VerifiedCode ASC),
        CONSTRAINT UQ_RegistroPublicoPendiente_Email UNIQUE (Email)
    );
END;
GO

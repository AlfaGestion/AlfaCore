/*
Agrega un token propio por base para resolver el tenant de un webhook entrante
(WhatsApp/Instagram/Facebook/MercadoLibre) sin depender de la sesion del usuario logueado.

Motivo: los webhooks son anonimos (Meta/MercadoLibre llaman sin cookie de sesion), asi que
antes de este cambio la conexion SQL efectiva caia siempre al fallback
ConnectionStrings:AlfaGestion — es decir, los mensajes entrantes de TODOS los clientes
terminaban en la misma base por defecto. Con este token, la URL del webhook queda propia de
cada base (".../webhook/{WebhookToken}") y el token alcanza para resolver a que base
corresponde el request, tanto en la verificacion (GET) como en el mensaje entrante (POST).

Las rutas viejas sin token siguen funcionando igual que hoy (fallback a AlfaGestion), para no
romper la configuracion ya cargada en Meta para el cliente actual.

Ejecutar manualmente contra ALFA_CENTRAL (no forma parte de App_Data/updates, que se aplica
por cliente contra la base de cada uno).

Nota: el ALTER TABLE y el CREATE INDEX van en batches separados por GO. Si van en el mismo
batch, el compilador valida el CREATE INDEX contra el esquema ANTES de reconocer la columna
agregada por el ALTER TABLE anterior (aunque en tiempo de ejecucion ya se haya corrido), y
tira "Invalid column name 'WebhookToken'".
*/

SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.bases') AND name = N'WebhookToken'
    )
    BEGIN
        ALTER TABLE dbo.bases ADD WebhookToken nvarchar(64) NULL;
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
GO

BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UQ_bases_WebhookToken' AND object_id = OBJECT_ID(N'dbo.bases')
    )
    BEGIN
        CREATE UNIQUE INDEX UQ_bases_WebhookToken
            ON dbo.bases (WebhookToken)
            WHERE WebhookToken IS NOT NULL;
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
GO

/*
    Cotizaciones - registro de cada envío por email (a quién, cuándo, con qué cuenta) para
    mostrarlo en la pestaña Historial. Antes solo se guardaba COT_VERSION.FechaHoraEnvio (un único
    timestamp del PRIMER envío que sacó la versión de BORRADOR) -- no alcanzaba para saber a qué
    dirección se mandó, ni para reenvíos posteriores a la misma versión.

    TrackingToken/FechaHoraLectura/CantidadLecturas: soporte de "abierto (aproximado)" vía un píxel
    de 1x1 embebido en el email (endpoint público, ver Program.cs). OJO -- el dato NO es preciso:
    Apple Mail (muy usado, iPhone/iCloud) precarga las imágenes de TODOS los emails apenas llegan,
    los haya abierto o no el destinatario, así que a esos destinatarios siempre les figura "abierto"
    al toque. Por eso la UI lo etiqueta "Abierto (aprox.)", nunca "Leído" a secas.

    Idempotente.
*/

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.COT_EMAIL_ENVIO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.COT_EMAIL_ENVIO
    (
        IdEnvio bigint IDENTITY(1,1) NOT NULL,
        IdVersion bigint NOT NULL,
        Destinatario nvarchar(200) NOT NULL,
        FechaHoraEnvio datetime NOT NULL CONSTRAINT DF_COT_EMAIL_ENVIO_FHE DEFAULT (GETDATE()),
        UsuarioEnvio nvarchar(50) NULL,
        UsoCuentaFallback bit NOT NULL CONSTRAINT DF_COT_EMAIL_ENVIO_Fallback DEFAULT (0),
        TrackingToken nvarchar(64) NULL,
        FechaHoraLectura datetime NULL,
        UltimaFechaHoraLectura datetime NULL,
        CantidadLecturas int NOT NULL CONSTRAINT DF_COT_EMAIL_ENVIO_CantLecturas DEFAULT (0),
        CONSTRAINT PK_COT_EMAIL_ENVIO PRIMARY KEY CLUSTERED (IdEnvio)
    );

    CREATE NONCLUSTERED INDEX IX_COT_EMAIL_ENVIO_Version ON dbo.COT_EMAIL_ENVIO (IdVersion, FechaHoraEnvio DESC);
    CREATE UNIQUE NONCLUSTERED INDEX IX_COT_EMAIL_ENVIO_Token ON dbo.COT_EMAIL_ENVIO (TrackingToken) WHERE TrackingToken IS NOT NULL;
END;
GO

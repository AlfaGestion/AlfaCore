/*
Agrega la jerarquía de Meta Business/Portfolio a los números operativos de WhatsApp, y una caché local
del NOMBRE del portfolio (con seguimiento de reintentos), para que "Configuración -> WhatsApp
conectados" pueda mostrar "Portfolio: <nombre>" sin pegarle a Meta en cada render.

MetaBusinessId/WabaId en CONV_WHATSAPP_NUMEROS: sólo IDs técnicos, nunca se muestran al cliente
directamente. Se completan de dos formas, ninguna con una llamada a Meta nueva:
  1) Al importar el número desde Embedded Signup (WhatsAppEmbeddedOperationalImportService.
     CompleteForBaseAsync) -- discovery ya trae estos ids de todos modos.
  2) Backfill para números ya conectados ANTES de esta actualización: se reconstruyen desde el
     ownership central ya persistido (WhatsAppPhoneOwnership -> WhatsAppWabaOwnership en ALFA_CENTRAL),
     nunca desde Meta -- ver WhatsAppEmbeddedOperationalImportService.BackfillNumeroMetaIdentityAsync.
Números agregados manualmente por Phone Number ID (nunca pasaron por Embedded Signup) quedan con estas
columnas vacías para siempre: no tienen portfolio conocido, y la UI no debe insinuar que sí lo tienen.

CONV_WHATSAPP_BUSINESS_PORTFOLIOS: una fila por Business/Portfolio de Meta (no por número -- varios
números pueden compartir el mismo portfolio, así que el nombre se guarda una sola vez). El nombre se
completa de dos formas:
  1) Oportunista, gratis: cuando el discovery de Embedded Signup YA trae el nombre de todos modos
     (me/businesses?fields=id,name, sólo en el path que no conocía el WABA de antemano).
  2) Bajo demanda, throttled: para un MetaBusinessId conocido sin nombre cacheado (típicamente un
     número backfillado), WhatsAppPortfolioResolutionThrottle reserva atómicamente un intento (nunca
     más de uno por MetaBusinessId por ventana de tiempo, ver WhatsAppEmbeddedSignupOptions.
     PortfolioResolutionThrottle) antes de llamar a IMetaWhatsAppManagementClient.GetBusinessNameAsync
     con el WhatsAppRuntimeCredential ya usado para enviar mensajes -- nunca el Vault de onboarding.
Ninguna de las dos formas dispara una llamada por render ni una llamada repetida sin throttle.

Idempotente (IF OBJECT_ID/COL_LENGTH), reintentable sin romper datos.
*/

IF OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMEROS', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'MetaBusinessId') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD MetaBusinessId nvarchar(40) NULL;
END;
GO

IF OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMEROS', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.CONV_WHATSAPP_NUMEROS', 'WabaId') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_WHATSAPP_NUMEROS ADD WabaId nvarchar(40) NULL;
END;
GO

IF OBJECT_ID(N'dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS
    (
        MetaBusinessId          nvarchar(40)  NOT NULL,
        -- Vacío = "hubo al menos un intento de resolución, Meta todavía no dio un nombre" (throttle
        -- activo vía LastResolutionAttemptUtc) -- distinto de "nunca se intentó" (sin fila). Nunca NULL
        -- para no tener que distinguir NULL vs '' en el código que lee.
        PortfolioName           nvarchar(200) NOT NULL CONSTRAINT DF_CONV_WHATSAPP_BUSINESS_PORTFOLIOS_PortfolioName DEFAULT (N''),
        ModifiedAtUtc           datetime2     NOT NULL,
        -- Cuándo fue el último intento de resolver el nombre contra Meta (haya tenido éxito o no).
        -- Null = todavía no se intentó nunca. Usado por el throttle para no reintentar antes de la
        -- ventana configurada (ver WhatsAppPortfolioResolutionThrottle) -- nunca una llamada por render.
        LastResolutionAttemptUtc datetime2    NULL,
        CONSTRAINT PK_CONV_WHATSAPP_BUSINESS_PORTFOLIOS PRIMARY KEY CLUSTERED (MetaBusinessId)
    );
END;
GO

-- Reemisión-compatible: si la tabla ya existía de una versión previa de este mismo script (todavía no
-- aplicado a ninguna base real, pero por las dudas ante un reintento parcial), agrega la columna que
-- pudiera faltar sin recrear la tabla ni tocar filas existentes.
IF OBJECT_ID(N'dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS', 'LastResolutionAttemptUtc') IS NULL
BEGIN
    ALTER TABLE dbo.CONV_WHATSAPP_BUSINESS_PORTFOLIOS ADD LastResolutionAttemptUtc datetime2 NULL;
END;
GO

-- Números agregados MANUALMENTE por Phone Number ID no tienen ownership central (nunca pasaron por
-- Embedded Signup) -- pero SÍ suelen tener una WABA legacy conocida (ConversacionWhatsAppConfigDto.
-- BusinessAccountId, compartida por todos los números legacy de la base) con un AccessToken utilizable
-- de sólo lectura. Esta tabla cachea, con el mismo mecanismo de throttle que el nombre del portfolio,
-- qué Business es dueño de esa WABA -- para que "manual" no sea sinónimo de "Portfolio siempre
-- desconocido". Ver WhatsAppPortfolioResolutionService.TryResolveLegacyWabaOwningBusinessAsync.
IF OBJECT_ID(N'dbo.CONV_WHATSAPP_WABA_BUSINESS_MAP', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_WHATSAPP_WABA_BUSINESS_MAP
    (
        WabaId                   nvarchar(40)  NOT NULL,
        -- Vacío = intento hecho, todavía sin resultado (mismo criterio que PortfolioName en
        -- CONV_WHATSAPP_BUSINESS_PORTFOLIOS) -- nunca NULL.
        MetaBusinessId            nvarchar(40)  NOT NULL CONSTRAINT DF_CONV_WHATSAPP_WABA_BUSINESS_MAP_MetaBusinessId DEFAULT (N''),
        ModifiedAtUtc             datetime2     NOT NULL,
        LastResolutionAttemptUtc  datetime2     NULL,
        CONSTRAINT PK_CONV_WHATSAPP_WABA_BUSINESS_MAP PRIMARY KEY CLUSTERED (WabaId)
    );
END;
GO

/*
Decisiones de diseño explícitas (pedidas en la auditoría):

- SIN FK de CONV_WHATSAPP_NUMEROS.MetaBusinessId -> CONV_WHATSAPP_BUSINESS_PORTFOLIOS.MetaBusinessId.
  El número recibe su MetaBusinessId al importarse (o vía backfill posterior desde ownership central) --
  ANTES de que exista necesariamente una fila de portfolio para ese id (la fila de portfolio recién se
  crea cuando algo intenta resolver el nombre). Es una caché best-effort/eventualmente consistente, no
  una relación de integridad -- igual criterio que WhatsAppWabaOwnership.MetaBusinessId (tampoco tiene FK
  a nada) en el central.

- SIN índices adicionales más allá de la PK en ninguna de las dos tablas nuevas. Todo el acceso es por
  MetaBusinessId o WabaId (punto o IN (...)), cubierto por la PK clustered de cada una. No hay ningún
  escaneo por las demás columnas en el código -- agregar un índice sin un query real que lo use sería
  overhead sin beneficio. Mismo razonamiento de "sin FK" para CONV_WHATSAPP_WABA_BUSINESS_MAP: es una
  caché best-effort, no una relación de integridad.

- SQL Server 2016 SP1, compatibility_level 100: todo lo de este script (CREATE TABLE, ALTER TABLE ADD,
  MERGE, IF OBJECT_ID/COL_LENGTH, datetime2) es sintaxis disponible desde SQL Server 2008 -- ninguna
  característica de este script requiere compat level > 100 (a diferencia de, por ejemplo,
  OFFSET/FETCH o STRING_SPLIT, que si se usaran romperían acá). datetime2 es un tipo de dato del motor
  2016, no una característica de superficie T-SQL gobernada por compatibility_level.
*/

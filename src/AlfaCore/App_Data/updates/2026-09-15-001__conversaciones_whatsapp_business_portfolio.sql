/*
Agrega la jerarquía de Meta Business/Portfolio a los números operativos de WhatsApp, y una caché local
(sin expiración -- se sobrescribe cada vez que se resuelve) del NOMBRE del portfolio, para que
"Configuración -> WhatsApp conectados" pueda mostrar "Portfolio: <nombre>" sin pegarle a Meta en cada
render.

MetaBusinessId/WabaId en CONV_WHATSAPP_NUMEROS: sólo IDs técnicos, nunca se muestran al cliente
directamente -- se completan al importar el número desde Embedded Signup (ver
WhatsAppEmbeddedOperationalImportService.CompleteForBaseAsync). Números agregados manualmente por
Phone Number ID quedan con estas columnas vacías: no tienen portfolio conocido, y la UI no debe
insinuar que sí lo tienen.

CONV_WHATSAPP_BUSINESS_PORTFOLIOS: una fila por Business/Portfolio de Meta (no por número -- varios
números pueden compartir el mismo portfolio, así que el nombre se guarda una sola vez). Se completa de
forma oportunista con el nombre que Meta ya devuelve durante el discovery de Embedded Signup
(me/businesses?fields=id,name) -- nunca dispara una llamada nueva sólo para esto.

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
        MetaBusinessId nvarchar(40)  NOT NULL,
        PortfolioName  nvarchar(200) NOT NULL,
        ModifiedAtUtc  datetime2     NOT NULL,
        CONSTRAINT PK_CONV_WHATSAPP_BUSINESS_PORTFOLIOS PRIMARY KEY CLUSTERED (MetaBusinessId)
    );
END;
GO

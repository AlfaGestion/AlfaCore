/*
    Facturación electrónica AFIP/ARCA (CAE real) -- Fase 1: infraestructura base.
    Solo agrega estructura nueva y claves de configuración; no toca TA_MENU ni modifica ninguna fila
    existente de V_MV_CPTE_ELECTRONICOS. Idempotente.

    ARCA_WSAA_TICKET: cache del Ticket de Acceso WSAA (token/sign) por CUIT+ambiente, compartible entre
    workers de IIS/Kestrel -- evita reautenticar contra AFIP en cada request.

    Claves nuevas en TA_CONFIGURACION:
      - ARCA_AMBIENTE ('HOMOLOGACION'|'PRODUCCION'): default HOMOLOGACION, nunca se asume producción.
      - ARCA_MODO_FALLO_CAE ('ESTRICTO'|'DEGRADADO'): default ESTRICTO (corta la venta antes de la
        cobranza si AFIP rechaza o no responde).
      - ARCA_RUTA_CRT / ARCA_RUTA_KEY: certificado/clave privada globales (fallback cuando la unidad de
        negocio no tiene los propios en V_TA_UnidadNegocio.RUTA_CRT/RUTA_KEY). Vacías por default, a
        completar por el usuario antes de habilitar.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.V_MV_CPTE_ELECTRONICOS', N'U') IS NULL OR OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.ARCA_WSAA_TICKET', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ARCA_WSAA_TICKET
    (
        Cuit                nvarchar(11)  NOT NULL,
        Ambiente            nvarchar(20)  NOT NULL,
        Token               nvarchar(max) NOT NULL,
        Sign                nvarchar(max) NOT NULL,
        FechaGeneracionUtc  datetime      NOT NULL,
        FechaExpiracionUtc  datetime      NOT NULL,
        CONSTRAINT PK_ARCA_WSAA_TICKET PRIMARY KEY CLUSTERED (Cuit, Ambiente)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'ARCA_AMBIENTE')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES (N'ARCA', N'ARCA_AMBIENTE', N'HOMOLOGACION', N'Ambiente AFIP/ARCA (HOMOLOGACION o PRODUCCION)', GETDATE(), GETDATE());
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'ARCA_MODO_FALLO_CAE')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES (N'ARCA', N'ARCA_MODO_FALLO_CAE', N'ESTRICTO', N'Modo de fallo CAE (ESTRICTO o DEGRADADO)', GETDATE(), GETDATE());
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'ARCA_RUTA_CRT')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES (N'ARCA', N'ARCA_RUTA_CRT', N'', N'Ruta certificado AFIP (.crt) global', GETDATE(), GETDATE());
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'ARCA_RUTA_KEY')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES (N'ARCA', N'ARCA_RUTA_KEY', N'', N'Ruta clave privada AFIP (.key) global', GETDATE(), GETDATE());
END;
GO

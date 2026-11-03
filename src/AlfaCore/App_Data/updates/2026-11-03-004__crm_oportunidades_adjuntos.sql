/*
    CRM - Adjuntos de las oportunidades (presupuestos, listas de precios, fotos, planos...).
    El archivo puede estar:
      - en SQL (ArchivoContenido): lo que se sube desde la pantalla; es chico y queda con el backup de la base.
      - en disco (RutaLocal, relativa a la instalación): lo que se trae de otro sistema en volumen
        (por ejemplo, la migración de Odoo). Por seguridad, la ruta tiene que quedar dentro de la carpeta
        de adjuntos de la base, la misma que usa Conversaciones.
    Las bajas son lógicas (Baja = 1).
    Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CRM_OPORTUNIDADES', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.CRM_OPORTUNIDAD_ADJUNTOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CRM_OPORTUNIDAD_ADJUNTOS
    (
        IdAdjunto bigint IDENTITY(1,1) NOT NULL,
        IdOportunidad bigint NOT NULL,
        NombreArchivo nvarchar(255) NOT NULL,
        MimeType nvarchar(100) NULL,
        TamanoBytes bigint NULL,
        RutaLocal nvarchar(500) NULL,
        ArchivoContenido varbinary(max) NULL,
        ArchivoHashSha256 nvarchar(64) NULL,
        Origen nvarchar(20) NOT NULL CONSTRAINT DF_CRM_OPORTUNIDAD_ADJUNTOS_Origen DEFAULT (N'MANUAL'),
        UsuarioAlta nvarchar(80) NULL,
        FechaHoraAlta datetime NOT NULL CONSTRAINT DF_CRM_OPORTUNIDAD_ADJUNTOS_FHA DEFAULT (GETDATE()),
        Baja bit NOT NULL CONSTRAINT DF_CRM_OPORTUNIDAD_ADJUNTOS_Baja DEFAULT (0),
        UsuarioBaja nvarchar(80) NULL,
        FechaHoraBaja datetime NULL,
        CONSTRAINT PK_CRM_OPORTUNIDAD_ADJUNTOS PRIMARY KEY CLUSTERED (IdAdjunto),
        CONSTRAINT FK_CRM_OPORTUNIDAD_ADJUNTOS_OP FOREIGN KEY (IdOportunidad) REFERENCES dbo.CRM_OPORTUNIDADES (IdOportunidad)
    );
END;
GO

-- La ficha lista los adjuntos activos de una oportunidad, del más nuevo al más viejo.
IF OBJECT_ID(N'dbo.CRM_OPORTUNIDAD_ADJUNTOS', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CRM_OPORTUNIDAD_ADJUNTOS') AND name = N'IX_CRM_OPORTUNIDAD_ADJUNTOS_OP')
    CREATE NONCLUSTERED INDEX IX_CRM_OPORTUNIDAD_ADJUNTOS_OP
        ON dbo.CRM_OPORTUNIDAD_ADJUNTOS (IdOportunidad, Baja, FechaHoraAlta DESC)
        INCLUDE (NombreArchivo, MimeType, TamanoBytes, Origen, UsuarioAlta);
GO

-- Tamaño máximo (en MB) de un archivo subido a una oportunidad. Solo se agrega si falta.
IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'CRM-ADJUNTOS-MAX-MB')
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
    VALUES (N'CRM', N'CRM-ADJUNTOS-MAX-MB', N'25', N'Tamaño máximo en MB de cada archivo adjunto a una oportunidad del CRM.', GETDATE());
GO

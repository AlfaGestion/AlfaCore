/*
    Facturación electrónica AFIP/ARCA -- almacenamiento del certificado/clave privada como blob en la
    base (mismo criterio que dbo.TA_LOGOS para el logo de la empresa), en vez de una ruta de archivo en
    el servidor. Motivo: las rutas ya cargadas en V_TA_UnidadNegocio.RUTA_CRT/RUTA_KEY son rutas de las
    máquinas de escritorio (ej. "C:\Certificados\..."), que no necesariamente existen en el servidor
    donde corre AlfaCore -- subir el archivo desde la pantalla web y guardarlo en la base funciona sin
    importar dónde esté instalado AlfaCore.

    Reemplaza el uso de ARCA_RUTA_CRT/ARCA_RUTA_KEY (TA_CONFIGURACION) y de
    V_TA_UnidadNegocio.RUTA_CRT/RUTA_KEY como fuente del certificado -- esas columnas/claves quedan sin
    tocar (por si algún otro proceso las lee) pero ArcaConfigService ya no las usa para resolver el
    certificado, solo esta tabla nueva.

    Clave 'GLOBAL' = certificado de fallback general; cualquier otro valor = código de una unidad de
    negocio (V_TA_UnidadNegocio.Codigo, sin espacios de relleno). Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ARCA_CERTIFICADO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ARCA_CERTIFICADO
    (
        UNegocio               nvarchar(10)   NOT NULL,
        ArchivoCrt             varbinary(max) NULL,
        NombreArchivoCrt       nvarchar(260)  NULL,
        ArchivoKey             varbinary(max) NULL,
        NombreArchivoKey       nvarchar(260)  NULL,
        FechaHoraModificacion  datetime       NULL,
        CONSTRAINT PK_ARCA_CERTIFICADO PRIMARY KEY CLUSTERED (UNegocio)
    );
END;
GO

/*
Adjuntos durables para Conversaciones.

Objetivo:
- conservar una copia binaria propia en SQL Server
- evitar pérdidas por cambios de carpeta local/publicada
- mantener RutaLocal como cache de alto rendimiento
*/

IF OBJECT_ID(N'dbo.CONV_ADJUNTOS', N'U') IS NULL
    RETURN;

IF COL_LENGTH(N'dbo.CONV_ADJUNTOS', N'ArchivoContenido') IS NULL
    ALTER TABLE dbo.CONV_ADJUNTOS ADD ArchivoContenido varbinary(max) NULL;

IF COL_LENGTH(N'dbo.CONV_ADJUNTOS', N'ArchivoHashSha256') IS NULL
    ALTER TABLE dbo.CONV_ADJUNTOS ADD ArchivoHashSha256 nvarchar(64) NULL;

IF COL_LENGTH(N'dbo.CONV_ADJUNTOS', N'AlmacenamientoEstado') IS NULL
    ALTER TABLE dbo.CONV_ADJUNTOS ADD AlmacenamientoEstado nvarchar(20) NULL;

IF COL_LENGTH(N'dbo.CONV_ADJUNTOS', N'FechaHora_Archivo') IS NULL
    ALTER TABLE dbo.CONV_ADJUNTOS ADD FechaHora_Archivo datetime NULL;
GO

UPDATE dbo.CONV_ADJUNTOS
SET AlmacenamientoEstado = N'RUTA_LOCAL'
WHERE AlmacenamientoEstado IS NULL
  AND NULLIF(LTRIM(RTRIM(ISNULL(RutaLocal, N''))), N'') IS NOT NULL;


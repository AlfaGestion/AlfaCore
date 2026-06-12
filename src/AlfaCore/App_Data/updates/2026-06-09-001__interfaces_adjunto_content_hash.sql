/*
Agrega la columna ContentHash a INT_COMPROBANTE_RECIBIDO_ADJUNTO.

Objetivo:
- almacenar el hash SHA-256 (hex, 64 chars) del contenido binario de cada adjunto
- permitir detectar adjuntos identicos subidos a diferentes documentos
- los registros existentes quedan con NULL y se hashean al subir nuevos archivos
*/

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO')
         AND name = N'ContentHash'
   )
BEGIN
    ALTER TABLE dbo.INT_COMPROBANTE_RECIBIDO_ADJUNTO
        ADD ContentHash nvarchar(64) NULL;
END;
GO

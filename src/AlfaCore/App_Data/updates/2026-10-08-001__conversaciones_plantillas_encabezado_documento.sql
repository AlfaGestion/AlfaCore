/*
   Plantillas WhatsApp con encabezado DOCUMENT (ej. Cierre de caja con el Excel adjunto).
   EncabezadoFormato: NULL/'TEXT' = encabezado de texto (comportamiento previo), 'DOCUMENT' = el
   archivo se elige al enviar. ConversacionesService también la agrega en caliente si falta.
   Aditiva e idempotente: no modifica filas existentes.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS', N'U') IS NULL
    RETURN;

IF COL_LENGTH(N'dbo.CONV_PLANTILLAS', N'EncabezadoFormato') IS NULL
    ALTER TABLE dbo.CONV_PLANTILLAS ADD EncabezadoFormato varchar(20) NULL;

/*
    Asocia las plantillas locales de capacitaciones al WABA activo.
    La pantalla de Plantillas filtra por WabaId cuando hay un WhatsApp seleccionado.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMEROS', N'U') IS NULL
    RETURN;

DECLARE @WabaId nvarchar(40);

SELECT TOP (1) @WabaId = NULLIF(LTRIM(RTRIM(WabaId)), N'')
FROM dbo.CONV_WHATSAPP_NUMEROS
WHERE ISNULL(Activo, 0) = 1
  AND NULLIF(LTRIM(RTRIM(WabaId)), N'') IS NOT NULL
ORDER BY IdNumero;

IF @WabaId IS NULL
    RETURN;

UPDATE dbo.CONV_PLANTILLAS
SET WabaId = @WabaId,
    FechaHora_Modificacion = GETDATE()
WHERE NombreMeta IN (N'capacitacion_reserva_organizador', N'capacitacion_reserva_cliente')
  AND Idioma = N'es_AR'
  AND NULLIF(LTRIM(RTRIM(ISNULL(WabaId, N''))), N'') IS NULL;

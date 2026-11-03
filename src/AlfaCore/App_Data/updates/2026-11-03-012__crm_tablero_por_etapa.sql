/*
    CRM - Tablero: cuántas oportunidades se muestran por columna antes del botón "Cargar más".
    Solo agrega la clave si falta (no pisa un valor ya ajustado).
*/
IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'CRM-TABLERO-POR-ETAPA')
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
    VALUES (N'CRM', N'CRM-TABLERO-POR-ETAPA', N'80', N'Oportunidades por columna del tablero CRM; el resto se trae con "Cargar más".', GETDATE());
GO

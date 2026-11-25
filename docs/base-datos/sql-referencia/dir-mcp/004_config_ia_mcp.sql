-- MCP de consulta en lenguaje natural - Etapa 2 / script 4: claves IA_MCP_* en TA_CONFIGURACION.
-- Idempotente: inserta solo las claves que faltan; NUNCA pisa un valor existente ni borra nada.
-- No hay claves de conexion ni contrasenas aca: la cadena de conexion va por variable de entorno / user-secrets.
SET NOCOUNT ON;
GO
DECLARE @k TABLE (Clave nvarchar(100) PRIMARY KEY, Valor nvarchar(300), Descripcion nvarchar(500));
INSERT INTO @k (Clave, Valor, Descripcion) VALUES
 (N'IA_MCP_HABILITADO',                N'SI',      N'Interruptor general del servidor MCP. NO = rechaza todas las consultas.'),
 (N'IA_MCP_MAX_FILAS',                 N'500',     N'Tope duro de filas que devuelve cualquier herramienta (incluida consultar_sql).'),
 (N'IA_MCP_TOP_MAXIMO',                N'100',     N'Maximo permitido para el parametro top de los rankings.'),
 (N'IA_MCP_TIMEOUT_SEG',               N'60',      N'Segundos maximos por consulta SQL. cuenta corriente y stock tardan 10-15 s en bases grandes.'),
 (N'IA_MCP_TC_PROFORMA',               N'FP,NCFP', N'Tipos de comprobante proforma (afectan cuenta corriente pero no el libro de IVA), separados por coma. Los usa dir.vw_Ventas.'),
 (N'IA_MCP_INCLUIR_PROFORMAS',         N'SI',      N'SI = las herramientas de ventas incluyen proformas (son ventas reales no fiscales). NO = solo comprobantes del libro de IVA.'),
 (N'IA_MCP_CONSULTAR_SQL_HABILITADO',  N'SI',      N'SI = permite la herramienta consultar_sql (solo SELECT sobre el esquema dir).'),
 (N'IA_MCP_LOG_PARAMETROS',            N'SI',      N'SI = IA_LOG_CONSULTAS guarda los parametros de cada llamada. NO = solo herramienta, duracion y filas.'),
 (N'IA_MCP_ADVERTIR_SIN_COSTO_PCT',    N'5',       N'Si el porcentaje de lineas sin costo supera este valor, las herramientas de margen agregan una advertencia.');

INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
SELECT N'IA_MCP', k.Clave, k.Valor, k.Descripcion, GETDATE()
FROM @k k
WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE c.CLAVE = k.Clave);

SELECT CLAVE, VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE LIKE N'IA[_]MCP[_]%' ORDER BY CLAVE;
GO
